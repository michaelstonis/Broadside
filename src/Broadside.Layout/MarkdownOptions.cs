namespace Broadside.Layout.Flow;

/// <summary>
/// Options for converting Markdown with the document builder (<see cref="ContainerBuilder.Markdown"/>,
/// <see cref="PdfDocumentMarkdownExtensions"/>). Immutable: each <c>With*</c> member returns a changed copy, so one instance is safe to
/// share between threads.
/// </summary>
/// <remarks>
/// CommonMark 0.31 with the GitHub Flavored Markdown table, strikethrough, autolink and task list extensions, written as the structure
/// types of ISO 32000-2 §14.8.4 with the alternate descriptions of §14.9.3. Images are never fetched from the network: only
/// <see cref="ImageResolver"/> or a file under <see cref="BaseDirectory"/> supplies one.
/// </remarks>
public sealed class MarkdownOptions
{
    /// <summary>Initializes a new instance of the <see cref="MarkdownOptions"/> class with the defaults: GitHub extensions on, no images resolved.</summary>
    public MarkdownOptions()
    {
    }

    private MarkdownOptions(MarkdownOptions other)
    {
        BaseDirectory = other.BaseDirectory;
        ImageResolver = other.ImageResolver;
        GitHubExtensions = other.GitHubExtensions;
    }

    /// <summary>Gets the default options.</summary>
    internal static MarkdownOptions Default { get; } = new();

    /// <summary>
    /// Gets the directory relative image paths are read from, or null (the default) to read no image files. A path that leaves the
    /// directory (<c>../secret.png</c>, an absolute path) is refused.
    /// </summary>
    public string? BaseDirectory { get; private init; }

    /// <summary>
    /// Gets the function that supplies the image for a destination as written in the Markdown (percent-escapes decoded), or null for
    /// none; it is asked first, and a null answer falls back to <see cref="BaseDirectory"/>.
    /// </summary>
    public Func<string, ImageSource?>? ImageResolver { get; private init; }

    /// <summary>
    /// Gets a value indicating whether the GitHub Flavored Markdown extensions are recognized: tables, <c>~~strikethrough~~</c>,
    /// autolinks without angle brackets and task list items. Default <see langword="true"/>.
    /// </summary>
    public bool GitHubExtensions { get; private init; } = true;

    /// <summary>Returns a copy that reads relative image paths from <paramref name="directory"/>.</summary>
    /// <param name="directory">The directory, or null to read no image files.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is empty.</exception>
    public MarkdownOptions WithBaseDirectory(string? directory)
    {
        if (directory is { Length: 0 })
        {
            throw new ArgumentException("The base directory shall not be empty; pass null for none.", nameof(directory));
        }

        return new(this) { BaseDirectory = directory };
    }

    /// <summary>Returns a copy that asks <paramref name="resolver"/> for each image.</summary>
    /// <param name="resolver">Supplies the image for a destination, or null when it has none; null for no resolver.</param>
    /// <returns>The changed copy.</returns>
    public MarkdownOptions WithImageResolver(Func<string, ImageSource?>? resolver) => new(this) { ImageResolver = resolver };

    /// <summary>Returns a copy that recognizes, or does not recognize, the GitHub Flavored Markdown extensions.</summary>
    /// <param name="enabled">Whether to recognize them.</param>
    /// <returns>The changed copy.</returns>
    public MarkdownOptions WithGitHubExtensions(bool enabled) => new(this) { GitHubExtensions = enabled };
}
