using Broadside.Diagnostics;
using Broadside.Graphics.Colors;
using Broadside.Graphics.Functions;
using Broadside.Objects;

namespace Broadside.Graphics;

/// <summary>
/// A colour space: how colour values are to be interpreted. A live view over the colour space's name or array; the subclass says
/// which family it is.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.1 to §8.6.3, Table 61. Get one with <see cref="PdfDocument.GetColorSpace"/>, from a content stream with
/// <see cref="Content.ContentContext.GetColorSpace"/>, or from the colour in the graphics state (<see cref="PdfColor.ColorSpace"/>).
/// The device spaces are singletons (<see cref="PdfDeviceGrayColorSpace.Instance"/> and its siblings); every other space is one
/// instance per colour space object of a document, shared by every caller and thread.
/// </para>
/// <para>
/// Entries are read from the COS objects on every call (ADR 0004), with the repairs lenient reading applies (a missing white point
/// reads as D65, for instance); the deviations themselves are recorded once, when the document first reads the space. A space that
/// cannot be used at all (an unknown family, an Indexed space without a lookup table, a space that refers to itself) is read as
/// DeviceGray. Converting colours to a device is <see cref="PdfColorConverter"/>'s job.
/// </para>
/// </remarks>
public abstract class PdfColorSpace
{
    private protected PdfColorSpace(ColorSpaceCache? cache, CosObject cosObject, CosReference? reference)
    {
        Cache = cache;
        CosObject = cosObject;
        Reference = reference;
    }

    /// <summary>Gets the colour space as stored: a <see cref="CosName"/> for a family named alone, else a <see cref="CosArray"/>.</summary>
    /// <remarks>ISO 32000-2 §8.6.3: "either by name or by an array"; an array's first element names the family.</remarks>
    public CosObject CosObject { get; }

    /// <summary>Gets the indirect reference the colour space was reached through, or <see langword="null"/> for a direct object.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the colour space family.</summary>
    /// <remarks>ISO 32000-2 §8.6.3, Table 61.</remarks>
    public abstract PdfColorSpaceFamily Family { get; }

    /// <summary>Gets the number of components of a colour value in this space; 0 for a Pattern space without an underlying space.</summary>
    /// <remarks>ISO 32000-2 §8.6.2, §8.6.8 (Table 73: the operand counts of <c>sc</c> and <c>scn</c>).</remarks>
    public abstract int ComponentCount { get; }

    /// <summary>Gets the earliest PDF version that has this colour space (Table 61), for a writer raising the file's version (ADR 0003).</summary>
    /// <remarks>
    /// ISO 32000-2 §8.6.3: the device spaces, CalGray, CalRGB, Lab and Indexed 1.1 (an Indexed lookup string 1.2), Pattern and
    /// Separation 1.2, ICCBased and DeviceN 1.3 (DeviceN attributes 1.6).
    /// </remarks>
    public virtual PdfVersion MinimumVersion => Family switch
    {
        PdfColorSpaceFamily.Pattern or PdfColorSpaceFamily.Separation => new PdfVersion(1, 2),
        PdfColorSpaceFamily.IccBased or PdfColorSpaceFamily.DeviceN => new PdfVersion(1, 3),
        _ => new PdfVersion(1, 1),
    };

    /// <summary>Gets the document's colour spaces, for spaces that refer to others; <see langword="null"/> for the device singletons.</summary>
    internal ColorSpaceCache? Cache { get; }

    /// <summary>Gets the object diagnostics about this space are recorded against: its own reference, else its nearest indirect owner.</summary>
    internal CosReference? DiagnosticReference => Reference ?? OwnerReference;

    /// <summary>Gets the nearest indirect object the space was found in, when the space itself is direct.</summary>
    internal CosReference? OwnerReference { get; init; }

    /// <summary>Gets a value indicating whether the space was read from an inline image, where abbreviated names are allowed (§8.9.7).</summary>
    internal bool IsInline { get; init; }

    /// <summary>Returns the range of values component <paramref name="index"/> takes.</summary>
    /// <param name="index">The component, from 0.</param>
    /// <returns>The range: 0 to 1 unless the family defines another (Lab, ICCBased, Indexed).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not less than <see cref="ComponentCount"/>.</exception>
    /// <remarks>ISO 32000-2 §8.6.2, §8.6.4 to §8.6.6.</remarks>
    public ComponentRange GetComponentRange(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ComponentCount);
        return GetRangeCore(index);
    }

    /// <summary>Returns the colour <c>CS</c> and <c>cs</c> select along with this space.</summary>
    /// <returns>The initial colour.</returns>
    /// <remarks>
    /// ISO 32000-2 §8.6.8, Table 73 and §8.6.4 to §8.6.6: 0 for DeviceGray, CalGray and Indexed, 0 0 0 for DeviceRGB and CalRGB,
    /// 0 0 0 1 for DeviceCMYK; for Lab and ICCBased each component 0 clipped into its range; 1.0 for every component of Separation
    /// and DeviceN; for Pattern, a colour with no pattern, which paints nothing.
    /// </remarks>
    public PdfColor GetInitialColor()
    {
        int count = Math.Min(ComponentCount, PdfColor.MaxComponents);
        Span<float> components = stackalloc float[count];
        for (int i = 0; i < count; i++)
        {
            components[i] = (float)GetInitialComponent(i);
        }

        return new PdfColor(this, components);
    }

    /// <summary>Returns the default <c>Decode</c> array of an image whose samples are in this space.</summary>
    /// <param name="bitsPerComponent">The image's bits per component (an Indexed space maps 0 to 2^bits − 1 onto the table).</param>
    /// <returns>2 × <see cref="ComponentCount"/> numbers; empty for a Pattern space, which images cannot use.</returns>
    /// <remarks>ISO 32000-2 §8.9.5.2, Table 88.</remarks>
    public double[] GetDefaultDecode(int bitsPerComponent)
    {
        if (Family == PdfColorSpaceFamily.Pattern)
        {
            return [];
        }

        if (Family == PdfColorSpaceFamily.Indexed)
        {
            return [0, Math.Pow(2, Math.Clamp(bitsPerComponent, 1, 16)) - 1];
        }

        double[] decode = new double[2 * ComponentCount];
        for (int i = 0; i < ComponentCount; i++)
        {
            ComponentRange range = GetRangeCore(i);
            decode[2 * i] = range.Minimum;
            decode[(2 * i) + 1] = range.Maximum;
        }

        return decode;
    }

    /// <summary>Returns the range of component <paramref name="index"/>, already checked.</summary>
    /// <param name="index">The component.</param>
    /// <returns>The range.</returns>
    private protected virtual ComponentRange GetRangeCore(int index) => new(0, 1);

    /// <summary>Returns the initial value of component <paramref name="index"/>.</summary>
    /// <param name="index">The component.</param>
    /// <returns>0 clipped into the component's range, unless the family says otherwise.</returns>
    private protected virtual double GetInitialComponent(int index) => GetRangeCore(index).Clamp(0);

    /// <summary>Records the deviations of this space, once, when the document first reads it.</summary>
    /// <returns><see langword="false"/> when the space cannot be used at all and is read as DeviceGray instead.</returns>
    internal virtual bool Validate() => true;

    /// <summary>Adds the containers this space's conversion reads, so a converter built from it notices when they change.</summary>
    /// <param name="dependencies">The list to add to.</param>
    internal virtual void AddDependencies(List<FunctionDependency> dependencies)
    {
        if (CosObject is CosArray array)
        {
            dependencies.Add(new FunctionDependency(array, array.Version));
        }
    }

    /// <summary>Records a deviation found in this space.</summary>
    /// <param name="code">The diagnostic code.</param>
    /// <param name="message">What was found and what is done about it.</param>
    /// <param name="severity">The severity; Warning unless given.</param>
    internal void Report(string code, string message, DiagnosticSeverity severity = DiagnosticSeverity.Warning) =>
        new ObjectDiagnostics(Cache?.Diagnostics, DiagnosticReference).Report(code, message, severity);

    /// <summary>
    /// Returns the colour space <paramref name="value"/> describes, read by this space's document with diagnostics on this space (a
    /// DeviceN Process dictionary's space); <see langword="null"/> outside a document, for a cycle or nesting too deep.
    /// </summary>
    /// <param name="value">The space's name or array, or a reference to it.</param>
    /// <returns>The space.</returns>
    internal PdfColorSpace? FindRelatedSpace(CosObject value) => Cache?.Find(value, DiagnosticReference, inline: false, out _);

    /// <summary>Returns the nested colour space at array element <paramref name="index"/>, or <see langword="null"/> when there is none.</summary>
    /// <param name="index">The element.</param>
    /// <returns>The space as the document reads it; <see langword="null"/> for a missing element, a cycle or nesting too deep.</returns>
    private protected PdfColorSpace? NestedSpace(int index) =>
        CosObject is CosArray array && index < array.Count ? Cache!.Find(array[index], DiagnosticReference, IsInline, out _) : null;

    /// <summary>Returns the array element at <paramref name="index"/>, resolved; the null object when absent.</summary>
    /// <param name="index">The element.</param>
    /// <returns>The element.</returns>
    private protected CosObject Element(int index) =>
        CosObject is CosArray array && index < array.Count ? Cache!.Resolve(array[index]) : CosNull.Instance;

    /// <summary>Returns the unresolved array element at <paramref name="index"/>; the null object when absent.</summary>
    /// <param name="index">The element.</param>
    /// <returns>The element as stored.</returns>
    private protected CosObject RawElement(int index) =>
        CosObject is CosArray array && index < array.Count ? array[index] : CosNull.Instance;
}
