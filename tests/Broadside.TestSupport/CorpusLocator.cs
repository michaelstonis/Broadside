namespace Broadside.TestSupport;

/// <summary>
/// Finds the repository checkout that the running assembly was built from, and the hand-written corpus inside it.
/// Walks up from <see cref="AppContext.BaseDirectory"/> to the directory that contains <c>Broadside.slnx</c>.
/// This file is also linked as source into <c>bench/Broadside.Benchmarks</c> and <c>tests/Broadside.Fuzz</c>, which
/// must not reference the test support library (it carries xunit and Verify with it).
/// </summary>
internal static class CorpusLocator
{
    /// <summary>The directory that contains <c>Broadside.slnx</c>.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The absolute path of <c>tests/Corpus/</c>.</summary>
    public static string CorpusDirectory { get; } = FindCorpusDirectory();

    /// <summary>Every <c>.pdf</c> in the corpus directory, sorted by ordinal file name.</summary>
    public static string[] CorpusFiles()
    {
        string[] files = Directory.GetFiles(CorpusDirectory, "*.pdf");
        Array.Sort(files, StringComparer.Ordinal);
        return files;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find Broadside.slnx in any parent of '{AppContext.BaseDirectory}'. Corpus-driven code must run from a checkout of the repository.");
    }

    private static string FindCorpusDirectory()
    {
        string corpus = Path.Combine(RepositoryRoot, "tests", "Corpus");
        return Directory.Exists(corpus)
            ? corpus
            : throw new DirectoryNotFoundException($"Found the repository root at '{RepositoryRoot}' but it has no tests/Corpus directory.");
    }
}
