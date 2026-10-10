using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>Reads a structure element's <c>A</c> and <c>C</c> entries and the root's <c>ClassMap</c> into attribute objects.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.7.6 (Tables 355, 360) and §14.7.6.3: in an <c>A</c> or <c>C</c> array, an integer is the revision number of the
/// object (or class) BEFORE it; an attribute object is never an integer, so the two are told apart by type (p.738 NOTE 3). A leading
/// integer, or two in a row, is ignored with an <c>AttributeRevisionInvalid</c> diagnostic (PDFBox's model; pdf.js drops every
/// integer). An entry that is neither an attribute object nor a revision is skipped with <c>AttributeObjectInvalid</c>.
/// </para>
/// <para>
/// An attribute object whose <c>O</c> is missing, is <c>NSO</c> without <c>NS</c>, or has <c>NS</c> with another owner, is still
/// read (generically, by its owner name) with an <c>AttributeOwnerInvalid</c> diagnostic (Table 360).
/// </para>
/// </remarks>
internal static class AttributeReader
{
    public static List<PdfAttributeObject> ReadObjects(StructureContext context, CosObject? value, CosReference? where)
    {
        var objects = new List<PdfAttributeObject>();
        if (value is null)
        {
            return objects;
        }

        CosObject resolved = context.Resolve(value);
        if (resolved is CosArray array)
        {
            CosObject? pending = null;
            CosReference? pendingReference = null;
            bool revisionSeen = false;
            foreach (CosObject item in array)
            {
                CosObject entry = context.Resolve(item);
                if (entry is CosInteger revision)
                {
                    if (pending is null || revisionSeen || revision.Value is < 0 or > int.MaxValue)
                    {
                        context.Report(DiagnosticCodes.AttributeRevisionInvalid, "A revision number in an A array does not follow an attribute object (§14.7.6.3); ignored.", where);
                        continue;
                    }

                    objects.Add(Create(context, pending, pendingReference, (int)revision.Value, where));
                    pending = null;
                    revisionSeen = true;
                    continue;
                }

                if (pending is not null)
                {
                    objects.Add(Create(context, pending, pendingReference, 0, where));
                    pending = null;
                }

                revisionSeen = false;
                if (entry is CosDictionary or CosStream)
                {
                    pending = entry;
                    pendingReference = item as CosReference;
                }
                else if (entry is not CosNull)
                {
                    context.Report(DiagnosticCodes.AttributeObjectInvalid, "An A entry is neither an attribute object (a dictionary or a stream) nor a revision number (Table 355); skipped.", where);
                }
            }

            if (pending is not null)
            {
                objects.Add(Create(context, pending, pendingReference, 0, where));
            }
        }
        else if (resolved is CosDictionary or CosStream)
        {
            objects.Add(Create(context, resolved, value as CosReference, 0, where));
        }
        else if (resolved is not CosNull)
        {
            context.Report(DiagnosticCodes.AttributeObjectInvalid, "An A entry is neither an attribute object nor an array of them (Table 355); ignored.", where);
        }

        return objects;
    }

    /// <summary>Reads <c>C</c>: class names, each optionally followed by its revision number.</summary>
    public static List<(CosName Name, int Revision)> ReadClassNames(StructureContext context, CosObject? value, CosReference? where)
    {
        var names = new List<(CosName, int)>();
        if (value is null)
        {
            return names;
        }

        switch (context.Resolve(value))
        {
            case CosName single:
                names.Add((single, 0));
                break;
            case CosArray array:
                bool revisionAllowed = false;
                foreach (CosObject item in array)
                {
                    switch (context.Resolve(item))
                    {
                        case CosName name:
                            names.Add((name, 0));
                            revisionAllowed = true;
                            break;
                        case CosInteger { Value: >= 0 and <= int.MaxValue } revision when revisionAllowed:
                            names[^1] = (names[^1].Item1, (int)revision.Value);
                            revisionAllowed = false;
                            break;
                        case CosInteger:
                            context.Report(DiagnosticCodes.AttributeRevisionInvalid, "A revision number in a C array does not follow a class name (§14.7.6.3); ignored.", where);
                            break;
                        default:
                            context.Report(DiagnosticCodes.AttributeObjectInvalid, "A C entry is not a class name (Table 355); skipped.", where);
                            revisionAllowed = false;
                            break;
                    }
                }

                break;
            case CosNull:
                break;
            default:
                context.Report(DiagnosticCodes.AttributeObjectInvalid, "C is neither a class name nor an array of them (Table 355); ignored.", where);
                break;
        }

        return names;
    }

    /// <summary>The attribute objects of class <paramref name="name"/> in the root's <c>ClassMap</c>, carrying <paramref name="revision"/>.</summary>
    public static List<PdfAttributeObject> ReadClass(StructureContext context, CosName name, int revision, CosReference? where)
    {
        if (ViewReading.Get(context.Document, context.Root, StructureNames.ClassMap) is not CosDictionary classMap
            || !classMap.TryGetValue(name, out CosObject? value))
        {
            return [];
        }

        List<PdfAttributeObject> objects = ReadObjects(context, value, where);
        if (revision == 0)
        {
            return objects;
        }

        for (int index = 0; index < objects.Count; index++)
        {
            PdfAttributeObject attributes = objects[index];
            objects[index] = PdfAttributeObject.Create(context, (CosObject?)attributes.Stream ?? attributes.Dictionary, attributes.Reference, revision);
        }

        return objects;
    }

    private static PdfAttributeObject Create(StructureContext context, CosObject source, CosReference? reference, int revision, CosReference? where)
    {
        PdfAttributeObject attributes = PdfAttributeObject.Create(context, source, reference, revision);
        bool isNamespaceOwner = StructureNames.NSO.Equals(attributes.Owner);
        bool hasNamespace = attributes.Dictionary.ContainsKey(StructureNames.NS);
        if (attributes.Owner is null)
        {
            context.Report(DiagnosticCodes.AttributeOwnerInvalid, "An attribute object has no owner (O, Table 360); read without one.", reference ?? where);
        }
        else if (isNamespaceOwner != hasNamespace)
        {
            context.Report(
                DiagnosticCodes.AttributeOwnerInvalid,
                isNamespaceOwner ? "An attribute object owned by NSO has no NS entry (Table 360)." : "An attribute object has NS but its owner is not NSO (Table 360).",
                reference ?? where);
        }

        return attributes;
    }
}
