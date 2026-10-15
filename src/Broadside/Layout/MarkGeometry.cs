using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Text;

namespace Broadside.Layout;

/// <summary>
/// Collects, beside the text extractor in the same pass, the page-space bounds of the paths and images each content item paints,
/// so a node whose content is graphics (a figure) has a place on the page.
/// </summary>
/// <remarks>
/// ISO 32000-2 §14.7.5 (content items: a marked-content sequence with an MCID in the page's or a form's content, §14.7.5.2, or a form
/// XObject with <c>StructParent</c>, §14.7.5.3), §8.5.3 (paths, in user space through the CTM) and §8.9.4 (an image fills the unit
/// square through the CTM). Content items are recognised exactly as <see cref="TextExtractor"/> does; marks inside an
/// <c>Artifact</c> sequence and hidden marks (unless asked for) are not collected. Bounds are not clipped.
/// </remarks>
internal sealed class MarkGeometry(bool includeHidden) : ContentProcessor
{
    private readonly Dictionary<ContentItemKey, PdfRectangle> _bounds = [];
    private readonly List<(ContentItemKey Key, int Depth)> _open = [];
    private int _markedDepth;
    private int _formDepth;
    private int _artifactDepth;

    /// <inheritdoc/>
    public override ContentEvents Events =>
        ContentEvents.Paths | ContentEvents.Images | ContentEvents.Forms | ContentEvents.MarkedContent | (includeHidden ? ContentEvents.HiddenContent : ContentEvents.None);

    /// <summary>Gets the bounds painted in each content item of the last run.</summary>
    public IReadOnlyDictionary<ContentItemKey, PdfRectangle> Bounds => _bounds;

    /// <inheritdoc/>
    public override void BeginRun(ContentContext context)
    {
        _bounds.Clear();
        _open.Clear();
        _markedDepth = 0;
        _formDepth = 0;
        _artifactDepth = 0;
    }

    /// <inheritdoc/>
    public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
    {
        _formDepth++;
        if (form.StructParent is not null && form.Stream is { } stream)
        {
            _open.Add((new ContentItemKey(stream, -1), -_formDepth));
        }

        return ContentVisit.Enter;
    }

    /// <inheritdoc/>
    public override void EndForm(in FormEvent form, ContentContext context)
    {
        if (_open.Count > 0 && _open[^1].Depth == -_formDepth)
        {
            _open.RemoveAt(_open.Count - 1);
        }

        _formDepth--;
    }

    /// <inheritdoc/>
    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _markedDepth++;
        if (mc.Mcid is { } mcid && ((CosObject?)mc.ContentStream ?? context.Page?.Dictionary) is { } streamKey)
        {
            _open.Add((new ContentItemKey(streamKey, mcid), _markedDepth));
        }

        if (_artifactDepth == 0 && mc.Tag.SequenceEqual("Artifact"u8))
        {
            _artifactDepth = _markedDepth;
        }
    }

    /// <inheritdoc/>
    public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context)
    {
        if (_markedDepth == 0)
        {
            return;
        }

        if (_markedDepth == _artifactDepth)
        {
            _artifactDepth = 0;
        }

        if (_open.Count > 0 && _open[^1].Depth == _markedDepth)
        {
            _open.RemoveAt(_open.Count - 1);
        }

        _markedDepth--;
    }

    /// <inheritdoc/>
    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (path.Paint == PathPaint.None || path.Path.IsEmpty || !Collecting(path.IsHidden))
        {
            return;
        }

        PdfRectangle box = path.Path.Bounds;
        Add(Quad.FromRectangle(context.State.Ctm, box.Left, box.Bottom, box.Right, box.Top).Bounds);
    }

    /// <inheritdoc/>
    public override void PaintImage(in ImageEvent image, ContentContext context)
    {
        if (Collecting(image.IsHidden))
        {
            Add(Quad.FromRectangle(image.Ctm, 0, 0, 1, 1).Bounds);
        }
    }

    private bool Collecting(bool hidden) => _open.Count > 0 && _artifactDepth == 0 && (includeHidden || !hidden);

    private void Add(PdfRectangle bounds)
    {
        if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Bottom) || !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Top))
        {
            return;
        }

        ContentItemKey key = _open[^1].Key;
        _bounds[key] = _bounds.TryGetValue(key, out PdfRectangle known) ? Union(known, bounds) : bounds;
    }

    /// <summary>The smallest rectangle holding both.</summary>
    internal static PdfRectangle Union(PdfRectangle first, PdfRectangle second) =>
        new(Math.Min(first.Left, second.Left), Math.Min(first.Bottom, second.Bottom), Math.Max(first.Right, second.Right), Math.Max(first.Top, second.Top));
}
