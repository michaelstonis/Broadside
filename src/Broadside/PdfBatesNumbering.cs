using System.Globalization;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside;

/// <summary>
/// A Bates numbering run for <see cref="PdfDocument.AddBatesNumbers(PdfBatesNumbering, Range?)"/>: a prefix, a zero-padded number and
/// a suffix, such as <c>ACME000123</c>, stamped on each page, with a counter that carries from one document to the next.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.2.2.2 (Table 363, NOTE 3): each number is a <c>Pagination</c> artifact with the PDF 2.0 <c>Bates</c> subtype in a
/// PDF 2.0 document and the <c>Footer</c> subtype otherwise, so numbering never raises a document's version by itself.
/// Placement is that of a <see cref="PdfStamp"/> with <see cref="PdfStamp.FollowPageRotation"/>, aligned in <see cref="Boundary"/>
/// (§14.11.2) and inset by <see cref="Margin"/> from the edges it is aligned to.
/// </para>
/// <para>
/// A font belongs to one document. A Standard 14 font (<see cref="PdfDocument.GetStandard14Font"/>) of any document serves every
/// document: the numbered document's own instance of the same font is used. An embedded font serves only its document; set
/// <see cref="Font"/> to the next document's font before numbering it.
/// </para>
/// <para>
/// Not thread-safe: <see cref="PdfDocument.AddBatesNumbers(PdfBatesNumbering, Range?)"/> advances <see cref="NextNumber"/>. To number
/// documents in parallel, give each its own numbering whose <see cref="NextNumber"/> is the first number plus the pages of the
/// documents before it, computed from the page counts up front.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var bates = new PdfBatesNumbering(font, 9).WithPrefix("ACME").WithDigits(6);
/// foreach (string path in files)
/// {
///     using PdfDocument document = PdfDocument.Open(path);
///     PdfBatesRange range = document.AddBatesNumbers(bates); // bates.NextNumber advances
///     document.Save(Output(path));
///     log.Add((path, range.First, range.Last));
/// }
/// </code>
/// </example>
public sealed class PdfBatesNumbering
{
    private const int MaxDigits = 18;

    private PdfCanvasFont _font;
    private double _fontSize;
    private string _prefix = string.Empty;
    private string _suffix = string.Empty;
    private long _nextNumber = 1;
    private int _digits = 6;
    private PdfStampAlignment _horizontal = PdfStampAlignment.End;
    private PdfStampAlignment _vertical = PdfStampAlignment.End;
    private double _margin = 18;
    private PdfColor _textColor = PdfColor.FromGray(0);
    private PdfPageBoundary _boundary = PdfPageBoundary.CropBox;

    /// <summary>Initializes a new instance of the <see cref="PdfBatesNumbering"/> class.</summary>
    /// <param name="font">The font the numbers are shown in: a Standard 14 font, or an embedded font of the document to number.</param>
    /// <param name="fontSize">The font size, in points.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fontSize"/> is not a positive finite number.</exception>
    /// <remarks>ISO 32000-2 §9.2 to §9.4.</remarks>
    public PdfBatesNumbering(PdfCanvasFont font, double fontSize)
    {
        ArgumentNullException.ThrowIfNull(font);
        _font = font;
        _fontSize = Positive(fontSize, nameof(fontSize));
    }

    /// <summary>Gets or sets the font the numbers are shown in.</summary>
    /// <remarks>ISO 32000-2 §9.2 to §9.4. A Standard 14 font serves every document; an embedded font only its own.</remarks>
    public PdfCanvasFont Font
    {
        get => _font;
        set => _font = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the font size, in points.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    /// <remarks>ISO 32000-2 §9.3.</remarks>
    public double FontSize
    {
        get => _fontSize;
        set => _fontSize = Positive(value);
    }

    /// <summary>Gets or sets the text before the number; empty by default.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.2. Shown as given: braces are not tokens here.</remarks>
    public string Prefix
    {
        get => _prefix;
        set => _prefix = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the text after the number; empty by default.</summary>
    /// <remarks>ISO 32000-2 §14.8.2.2.2. Shown as given: braces are not tokens here.</remarks>
    public string Suffix
    {
        get => _suffix;
        set => _suffix = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the number the next numbered page gets; 1 by default. Each numbering call advances it past the pages it numbered.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2.2.</remarks>
    public long NextNumber
    {
        get => _nextNumber;
        set => _nextNumber = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A Bates number shall not be negative.");
    }

    /// <summary>Gets or sets how many digits the number has, zero padded; 6 by default, from 1 to 18.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 18.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2.2.</remarks>
    public int Digits
    {
        get => _digits;
        set => _digits = value is >= 1 and <= MaxDigits
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "A Bates number shall have from 1 to 18 digits.");
    }

    /// <summary>Gets or sets where the number goes across the box, as the page is viewed: the left edge, the center or the right edge (the default).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined alignment.</exception>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfStampAlignment HorizontalAlignment
    {
        get => _horizontal;
        set => _horizontal = Defined(value);
    }

    /// <summary>
    /// Gets or sets where the number goes down the box, as the page is viewed: the top edge (<see cref="PdfStampAlignment.Start"/>),
    /// the center or the bottom edge (<see cref="PdfStampAlignment.End"/>, the default), as for <see cref="PdfStamp.VerticalAlignment"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined alignment.</exception>
    /// <remarks>ISO 32000-2 §14.11.2 and §14.8.2.2.2: a number at the top or bottom is attached to that edge.</remarks>
    public PdfStampAlignment VerticalAlignment
    {
        get => _vertical;
        set => _vertical = Defined(value);
    }

    /// <summary>Gets or sets the distance, in points, from the edges the number is aligned to; 18 by default. Not applied along a centered direction.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public double Margin
    {
        get => _margin;
        set => _margin = double.IsFinite(value) && value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "The margin shall be a finite number, zero or more.");
    }

    /// <summary>Gets or sets the color the numbers are filled with; black by default.</summary>
    /// <remarks>ISO 32000-2 §8.6. Its space shall belong to the numbered document or be a device space.</remarks>
    public PdfColor TextColor
    {
        get => _textColor;
        set => _textColor = value;
    }

    /// <summary>Gets or sets the page boundary the number is aligned in. The default is <see cref="PdfPageBoundary.CropBox"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined boundary.</exception>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfPageBoundary Boundary
    {
        get => _boundary;
        set => _boundary = Defined(value);
    }

    /// <summary>The largest number <see cref="Digits"/> digits hold.</summary>
    private long MaxNumber
    {
        get
        {
            long max = 9;
            for (int digit = 1; digit < _digits; digit++)
            {
                max = (max * 10) + 9;
            }

            return max;
        }
    }

    /// <summary>Spells <paramref name="number"/> as it is stamped: <see cref="Prefix"/>, the number zero padded to <see cref="Digits"/>, <see cref="Suffix"/>.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The Bates number, such as <c>ACME000123</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number"/> is negative or needs more than <see cref="Digits"/> digits.</exception>
    /// <remarks>ISO 32000-2 §14.8.2.2.2.</remarks>
    public string Format(long number)
    {
        if (number < 0 || number > MaxNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, $"A Bates number shall be from 0 to {MaxNumber.ToString(CultureInfo.InvariantCulture)} with {_digits} digits.");
        }

        return string.Concat(_prefix, number.ToString(CultureInfo.InvariantCulture).PadLeft(_digits, '0'), _suffix);
    }

    /// <summary>Sets <see cref="Font"/>.</summary>
    /// <param name="font">The font.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §9.2 to §9.4.</remarks>
    public PdfBatesNumbering WithFont(PdfCanvasFont font)
    {
        Font = font;
        return this;
    }

    /// <summary>Sets <see cref="FontSize"/>.</summary>
    /// <param name="fontSize">The font size, in points.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §9.3.</remarks>
    public PdfBatesNumbering WithFontSize(double fontSize)
    {
        FontSize = fontSize;
        return this;
    }

    /// <summary>Sets <see cref="Prefix"/>.</summary>
    /// <param name="prefix">The text before the number.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2.2.</remarks>
    public PdfBatesNumbering WithPrefix(string prefix)
    {
        Prefix = prefix;
        return this;
    }

    /// <summary>Sets <see cref="Suffix"/>.</summary>
    /// <param name="suffix">The text after the number.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2.2.</remarks>
    public PdfBatesNumbering WithSuffix(string suffix)
    {
        Suffix = suffix;
        return this;
    }

    /// <summary>Sets <see cref="NextNumber"/>.</summary>
    /// <param name="number">The number the next numbered page gets.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2.2.</remarks>
    public PdfBatesNumbering WithNextNumber(long number)
    {
        NextNumber = number;
        return this;
    }

    /// <summary>Sets <see cref="Digits"/>.</summary>
    /// <param name="digits">The number of digits, from 1 to 18.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §14.8.2.2.2.</remarks>
    public PdfBatesNumbering WithDigits(int digits)
    {
        Digits = digits;
        return this;
    }

    /// <summary>Sets <see cref="HorizontalAlignment"/>.</summary>
    /// <param name="alignment">The alignment.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfBatesNumbering WithHorizontalAlignment(PdfStampAlignment alignment)
    {
        HorizontalAlignment = alignment;
        return this;
    }

    /// <summary>Sets <see cref="VerticalAlignment"/>.</summary>
    /// <param name="alignment">The alignment.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfBatesNumbering WithVerticalAlignment(PdfStampAlignment alignment)
    {
        VerticalAlignment = alignment;
        return this;
    }

    /// <summary>Sets <see cref="Margin"/>.</summary>
    /// <param name="margin">The margin, in points.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfBatesNumbering WithMargin(double margin)
    {
        Margin = margin;
        return this;
    }

    /// <summary>Sets <see cref="TextColor"/>.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §8.6.</remarks>
    public PdfBatesNumbering WithTextColor(PdfColor color)
    {
        TextColor = color;
        return this;
    }

    /// <summary>Sets <see cref="Boundary"/>.</summary>
    /// <param name="boundary">The boundary.</param>
    /// <returns>This numbering.</returns>
    /// <remarks>ISO 32000-2 §14.11.2.</remarks>
    public PdfBatesNumbering WithBoundary(PdfPageBoundary boundary)
    {
        Boundary = boundary;
        return this;
    }

    /// <summary>Numbers <paramref name="count"/> pages of <paramref name="document"/> from <paramref name="first"/> and advances the counter.</summary>
    internal PdfBatesRange Apply(PdfDocument document, int first, int count, string parameter)
    {
        long start = _nextNumber;
        if (count == 0)
        {
            return new PdfBatesRange(start, start - 1, string.Empty, string.Empty);
        }

        long max = MaxNumber;
        if (start > max - (count - 1))
        {
            decimal last = (decimal)start + count - 1;
            throw new InvalidOperationException(
                $"The Bates number {last.ToString(CultureInfo.InvariantCulture)} needs more than {_digits} digits; no page changed.");
        }

        // Checked on the caller's font before the document's own Standard 14 instance is made, so a refusal adds nothing.
        string fixedText = string.Concat(_prefix, "0123456789", _suffix);
        if (!_font.CanEncode(fixedText))
        {
            throw new ArgumentException($"The font {_font.Name} cannot show the Bates prefix \"{_prefix}\", suffix \"{_suffix}\" or digits.", parameter);
        }

        PdfCanvasFont font = FontFor(document, parameter);
        var texts = new string[count];
        for (int index = 0; index < count; index++)
        {
            texts[index] = Format(start + index);
        }

        PdfStamp stamp = PdfStamp.FromText(string.Empty, font, _fontSize)
            .WithHorizontalAlignment(_horizontal)
            .WithVerticalAlignment(_vertical)
            .WithOffsetX(_horizontal switch { PdfStampAlignment.Start => _margin, PdfStampAlignment.End => -_margin, _ => 0 })
            .WithOffsetY(_vertical switch { PdfStampAlignment.Start => -_margin, PdfStampAlignment.End => _margin, _ => 0 })
            .WithTextColor(_textColor)
            .WithBoundary(_boundary)
            .WithFollowPageRotation()
            .WithArtifactType(Structure.PdfArtifactType.Pagination)
            .WithArtifactSubtype(document.Version >= new PdfVersion(2, 0) ? "Bates" : "Footer");
        Structure.PdfArtifactEdges edges = _vertical switch
        {
            PdfStampAlignment.Start => Structure.PdfArtifactEdges.Top,
            PdfStampAlignment.End => Structure.PdfArtifactEdges.Bottom,
            _ => Structure.PdfArtifactEdges.None,
        };

        StampWriter.StampTexts(document, stamp, first, texts, edges, parameter);
        long end = start + count - 1;
        _nextNumber = end + 1;
        return new PdfBatesRange(start, end, texts[0], texts[^1]);
    }

    /// <summary>The font for <paramref name="document"/>: the caller's, or the document's own instance of the same Standard 14 font.</summary>
    private PdfCanvasFont FontFor(PdfDocument document, string parameter)
    {
        if (ReferenceEquals(_font.Document, document))
        {
            return _font;
        }

        if (_font is Standard14CanvasFont standard)
        {
            return document.GetStandard14Font(standard.Standard14);
        }

        throw new ArgumentException(
            $"The font {_font.Name} is embedded in another document; set the numbering's Font to a font of this document.", parameter);
    }

    private static T Defined<T>(T value)
        where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, $"Not a defined {typeof(T).Name}.");

    private static double Positive(double value, string name = "value") =>
        double.IsFinite(value) && value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The value shall be a positive finite number.");
}
