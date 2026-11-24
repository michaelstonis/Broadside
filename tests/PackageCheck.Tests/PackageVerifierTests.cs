namespace Broadside.Tools.PackageCheck.Tests;

/// <summary>
/// Drives <see cref="PackageVerifier"/> with synthetic packages against the repository's real <c>tools/PackageCheck/package-policy.json</c>:
/// a package that meets the policy passes, and each broken property (dependency, symbols, SourceLink, licence, README, icon) fails.
/// </summary>
public sealed class PackageVerifierTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("packagecheck-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    internal static string RepositoryRoot
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException("Broadside.slnx not found above the test assembly.");
        }
    }

    private static PackagePolicy Policy { get; } = PackagePolicy.Load(Path.Combine(RepositoryRoot, "tools", "PackageCheck", "package-policy.json"));

    [Theory]
    [InlineData("Broadside.Rendering")]
    [InlineData("Broadside.Layout")]
    [InlineData("Broadside.Viewer")]
    public void Managed_package_with_Microsoft_dependencies_passes(string id)
    {
        TestPackage package = Managed(id);
        package.Dependencies.Add(("Broadside", TestPackage.Version));

        Assert.Empty(Verify(package));
    }

    [Fact]
    public void Core_package_with_its_licence_file_and_notices_passes()
    {
        var package = new TestPackage("Broadside") { LicenseType = "file", License = "LICENSE.txt" };
        package.ExtraFiles.AddRange(Policy.RuleFor("Broadside").Files);

        Assert.Empty(Verify(package));
    }

    [Theory]
    [InlineData("Broadside")]
    [InlineData("Broadside.Rendering")]
    [InlineData("Broadside.Layout")]
    [InlineData("Broadside.Fonts.Standard14")]
    public void Non_Microsoft_dependency_of_a_managed_package_fails(string id)
    {
        TestPackage package = Managed(id);
        package.Dependencies.Add(("Newtonsoft.Json", "13.0.3"));

        Finding finding = Assert.Single(Verify(package), f => f.Message.Contains("Newtonsoft.Json", StringComparison.Ordinal));
        Assert.Equal($"depends on Newtonsoft.Json 13.0.3, which is not on its allowlist in the package policy", finding.Message);
    }

    [Fact]
    public void A_backends_platform_library_is_allowed_only_for_that_backend()
    {
        TestPackage skia = Managed("Broadside.Rendering.Skia");
        skia.Dependencies.Add(("SkiaSharp", "4.153.1"));
        TestPackage rendering = Managed("Broadside.Rendering");
        rendering.Dependencies.Add(("SkiaSharp", "4.153.1"));

        Assert.Empty(Verify(skia));
        Assert.Contains(Verify(rendering), f => f.Message.Contains("SkiaSharp", StringComparison.Ordinal));
    }

    [Fact]
    public void A_package_the_policy_does_not_name_gets_the_strict_default()
    {
        Assert.DoesNotContain("Broadside.Future", Policy.NamedPackages);
        TestPackage clean = Managed("Broadside.Future");
        TestPackage native = Managed("Broadside.Future");
        native.Dependencies.Add(("SkiaSharp", "4.153.1"));

        Assert.Empty(Verify(clean));
        Assert.Contains(Verify(native), f => f.Message.Contains("SkiaSharp", StringComparison.Ordinal));
    }

    [Fact]
    public void Dependency_on_a_sibling_package_at_another_version_fails()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.Dependencies.Add(("Broadside", "1.0.0"));

        Assert.Equal($"depends on Broadside 1.0.0, not on its own version {TestPackage.Version}", Assert.Single(Verify(package)).Message);
    }

    [Fact]
    public void Package_without_a_symbol_package_fails()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.WriteSymbolPackage = false;

        Assert.Equal($"has no symbol package (Broadside.Rendering.{TestPackage.Version}.snupkg)", Assert.Single(Verify(package)).Message);
    }

    [Fact]
    public void Symbol_package_without_the_pdb_fails()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.PdbInSymbolPackage = false;

        Assert.Equal("symbol package has no lib/net10.0/Broadside.Rendering.pdb", Assert.Single(Verify(package)).Message);
    }

    [Fact]
    public void Windows_pdb_fails()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.PdbOverride = "Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8.ToArray();

        Assert.Equal("lib/net10.0/Broadside.Rendering.pdb: is not a portable PDB", Assert.Single(Verify(package)).Message);
    }

    [Fact]
    public void Pdb_of_another_build_fails()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.PdbOverride = TestPackage.Compile("Broadside.Rendering", "namespace Sample; public sealed class Other { }", package.SourcePath, package.SourceLink, false).Pdb;

        Assert.Contains("PDB ID differs", Assert.Single(Verify(package)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdb_without_SourceLink_fails()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.SourceLink = null;

        Assert.Equal("lib/net10.0/Broadside.Rendering.pdb: has no SourceLink information", Assert.Single(Verify(package)).Message);
    }

    [Fact]
    public void Source_document_SourceLink_does_not_map_fails()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.SourceLink = TestPackage.SourceLinkFor(TestPackage.Commit, "/_/tests/*");

        Assert.Equal(
            "lib/net10.0/Broadside.Rendering.pdb: source document /_/src/Sample/Thing.cs is neither embedded nor mapped by SourceLink",
            Assert.Single(Verify(package)).Message);
    }

    [Fact]
    public void Source_document_mapped_to_another_commit_fails()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.SourceLink = TestPackage.SourceLinkFor(new string('f', 40));

        Assert.Contains("not under https://raw.githubusercontent.com/michaelstonis/Broadside/0123456789", Assert.Single(Verify(package)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Embedded_source_document_needs_no_mapping()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.SourceLink = TestPackage.SourceLinkFor(TestPackage.Commit, "/_/tests/*");
        package.EmbedSource = true;

        Assert.Empty(Verify(package));
    }

    [Fact]
    public void Source_path_of_a_local_build_fails()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.SourcePath = "/home/someone/Broadside/src/Sample/Thing.cs";
        package.SourceLink = TestPackage.SourceLinkFor(TestPackage.Commit, "/home/someone/Broadside/*");

        Assert.Contains("is not under /_/ (pack with ContinuousIntegrationBuild=true)", Assert.Single(Verify(package)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceLink_urls_are_collected_for_the_online_check()
    {
        TestPackage package = Managed("Broadside.Rendering");
        var urls = new List<string>();

        Assert.Empty(PackageVerifier.VerifyPackage(package.WriteTo(_directory), Policy, urls));
        Assert.Equal($"https://raw.githubusercontent.com/michaelstonis/Broadside/{TestPackage.Commit}/src/Sample/Thing.cs", Assert.Single(urls));
    }

    [Fact]
    public void Package_without_readme_or_icon_fails()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.Readme = null;
        package.Icon = null;

        Assert.Equal(["has no icon in its nuspec", "has no readme in its nuspec"], Verify(package).Select(f => f.Message).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Licence_other_than_the_policys_fails()
    {
        TestPackage package = Managed("Broadside.Fonts.Cmaps");
        package.License = "MIT";

        Assert.Equal(
            "declares licence expression 'MIT', the policy expects expression 'MIT AND BSD-3-Clause'",
            Assert.Single(Verify(package)).Message);
    }

    [Fact]
    public void Missing_third_party_licence_file_fails()
    {
        TestPackage package = Managed("Broadside.Fonts.Cmaps");
        package.ExtraFiles.Remove("licenses/cmap-resources/LICENSE.md");

        Assert.Equal("does not contain licenses/cmap-resources/LICENSE.md", Assert.Single(Verify(package)).Message);
    }

    [Fact]
    public void Repository_metadata_must_name_the_repository_and_a_full_commit()
    {
        TestPackage package = Managed("Broadside.Rendering");
        package.RepositoryUrl = "https://github.com/someone/fork";
        package.RepositoryCommit = "0123456";

        Assert.Equal(
            ["has no full commit in its repository element (0123456)", "names repository https://github.com/someone/fork, not https://github.com/michaelstonis/Broadside"],
            Verify(package).Select(f => f.Message).Where(m => !m.Contains(".pdb", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_package_the_policy_names_exists_under_src()
    {
        foreach (string id in Policy.NamedPackages)
        {
            Assert.True(File.Exists(Path.Combine(RepositoryRoot, "src", id, id + ".csproj")), $"package-policy.json names {id}, which is not a project under src/");
        }
    }

    [Fact]
    public void Policy_with_an_unknown_dependency_set_is_rejected()
    {
        const string json = """{"repositoryUrl":"r","sourceLinkUrlPrefix":"s","default":{"dependencies":["@missing"],"license":"MIT"}}""";

        Assert.Throws<InvalidDataException>(() => PackagePolicy.Parse(json));
    }

    /// <summary>A package that meets its rule in the policy: its licence and required files, README and icon.</summary>
    private static TestPackage Managed(string id)
    {
        PackageRule rule = Policy.RuleFor(id);
        bool file = rule.License.StartsWith("file:", StringComparison.Ordinal);
        var package = new TestPackage(id) { LicenseType = file ? "file" : "expression", License = file ? rule.License[5..] : rule.License };
        package.ExtraFiles.AddRange(rule.Files);
        package.Dependencies.Add(("Microsoft.Extensions.Options", "10.0.12"));
        package.Dependencies.Add(("System.Security.Cryptography.Pkcs", "10.0.13"));
        return package;
    }

    private List<Finding> Verify(TestPackage package)
    {
        string path = package.WriteTo(Directory.CreateDirectory(Path.Combine(_directory, Path.GetRandomFileName())).FullName);
        return [.. PackageVerifier.VerifyPackage(path, Policy)];
    }
}
