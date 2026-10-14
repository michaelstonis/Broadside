using System.Text;
using Broadside.Annotations;
using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>
/// The normal appearances generated for a text field's widgets from its value: the variable text laid out in the default appearance's
/// font, size and colour, inside the widget's background and border, built before anything in the document changes.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.7.4.3 and §12.7.5.3. Each appearance is a form XObject whose <c>BBox</c> is the widget rectangle's size (with
/// width and height swapped and a rotating <c>Matrix</c> when the widget's <c>MK</c> <c>R</c> is 90 or 270, §12.5.6.19 Table 192),
/// whose resources are copied from the default resources the font was found in, and whose content draws the <c>MK</c> background
/// (<c>BG</c>) and border (<c>BC</c>, in the widget's border style, §12.5.4) and then the text as the clause's example shows:
/// <c>/Tx BMC q</c> clip <c>BT</c> <c>Tf</c>, colour, the default appearance's other operators, then one <c>Tm</c> per line or comb
/// cell (the default appearance's <c>Tm</c> with its translation replaced) and <c>Tj</c>, <c>ET Q EMC</c>.
/// </para>
/// <para>
/// Layout: a single line is centred vertically on the font's ascent and descent and placed by the quadding (<c>Q</c>) 2 points
/// inside the border; a multi-line field breaks its text at line breaks and between words to the field's width, from the top; a comb
/// field divides the width into <c>MaxLen</c> cells and centres one character in each; a password field shows one asterisk per
/// character. An auto-sized font (size 0) fills the height of a single line or a comb cell (shrinking to fit the width) and starts
/// multi-line text at 12 points, shrinking until the lines fit (at least 4 points).
/// </para>
/// <para>
/// Font: the default appearance font when it is a simple font whose encoding has every character; Helvetica (WinAnsi, not embedded)
/// when the string names no usable font; otherwise a TrueType program from the engine's font resolvers, embedded as a subset Type 0
/// font (§9.7, §9.9.2, §9.10.3).
/// </para>
/// <para>
/// Choice fields (§12.7.5.4) are variable text too: a combo box shows its selected option's text (or the typed text of an editable
/// one) as a single-line text field would; a list box shows its options one per line from the top index, each selected one on a
/// highlight.
/// </para>
/// </remarks>
internal sealed class TextFieldAppearance
{
    private const double TextPadding = 2;
    private const double AutoLineFactor = 1.35;
    private const double MultilineAutoStart = 12;
    private const double MinimumAutoSize = 4;
    private const double ListAutoSize = 12;

    private static readonly CosName XObjectName = new("XObject");
    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName Tx = new("Tx");
    private static readonly CosName HelveticaName = new("Helvetica");

    private readonly PdfDocument _document;
    private readonly List<(PdfWidgetAnnotation Widget, CosStream Stream)> _appearances;

    private TextFieldAppearance(PdfDocument document, List<(PdfWidgetAnnotation Widget, CosStream Stream)> appearances, int topIndex)
    {
        _document = document;
        _appearances = appearances;
        TopIndex = topIndex;
    }

    /// <summary>Gets the index of the first option a list box's appearances show (<c>TI</c>); 0 for other fields.</summary>
    public int TopIndex { get; }

    /// <summary>Builds the appearances of every widget of <paramref name="field"/> for <paramref name="value"/>.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value to show; <see langword="null"/> for none.</param>
    /// <returns>The appearances, not yet placed on the widgets (<see cref="Apply"/>).</returns>
    /// <exception cref="InvalidOperationException">A character has no glyph in the default appearance font and no font program that has one was found.</exception>
    public static TextFieldAppearance Build(PdfTextField field, string? value)
    {
        PdfDocument document = field.Document;
        PdfDefaultAppearance? appearance = field.ParsedDefaultAppearance;
        bool multiline = field.IsMultiline;
        int? comb = !multiline && !field.IsPassword && !field.IsFileSelect && field.IsComb && field.MaxLength is > 0 and var length ? length : null;
        string text = DisplayText(value ?? string.Empty, field.IsPassword, multiline, comb);
        return Build(field, text, multiline ? text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal) : text, multiline, comb, list: null);
    }

    /// <summary>
    /// Builds the appearances of every widget of a choice field showing the options at <paramref name="selected"/>, or the typed
    /// <paramref name="text"/> of an editable combo box.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="selected">The selected indices into the field's options, ascending.</param>
    /// <param name="text">The text an editable combo box shows instead of an option, or <see langword="null"/>.</param>
    /// <param name="topIndex">The first option a list box shows; <see langword="null"/> for the field's <c>TI</c>.</param>
    /// <param name="scrollToSelection">Whether a list box scrolls so that its first selected option is visible.</param>
    /// <returns>The appearances, not yet placed on the widgets (<see cref="Apply"/>).</returns>
    /// <exception cref="InvalidOperationException">A character has no glyph in the default appearance font and no font program that has one was found.</exception>
    public static TextFieldAppearance BuildChoice(PdfChoiceField field, IReadOnlyList<int> selected, string? text, int? topIndex, bool scrollToSelection)
    {
        IReadOnlyList<PdfChoiceOption> options = field.Options;
        if (field is PdfComboBoxField)
        {
            string shown = DisplayText(text ?? (selected.Count > 0 ? options[selected[0]].DisplayText : string.Empty), password: false, multiline: false, comb: null);
            return Build(field, shown, shown, multiline: false, comb: null, list: null);
        }

        string[] lines = [.. options.Select(option => DisplayText(option.DisplayText, password: false, multiline: false, comb: null))];
        var list = new ListContent(lines, selected, Math.Clamp(topIndex ?? field.TopIndex, 0, Math.Max(0, lines.Length - 1)), scrollToSelection);
        return Build(field, string.Empty, string.Concat(lines), multiline: false, comb: null, list);
    }

    /// <summary>Builds the appearances of every widget of <paramref name="field"/> showing <paramref name="text"/>, or a list.</summary>
    private static TextFieldAppearance Build(PdfTerminalField field, string text, string fontText, bool multiline, int? comb, ListContent? list)
    {
        PdfDocument document = field.Document;
        PdfDefaultAppearance? appearance = field.ParsedDefaultAppearance;
        CosDictionary fontResources = CopyFontResources(document, appearance?.FontResources);
        (AppearanceFont font, CosName fontName) = ChooseFont(document, appearance, fontResources, fontText);
        fontResources[fontName] = font.FontObject;

        var layout = new Layout(
            font,
            fontName,
            Math.Abs(appearance?.FontSize ?? 0),
            appearance?.Color,
            appearance?.TextMatrix,
            StateOperators(appearance?.OtherOperators ?? ReadOnlyMemory<byte>.Empty),
            field.Quadding,
            multiline,
            comb,
            list);

        var appearances = new List<(PdfWidgetAnnotation, CosStream)>();
        foreach (PdfWidgetAnnotation widget in field.Widgets)
        {
            appearances.Add((widget, BuildStream(document, widget, appearance?.FontResources, fontResources, (content, box, width) =>
            {
                content.Name(Tx).Operator("BMC").Operator("q");
                content.Rectangle(box.Left, box.Bottom, box.Width, box.Height).Operator("W").Operator("n");
                layout.Write(content, text, box, width);
                content.Operator("Q").Operator("EMC");
            })));
        }

        return new TextFieldAppearance(document, appearances, list?.Top ?? list?.RequestedTop ?? 0);
    }

    /// <summary>Makes each appearance a new indirect object and the normal appearance (<c>AP</c> <c>N</c>) of its widget.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.3: the new appearance stream becomes the normal appearance of the widget.</remarks>
    public void Apply()
    {
        foreach ((PdfWidgetAnnotation widget, CosStream stream) in _appearances)
        {
            CosReference reference = _document.AddObject(stream);
            widget.Dictionary[AnnotationNames.AP] = new CosDictionary { [AnnotationNames.N] = reference };
        }
    }

    /// <summary>
    /// The default appearance's other operators without those that position or show text (<c>Td</c>, <c>TD</c>, <c>T*</c>, <c>Tj</c>,
    /// <c>TJ</c>, <c>'</c>, <c>"</c>): the string establishes parameters for the variable text (§12.7.4.3), and the processor
    /// positions the text itself, so a string that shows text of its own would draw it into every appearance.
    /// </summary>
    private static ReadOnlyMemory<byte> StateOperators(ReadOnlyMemory<byte> operators)
    {
        if (operators.IsEmpty)
        {
            return operators;
        }

        var kept = new List<byte>(operators.Length);
        var arena = new OperandArena();
        var reader = new ContentReader(operators.Span, arena);
        while (reader.Next(out ReadOperator op))
        {
            arena.Clear();
            if (op.Code is ContentOperatorCode.MoveText or ContentOperatorCode.MoveTextSetLeading or ContentOperatorCode.NextLine
                or ContentOperatorCode.ShowText or ContentOperatorCode.ShowTextAdjusted or ContentOperatorCode.NextLineShowText
                or ContentOperatorCode.NextLineShowTextWithSpacing)
            {
                continue;
            }

            if (kept.Count > 0)
            {
                kept.Add((byte)' ');
            }

            kept.AddRange(operators.Span[op.Start..op.End]);
        }

        return kept.ToArray();
    }

    /// <summary>The text as shown: asterisks for a password, at most MaxLen characters in combs, one line unless multi-line.</summary>
    private static string DisplayText(string value, bool password, bool multiline, int? comb)
    {
        if (!multiline)
        {
            value = value.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
        }

        if (password)
        {
            int count = 0;
            foreach (Rune rune in value.EnumerateRunes())
            {
                count += rune.Value is '\r' or '\n' ? 0 : 1;
            }

            return new string('*', count);
        }

        if (comb is { } cells)
        {
            var truncated = new StringBuilder();
            int count = 0;
            foreach (Rune rune in value.EnumerateRunes())
            {
                if (count++ == cells)
                {
                    break;
                }

                truncated.Append(rune.ToString());
            }

            return truncated.ToString();
        }

        return value;
    }

    /// <summary>
    /// The appearance's resources: those of the default resources the font was found in, each category copied one level deep so a
    /// font added for the appearance does not change the form's <c>DR</c> (§12.7.4.3).
    /// </summary>
    internal static CosDictionary CopyFontResources(PdfDocument document, CosDictionary? resources)
    {
        if (resources is not null && document.Resolve(resources.TryGetValue(FontNames.Font, out CosObject? fonts) ? fonts : null) is CosDictionary existing)
        {
            var copy = new CosDictionary();
            foreach (KeyValuePair<CosName, CosObject> entry in existing)
            {
                copy[entry.Key] = entry.Value;
            }

            return copy;
        }

        return [];
    }

    /// <summary>Chooses the font that shows <paramref name="text"/> and the resource name it is shown under.</summary>
    private static (AppearanceFont Font, CosName Name) ChooseFont(PdfDocument document, PdfDefaultAppearance? appearance, CosDictionary fonts, string text)
    {
        if (appearance is { Font: PdfSimpleFont simple, FontName: { } name } && fonts.TryGetValue(name, out CosObject? entry))
        {
            var candidate = new SimpleAppearanceFont(simple, entry);
            if (candidate.CanEncode(text))
            {
                return (candidate, name);
            }
        }

        // Without a usable default appearance font, Helvetica (non-embedded) shows what WinAnsi encodes, as other processors do.
        SimpleAppearanceFont? helvetica = Helvetica(document);
        if (appearance?.Font is null && helvetica is not null && helvetica.CanEncode(text))
        {
            return (helvetica, UniqueName(fonts, "Helv"));
        }

        if (EmbeddedAppearanceFont.Create(document, appearance?.Font, [text], out string? missing) is { } embedded)
        {
            return (embedded, UniqueName(fonts, "BrsF"));
        }

        // A default appearance font the text cannot be shown in (a composite or Type 3 font) and no program to embed: Helvetica.
        if (helvetica is not null && helvetica.CanEncode(text))
        {
            return (helvetica, UniqueName(fonts, "Helv"));
        }

        throw new InvalidOperationException(missing is not null
            ? $"No font program found by the engine's font resolvers has a glyph for the character '{missing}' (U+{char.ConvertToUtf32(missing, 0):X4}), which the field's default appearance font cannot show either."
            : "The field's text has characters its default appearance font cannot show, and no TrueType font program to embed for them was found. Configure the Standard 14 fonts package (PdfOptions.UseStandard14Fonts()) or a font resolver (PdfOptions.UseFontResolver).");
    }

    /// <summary>Non-embedded Helvetica in WinAnsiEncoding, a new font dictionary (§9.6.2.2).</summary>
    private static SimpleAppearanceFont? Helvetica(PdfDocument document)
    {
        var dictionary = new CosDictionary
        {
            [Objects.KnownNames.Type] = FontNames.Font,
            [Subtype] = FontNames.Type1,
            [FontNames.BaseFont] = HelveticaName,
            [FontNames.Encoding] = FontNames.WinAnsiEncoding,
        };
        return document.GetFont(dictionary) is PdfSimpleFont font ? new SimpleAppearanceFont(font, dictionary) : null;
    }

    /// <summary>A name not yet used in <paramref name="fonts"/>: <paramref name="stem"/>, else the stem followed by a number.</summary>
    internal static CosName UniqueName(CosDictionary fonts, string stem)
    {
        var name = new CosName(stem);
        for (int index = 1; fonts.ContainsKey(name); index++)
        {
            name = new CosName(stem + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return name;
    }

    /// <summary>
    /// Builds one widget's appearance stream: the <c>MK</c> background and border, then what <paramref name="body"/> writes given the
    /// box inside the border and the form's width; resources copied from <paramref name="defaultResources"/> with <paramref name="fonts"/>.
    /// </summary>
    internal static CosStream BuildStream(PdfDocument document, PdfWidgetAnnotation widget, CosDictionary? defaultResources, CosDictionary fonts, Action<ContentWriter, Box, double> body)
    {
        PdfRectangle rect = widget.Rect;
        PdfAppearanceCharacteristics? characteristics = widget.AppearanceCharacteristics;
        int rotation = characteristics?.Rotation ?? 0;
        (double width, double height) = rotation is 90 or 270 ? (Math.Abs(rect.Height), Math.Abs(rect.Width)) : (Math.Abs(rect.Width), Math.Abs(rect.Height));

        PdfDeviceColor? background = Visible(characteristics?.BackgroundColor);
        PdfDeviceColor? borderColor = Visible(characteristics?.BorderColor);
        PdfAnnotationBorder border = widget.Border;
        double borderWidth = borderColor is null ? 0 : Math.Max(0, border.Width);
        bool bevelled = border.Style is PdfBorderStyle.Beveled or PdfBorderStyle.Inset;

        var content = new ContentWriter();
        if (background is not null)
        {
            content.Operator("q");
            WriteColor(content, background.Components, stroking: false);
            content.Rectangle(0, 0, width, height).Operator("f").Operator("Q");
        }

        if (borderColor is not null && borderWidth > 0)
        {
            WriteBorder(content, border, borderColor, borderWidth, width, height);
        }

        double inset = Math.Max(1, bevelled ? 2 * borderWidth : borderWidth);
        var box = new Box(inset, inset, Math.Max(0, width - (2 * inset)), Math.Max(0, height - (2 * inset)));
        body(content, box, width);

        var resources = new CosDictionary();
        if (defaultResources is not null)
        {
            foreach (KeyValuePair<CosName, CosObject> entry in defaultResources)
            {
                if (!entry.Key.Equals(FontNames.Font))
                {
                    resources[entry.Key] = document.Resolve(entry.Value) is CosDictionary category ? Copy(category) : entry.Value;
                }
            }
        }

        resources[FontNames.Font] = Copy(fonts);
        var dictionary = new CosDictionary
        {
            [Objects.KnownNames.Type] = XObjectName,
            [Subtype] = AnnotationNames.Form,
            [AnnotationNames.BBox] = new CosArray([new CosInteger(0), new CosInteger(0), Number(width), Number(height)]),
            [AnnotationNames.Resources] = resources,
        };
        if (rotation != 0)
        {
            double[] matrix = rotation switch
            {
                90 => [0, 1, -1, 0, height, 0],
                180 => [-1, 0, 0, -1, width, height],
                _ => [0, -1, 1, 0, 0, width],
            };
            dictionary[AnnotationNames.Matrix] = new CosArray(matrix.Select(Number));
        }

        return new CosStream(dictionary, content.ToArray());
    }

    /// <summary>The colour, or <see langword="null"/> when there is none or it is transparent (an empty array, Table 192).</summary>
    private static PdfDeviceColor? Visible(PdfDeviceColor? color) => color is null || color.IsTransparent ? null : color;

    private static CosDictionary Copy(CosDictionary source)
    {
        var copy = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> entry in source)
        {
            copy[entry.Key] = entry.Value;
        }

        return copy;
    }

    internal static CosNumber Number(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < int.MaxValue ? new CosInteger((long)value) : new CosReal(Math.Round(value, 4));

    /// <summary>Strokes the border in its style (§12.5.4, Table 168): solid, dashed, underlined, or bevelled and inset with shaded edges.</summary>
    private static void WriteBorder(ContentWriter content, PdfAnnotationBorder border, PdfDeviceColor color, double borderWidth, double width, double height)
    {
        double half = borderWidth / 2;
        content.Operator("q");
        WriteColor(content, color.Components, stroking: true);
        content.Number(borderWidth).Operator("w");
        switch (border.Style)
        {
            case PdfBorderStyle.Underline:
                content.Number(0).Number(half).Operator("m").Number(width).Number(half).Operator("l").Operator("S");
                break;
            case PdfBorderStyle.Dashed:
                content.Verbatim(Encoding.ASCII.GetBytes("[" + string.Join(' ', (border.DashPattern.Count > 0 ? border.DashPattern : [3.0]).Select(dash => Math.Round(dash, 4).ToString(System.Globalization.CultureInfo.InvariantCulture))) + "] 0 d"));
                content.Rectangle(half, half, width - borderWidth, height - borderWidth).Operator("S");
                break;
            default:
                content.Rectangle(half, half, width - borderWidth, height - borderWidth).Operator("S");
                if (border.Style is PdfBorderStyle.Beveled or PdfBorderStyle.Inset)
                {
                    double outer = borderWidth;
                    double inner = 2 * borderWidth;
                    content.Number(border.Style == PdfBorderStyle.Beveled ? 1 : 0.5).Operator("g");
                    content.Number(outer).Number(outer).Operator("m").Number(outer).Number(height - outer).Operator("l")
                        .Number(width - outer).Number(height - outer).Operator("l").Number(width - inner).Number(height - inner).Operator("l")
                        .Number(inner).Number(height - inner).Operator("l").Number(inner).Number(inner).Operator("l").Operator("f");
                    content.Number(0.75).Operator("g");
                    content.Number(width - outer).Number(height - outer).Operator("m").Number(width - outer).Number(outer).Operator("l")
                        .Number(outer).Number(outer).Operator("l").Number(inner).Number(inner).Operator("l")
                        .Number(width - inner).Number(inner).Operator("l").Number(width - inner).Number(height - inner).Operator("l").Operator("f");
                }

                break;
        }

        content.Operator("Q");
    }

    /// <summary>Writes a device colour operator for 1, 3 or 4 components (§8.6.8, Table 73).</summary>
    internal static void WriteColor(ContentWriter content, IReadOnlyList<double> components, bool stroking)
    {
        foreach (double component in components)
        {
            content.Number(component);
        }

        content.Operator(components.Count switch
        {
            1 => stroking ? "G" : "g",
            3 => stroking ? "RG" : "rg",
            _ => stroking ? "K" : "k",
        });
    }

    /// <summary>The rectangle text is clipped to and laid out in, in form space.</summary>
    internal readonly record struct Box(double Left, double Bottom, double Width, double Height)
    {
        public double Top => Bottom + Height;
    }

    /// <summary>The field-wide part of the text layout: font, size, colour, other operators, quadding and kind of field.</summary>
    private sealed record Layout(
        AppearanceFont Font,
        CosName FontName,
        double FontSize,
        PdfColor? Color,
        Matrix? TextMatrix,
        ReadOnlyMemory<byte> OtherOperators,
        PdfTextJustification Quadding,
        bool Multiline,
        int? Comb,
        ListContent? List)
    {
        /// <summary>The height of a line in thousandths of the font size: the font's ascent to its descent.</summary>
        private double LineFactor => Math.Max(1000, Font.Ascent - Font.Descent) / 1000;

        /// <summary>Writes the text object for one widget.</summary>
        public void Write(ContentWriter content, string text, Box box, double width)
        {
            var placed = new List<(string Text, double X, double Y)>();
            var highlights = new List<Box>();
            double size = List is { } list ? LayOutList(list, box, placed, highlights)
                : Comb is { } cells ? LayOutComb(text, cells, box, width, placed)
                : Multiline ? LayOutLines(text, box, placed) : LayOutLine(text, box, placed);
            if (highlights.Count > 0)
            {
                content.Operator("q");
                WriteColor(content, ListHighlight, stroking: false);
                foreach (Box highlight in highlights)
                {
                    content.Rectangle(highlight.Left, highlight.Bottom, highlight.Width, highlight.Height);
                }

                content.Operator("f").Operator("Q");
            }

            if (placed.Count == 0)
            {
                return;
            }

            content.Operator("BT");
            content.Name(FontName).Number(size).Operator("Tf");
            if (Color is { } color)
            {
                foreach (float component in color.Components)
                {
                    content.Number(component);
                }

                content.Operator(color.ComponentCount switch { 1 => "g", 3 => "rg", _ => "k" });
            }

            content.Verbatim(OtherOperators.Span);
            Matrix matrix = TextMatrix ?? Matrix.Identity;
            foreach ((string part, double x, double y) in placed)
            {
                content.Number(matrix.A).Number(matrix.B).Number(matrix.C).Number(matrix.D).Number(x).Number(y).Operator("Tm");
                content.String(Font.Encode(part), Font.IsHexadecimal).Operator("Tj");
            }

            content.Operator("ET");
        }

        private double LayOutLine(string text, Box box, List<(string, double, double)> placed)
        {
            double available = Math.Max(0, box.Width - (2 * TextPadding));
            double units = Font.Measure(text);
            double size = FontSize;
            if (size == 0)
            {
                size = Math.Max(MinimumAutoSize, box.Height / AutoLineFactor);
                if (units > 0 && units * size / 1000 > available)
                {
                    size = Math.Max(MinimumAutoSize, available * 1000 / units);
                }
            }

            if (text.Length > 0)
            {
                double textWidth = units * size / 1000;
                placed.Add((text, X(box, textWidth), Baseline(box, size)));
            }

            return size;
        }

        /// <summary>
        /// Lays a list box out one option per line from the top index (scrolled so the first selected option is fully visible when
        /// asked), each selected option on a highlight; an auto size is 12 points.
        /// </summary>
        private double LayOutList(ListContent list, Box box, List<(string, double, double)> placed, List<Box> highlights)
        {
            double size = FontSize == 0 ? ListAutoSize : FontSize;
            double lineHeight = size * LineFactor;
            int visible = Math.Max(1, (int)Math.Floor(box.Height / lineHeight));
            int top = list.Top ??= list.Scroll && list.Selected.Count > 0 && (list.Selected[0] < list.RequestedTop || list.Selected[0] >= list.RequestedTop + visible)
                ? list.Selected[0]
                : list.RequestedTop;
            for (int index = top; index < list.Lines.Count; index++)
            {
                double lineTop = box.Top - ((index - top) * lineHeight);
                if (lineTop <= box.Bottom)
                {
                    break;
                }

                double lineBottom = lineTop - lineHeight;
                if (list.Selected.Contains(index))
                {
                    highlights.Add(new Box(box.Left, lineBottom, box.Width, lineHeight));
                }

                string line = list.Lines[index];
                if (line.Length > 0)
                {
                    placed.Add((line, X(box, Font.Measure(line) * size / 1000), lineBottom - (Math.Min(0, Font.Descent) * size / 1000)));
                }
            }

            return size;
        }

        private double LayOutComb(string text, int cells, Box box, double width, List<(string, double, double)> placed)
        {
            double cell = width / cells;
            double size = FontSize;
            var runes = text.EnumerateRunes().Select(rune => rune.ToString()).ToList();
            if (size == 0)
            {
                size = Math.Max(MinimumAutoSize, box.Height / AutoLineFactor);
                double widest = runes.Count == 0 ? 0 : runes.Max(Font.Measure);
                if (widest > 0 && widest * size / 1000 > cell)
                {
                    size = Math.Max(MinimumAutoSize, cell * 1000 / widest);
                }
            }

            double baseline = Baseline(box, size);
            for (int index = 0; index < runes.Count; index++)
            {
                double glyphWidth = Font.Measure(runes[index]) * size / 1000;
                placed.Add((runes[index], (index * cell) + ((cell - glyphWidth) / 2), baseline));
            }

            return size;
        }

        private double LayOutLines(string text, Box box, List<(string, double, double)> placed)
        {
            double available = Math.Max(1, box.Width - (2 * TextPadding));
            double size = FontSize;
            List<string> lines;
            if (size == 0)
            {
                size = MultilineAutoStart;
                lines = Wrap(text, available * 1000 / size);
                while (size > MinimumAutoSize && (lines.Count * size * LineFactor) > box.Height - TextPadding)
                {
                    size = Math.Max(MinimumAutoSize, size - 0.5);
                    lines = Wrap(text, available * 1000 / size);
                }
            }
            else
            {
                lines = Wrap(text, available * 1000 / size);
            }

            double y = box.Top - TextPadding - (Font.Ascent * size / 1000);
            foreach (string line in lines)
            {
                if (line.Length > 0)
                {
                    placed.Add((line, X(box, Font.Measure(line) * size / 1000), y));
                }

                y -= size * LineFactor;
            }

            return size;
        }

        /// <summary>Breaks text at line breaks, then greedily between words to <paramref name="limit"/> (thousandths); a word wider than a line breaks between characters.</summary>
        private List<string> Wrap(string text, double limit)
        {
            var lines = new List<string>();
            double space = Font.Measure(" ");
            foreach (string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
            {
                var line = new StringBuilder();
                double width = 0;
                foreach (string word in paragraph.Split(' '))
                {
                    double wordWidth = Font.Measure(word);
                    double joined = line.Length == 0 ? wordWidth : width + space + wordWidth;
                    if (joined <= limit)
                    {
                        if (line.Length > 0)
                        {
                            line.Append(' ');
                        }

                        line.Append(word);
                        width = joined;
                        continue;
                    }

                    if (line.Length > 0)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                        width = 0;
                    }

                    foreach (Rune rune in word.EnumerateRunes())
                    {
                        string character = rune.ToString();
                        double characterWidth = Font.Measure(character);
                        if (line.Length > 0 && width + characterWidth > limit)
                        {
                            lines.Add(line.ToString());
                            line.Clear();
                            width = 0;
                        }

                        line.Append(character);
                        width += characterWidth;
                    }
                }

                lines.Add(line.ToString());
            }

            return lines;
        }

        private double X(Box box, double textWidth) => Quadding switch
        {
            PdfTextJustification.Centered => box.Left + ((box.Width - textWidth) / 2),
            PdfTextJustification.Right => box.Left + box.Width - TextPadding - textWidth,
            _ => box.Left + TextPadding,
        };

        /// <summary>The highlight behind a selected list box option, as other processors draw it (a light blue).</summary>
        private static readonly double[] ListHighlight = [0.6, 0.757, 0.855];

        /// <summary>The baseline that centres the font's ascent-to-descent box in the box's height.</summary>
        private double Baseline(Box box, double size) =>
            box.Bottom + ((box.Height - (LineFactor * size)) / 2) - (Math.Min(0, Font.Descent) * size / 1000);
    }

    /// <summary>A list box's options as shown, the selected indices, and the first option shown (decided by the first widget laid out).</summary>
    private sealed class ListContent(IReadOnlyList<string> lines, IReadOnlyList<int> selected, int requestedTop, bool scroll)
    {
        public IReadOnlyList<string> Lines { get; } = lines;

        public IReadOnlyList<int> Selected { get; } = selected;

        public int RequestedTop { get; } = requestedTop;

        public bool Scroll { get; } = scroll;

        public int? Top { get; set; }
    }
}
