using System.Globalization;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>
/// The indirect objects a document's canvases and drawing factories created, keyed by what they hold, so drawing the same
/// graphics state parameters or creating the same colour space again reuses one object.
/// </summary>
/// <remarks>
/// A key is reused only while its reference still resolves to the object created for it (an object removed or replaced through
/// <see cref="PdfDocument.RemoveObject"/> or <see cref="PdfDocument.AddObject"/> is created again). Used only by mutations, which are
/// single-threaded (the document's concurrency contract), so it takes no lock.
/// </remarks>
internal sealed class DrawingCache(PdfDocument document)
{
    private static readonly CosName ExtGStateType = new("ExtGState");
    private readonly Dictionary<string, (CosReference Reference, CosObject Value)> _objects = new(StringComparer.Ordinal);

    /// <summary>Returns the graphics state parameter dictionary <c>&lt;&lt; /Type /ExtGState /key value &gt;&gt;</c> (§8.4.5, Table 57).</summary>
    public CosReference GraphicsState(string key, CosObject value) =>
        GetOrAdd(
            $"ExtGState|{key}|{value switch { CosReal real => real.Value.ToString("R", CultureInfo.InvariantCulture), CosName name => name.Value, _ => value.ToString() }}",
            () => new CosDictionary { [KnownNames.Type] = ExtGStateType, [new CosName(key)] = value });

    /// <summary>Returns the object created for <paramref name="key"/>, creating and adding it when there is none; a <see langword="null"/> key always creates.</summary>
    public CosReference GetOrAdd(string? key, Func<CosObject> create)
    {
        if (key is not null && _objects.TryGetValue(key, out (CosReference Reference, CosObject Value) known)
            && ReferenceEquals(document.Resolve(known.Reference), known.Value))
        {
            return known.Reference;
        }

        CosObject value = create();
        CosReference reference = document.AddObject(value);
        if (key is not null)
        {
            _objects[key] = (reference, value);
        }

        return reference;
    }

    /// <summary>The value a colour space is written as where another refers to it: its reference, else the object.</summary>
    public static CosObject Value(PdfColorSpace space) => (CosObject?)space.Reference ?? space.CosObject;

    /// <summary>A key for a colour space: its family for a device space, its reference otherwise; <see langword="null"/> for a direct space.</summary>
    public static string? Key(PdfColorSpace space) => space.Family switch
    {
        PdfColorSpaceFamily.DeviceGray or PdfColorSpaceFamily.DeviceRgb or PdfColorSpaceFamily.DeviceCmyk => space.Family.ToString(),
        _ => space.Reference is { } reference ? Key(reference) : null,
    };

    /// <summary>A key for an indirect object.</summary>
    public static string Key(CosReference reference) => string.Create(CultureInfo.InvariantCulture, $"{reference.ObjectNumber} {reference.Generation} R");

    /// <summary>A number as an integer when it is one, else a real.</summary>
    public static CosNumber Number(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < int.MaxValue ? new CosInteger((long)value) : new CosReal(value);

    /// <summary>An array of numbers.</summary>
    public static CosArray Numbers(ReadOnlySpan<double> values)
    {
        var array = new CosArray();
        foreach (double value in values)
        {
            array.Add(Number(value));
        }

        return array;
    }

    /// <summary>A rectangle as <c>[left bottom right top]</c> (§7.9.5).</summary>
    public static CosArray Rectangle(PdfRectangle box) => Numbers([box.Left, box.Bottom, box.Right, box.Top]);

    /// <summary>A number as PostScript calculator text: invariant, at most six decimals, no exponent.</summary>
    public static string Format(double value) => Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);
}
