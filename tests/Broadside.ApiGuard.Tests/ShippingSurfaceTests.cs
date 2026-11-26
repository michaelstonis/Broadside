using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Security;

namespace Broadside.ApiGuard.Tests;

/// <summary>
/// The mechanical rules of the API review checklist (site/articles/api-review.md) over the public surface of every v1 shipping
/// package (<see cref="ShippingPackages"/>). A breach fails unless <see cref="KnownViolations"/> lists it, and a listed breach
/// that is gone fails too, so the list stays exact.
/// </summary>
public sealed class ShippingSurfaceTests
{
    /// <summary>The extension points the spec names, each an interface registered through the options object.</summary>
    internal static readonly Type[] ExtensionPoints =
    [
        typeof(IStreamFilter), typeof(IImageFilter), typeof(IStreamEncoder), // filters
        typeof(IFontProgramParser), // font program parsers
        typeof(IFontResolver), // font resolver
        typeof(IColorManagement), // color management
        typeof(IRenderingBackend), // backends
        typeof(ISecurityHandler), // security handlers
    ];

    private static Type[] Types => [.. ShippingPackages.PublicTypes];

    [Fact]
    public void Every_project_under_src_is_a_shipping_package()
    {
        string[] projects = [.. Directory.GetDirectories(Path.Combine(ShippingPackages.RepositoryRoot, "src"))
            .Where(d => File.Exists(Path.Combine(d, Path.GetFileName(d) + ".csproj")))
            .Select(Path.GetFileName).OfType<string>()];
        string[] unlisted = [.. projects.Where(p => !ShippingPackages.All.Contains(p))];
        string[] missing = [.. ShippingPackages.All.Where(p => !projects.Contains(p))];

        Assert.True(unlisted.Length == 0, $"Add to ShippingPackages: {string.Join(", ", unlisted)}");
        Assert.True(missing.Length == 0, $"Listed in ShippingPackages but not under src/: {string.Join(", ", missing)}");
    }

    [Fact]
    public void The_reflected_packages_have_a_public_surface() =>
        Assert.All(ShippingPackages.Assemblies, assembly => Assert.NotEmpty(ApiSurface.PublicTypes(assembly)));

    [Fact]
    public void Every_PublicAPI_line_of_a_shipping_package_parses()
    {
        foreach (string package in ShippingPackages.All)
        {
            PublicApiLine[] lines = [.. PublicApiFiles.Read(package)];
            Assert.NotEmpty(lines);
            Assert.All(lines, line => Assert.False(string.IsNullOrWhiteSpace(line.Member), line.Text));
        }
    }

    /// <summary>
    /// The v1 freeze (#262): every shipping package records its public API in <c>PublicAPI.Shipped.txt</c>, so removing or changing
    /// a shipped member fails the build (RS0017, which <c>check-api-freeze.sh</c> proves). The analyzer's code fix silences that
    /// error by writing a <c>*REMOVED*</c> line into <c>PublicAPI.Unshipped.txt</c>; this test fails on such a line instead, so a
    /// breaking change cannot pass CI in 1.x.
    /// </summary>
    [Fact]
    public void No_shipped_member_is_removed_or_changed()
    {
        foreach (string package in ShippingPackages.All)
        {
            string[] paths = [.. PublicApiFiles.Paths(package)];
            Assert.True(File.Exists(paths[0]), $"{package} has no PublicAPI.Shipped.txt.");
            Assert.Contains(File.ReadLines(paths[0]), line => PublicApiLine.Parse(package, line) is not null);

            string[] removed = [.. paths.Where(File.Exists).SelectMany(File.ReadLines).Where(l => l.TrimStart().StartsWith("*REMOVED*", StringComparison.Ordinal))];
            Assert.True(
                removed.Length == 0,
                $"{package}: a shipped member was removed or changed, which breaks 1.x consumers. Add an overload or a new member "
                + $"instead:\n  {string.Join("\n  ", removed)}");
        }
    }

    [Fact]
    public void Namespaces_are_the_root_or_the_spec_areas() => AssertKnown(nameof(ApiRules.Namespaces), ApiRules.Namespaces(Types));

    [Fact]
    public void Pdf_and_Cos_prefixes_mark_the_document_model_and_the_COS_layer() => AssertKnown(nameof(ApiRules.Prefixes), ApiRules.Prefixes(Types));

    [Fact]
    public void Options_methods_are_Use_or_With() => AssertKnown(nameof(ApiRules.UseAndWith), ApiRules.UseAndWith(Types));

    [Fact]
    public void Classes_are_sealed_unless_designed_for_inheritance() => AssertKnown(nameof(ApiRules.Sealed), ApiRules.Sealed(Types));

    [Fact]
    public void The_cancellation_token_is_last() => AssertKnown(nameof(ApiRules.CancellationTokenLast), ApiRules.CancellationTokenLast(Types));

    [Fact]
    public void Async_names_match_awaitable_results_that_take_a_token() => AssertKnown(nameof(ApiRules.AsyncNaming), ApiRules.AsyncNaming(Types));

    [Fact]
    public void Overloaded_methods_have_no_optional_parameters() =>
        AssertKnown(nameof(ApiRules.NoOptionalParametersOnOverloads), ApiRules.NoOptionalParametersOnOverloads(Types));

    [Fact]
    public void Collections_handed_out_are_read_only() => AssertKnown(nameof(ApiRules.ReadOnlyCollections), ApiRules.ReadOnlyCollections(Types));

    [Fact]
    public void Extension_points_are_interfaces() =>
        AssertKnown(nameof(ApiRules.ExtensionPointsAreInterfaces), ApiRules.ExtensionPointsAreInterfaces(Types));

    [Fact]
    public void Every_extension_point_has_a_managed_default_registered_through_options() =>
        AssertKnown(nameof(ApiRules.ExtensionPointsHaveManagedDefaults), ApiRules.ExtensionPointsHaveManagedDefaults(ExtensionPoints, ShippingPackages.Assemblies));

    [Fact]
    public void Names_use_the_glossary() => AssertKnown(nameof(ApiRules.GlossaryWords), ApiRules.GlossaryWords(Types));

    [Fact]
    public void Every_member_carries_nullable_annotations() =>
        AssertKnown(nameof(PublicApiFiles.Nullable), ShippingPackages.All.SelectMany(PublicApiFiles.Nullable));

    [Fact]
    public void The_platform_packages_follow_the_signature_rules() =>
        AssertKnown(KnownViolations.PlatformSignatures, ShippingPackages.Platform.SelectMany(p => PublicApiFiles.Signatures(PublicApiFiles.Read(p))));

    private static void AssertKnown(string rule, IEnumerable<Violation> violations)
    {
        Violation[] found = [.. violations.Distinct()];
        IReadOnlyDictionary<string, string> known = KnownViolations.For(rule);
        string[] unexpected = [.. found.Where(v => !known.ContainsKey(v.Key)).Select(v => v.ToString()).Order(StringComparer.Ordinal)];
        string[] fixedNow = [.. known.Keys.Where(k => !found.Any(v => v.Key == k)).Order(StringComparer.Ordinal)];

        Assert.True(
            unexpected.Length == 0,
            $"{rule}: {unexpected.Length} breach(es) of the API review checklist (site/articles/api-review.md). Fix them, or list each in "
            + $"KnownViolations with the reason a reviewer accepted:\n  {string.Join("\n  ", unexpected)}");
        Assert.True(fixedNow.Length == 0, $"{rule}: no longer breached; remove from KnownViolations:\n  {string.Join("\n  ", fixedNow)}");
    }
}
