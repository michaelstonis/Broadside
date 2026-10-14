using Foundation;
using Xunit.MicrosoftTestingPlatform;

namespace Broadside.Rendering.CoreGraphics.Tests;

/// <summary>
/// The test application's entry point, in place of the one xUnit generates. This project is a macOS app, whose framework delivers
/// some completions on the main run loop; xUnit's entry point blocks the main thread until the test platform finishes, so the run
/// would never end. This one keeps the main run loop turning until it does.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        Task<int> run = TestPlatformTestFramework.RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions);
        while (!run.IsCompleted)
        {
            NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(0.05));
        }

        return run.GetAwaiter().GetResult();
    }
}
