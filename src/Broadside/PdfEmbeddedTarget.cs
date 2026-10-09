using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>One step of an embedded go-to action's path to its target document: to the parent, or to a child embedded file. A live view over a target dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.4, Table 205. A child is found either by its name in the <c>EmbeddedFiles</c> name tree
/// (<see cref="EmbeddedFileName"/>) or through a file attachment annotation (<see cref="PageNumber"/> or <see cref="PageName"/>, with
/// <see cref="AnnotationIndex"/> or <see cref="AnnotationName"/>). "It is an error for a target dictionary to have an infinite cycle":
/// <see cref="Target"/> refuses a step that leads back to a dictionary already on the path, with an <c>ActionTargetCycle</c> diagnostic.
/// </remarks>
public sealed class PdfEmbeddedTarget
{
    private const int MaxDepth = 64;

    private static readonly CosName P = new("P");
    private static readonly CosName A = new("A");

    private readonly PdfDocument _document;
    private readonly CosReference? _owner;
    private readonly CosDictionary[] _path;

    internal PdfEmbeddedTarget(PdfDocument document, CosDictionary dictionary, CosReference? owner, CosDictionary[] path)
    {
        _document = document;
        _owner = owner;
        _path = path;
        Dictionary = dictionary;
    }

    /// <summary>Gets the target dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 205.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the relationship of the target to the current document (<c>R</c>, required).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 205: <c>P</c> parent, <c>C</c> child; anything else reads as <see cref="PdfEmbeddedTargetRelationship.Unknown"/>.</remarks>
    public PdfEmbeddedTargetRelationship Relationship => (Get(ActionNames.R) as CosName)?.Value switch
    {
        "P" => PdfEmbeddedTargetRelationship.Parent,
        "C" => PdfEmbeddedTargetRelationship.Child,
        _ => PdfEmbeddedTargetRelationship.Unknown,
    };

    /// <summary>Gets the name of the child in the current document's <c>EmbeddedFiles</c> name tree (<c>N</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 205: a byte string.</remarks>
    public CosString? EmbeddedFileName => Get(ActionNames.N) as CosString;

    /// <summary>Gets the 0-based page number of the file attachment annotation holding the child (<c>P</c> as an integer), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 205.</remarks>
    public int? PageNumber => Get(P) is CosInteger { Value: >= 0 and <= int.MaxValue } page ? (int)page.Value : null;

    /// <summary>Gets the named destination giving the page of the file attachment annotation (<c>P</c> as a string), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 205.</remarks>
    public CosString? PageName => Get(P) as CosString;

    /// <summary>Gets the 0-based index of the file attachment annotation in the page's <c>Annots</c> (<c>A</c> as an integer), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 205.</remarks>
    public int? AnnotationIndex => Get(A) is CosInteger { Value: >= 0 and <= int.MaxValue } index ? (int)index.Value : null;

    /// <summary>Gets the <c>NM</c> name of the file attachment annotation (<c>A</c> as a text string), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 205.</remarks>
    public string? AnnotationName => (Get(A) as CosString)?.DecodeText();

    /// <summary>Gets the next step of the path (<c>T</c>), or <see langword="null"/> when this step reaches the target document.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.4, Table 205. A step leading back to a dictionary on the path is refused with an <c>ActionTargetCycle</c> diagnostic.</remarks>
    public PdfEmbeddedTarget? Target
    {
        get
        {
            if (Get(ActionNames.T) is not CosDictionary next)
            {
                return null;
            }

            if (ReferenceEquals(next, Dictionary) || Array.Exists(_path, step => ReferenceEquals(step, next)) || _path.Length >= MaxDepth)
            {
                _document.DiagnosticSink.Report(
                    DiagnosticCodes.ActionTargetCycle,
                    DiagnosticSeverity.Warning,
                    "An embedded go-to action's target dictionaries form a cycle (or nest too deeply); the action shall not be performed.",
                    objectReference: _owner);
                return null;
            }

            return new PdfEmbeddedTarget(_document, next, _owner, [.. _path, Dictionary]);
        }
    }

    private CosObject? Get(CosName key) => Dictionary.TryGetValue(key, out CosObject? value) ? _document.Resolve(value) : null;
}
