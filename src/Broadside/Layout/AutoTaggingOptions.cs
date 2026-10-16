namespace Broadside.Layout;

/// <summary>Options for auto-tagging a document from its detected layout tree.</summary>
/// <remarks>ISO 32000-2 §14.8 (tagged PDF), §14.7 (logical structure), §14.9.3 (alternate descriptions); spec story 38.</remarks>
public sealed class AutoTaggingOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether a document that already has a structure tree is tagged again. Default
    /// <see langword="false"/>: such a document is left alone.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §14.7.2. When forced, the old structure tree is detached from the catalog (with the pages', their annotations'
    /// and their XObjects' parent-tree keys), the page-level marked-content sequences that carried an MCID lose it (they become
    /// <c>BMC</c> with the same tag), and every page is laid out from its geometry.
    /// </remarks>
    public bool Force { get; set; }

    /// <summary>Gets or sets the natural language written to the catalog's <c>Lang</c>; <see langword="null"/>, the default, keeps the catalog's.</summary>
    /// <remarks>ISO 32000-2 §14.9.2, Table 29 (<c>Lang</c>).</remarks>
    public string? Language { get; set; }

    /// <summary>
    /// Gets or sets the placeholder alternate description given to every detected figure. Default <c>"Figure"</c>; each placeholder
    /// is reported so the caller can replace it.
    /// </summary>
    /// <remarks>ISO 32000-2 §14.9.3 (<c>Alt</c>).</remarks>
    public string FigureAlternateText { get; set; } = "Figure";

    /// <summary>Sets whether a document that already has a structure tree is tagged again.</summary>
    /// <param name="force">Whether to tag it again.</param>
    /// <returns>These options.</returns>
    public AutoTaggingOptions WithForce(bool force = true)
    {
        Force = force;
        return this;
    }

    /// <summary>Sets the natural language written to the catalog.</summary>
    /// <param name="language">A language identifier (BCP 47), or <see langword="null"/> to keep the catalog's.</param>
    /// <returns>These options.</returns>
    public AutoTaggingOptions WithLanguage(string? language)
    {
        Language = language;
        return this;
    }

    /// <summary>Sets the placeholder alternate description of detected figures.</summary>
    /// <param name="text">The placeholder; not empty.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty or white space.</exception>
    public AutoTaggingOptions WithFigureAlternateText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        FigureAlternateText = text;
        return this;
    }
}
