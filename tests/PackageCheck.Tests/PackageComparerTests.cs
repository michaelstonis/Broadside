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

    [Theory]
    [InlineData("19:07:20", "21:00:00", "abc", true)]
    [InlineData("19:02:48", "20:57:01", "xyz", false)]
    public void Static_web_asset_manifests_differ_only_in_checkout_times(string writeTime, string lastModified, string hash, bool identical)
    {
        const string entry = "build/Microsoft.AspNetCore.StaticWebAssets.props";
        static string Manifest(string writeTime, string lastModified, string hash) =>
            $$"""<LastWriteTime>Tue, 24 Nov 2026 {{writeTime}} GMT</LastWriteTime><ResponseHeaders>[{"Name":"ETag","Value":"{{hash}}"},{"Name":"Last-Modified","Value":"Tue, 24 Nov 2026 {{lastModified}} GMT"}]</ResponseHeaders>""";
        string second = Manifest(writeTime, lastModified, hash);
        var a = new TestPackage("Broadside.Rendering.WebGL");
        a.Contents[entry] = Manifest("19:02:48", "20:57:01", "abc");
        var b = new TestPackage("Broadside.Rendering.WebGL");
        b.Contents[entry] = second;
        a.WriteTo(_first);
        b.WriteTo(_second);

        IReadOnlyList<Finding> findings = PackageComparer.Compare(_first, _second).Findings;

        Assert.Equal(identical, findings.Count == 0);
    }

    [Theory]
    [InlineData("Broadside.Viewer.WinUI.1.2.3.nupkg", "Broadside.Viewer.WinUI", true)]
    [InlineData("Broadside.Viewer.WinUI.1.2.3.snupkg", "Broadside.Viewer.WinUI", true)]
    [InlineData("Broadside.Viewer.WinUI.1.2.3.nupkg", "Broadside.Viewer", false)]
    [InlineData("Broadside.1.2.3.nupkg", "Broadside.Viewer.WinUI", false)]
    public void Except_names_exactly_one_package(string fileName, string id, bool matches)
    {
        Assert.Equal(matches, PackageComparer.IsPackageFile(fileName, id));
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
