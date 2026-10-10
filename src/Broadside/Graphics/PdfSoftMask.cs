using Broadside.Annotations;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>How a soft mask derives its values from its transparency group.</summary>
/// <remarks>ISO 32000-2 §11.6.5.1, Table 142 (<c>S</c>).</remarks>
public enum PdfSoftMaskType
{
    /// <summary><c>Alpha</c>: the group's computed alpha, its colour disregarded (§11.5.2).</summary>
    Alpha,

    /// <summary><c>Luminosity</c>: the group's colour, composited over the backdrop <c>BC</c>, as a luminosity (§11.5.3).</summary>
    Luminosity,
}

/// <summary>A soft-mask dictionary, the value of a graphics state's soft mask: a live view over the dictionary.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §11.6.5.1, Table 142. The mask's group is painted in the coordinate system of the CTM current at the <c>gs</c> that
/// set the mask, recorded as <see cref="GraphicsState.SoftMaskMatrix"/>, concatenated with the group's own <c>Matrix</c>.
/// </para>
/// <para>
/// Properties read the dictionary on every call and fall back to the table's defaults when an entry is absent or malformed; the
/// content interpreter reports a malformed mask when <c>gs</c> sets it.
/// </para>
/// </remarks>
public sealed class PdfSoftMask
{
    private static readonly CosName S = new("S");
    private static readonly CosName G = new("G");
    private static readonly CosName BC = new("BC");
    private static readonly CosName TR = new("TR");
    private static readonly CosName Luminosity = new("Luminosity");
    private static readonly CosName Alpha = new("Alpha");

    private readonly PdfDocument _document;

    internal PdfSoftMask(PdfDocument document, CosDictionary dictionary, CosReference? reference)
    {
        _document = document;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the soft-mask dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the dictionary, or <see langword="null"/> when it is direct.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets how the mask values are derived; <see cref="PdfSoftMaskType.Luminosity"/> unless <c>S</c> is <c>Alpha</c>.</summary>
    /// <remarks>ISO 32000-2 Table 142 (<c>S</c>, required).</remarks>
    public PdfSoftMaskType Subtype => Alpha.Equals(Get(S)) ? PdfSoftMaskType.Alpha : PdfSoftMaskType.Luminosity;

    /// <summary>Gets the transparency group XObject the mask values come from, or <see langword="null"/> when <c>G</c> is not a stream.</summary>
    /// <remarks>ISO 32000-2 Table 142 (<c>G</c>, required) and §11.6.6.</remarks>
    public PdfFormXObject? Group => Dictionary.TryGetValue(G, out CosObject? value) ? PdfFormXObject.Create(_document, value) : null;

    /// <summary>
    /// Gets the backdrop colour components for a luminosity mask, in the group's colour space; <see langword="null"/> when absent
    /// (the space's initial colour, black).
    /// </summary>
    /// <remarks>ISO 32000-2 Table 142 (<c>BC</c>).</remarks>
    public IReadOnlyList<double>? Backdrop => AnnotationValues.ReadNumbers(_document, Get(BC), out _);

    /// <summary>
    /// Gets the transfer function applied to the alpha or luminosity, or <see langword="null"/> for the identity (absent,
    /// <c>/Identity</c>, or not a function).
    /// </summary>
    /// <remarks>ISO 32000-2 Table 142 (<c>TR</c>, default Identity).</remarks>
    public PdfFunction? TransferFunction => Get(TR) is CosDictionary or CosStream ? _document.Functions.Get(Dictionary[TR], 1, 1) : null;

    /// <summary>Gets a value indicating whether the dictionary is usable: <c>S</c> is Alpha or Luminosity and <c>G</c> a stream.</summary>
    internal bool IsValid => (Alpha.Equals(Get(S)) || Luminosity.Equals(Get(S))) && _document.Resolve(Get(G)) is CosStream;

    private CosObject? Get(CosName key) => Dictionary.TryGetValue(key, out CosObject? value) ? _document.Resolve(value) : null;
}
