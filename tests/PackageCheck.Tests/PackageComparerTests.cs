namespace Broadside.Tools.PackageCheck.Tests;

/// <summary>Drives <see cref="PackageComparer"/> with two folders of synthetic packages, as the reproducible-build check does.</summary>
public sealed class PackageComparerTests : IDisposable
{
    private readonly string _first = Directory.CreateTempSubdirectory("packagecheck-a-").FullName;
    private readonly string _second = Directory.CreateTempSubdirectory("packagecheck-b-").FullName;

    public void Dispose()
    {
        Directory.Delete(_first, recursive: true);
        Directory.Delete(_second, recursive: true);
    }

    [Fact]
    public void Identical_builds_pass_even_though_NuGet_bookkeeping_differs()
    {
        // TestPackage writes a fresh random _rels/.rels and .psmdcp every time, as NuGet does.
        new TestPackage("Broadside").WriteTo(_first);
        new TestPackage("Broadside").WriteTo(_second);

        (IReadOnlyList<string> packages, IReadOnlyList<Finding> findings) = PackageComparer.Compare(_first, _second);

        Assert.Empty(findings);
        Assert.Equal([$"Broadside.{TestPackage.Version}.nupkg", $"Broadside.{TestPackage.Version}.snupkg"], packages);
    }

    [Fact]
    public void A_different_assembly_fails()
    {
        new TestPackage("Broadside").WriteTo(_first);
        new TestPackage("Broadside") { Source = "namespace Sample; public sealed class Changed { }" }.WriteTo(_second);

        IReadOnlyList<Finding> findings = PackageComparer.Compare(_first, _second).Findings;

        Assert.Contains(new Finding($"Broadside.{TestPackage.Version}.nupkg", "lib/net10.0/Broadside.dll differs between the builds"), findings);
        Assert.Contains(new Finding($"Broadside.{TestPackage.Version}.snupkg", "lib/net10.0/Broadside.pdb differs between the builds"), findings);
    }

    [Fact]
    public void A_package_missing_from_one_build_fails()
    {
        new TestPackage("Broadside").WriteTo(_first);
        new TestPackage("Broadside").WriteTo(_second);
        new TestPackage("Broadside.Rendering") { WriteSymbolPackage = false }.WriteTo(_first);

        Finding finding = Assert.Single(PackageComparer.Compare(_first, _second).Findings);

        Assert.Equal(new Finding($"Broadside.Rendering.{TestPackage.Version}.nupkg", $"only in {_first}"), finding);
    }
}
