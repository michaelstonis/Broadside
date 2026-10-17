using SkiaSharp;

namespace Broadside.Viewer.Maui;

/// <summary>
/// Hands composed frames from the UI thread to the canvas's paint, which may run on another thread (Android's GL thread), and
/// releases tile images only once no frame the paint can still draw refers to them.
/// </summary>
/// <remarks>
/// An image retired while frame <c>g</c> is the newest is tagged <c>g + 1</c>: no frame from <c>g + 1</c> on holds it. The paint only
/// ever draws the newest frame, so the image is released once no paint is drawing a frame older than its tag.
/// </remarks>
internal sealed class FramePresenter : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Stack<ViewerFrame> _free = new();
    private readonly List<(SKImage Image, long Tag)> _retired = [];
    private readonly SKPaint _paint = new();
    private readonly SKPathBuilder _path = new();
    private ViewerFrame? _latest;
    private ViewerFrame? _drawing;
    private long _generation;
    private bool _disposed;

    /// <summary>The newest published frame (tests and diagnostics; read on the UI thread).</summary>
    internal ViewerFrame? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public ViewerFrame Rent()
    {
        lock (_gate)
        {
            return _free.Count > 0 ? _free.Pop() : new ViewerFrame();
        }
    }

    /// <summary>Makes <paramref name="frame"/> the newest frame and takes ownership of the <paramref name="retired"/> images (the list is emptied).</summary>
    public void Publish(ViewerFrame frame, List<SKImage> retired)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                foreach (SKImage image in retired)
                {
                    image.Dispose();
                }

                retired.Clear();
                return;
            }

            frame.Generation = ++_generation;
            ViewerFrame? old = _latest;
            _latest = frame;
            if (old is not null && !ReferenceEquals(old, _drawing))
            {
                _free.Push(old.Reset());
            }

            foreach (SKImage image in retired)
            {
                _retired.Add((image, frame.Generation));
            }

            retired.Clear();
            ReleaseRetired();
        }
    }

    /// <summary>Draws the newest frame (or the bare background before the first one).</summary>
    public void Draw(SKCanvas canvas)
    {
        ViewerFrame? frame;
        lock (_gate)
        {
            frame = _disposed ? null : _latest;
            _drawing = frame;
        }

        try
        {
            if (frame is null)
            {
                canvas.Clear(ViewerFrame.Background);
            }
            else
            {
                frame.Draw(canvas, _paint, _path);
            }
        }
        finally
        {
            lock (_gate)
            {
                _drawing = null;
                if (frame is not null && !ReferenceEquals(frame, _latest) && !_disposed)
                {
                    _free.Push(frame.Reset());
                }

                ReleaseRetired();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _latest = null;
            _free.Clear();
            if (_drawing is null)
            {
                ReleaseRetired();
                _paint.Dispose();
                _path.Dispose();
            }
        }
    }

    private void ReleaseRetired()
    {
        long limit = _drawing?.Generation ?? long.MaxValue;
        int kept = 0;
        for (int i = 0; i < _retired.Count; i++)
        {
            (SKImage image, long tag) = _retired[i];
            if (tag <= limit)
            {
                image.Dispose();
            }
            else
            {
                _retired[kept++] = _retired[i];
            }
        }

        _retired.RemoveRange(kept, _retired.Count - kept);
    }
}
