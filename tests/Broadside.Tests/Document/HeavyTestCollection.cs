namespace Broadside.Tests.Document;

/// <summary>
/// Tests that run on their own, one at a time, after the parallel tests: those that saturate the machine (the multi-threaded
/// stress theories, the 2 GiB file) and the allocation tests, which count the bytes their own thread allocates through
/// <see cref="Broadside.TestSupport.Allocations.Measure"/> and are disturbed by the collections and compilation that parallel tests
/// cause (issue #48).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyTestCollection
{
    public const string Name = "Heavy";
}
