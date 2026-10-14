using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Rendering;

/// <summary>
/// Transparency groups (ISO 32000-2 §11.4): group XObjects (§11.6.6), the page group (§11.4.7) and the implicit knockout group of a
/// combined fill and stroke (§11.7.4.4) become <see cref="DisplayOperationKind.DrawGroup"/> records whose content is the operations
/// that follow, up to the group's end.
/// </summary>
internal sealed partial class DisplayListBuilder
{
    private static readonly CosName GroupName = new("Group");
    private static readonly CosName GroupSubtypeName = new("S");
    private static readonly CosName TransparencyName = new("Transparency");
    private static readonly CosName KnockoutName = new("K");
    private static readonly CosName GroupColorSpaceName = new("CS");

    private DisplayGroupEntry[] _groups = new DisplayGroupEntry[4];
    private int _groupCount;

    // The open groups of transparency group XObjects, innermost last (indexes into _groups).
    private int[] _openForms = new int[8];
    private int _openFormCount;

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §11.6.6: a transparency group XObject opens a group with the attributes of its group dictionary (I, K, and CS when
    /// isolated) and the nonstroking alpha and blend mode at <c>Do</c>, which composite its result; its content (from the implicit
    /// save the form runs in) follows. A form without a transparency group paints as before, without one (§8.10.3).
    /// </remarks>
    public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
    {
        if (!form.IsTransparencyGroup)
        {
            return ContentVisit.Enter;
        }

        PdfDocument document = context.Document;
        PdfColorSpace? space = form.IsIsolated ? BlendingSpace(form.GroupColorSpace, document, form.Reference) : null;
        ref readonly GraphicsState state = ref context.State;
        int group = OpenGroup(new DisplayGroupEntry(
            DisplayGroupKind.Transparency,
            form.IsIsolated,
            form.IsKnockout,
            space,
            context.DefaultColorSpaces,
            ColorConversion.FromState(state, DeviceColorModel.Rgb),
            form.FillAlpha,
            form.BlendMode,
            form.HasBoundingBox,
            form.BoundingBox,
            form.Matrix * state.Ctm,
            0,
            0));
        Append(ref _openForms, ref _openFormCount, group);
        return ContentVisit.Enter;
    }

    /// <inheritdoc/>
    public override void EndForm(in FormEvent form, ContentContext context)
    {
        if (form.IsTransparencyGroup && _openFormCount > 0)
        {
            CloseGroup(_openForms[--_openFormCount]);
        }
    }

    /// <summary>
    /// Opens the page group (§11.4.7) when the page dictionary has a transparency <c>Group</c>: isolated whatever its <c>I</c> says,
    /// knockout as its <c>K</c> says, blending in its <c>CS</c>. The content follows inside a save, closed by <see cref="ClosePageGroup"/>.
    /// </summary>
    /// <returns>The group's index, or −1 when the page has no transparency group.</returns>
    private int OpenPageGroup(PdfPage page, PdfDocument document)
    {
        if (document.Resolve(page.Dictionary.TryGetValue(GroupName, out CosObject? value) ? value : null) is not CosDictionary group
            || !TransparencyName.Equals(document.Resolve(group.TryGetValue(GroupSubtypeName, out CosObject? subtype) ? subtype : null)))
        {
            return -1;
        }

        bool knockout = document.Resolve(group.TryGetValue(KnockoutName, out CosObject? k) ? k : null) is CosBoolean { Value: true };
        PdfColorSpace? space = group.TryGetValue(GroupColorSpaceName, out CosObject? cs)
            ? BlendingSpace(document.ColorSpaces.Get(cs, page.Reference), document, page.Reference)
            : null;
        int index = OpenGroup(new DisplayGroupEntry(
            DisplayGroupKind.Page,
            IsIsolated: true,
            knockout,
            space,
            document.ColorSpaces.GetDefaults(page.Resources),
            new ColorConversion { Target = DeviceColorModel.Rgb, Intent = RenderingIntent.RelativeColorimetric },
            Opacity: 1,
            BlendMode.Normal,
            HasBoundingBox: false,
            default,
            Matrix.Identity,
            0,
            0));
        Save();
        return index;
    }

    /// <summary>Closes the page group opened by <see cref="OpenPageGroup"/>.</summary>
    private void ClosePageGroup(int group)
    {
        if (group >= 0)
        {
            Restore();
            CloseGroup(group);
        }
    }

    /// <summary>
    /// Opens the implicit group of a combined fill and stroke (§11.7.4.4) when it changes the result: a non-isolated knockout group
    /// composited at alpha 1 with Normal, needed only when an alpha is below 1 or the blend mode is not Normal (an opaque stroke in
    /// the Normal mode over its fill is the same either way). The content follows inside a save.
    /// </summary>
    /// <returns>The group's index, or −1 when no group is needed.</returns>
    /// <remarks>Overprint (the first case of §11.7.4.4) is not simulated, so the knockout case always applies.</remarks>
    private int OpenFillAndStrokeGroup(in GraphicsState state, PdfDefaultColorSpaces defaults, PdfRectangle bounds)
    {
        if (state.StrokeAlpha >= 1 && state.FillAlpha >= 1 && state.BlendMode == BlendMode.Normal)
        {
            return -1;
        }

        int index = OpenGroup(new DisplayGroupEntry(
            DisplayGroupKind.FillAndStroke,
            IsIsolated: false,
            IsKnockout: true,
            ColorSpace: null,
            defaults,
            ColorConversion.FromState(state, DeviceColorModel.Rgb),
            Opacity: 1,
            BlendMode.Normal,
            HasBoundingBox: true,
            bounds,
            state.Ctm,
            0,
            0));
        Save();
        return index;
    }

    /// <summary>Closes a group opened by <see cref="OpenFillAndStrokeGroup"/>, replacing its bounds when <paramref name="bounds"/> is given.</summary>
    private void CloseFillAndStrokeGroup(int group, PdfRectangle? bounds = null)
    {
        if (group < 0)
        {
            return;
        }

        Restore();
        if (bounds is { } box)
        {
            _groups[group] = _groups[group] with { BoundingBox = box };
        }

        CloseGroup(group);
    }

    /// <summary>The reach of a stroke beyond its path in user space: half the line width times the largest corner or cap extension.</summary>
    private static double StrokeReach(in GraphicsState state)
    {
        double factor = state.LineJoin == LineJoin.Miter ? Math.Max(state.MiterLimit, Math.Sqrt(2)) : Math.Sqrt(2);
        double reach = Math.Abs(state.LineWidth) / 2 * factor;
        return double.IsFinite(reach) ? reach : 0;
    }

    /// <summary>The bounds of a path's points (its control polygon holds every curve), widened by <paramref name="reach"/>.</summary>
    private static PdfRectangle Bounds(ReadOnlySpan<PathPoint> points, double reach)
    {
        if (points.IsEmpty)
        {
            return default;
        }

        double left = double.PositiveInfinity;
        double bottom = double.PositiveInfinity;
        double right = double.NegativeInfinity;
        double top = double.NegativeInfinity;
        foreach (PathPoint point in points)
        {
            left = Math.Min(left, point.X);
            right = Math.Max(right, point.X);
            bottom = Math.Min(bottom, point.Y);
            top = Math.Max(top, point.Y);
        }

        return new PdfRectangle(left - reach, bottom - reach, right + reach, top + reach);
    }

    /// <summary>Records the group and its operation; the content follows.</summary>
    private int OpenGroup(in DisplayGroupEntry entry)
    {
        int index = _groupCount;
        Append(ref _groups, ref _groupCount, entry with { Start = _operationCount + 1, End = _operationCount + 1 });
        AddOperation(new DisplayOperation(DisplayOperationKind.DrawGroup, default, index, 0));
        return index;
    }

    /// <summary>Ends a group's content; a group whose content painted nothing (its save and restore dropped) is dropped with its record.</summary>
    private void CloseGroup(int index)
    {
        ref DisplayGroupEntry entry = ref _groups[index];
        if (_operationCount <= entry.Start)
        {
            // Nothing painted: the group was the last record and the last group.
            _operationCount = entry.Start - 1;
            _groupCount = index;
            return;
        }

        entry = entry with { End = _operationCount };
    }

    /// <summary>
    /// Returns the group colour space if it can be a blending colour space (§11.3.4: DeviceGray, DeviceRGB, DeviceCMYK, CalGray,
    /// CalRGB, ICCBased gray, RGB and CMYK), else <see langword="null"/> with <c>GroupColorSpaceInvalid</c>: the group then blends
    /// in its parent's space.
    /// </summary>
    private static PdfColorSpace? BlendingSpace(PdfColorSpace? space, PdfDocument document, CosReference? owner)
    {
        bool valid = space switch
        {
            null => true,
            { Family: PdfColorSpaceFamily.DeviceGray or PdfColorSpaceFamily.DeviceRgb or PdfColorSpaceFamily.DeviceCmyk } => true,
            { Family: PdfColorSpaceFamily.CalGray or PdfColorSpaceFamily.CalRgb } => true,
            PdfIccBasedColorSpace icc => icc.ComponentCount is 1 or 3 or 4 && !icc.IsLabProfile,
            _ => false,
        };
        if (valid)
        {
            return space;
        }

        document.DiagnosticSink.ReportOnce(
            DiagnosticCodes.GroupColorSpaceInvalid,
            DiagnosticSeverity.Warning,
            $"A transparency group's colour space ({space!.Family}) cannot be a blending colour space (§11.3.4); the group blends in its parent's.",
            offset: null,
            owner);
        return null;
    }

    /// <summary>Forgets the groups, releasing the colour spaces they referenced.</summary>
    private void ResetGroups()
    {
        Array.Clear(_groups, 0, _groupCount);
        _groupCount = 0;
        _openFormCount = 0;
    }
}
