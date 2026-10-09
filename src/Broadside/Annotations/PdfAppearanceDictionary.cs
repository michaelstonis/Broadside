using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Annotations;

/// <summary>An annotation's appearance dictionary: its normal, rollover and down appearances. A live view over the <c>AP</c> dictionary.</summary>
/// <remarks>
/// ISO 32000-2 §12.5.5, Table 170. Each entry is a form XObject or a subdictionary of them keyed by appearance state
/// (<see cref="PdfAppearanceEntry"/>). <see cref="Rollover"/> and <see cref="Down"/> default to <see cref="Normal"/> when absent.
/// </remarks>
public sealed class PdfAppearanceDictionary
{
    private readonly PdfAnnotation _owner;

    internal PdfAppearanceDictionary(PdfAnnotation owner, CosDictionary dictionary)
    {
        _owner = owner;
        Dictionary = dictionary;
    }

    /// <summary>Gets the appearance dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.5.5, Table 170.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the normal appearance (<c>N</c>, required), or <see langword="null"/> when it is missing or neither a stream nor a dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.5.5, Table 170.</remarks>
    public PdfAppearanceEntry? Normal => Read(AnnotationNames.N, required: true);

    /// <summary>Gets the rollover appearance (<c>R</c>); the normal appearance when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.5, Table 170: "Default value: the value of the N entry".</remarks>
    public PdfAppearanceEntry? Rollover => Dictionary.ContainsKey(AnnotationNames.R) ? Read(AnnotationNames.R, required: false) : Normal;

    /// <summary>Gets the down appearance (<c>D</c>); the normal appearance when absent.</summary>
    /// <remarks>ISO 32000-2 §12.5.5, Table 170: "Default value: the value of the N entry".</remarks>
    public PdfAppearanceEntry? Down => Dictionary.ContainsKey(AnnotationNames.D) ? Read(AnnotationNames.D, required: false) : Normal;

    /// <summary>Gets a value indicating whether the dictionary has its own rollover entry (<c>R</c>).</summary>
    public bool HasRollover => Dictionary.ContainsKey(AnnotationNames.R);

    /// <summary>Gets a value indicating whether the dictionary has its own down entry (<c>D</c>).</summary>
    public bool HasDown => Dictionary.ContainsKey(AnnotationNames.D);

    /// <summary>Returns the entry for <paramref name="mode"/>, with the defaults of <see cref="Rollover"/> and <see cref="Down"/>.</summary>
    /// <param name="mode">Normal, rollover or down.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    /// <remarks>ISO 32000-2 §12.5.5, Table 170.</remarks>
    public PdfAppearanceEntry? GetEntry(PdfAppearanceMode mode) => mode switch
    {
        PdfAppearanceMode.Rollover => Rollover,
        PdfAppearanceMode.Down => Down,
        _ => Normal,
    };

    private PdfAppearanceEntry? Read(CosName key, bool required)
    {
        CosObject? raw = Dictionary.TryGetValue(key, out CosObject? value) ? value : null;
        switch (_owner.Document.Resolve(raw))
        {
            case CosStream or CosDictionary:
                return new PdfAppearanceEntry(_owner, raw!);
            case CosNull when !required:
                return null;
            default:
                _owner.Report(
                    DiagnosticCodes.AppearanceEntryInvalid,
                    $"The appearance dictionary's {key.Value} entry is {(raw is null ? "missing" : "neither a stream nor a dictionary")}; it gives no appearance.");
                return null;
        }
    }
}
