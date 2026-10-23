using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>
/// An output intent: the colour characteristics of a device or production condition the document's content is meant for, with the
/// ICC profile that describes it.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.11.5, Tables 401 and 402. Read from <see cref="PdfDocument.OutputIntents"/> (the catalog's <c>OutputIntents</c>,
/// PDF 1.4) or <see cref="PdfPage.OutputIntents"/> (a page's, PDF 2.0, which takes precedence for that page). A live view over the
/// dictionary. <c>MixingHints</c>, <c>SpectralData</c> and <c>DestOutputProfileRef</c> are kept in
/// <see cref="Dictionary"/> unread (the specification lets a processor ignore them).
/// </para>
/// <para>
/// When rendering, the output intent in effect (<see cref="GraphicsState.OutputIntent"/>) travels with every conversion
/// (<see cref="ColorConversion.OutputIntent"/>); a colour management that honours it, such as the ICC engine of Broadside.Rendering,
/// treats DeviceCMYK colours (and DeviceRGB or DeviceGray ones when the profile is of that kind) as described by
/// <see cref="DestinationProfile"/>. The managed default ignores it, as §14.11.5 allows.
/// </para>
/// </remarks>
public sealed class PdfOutputIntent
{
    private static readonly CosName SKey = new("S");
    private static readonly CosName OutputConditionKey = new("OutputCondition");
    private static readonly CosName OutputConditionIdentifierKey = new("OutputConditionIdentifier");
    private static readonly CosName RegistryNameKey = new("RegistryName");
    private static readonly CosName InfoKey = new("Info");
    private static readonly CosName DestOutputProfileKey = new("DestOutputProfile");
    private static readonly CosName IccBasedName = new("ICCBased");

    private readonly DictionaryView _view;
    private ProfileState? _profile;

    private PdfOutputIntent(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosReference? owner)
    {
        Dictionary = dictionary;
        Reference = reference;
        _view = new DictionaryView(document, dictionary, reference ?? owner);
    }

    /// <summary>Gets the output intent dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the dictionary's reference, or <see langword="null"/> when it is a direct object.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the subtype (<c>S</c>), such as <c>GTS_PDFX</c>, <c>GTS_PDFA1</c> or <c>ISO_PDFE1</c>; <see langword="null"/> when missing.</summary>
    /// <remarks>ISO 32000-2 Table 401 (required). A subtype that is not a name reads as missing with an <c>OutputIntentInvalid</c> diagnostic.</remarks>
    public string? Subtype
    {
        get
        {
            switch (_view.Get(SKey))
            {
                case null:
                    return null;
                case CosName name:
                    return name.Value;
                default:
                    _view.Report(DiagnosticCodes.OutputIntentInvalid, "The output intent's S entry shall be a name; it is ignored.");
                    return null;
            }
        }
    }

    /// <summary>Gets the human-readable name of the output condition (<c>OutputCondition</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 401 (optional).</remarks>
    public string? OutputCondition => _view.ReadText(OutputConditionKey, DiagnosticCodes.OutputIntentInvalid);

    /// <summary>Gets the identifier of the output condition (<c>OutputConditionIdentifier</c>), such as a registry name or <c>Custom</c>.</summary>
    /// <remarks>ISO 32000-2 Table 401 (required).</remarks>
    public string? OutputConditionIdentifier => _view.ReadText(OutputConditionIdentifierKey, DiagnosticCodes.OutputIntentInvalid);

    /// <summary>Gets the registry the condition is defined in (<c>RegistryName</c>), conventionally a URI, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 401 (optional).</remarks>
    public string? RegistryName => _view.ReadText(RegistryNameKey, DiagnosticCodes.OutputIntentInvalid);

    /// <summary>Gets further human-readable information on the condition (<c>Info</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 Table 401.</remarks>
    public string? Info => _view.ReadText(InfoKey, DiagnosticCodes.OutputIntentInvalid);

    /// <summary>
    /// Gets the ICC profile of the output condition (<c>DestOutputProfile</c>) as an ICCBased colour space, or <see langword="null"/>
    /// when the dictionary has none.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 Table 401: "the same format as that used in specifying an ICCBased colour space" (§8.6.5.5), so the stream is read
    /// as one, with the repairs and diagnostics of <see cref="PdfIccBasedColorSpace"/>; its alternate is the device space with as many
    /// components. A value that is not a stream reads as missing with an <c>OutputIntentInvalid</c> diagnostic. The same space is
    /// returned until the entry changes.
    /// </remarks>
    public PdfIccBasedColorSpace? DestinationProfile
    {
        get
        {
            Dictionary.TryGetValue(DestOutputProfileKey, out CosObject? entry);
            ProfileState? state = Volatile.Read(ref _profile);
            if (state is not null && ReferenceEquals(state.Entry, entry))
            {
                return state.Space;
            }

            PdfIccBasedColorSpace? space = null;
            switch (_view.Get(DestOutputProfileKey))
            {
                case null:
                    break;
                case CosStream:
                    space = _view.Document.ColorSpaces.Find(new CosArray([IccBasedName, entry!]), _view.Reference, inline: false, out _) as PdfIccBasedColorSpace;
                    break;
                default:
                    _view.Report(DiagnosticCodes.OutputIntentInvalid, "The output intent's DestOutputProfile shall be an ICC profile stream; it is ignored.");
                    break;
            }

            Volatile.Write(ref _profile, new ProfileState(entry, space));
            return space;
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Subtype} {OutputConditionIdentifier}";

    /// <summary>Reads an <c>OutputIntents</c> array (§14.11.5); entries that are not dictionaries are skipped with a diagnostic.</summary>
    /// <param name="document">The document.</param>
    /// <param name="value">The array, or a reference to it; <see langword="null"/> for none.</param>
    /// <param name="owner">The catalog or page holding the entry, for diagnostics.</param>
    /// <param name="cache">The views read before, by dictionary, so every read returns the same instances.</param>
    /// <returns>The output intents in order; empty when there are none.</returns>
    internal static IReadOnlyList<PdfOutputIntent> Read(PdfDocument document, CosObject? value, CosReference? owner, Dictionary<CosDictionary, PdfOutputIntent> cache)
    {
        switch (document.Resolve(value))
        {
            case CosNull:
                return [];
            case CosArray array:
                var intents = new List<PdfOutputIntent>(array.Count);
                foreach (CosObject element in array)
                {
                    if (document.Resolve(element) is not CosDictionary dictionary)
                    {
                        document.DiagnosticSink.Report(
                            DiagnosticCodes.OutputIntentInvalid,
                            Diagnostics.DiagnosticSeverity.Warning,
                            "An OutputIntents element is not an output intent dictionary; it is skipped.",
                            objectReference: element as CosReference ?? owner);
                        continue;
                    }

                    lock (cache)
                    {
                        if (!cache.TryGetValue(dictionary, out PdfOutputIntent? intent))
                        {
                            intent = new PdfOutputIntent(document, dictionary, element as CosReference, owner);
                            cache.Add(dictionary, intent);
                        }

                        intents.Add(intent);
                    }
                }

                return intents;
            default:
                document.DiagnosticSink.Report(
                    DiagnosticCodes.OutputIntentInvalid,
                    Diagnostics.DiagnosticSeverity.Warning,
                    "The OutputIntents entry shall be an array of output intent dictionaries; it is ignored.",
                    objectReference: value as CosReference ?? owner);
                return [];
        }
    }

    /// <summary>Returns the output intent whose profile applies: the first of <paramref name="intents"/> with a destination profile.</summary>
    /// <param name="intents">The page's output intents, or the document's.</param>
    /// <returns>The output intent, or <see langword="null"/> when none has a profile.</returns>
    /// <remarks>
    /// ISO 32000-2 §14.11.5 leaves the choice among several output intents to the workflow; Broadside takes the first that has a
    /// <c>DestOutputProfile</c>.
    /// </remarks>
    internal static PdfOutputIntent? FirstWithProfile(IReadOnlyList<PdfOutputIntent> intents)
    {
        foreach (PdfOutputIntent intent in intents)
        {
            if (intent._view.Get(DestOutputProfileKey) is CosStream)
            {
                return intent;
            }
        }

        return null;
    }

    private sealed record ProfileState(CosObject? Entry, PdfIccBasedColorSpace? Space);
}
