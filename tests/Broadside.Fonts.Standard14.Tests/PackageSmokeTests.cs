namespace Broadside.Fonts.Standard14.Tests;

/// <summary>Proves the test wiring: the package builds, its internals are visible to this project, and the assembly loads.</summary>
public class PackageSmokeTests
{
    [Fact]
    public void Package_assembly_loads()
    {
        Assert.Equal("Broadside.Fonts.Standard14", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}
