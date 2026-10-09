using System.Globalization;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics.Shadings;

/// <summary>
/// Reads the entries of one shading or pattern dictionary while its model is built: values as they are now, with the repairs and
/// diagnostics ISO 32000-2 §8.7 calls for, and whether the object can be used at all.
/// </summary>
internal sealed class ShadingReader
{
    public ShadingReader(PdfDocument document, CosObject cosObject, CosDictionary dictionary, CosReference? reference, CosReference? owner)
    {
        Document = document;
        CosObject = cosObject;
        Dictionary = dictionary;
        Reference = reference;
        DiagnosticReference = reference ?? owner;
    }

    public PdfDocument Document { get; }

    public CosObject CosObject { get; }

    public CosDictionary Dictionary { get; }

    public CosReference? Reference { get; }

    public CosReference? DiagnosticReference { get; }

    /// <summary>Gets a value indicating whether nothing found so far makes the object unusable.</summary>
    public bool IsValid { get; private set; } = true;

    public CosObject? Get(CosName key) => Dictionary.TryGetValue(key, out CosObject? value) ? value : null;

    public CosObject Resolve(CosObject? value) => Document.Resolve(value);

    /// <summary>Records a repair: the object stays usable.</summary>
    public void Report(string code, string message) =>
        Document.DiagnosticSink.Report(code, DiagnosticSeverity.Warning, message, offset: null, DiagnosticReference);

    /// <summary>Records why the object cannot be used: it paints nothing.</summary>
    public void Invalid(string code, string message)
    {
        IsValid = false;
        Document.DiagnosticSink.Report(code, DiagnosticSeverity.Error, message, offset: null, DiagnosticReference);
    }

    /// <summary>Reads an array of finite numbers; <see langword="null"/> when absent or anything else.</summary>
    public double[]? Numbers(CosName key) => ColorEntries.Numbers(Document.ColorSpaces, Get(key));

    /// <summary>Reads a finite number; <see langword="null"/> when absent or anything else.</summary>
    public double? Number(CosName key) => ColorEntries.Number(Document.ColorSpaces, Get(key));

    /// <summary>Reads an integer; <see langword="null"/> when absent or anything else.</summary>
    public int? Integer(CosName key) => AnnotationValues.ReadInteger(Document, Get(key));

    /// <summary>Reads a matrix (§8.3.4); the identity when absent, the identity with a diagnostic when malformed.</summary>
    public Matrix Matrix(CosName key, string code)
    {
        CosObject? value = Get(key);
        if (value is null)
        {
            return Graphics.Matrix.Identity;
        }

        if (AnnotationValues.ReadMatrix(Document, value) is { } matrix)
        {
            return matrix;
        }

        Report(code, $"The {key.Value} entry is not six numbers; the identity matrix is used.");
        return Graphics.Matrix.Identity;
    }

    /// <summary>Reads a rectangle; <see langword="null"/> when absent, <see langword="null"/> with a diagnostic when malformed.</summary>
    public PdfRectangle? Rectangle(CosName key, string code)
    {
        CosObject? value = Get(key);
        if (value is null)
        {
            return null;
        }

        PdfRectangle? rectangle = AnnotationValues.ReadRectangle(Document, value);
        if (rectangle is null)
        {
            Report(code, $"The {key.Value} entry is not four numbers; it is ignored.");
        }

        return rectangle;
    }

    /// <summary>Reads a boolean; <paramref name="fallback"/> when absent, with a diagnostic when malformed.</summary>
    public bool Boolean(CosName key, bool fallback, string code)
    {
        CosObject? value = Get(key);
        if (value is null)
        {
            return fallback;
        }

        if (Resolve(value) is CosBoolean boolean)
        {
            return boolean.Value;
        }

        Report(code, $"The {key.Value} entry is not a boolean; {fallback.ToString().ToLowerInvariant()} is used.");
        return fallback;
    }

    /// <summary>Reads an array of <paramref name="count"/> numbers; <paramref name="fallback"/> when absent, with a diagnostic when malformed.</summary>
    public double[] Numbers(CosName key, int count, double[] fallback, string code)
    {
        if (Get(key) is null)
        {
            return fallback;
        }

        if (Numbers(key) is { } numbers && numbers.Length == count)
        {
            return numbers;
        }

        Report(code, string.Create(CultureInfo.InvariantCulture, $"The {key.Value} entry is not {count} numbers; the default is used."));
        return fallback;
    }

    /// <summary>Reads <c>Extend</c> (Tables 79 and 80): two booleans, both false by default.</summary>
    public (bool Start, bool End) Extend()
    {
        CosObject? value = Get(ShadingNames.Extend);
        if (value is null)
        {
            return (false, false);
        }

        if (Resolve(value) is CosArray { Count: 2 } array && Resolve(array[0]) is CosBoolean start && Resolve(array[1]) is CosBoolean end)
        {
            return (start.Value, end.Value);
        }

        Report(DiagnosticCodes.ShadingExtendInvalid, "The Extend entry is not an array of two booleans; neither end is extended.");
        return (false, false);
    }
}
