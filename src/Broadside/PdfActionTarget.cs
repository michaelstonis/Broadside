using Broadside.Objects;

namespace Broadside;

/// <summary>
/// An annotation or form field an action applies to, given either by its dictionary or by the fully qualified name of a field: an element
/// of a hide action's <c>T</c> or of a form action's <c>Fields</c>.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.11, Table 214, and §12.7.6.2-12.7.6.3, Tables 239 and 241. Resolve a target to the fields it names with
/// <see cref="Forms.PdfAcroForm.FindFields(PdfActionTarget)"/> (§12.7.4.2).
/// </remarks>
public sealed class PdfActionTarget
{
    internal PdfActionTarget(CosDictionary? dictionary, CosReference? reference, string? fieldName)
    {
        Dictionary = dictionary;
        Reference = reference;
        FieldName = fieldName;
    }

    /// <summary>Gets the annotation or field dictionary, or <see langword="null"/> when the target is given by name.</summary>
    public CosDictionary? Dictionary { get; }

    /// <summary>Gets the indirect reference to <see cref="Dictionary"/>, or <see langword="null"/>.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the fully qualified field name, or <see langword="null"/> when the target is given by its dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.7.4.2.</remarks>
    public string? FieldName { get; }

    /// <summary>Reads a single target or an array of them; elements of another type are skipped with a diagnostic through <paramref name="invalid"/>.</summary>
    internal static List<PdfActionTarget> Read(PdfDocument document, CosObject? value, Action invalid)
    {
        var targets = new List<PdfActionTarget>();
        if (document.Resolve(value) is CosArray array)
        {
            foreach (CosObject element in array)
            {
                Add(document, element, targets, invalid);
            }
        }
        else if (value is not null)
        {
            Add(document, value, targets, invalid);
        }

        return targets;
    }

    private static void Add(PdfDocument document, CosObject element, List<PdfActionTarget> targets, Action invalid)
    {
        switch (document.Resolve(element))
        {
            case CosDictionary dictionary:
                targets.Add(new PdfActionTarget(dictionary, element as CosReference, fieldName: null));
                break;
            case CosString name:
                targets.Add(new PdfActionTarget(dictionary: null, reference: null, name.DecodeText()));
                break;
            case CosNull:
                break;
            default:
                invalid();
                break;
        }
    }
}
