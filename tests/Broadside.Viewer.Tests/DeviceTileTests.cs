using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>
/// A backend that leaves finished tiles on the device (the WebGL backend, ADR 0012; #380): a surface that reads pixels gets them read
/// back, a surface that accepts device pixels takes them without a read-back, and no later render touches a tile's released bitmap.
/// </summary>
public sealed class DeviceTileTests
{
    private const string FileName = "paths-fill-rules.pdf";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(ViewerThreading.Cooperative)]
    [InlineData(ViewerThreading.Background)]
    public async Task A_surface_that_reads_pixels_gets_a_device_tile_read_back_and_the_next_render_succeeds(ViewerThreading threading)
    {
        var device = new Device();
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes(FileName), Options(threading, device));
        using var thumbnail = new HeadlessSurface();
        using var next = new HeadlessSurface();

        await session.RenderPageAsync(0, 0.5, thumbnail, Token);
        Assert.Null(device.Held); // released before the bitmap's lifetime ended
        await session.RenderPageAsync(0, 1, next, Token); // read a held tile back into a disposed bitmap before #380

        using Bitmap small = await RenderWithSoftwareAsync(0.5);
        using Bitmap large = await RenderWithSoftwareAsync(1);
        Assert.Equal(0, ViewerTestSupport.MaxDifference(small, Assert.Single(thumbnail.Tiles).Pixels));
        Assert.Equal(0, ViewerTestSupport.MaxDifference(large, Assert.Single(next.Tiles).Pixels));
        Assert.Equal(2, device.ReadBacks);
        Assert.Null(device.Held);
    }

    [Fact]
    public async Task A_surface_that_accepts_device_pixels_takes_the_tile_without_a_read_back()
    {
        var device = new Device();
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes(FileName), Options(ViewerThreading.Cooperative, device));
        using var surface = new ClaimingSurface(device);

        await session.RenderPageAsync(0, 0.5, surface, Token);
        await session.RenderPageAsync(0, 1, surface, Token);

        using Bitmap small = await RenderWithSoftwareAsync(0.5);
        using Bitmap large = await RenderWithSoftwareAsync(1);
        Assert.Equal(0, ViewerTestSupport.MaxDifference(small, surface.Tiles[0]));
        Assert.Equal(0, ViewerTestSupport.MaxDifference(large, surface.Tiles[1]));
        Assert.Equal((0, 2), (device.ReadBacks, surface.Claimed));
        Assert.Null(device.Held);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_tile_scheduler_presents_device_tiles_and_releases_them_before_their_bitmaps(bool accepts)
    {
        var device = new Device();
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes(FileName), Options(ViewerThreading.Cooperative, device));
        PageLayout layout = await PageLayout.CreateAsync(session, cancellationToken: Token);
        using var surface = new ClaimingSurface(device, accepts);
        using (var scheduler = new TileScheduler(session, layout, surface))
        {
            scheduler.Update(new Viewport(0, 0, 400, 400, 1));
            while (scheduler.RunFrame(Timeout.InfiniteTimeSpan))
            {
            }
        }

        Assert.NotEmpty(surface.Tiles);
        Assert.All(surface.Tiles, tile => Assert.True(HasInk(tile), "the tile has ink"));
        Assert.Equal(accepts ? 0 : surface.Tiles.Count, device.ReadBacks);
        Assert.Equal(accepts ? surface.Tiles.Count : 0, surface.Claimed);
        Assert.Null(device.Held);
    }

    private static ViewerOptions Options(ViewerThreading threading, Device device) =>
        ViewerTestSupport.OptionsFor(FileName, threading).UseBackend(() => new DeviceBackend(device));

    private static async Task<Bitmap> RenderWithSoftwareAsync(double scale)
    {
        await using DocumentSession session = DocumentSession.Open(Corpus.Bytes(FileName), ViewerTestSupport.OptionsFor(FileName, ViewerThreading.Cooperative));
        using var surface = new HeadlessSurface();
        await session.RenderPageAsync(0, scale, surface, Token);
        Bitmap pixels = Assert.Single(surface.Tiles).Pixels;
        var copy = new Bitmap(pixels.Width, pixels.Height);
        pixels.Pixels.CopyTo(copy.Pixels);
        return copy;
    }

    private static bool HasInk(Bitmap pixels)
    {
        for (int y = 0; y < pixels.Height; y++)
        {
            if (pixels.GetRow(y).ContainsAnyExcept(byte.MaxValue))
            {
                return true;
            }
        }

        return false;
    }

    private static Bitmap Copy(Bitmap source)
    {
        var copy = new Bitmap(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        {
            source.GetRow(y).CopyTo(copy.GetRow(y));
        }

        return copy;
    }

    /// <summary>The device the backend draws on, holding at most one finished tile as the WebGL canvas does.</summary>
    private sealed class Device
    {
        private Bitmap? _canvas;

        public Bitmap? Held { get; private set; }

        public int ReadBacks { get; private set; }

        public void Hold(Bitmap target, Bitmap canvas)
        {
            Flush();
            Held = target;
            _canvas = canvas;
        }

        /// <summary>Reads a held tile back into its bitmap; throws <see cref="ObjectDisposedException"/> when that bitmap was released.</summary>
        public void Flush()
        {
            if (Held is { } held)
            {
                Held = null;
                for (int y = 0; y < held.Height; y++)
                {
                    _canvas!.GetRow(y).CopyTo(held.GetRow(y));
                }

                _canvas = null;
                ReadBacks++;
            }
        }

        public void ReadBack(Bitmap target)
        {
            if (ReferenceEquals(Held, target))
            {
                Flush();
            }
        }

        public Bitmap? Claim(Bitmap pixels)
        {
            if (!ReferenceEquals(Held, pixels))
            {
                Flush();
                return null;
            }

            Held = null;
            Bitmap canvas = _canvas!;
            _canvas = null;
            return canvas;
        }

        public void Release(Bitmap target)
        {
            if (ReferenceEquals(Held, target))
            {
                Held = null;
                _canvas = null;
            }
        }
    }

    /// <summary>Draws with the software rasterizer into a canvas of its own and leaves the finished tile there, held.</summary>
    private sealed class DeviceBackend(Device device) : ITileBackend
    {
        private readonly SoftwareTileBackend _inner = new();

        public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken)
        {
            device.Flush();
            return new Render(device, _inner, list, target, deviceTransform, cancellationToken);
        }

        public void Dispose() => _inner.Dispose();

        private sealed class Render : ITileRender
        {
            private readonly Device _device;
            private readonly Bitmap _target;
            private readonly Bitmap _canvas;
            private readonly ITileRender _inner;

            public Render(Device device, SoftwareTileBackend backend, DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken)
            {
                _device = device;
                _target = target;
                _canvas = Copy(target); // the background
                _inner = backend.Begin(list, _canvas, deviceTransform, cancellationToken);
            }

            public bool RenderSlice(TimeSpan budget)
            {
                if (!_inner.RenderSlice(budget))
                {
                    return false;
                }

                _device.Hold(_target, _canvas);
                return true;
            }

            public void ReadBack() => _device.ReadBack(_target);

            public void Dispose()
            {
                _device.Release(_target);
                _inner.Dispose();
                _canvas.Dispose();
            }
        }
    }

    /// <summary>Keeps copies of its tiles; when it accepts device pixels it claims a held tile, as the Blazor canvas surface does.</summary>
    private sealed class ClaimingSurface(Device device, bool accepts = true) : IRenderSurface, IDisposable
    {
        public List<Bitmap> Tiles { get; } = [];

        public int Claimed { get; private set; }

        public bool AcceptsDevicePixels => accepts;

        public void Present(Tile tile, Bitmap pixels)
        {
            if (accepts && device.Claim(pixels) is { } canvas)
            {
                Claimed++;
                Tiles.Add(Copy(canvas));
                return;
            }

            Tiles.Add(Copy(pixels));
        }

        public void Dispose()
        {
            foreach (Bitmap tile in Tiles)
            {
                tile.Dispose();
            }
        }
    }
}
