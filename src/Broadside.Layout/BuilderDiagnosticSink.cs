using System.Runtime.CompilerServices;
using Broadside.Diagnostics;

namespace Broadside.Layout.Flow;

/// <summary>Where the document builder's diagnostics and the layout's go: the document's own diagnostics.</summary>
internal static class BuilderDiagnosticSink
{
    private static readonly ConditionalWeakTable<PdfDocument, List<Diagnostic>> Pending = [];

    /// <summary>Records <paramref name="diagnostics"/> against <paramref name="document"/>.</summary>
    public static void Report(PdfDocument document, IReadOnlyList<Diagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        // Until layout diagnostics can land on PdfDocument.Diagnostics (the core addition C2 of #397), they are kept beside the document.
        Pending.GetOrCreateValue(document).AddRange(diagnostics);
    }

    /// <summary>What has been recorded against <paramref name="document"/>.</summary>
    public static IReadOnlyList<Diagnostic> Of(PdfDocument document) => Pending.TryGetValue(document, out List<Diagnostic>? list) ? list : [];
}
