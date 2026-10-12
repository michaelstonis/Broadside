using Broadside.Objects;

namespace Broadside;

/// <summary>One requirement the document places on an interactive processor: an element of the catalog's <c>Requirements</c> array.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.11, Tables 273 to 276. A requirement names a feature the processor should support for the document to work as
/// intended (Table 275: <c>EnableJavaScripts</c>, <c>DigSig</c>, <c>Encryption</c> and others; the list is open), how much the
/// experience suffers without it, and handlers that check it. Requirements are exposed, never evaluated: evaluating them would run
/// JavaScript, which is out of scope.
/// </para>
/// <para>A live view over the requirement dictionary; deviations are reported when <see cref="PdfDocument.Requirements"/> is read.</para>
/// </remarks>
public sealed class PdfRequirement
{
    private static readonly CosName SKey = new("S");
    private static readonly CosName VKey = new("V");
    private static readonly CosName PenaltyKey = new("Penalty");
    private static readonly CosName RHKey = new("RH");

    private readonly PdfDocument _document;

    internal PdfRequirement(PdfDocument document, CosDictionary dictionary)
    {
        _document = document;
        Dictionary = dictionary;
    }

    /// <summary>Gets the requirement dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.11.2, Table 273.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the type of the requirement (<c>S</c>), such as <c>EnableJavaScripts</c>, or <see langword="null"/> when missing.</summary>
    /// <remarks>ISO 32000-2 §12.11.2, Table 273, and Table 275 (the table's reference to Table 276 is an erratum).</remarks>
    public CosName? RequirementType => _document.Resolve(Dictionary.GetValueOrDefault(SKey)) as CosName;

    /// <summary>Gets the version of the feature required (<c>V</c>, PDF 2.0): a name, or an extensions dictionary; <see langword="null"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.11.2, Table 273.</remarks>
    public CosObject? Version => _document.Resolve(Dictionary.GetValueOrDefault(VKey)) is not CosNull and var version ? version : null;

    /// <summary>Gets how much the experience degrades without the feature, 0 to 100 (<c>Penalty</c>, PDF 2.0). Default 100.</summary>
    /// <remarks>ISO 32000-2 §12.11.2, Table 273. A value outside 0 to 100 is clamped into it.</remarks>
    public int Penalty => ReadPenalty(_document, Dictionary, out _);

    /// <summary>Gets the handlers that check the requirement (<c>RH</c>: one handler dictionary or an array of them); empty when absent.</summary>
    /// <remarks>ISO 32000-2 §12.11.3, Table 276. A handler of an unknown type shall be ignored by the processor; it is still listed here.</remarks>
    public IReadOnlyList<PdfRequirementHandler> Handlers
    {
        get
        {
            CosObject value = _document.Resolve(Dictionary.GetValueOrDefault(RHKey));
            IEnumerable<CosObject> elements = value switch
            {
                CosDictionary => [value],
                CosArray array => array,
                _ => [],
            };
            var handlers = new List<PdfRequirementHandler>();
            foreach (CosObject element in elements)
            {
                if (_document.Resolve(element) is CosDictionary handler)
                {
                    handlers.Add(new PdfRequirementHandler(_document, handler));
                }
            }

            return handlers;
        }
    }

    /// <summary>Reads Penalty, clamped; <paramref name="valid"/> says whether it was absent or an integer in range.</summary>
    internal static int ReadPenalty(PdfDocument document, CosDictionary dictionary, out bool valid)
    {
        switch (document.Resolve(dictionary.GetValueOrDefault(PenaltyKey)))
        {
            case CosNull:
                valid = true;
                return 100;
            case CosInteger { Value: >= 0 and <= 100 } penalty:
                valid = true;
                return (int)penalty.Value;
            case CosNumber number:
                valid = false;
                return (int)Math.Clamp(Math.Round(number.ToDouble()), 0, 100);
            default:
                valid = false;
                return 100;
        }
    }

    /// <summary>Whether the requirement dictionary has the entries Table 273 requires with the right types.</summary>
    internal static bool IsWellFormed(PdfDocument document, CosDictionary dictionary)
    {
        _ = ReadPenalty(document, dictionary, out bool penaltyValid);
        CosObject handlers = document.Resolve(dictionary.GetValueOrDefault(RHKey));
        bool handlersValid = handlers switch
        {
            CosNull or CosDictionary => true,
            CosArray array => array.All(element => document.Resolve(element) is CosDictionary),
            _ => false,
        };
        return penaltyValid && handlersValid && document.Resolve(dictionary.GetValueOrDefault(SKey)) is CosName;
    }
}
