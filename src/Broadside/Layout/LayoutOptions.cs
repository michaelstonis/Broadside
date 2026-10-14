using Broadside.Content;

namespace Broadside.Layout;

/// <summary>Options for building a layout tree.</summary>
/// <remarks>ISO 32000-2 §14.8 and §8.11.3.1. Artifacts are never part of the tree (§14.8.2.2).</remarks>
public sealed class LayoutOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether content hidden by optional content is part of the tree. Default <see langword="false"/>:
    /// the tree holds what a viewer shows.
    /// </summary>
    /// <remarks>ISO 32000-2 §8.11.3.1.</remarks>
    public bool IncludeHiddenContent { get; set; }

    /// <summary>Gets or sets the options for the content runs (limits, optional-content state, cancellation); <see langword="null"/>, the default, for the interpreter's defaults.</summary>
    public ContentOptions? ContentOptions { get; set; }

    /// <summary>Sets whether content hidden by optional content is part of the tree.</summary>
    /// <param name="include">Whether to include it.</param>
    /// <returns>These options.</returns>
    public LayoutOptions WithHiddenContent(bool include = true)
    {
        IncludeHiddenContent = include;
        return this;
    }

    /// <summary>Sets the options for the content runs.</summary>
    /// <param name="options">The content run's options, or <see langword="null"/> for the defaults.</param>
    /// <returns>These options.</returns>
    public LayoutOptions WithContentOptions(ContentOptions? options)
    {
        ContentOptions = options;
        return this;
    }
}
