namespace Broadside.Rendering.Skia.Tests;

/// <summary>Proves the test wiring: the package builds, its internals are visible to this project, and the assembly loads.</summary>
public sealed class PackageSmokeTests
{
    [Fact]
    public void Package_assembly_loads()
    {
        Assert.Equal("Broadside.Rendering.Skia", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
