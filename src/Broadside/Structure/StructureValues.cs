using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>Reads typed values out of the dictionaries of §14.6-§14.8, resolving indirect references; a value of the wrong type reads as absent.</summary>
internal static class StructureValues
{
    public static CosObject? Get(PdfDocument? document, CosDictionary dictionary, CosName key)
    {
        if (!dictionary.TryGetValue(key, out CosObject? value))
        {
            return null;
        }

        CosObject resolved = document is null ? value : document.Resolve(value);
        return resolved is CosNull ? null : resolved;
    }

    public static string? Text(PdfDocument? document, CosDictionary dictionary, CosName key) =>
        Get(document, dictionary, key) is CosString text ? text.DecodeText() : null;

    public static CosName? Name(PdfDocument? document, CosDictionary dictionary, CosName key) =>
        Get(document, dictionary, key) as CosName;

    public static int? Integer(PdfDocument? document, CosDictionary dictionary, CosName key) =>
        Get(document, dictionary, key) is CosInteger { Value: >= int.MinValue and <= int.MaxValue } integer ? (int)integer.Value : null;

    public static double? Number(PdfDocument? document, CosDictionary dictionary, CosName key) =>
        Get(document, dictionary, key) is CosNumber number ? number.ToDouble() : null;

    public static bool? Boolean(PdfDocument? document, CosDictionary dictionary, CosName key) =>
        Get(document, dictionary, key) is CosBoolean boolean ? boolean.Value : null;

    /// <summary>Reads an array of numbers; <see langword="null"/> when absent, not an array, or holding a non-number.</summary>
    public static double[]? Numbers(PdfDocument? document, CosDictionary dictionary, CosName key)
    {
        if (Get(document, dictionary, key) is not CosArray array)
        {
            return null;
        }

        double[] values = new double[array.Count];
        for (int index = 0; index < values.Length; index++)
        {
            CosObject item = document is null ? array[index] : document.Resolve(array[index]);
            if (item is not CosNumber number)
            {
                return null;
            }

            values[index] = number.ToDouble();
        }

        return values;
    }

    /// <summary>Reads a rectangle (§7.9.5): four numbers, normalized.</summary>
    public static PdfRectangle? Rectangle(PdfDocument? document, CosDictionary dictionary, CosName key) =>
        Numbers(document, dictionary, key) is [var x1, var y1, var x2, var y2] ? new PdfRectangle(x1, y1, x2, y2) : null;

    /// <summary>The dictionary of an attribute object or property list, which may be a stream (legacy form, §14.7.6.1).</summary>
    public static CosDictionary? DictionaryOf(CosObject value) => value switch
    {
        CosDictionary dictionary => dictionary,
        CosStream stream => stream.Dictionary,
        _ => null,
    };
}
