using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Uno.UI.Hosting;

namespace Broadside.Viewer.Uno.Sample;

/// <summary>
/// The WebAssembly head: Uno's Skia renderer on a canvas. No .NET WebAssembly threads: the viewer renders tiles, text and search in
/// cooperative slices on the page's thread, between frames.
/// </summary>
internal static class Program
{
    public static async Task Main()
    {
        UnoPlatformHost host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseWebAssembly()
            .Build();
        await host.RunAsync();
    }
}

/// <summary>The browser tests' channel (<c>?automation=1</c>): state out, commands in, through two globals.</summary>
internal static partial class Automation
{
    static partial void StartPlatform(MainPage page)
    {
        if (!Browser.Eval("location.search").Contains("automation=1", StringComparison.Ordinal))
        {
            return;
        }

        Browser.Eval("globalThis.broadsideCommands = globalThis.broadsideCommands || []; 'ok'");
        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = page.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(100);
        timer.Tick += (_, _) =>
        {
            string commands = Browser.Eval("JSON.stringify(globalThis.broadsideCommands.splice(0))");
            using (JsonDocument document = JsonDocument.Parse(commands))
            {
                foreach (JsonElement command in document.RootElement.EnumerateArray())
                {
                    Apply(page, command);
                }
            }

            Browser.Eval("globalThis.broadsideState = " + State(page) + "; 'ok'");
        };
        timer.Start();
    }
}

internal static partial class Browser
{
    /// <summary>Evaluates a script in the page's global scope and returns its value as a string.</summary>
    [JSImport("globalThis.eval")]
    public static partial string Eval(string code);
}
