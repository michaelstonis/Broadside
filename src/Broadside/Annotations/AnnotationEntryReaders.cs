using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Annotations;

/// <summary>
/// Readers for entries that only some annotation subtypes define: point lists, quadrilaterals, rectangle differences, line
/// endings, border effects, icon names and the like. The subtype that owns an entry calls the reader; the annotation base class
/// keeps only the readers every subtype uses (Table 166).
/// </summary>
/// <remarks>ISO 32000-2 §12.5.2 (Table 166) for the entries every annotation has, and the subtype tables of §12.5.6 for the rest.</remarks>
internal static class AnnotationEntryReaders
{
    internal static CosObject? Entry(this PdfAnnotation annotation, CosName key) => EntryReader.Get(annotation.Document, annotation.Dictionary, key);

    internal static string? GetByteString(this PdfAnnotation annotation, CosName key)
    {
        switch (annotation.Entry(key))
        {
            case null:
                return null;
            case CosString text:
                return System.Text.Encoding.Latin1.GetString(text.Bytes);
            default:
                Invalid(annotation, key, "a string");
                return null;
        }
    }

    internal static string? GetTextOrStream(this PdfAnnotation annotation, CosName key)
    {
        switch (annotation.Entry(key))
        {
            case null:
                return null;
            case CosString text:
                return text.DecodeText();
            case CosStream stream:
                return new CosString(annotation.Document.DecodeStream(stream).Span).DecodeText();
            default:
                Invalid(annotation, key, "a text string or text stream");
                return null;
        }
    }

    internal static T GetChoice<T>(this PdfAnnotation annotation, CosName key, T fallback, params ReadOnlySpan<(string Name, T Value)> choices)
    {
        if (EntryReader.Typed<CosName>(annotation.Entry(key), key, "a name", Issue(annotation)) is { } name)
        {
            foreach ((string text, T value) in choices)
            {
                if (string.Equals(name.Value, text, StringComparison.Ordinal))
                {
                    return value;
                }
            }
        }

        return fallback;
    }

    internal static PdfAction? GetAction(this PdfAnnotation annotation, CosName key) =>
        annotation.Dictionary.TryGetValue(key, out CosObject? value) ? PdfAction.Create(annotation.Document, value, annotation.DiagnosticReference) : null;

    internal static PdfFormXObject? GetForm(this PdfAnnotation annotation, CosName key) =>
        EntryReader.Typed<CosStream>(annotation.Entry(key), key, "a stream", Issue(annotation)) is not null
        && annotation.Dictionary.TryGetValue(key, out CosObject? value)
            ? PdfFormXObject.Create(annotation.Document, value)
            : null;

    internal static PdfDate? GetDate(this PdfAnnotation annotation, CosName key)
    {
        if (EntryReader.Text(annotation.Entry(key), key, Issue(annotation)) is not { } text)
        {
            return null;
        }

        switch (PdfDate.Parse(text, out PdfDate date))
        {
            case DateParseOutcome.Valid:
                return date;
            case DateParseOutcome.Repaired:
                annotation.Report(DiagnosticCodes.DateInvalid, $"The annotation's {key.Value} entry does not follow the date format of §7.9.4; it is read with the deviating fields repaired.");
                return date;
            default:
                annotation.Report(DiagnosticCodes.DateUnreadable, $"The annotation's {key.Value} entry is not a date (§7.9.4).");
                return null;
        }
    }

    /// <summary>Reads a flat array of x y pairs, such as <c>Vertices</c> or <c>L</c>; an odd count or a non-number records a diagnostic.</summary>
    internal static IReadOnlyList<PathPoint> GetPointPairs(this PdfAnnotation annotation, CosName key, bool required, string table)
    {
        CosObject? value = annotation.Entry(key);
        if (value is null)
        {
            if (required)
            {
                Missing(annotation, key, table);
            }

            return [];
        }

        List<double>? numbers = AnnotationValues.ReadNumbers(annotation.Document, value, out bool complete);
        if (numbers is null || !complete || numbers.Count % 2 != 0)
        {
            Invalid(annotation, key, "an array of pairs of numbers");
            if (numbers is null)
            {
                return [];
            }
        }

        var points = new PathPoint[numbers.Count / 2];
        for (int index = 0; index < points.Length; index++)
        {
            points[index] = new PathPoint(numbers[2 * index], numbers[(2 * index) + 1]);
        }

        return points;
    }

    /// <summary>Reads an array of arrays of x y pairs, such as <c>InkList</c>.</summary>
    internal static IReadOnlyList<IReadOnlyList<PathPoint>> GetPointPairLists(this PdfAnnotation annotation, CosName key, bool required, string table)
    {
        CosObject? value = annotation.Entry(key);
        if (value is null)
        {
            if (required)
            {
                Missing(annotation, key, table);
            }

            return [];
        }

        if (value is not CosArray outer)
        {
            Invalid(annotation, key, "an array of arrays of numbers");
            return [];
        }

        var lists = new List<IReadOnlyList<PathPoint>>(outer.Count);
        bool valid = true;
        foreach (CosObject element in outer)
        {
            List<double>? numbers = AnnotationValues.ReadNumbers(annotation.Document, element, out bool complete);
            valid &= numbers is not null && complete && numbers.Count % 2 == 0;
            if (numbers is null)
            {
                continue;
            }

            var points = new PathPoint[numbers.Count / 2];
            for (int index = 0; index < points.Length; index++)
            {
                points[index] = new PathPoint(numbers[2 * index], numbers[(2 * index) + 1]);
            }

            lists.Add(points);
        }

        if (!valid)
        {
            Invalid(annotation, key, "an array of arrays of pairs of numbers");
        }

        return lists;
    }

    /// <summary>Reads <c>Path</c> (PDF 2.0): arrays of 2 numbers (a point) or 6 (a Bézier curve's two control points and end point).</summary>
    internal static IReadOnlyList<IReadOnlyList<double>>? GetCurvePath(this PdfAnnotation annotation)
    {
        if (EntryReader.Typed<CosArray>(annotation.Entry(AnnotationNames.Path), AnnotationNames.Path, "an array", Issue(annotation)) is not { } outer)
        {
            return null;
        }

        var segments = new List<IReadOnlyList<double>>(outer.Count);
        bool valid = true;
        foreach (CosObject element in outer)
        {
            List<double>? numbers = AnnotationValues.ReadNumbers(annotation.Document, element, out bool complete);
            bool shape = numbers is not null && complete && (numbers.Count == 2 || (numbers.Count == 6 && segments.Count > 0));
            valid &= shape;
            if (shape)
            {
                segments.Add(numbers!);
            }
        }

        if (!valid)
        {
            Invalid(annotation, AnnotationNames.Path, "an array of arrays of 2 or 6 numbers, the first of 2");
        }

        return segments;
    }

    /// <summary>Reads <c>QuadPoints</c>: complete quadrilaterals only; a count not a multiple of 8 or a non-number records a diagnostic.</summary>
    internal static IReadOnlyList<PdfQuadrilateral> GetQuadrilaterals(this PdfAnnotation annotation, bool required, string table)
    {
        CosObject? value = annotation.Entry(AnnotationNames.QuadPoints);
        if (value is null)
        {
            if (required)
            {
                Missing(annotation, AnnotationNames.QuadPoints, table);
            }

            return [];
        }

        List<double>? numbers = AnnotationValues.ReadNumbers(annotation.Document, value, out bool complete);
        if (numbers is null || !complete || numbers.Count % 8 != 0 || numbers.Count == 0)
        {
            annotation.Report(DiagnosticCodes.QuadPointsInvalid, "The annotation's QuadPoints entry is not an array of 8n numbers; only its complete quadrilaterals are read.");
            if (numbers is null)
            {
                return [];
            }
        }

        var quads = new PdfQuadrilateral[numbers.Count / 8];
        for (int index = 0; index < quads.Length; index++)
        {
            int at = index * 8;
            quads[index] = new PdfQuadrilateral(
                new PathPoint(numbers[at], numbers[at + 1]),
                new PathPoint(numbers[at + 2], numbers[at + 3]),
                new PathPoint(numbers[at + 4], numbers[at + 5]),
                new PathPoint(numbers[at + 6], numbers[at + 7]));
        }

        return quads;
    }

    /// <summary>Reads <c>RD</c>: four non-negative numbers.</summary>
    internal static PdfRectangleDifferences? GetRectangleDifferences(this PdfAnnotation annotation)
    {
        CosObject? value = annotation.Entry(AnnotationNames.RD);
        if (value is null)
        {
            return null;
        }

        Span<double> numbers = stackalloc double[4];
        if (value is CosArray { Count: 4 } array && AnnotationValues.TryReadNumbers(annotation.Document, array, numbers)
            && numbers[0] >= 0 && numbers[1] >= 0 && numbers[2] >= 0 && numbers[3] >= 0)
        {
            return new PdfRectangleDifferences(numbers[0], numbers[1], numbers[2], numbers[3]);
        }

        Invalid(annotation, AnnotationNames.RD, "an array of four non-negative numbers");
        return null;
    }

    /// <summary>Reads a line ending name (Table 179); unknown names read as None.</summary>
    internal static PdfLineEnding ParseLineEnding(CosObject? value) => value is CosName name
        ? name.Value switch
        {
            "Square" => PdfLineEnding.Square,
            "Circle" => PdfLineEnding.Circle,
            "Diamond" => PdfLineEnding.Diamond,
            "OpenArrow" => PdfLineEnding.OpenArrow,
            "ClosedArrow" => PdfLineEnding.ClosedArrow,
            "Butt" => PdfLineEnding.Butt,
            "ROpenArrow" => PdfLineEnding.ROpenArrow,
            "RClosedArrow" => PdfLineEnding.RClosedArrow,
            "Slash" => PdfLineEnding.Slash,
            _ => PdfLineEnding.None,
        }
        : PdfLineEnding.None;

    /// <summary>Reads <c>LE</c> as two line endings, start and end (line and polyline annotations).</summary>
    internal static (PdfLineEnding Start, PdfLineEnding End) GetLineEndings(this PdfAnnotation annotation)
    {
        switch (annotation.Entry(AnnotationNames.LE))
        {
            case null:
                return (PdfLineEnding.None, PdfLineEnding.None);
            case CosArray { Count: 2 } array:
                return (ParseLineEnding(annotation.Document.Resolve(array[0])), ParseLineEnding(annotation.Document.Resolve(array[1])));
            default:
                Invalid(annotation, AnnotationNames.LE, "an array of two names");
                return (PdfLineEnding.None, PdfLineEnding.None);
        }
    }

    /// <summary>Reads the border effect dictionary <c>BE</c> (Table 169).</summary>
    internal static PdfBorderEffect? GetBorderEffect(this PdfAnnotation annotation)
    {
        if (EntryReader.Typed<CosDictionary>(annotation.Entry(AnnotationNames.BE), AnnotationNames.BE, "a dictionary", Issue(annotation)) is not { } effect)
        {
            return null;
        }

        var view = new DictionaryView(annotation.Document, effect, annotation.DiagnosticReference);
        PdfBorderEffectStyle style = view.Get(AnnotationNames.S) is CosName { Value: "C" } ? PdfBorderEffectStyle.Cloudy : PdfBorderEffectStyle.None;
        double intensity = AnnotationValues.ReadNumber(annotation.Document, view.Get(AnnotationNames.I)) ?? 0;
        if (intensity is < 0 or > 2)
        {
            annotation.Report(DiagnosticCodes.AnnotationValueInvalid, "The border effect's intensity I shall be from 0 to 2; it is clamped.");
            intensity = Math.Clamp(intensity, 0, 2);
        }

        return new PdfBorderEffect(style, intensity);
    }

    /// <summary>Reads the icon name entry <c>Name</c>, with the subtype's default.</summary>
    internal static string GetIconName(this PdfAnnotation annotation, string fallback) =>
        EntryReader.Typed<CosName>(annotation.Entry(AnnotationNames.Name), AnnotationNames.Name, "a name", Issue(annotation))?.Value ?? fallback;

    private static EntryReport Issue(PdfAnnotation annotation) => new(annotation.Document, DiagnosticCodes.AnnotationValueInvalid, annotation.DiagnosticReference, "The annotation");

    private static void Missing(PdfAnnotation annotation, CosName key, string table) =>
        annotation.Report(DiagnosticCodes.AnnotationRequiredEntryMissing, $"The {annotation.Kind} annotation has no {key.Value} entry, which {table} requires.");

    private static void Invalid(PdfAnnotation annotation, CosName key, string expected) => Issue(annotation).Ignored(key, expected);
}
