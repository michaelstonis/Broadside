using System.Buffers.Binary;
using System.Runtime.Versioning;
using Broadside.Diagnostics;
using Broadside.Graphics;

namespace Broadside.Rendering.WebGL;

/// <summary>
/// A WebGL render in progress: the display list is recorded in time slices, then drawn by the GPU in the slice that finishes it. When
/// the recording meets something the backend does not draw, the rest of the render is the software rasterizer's, in slices too.
/// </summary>
/// <remarks>ISO 32000-2 §10.7.1. Spec #33 viewer capability 2 (time-sliced replay).</remarks>
[SupportedOSPlatform("browser")]
public sealed class WebGLRender : IDisposable
{
    private readonly WebGLRenderer _owner;
    private readonly DisplayList _list;
    private readonly Bitmap _target;
    private readonly Matrix _device;
    private readonly CancellationToken _token;
    private readonly bool _readBack;
    private readonly CancellationTokenSource? _stop;
    private readonly DisplayListReplay? _replay;
    private ProgressiveRender? _software;

    internal WebGLRender(WebGLRenderer owner, DisplayList list, Bitmap target, in Matrix device, bool readBack, CancellationToken token)
    {
        _owner = owner;
        _list = list;
        _target = target;
        _device = device;
        _token = token;
        _readBack = readBack;
        if (!WebGLRenderer.IsAvailable)
        {
            owner.DiagnosticList.Add(WebGLRenderer.UnavailableDiagnostic);
            _software = owner.Software.BeginRender(list, target, device, token, null);
            return;
        }

        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        TextureCache.BeginRender();
        owner.Recorder.Reset(list.Document, device, target.Width, target.Height, _stop);
        _replay = list.BeginReplay(owner.Recorder, Region(device, target.Width, target.Height), _stop.Token, null);
    }

    /// <summary>Gets a value indicating whether the render is complete.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>Gets a value indicating whether the software rasterizer drew this render (WebGL unavailable, or a fallback).</summary>
    public bool UsedSoftwareRasterizer => _software is not null;

    /// <summary>
    /// Gets a value indicating whether the completed render's pixels are in <see cref="WebGLRenderer.Canvas"/> rather than the target
    /// (a render started without read-back that the GPU drew).
    /// </summary>
    public bool IsInCanvas => IsCompleted && !_readBack && _software is null;

    /// <summary>Gets the time the last slice spent waiting for the GPU (submitting the commands and, with read-back, reading the pixels).</summary>
    public TimeSpan GpuTime { get; private set; }

    /// <summary>Records (or rasterizes) for about <paramref name="budget"/>; the slice that completes the recording draws on the GPU.</summary>
    /// <param name="budget">The slice; <see cref="Timeout.InfiniteTimeSpan"/> renders to the end.</param>
    /// <returns><see langword="true"/> when the render is complete.</returns>
    public bool Continue(TimeSpan budget)
    {
        if (IsCompleted)
        {
            return true;
        }

        if (_software is not null)
        {
            IsCompleted = _software.Continue(budget);
            return IsCompleted;
        }

        bool done;
        try
        {
            done = _replay!.Continue(budget);
        }
        catch (OperationCanceledException) when (_owner.Recorder.Fallback is { } reason && !_token.IsCancellationRequested)
        {
            Fallback(reason);
            return false;
        }

        if (_owner.Recorder.Fallback is { } late)
        {
            Fallback(late);
            return false;
        }

        if (!done)
        {
            return false;
        }

        Submit();
        IsCompleted = true;
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _software?.Dispose();
        _stop?.Dispose();
        ReleaseTemporary();
    }

    private void Fallback(Diagnostic reason)
    {
        ReleaseTemporary();
        _owner.DiagnosticList.Add(reason);
        _software = _owner.Software.BeginRender(_list, _target, _device, _token, null);
    }

    private void Submit()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        int width = _target.Width;
        int height = _target.Height;
        uint background = BinaryPrimitives.ReadUInt32LittleEndian(_target.Pixels);
        double blue = (background & 0xFF) / 255.0;
        double green = ((background >> 8) & 0xFF) / 255.0;
        double red = ((background >> 16) & 0xFF) / 255.0;
        double alpha = (background >> 24) / 255.0;
        CommandList commands = _owner.Commands;
        Span<byte> pixels = _readBack ? _owner.ReadBackBuffer(width * height * 4) : default;
        WebGLInterop.Render(width, height, red, green, blue, alpha, commands.VertexBytes, commands.Commands, pixels);
        if (_readBack)
        {
            WebGLRenderer.CopyFlipped(pixels, _target);
        }

        ReleaseTemporary();
        TextureCache.Trim();
        GpuTime = System.Diagnostics.Stopwatch.GetElapsedTime(start);
    }

    private void ReleaseTemporary()
    {
        IReadOnlyList<int> temporary = _owner.Recorder.TemporaryTextures;
        if (temporary.Count == 0)
        {
            return;
        }

        foreach (int id in temporary)
        {
            TextureCache.Release(id);
        }

        ((List<int>)temporary).Clear();
    }

    private static PdfRectangle Region(in Matrix device, int width, int height)
    {
        if (!device.TryInvert(out Matrix inverse))
        {
            return new PdfRectangle(0, 0, 0, 0);
        }

        PathPoint a = inverse.Transform(0, 0);
        PathPoint b = inverse.Transform(width, 0);
        PathPoint c = inverse.Transform(width, height);
        PathPoint d = inverse.Transform(0, height);
        double left = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double right = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double bottom = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double top = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        return new PdfRectangle(left, bottom, right, top);
    }
}
