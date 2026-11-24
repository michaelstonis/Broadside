using System.Globalization;

namespace Broadside.Tools.PackageCheck;

/// <summary>
/// Entry point:
/// <c>dotnet run --project tools/PackageCheck -- verify &lt;packages dir&gt; [--policy &lt;file&gt;] [--online]</c> or
/// <c>dotnet run --project tools/PackageCheck -- compare &lt;packages dir A&gt; &lt;packages dir B&gt;</c>.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage:
          PackageCheck verify <packages dir> [--policy <package-policy.json>] [--online]
          PackageCheck compare <packages dir A> <packages dir B>

        verify   checks every .nupkg (and its .snupkg) in the folder against the package policy (default: the
                 package-policy.json next to the tool). --online also fetches every SourceLink URL.
        compare  fails unless both folders hold the same packages with byte-identical contents.
        Exit code 0 when everything passes, 1 on findings, 2 on a bad command line.
        """;

    private static async Task<int> Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "verify")
        {
            string? policyPath = null;
            bool online = false;
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--online")
                {
                    online = true;
                }
                else if (args[i] == "--policy" && i + 1 < args.Length)
                {
                    policyPath = args[++i];
                }
                else
                {
                    return BadUsage();
                }
            }

            return await VerifyAsync(args[1], policyPath ?? Path.Combine(AppContext.BaseDirectory, "package-policy.json"), online).ConfigureAwait(false);
        }

        if (args.Length == 3 && args[0] == "compare")
        {
            if (!Directory.Exists(args[1]) || !Directory.Exists(args[2]))
            {
                Console.Error.WriteLine("Both package folders must exist.");
                return 2;
            }

            (IReadOnlyList<string> packages, IReadOnlyList<Finding> findings) = PackageComparer.Compare(args[1], args[2]);
            return Report(findings, string.Create(CultureInfo.InvariantCulture, $"{packages.Count} package file(s) identical in both builds."));
        }

        return BadUsage();
    }

    private static async Task<int> VerifyAsync(string directory, string policyPath, bool online)
    {
        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"No such folder: {directory}");
            return 2;
        }

        PackagePolicy policy;
        try
        {
            policy = PackagePolicy.Load(policyPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot read the package policy {policyPath}: {ex.Message}");
            return 2;
        }

        var urls = new List<string>();
        (IReadOnlyList<string> packages, IReadOnlyList<Finding> findings) = PackageVerifier.VerifyDirectory(directory, policy, urls);
        var all = findings.ToList();
        if (online && all.Count == 0)
        {
            Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Fetching {urls.Distinct(StringComparer.Ordinal).Count()} SourceLink URL(s)..."));
            foreach (string failure in await SourceLinkResolver.FindUnresolvedAsync(urls, CancellationToken.None).ConfigureAwait(false))
            {
                all.Add(new Finding("SourceLink", failure));
            }
        }

        foreach (string package in packages)
        {
            Console.Out.WriteLine($"checked {package}");
        }

        return Report(all, string.Create(CultureInfo.InvariantCulture, $"{packages.Count} package(s) pass the package policy."));
    }

    private static int Report(IReadOnlyList<Finding> findings, string success)
    {
        foreach (Finding finding in findings)
        {
            Console.Out.WriteLine($"error: {finding}");
        }

        Console.Out.WriteLine(findings.Count == 0 ? success : string.Create(CultureInfo.InvariantCulture, $"FAILED: {findings.Count} finding(s)."));
        return findings.Count == 0 ? 0 : 1;
    }

    private static int BadUsage()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}
