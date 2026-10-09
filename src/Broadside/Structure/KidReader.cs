using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Structure;

/// <summary>What one entry of a structure element's <c>K</c> is.</summary>
internal enum KidKind
{
    /// <summary>Not a valid child: skipped (a diagnostic was recorded).</summary>
    Invalid,

    /// <summary>A structure element dictionary.</summary>
    Element,

    /// <summary>A marked-content sequence: an integer MCID or an MCR dictionary.</summary>
    MarkedContent,

    /// <summary>A whole object: an OBJR dictionary.</summary>
    Object,
}

/// <summary>One entry of <c>K</c>, classified.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Dictionary">The element, MCR or OBJR dictionary; <see langword="null"/> for an integer MCID.</param>
/// <param name="Reference">The indirect reference the entry was, if it was one.</param>
/// <param name="Mcid">The MCID of a marked-content item.</param>
internal readonly record struct Kid(KidKind Kind, CosDictionary? Dictionary, CosReference? Reference, int Mcid);

/// <summary>Reads and classifies the entries of <c>K</c> (ISO 32000-2 §14.7.2, Table 355; §14.7.5).</summary>
/// <remarks>
/// <para>
/// <c>K</c> is one object or an array of them: a structure element dictionary, an integer MCID, an MCR dictionary (Table 357) or an
/// OBJR dictionary (Table 358). "If the value of K is a dictionary containing no Type entry, it shall be assumed to be a structure
/// element dictionary" (Table 355).
/// </para>
/// <para>
/// Repairs (pdf.js and PDFBox treat these as structure elements, then fail on them): a dictionary without <c>Type</c> and without
/// <c>S</c> that holds an integer <c>MCID</c> is read as an MCR, one that holds <c>Obj</c> as an OBJR; a dictionary whose
/// <c>Type</c> is something else is read by the same rule (an element when it has <c>S</c>). Each is a
/// <c>StructElemTypeUnknown</c> diagnostic; anything else in <c>K</c> is skipped with <c>StructElemInvalid</c>.
/// </para>
/// </remarks>
internal static class KidReader
{
    /// <summary>The raw entries of <paramref name="owner"/>'s <c>K</c>, references kept.</summary>
    public static IReadOnlyList<CosObject> Entries(StructureContext context, CosDictionary owner)
    {
        if (!owner.TryGetValue(StructureNames.K, out CosObject? value))
        {
            return [];
        }

        return context.Resolve(value) switch
        {
            CosArray array => array,
            CosNull => [],
            _ => [value],
        };
    }

    public static Kid Classify(StructureContext context, CosObject entry, CosReference? ownerReference)
    {
        CosReference? reference = entry as CosReference;
        CosObject value = context.Resolve(entry);
        CosReference? where = reference ?? ownerReference;
        switch (value)
        {
            case CosInteger { Value: >= 0 and <= int.MaxValue } integer:
                return new Kid(KidKind.MarkedContent, null, reference, (int)integer.Value);
            case CosDictionary dictionary:
                return ClassifyDictionary(context, dictionary, reference, where);
            default:
                context.Report(DiagnosticCodes.StructElemInvalid, $"A K entry is {Describe(value)}, not a structure element, an MCID, an MCR or an OBJR (Table 355); skipped.", where);
                return default;
        }
    }

    private static Kid ClassifyDictionary(StructureContext context, CosDictionary dictionary, CosReference? reference, CosReference? where)
    {
        CosName? type = StructureValues.Name(context.Document, dictionary, KnownNames.Type);
        bool hasStructureType = dictionary.ContainsKey(StructureNames.S);
        CosObject? mcid = StructureValues.Get(context.Document, dictionary, StructureNames.MCID);
        bool hasObject = dictionary.ContainsKey(StructureNames.Obj);
        if (type is null || StructureNames.StructElem.Equals(type))
        {
            if (type is not null || hasStructureType || (mcid is null && !hasObject))
            {
                return new Kid(KidKind.Element, dictionary, reference, 0);
            }
        }
        else if (StructureNames.MCR.Equals(type))
        {
            return Mcr(context, dictionary, reference, where, mcid);
        }
        else if (StructureNames.OBJR.Equals(type))
        {
            return Objr(context, dictionary, reference, where, hasObject);
        }
        else if (hasStructureType)
        {
            context.Report(DiagnosticCodes.StructElemTypeUnknown, $"A K entry has Type /{type.Value} and an S entry; read as a structure element (Table 355).", where);
            return new Kid(KidKind.Element, dictionary, reference, 0);
        }

        if (mcid is not null)
        {
            context.Report(DiagnosticCodes.StructElemTypeUnknown, "A K entry without Type MCR holds an MCID; read as a marked-content reference (Table 357).", where);
            return Mcr(context, dictionary, reference, where, mcid);
        }

        if (hasObject)
        {
            context.Report(DiagnosticCodes.StructElemTypeUnknown, "A K entry without Type OBJR holds Obj; read as an object reference (Table 358).", where);
            return Objr(context, dictionary, reference, where, hasObject);
        }

        context.Report(DiagnosticCodes.StructElemTypeUnknown, $"A K entry has Type /{type!.Value} and is neither a structure element, an MCR nor an OBJR; skipped.", where);
        return default;
    }

    private static Kid Mcr(StructureContext context, CosDictionary dictionary, CosReference? reference, CosReference? where, CosObject? mcid)
    {
        if (mcid is CosInteger { Value: >= 0 and <= int.MaxValue } integer)
        {
            return new Kid(KidKind.MarkedContent, dictionary, reference, (int)integer.Value);
        }

        context.Report(DiagnosticCodes.StructElemInvalid, "A marked-content reference has no non-negative integer MCID (Table 357); skipped.", where);
        return default;
    }

    private static Kid Objr(StructureContext context, CosDictionary dictionary, CosReference? reference, CosReference? where, bool hasObject)
    {
        if (hasObject)
        {
            return new Kid(KidKind.Object, dictionary, reference, 0);
        }

        context.Report(DiagnosticCodes.StructElemInvalid, "An object reference has no Obj entry (Table 358); skipped.", where);
        return default;
    }

    private static string Describe(CosObject value) => value switch
    {
        CosNull => "null",
        CosInteger => "a negative or too large integer",
        CosReal => "a real number",
        CosBoolean => "a boolean",
        CosString => "a string",
        CosName => "a name",
        CosArray => "an array",
        CosStream => "a stream",
        _ => "an object of another kind",
    };
}
