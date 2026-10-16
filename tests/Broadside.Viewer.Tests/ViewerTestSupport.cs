using Broadside.Fonts.Cmaps;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Viewer.Tests;

/// <summary>Tests that measure allocations or memory run alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyTestCollection
{
    public const string Name = "Heavy";
}

internal static class ViewerTestSupport
{
    /// <summary>Viewer options that open corpus files as the golden images were drawn: machine-independent fonts, user password.</summary>
    public static ViewerOptions OptionsFor(string fileName, ViewerThreading threading)
    {
        ViewerOptions options = new ViewerOptions()
            .WithDocumentOptions(new PdfOptions().UseStandard14Fonts().UsePredefinedCMaps().UseSystemFontResolver(null))
            .WithThreading(threading);
        return Corpus.UserPassword(fileName) is { } password ? options.WithCredentials(new PdfPassword(password)) : options;
    }

    public static int MaxDifference(Bitmap expected, Bitmap actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        int max = 0;
        for (int y = 0; y < expected.Height; y++)
        {
            ReadOnlySpan<byte> a = expected.GetRow(y);
            ReadOnlySpan<byte> b = actual.GetRow(y);
            for (int i = 0; i < a.Length; i++)
            {
                max = Math.Max(max, Math.Abs(a[i] - b[i]));
            }
        }

        return max;
    }

    public static int MaxDifference(Bitmap whole, PresentedTile tile)
    {
        PixelRectangle region = tile.Tile.Region;
        int max = 0;
        for (int y = 0; y < region.Height; y++)
        {
            ReadOnlySpan<byte> a = whole.GetRow(region.Y + y).Slice(region.X * 4, region.Width * 4);
            ReadOnlySpan<byte> b = tile.Pixels.GetRow(y);
            for (int i = 0; i < a.Length; i++)
            {
                max = Math.Max(max, Math.Abs(a[i] - b[i]));
            }
        }

        return max;
    }
}

/// <summary>Wraps the software renderer, counting what the session does with it.</summary>
internal sealed class CountingBackend : ITileBackend
{
    private readonly SoftwareTileBackend _inner = new();

    public int Slices;

    public bool IsDisposed { get; private set; }

    /// <summary>A factory that records every renderer it creates in <paramref name="created"/>.</summary>
    public static Func<ITileBackend> Factory(List<CountingBackend> created) => () =>
    {
        var renderer = new CountingBackend();
        lock (created)
        {
            created.Add(renderer);
        }

        return renderer;
    };

    public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken) =>
        new Counting(this, _inner.Begin(list, target, deviceTransform, cancellationToken));

    public void Dispose()
    {
        IsDisposed = true;
        _inner.Dispose();
    }

    private sealed class Counting(CountingBackend owner, ITileRender inner) : ITileRender
    {
        public bool RenderSlice(TimeSpan budget)
        {
            Interlocked.Increment(ref owner.Slices);
            return inner.RenderSlice(budget);
        }

        public void Dispose() => inner.Dispose();
    }
}

/// <summary>A surface that drops what it is given, counting the tiles.</summary>
internal sealed class NullSurface : IRenderSurface
{
    public int Presented;

    public void Present(Tile tile, Bitmap pixels) => Presented++;
}
