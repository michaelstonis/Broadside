namespace Broadside.Text;

/// <summary>How <see cref="PdfPage.AddTextLayer(OcrPage, TextLayerOptions?)"/> writes an OCR text layer. Immutable: each <c>With*</c> method returns a changed copy.</summary>
/// <remarks>ISO 32000-2 §9.3.6, §14.8.2.4 and §14.7.5.</remarks>
public sealed class TextLayerOptions
{
    /// <summary>Initializes a new instance of the <see cref="TextLayerOptions"/> class with the defaults: every word, over the crop box, untagged.</summary>
    public TextLayerOptions()
    {
    }

    private TextLayerOptions(TextLayerOptions source)
    {
        MinConfidence = source.MinConfidence;
        Target = source.Target;
        Tag = source.Tag;
    }

    /// <summary>Gets the lowest <see cref="OcrWord.Confidence"/> a word needs to be written, from 0 to 1; 0 by default (every word).</summary>
    public double MinConfidence { get; private init; }

    /// <summary>
    /// Gets the rectangle of default user space the source image covers, or <see langword="null"/> (the default) for the page's
    /// crop box. The image's top-left corner is the rectangle's corner that is top-left as the page is displayed (after its
    /// <see cref="PdfPage.Rotation"/>).
    /// </summary>
    /// <remarks>ISO 32000-2 §14.11.2 (page boundaries) and §7.7.3.3, Table 31 (<c>Rotate</c>).</remarks>
    public PdfRectangle? Target { get; private init; }

    /// <summary>
    /// Gets a value indicating whether each line becomes a <c>P</c> structure element with its language, in a structure tree created
    /// when the document has none; <see langword="false"/> by default (plain content, which <see cref="PdfDocument.AutoTag()"/> can tag later).
    /// </summary>
    /// <remarks>ISO 32000-2 §14.7.5 (structure content) and §14.8.4.4 (<c>P</c>).</remarks>
    public bool Tag { get; private init; }

    /// <summary>Returns a copy whose <see cref="MinConfidence"/> is <paramref name="confidence"/>.</summary>
    /// <param name="confidence">The lowest confidence written, from 0 to 1.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="confidence"/> is not between 0 and 1.</exception>
    public TextLayerOptions WithMinConfidence(double confidence)
    {
        if (!(confidence is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "The confidence shall be between 0 and 1.");
        }

        return new TextLayerOptions(this) { MinConfidence = confidence };
    }

    /// <summary>Returns a copy whose <see cref="Target"/> is <paramref name="area"/>.</summary>
    /// <param name="area">The rectangle of default user space the source image covers.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="area"/> has no area or a coordinate that is not finite.</exception>
    public TextLayerOptions WithTarget(PdfRectangle area)
    {
        if (!double.IsFinite(area.Left) || !double.IsFinite(area.Right) || !double.IsFinite(area.Bottom) || !double.IsFinite(area.Top)
            || !(area.Width > 0) || !(area.Height > 0))
        {
            throw new ArgumentException("The target shall be a finite rectangle with a positive width and height.", nameof(area));
        }

        return new TextLayerOptions(this) { Target = area };
    }

    /// <summary>Returns a copy whose <see cref="Tag"/> is <paramref name="tag"/>.</summary>
    /// <param name="tag">Whether each line becomes a <c>P</c> structure element.</param>
    /// <returns>The changed copy.</returns>
    public TextLayerOptions WithTagging(bool tag) => new(this) { Tag = tag };
}
