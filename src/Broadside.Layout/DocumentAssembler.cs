using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Layout.Flow;

/// <summary>
/// Turns what a document builder run described into a <see cref="FlowDocument"/>: resolves every element's partial styles from the theme
/// down through its scopes into the engine's <see cref="ParagraphStyle"/> and <see cref="TextStyle"/> (each distinct resolution once),
/// embeds each font face and image once, and writes the document-level entries. The builder lays out nothing itself.
/// </summary>
internal sealed partial class DocumentAssembler
{
    private static readonly PdfColor Black = PdfColor.FromGray(0);
    private static readonly string[] Headings = ["H1", "H2", "H3", "H4", "H5", "H6"];

    private readonly BuildContext _context;
    private readonly Theme _theme;
    private readonly PdfDocument _document;
    private readonly Dictionary<FaceKey, PdfCanvasFont> _fonts = [];
    private readonly Dictionary<ImageSource, PdfImage> _images = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ImageSource, PdfFormXObject> _svgs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MergeKey, Style> _merged = [];
    private readonly Dictionary<TextKey, TextStyle> _textStyles = [];
    private readonly Dictionary<ParagraphKey, ParagraphStyle> _paragraphStyles = [];
    private readonly Style _root;
    private readonly Style[] _headings = new Style[6];
    private readonly Style _caption;
    private readonly Style _cell;
    private readonly Style _headerCell;
    private readonly Style _link;
    private int _lastHeading;
    private double _contentWidth;

    public DocumentAssembler(BuildContext context)
    {
        _context = context;
        _theme = context.Theme;
        _document = context.Document;
        _root = new Style
        {
            FontFamily = _theme.BodyFont,
            FontSize = _theme.BaseSize,
            LineHeight = _theme.LineHeight,
            Color = _theme.Text,
            Bold = false,
            Italic = false,
            Underline = false,
            StrikeThrough = false,
            Alignment = TextAlignment.Left,
            Language = context.Language,
        }.Merge(_theme.Body);
        for (int level = 1; level <= 6; level++)
        {
            Style heading = new Style { FontFamily = _theme.HeadingFont, Bold = true, FontSize = _theme.BaseSize * HeadingStyles.ScaleOf(level), SpaceBefore = 0, SpaceAfter = 0 }
                .Merge(_theme.Headings[level]);
            _headings[level - 1] = heading;
        }

        _caption = new Style { SpaceBefore = 0, SpaceAfter = 0 }.Merge(_theme.Caption);
        _cell = new Style { SpaceBefore = 0, SpaceAfter = 0 };
        _headerCell = _cell.Merge(_theme.TableHeader);
        _link = _theme.Link.Color is null ? _theme.Link with { Color = _theme.Accent } : _theme.Link;
    }

    /// <summary>Builds the flow: one section per described section (a section on the theme's page when none was described).</summary>
    public FlowDocument Assemble()
    {
        IReadOnlyList<SectionNode> sections = _context.Sections.Count == 0 ? [new SectionNode()] : _context.Sections;
        FlowDocument? flow = null;
        foreach (SectionNode section in sections)
        {
            PageTemplate template = TemplateOf(section);
            if (flow is null)
            {
                flow = new FlowDocument(template);
            }
            else
            {
                flow.StartSection(template);
            }

            _contentWidth = template.Setup.ContentWidth;
            Style scope = section.Style is { } style ? Merge(Style.Empty, style.Inherited) : Style.Empty;
            AddBlocks(flow.Add, section.Content, scope, _context.Language);
        }

        flow!.OutlineDepth = _context.OutlineDepth;
        return flow;
    }

    private PageTemplate TemplateOf(SectionNode section)
    {
        PageSetup setup = _theme.Page;
        if (section.Size is { } size)
        {
            setup = setup.WithSize(size.Width.ToPoints(), size.Height.ToPoints());
        }

        if (section.Landscape is { } landscape)
        {
            setup = setup.WithOrientation(landscape ? PageOrientation.Landscape : PageOrientation.Portrait);
        }

        if (section.Margins is { } margins)
        {
            setup = setup.WithMargins(margins);
        }

        setup.Validate("section");
        Style regionStyle = Merge(Merge(_root, new Style { Color = _theme.Muted }.Merge(_theme.HeaderFooter)), section.Style?.Inherited);
        Watermark? watermark = null;
        if (section.Watermark is { } text)
        {
            Style full = Merge(Merge(_root, new Style { FontFamily = _theme.HeadingFont, Bold = true, FontSize = 60, Color = _theme.Muted }), section.WatermarkStyle);
            watermark = new Watermark(text, TextStyleOf(full, full.Color));
        }

        return new PageTemplate(setup)
        {
            Decoration = section.Decoration,
            BackgroundColor = section.BackgroundColor,
            BackgroundImage = section.BackgroundImage is { } background ? Embed(background) : null,
            Watermark = watermark,
            Header = Region(section.Header, regionStyle),
            Footer = Region(section.Footer, regionStyle),
            FirstPageHeader = Region(section.FirstPageHeader, regionStyle),
            FirstPageFooter = Region(section.FirstPageFooter, regionStyle),
            EvenPageHeader = Region(section.EvenPageHeader, regionStyle),
            EvenPageFooter = Region(section.EvenPageFooter, regionStyle),
        };
    }

    private HeaderFooter? Region(HeaderFooterNode? region, Style inherited)
    {
        if (region is null)
        {
            return null;
        }

        if (region.Left is null && region.Center is null && region.Right is null)
        {
            return HeaderFooter.None;
        }

        Style full = Merge(inherited, region.Style);
        return new HeaderFooter(TextStyleOf(full, full.Color))
        {
            Left = region.Left?.Encoded,
            Center = region.Center?.Encoded,
            Right = region.Right?.Encoded,
            EscapedFields = true,
            Offset = region.Offset?.ToPoints() ?? 36,
        };
    }

    /// <summary>Adds the blocks of <paramref name="container"/> through <paramref name="add"/>, resolving their styles under <paramref name="scope"/>.</summary>
    private void AddBlocks(Func<FlowBlock, object> add, ContainerNode container, Style scope, string? language)
    {
        foreach (BlockNode node in container.Blocks)
        {
            switch (node)
            {
                case ParagraphNode paragraph:
                    add(Kept(paragraph, BuildParagraph(paragraph, scope, language)));
                    break;
                case ListNode list:
                    add(Kept(list, BuildList(list, scope, language)));
                    break;
                case TableNode table:
                    // A caption stays with its table, and an image with its caption (§14.8.4.4, Table 368).
                    if (table.Caption is { } tableCaption)
                    {
                        add(new BlockGroup(Caption(tableCaption, scope, language)) { KeepWithNext = true });
                    }

                    add(Kept(table, BuildTable(table, scope, language)));
                    break;
                case ImageNode image when image.Source.IsSvg:
                    // An SVG source drawn through Image or DecorativeImage is a vector drawing of the same size and tagging (issue #643).
                    var svgDrawing = new DrawingNode
                    {
                        Width = Length.Points(1),
                        Height = Length.Points(1),
                        Draw = static (_, _) => { },
                        AlternateText = image.AlternateText,
                        Svg = image.Source,
                        SvgWidth = image.Width,
                        SvgHeight = image.Height,
                        FillWidth = image.FillWidth,
                        Alignment = image.Alignment,
                        Caption = image.Caption,
                        Local = image.Local,
                        Id = image.Id,
                        KeepWithNext = image.KeepWithNext,
                        KeepTogether = image.KeepTogether,
                    };
                    FlowBlock builtSvg = BuildDrawing(svgDrawing, scope, language);
                    add(Kept(image, image.Caption is { } svgCaption ? new BlockGroup(builtSvg, Caption(svgCaption, scope, language)) { KeepTogether = true } : builtSvg));
                    break;
                case ImageNode image:
                    FlowBlock builtImage = BuildImage(image, scope, language);
                    add(Kept(image, image.Caption is { } imageCaption ? new BlockGroup(builtImage, Caption(imageCaption, scope, language)) { KeepTogether = true } : builtImage));
                    break;
                case DrawingNode drawing:
                    FlowBlock builtDrawing = BuildDrawing(drawing, scope, language);
                    add(Kept(drawing, drawing.Caption is { } drawingCaption ? new BlockGroup(builtDrawing, Caption(drawingCaption, scope, language)) { KeepTogether = true } : builtDrawing));
                    break;
                case ColumnsNode columns:
                    add(Kept(columns, BuildColumns(columns, scope, language)));
                    break;
                case TocNode toc:
                    AddTableOfContents(add, toc, scope, language);
                    break;
                case FieldNode field:
                    add(Kept(field, BuildField(field, scope)));
                    break;
                case GroupNode group:
                    add(new BlockGroup(Blocks(group.Content, Merge(scope, group.Scope?.Inherited), language))
                    {
                        KeepTogether = group.KeepTogether,
                        KeepWithNext = group.KeepWithNext,
                        IsTagged = group.Role is not null,
                        StructureType = group.Role ?? "Div",
                    });
                    break;
                case SpacerNode spacer:
                    add(new Spacer(spacer.Height.ToPoints()));
                    break;
                case PageBreakNode:
                    add(new PageBreak());
                    break;
                case RuleNode rule:
                    add(new Rule
                    {
                        Thickness = rule.Thickness?.ToPoints() ?? 0.5,
                        Color = rule.Color ?? _theme.Border,
                        SpaceBefore = rule.Local?.SpaceBefore?.ToPoints() ?? 6,
                        SpaceAfter = rule.Local?.SpaceAfter?.ToPoints() ?? 6,
                    });
                    break;
                case RawNode raw:
                    add(raw.Block);
                    break;
            }
        }
    }

    /// <summary>
    /// <paramref name="block"/> with its element's keep options: wrapped in an untagged group that keeps it on one page, or on the page of
    /// the next block (a group adds no structure element).
    /// </summary>
    private static FlowBlock Kept(BlockNode node, FlowBlock block) =>
        node.KeepTogether || node.KeepWithNext
            ? block is BlockGroup { IsTagged: false } group && !group.KeepWithNext
                ? new BlockGroup(group.Blocks) { KeepTogether = node.KeepTogether || group.KeepTogether, KeepWithNext = node.KeepWithNext }
                : new BlockGroup(block) { KeepTogether = node.KeepTogether, KeepWithNext = node.KeepWithNext }
            : block;

    private List<FlowBlock> Blocks(ContainerNode container, Style scope, string? language)
    {
        var blocks = new List<FlowBlock>(container.Blocks.Count);
        AddBlocks(block =>
        {
            blocks.Add(block);
            return blocks;
        }, container, scope, language);
        return blocks;
    }

    private Paragraph BuildParagraph(ParagraphNode node, Style scope, string? language)
    {
        Style? type = null;
        if (node.Level > 0)
        {
            type = _headings[node.Level - 1];
            if (node.Level > _lastHeading + 1)
            {
                _context.Report(
                    LayoutDiagnostics.HeadingLevelSkipped,
                    $"The heading \"{Shorten(node.Text)}\" is level {node.Level} after {(_lastHeading == 0 ? "no heading" : $"a level {_lastHeading} heading")}; heading levels should not be skipped (ISO 32000-2 §14.8.4.3.6).");
            }

            _lastHeading = node.Level;
        }

        Style full = Resolve(type, scope, node.Local);
        return Paragraph(node.Text, node.Spans, full, node.Level > 0 ? Headings[node.Level - 1] : "P", language, node.Id);
    }

    private Paragraph Caption(string text, Style scope, string? language) => Paragraph(text, null, Resolve(_caption, scope, null), "Caption", language);

    /// <summary>
    /// A paragraph of <paramref name="text"/> or <paramref name="spans"/> in the resolved style <paramref name="full"/>, tagged
    /// <paramref name="structureType"/>, with its language written only where it differs from <paramref name="parentLanguage"/>.
    /// </summary>
    private Paragraph Paragraph(string? text, List<SpanNode>? spans, Style full, string structureType, string? parentLanguage, string? id = null)
    {
        ParagraphStyle style = ParagraphStyleOf(full);
        string? language = string.Equals(full.Language, parentLanguage, StringComparison.OrdinalIgnoreCase) ? null : full.Language;
        if (text is not null)
        {
            List<InlineElement>? runs = AddRuns(null, text, full, NeedsStyle(full) ? TextStyleOf(full, full.Color) : null, style, null, null, null);
            return runs is null
                ? new Paragraph(text, style) { StructureType = structureType, Language = language, Id = id }
                : new Paragraph(runs, style) { StructureType = structureType, Language = language, Id = id };
        }

        var content = new List<InlineElement>(spans!.Count);
        foreach (SpanNode span in spans)
        {
            Style spanFull = span.Link is null && span.Destination is null ? Merge(full, span.Local) : Merge(Merge(full, _link), span.Local);
            string? spanLanguage = string.Equals(spanFull.Language, full.Language, StringComparison.OrdinalIgnoreCase) ? null : spanFull.Language;
            TextStyle? spanStyle = SameText(spanFull, full) && !NeedsStyle(full) ? null : TextStyleOf(spanFull, Visible(spanFull.Color));
            if (span.LineBreak)
            {
                content.Add(new TextRun(span.Text));
                continue;
            }

            if (span.PageOf is { } target)
            {
                content.Add(spanStyle is null ? new PageReference(target) : new PageReference(target, spanStyle));
                continue;
            }

            _ = AddRuns(content, span.Text, spanFull, spanStyle, style, span.Link, span.Destination, spanLanguage);
        }

        return new Paragraph(content, style) { StructureType = structureType, Language = language, Id = id };
    }

    /// <summary>
    /// Adds the runs of <paramref name="text"/> to <paramref name="runs"/> (created on demand) and returns the list: one run when the face encodes everything,
    /// else one per stretch of a face from the family's fallbacks, with characters no face encodes replaced and reported. Leaves
    /// <paramref name="runs"/> null for plain text that needs no run of its own.
    /// </summary>
    private List<InlineElement>? AddRuns(List<InlineElement>? runs, string text, Style full, TextStyle? style, ParagraphStyle paragraph, Uri? link, string? destination, string? language)
    {
        PdfCanvasFont primary = (style ?? paragraph.TextStyle).Font;
        FontFamily family = full.FontFamily!;
        if (Encodes(primary, text))
        {
            if (runs is not null || style is not null || link is not null || destination is not null || language is not null)
            {
                runs ??= [];
                runs.Add(Run(text, style, link, destination, language));
            }

            return runs;
        }

        runs ??= [];
        FontFace face = FaceOf(full);
        TextStyle baseStyle = style ?? paragraph.TextStyle;
        var segment = new StringBuilder();
        PdfCanvasFont? current = null;
        Span<char> buffer = stackalloc char[2];
        foreach (Rune rune in text.EnumerateRunes())
        {
            int length = rune.EncodeToUtf16(buffer);
            ReadOnlySpan<char> character = buffer[..length];
            PdfCanvasFont font = Skipped(rune) ? current ?? primary : FontFor(family, face, primary, character, rune);
            if (current is not null && !ReferenceEquals(font, current))
            {
                runs.Add(Run(segment.ToString(), ReferenceEquals(current, primary) ? style : baseStyle.WithFont(current), link, destination, language));
                segment.Clear();
            }

            current = font;
            if (font.CanEncode(character) || Skipped(rune))
            {
                segment.Append(character);
            }
            else
            {
                segment.Append(font.CanEncode("?") ? '?' : ' ');
            }
        }

        if (segment.Length > 0)
        {
            runs.Add(Run(segment.ToString(), ReferenceEquals(current, primary) ? style : baseStyle.WithFont(current!), link, destination, language));
        }

        return runs;
    }

    private static TextRun Run(string text, TextStyle? style, Uri? link, string? destination, string? language) =>
        style is null
            ? new TextRun(text) { Link = link, Destination = destination, Language = language }
            : new TextRun(text, style) { Link = link, Destination = destination, Language = language };

    private PdfCanvasFont FontFor(FontFamily family, FontFace face, PdfCanvasFont primary, ReadOnlySpan<char> character, Rune rune)
    {
        if (primary.CanEncode(character))
        {
            return primary;
        }

        foreach (FontFamily fallback in family.Fallbacks)
        {
            PdfCanvasFont font = Face(fallback, face);
            if (font.CanEncode(character))
            {
                return font;
            }
        }

        _context.Report(
            LayoutDiagnostics.CharacterNotEncodable,
            $"U+{rune.Value:X4} is in no face of the font family {family.Name} or its fallbacks; a question mark is drawn instead.");
        return primary;
    }

    private static bool Encodes(PdfCanvasFont font, string text)
    {
        if (font.CanEncode(text))
        {
            return true;
        }

        Span<char> buffer = stackalloc char[2];
        foreach (Rune rune in text.EnumerateRunes())
        {
            int length = rune.EncodeToUtf16(buffer);
            if (!Skipped(rune) && !font.CanEncode(buffer[..length]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Characters the engine never draws: line breaks and the soft hyphen.</summary>
    private static bool Skipped(Rune rune) => rune.Value is '\n' or '\r' or '\v' or '\f' or 0x85 or 0x2028 or 0x2029 or 0xAD;

    private ListBlock BuildList(ListNode node, Style scope, string? language)
    {
        Style full = Resolve(null, scope, node.Local);
        Style inner = Merge(scope, node.Local?.Inherited);
        ListStyle style = new ListStyle(TextStyleOf(full, Visible(full.Color))).WithNumbering(node.Numbering).WithStartAt(node.StartAt);
        if (node.Bullet is { } bullet)
        {
            style = style.WithBullet(bullet);
        }

        if (node.Prefix is { } prefix)
        {
            style = style.WithPrefix(prefix).WithSuffix(node.Suffix!);
        }

        if (node.Indent is { } indent)
        {
            style = style.WithIndent(indent.ToPoints());
        }

        string? listLanguage = string.Equals(full.Language, language, StringComparison.OrdinalIgnoreCase) ? null : full.Language;
        string? itemLanguage = full.Language;
        var list = new ListBlock(style) { Language = listLanguage };
        foreach (ItemNode item in node.Items)
        {
            if (item.Text is { } text)
            {
                list.Add(new ListItem(Paragraph(text, null, Resolve(null, inner, null), "P", itemLanguage)));
            }
            else
            {
                list.Add(new ListItem(Blocks(item.Content!, inner, itemLanguage)));
            }
        }

        return list;
    }

    private Table BuildTable(TableNode node, Style scope, string? language)
    {
        int span = 0;
        foreach (RowNode row in node.HeaderRows)
        {
            span = Math.Max(span, row.Span);
        }

        foreach (RowNode row in node.BodyRows)
        {
            span = Math.Max(span, row.Span);
        }

        ColumnWidth[] columns = node.Columns ?? [.. Enumerable.Repeat(ColumnWidth.Proportional(1), Math.Max(span, 1))];
        if (span > columns.Length)
        {
            throw new InvalidOperationException($"A table row covers {span} columns; the table has {columns.Length}.");
        }

        TableStyle style = _theme.Table;
        if (node.Border is { } border)
        {
            style = style.WithBorder(border);
        }

        if (node.Padding is { } padding)
        {
            style = style.WithPadding(new CellPadding(padding.ToPoints()));
        }

        if (node.HasHeaderBackground)
        {
            style = style.WithHeaderBackground(node.HeaderBackground);
        }

        if (node.RepeatHeaderRows is { } repeat)
        {
            style = style.WithRepeatHeaderRows(repeat);
        }

        if (node.KeepRowsTogether is { } keep)
        {
            style = style.WithKeepRowsTogether(keep);
        }

        Style tableScope = Merge(scope, node.Scope?.Inherited);
        string? tableLanguage = string.Equals(tableScope.Language ?? _root.Language, language, StringComparison.OrdinalIgnoreCase) ? null : tableScope.Language;
        string? cellLanguage = tableLanguage ?? language;
        var table = new Table(columns)
        {
            HeaderRows = node.HeaderRows.Count,
            Style = style,
            SpaceBefore = node.SpaceBefore?.ToPoints() ?? 0,
            SpaceAfter = node.SpaceAfter?.ToPoints() ?? _root.SpaceAfter?.ToPoints() ?? 0,
            Language = tableLanguage,
            Width = node.Width?.ToPoints(),
            Alignment = node.Alignment,
            Id = node.Id,
        };

        foreach (RowNode row in node.HeaderRows)
        {
            table.Add(BuildRow(row, tableScope, cellLanguage, header: true, null));
        }

        for (int index = 0; index < node.BodyRows.Count; index++)
        {
            table.Add(BuildRow(node.BodyRows[index], tableScope, cellLanguage, header: false, node.Striped && index % 2 == 1 ? _theme.Subtle : null));
        }

        return table;
    }

    private TableRow BuildRow(RowNode row, Style tableScope, string? language, bool header, PdfColor? stripe)
    {
        Style rowScope = Merge(tableScope, row.Scope?.Inherited);
        TableCell[] cells;
        if (row.Cells is { } nodes)
        {
            cells = new TableCell[nodes.Count];
            for (int index = 0; index < nodes.Count; index++)
            {
                cells[index] = BuildCell(nodes[index], rowScope, language, header);
            }
        }
        else
        {
            cells = new TableCell[row.PlainCount];
            Style full = Resolve(_cell, rowScope, null);
            for (int index = 0; index < row.PlainCount; index++)
            {
                cells[index] = new TableCell(Paragraph(_context.PlainCells[row.PlainStart + index], null, full, "P", language));
            }
        }

        return new TableRow(cells)
        {
            Background = row.Background ?? stripe,
            KeepTogether = row.KeepTogether,
            MinHeight = row.MinHeight?.ToPoints() ?? 0,
        };
    }

    private TableCell BuildCell(CellNode cell, Style rowScope, string? language, bool headerRow)
    {
        Style cellScope = Merge(rowScope, cell.Scope?.Inherited);
        Style type = cell.Header ? _headerCell : _cell;
        IEnumerable<FlowBlock> blocks = cell.Text is { } text
            ? [Paragraph(text, null, Resolve(type, cellScope, null), "P", language)]
            : Blocks(cell.Content!, cell.Header ? Merge(cellScope, _theme.TableHeader.Inherited) : cellScope, language);
        return new TableCell(blocks)
        {
            ColumnSpan = cell.ColumnSpan,
            RowSpan = cell.RowSpan,
            Background = cell.Background,
            Padding = cell.Padding is { } padding ? new CellPadding(padding.ToPoints()) : null,
            Scope = cell.Header && !headerRow ? TableHeaderScope.Row : TableHeaderScope.None,
        };
    }

    /// <summary>A table of contents, with its title (a paragraph in the level-2 heading look, not a heading) kept with it.</summary>
    private void AddTableOfContents(Func<FlowBlock, object> add, TocNode node, Style scope, string? language)
    {
        Style full = Resolve(null, scope, node.Local);
        var toc = new TableOfContents(ParagraphStyleOf(full)) { Depth = node.Depth, LeaderText = node.Leader ?? "." };
        if (node.Title is { } title)
        {
            Style look = Resolve(_headings[1], scope, node.Local?.SpaceBefore is { } before ? new Style { SpaceBefore = before } : null);
            add(new BlockGroup(Paragraph(title, null, look, "P", language)) { KeepWithNext = true });
        }

        add(Kept(node, toc));
    }

    private FieldBlock BuildField(FieldNode node, Style scope)
    {
        Style full = Resolve(null, scope, node.Local);
        ParagraphStyle label = ParagraphStyleOf(full);
        double height = node.Height?.ToPoints() ?? (node.Lines > 0 ? (node.Lines * full.FontSize!.Value * full.LineHeight!.Value) + 4 : 0);
        return new FieldBlock(node.Type, node.Name, node.Label, label)
        {
            Width = node.Width?.ToPoints() ?? 0,
            Height = height,
            Choices = node.Choices,
            Value = node.Value,
            IsChecked = node.Checked,
            Options = node.Flags == PdfFieldFlags.None ? null : new PdfFieldOptions().WithFlags(node.Flags),
            LabelPosition = node.LabelPosition,
            SpaceBefore = full.SpaceBefore?.ToPoints() ?? 0,
            SpaceAfter = full.SpaceAfter?.ToPoints() ?? 0,
            Id = node.Id,
        };
    }

    /// <summary>The image of <paramref name="source"/> in the document, embedded on first use.</summary>
    private PdfImage Embed(ImageSource source)
    {
        if (!_images.TryGetValue(source, out PdfImage? image))
        {
            image = source.Embed(_document);
            _images.Add(source, image);
        }

        return image;
    }

    /// <summary>The form XObject of an SVG source in the document, imported on first use.</summary>
    private PdfFormXObject EmbedSvg(ImageSource source)
    {
        if (!_svgs.TryGetValue(source, out PdfFormXObject? form))
        {
            form = source.EmbedSvg(_document);
            _svgs.Add(source, form);
        }

        return form;
    }

    private DrawingBlock BuildDrawing(DrawingNode node, Style scope, string? language)
    {
        double width = node.Width.ToPoints();
        double height = node.Height.ToPoints();
        Action<PdfCanvas, PdfRectangle> draw = node.Draw;
        if (node.Svg is { } svg)
        {
            PdfFormXObject form = EmbedSvg(svg);
            PdfRectangle bounds = form.BoundingBox ?? new PdfRectangle(0, 0, 1, 1);
            double aspect = bounds.Height / bounds.Width;
            (width, height) = (node.SvgWidth, node.SvgHeight) switch
            {
                ({ } w, { } h) => (w.ToPoints(), h.ToPoints()),
                ({ } w, null) => (w.ToPoints(), w.ToPoints() * aspect),
                (null, { } h) => (h.ToPoints() / aspect, h.ToPoints()),
                _ => (bounds.Width, bounds.Height),
            };
            draw = (canvas, area) =>
            {
                canvas.Transform(new Matrix(area.Width / bounds.Width, 0, 0, area.Height / bounds.Height, area.Left - (bounds.Left * area.Width / bounds.Width), area.Bottom - (bounds.Bottom * area.Height / bounds.Height)));
                canvas.DrawForm(form);
            };
        }

        if (node.FillWidth)
        {
            height *= _contentWidth / width;
            width = _contentWidth;
        }

        CheckAlternateText(node.AlternateText);
        Style full = Resolve(null, scope, node.Local);
        Style box = node.Local ?? Style.Empty;
        if (node.Barcode is { } barcode && node.BarcodeOptions is { Font: null, ShowText: true } options && barcode.HumanReadableText is not null)
        {
            // The human-readable line of a linear symbol is set in the style's font, embedded only when one is drawn.
            BarcodeOptions styled = options.WithFont(Face(full.FontFamily!, FaceOf(full)), full.FontSize!.Value);
            draw = (canvas, area) => canvas.DrawBarcode(barcode, area, styled);
        }

        return new DrawingBlock(width, height, draw)
        {
            AlternateText = node.AlternateText,
            IsDecorative = node.AlternateText is null,
            Id = node.Id,
            Alignment = node.Alignment,
            SpaceBefore = box.SpaceBefore?.ToPoints() ?? 0,
            SpaceAfter = box.SpaceAfter?.ToPoints() ?? _root.SpaceAfter?.ToPoints() ?? 0,
            Language = node.AlternateText is null || string.Equals(full.Language, language, StringComparison.OrdinalIgnoreCase) ? null : full.Language,
        };
    }

    private ColumnsBlock BuildColumns(ColumnsNode node, Style scope, string? language)
    {
        Style inner = Merge(scope, node.Scope?.Inherited);
        var columns = new FlowColumn[node.Columns.Count];
        for (int index = 0; index < columns.Length; index++)
        {
            ColumnNode column = node.Columns[index];
            columns[index] = new FlowColumn(column.Width, Blocks(column.Content, inner, language));
        }

        Style box = node.Local ?? Style.Empty;
        return new ColumnsBlock(columns)
        {
            Gap = node.Gap?.ToPoints() ?? 12,
            SpaceBefore = box.SpaceBefore?.ToPoints() ?? 0,
            SpaceAfter = box.SpaceAfter?.ToPoints() ?? _root.SpaceAfter?.ToPoints() ?? 0,
        };
    }

    private void CheckAlternateText(string? alternate)
    {
        if (alternate is not null && FileNameLike().IsMatch(alternate))
        {
            _context.Report(
                LayoutDiagnostics.AlternateTextLooksLikeFileName,
                $"The alternate text \"{Shorten(alternate)}\" looks like a file name; it should say what the image shows (ISO 32000-2 §14.9.3).");
        }
    }

    private ImageBlock BuildImage(ImageNode node, Style scope, string? language)
    {
        PdfImage image = Embed(node.Source);

        double aspect = image.Height / (double)image.Width;
        double width;
        double height;
        if (node.FillWidth)
        {
            width = _contentWidth;
            height = node.Height?.ToPoints() ?? width * aspect;
        }
        else if (node.Width is { } w)
        {
            width = w.ToPoints();
            height = node.Height?.ToPoints() ?? width * aspect;
        }
        else if (node.Height is { } h)
        {
            height = h.ToPoints();
            width = height / aspect;
        }
        else
        {
            width = image.Width;
            height = image.Height;
        }

        CheckAlternateText(node.AlternateText);
        Style full = Resolve(null, scope, node.Local);
        string? blockLanguage = string.Equals(full.Language, language, StringComparison.OrdinalIgnoreCase) ? null : full.Language;
        Style box = node.Local ?? Style.Empty;
        return new ImageBlock(image, width, height)
        {
            AlternateText = node.AlternateText,
            Alignment = node.Alignment,
            SpaceBefore = box.SpaceBefore?.ToPoints() ?? 0,
            SpaceAfter = box.SpaceAfter?.ToPoints() ?? _root.SpaceAfter?.ToPoints() ?? 0,
            Id = node.Id,
            StructureType = node.AlternateText is null ? "Artifact" : "Figure",
            Language = node.AlternateText is null ? null : blockLanguage,
        };
    }

    /// <summary>The full style of an element: the theme root, the element type's theme style, the scopes and the element's own modifiers.</summary>
    private Style Resolve(Style? type, Style scope, Style? local) => Merge(Merge(Merge(_root, type), scope), local);

    /// <summary><paramref name="over"/> merged over <paramref name="under"/>, each distinct pair merged once.</summary>
    private Style Merge(Style under, Style? over)
    {
        if (over is null || ReferenceEquals(over, Style.Empty))
        {
            return under;
        }

        var key = new MergeKey(under, over);
        if (!_merged.TryGetValue(key, out Style? merged))
        {
            merged = under.Merge(over);
            _merged.Add(key, merged);
        }

        return merged;
    }

    private ParagraphStyle ParagraphStyleOf(Style full)
    {
        PdfCanvasFont font = Face(full.FontFamily!, FaceOf(full));
        var key = new ParagraphKey(
            font,
            full.FontSize!.Value,
            full.LineHeight!.Value,
            full.Alignment ?? TextAlignment.Left,
            full.FirstLineIndent?.ToPoints() ?? 0,
            full.SpaceBefore?.ToPoints() ?? 0,
            full.SpaceAfter?.ToPoints() ?? 0,
            full.Shaping);
        if (!_paragraphStyles.TryGetValue(key, out ParagraphStyle? style))
        {
            style = new ParagraphStyle(font, key.Size)
                .WithLineHeight(key.LineHeight)
                .WithAlignment(key.Alignment)
                .WithFirstLineIndent(key.FirstLineIndent)
                .WithSpaceBefore(key.SpaceBefore)
                .WithSpaceAfter(key.SpaceAfter);
            if (key.Shaping is { } shaping)
            {
                style = style.WithShaping(shaping);
            }

            _paragraphStyles.Add(key, style);
        }

        return style;
    }

    private static bool NeedsStyle(Style full) => Visible(full.Color) is not null || full.Underline == true || full.StrikeThrough == true;

    /// <summary>Whether two resolved styles draw text the same way.</summary>
    private static bool SameText(Style a, Style b) =>
        ReferenceEquals(a.FontFamily, b.FontFamily) && a.FontSize == b.FontSize && a.Bold == b.Bold && a.Italic == b.Italic
        && a.Underline == b.Underline && a.StrikeThrough == b.StrikeThrough && Visible(a.Color) == Visible(b.Color) && ReferenceEquals(a.Shaping, b.Shaping);

    /// <summary>A text color worth writing: black writes none.</summary>
    private static PdfColor? Visible(PdfColor? color) => color is { } value && value != Black ? value : null;

    private TextStyle TextStyleOf(Style full, PdfColor? color)
    {
        PdfCanvasFont font = Face(full.FontFamily!, FaceOf(full));
        var key = new TextKey(font, full.FontSize!.Value, Visible(color), full.Underline == true, full.StrikeThrough == true, full.Shaping);
        if (!_textStyles.TryGetValue(key, out TextStyle? style))
        {
            style = new TextStyle(font, key.Size).WithColor(key.Color).WithUnderline(key.Underline).WithStrikeThrough(key.StrikeThrough);
            if (key.Shaping is { } shaping)
            {
                style = style.WithShaping(shaping);
            }

            _textStyles.Add(key, style);
        }

        return style;
    }

    private static FontFace FaceOf(Style full) => (full.Bold == true, full.Italic == true) switch
    {
        (false, false) => FontFace.Regular,
        (true, false) => FontFace.Bold,
        (false, true) => FontFace.Italic,
        _ => FontFace.BoldItalic,
    };

    /// <summary>The embedded face of a family, embedding it on first use; a missing face falls back to the regular one with a diagnostic.</summary>
    private PdfCanvasFont Face(FontFamily family, FontFace face)
    {
        var key = new FaceKey(family, face);
        if (_fonts.TryGetValue(key, out PdfCanvasFont? font))
        {
            return font;
        }

        font = family.Embed(_document, face);
        if (font is null && face != FontFace.Regular)
        {
            font = Face(family, FontFace.Regular);
            _context.Report(
                LayoutDiagnostics.FontFaceMissing,
                $"The font family {family.Name} has no {Describe(face)} face; its regular face is used (a face is never synthesized).");
        }
        else if (font is null)
        {
            font = Face(FontFamily.Standard(Standard14Family.Helvetica), face);
            _context.Report(
                LayoutDiagnostics.FontNotFound,
                $"The document's font resolvers found no font named {family.Name}; Helvetica (embedded Liberation Sans) is used instead.");
        }

        _fonts.Add(key, font);
        return font;
    }

    private static string Describe(FontFace face) => face switch
    {
        FontFace.Bold => "bold",
        FontFace.Italic => "italic",
        FontFace.BoldItalic => "bold italic",
        _ => "regular",
    };

    private static string Shorten(string? text) => text is null ? string.Empty : text.Length <= 40 ? text : string.Concat(text.AsSpan(0, 37), "...");

    [GeneratedRegex(@"^\S+\.(png|jpe?g|gif|bmp|tiff?|svg|webp|heic|emf|wmf)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileNameLike();

    private readonly record struct FaceKey(FontFamily Family, FontFace Face)
    {
        public bool Equals(FaceKey other) => ReferenceEquals(Family, other.Family) && Face == other.Face;

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Family), Face);
    }

    private readonly record struct MergeKey(Style Under, Style Over)
    {
        public bool Equals(MergeKey other) => ReferenceEquals(Under, other.Under) && ReferenceEquals(Over, other.Over);

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Under), RuntimeHelpers.GetHashCode(Over));
    }

    private readonly record struct TextKey(PdfCanvasFont Font, double Size, PdfColor? Color, bool Underline, bool StrikeThrough, TextShaping? Shaping);

    private readonly record struct ParagraphKey(PdfCanvasFont Font, double Size, double LineHeight, TextAlignment Alignment, double FirstLineIndent, double SpaceBefore, double SpaceAfter, TextShaping? Shaping);
}
