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

    private readonly bool _includeHidden;
    private readonly Dictionary<PdfFont, (double Descent, double Ascent)> _extents = new(ReferenceEqualityComparer.Instance);
    private TextGlyph[] _glyphs = new TextGlyph[64];
    private char[] _text = new char[256];
    private int _glyphCount;
    private int _textLength;

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
        _includeHidden = options.IncludeHiddenContent;
    }

    /// <inheritdoc/>
    public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.MarkedContent | (_includeHidden ? ContentEvents.HiddenContent : ContentEvents.None);

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

    /// <summary>Forgets the extracted glyphs and text, keeping the buffers.</summary>
    public void Clear()
    {
        _glyphCount = 0;
        _textLength = 0;
    }

    /// <summary>Copies what was extracted into a <see cref="TextPage"/>.</summary>
    /// <returns>The glyphs and their text.</returns>
    public TextPage ToTextPage() => new(Glyphs.ToArray(), new string(Text));

    /// <inheritdoc/>
    public override void BeginRun(ContentContext context)
    {
        _extents.Clear();
        _markedDepth = 0;
        _artifactDepth = 0;
        _actualTextDepth = 0;
        _actualText = null;
    }

    /// <inheritdoc/>
    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _markedDepth++;
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
            flags |= TextGlyphFlags.Artifact;
        }

        if (_actualTextDepth != 0)
        {
            flags |= TextGlyphFlags.ActualText;
        }

        if (_glyphCount == _glyphs.Length)
        {
            Array.Resize(ref _glyphs, _glyphs.Length * 2);
        }

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
        };
        _textLength += written;
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
                return item.Kind == ContentOperandKind.String ? new CosString(item.Bytes, item.IsHexadecimal).DecodeText() : null;
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
    private static (double Descent, double Ascent) ReadExtent(PdfFont font)
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

    private void EnsureText(int free)
    {
        if (_text.Length - _textLength < free)
        {
            Array.Resize(ref _text, Math.Max(_text.Length * 2, _textLength + free));
        }
    }
}
