using System.Globalization;
using System.Text;
using Broadside.Diagnostics;
using Broadside.Layout.Flow.Units;

namespace Broadside.Layout.Flow;

/// <summary>
/// Maps a parsed Markdown document onto the document builder's nodes, so it is laid out and tagged like content described in C#:
/// headings <c>H1</c> to <c>H6</c> with GitHub-style slugs as anchor ids, paragraphs with <c>Em</c>, <c>Strong</c> and <c>Link</c> spans
/// (code in the monospace font is a plain span: the PDF 2.0 namespace has no <c>Code</c>), lists <c>L</c>, tables <c>Table</c>, images
/// <c>Figure</c>, block quotes as indented <c>Div</c>s, thematic breaks as artifact rules. Raw HTML is dropped. Nesting deeper than <see cref="MaxNesting"/> is laid out at that depth.
/// </summary>
/// <remarks>ISO 32000-2 §14.8.4.3 to §14.8.4.8 (the structure types written), §14.9.3 (alternate descriptions) and ISO 14289-2 §8.2.</remarks>
internal sealed class MarkdownBuilder
{
    /// <summary>The deepest nesting of block quotes and lists laid out as such; deeper content keeps the indent of this depth.</summary>
    internal const int MaxNesting = 12;

    private const int Bold = 1;
    private const int Italic = 2;
    private const int Strike = 4;
    private const int Mono = 8;

    private static readonly Length QuoteIndent = 12.Pt;

    private readonly BuildContext _context;
    private readonly MarkdownOptions _options;
    private readonly bool _titleFromHeading;
    private readonly Dictionary<string, int> _slugs = new(StringComparer.Ordinal);
    private readonly Style?[] _styles = new Style?[16];
    private readonly StringBuilder _pending = new();
    private readonly Dictionary<string, ImageSource> _files = new(StringComparer.Ordinal);

    public MarkdownBuilder(BuildContext context, MarkdownOptions? options, bool titleFromHeading)
    {
        _context = context;
        _options = options ?? MarkdownOptions.Default;
        _titleFromHeading = titleFromHeading;
    }

    /// <summary>Parses <paramref name="markdown"/> and adds its blocks to <paramref name="target"/>.</summary>
    public void Add(ContainerNode target, string markdown)
    {
        MarkdownNode document = MarkdownParser.Parse(markdown, _options.GitHubExtensions);
        AddBlocks(target, document, 0);
    }

    private void AddBlocks(ContainerNode target, MarkdownNode parent, int depth)
    {
        for (MarkdownNode? block = parent.FirstChild; block is not null; block = block.Next)
        {
            switch (block.Kind)
            {
                case MarkdownKind.BlockQuote when depth < MaxNesting:
                    var quote = new GroupNode { Role = "Div", Indent = QuoteIndent };
                    AddBlocks(quote.Content, block, depth + 1);
                    target.Blocks.Add(quote);
                    break;
                case MarkdownKind.List when depth < MaxNesting:
                    AddList(target, block, depth);
                    break;
                case MarkdownKind.BlockQuote or MarkdownKind.List:
                    AddFlattened(target, block);
                    break;
                default:
                    AddLeaf(target, block);
                    break;
            }
        }
    }

    /// <summary>Adds the leaf blocks under <paramref name="container"/> in order, without its nesting; iterative, so any depth is safe.</summary>
    private void AddFlattened(ContainerNode target, MarkdownNode container)
    {
        foreach ((MarkdownNode node, bool entering) in container.Walk())
        {
            if (entering && node.Kind is MarkdownKind.Paragraph or MarkdownKind.Heading or MarkdownKind.CodeBlock or MarkdownKind.HtmlBlock
                or MarkdownKind.ThematicBreak or MarkdownKind.Table)
            {
                AddLeaf(target, node);
            }
        }
    }

    private void AddLeaf(ContainerNode target, MarkdownNode block)
    {
        switch (block.Kind)
        {
            case MarkdownKind.Paragraph:
                AddParagraph(target, block, 0, null);
                break;
            case MarkdownKind.Heading:
                AddHeading(target, block);
                break;
            case MarkdownKind.ThematicBreak:
                target.Blocks.Add(new RuleNode());
                break;
            case MarkdownKind.CodeBlock:
                // The code as it is: whitespace kept, tabs expanded to four columns, in the theme's monospace font.
                string code = MarkdownText.ExpandTabs(block.Literal).TrimEnd('\n');
                target.Blocks.Add(new ParagraphNode { Text = code, Local = new Style { FontFamily = _context.Theme.MonospaceFont } });
                break;
            case MarkdownKind.HtmlBlock:
                ReportHtml();
                break;
            case MarkdownKind.Table:
                AddTable(target, block);
                break;
        }
    }

    private void AddHeading(ContainerNode target, MarkdownNode heading)
    {
        string text = heading.PlainText().Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (_titleFromHeading && heading.Level == 1 && _context.Title is null)
        {
            _context.Title = text;
        }

        AddParagraph(target, heading, heading.Level, Slug(text));
    }

    /// <summary>A GitHub-style anchor for a heading: lowercase, punctuation dropped, spaces as hyphens, <c>-1</c>, <c>-2</c> for repeats.</summary>
    private string? Slug(string text)
    {
        var slug = new StringBuilder(text.Length);
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                slug.Append(c);
            }
            else if (c == ' ')
            {
                slug.Append('-');
            }
        }

        if (slug.Length == 0)
        {
            return null;
        }

        string id = slug.ToString();
        if (_slugs.TryGetValue(id, out int count))
        {
            _slugs[id] = count + 1;
            return string.Create(CultureInfo.InvariantCulture, $"{id}-{count}");
        }

        _slugs.Add(id, 1);
        return id;
    }

    private void AddList(ContainerNode target, MarkdownNode block, int depth)
    {
        MarkdownListData data = block.ListData!;
        var list = new ListNode { Numbering = data.Ordered ? ListNumbering.Decimal : ListNumbering.Bullet, StartAt = Math.Max(1, data.Start) };
        if (data.Ordered && data.Delimiter == ')')
        {
            list.Prefix = string.Empty;
            list.Suffix = ")";
        }

        int number = data.Start;
        for (MarkdownNode? item = block.FirstChild; item is not null; item = item.Next, number++)
        {
            var content = new ContainerNode();
            AddBlocks(content, item, depth + 1);
            if (content.Blocks.Count == 0)
            {
                content.Blocks.Add(new ParagraphNode { Text = string.Empty });
            }

            string? marker = data.Ordered && data.Start < 1 ? string.Create(CultureInfo.InvariantCulture, $"{number}{data.Delimiter}") : null;
            list.Items.Add(item.TaskChecked is { } done
                ? new ItemNode
                {
                    Content = content,
                    Marker = done ? "☑" : "☐",
                    MarkerFallback = done ? "■" : "□",
                    MarkerActualText = done ? "☑" : "☐",
                }
                : new ItemNode { Content = content, Marker = marker });
        }

        target.Blocks.Add(list);
    }

    private void AddTable(ContainerNode target, MarkdownNode block)
    {
        var table = new TableNode();
        for (MarkdownNode? row = block.FirstChild; row is not null; row = row.Next)
        {
            var cells = new List<CellNode>();
            for (MarkdownNode? cell = row.FirstChild; cell is not null; cell = cell.Next)
            {
                var content = new ContainerNode();
                AddParagraph(content, cell, 0, null);
                if (content.Blocks.Count == 0)
                {
                    // An empty cell still stands in its row, so the table stays regular.
                    content.Blocks.Add(new ParagraphNode { Text = string.Empty });
                }

                TextAlignment? alignment = cell.Alignments![0] switch
                {
                    MarkdownAlignment.Center => TextAlignment.Center,
                    MarkdownAlignment.Right => TextAlignment.Right,
                    MarkdownAlignment.Left => TextAlignment.Left,
                    _ => null,
                };
                cells.Add(new CellNode { Content = content, Header = row.IsHeader, Scope = alignment is { } align ? new Style { Alignment = align } : null });
            }

            (row.IsHeader ? table.HeaderRows : table.BodyRows).Add(new RowNode { Cells = cells });
        }

        target.Blocks.Add(table);
    }

    /// <summary>
    /// Adds the inlines of <paramref name="block"/> as one paragraph (a heading when <paramref name="level"/> is 1 to 6), split around
    /// the images it holds, which become blocks of their own.
    /// </summary>
    private void AddParagraph(ContainerNode target, MarkdownNode block, int level, string? id)
    {
        var spans = new List<SpanNode>();
        var link = new Stack<(Uri? Uri, string? Anchor)>();
        var emphasis = new Stack<string?>();
        int flags = 0;
        int bold = 0;
        int italic = 0;
        int strike = 0;
        MarkdownNode? image = null;
        SpanKey key = default;
        foreach ((MarkdownNode node, bool entering) in block.Walk())
        {
            if (ReferenceEquals(node, block))
            {
                continue;
            }

            if (image is not null)
            {
                if (ReferenceEquals(node, image) && !entering)
                {
                    image = null;
                }

                continue;
            }

            switch (node.Kind)
            {
                case MarkdownKind.Emphasis:
                    italic += entering ? 1 : -1;
                    Push(emphasis, entering, "Em");
                    break;
                case MarkdownKind.Strong:
                    bold += entering ? 1 : -1;
                    Push(emphasis, entering, "Strong");
                    break;
                case MarkdownKind.Strikethrough:
                    strike += entering ? 1 : -1;
                    Push(emphasis, entering, null);
                    break;
                case MarkdownKind.Link:
                    if (entering)
                    {
                        link.Push(LinkTarget(node.Destination));
                    }
                    else
                    {
                        link.Pop();
                    }

                    break;
                case MarkdownKind.Image when entering:
                    if (Resolve(node.Destination) is { } source)
                    {
                        Flush(spans, key);
                        if (spans.Count > 0)
                        {
                            target.Blocks.Add(NewParagraph(spans, level, ref id));
                            spans = [];
                        }

                        string alternate = node.PlainText().Trim();
                        target.Blocks.Add(new ImageNode { Source = source, AlternateText = alternate.Length > 0 ? alternate : null, Figure = true });
                    }
                    else
                    {
                        string alternate = node.PlainText();
                        if (alternate.Length > 0)
                        {
                            key = Append(spans, key, new SpanKey(flags | Italic, link.Count > 0 ? link.Peek() : default, emphasis.Count > 0 ? emphasis.Peek() : null), alternate);
                        }
                    }

                    image = node.FirstChild is null ? null : node;
                    break;
                case MarkdownKind.Text:
                    key = Append(spans, key, Key(flags, link, emphasis), node.Literal);
                    break;
                case MarkdownKind.Code:
                    key = Append(spans, key, new SpanKey(flags | Mono, link.Count > 0 ? link.Peek() : default, null), node.Literal);
                    break;
                case MarkdownKind.SoftBreak:
                    key = Append(spans, key, Key(flags, link, emphasis), " ");
                    break;
                case MarkdownKind.LineBreak:
                    Flush(spans, key);
                    key = default;
                    spans.Add(new SpanNode { Text = "\n", LineBreak = true });
                    break;
                case MarkdownKind.HtmlInline:
                    ReportHtml();
                    break;
            }

            flags = (bold > 0 ? Bold : 0) | (italic > 0 ? Italic : 0) | (strike > 0 ? Strike : 0);
        }

        Flush(spans, key);
        if (spans.Count > 0 && (level == 0 || spans.Exists(span => !string.IsNullOrWhiteSpace(span.Text))))
        {
            target.Blocks.Add(NewParagraph(spans, level, ref id));
        }
    }

    private static void Push(Stack<string?> emphasis, bool entering, string? type)
    {
        if (entering)
        {
            emphasis.Push(type);
        }
        else
        {
            emphasis.Pop();
        }
    }

    private static ParagraphNode NewParagraph(List<SpanNode> spans, int level, ref string? id)
    {
        var paragraph = new ParagraphNode { Spans = spans, Level = level, Id = id };
        id = null;
        return paragraph;
    }

    private static SpanKey Key(int flags, Stack<(Uri? Uri, string? Anchor)> link, Stack<string?> emphasis) =>
        new(flags, link.Count > 0 ? link.Peek() : default, emphasis.Count > 0 ? emphasis.Peek() : null);

    /// <summary>Appends <paramref name="text"/> in the style <paramref name="next"/>, ending the pending span when its style differs.</summary>
    private SpanKey Append(List<SpanNode> spans, SpanKey current, SpanKey next, string text)
    {
        if (_pending.Length > 0 && current != next)
        {
            Flush(spans, current);
        }

        _pending.Append(text);
        return next;
    }

    private void Flush(List<SpanNode> spans, SpanKey key)
    {
        if (_pending.Length == 0)
        {
            return;
        }

        string text = _pending.ToString();
        _pending.Clear();
        spans.Add(new SpanNode
        {
            Text = text,
            Local = StyleOf(key.Flags),
            Link = key.Link.Uri,
            Destination = key.Link.Anchor,
            StructureType = key.Link.Uri is null && key.Link.Anchor is null ? key.StructureType : null,
        });
    }

    private Style? StyleOf(int flags)
    {
        if (flags == 0)
        {
            return null;
        }

        return _styles[flags] ??= new Style
        {
            Bold = (flags & Bold) != 0 ? true : null,
            Italic = (flags & Italic) != 0 ? true : null,
            StrikeThrough = (flags & Strike) != 0 ? true : null,
            FontFamily = (flags & Mono) != 0 ? _context.Theme.MonospaceFont : null,
        };
    }

    /// <summary>
    /// Where a link goes: an anchor id for <c>#name</c>; an absolute <c>http</c>, <c>https</c>, <c>mailto</c> or <c>ftp</c> URI;
    /// anything else (relative paths, other schemes) is plain text, as a URI action needs an absolute URI.
    /// </summary>
    private static (Uri? Uri, string? Anchor) LinkTarget(string destination)
    {
        if (destination.StartsWith('#'))
        {
            string anchor = Unescaped(destination[1..]);
            return anchor.Length > 0 ? (null, anchor) : default;
        }

        if (Uri.TryCreate(destination, UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "http" or "https" or "mailto" or "ftp")
        {
            return (uri, null);
        }

        return default;
    }

    private static string Unescaped(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            return text;
        }
    }

    /// <summary>
    /// The image for <paramref name="destination"/>: the resolver's, else a PNG or JPEG file under the base directory. Remote
    /// destinations are never fetched; a path that leaves the base directory is refused. Null, with a diagnostic, when there is none.
    /// </summary>
    private ImageSource? Resolve(string destination)
    {
        string written = Unescaped(destination);
        if (_options.ImageResolver?.Invoke(written) is { } supplied)
        {
            return supplied;
        }

        string? reason = null;
        if (_options.BaseDirectory is not { } directory)
        {
            reason = "no image resolver or base directory supplies it";
        }
        else if (written.Contains("://", StringComparison.Ordinal) || written.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || written.StartsWith("//", StringComparison.Ordinal))
        {
            reason = "remote images are not fetched";
        }
        else
        {
            string path = written;
            int query = path.AsSpan().IndexOfAny('?', '#');
            if (query >= 0)
            {
                path = path[..query];
            }

            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string full = path.Length == 0 || Path.IsPathRooted(path) ? string.Empty : Path.GetFullPath(Path.Combine(root, path));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                reason = "the path leaves the base directory";
            }
            else if (!File.Exists(full))
            {
                reason = "no such file is under the base directory";
            }
            else if (_files.TryGetValue(full, out ImageSource? read))
            {
                // The same file drawn again is the same source, so the document embeds it once.
                return read;
            }
            else
            {
                try
                {
                    ImageSource source = ImageSource.FromBytes(File.ReadAllBytes(full));
                    _files.Add(full, source);
                    return source;
                }
                catch (ArgumentException)
                {
                    reason = "the file is neither PNG nor JPEG";
                }
                catch (IOException exception)
                {
                    reason = $"the file cannot be read ({exception.Message})";
                }
                catch (UnauthorizedAccessException exception)
                {
                    reason = $"the file cannot be read ({exception.Message})";
                }
            }
        }

        _context.Report(LayoutDiagnostics.MarkdownImageNotFound, $"The Markdown image \"{Shorten(written)}\" is not shown: {reason}; its alternate text is written instead.");
        return null;
    }

    private void ReportHtml()
    {
        if (_context.MarkdownHtmlReported)
        {
            return;
        }

        _context.MarkdownHtmlReported = true;
        _context.Report(LayoutDiagnostics.MarkdownHtmlIgnored, "The Markdown holds raw HTML, which is not rendered; it is dropped.");
    }

    private static string Shorten(string text) => text.Length <= 80 ? text : string.Concat(text.AsSpan(0, 77), "...");

    /// <summary>What a span's style depends on: its emphasis flags, its link and its structure type.</summary>
    private readonly record struct SpanKey(int Flags, (Uri? Uri, string? Anchor) Link, string? StructureType);
}
