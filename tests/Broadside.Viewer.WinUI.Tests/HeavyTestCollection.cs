namespace Broadside.Viewer.WinUI.Tests;

/// <summary>
/// Tests that run on their own, one at a time, after the parallel tests: those that count display-list builds in a document's cache.
/// A cached list goes stale on any COS change in the process (the change epoch is process-wide), so a parallel test that edits or
/// builds another document would make every list rebuild (#570).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyTestCollection
{
    public const string Name = "Heavy";
}
