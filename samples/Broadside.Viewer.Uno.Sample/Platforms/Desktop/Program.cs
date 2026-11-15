using Uno.UI.Hosting;

namespace Broadside.Viewer.Uno.Sample;

/// <summary>The desktop head: Uno's Skia renderer in a native window (macOS, Windows, Linux X11 or framebuffer).</summary>
internal static class Program
{
    [STAThread]
    public static void Main()
    {
        UnoPlatformHost host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();
        host.Run();
    }
}

/// <summary>The desktop script (BROADSIDE_UNO_SCRIPT=1): runs every step, prints the result and exits.</summary>
internal static partial class Automation
{
    static partial void StartPlatform(MainPage page)
    {
        if (Environment.GetEnvironmentVariable("BROADSIDE_UNO_SCRIPT") != "1")
        {
            return;
        }

        _ = RunAndExitAsync(page);
    }

    private static async Task RunAndExitAsync(MainPage page)
    {
        bool ok;
        try
        {
            ok = await RunScriptAsync(page, Console.Out);
        }
        catch (Exception exception)
        {
            await Console.Out.WriteLineAsync("BROADSIDE_SCRIPT_FAILED " + exception);
            ok = false;
        }

        await Console.Out.FlushAsync();
        Environment.Exit(ok ? 0 : 1);
    }
}
