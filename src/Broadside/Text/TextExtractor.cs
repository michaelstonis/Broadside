using Broadside.Content;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Text;

/// <summary>
/// The content processor that extracts positioned text: every glyph a run shows, with its Unicode text, its box and origin in page
/// space, its font, size and rendering mode. Runs alone through <see cref="PdfPage.ExtractText()"/>, or beside other processors in
/// one run through <see cref="CompositeContentProcessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.4.4 (glyph positions), §9.10 (Unicode), §9.3.6 (rendering modes), §8.11.3.1 (optional content) and §14.9.4
/// (replacement text). Glyphs shown by form XObjects are extracted where the form paints them. A Type 3 glyph is one glyph, mapped
/// to Unicode like any other; its glyph description is not run, so text inside it is part of its appearance, not of the page's text.
/// Glyphs in <c>Artifact</c> marked-content sequences (§14.8.2.2) are left out unless <see cref="TextExtractionOptions.IncludeArtifacts"/>
/// asks for them.
/// </para>
/// <para>
/// The extractor gives page content order. <see cref="PdfPage.ExtractText(TextExtractionOptions)"/> puts a tagged page in logical
/// content order from the structure tree (§14.8.2.5.1, <see cref="TextReadingOrder"/>); the extractor records, for that, the
/// content item each glyph is shown in. <see cref="Words"/>, <see cref="Lines"/> and <see cref="PlainText"/> group the glyphs in
/// content order (§14.8.2.6.2; see <see cref="TextWord"/> and <see cref="TextLine"/>), assembled when first read after a run.
/// </para>
/// <para>
/// Glyphs accumulate over runs until <see cref="Clear"/>; the buffers are kept, so an extractor reused for page after page
/// allocates nothing per glyph once its buffers have grown. Not thread-safe: use one extractor per thread.
/// </para>
/// </remarks>
public sealed class TextExtractor : ContentProcessor
{
    private const double DefaultAscent = 0.8;
    private const double DefaultDescent = -0.2;

    /// <summary>
    /// The most glyphs an extractor <see cref="Return"/>ed to its thread may hold room for: a larger one is dropped, so a thread keeps
    /// at most about 7 MB for <see cref="PdfPage.ExtractText()"/>, not whatever an unusually dense page needed.
    /// </summary>
    internal const int SharedGlyphLimit = 1 << 15;

    /// <summary>
    /// The extractor <see cref="PdfPage.ExtractText()"/> reuses on this thread (issue #284), so extracting page after page grows its
    /// buffers once instead of on every page; <see langword="null"/> while it is rented.
    /// </summary>
    [ThreadStatic]
    private static TextExtractor? _shared;

    private bool _includeHidden;
    private bool _includeArtifacts;
    private readonly Dictionary<PdfFont, (double Descent, double Ascent)> _extents = new(ReferenceEqualityComparer.Instance);

    /// <summary>The words, lines and plain text of the glyphs, assembled on demand.</summary>
    private readonly TextLayout _layout = new();
    private TextGlyph[] _glyphs = new TextGlyph[64];
    private char[] _text = new char[256];
    private int _glyphCount;
    private int _textLength;

    /// <summary>For each glyph, the index in <see cref="_items"/> of the content item it is shown in, -1 for none.</summary>
    private int[] _glyphItems = new int[64];

    /// <summary>The content items glyphs were shown in: marked-content sequences with an MCID, and forms with a <c>StructParent</c>.</summary>
    private ContentItemKey[] _items = new ContentItemKey[16];
    private int _itemCount;

    /// <summary>The content items open in the run, innermost last.</summary>
    private OpenItem[] _openItems = new OpenItem[8];
    private int _openItemCount;

    /// <summary>The form XObjects open in the run.</summary>
    private int _formDepth;

    /// <summary>Whether <see cref="_layout"/> describes the glyphs as they are now.</summary>
    private bool _layoutCurrent;

    /// <summary>The marked-content sequences open in the run, counted here so a form's sequences nest in the page's.</summary>
    private int _markedDepth;

    /// <summary>The depth of the outermost open <c>Artifact</c> sequence, 0 for none.</summary>
    private int _artifactDepth;

    /// <summary>The depth of the outermost open sequence with <c>ActualText</c>, 0 for none.</summary>
    private int _actualTextDepth;

    /// <summary>The replacement text of that sequence.</summary>
    private string? _actualText;

    /// <summary>The first glyph shown inside that sequence.</summary>
    private int _actualTextGlyphStart;

    /// <summary>Creates an extractor with the default options.</summary>
    public TextExtractor()
        : this(new TextExtractionOptions())
    {
    }

    /// <summary>Creates an extractor.</summary>
    /// <param name="options">What to extract; read once here.</param>
    public TextExtractor(TextExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Configure(options);
    }

    /// <inheritdoc/>
    public override ContentEvents Events =>
        ContentEvents.Glyphs | ContentEvents.MarkedContent | ContentEvents.Forms | (_includeHidden ? ContentEvents.HiddenContent : ContentEvents.None);

    /// <summary>Gets the glyphs extracted since the last <see cref="Clear"/>, in content order. Valid until the extractor next runs or is cleared.</summary>
    /// <remarks>ISO 32000-2 §9.4.3.</remarks>
    public ReadOnlySpan<TextGlyph> Glyphs => _glyphs.AsSpan(0, _glyphCount);

    /// <summary>Gets the text of the extracted glyphs, in content order. Valid until the extractor next runs or is cleared.</summary>
    /// <remarks>ISO 32000-2 §9.10.</remarks>
    public ReadOnlySpan<char> Text => _text.AsSpan(0, _textLength);

    /// <summary>Gets the text of one extracted glyph. Valid until the extractor next runs or is cleared.</summary>
    /// <param name="glyph">A glyph of <see cref="Glyphs"/>.</param>
    /// <returns>Its UTF-16 units in <see cref="Text"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The glyph's range is not within <see cref="Text"/>.</exception>
    /// <remarks>ISO 32000-2 §9.10.2 and §14.9.4.</remarks>
    public ReadOnlySpan<char> GetGlyphText(in TextGlyph glyph) => Text.Slice(glyph.TextStart, glyph.TextLength);

    /// <summary>Gets the words of the extracted glyphs, assembled when first read after a run. Valid until the extractor next runs or is cleared.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.6.2 and §14.9.4; <see cref="TextWord"/> gives the rules.</remarks>
    public ReadOnlySpan<TextWord> Words => Assemble().Words;

    /// <summary>Gets the lines of the extracted glyphs, assembled when first read after a run. Valid until the extractor next runs or is cleared.</summary>
    /// <remarks>ISO 32000-2 §9.4.4 and §9.7.4.3; <see cref="TextLine"/> gives the rules.</remarks>
    public ReadOnlySpan<TextLine> Lines => Assemble().Lines;

    /// <summary>
    /// Gets the plain text of the extracted glyphs in content order, as <see cref="TextPage.PlainText"/>. Valid until the extractor
    /// next runs or is cleared.
    /// </summary>
    /// <remarks>ISO 32000-2 §9.10 and §14.8.2.6.2.</remarks>
    public ReadOnlySpan<char> PlainText => Assemble().PlainText;

    /// <summary>Gets the text of one word: the texts of its glyphs. Valid until the extractor next runs or is cleared.</summary>
    /// <param name="word">A word of <see cref="Words"/>.</param>
    /// <returns>Its UTF-16 units in <see cref="Text"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The word's range is not within <see cref="Text"/>.</exception>
    /// <remarks>ISO 32000-2 §9.10 and §14.8.2.6.2.</remarks>
    public ReadOnlySpan<char> GetWordText(in TextWord word) => Text.Slice(word.TextStart, word.TextLength);

    /// <summary>Gets the text of one line: its words separated by single spaces. Valid until the extractor next runs or is cleared.</summary>
    /// <param name="line">A line of <see cref="Lines"/>.</param>
    /// <returns>Its UTF-16 units in <see cref="PlainText"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The line's range is not within <see cref="PlainText"/>.</exception>
    /// <remarks>ISO 32000-2 §9.10.</remarks>
    public ReadOnlySpan<char> GetLineText(in TextLine line) => PlainText.Slice(line.PlainTextStart, line.PlainTextLength);

    /// <summary>Forgets the extracted glyphs and text, keeping the buffers.</summary>
    public void Clear()
    {
        _glyphCount = 0;
        _textLength = 0;
        _itemCount = 0;
        _layoutCurrent = false;
    }

    /// <summary>Copies what was extracted into a <see cref="TextPage"/>.</summary>
    /// <returns>The glyphs and their text.</returns>
    public TextPage ToTextPage() => new(Glyphs.ToArray(), new string(Text));

    /// <summary>
    /// Takes this thread's shared extractor, empty and configured with <paramref name="options"/>, or a new one when it is in use
    /// (a nested extraction) or was dropped. Give it back with <see cref="Return"/> once its output has been copied.
    /// </summary>
    internal static TextExtractor Rent(TextExtractionOptions options)
    {
        TextExtractor? extractor = _shared;
        if (extractor is null)
        {
            return new TextExtractor(options);
        }

        _shared = null;
        extractor.Configure(options);
        return extractor;
    }

    /// <summary>
    /// Empties <paramref name="extractor"/>, dropping its references to fonts, strings and content items so it keeps no document
    /// alive, and keeps it as this thread's shared extractor unless its buffers grew past <see cref="SharedGlyphLimit"/>.
    /// </summary>
    internal static void Return(TextExtractor extractor)
    {
        Array.Clear(extractor._glyphs, 0, extractor._glyphCount);
        Array.Clear(extractor._items, 0, extractor._itemCount);
        extractor.Clear();
        extractor._extents.Clear();
        extractor._actualText = null;
        if (extractor._glyphs.Length <= SharedGlyphLimit && extractor._text.Length <= SharedGlyphLimit * 2 && extractor._items.Length <= SharedGlyphLimit)
        {
            _shared = extractor;
        }
    }

    /// <summary>The content item each glyph of <see cref="Glyphs"/> was shown in: an index into <see cref="ContentItems"/>, -1 for none.</summary>
    internal ReadOnlySpan<int> GlyphItems => _glyphItems.AsSpan(0, _glyphCount);

    /// <summary>The content items glyphs were shown in since the last <see cref="Clear"/>.</summary>
    internal ReadOnlySpan<ContentItemKey> ContentItems => _items.AsSpan(0, _itemCount);

    /// <inheritdoc/>
    public override void BeginRun(ContentContext context)
    {
        _extents.Clear();
        _markedDepth = 0;
        _artifactDepth = 0;
        _actualTextDepth = 0;
        _actualText = null;
        _openItemCount = 0;
        _formDepth = 0;
    }

    /// <inheritdoc/>
    public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
    {
        _formDepth++;
        if (form.StructParent is not null && form.Stream is { } stream)
        {
            // §14.7.5.3: a form XObject with StructParent is itself a content item; what it shows belongs to the element that holds it.
            Open(new ContentItemKey(stream, -1), -_formDepth);
        }

        return ContentVisit.Enter;
    }

    /// <inheritdoc/>
    public override void EndForm(in FormEvent form, ContentContext context)
    {
        if (_openItemCount > 0 && _openItems[_openItemCount - 1].Depth == -_formDepth)
        {
            _openItemCount--;
        }

        _formDepth--;
    }

    /// <inheritdoc/>
    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _markedDepth++;
        if (mc.Mcid is { } mcid && ((CosObject?)mc.ContentStream ?? context.Page?.Dictionary) is { } streamKey)
        {
            // §14.7.5.2: an MCID names the sequence within its content stream, the page's own content or a form's.
            Open(new ContentItemKey(streamKey, mcid), _markedDepth);
        }

        if (_artifactDepth == 0 && mc.Tag.SequenceEqual("Artifact"u8))
        {
            // §14.8.2.2: everything inside the sequence is an artifact, the content of forms it paints included.
            _artifactDepth = _markedDepth;
        }

        if (_actualTextDepth == 0 && ReadActualText(mc, context.Document) is { } replacement)
        {
            // §14.9.4: the replacement stands for everything the sequence shows, nested replacements included.
            _actualTextDepth = _markedDepth;
            _actualText = replacement;
            _actualTextGlyphStart = _glyphCount;
        }
    }

    /// <inheritdoc/>
    public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        if (_markedDepth == 0)
        {
            return;
        }

        if (_markedDepth == _actualTextDepth)
        {
            ApplyActualText();
        }

        if (_markedDepth == _artifactDepth)
        {
            _artifactDepth = 0;
        }

        if (_openItemCount > 0 && _openItems[_openItemCount - 1].Depth == _markedDepth)
        {
            _openItemCount--;
        }

        _markedDepth--;
    }

    /// <inheritdoc/>
    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        PdfFont? font = glyph.Font;
        if (font is null)
        {
            return;
        }

        EnsureText(FontUnicode.MaxLength);
        int written = glyph.MapUnicode(_text.AsSpan(_textLength), out UnicodeSource source);
        (double descent, double ascent) = GetExtent(font);
        Matrix toPage = glyph.TextMatrix * glyph.Ctm;
        TextGlyphFlags flags = TextGlyphFlags.None;
        if (glyph.RenderingMode is TextRenderingMode.Invisible or TextRenderingMode.Clip)
        {
            flags |= TextGlyphFlags.Invisible;
        }

        if (glyph.IsHidden)
        {
            flags |= TextGlyphFlags.Hidden;
        }

        if (_artifactDepth != 0)
        {
            if (!_includeArtifacts)
            {
                return;
            }

            flags |= TextGlyphFlags.Artifact;
        }

        if (_actualTextDepth != 0)
        {
            flags |= TextGlyphFlags.ActualText;
        }

        if (_glyphCount == _glyphs.Length)
        {
            Array.Resize(ref _glyphs, _glyphs.Length * 2);
            Array.Resize(ref _glyphItems, _glyphs.Length);
        }

        _glyphItems[_glyphCount] = _openItemCount == 0 ? -1 : _openItems[_openItemCount - 1].Item;
        _glyphs[_glyphCount++] = new TextGlyph
        {
            Quad = Quad.FromRectangle(toPage, 0, descent, glyph.HorizontalDisplacement, ascent),
            Origin = glyph.DeviceOrigin,
            Advance = new PathPoint(glyph.DeviceAdvanceX, glyph.DeviceAdvanceY),
            Font = font,
            FontSize = context.State.FontSize,
            RenderingMode = glyph.RenderingMode,
            CharacterCode = glyph.CharacterCode,
            CodeLength = glyph.CodeLength,
            UnicodeSource = source,
            TextStart = _textLength,
            TextLength = written,
            Adjustment = glyph.Adjustment,
            IsVertical = glyph.IsVertical,
            Flags = flags,
            TextRenderingMatrix = toPage,
        };
        _textLength += written;
        _layoutCurrent = false;
    }

    /// <summary>
    /// The <c>ActualText</c> of a <c>BDC</c> property list, inline or named in the resources (§14.9.4, Table 352a); allocates the
    /// string, once per sequence that has one.
    /// </summary>
    private static string? ReadActualText(in MarkedContentEvent mc, PdfDocument document)
    {
        if (mc.Properties is { } properties)
        {
            return properties.TryGetValue(StructureNames.ActualText, out CosObject? value) && document.Resolve(value) is CosString text
                ? text.DecodeText()
                : null;
        }

        ContentOperand inline = mc.InlineProperties;
        if (inline.Kind != ContentOperandKind.Dictionary)
        {
            return null;
        }

        bool isKey = true;
        bool matched = false;
        foreach (ContentOperand item in inline.Items)
        {
            if (isKey)
            {
                matched = item.IsName("ActualText"u8);
            }
            else if (matched)
            {
                // Decoded from the operand's bytes: a CosString here would copy them, and only an optimizing JIT that proves it
                // does not escape keeps it off the heap, so what the sequence allocates would depend on the JIT tier (CI saw it).
                return item.Kind == ContentOperandKind.String ? TextStringDecoder.Decode(item.Bytes) : null;
            }

            isKey = !isKey;
        }

        return null;
    }

    /// <summary>
    /// Ends the outermost <c>ActualText</c> sequence (§14.9.4): when it showed glyphs, their text is replaced by the replacement,
    /// carried by the first of them; a sequence that showed none adds nothing, as no position stands for it.
    /// </summary>
    private void ApplyActualText()
    {
        string replacement = _actualText!;
        _actualTextDepth = 0;
        _actualText = null;
        int first = _actualTextGlyphStart;
        if (first >= _glyphCount)
        {
            return;
        }

        int start = _glyphs[first].TextStart;
        _textLength = start;
        EnsureText(replacement.Length);
        replacement.CopyTo(_text.AsSpan(start));
        _textLength = start + replacement.Length;
        _glyphs[first] = _glyphs[first] with { TextLength = replacement.Length };
        _layoutCurrent = false;
        for (int index = first + 1; index < _glyphCount; index++)
        {
            _glyphs[index] = _glyphs[index] with { TextStart = _textLength, TextLength = 0 };
        }
    }

    /// <summary>The font's extent across the baseline in text space units (before the font size): descent and ascent.</summary>
    private (double Descent, double Ascent) GetExtent(PdfFont font)
    {
        if (!_extents.TryGetValue(font, out (double Descent, double Ascent) extent))
        {
            extent = ReadExtent(font);
            _extents[font] = extent;
        }

        return extent;
    }

    /// <summary>
    /// §9.8.1 Table 120: Ascent and Descent, else the FontBBox, in glyph space (thousandths of text space); a Type 3 font's FontBBox
    /// through its FontMatrix (§9.6.4); else 0.8 and -0.2 as pdf.js and MuPDF do.
    /// </summary>
    internal static (double Descent, double Ascent) ReadExtent(PdfFont font)
    {
        if (font is PdfType3Font type3)
        {
            PdfRectangle box = type3.FontBBox;
            if (box.Height > 0)
            {
                Quad mapped = Quad.FromRectangle(type3.FontMatrix, box.Left, box.Bottom, box.Right, box.Top);
                PdfRectangle bounds = mapped.Bounds;
                if (bounds.Height > 0)
                {
                    return (bounds.Bottom, bounds.Top);
                }
            }

            return (DefaultDescent, DefaultAscent);
        }

        PdfFontDescriptor? descriptor = font is PdfType0Font composite ? composite.DescendantFont?.Descriptor : font.Descriptor;
        if (descriptor is not null)
        {
            double ascent = descriptor.Ascent;
            double descent = descriptor.Descent;
            if (descent > 0)
            {
                // A positive Descent is a common authoring slip for its magnitude.
                descent = -descent;
            }

            if (ascent > 0 && ascent > descent)
            {
                return (descent / 1000, ascent / 1000);
            }

            PdfRectangle box = descriptor.FontBBox;
            if (box.Height > 0)
            {
                return (box.Bottom / 1000, box.Top / 1000);
            }
        }

        return (DefaultDescent, DefaultAscent);
    }

    /// <summary>Reads what to extract from <paramref name="options"/>; every option the extractor keeps is set here.</summary>
    private void Configure(TextExtractionOptions options)
    {
        _includeHidden = options.IncludeHiddenContent;
        _includeArtifacts = options.IncludeArtifacts;
    }

    private TextLayout Assemble()
    {
        if (!_layoutCurrent)
        {
            _layout.Build(Glyphs, Text);
            _layoutCurrent = true;
        }

        return _layout;
    }

    private void EnsureText(int free)
    {
        if (_text.Length - _textLength < free)
        {
            Array.Resize(ref _text, Math.Max(_text.Length * 2, _textLength + free));
        }
    }

    /// <summary>Records a content item that has opened, at marked-content depth <paramref name="depth"/> (negative for a form's nesting).</summary>
    private void Open(ContentItemKey key, int depth)
    {
        if (_itemCount == _items.Length)
        {
            Array.Resize(ref _items, _items.Length * 2);
        }

        if (_openItemCount == _openItems.Length)
        {
            Array.Resize(ref _openItems, _openItems.Length * 2);
        }

        _items[_itemCount] = key;
        _openItems[_openItemCount++] = new OpenItem(_itemCount++, depth);
    }

    /// <summary>A content item that is open: its index in <see cref="_items"/> and the depth that closes it.</summary>
    private readonly record struct OpenItem(int Item, int Depth);
}
