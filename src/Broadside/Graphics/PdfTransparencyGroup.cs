using Broadside.Annotations;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside.Graphics;

/// <summary>
/// A transparency group attributes dictionary: the <c>Group</c> of a form XObject, which makes it a transparency group XObject, or of
/// a page, which describes the page group. A live view over the dictionary; setting a property writes its entry.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.10.3, Table 94, and §11.6.6, Table 145 (PDF 1.4). Get one with <see cref="PdfFormXObject.TransparencyGroup"/> or
/// <see cref="PdfPage.TransparencyGroup"/>, or create one with <see cref="PdfFormXObject.CreateTransparencyGroup"/> or
/// <see cref="PdfPage.CreateTransparencyGroup"/>. Properties read the dictionary on every call and return the table's default when an
/// entry is absent or malformed. For a page group, <see cref="IsIsolated"/> is written but ignored: a page imposed on the output
/// medium is effectively isolated (§11.4.7).
/// </para>
/// <para>Setting a property is a mutation: no read may run concurrently with it.</para>
/// </remarks>
public sealed class PdfTransparencyGroup
{
    private static readonly CosName I = new("I");
    private static readonly CosName K = new("K");
    private static readonly CosName CS = new("CS");
    private static readonly CosName Transparency = new("Transparency");

    private readonly PdfDocument _document;
    private readonly CosReference? _owner;

    internal PdfTransparencyGroup(PdfDocument document, CosDictionary dictionary, CosReference? owner)
    {
        _document = document;
        _owner = owner;
        Dictionary = dictionary;
    }

    /// <summary>Gets the group attributes dictionary.</summary>
    /// <remarks>ISO 32000-2 §8.10.3, Table 94.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the group is isolated (<c>I</c>): its objects composite against a fully transparent
    /// initial backdrop instead of the group's backdrop. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §11.6.6, Table 145 (<c>I</c>), and §11.4.5. Setting <see langword="false"/> removes the entry. A mutation.</remarks>
    public bool IsIsolated
    {
        get => _document.Resolve(Get(I)) is CosBoolean { Value: true };
        set => SetFlag(I, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the group is a knockout group (<c>K</c>): each object composites with the group's initial
    /// backdrop and overwrites earlier overlapping objects. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §11.6.6, Table 145 (<c>K</c>), and §11.4.6. Setting <see langword="false"/> removes the entry. A mutation.</remarks>
    public bool IsKnockout
    {
        get => _document.Resolve(Get(K)) is CosBoolean { Value: true };
        set => SetFlag(K, value);
    }

    /// <summary>
    /// Gets or sets the group colour space (<c>CS</c>): the space colours are converted into when painted into the group, the blending
    /// space, and the space of the group as a whole; <see langword="null"/>, the default, inherits the parent group's or page's.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// On set, the space belongs to another document, or cannot be a blending colour space: only DeviceGray, DeviceRGB, DeviceCMYK,
    /// CalGray, CalRGB and gray, RGB or CMYK ICCBased spaces can (not Lab, a Lab profile, Indexed, Pattern, Separation or DeviceN).
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §11.6.6, Table 145 (<c>CS</c>), and §11.3.4. Ordinarily present only for isolated groups and page groups; it shall
    /// be present in the group of a luminosity soft mask (§11.6.5.1), which has no parent to inherit from. Setting records the space's
    /// minimum version; <see langword="null"/> removes the entry. A mutation.
    /// </remarks>
    public PdfColorSpace? ColorSpace
    {
        get => Get(CS) is { } value ? _document.ColorSpaces.Get(value, _owner) : null;
        set
        {
            if (value is null)
            {
                Dictionary.Remove(CS);
                return;
            }

            ThrowIfNotBlendingSpace(_document, value, nameof(value));
            if (VersionedFeatures.ColorSpace(value) is { } feature)
            {
                _document.RequireFeature(feature);
            }

            Dictionary[CS] = DrawingCache.Value(value);
        }
    }

    /// <summary>Returns the view over <paramref name="value"/> when it resolves to a group attributes dictionary of subtype <c>Transparency</c>.</summary>
    internal static PdfTransparencyGroup? Read(PdfDocument document, CosObject? value, CosReference? owner) =>
        document.Resolve(value) is CosDictionary dictionary && IsTransparency(document, dictionary)
            ? new PdfTransparencyGroup(document, dictionary, owner)
            : null;

    /// <summary>
    /// Returns the transparency group <paramref name="holder"/>'s <c>Group</c> holds, after writing a new direct
    /// <c>&lt;&lt; /Type /Group /S /Transparency &gt;&gt;</c> there when it holds none (or a group of another subtype). Records PDF 1.4.
    /// </summary>
    internal static PdfTransparencyGroup Create(PdfDocument document, CosDictionary holder, CosReference? owner)
    {
        document.RequireFeature(VersionedFeatures.TransparencyGroups);
        if (Read(document, holder.TryGetValue(AnnotationNames.Group, out CosObject? existing) ? existing : null, owner) is { } group)
        {
            return group;
        }

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = AnnotationNames.Group,
            [AnnotationNames.S] = Transparency,
        };
        holder[AnnotationNames.Group] = dictionary;
        return new PdfTransparencyGroup(document, dictionary, owner);
    }

    /// <summary>Throws unless <paramref name="space"/> is of <paramref name="document"/> and can be a blending colour space (§11.3.4).</summary>
    internal static void ThrowIfNotBlendingSpace(PdfDocument document, PdfColorSpace space, string parameter)
    {
        ArgumentNullException.ThrowIfNull(space, parameter);
        if (space.Cache is { } cache && !ReferenceEquals(cache.Document, document))
        {
            throw new ArgumentException("The colour space belongs to another document.", parameter);
        }

        bool blends = space switch
        {
            { Family: PdfColorSpaceFamily.DeviceGray or PdfColorSpaceFamily.DeviceRgb or PdfColorSpaceFamily.DeviceCmyk } => true,
            { Family: PdfColorSpaceFamily.CalGray or PdfColorSpaceFamily.CalRgb } => true,
            PdfIccBasedColorSpace icc => icc.ComponentCount is 1 or 3 or 4 && !icc.IsLabProfile,
            _ => false,
        };
        if (!blends)
        {
            throw new ArgumentException(
                $"A {space.Family} space cannot be a group colour space: it shall be a device or CIE-based space with independent additive or subtractive components (ISO 32000-2 §11.3.4, Table 145).",
                parameter);
        }
    }

    private static bool IsTransparency(PdfDocument document, CosDictionary dictionary) =>
        Transparency.Equals(document.Resolve(dictionary.TryGetValue(AnnotationNames.S, out CosObject? subtype) ? subtype : null));

    private CosObject? Get(CosName key) => Dictionary.TryGetValue(key, out CosObject? value) ? value : null;

    private void SetFlag(CosName key, bool value)
    {
        if (value)
        {
            Dictionary[key] = CosBoolean.True;
        }
        else
        {
            Dictionary.Remove(key);
        }
    }
}
