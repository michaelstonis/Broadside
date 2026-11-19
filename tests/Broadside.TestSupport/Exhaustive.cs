namespace Broadside.TestSupport;

/// <summary>
/// Exhaustive sweeps and their samples (issue #587). A sweep that checks every case of a large domain (every corpus page as tiles,
/// twenty million colors, every line of a Unicode test file) is split in two tests: the whole sweep, which carries
/// <c>[Trait("Category", Exhaustive.Category)]</c>, and a deterministic sample of it, which does not. Pull requests run the sample
/// only (<c>--filter-not-trait "Category=Exhaustive"</c>); pushes to <c>main</c>, the nightly run and pull requests into
/// <c>main</c> run both, so every case is still checked before a release. A local <c>dotnet test</c> runs both.
/// </summary>
public static class Exhaustive
{
    /// <summary>The test category of a whole sweep.</summary>
    public const string Category = "Exhaustive";

    /// <summary>Returns every <paramref name="every"/>th row of <paramref name="all"/>, starting with the first: the same rows on every run.</summary>
    /// <typeparam name="T1">The first parameter's type.</typeparam>
    /// <typeparam name="T2">The second parameter's type.</typeparam>
    /// <param name="all">The whole sweep.</param>
    /// <param name="every">The stride; 1 keeps every row.</param>
    /// <returns>The sample, in the sweep's order.</returns>
    public static TheoryData<T1, T2> Sample<T1, T2>(TheoryData<T1, T2> all, int every)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentOutOfRangeException.ThrowIfLessThan(every, 1);
        var sample = new TheoryData<T1, T2>();
        int index = 0;
        foreach (TheoryDataRow<T1, T2> row in all)
        {
            if (index++ % every == 0)
            {
                sample.Add(row.Data.Item1, row.Data.Item2);
            }
        }

        return sample;
    }
}
