using Broadside.Layout;

namespace Broadside;

/// <content>The layout tree (issue #186): ISO 32000-2 §14.8.4; ISO/TS 32005.</content>
public sealed partial class PdfDocument
{
    /// <summary>Builds the document's layout tree: its logical content over every page, from the structure tree when the document is tagged.</summary>
    /// <returns>The tree; a new one on every call, reading the document as it is now.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content, its fonts or the structure tree.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.8.4, §14.8.2.5 and ISO/TS 32005; see <see cref="LayoutTree"/> and <see cref="LayoutNodeKind"/>. Every page is
    /// extracted once as <see cref="PdfPage.ExtractText()"/> does (artifacts and hidden content left out), and each node is placed on
    /// the pages its content is on. Pages that are not tagged contribute no nodes, only their text.
    /// </remarks>
    public LayoutTree ExtractLayout() => ExtractLayout(new LayoutOptions());

    /// <summary>Builds the document's layout tree with options.</summary>
    /// <param name="options">What content to include and the content runs' options.</param>
    /// <returns>The tree.</returns>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the content, its fonts or the structure tree.</exception>
    /// <exception cref="OperationCanceledException">The content options' cancellation token was canceled.</exception>
    /// <remarks>ISO 32000-2 §14.8.4; ISO/TS 32005. As <see cref="ExtractLayout()"/>.</remarks>
    public LayoutTree ExtractLayout(LayoutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return StructureLayout.Build(this, Pages, options);
    }
}
