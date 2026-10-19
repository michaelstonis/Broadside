using System.Globalization;

namespace Broadside.Tools.ApkAotCheck;

/// <summary>Entry point: <c>dotnet run --project tools/ApkAotCheck -- &lt;path to .apk&gt;</c>.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] is "-h" or "--help")
        {
            Console.Error.WriteLine("Usage: ApkAotCheck <path to .apk>");
            return 2;
        }

        if (!File.Exists(args[0]))
        {
            Console.Error.WriteLine($"No such APK: {args[0]}");
            return 2;
        }

        IReadOnlyList<AbiReport> reports;
        try
        {
            reports = ApkAotChecker.Check(args[0]);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Could not read {args[0]}: {ex.Message}");
            return 2;
        }

        int images = 0;
        int problems = 0;
        foreach (AbiReport report in reports)
        {
            images += report.AotImageCount;
            problems += report.Problems.Count;
            foreach (string problem in report.Problems)
            {
                Console.Out.WriteLine(problem);
            }

            Console.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{report.Abi}: {report.AssemblyCount} assemblies, {report.AotImageCount} AOT images, {(report.Problems.Count == 0 ? "OK" : $"{report.Problems.Count} problem(s)")}"));
        }

        if (images == 0)
        {
            // A check that sees nothing must not pass: a Debug or non-AOT APK here means the build step changed.
            Console.Out.WriteLine("FAILED: the APK has no AOT images (libaot-*.so). Is it a Release build with AOT on?");
            return 1;
        }

        Console.Out.WriteLine(problems == 0
            ? "OK: every AOT image matches the assemblies the APK ships."
            : string.Create(CultureInfo.InvariantCulture, $"FAILED: {problems} AOT image(s) do not match the assemblies the APK ships (#366)."));
        return problems == 0 ? 0 : 1;
    }
}
