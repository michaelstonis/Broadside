using System.Runtime.CompilerServices;
using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

/// <summary>
/// The Skia images a backend made from display-list images and masks, one per interned instance, with their resamplings to the
/// device (<see cref="ImageReducer"/>), kept least recently used within a byte budget: the document's image cache budget (spec D10,
/// <see cref="PdfOptions.ImageCacheBudget"/>), so a backend never holds more converted pixels than the document may hold decoded
/// ones. A budget of zero keeps nothing: every paint converts its image and releases it after drawing, with the same pixels.
/// </summary>
/// <remarks>
/// <para>
/// Keys are weak (<see cref="ConditionalWeakTable{TKey, TValue}"/>), so a backend kept for a viewer's lifetime does not keep evicted
/// display lists alive; the converted pixels themselves are released by the budget, not by the collector. Finding a kept image
/// allocates nothing. Not thread-safe: a backend renders one list at a time.
/// </para>
/// <para>
/// The two images larger than the whole budget used last are still kept, outside it (with their resamplings; two, so an image and
/// its own large soft mask do not evict each other), until the next such image: the tiles
/// of one display list (spec #33 story 14) all draw it, and converting a 20-megapixel photograph once per tile made each frame of a
/// viewer take seconds. The software rasterizer keeps its last converted image the same way (<c>ImageSource</c>). A budget of zero
/// still keeps nothing.
/// </para>
/// </remarks>
internal sealed class ImageCache : IDisposable
{
    private readonly ConditionalWeakTable<object, Entry> _entries = [];
    private readonly LinkedList<Entry> _recent = new();

    // The last two images larger than the budget, most recent first, kept outside it so the tiles of a list convert them once.
    private Entry? _oversize;
    private Entry? _oversizeOlder;

    /// <summary>Gets or sets the most bytes of pixels kept.</summary>
    public long Budget { get; set; } = PdfOptions.DefaultImageCacheBudget;

    /// <summary>Gets the bytes of pixels kept.</summary>
    public long Bytes { get; private set; }

    /// <summary>Gets or sets how many Skia images were made from display-list images and masks.</summary>
    public int Created { get; set; }

    /// <summary>Returns the entry of <paramref name="key"/>, whose image is <see langword="null"/> when none is kept.</summary>
    public Entry Find(object key)
    {
        if (!_entries.TryGetValue(key, out Entry? entry))
        {
            entry = new Entry();
            _entries.Add(key, entry);
        }

        if (entry.Image is not null && !entry.Oversize)
        {
            _recent.Remove(entry.Node);
            _recent.AddFirst(entry.Node);
        }
        else if (entry.Oversize && entry == _oversizeOlder)
        {
            (_oversize, _oversizeOlder) = (entry, _oversize);
        }

        return entry;
    }

    /// <summary>
    /// Keeps <paramref name="image"/> in <paramref name="entry"/> when it fits the budget, evicting the least recently used; an image
    /// larger than a non-zero budget replaces the older of the two kept outside it.
    /// </summary>
    /// <returns>Whether the image is kept; when not, the caller disposes it after drawing.</returns>
    public bool Keep(Entry entry, SKImage image, long bytes, ColorConversion conversion, PdfDefaultColorSpaces? defaults)
    {
        Release(entry);
        if (bytes > Budget)
        {
            if (Budget <= 0)
            {
                return false;
            }

            if (_oversizeOlder is { } oldest)
            {
                Release(oldest);
            }

            entry.Image = image;
            entry.Bytes = bytes;
            entry.Conversion = conversion;
            entry.Defaults = defaults;
            entry.Oversize = true;
            (_oversize, _oversizeOlder) = (entry, _oversize);
            return true;
        }

        Evict(bytes, except: null);
        entry.Image = image;
        entry.Bytes = bytes;
        entry.Conversion = conversion;
        entry.Defaults = defaults;
        _recent.AddFirst(entry.Node);
        Bytes += bytes;
        return true;
    }

    /// <summary>Keeps a resampling of the entry's kept image in one of its slots when it fits the budget beside it.</summary>
    /// <returns>Whether the resampling is kept; when not, the caller disposes it after drawing.</returns>
    public bool KeepReduction(Entry entry, int slot, SKImage reduced, long bytes)
    {
        if (entry.Oversize)
        {
            // Bounded by the resampling's own limit (ImageReducer) and the entry's slots, like the image beside it.
            entry.SetReduction(slot, reduced, bytes);
            return true;
        }

        if (entry.Image is null || bytes > Budget - entry.Bytes)
        {
            return false;
        }

        Evict(bytes, except: entry);
        if (Bytes + bytes > Budget)
        {
            return false;
        }

        entry.SetReduction(slot, reduced, bytes);
        Bytes += bytes;
        return true;
    }

    /// <summary>Releases every kept image.</summary>
    public void Dispose()
    {
        while (_recent.Last is { } last)
        {
            Release(last.Value);
        }

        while (_oversize is { } oversize)
        {
            Release(oversize);
        }
    }

    private void Evict(long bytes, Entry? except)
    {
        while (Bytes + bytes > Budget && _recent.Last is { } last && last.Value != except)
        {
            Release(last.Value);
        }
    }

    private void Release(Entry entry)
    {
        if (entry.Image is { } image)
        {
            if (entry.Oversize)
            {
                entry.Oversize = false;
                if (entry == _oversize)
                {
                    (_oversize, _oversizeOlder) = (_oversizeOlder, null);
                }
                else
                {
                    _oversizeOlder = null;
                }
            }
            else
            {
                _recent.Remove(entry.Node);
                Bytes -= entry.Bytes;
            }

            entry.Image = null;
            entry.Defaults = null;
            entry.Bytes = 0;
            entry.ReleaseReductions();
            image.Dispose();
        }
    }

    /// <summary>A kept image, the conversion it was made with, and the transforms it is resampled for.</summary>
    internal sealed class Entry
    {
        /// <summary>
        /// The most transforms an image is resampled for (an image drawn at several sizes on one page). They are the first ones it
        /// is drawn at, and stay its own whether or not the resamplings are kept, so the output never depends on the budget; further
        /// transforms are left to Skia's sampling, so that an image painted at many positions (a Type 3 glyph's mask) is not
        /// resampled on every replay.
        /// </summary>
        public const int MaxReductions = 4;

        private readonly Reduction[] _reductions = new Reduction[MaxReductions];

        public Entry() => Node = new LinkedListNode<Entry>(this);

        public LinkedListNode<Entry> Node { get; }

        public SKImage? Image { get; set; }

        public long Bytes { get; set; }

        public ColorConversion Conversion { get; set; }

        public PdfDefaultColorSpaces? Defaults { get; set; }

        /// <summary>Whether the image is the one kept outside the budget.</summary>
        public bool Oversize { get; set; }

        /// <summary>
        /// Returns the slot of <paramref name="transform"/>, taking a free one for a new transform; false when all are taken. A
        /// transform that differs from a slot's by whole device pixels only, covering <paramref name="pixels"/> shifted as much, shares
        /// its slot: the resampling is the same pixels moved, so the tiles of one list (each the page's transform plus its own whole-pixel
        /// offset) resample an image once and draw the same pixels as the whole page.
        /// </summary>
        public bool Admit(in Matrix transform, SKRectI pixels, out int slot)
        {
            for (slot = 0; slot < _reductions.Length; slot++)
            {
                ref Reduction reduction = ref _reductions[slot];
                if (!reduction.Used)
                {
                    reduction = new Reduction(true, transform, pixels, null, 0);
                    return true;
                }

                if (reduction.Transform == transform || reduction.IsShifted(transform, pixels))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Returns the kept resampling of a slot, or null.</summary>
        public SKImage? ReductionAt(int slot) => _reductions[slot].Image;

        /// <summary>Returns the transform a slot was taken for: the one its resampling is made with.</summary>
        public Matrix TransformAt(int slot) => _reductions[slot].Transform;

        /// <summary>Returns the device pixels the slot's transform covers: the size and grid of its resampling.</summary>
        public SKRectI PixelsAt(int slot) => _reductions[slot].Pixels;

        /// <summary>Stores a resampling in its slot.</summary>
        public void SetReduction(int slot, SKImage image, long bytes)
        {
            ref Reduction reduction = ref _reductions[slot];
            reduction = reduction with { Image = image, Bytes = bytes };
            Bytes += bytes;
        }

        /// <summary>Releases the kept resamplings; the slots keep their transforms.</summary>
        public void ReleaseReductions()
        {
            for (int i = 0; i < _reductions.Length; i++)
            {
                _reductions[i].Image?.Dispose();
                _reductions[i] = _reductions[i] with { Image = null, Bytes = 0 };
            }
        }

        /// <summary>Whether the kept image was converted with this conversion and these default colour spaces.</summary>
        public bool Matches(ColorConversion conversion, PdfDefaultColorSpaces? defaults) =>
            Image is not null && ReferenceEquals(Defaults, defaults) && Conversion == conversion;

        private readonly record struct Reduction(bool Used, Matrix Transform, SKRectI Pixels, SKImage? Image, long Bytes)
        {
            /// <summary>Whether <paramref name="transform"/> is this one moved by whole pixels, and <paramref name="pixels"/> these moved as much.</summary>
            public bool IsShifted(in Matrix transform, SKRectI pixels)
            {
                if (transform.A != Transform.A || transform.B != Transform.B || transform.C != Transform.C || transform.D != Transform.D)
                {
                    return false;
                }

                double dx = transform.E - Transform.E;
                double dy = transform.F - Transform.F;
                double x = Math.Round(dx);
                double y = Math.Round(dy);
                if (Math.Abs(dx - x) > 1e-9 || Math.Abs(dy - y) > 1e-9 || Math.Abs(x) > 1 << 24 || Math.Abs(y) > 1 << 24)
                {
                    return false;
                }

                int ix = (int)x;
                int iy = (int)y;
                return pixels.Left == Pixels.Left + ix && pixels.Top == Pixels.Top + iy && pixels.Right == Pixels.Right + ix && pixels.Bottom == Pixels.Bottom + iy;
            }
        }
    }
}
