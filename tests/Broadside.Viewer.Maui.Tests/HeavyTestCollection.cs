namespace Broadside.Viewer.Maui.Tests;

/// <summary>
/// Tests that run on their own, one at a time, after the parallel tests: those that count image decodes in a document's caches,
/// whose epoch is process-wide, so a parallel test's COS change would make a cached image stale.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyTestCollection
{
    public const string Name = "Heavy";
}
