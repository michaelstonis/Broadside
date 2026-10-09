namespace Broadside.Tests.Document;

/// <summary>
/// Tests that saturate the machine (the multi-threaded stress theory, the 2 GiB file) run on their own, after the parallel tests, so
/// they neither slow nor disturb the allocation tests, which count the bytes their own thread allocates.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyTestCollection
{
    public const string Name = "Heavy";
}
