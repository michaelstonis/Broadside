using System.Text;

namespace Broadside.Tools.ReleaseNotes;

/// <summary>
/// Entry point: <c>dotnet run --project tools/ReleaseNotes -- [--root &lt;dir&gt;] [--from &lt;ref&gt;] [--to &lt;ref&gt;] [--version &lt;v&gt;]
/// [--repository &lt;owner/name&gt;] [--output &lt;file&gt;] [--changelog &lt;file&gt;]</c>. Prints the notes unless <c>--output</c> is given.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage: dotnet run --project tools/ReleaseNotes -- [options]

          --root <dir>           repository root (default: the current directory)
          --from <ref>           last released ref (default: the highest v* tag below the one on --to)
          --to <ref>             ref being released (default: HEAD)
          --version <version>    version in the heading (default: the v* tag on --to, else Unreleased)
          --repository <o/name>  GitHub repository for links (default: michaelstonis/Broadside)
          --output <file>        write the notes to this file instead of standard output
          --changelog <file>     also add the notes to this changelog (replacing the same version or Unreleased)
        """;

    private static int Main(string[] args)
    {
        string root = Directory.GetCurrentDirectory();
        string? output = null;
        string? changelog = null;
        var options = new ReleaseNotesOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string? value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "-h" or "--help":
                    Console.Out.WriteLine(Usage);
                    return 0;
                case "--root" when value is not null:
                    root = Path.GetFullPath(value);
                    break;
                case "--from" when value is not null:
                    options = options with { From = value };
                    break;
                case "--to" when value is not null:
                    options = options with { To = value };
                    break;
                case "--version" when value is not null:
                    options = options with { Version = value.TrimStart('v') };
                    break;
                case "--repository" when value is not null:
                    options = options with { Repository = value };
                    break;
                case "--output" when value is not null:
                    output = value;
                    break;
                case "--changelog" when value is not null:
                    changelog = value;
                    break;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete option '{args[i]}'.");
                    Console.Error.WriteLine(Usage);
                    return 2;
            }

            i++;
        }

        ReleaseNotesResult result;
        try
        {
            result = ReleaseNotesGenerator.Generate(root, options);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (output is null)
        {
            Console.Out.Write(result.Markdown);
        }
        else
        {
            File.WriteAllText(output, result.Markdown, utf8);
        }

        if (changelog is not null)
        {
            string existing = File.Exists(changelog) ? File.ReadAllText(changelog) : string.Empty;
            File.WriteAllText(changelog, Changelog.Update(existing, result.Markdown), utf8);
        }

        Console.Error.WriteLine($"Release notes for {result.Version}: {result.Entries.Count} entries since {result.From ?? "the first commit"}, {result.ApiChanges.Count} package(s) with API changes.");
        return 0;
    }
}
