using Broadside.Diagnostics;
using Broadside.IO;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>Detects a linearized file: its first object is a linearization parameter dictionary (Annex F, F.3.3).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 F.3.3 and Table F.1. The first indirect object after the header is looked at only when the first 1024 bytes hold
/// the name <c>/Linearized</c>, so no other file pays for loading its first object. The object is loaded through the cross-reference
/// information like any other, so it is the instance <see cref="PdfDocument.Resolve"/> returns.
/// </para>
/// <para>
/// A dictionary with a <c>Linearized</c> entry whose required entries are missing or of the wrong type is ignored with a
/// <see cref="DiagnosticCodes.LinearizationDictionaryInvalid"/> diagnostic (pdf.js and qpdf ignore it too). The <c>L</c> check
/// against the file length is the caller's: a mismatch is not a deviation, it is what appending an update does (G.7).
/// </para>
/// </remarks>
internal static class LinearizationReader
{
    /// <summary>The parameter dictionary shall be within the first 1024 bytes of the file (F.3.3).</summary>
    private const int SearchLength = 1024;

    /// <summary>Reads the linearization information of the file, if it has any.</summary>
    /// <param name="source">The file.</param>
    /// <param name="loader">The object loader.</param>
    /// <param name="diagnostics">Where to report an invalid parameter dictionary or hint table.</param>
    /// <returns>The information, or <see langword="null"/> when the file does not start with a valid parameter dictionary.</returns>
    public static PdfLinearization? Read(PdfSource source, ObjectLoader loader, DiagnosticSink diagnostics)
    {
        long headerOffset = loader.Header.Offset;
        Span<byte> buffer = stackalloc byte[SearchLength];
        ReadOnlySpan<byte> start = buffer[..source.Read(headerOffset, buffer)];
        if (start.IndexOf("/Linearized"u8) < 0)
        {
            return null;
        }

        var lexer = new CosLexer(start);
        if (!StructureTokens.TryReadUnsigned(ref lexer, out long number)
            || !StructureTokens.TryReadUnsigned(ref lexer, out long generation)
            || !StructureTokens.TryReadKeyword(ref lexer, "obj"u8)
            || number is 0 or > int.MaxValue
            || generation > CosReference.MaxGeneration)
        {
            return null;
        }

        var reference = new CosReference((int)number, (int)generation);
        if (loader.Load(reference) is not CosDictionary dictionary || !dictionary.ContainsKey(PdfLinearization.Names.Linearized))
        {
            return null;
        }

        if (!TryValidate(dictionary, out long[] hintStreams))
        {
            diagnostics.Report(
                DiagnosticCodes.LinearizationDictionaryInvalid,
                DiagnosticSeverity.Warning,
                "The linearization parameter dictionary lacks a required entry or has one of the wrong type (Linearized, L, H, O, E, N, T, P); the file is read as not linearized.",
                objectReference: reference);
            return null;
        }

        IReadOnlyList<CosReference> firstPageObjects = FirstPageObjects(loader.CrossReference);
        return new PdfLinearization(
            dictionary,
            reference,
            firstPageObjects,
            () => HintTableReader.Read(source, loader, dictionary, hintStreams, diagnostics));
    }

    /// <summary>Checks the entries of Table F.1: all required ones present, direct and of the right type and range.</summary>
    private static bool TryValidate(CosDictionary dictionary, out long[] hintStreams)
    {
        hintStreams = [];
        if (!dictionary.TryGetValue(PdfLinearization.Names.Linearized, out CosObject? version) || version is not CosNumber { } number || number.ToDouble() <= 0)
        {
            return false;
        }

        if (!IsInteger(dictionary, PdfLinearization.Names.L, minimum: 1)
            || !IsInteger(dictionary, PdfLinearization.Names.O, minimum: 1)
            || !IsInteger(dictionary, PdfLinearization.Names.E, minimum: 1)
            || !IsInteger(dictionary, PdfLinearization.Names.N, minimum: 1)
            || !IsInteger(dictionary, PdfLinearization.Names.T, minimum: 1)
            || (dictionary.ContainsKey(PdfLinearization.Names.P) && !IsInteger(dictionary, PdfLinearization.Names.P, minimum: 0)))
        {
            return false;
        }

        if (!dictionary.TryGetValue(PdfLinearization.Names.H, out CosObject? h) || h is not CosArray { Count: 2 or 4 } array)
        {
            return false;
        }

        hintStreams = new long[array.Count];
        for (int index = 0; index < array.Count; index++)
        {
            if (array[index] is not CosInteger { Value: > 0 } item)
            {
                return false;
            }

            hintStreams[index] = item.Value;
        }

        return true;
    }

    private static bool IsInteger(CosDictionary dictionary, CosName key, long minimum) =>
        dictionary.TryGetValue(key, out CosObject? value) && value is CosInteger integer && integer.Value >= minimum;

    /// <summary>
    /// The in-use objects of the first-page cross-reference section (F.3.4): the section the original revision's chain reaches first,
    /// whose <c>Prev</c> points forward to the main section.
    /// </summary>
    private static List<CosReference> FirstPageObjects(CrossReference crossReference)
    {
        var objects = new List<CosReference>();
        XrefRevision original = crossReference.Revisions[0];
        if (!original.IsLinearizedPair)
        {
            return objects;
        }

        foreach ((int number, XrefEntry entry) in original.Newest.Entries)
        {
            switch (entry.Kind)
            {
                case XrefEntryKind.InUse:
                    objects.Add(new CosReference(number, entry.Generation));
                    break;
                case XrefEntryKind.Compressed:
                    objects.Add(new CosReference(number, 0));
                    break;
                default:
                    break;
            }
        }

        objects.Sort((left, right) => left.ObjectNumber.CompareTo(right.ObjectNumber));
        return objects;
    }
}
