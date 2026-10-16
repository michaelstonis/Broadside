using Broadside.Diagnostics;
using Broadside.Structure;

namespace Broadside.Layout;

/// <summary>What auto-tagging did: whether the document was tagged, the layout tree written, and what needs the caller's attention.</summary>
/// <remarks>ISO 32000-2 §14.8; spec story 38.</remarks>
public sealed class AutoTaggingResult
{
    internal AutoTaggingResult(bool isTagged, LayoutTree? layout, IReadOnlyList<PdfStructureElement> figures, IReadOnlyList<Diagnostic> diagnostics)
    {
        IsTagged = isTagged;
        Layout = layout;
        Figures = figures;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets a value indicating whether the document was tagged; <see langword="false"/> when it already had a structure tree and tagging was not forced.</summary>
    public bool IsTagged { get; }

    /// <summary>Gets the detected layout tree that was written as the structure tree, or <see langword="null"/> when nothing was tagged.</summary>
    public LayoutTree? Layout { get; }

    /// <summary>Gets the <c>Figure</c> elements written, in reading order, each with the placeholder alternate description to replace.</summary>
    /// <remarks>ISO 32000-2 §14.9.3.</remarks>
    public IReadOnlyList<PdfStructureElement> Figures { get; }

    /// <summary>Gets what auto-tagging reports: a <c>FigureAlternateTextPlaceholder</c> warning per figure, with the element's reference.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
