using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>The document's name dictionary: the name trees that map names to destinations, embedded files, scripts and more. A live view.</summary>
/// <remarks>
/// ISO 32000-2 §7.7.4, Table 32 (PDF 1.2), reached through the catalog's <c>Names</c> entry (§7.7.2, Table 29). Every entry is a name
/// tree (§7.9.6): <c>Dests</c> (PDF 1.2), <c>AP</c>, <c>JavaScript</c>, <c>Pages</c>, <c>Templates</c>, <c>IDS</c>, <c>URLS</c> (PDF 1.3),
/// <c>EmbeddedFiles</c>, <c>AlternatePresentations</c> (PDF 1.4; deprecated in PDF 2.0) and <c>Renditions</c> (PDF 1.5). Each is
/// reachable through <see cref="GetTree"/>; the typed views of their values arrive with their features.
/// </remarks>
public sealed class PdfNameDictionary
{
    private readonly PdfDocument _document;

    internal PdfNameDictionary(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the name dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.7.4, Table 32.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the tree of named destinations (string keys), or <see langword="null"/> when there is none.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.7.4, Table 32 (PDF 1.2), and §12.3.2.4: each value is a destination array, or a dictionary whose <c>D</c> entry
    /// is one. Named destinations of PDF 1.1 (name keys) are in the catalog's own <c>Dests</c> dictionary instead.
    /// </remarks>
    public PdfNameTree? Dests => GetTree(NavigationNames.Dests);

    /// <summary>Gets the tree of embedded files (string keys to file specifications), or <see langword="null"/> when there is none.</summary>
    /// <remarks>ISO 32000-2 §7.7.4, Table 32 (PDF 1.4), and §7.11.4. <see cref="PdfDocument.EmbeddedFiles"/> lists it typed.</remarks>
    public PdfNameTree? EmbeddedFiles => GetTree(FileAndLayerNames.EmbeddedFiles);

    /// <summary>Gets the tree of document-level ECMAScript actions (<c>JavaScript</c>, PDF 1.3), or <see langword="null"/> when there is none.</summary>
    /// <remarks>ISO 32000-2 §7.7.4, Table 32, and §12.6.4.17: the actions a processor runs when the document opens; the names are arbitrary. See <see cref="JavaScriptActions"/>.</remarks>
    public PdfNameTree? JavaScript => GetTree(ActionNames.JavaScript);

    /// <summary>
    /// Gets the document-level ECMAScript actions of the <c>JavaScript</c> tree, in key order. Never run. A value that is not an
    /// ECMAScript action is skipped with a diagnostic.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.7.4, Table 32, and §12.6.4.17. A fresh walk of the tree on every call.</remarks>
    public IReadOnlyList<KeyValuePair<CosString, PdfJavaScriptAction>> JavaScriptActions
    {
        get
        {
            var actions = new List<KeyValuePair<CosString, PdfJavaScriptAction>>();
            if (JavaScript is not { } tree)
            {
                return actions;
            }

            foreach (KeyValuePair<CosString, CosObject> entry in tree)
            {
                switch (PdfAction.Create(_document, entry.Value, tree.RootReference))
                {
                    case PdfJavaScriptAction script:
                        actions.Add(new(entry.Key, script));
                        break;
                    case { } other:
                        _document.DiagnosticSink.Report(
                            DiagnosticCodes.ActionEntryInvalid,
                            DiagnosticSeverity.Warning,
                            $"A value of the name dictionary's JavaScript tree shall be an ECMAScript action; it is a {other.ActionType.Value} action, skipped.",
                            objectReference: other.Reference ?? tree.RootReference);
                        break;
                }
            }

            return actions;
        }
    }

    /// <summary>Gets the name tree under <paramref name="key"/>, or <see langword="null"/> when the entry is absent.</summary>
    /// <param name="key">The entry, for example <c>EmbeddedFiles</c> or <c>JavaScript</c>.</param>
    /// <returns>The tree, read lazily; <see langword="null"/> when the entry is absent or is not a dictionary (with a diagnostic).</returns>
    /// <remarks>ISO 32000-2 §7.7.4, Table 32, and §7.9.6.</remarks>
    public PdfNameTree? GetTree(CosName key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Dictionary.TryGetValue(key, out CosObject? root) ? _document.GetNameTree(root) : null;
    }
}
