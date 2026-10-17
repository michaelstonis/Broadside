using Microsoft.Playwright;

namespace Broadside.Viewer.Blazor.BrowserTests;

/// <summary>Reads what the viewer exposes in the page: its <c>data-*</c> statistics and the canvas pixels.</summary>
internal static class Viewer
{
    /// <summary>Counts canvas pixels darker than mid grey (page ink; the background and paper are light).</summary>
    public const string CountDarkPixels = """
        () => {
          const c = document.querySelector('.bs-viewer canvas');
          const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
          let n = 0;
          for (let i = 0; i < d.length; i += 4) if (d[i] < 100 && d[i + 1] < 100 && d[i + 2] < 100) n++;
          return n;
        }
        """;

    /// <summary>Writes a measurement to the test output and, when BROADSIDE_BROWSER_LOG names a file, appends it there (CI prints it).</summary>
    public static void Log(string line)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(line);
        if (Environment.GetEnvironmentVariable("BROADSIDE_BROWSER_LOG") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    public static Task<string?> Data(IPage page, string name) =>
        page.EvaluateAsync<string?>($"() => document.querySelector('.bs-viewer').dataset.{name} ?? null");

    /// <summary>Writes the viewer's frame statistics (each frame: the scheduler's slice, composing and the canvas draw) to the test output.</summary>
    public static async Task LogFramesAsync(IPage page, string what)
    {
        string? frames = await Data(page, "frames");
        string? mean = await Data(page, "meanFrameMs");
        string? max = await Data(page, "maxFrameMs");
        string? longFrames = await Data(page, "longFrames");
        Log($"{what}: {frames} frames, mean {mean} ms, max {max} ms, {longFrames} over 50 ms");
    }
}
