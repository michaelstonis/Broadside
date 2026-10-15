using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Rendering;

/// <summary>
/// Soft masks in the graphics state (ISO 32000-2 §11.5, §11.6.4.3, §11.6.5.1): a state-only <see cref="DisplayOperationKind.SetSoftMask"/>
/// record before a paint whose mask differs from the one the backend holds, and the mask at <c>Do</c> on a transparency group's
/// record. Each mask's group is built once per page into a display list of its own, in mask space.
/// </summary>
/// <remarks>
/// The group is built by a child builder (one per nesting level, kept with its parent so warm builds reuse its tables) that receives
/// its events from <see cref="ContentContext.RunSoftMaskGroup"/>, which runs it from the initial graphics state (so no transfer
/// function, alpha or mask of the page reaches it) with the CTM of the <c>gs</c> that set the mask. Like a tiling cell (#112) the
/// child records relative to that CTM (<see cref="Relative"/>), so one list serves the mask wherever it was set; the record carries
/// the CTM. The lists of one page build are cached in a <see cref="SoftMaskLists"/> shared by the root builder and its children.
/// </remarks>
internal sealed partial class DisplayListBuilder
{
    private static readonly CosName IsolatedName = new("I");

    private DisplaySoftMaskEntry[] _softMasks = new DisplaySoftMaskEntry[4];
    private int _softMaskCount;

    // The soft mask the backend holds at this point of the replay (−1 for none), and the ones saved by the open SaveState records.
    private int _softMaskHeld = -1;
    private int[] _savedSoftMasks = new int[16];
    private int _savedSoftMaskCount;

    // The last mask resolved to a record, so that a run of paints under one mask adds one entry.
    private PdfSoftMask? _lastMask;
    private Matrix _lastMaskMatrix;
    private bool _lastMaskShape;
    private int _lastMaskIndex = -1;

    // The mask groups of the page being built: the root builder's own, borrowed by its children while they build a mask or cell.
    private SoftMaskLists? _ownMasks;
    private SoftMaskLists? _masks;
    private DisplayListBuilder? _maskBuilder;

    /// <summary>
    /// Records the soft mask of <paramref name="state"/> before a paint, when it differs from the backend's: none when
    /// <paramref name="overridden"/> (an image with a mask of its own, §11.6.4.3).
    /// </summary>
    private void SetSoftMask(in GraphicsState state, ContentContext context, bool overridden)
    {
        int index = overridden ? -1 : SoftMaskIndex(state.SoftMask, state.SoftMaskMatrix, state.AlphaIsShape, context);
        SetSoftMaskRecord(index);
    }

    /// <summary>Records <see cref="DisplayOperationKind.SetSoftMask"/> with the entry <paramref name="index"/> when it differs from the backend's.</summary>
    private void SetSoftMaskRecord(int index)
    {
        if (SameSoftMask(index, _softMaskHeld))
        {
            return;
        }

        _softMaskHeld = index;
        AddOperation(new DisplayOperation(DisplayOperationKind.SetSoftMask, default, index, 0));
    }

    private bool SameSoftMask(int a, int b) => a == b || (a >= 0 && b >= 0 && _softMasks[a] == _softMasks[b]);

    /// <summary>
    /// Returns the soft-mask entry for <paramref name="mask"/> set at <paramref name="matrix"/>, building its group the first time
    /// this page needs it; −1 for no mask, or when the group cannot run (recursion and nesting depth are recorded by the interpreter).
    /// </summary>
    private int SoftMaskIndex(PdfSoftMask? mask, in Matrix matrix, bool shape, ContentContext context)
    {
        if (mask is null)
        {
            return -1;
        }

        if (ReferenceEquals(mask, _lastMask) && matrix == _lastMaskMatrix && shape == _lastMaskShape)
        {
            return _lastMaskIndex;
        }

        int index = -1;
        if (GetMaskGroup(mask, matrix, context) is { } built)
        {
            Append(ref _softMasks, ref _softMaskCount, new DisplaySoftMaskEntry(
                built.Subtype,
                built.List,
                Relative(matrix),
                built.ColorSpace,
                built.Backdrop,
                built.DefaultColorSpaces,
                built.Conversion,
                built.TransferFunction,
                shape,
                built.HasBoundingBox,
                built.BoundingBox));
            index = _softMaskCount - 1;
        }

        (_lastMask, _lastMaskMatrix, _lastMaskShape, _lastMaskIndex) = (mask, matrix, shape, index);
        return index;
    }

    /// <summary>Returns the built group of a mask, from the page's lists or built now; null when it cannot be built.</summary>
    /// <remarks>
    /// ISO 32000-2 §11.6.5.1: the group runs in the coordinate system of <paramref name="matrix"/> (the CTM at <c>gs</c>) concatenated
    /// with its own Matrix, wrapped in a group with its attributes (I, K and, for an isolated group, CS: §11.6.6). A luminosity mask's
    /// group must name its colour space (Table 145); without one the backdrop's components decide it.
    /// </remarks>
    private MaskGroup? GetMaskGroup(PdfSoftMask mask, in Matrix matrix, ContentContext context)
    {
        if (mask.Group is not { } group)
        {
            return null;
        }

        _masks ??= _ownMasks ??= new SoftMaskLists();
        CosDictionary? inherited = group.Resources is null ? context.Resources : null;
        if (_masks.TryFind(mask.Dictionary, inherited, out MaskGroup? found))
        {
            // A mask still being built is used inside its own group: the interpreter refuses to run it there.
            return found;
        }

        int entry = _masks.Add(mask.Dictionary, inherited);
        PdfDocument document = context.Document;
        CosDictionary? attributes = group.Group;
        bool isolated = Flag(attributes, IsolatedName, document);
        bool knockout = Flag(attributes, KnockoutName, document);
        PdfColorSpace? space = attributes is not null && attributes.TryGetValue(GroupColorSpaceName, out CosObject? cs)
            ? BlendingSpace(document.ColorSpaces.Get(cs, group.Reference), document, group.Reference)
            : null;
        PdfSoftMaskType subtype = mask.Subtype;
        float[] backdrop = [];
        if (subtype == PdfSoftMaskType.Luminosity)
        {
            IReadOnlyList<double>? bc = mask.Backdrop;
            if (space is null)
            {
                document.DiagnosticSink.ReportOnce(
                    DiagnosticCodes.SoftMaskColorSpaceMissing,
                    DiagnosticSeverity.Warning,
                    "A luminosity soft mask's group has no usable colour space (CS, Table 145); the backdrop's number of components decides it.",
                    offset: null,
                    group.Reference);
                space = bc?.Count switch
                {
                    1 => PdfDeviceGrayColorSpace.Instance,
                    4 => PdfDeviceCmykColorSpace.Instance,
                    _ => PdfDeviceRgbColorSpace.Instance,
                };
            }

            backdrop = bc is not null && bc.Count == space.ComponentCount
                ? [.. bc.Select(value => (float)value)]
                : space.GetInitialColor().Components.ToArray();
        }

        CosDictionary? resources = group.Resources ?? context.Resources;
        PdfDefaultColorSpaces defaults = document.ColorSpaces.GetDefaults(resources);
        var conversion = ColorConversion.FromState(GraphicsState.CreateInitial(), DeviceColorModel.Rgb);
        Matrix groupMatrix = group.Matrix;
        PdfRectangle? box = group.BoundingBox;
        DisplayListBuilder child = _maskBuilder ??= new DisplayListBuilder();
        child._cells = _cells ??= _ownCells ??= new TilingCells();
        child._masks = _masks;
        try
        {
            if (matrix.TryInvert(out Matrix inverse) && IsFinite(inverse))
            {
                child._inCell = true;
                child._cellSpace = matrix;
                child._toCell = inverse;
                int index = child.OpenGroup(new DisplayGroupEntry(
                    DisplayGroupKind.Transparency,
                    isolated,
                    knockout,
                    isolated ? space : null,
                    defaults,
                    conversion,
                    Opacity: 1,
                    BlendMode.Normal,
                    box is not null,
                    box ?? default,
                    groupMatrix,
                    0,
                    0));
                context.RunSoftMaskGroup(child);
                child.CloseGroup(index);
            }

            // A mask set under a singular CTM maps its group to nothing: it is its backdrop's value everywhere.
            _masks.Set(entry, new MaskGroup(
                child.ToDisplayList(document),
                subtype,
                subtype == PdfSoftMaskType.Luminosity ? space : null,
                backdrop,
                defaults,
                conversion,
                mask.TransferFunction,
                box is not null,
                box is { } b ? Bounds(b, groupMatrix) : default));
        }
        finally
        {
            child.Reset();
        }

        return _masks.Get(entry);
    }

    /// <summary>Returns a mask entry for a sub-list replay under <paramref name="delta"/>: mapped unless it is the mask the glyph inherited.</summary>
    /// <remarks>
    /// A mask the description inherited is part of the sub-list's key (<see cref="SameInheritedState"/>) and so the same at every show;
    /// one set by a <c>gs</c> inside the description captured a CTM in the description's space and moves with the glyph.
    /// </remarks>
    private int MoveSoftMask(int index, in Matrix delta, in GraphicsState inherited)
    {
        if (index < 0)
        {
            return index;
        }

        DisplaySoftMaskEntry entry = _softMasks[index];
        if (inherited.SoftMask is not null && entry.Matrix == Relative(inherited.SoftMaskMatrix))
        {
            return index;
        }

        Append(ref _softMasks, ref _softMaskCount, entry with { Matrix = entry.Matrix * delta });
        return _softMaskCount - 1;
    }

    private void SaveSoftMask() => Append(ref _savedSoftMasks, ref _savedSoftMaskCount, _softMaskHeld);

    private void RestoreSoftMask() => _softMaskHeld = _savedSoftMaskCount > 0 ? _savedSoftMasks[--_savedSoftMaskCount] : -1;

    private void ResetSoftMaskTracking()
    {
        _softMaskHeld = -1;
        _savedSoftMaskCount = 0;
    }

    /// <summary>Forgets the soft masks and, for the root builder, the page's mask groups.</summary>
    private void ResetSoftMasks()
    {
        Array.Clear(_softMasks, 0, _softMaskCount);
        _softMaskCount = 0;
        ResetSoftMaskTracking();
        (_lastMask, _lastMaskMatrix, _lastMaskShape, _lastMaskIndex) = (null, default, false, -1);
        if (ReferenceEquals(_masks, _ownMasks))
        {
            _ownMasks?.Clear();
        }

        _masks = null;
    }

    private static bool Flag(CosDictionary? dictionary, CosName key, PdfDocument document) =>
        dictionary is not null && document.Resolve(dictionary.TryGetValue(key, out CosObject? value) ? value : null) is CosBoolean { Value: true };

    /// <summary>The bounds of <paramref name="box"/> mapped by <paramref name="matrix"/>.</summary>
    private static PdfRectangle Bounds(PdfRectangle box, in Matrix matrix)
    {
        PathPoint a = matrix.Transform(box.Left, box.Bottom);
        PathPoint b = matrix.Transform(box.Right, box.Bottom);
        PathPoint c = matrix.Transform(box.Right, box.Top);
        PathPoint d = matrix.Transform(box.Left, box.Top);
        return new PdfRectangle(
            Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)),
            Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)),
            Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)),
            Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
    }

    /// <summary>A mask's group built for one page: its list in mask space and what the entries made from it share.</summary>
    private sealed record MaskGroup(
        DisplayList List,
        PdfSoftMaskType Subtype,
        PdfColorSpace? ColorSpace,
        float[] Backdrop,
        PdfDefaultColorSpaces DefaultColorSpaces,
        ColorConversion Conversion,
        PdfFunction? TransferFunction,
        bool HasBoundingBox,
        PdfRectangle BoundingBox);

    /// <summary>The mask groups built for one page: one per soft-mask dictionary (and inherited resources, for a group without its own).</summary>
    private sealed class SoftMaskLists
    {
        private Entry[] _entries = new Entry[4];
        private int _count;

        public bool TryFind(CosDictionary mask, CosDictionary? resources, out MaskGroup? group)
        {
            for (int index = 0; index < _count; index++)
            {
                ref readonly Entry entry = ref _entries[index];
                if (ReferenceEquals(entry.Mask, mask) && ReferenceEquals(entry.Resources, resources))
                {
                    group = entry.Group;
                    return true;
                }
            }

            group = null;
            return false;
        }

        public int Add(CosDictionary mask, CosDictionary? resources)
        {
            Append(ref _entries, ref _count, new Entry(mask, resources, null));
            return _count - 1;
        }

        public MaskGroup? Get(int index) => _entries[index].Group;

        public void Set(int index, MaskGroup group) => _entries[index] = _entries[index] with { Group = group };

        public void Clear()
        {
            Array.Clear(_entries, 0, _count);
            _count = 0;
        }

        private readonly record struct Entry(CosDictionary Mask, CosDictionary? Resources, MaskGroup? Group);
    }
}
