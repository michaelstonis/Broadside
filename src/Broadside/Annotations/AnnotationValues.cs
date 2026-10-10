using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>Lenient readers for the value types annotations and form XObjects use; they never report, the caller decides.</summary>
internal static class AnnotationValues
{
    /// <summary>Reads a rectangle (§7.9.5): an array of four finite numbers, normalized; <see langword="null"/> otherwise.</summary>
    public static PdfRectangle? ReadRectangle(PdfDocument document, CosObject? value)
    {
        Span<double> corners = stackalloc double[4];
        return document.Resolve(value) is CosArray { Count: 4 } array && TryReadNumbers(document, array, corners)
            ? new PdfRectangle(corners[0], corners[1], corners[2], corners[3])
            : null;
    }

    /// <summary>Reads a matrix (§8.3.4): an array of six finite numbers; <see langword="null"/> otherwise.</summary>
    public static Matrix? ReadMatrix(PdfDocument document, CosObject? value)
    {
        Span<double> elements = stackalloc double[6];
        return document.Resolve(value) is CosArray { Count: 6 } array && TryReadNumbers(document, array, elements)
            ? new Matrix(elements[0], elements[1], elements[2], elements[3], elements[4], elements[5])
            : null;
    }

    /// <summary>Reads an integer in the 32-bit range; <see langword="null"/> otherwise.</summary>
    public static int? ReadInteger(PdfDocument document, CosObject? value) =>
        document.Resolve(value) is CosInteger { Value: >= int.MinValue and <= int.MaxValue } integer ? (int)integer.Value : null;

    /// <summary>Reads a finite number; <see langword="null"/> otherwise.</summary>
    public static double? ReadNumber(PdfDocument document, CosObject? value) =>
        document.Resolve(value) is CosNumber number && double.IsFinite(number.ToDouble()) ? number.ToDouble() : null;

    /// <summary>Reads the first <c>values.Length</c> elements of <paramref name="array"/> as finite numbers.</summary>
    public static bool TryReadNumbers(PdfDocument document, CosArray array, Span<double> values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            if (ReadNumber(document, array[index]) is not { } number)
            {
                return false;
            }

            values[index] = number;
        }

        return true;
    }

    /// <summary>
    /// Reads every element of an array as a number; <paramref name="complete"/> is false when an element is not one (it is skipped).
    /// <see langword="null"/> when <paramref name="value"/> is not an array.
    /// </summary>
    public static List<double>? ReadNumbers(PdfDocument document, CosObject? value, out bool complete)
    {
        complete = true;
        if (document.Resolve(value) is not CosArray array)
        {
            return null;
        }

        var numbers = new List<double>(array.Count);
        foreach (CosObject element in array)
        {
            if (ReadNumber(document, element) is { } number)
            {
                numbers.Add(number);
            }
            else
            {
                complete = false;
            }
        }

        return numbers;
    }

    /// <summary>Reads a colour array of 0, 1, 3 or 4 numbers (Table 166 <c>C</c>); <paramref name="valid"/> is false for anything else.</summary>
    public static PdfDeviceColor? ReadColor(PdfDocument document, CosObject? value, out bool valid)
    {
        valid = true;
        CosObject resolved = document.Resolve(value);
        if (resolved is CosNull)
        {
            return null;
        }

        if (resolved is not CosArray { Count: 0 or 1 or 3 or 4 } array)
        {
            valid = false;
            return null;
        }

        Span<double> components = stackalloc double[array.Count];
        if (!TryReadNumbers(document, array, components))
        {
            valid = false;
            return null;
        }

        return new PdfDeviceColor(components);
    }
}
