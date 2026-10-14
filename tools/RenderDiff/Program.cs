namespace Broadside.Tools.RenderDiff;

/// <summary>Entry point: <c>dotnet run --project tools/RenderDiff -c Release -- [options] &lt;pdf-or-directory&gt;...</c>. See README.md.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return await RenderDiffCommand.RunAsync(args, Console.Out, Console.Error, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Cancelled.").ConfigureAwait(false);
            return 130;
        }
    }
}
