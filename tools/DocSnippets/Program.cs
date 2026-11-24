using Broadside.Tools.DocSnippets;

// dotnet run --project tools/DocSnippets -- [--root <dir>] [--write]
// Checks that the sample code in the site's pages equals the regions of samples/guides/ it names (exit 1 when not), or with
// --write regenerates it. ./check-docs.sh runs the check.
string? root = null;
bool write = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--root" when i + 1 < args.Length:
            root = Path.GetFullPath(args[++i]);
            break;
        case "--write":
            write = true;
            break;
        default:
            Console.Error.WriteLine("usage: DocSnippets [--root <repository root>] [--write]");
            return 2;
    }
}

root ??= FindRoot(Directory.GetCurrentDirectory());
if (root is null)
{
    Console.Error.WriteLine("Could not find Broadside.slnx above the current directory; pass --root <repository root>.");
    return 2;
}

IReadOnlyList<SnippetProblem> problems = SnippetSync.CheckSite(root, write, out int snippets);
foreach (SnippetProblem problem in problems)
{
    Console.Error.WriteLine(problem);
}

Console.WriteLine(problems.Count == 0
    ? $"Doc snippets OK: {snippets} snippet(s){(write ? ", regenerated" : string.Empty)}."
    : $"Doc snippets: {problems.Count} problem(s) in {snippets} snippet(s).");
return problems.Count == 0 ? 0 : 1;

static string? FindRoot(string start)
{
    for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
        {
            return directory.FullName;
        }
    }

    return null;
}
