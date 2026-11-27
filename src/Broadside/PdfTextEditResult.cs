using Broadside.Diagnostics;

namespace Broadside;

/// <summary>What replacing or deleting text changed, and the diagnostics it recorded.</summary>
/// <remarks>
/// ISO 32000-2 §9.4.3 and §14.9.4. The old text stays in the original bytes of the file until the document is saved as a full
/// rewrite: an incremental update (§7.5.6) keeps the earlier revision.
/// </remarks>
public sealed class PdfTextEditResult
{
    internal PdfTextEditResult(int replaced, int usedFallbackFont, IReadOnlyList<Diagnostic> diagnostics)
    {
        Replaced = replaced;
        UsedFallbackFont = usedFallbackFont;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the number of matches replaced or deleted.</summary>
    /// <remarks>ISO 32000-2 §9.4.3.</remarks>
    public int Replaced { get; }

    /// <summary>Gets the number of replaced matches some of whose characters are shown in a fallback font.</summary>
    /// <remarks>ISO 32000-2 §9.9.2.</remarks>
    public int UsedFallbackFont { get; }

    /// <summary>Gets the <c>TextEditDiagnostics</c> recorded.</summary>
    /// <remarks>ISO 32000-2 §9.10.3 and §14.9.4.</remarks>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}
