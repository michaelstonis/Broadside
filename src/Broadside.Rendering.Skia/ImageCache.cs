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
/// Keys are weak (<see cref="ConditionalWeakTable{TKey, TValue}"/>), so a backend kept for a viewer's lifetime does not keep evicted
/// display lists alive; the converted pixels themselves are released by the budget, not by the collector. Finding a kept image
/// allocates nothing. Not thread-safe: a backend renders one list at a time.
/// </remarks>
internal sealed class ImageCache : IDisposable
{
    private readonly ConditionalWeakTable<object, Entry> _entries = [];
    private readonly LinkedList<Entry> _recent = new();

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

        if (entry.Image is not null)
        {
            _recent.Remove(entry.Node);
            _recent.AddFirst(entry.Node);
        }

        return entry;
    }

    /// <summary>Keeps <paramref name="image"/> in <paramref name="entry"/> when it fits the budget, evicting the least recently used.</summary>
    /// <returns>Whether the image is kept; when not, the caller disposes it after drawing.</returns>
    public bool Keep(Entry entry, SKImage image, long bytes, ColorConversion conversion, PdfDefaultColorSpaces? defaults)
    {
        Release(entry);
        if (bytes > Budget)
        {
            return false;
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
            _recent.Remove(entry.Node);
            Bytes -= entry.Bytes;
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

        /// <summary>Returns the slot of <paramref name="transform"/>, taking a free one for a new transform; false when all are taken.</summary>
        public bool Admit(in Matrix transform, out int slot)
        {
            for (slot = 0; slot < _reductions.Length; slot++)
            {
                ref Reduction reduction = ref _reductions[slot];
                if (!reduction.Used)
                {
                    reduction = new Reduction(true, transform, null, 0);
                    return true;
                }

                if (reduction.Transform == transform)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Returns the kept resampling of a slot, or null.</summary>
        public SKImage? ReductionAt(int slot) => _reductions[slot].Image;

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

        private readonly record struct Reduction(bool Used, Matrix Transform, SKImage? Image, long Bytes);
    }
}
