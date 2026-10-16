using System.Globalization;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <summary>Writing graphics (issue #154): soft-mask dictionaries a <see cref="PdfCanvas"/> sets in the graphics state.</summary>
public sealed partial class PdfDocument
{
    private static readonly CosName MaskType = new("Mask");
    private static readonly CosName SoftMaskSubtype = new("S");
    private static readonly CosName SoftMaskGroup = new("G");
    private static readonly CosName SoftMaskBackdrop = new("BC");
    private static readonly CosName SoftMaskTransfer = new("TR");

    /// <summary>
    /// Creates a soft-mask dictionary whose mask values come from a transparency group XObject, or returns the one this document
    /// already has for the same values.
    /// </summary>
    /// <param name="type">How the values are derived: the group's alpha, or its luminosity over the backdrop.</param>
    /// <param name="group">
    /// A transparency group XObject of this document (<see cref="PdfFormXObject.CreateTransparencyGroup"/>); for a luminosity mask its
    /// group colour space shall be set. Its bounding box bounds the mask; its matrix maps it into the coordinate system of the
    /// <c>gs</c> that sets the mask.
    /// </param>
    /// <param name="backdrop">
    /// For a luminosity mask, the backdrop colour (<c>BC</c>) the group composites over, one component per component of the group
    /// colour space; <see langword="null"/>, the default, is that space's initial colour, black. Not allowed for an alpha mask.
    /// </param>
    /// <param name="transferFunction">
    /// The transfer function (<c>TR</c>) applied to the alpha or luminosity, a function of this document from 1 input to 1 output;
    /// <see langword="null"/>, the default, is the identity.
    /// </param>
    /// <returns>The mask: a new indirect <c>&lt;&lt; /Type /Mask /S ... /G ... &gt;&gt;</c>, to set with <see cref="PdfCanvas.SetSoftMask"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="type"/> is not a defined type.</exception>
    /// <exception cref="ArgumentException">
    /// The group belongs to another document, is not indirect, or is not a transparency group; a luminosity mask's group has no colour
    /// space; a backdrop is given for an alpha mask, does not have one finite component per component of the group colour space; the
    /// transfer function is another document's, cannot be used, or does not take 1 input to 1 output.
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §11.6.5.1, Table 142 (PDF 1.4), §11.5.2 and §11.5.3. Outside the group's bounding box the mask value is the transfer
    /// function of 0 (alpha) or of the backdrop's luminosity (luminosity). Records PDF 1.4 and the function's version. A mutation.
    /// </remarks>
    public PdfSoftMask CreateSoftMask(PdfSoftMaskType type, PdfFormXObject group, IReadOnlyList<double>? backdrop = null, PdfFunction? transferFunction = null)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not a defined soft-mask type.");
        }

        ArgumentNullException.ThrowIfNull(group);
        if (!ReferenceEquals(group.Document, this))
        {
            throw new ArgumentException("The group belongs to another document.", nameof(group));
        }

        if (group.Reference is not { } groupReference)
        {
            throw new ArgumentException("A soft mask's group shall be an indirect stream (ISO 32000-2 §7.3.8).", nameof(group));
        }

        if (group.TransparencyGroup is not { } attributes)
        {
            throw new ArgumentException(
                "A soft mask's G shall be a transparency group XObject (ISO 32000-2 Table 142); call CreateTransparencyGroup on the form first.",
                nameof(group));
        }

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = MaskType,
            [SoftMaskSubtype] = new CosName(type == PdfSoftMaskType.Alpha ? "Alpha" : "Luminosity"),
            [SoftMaskGroup] = groupReference,
        };
        string? key = $"SMask|{type}|{DrawingCache.Key(groupReference)}";
        if (type == PdfSoftMaskType.Luminosity)
        {
            if (attributes.ColorSpace is not { } space)
            {
                throw new ArgumentException(
                    "A luminosity soft mask's group shall have a group colour space (CS) to composite in (ISO 32000-2 Table 142); set its ColorSpace.",
                    nameof(group));
            }

            if (backdrop is not null)
            {
                if (backdrop.Count != space.ComponentCount || backdrop.Any(static value => !double.IsFinite(value)))
                {
                    throw new ArgumentException(
                        string.Create(CultureInfo.InvariantCulture, $"The backdrop shall have {space.ComponentCount} finite component(s), one per component of the group colour space."),
                        nameof(backdrop));
                }

                CosArray components = DrawingCache.Numbers([.. backdrop]);
                dictionary[SoftMaskBackdrop] = components;
                key += "|" + components;
            }
        }
        else if (backdrop is not null)
        {
            throw new ArgumentException("A backdrop (BC) applies to a luminosity soft mask only (ISO 32000-2 Table 142).", nameof(backdrop));
        }

        if (transferFunction is not null)
        {
            if (!ReferenceEquals(transferFunction.Document, this))
            {
                throw new ArgumentException("The transfer function belongs to another document.", nameof(transferFunction));
            }

            if (!transferFunction.IsValid || transferFunction.InputCount != 1 || transferFunction.OutputCount != 1)
            {
                throw new ArgumentException("The transfer function shall be a usable function of 1 input to 1 output (ISO 32000-2 Table 142).", nameof(transferFunction));
            }

            RequireFeature(transferFunction.FunctionType == PdfFunctionType.Sampled ? VersionedFeatures.SampledFunction : VersionedFeatures.CalculatedFunction);
            dictionary[SoftMaskTransfer] = (CosObject?)transferFunction.Reference ?? transferFunction.CosObject;
            key = transferFunction.Reference is { } functionReference ? key + "|" + DrawingCache.Key(functionReference) : null;
        }

        RequireFeature(VersionedFeatures.SoftMasks);
        CosReference reference = Drawing.GetOrAdd(key, () => dictionary);
        return new PdfSoftMask(this, (CosDictionary)Resolve(reference), reference);
    }
}
