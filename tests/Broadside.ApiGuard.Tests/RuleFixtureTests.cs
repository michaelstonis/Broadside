using Broadside.Annotations.GuardFixtures;

namespace Broadside.ApiGuard.Tests;

/// <summary>
/// Each mechanical rule run over its fixtures (the <c>*.GuardFixtures</c> namespaces of this assembly): it flags exactly the
/// members named <c>Bad*</c> for it, so the rule is shown to fail on a breach and to pass what follows it.
/// </summary>
public sealed class RuleFixtureTests
{
    private const string Fixtures = "Broadside.Annotations.GuardFixtures.";

    private static Type[] FixtureTypes =>
        [.. ApiSurface.PublicTypes(typeof(RuleFixtureTests).Assembly).Where(t => t.Namespace?.EndsWith(".GuardFixtures", StringComparison.Ordinal) == true)];

    private static void AssertFlags(IEnumerable<Violation> violations, params string[] expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), violations.Select(v => v.Key).Distinct().Order(StringComparer.Ordinal));

    [Fact]
    public void Namespaces_flags_a_type_outside_the_spec_areas() =>
        AssertFlags(ApiRules.Namespaces(FixtureTypes), "Broadside.Miscellany.GuardFixtures.BadStray");

    [Fact]
    public void Prefixes_flags_unprefixed_views_stray_Cos_types_and_prefixed_rendering_types() =>
        AssertFlags(
            ApiRules.Prefixes(FixtureTypes),
            Fixtures + "BadStampView",
            Fixtures + "CosBadOutsideObjects",
            "Broadside.Objects.GuardFixtures.BadUnprefixedObject",
            "Broadside.Rendering.GuardFixtures.PdfBadRenderType");

    [Fact]
    public void UseAndWith_flags_other_verbs_value_Use_and_implementation_With() =>
        AssertFlags(
            ApiRules.UseAndWith(FixtureTypes),
            Fixtures + "FixtureOptions.BadSetScale(Double)",
            Fixtures + "FixtureOptions.UseBadScale(Double)",
            Fixtures + "FixtureOptions.WithBadResolver(IFontResolver)",
            Fixtures + "FixtureOptionsExtensions.BadExtension(FixtureOptions)");

    [Fact]
    public void Sealed_flags_an_unsealed_class_without_an_inheritance_contract() =>
        AssertFlags(ApiRules.Sealed(FixtureTypes), Fixtures + "BadUnsealed");

    [Fact]
    public void CancellationTokenLast_flags_a_token_before_other_parameters_or_misnamed() =>
        AssertFlags(
            ApiRules.CancellationTokenLast(FixtureTypes),
            Fixtures + "TokenFixtures.BadTokenFirst(CancellationToken, Int32)",
            Fixtures + "TokenFixtures.BadTokenName(Int32, CancellationToken)");

    [Fact]
    public void AsyncNaming_flags_mismatched_names_and_missing_tokens() =>
        AssertFlags(
            ApiRules.AsyncNaming(FixtureTypes),
            Fixtures + "AsyncFixtures.BadUnnamed(CancellationToken)",
            Fixtures + "AsyncFixtures.BadNotAwaitableAsync()",
            Fixtures + "AsyncFixtures.BadNoTokenAsync()");

    [Fact]
    public void NoOptionalParametersOnOverloads_flags_a_defaulted_parameter_beside_an_overload() =>
        AssertFlags(ApiRules.NoOptionalParametersOnOverloads(FixtureTypes), Fixtures + "OverloadFixtures.Place(Int32, Int32)");

    [Fact]
    public void ReadOnlyCollections_flags_mutable_collections_handed_out_and_array_properties() =>
        AssertFlags(
            ApiRules.ReadOnlyCollections(FixtureTypes),
            Fixtures + "CollectionFixtures.BadList",
            Fixtures + "CollectionFixtures.BadArray",
            Fixtures + "CollectionFixtures.BadNested",
            Fixtures + "CollectionFixtures.BadResult()",
            Fixtures + "CollectionFixtures.BadOut(Dictionary<String, Int32>&)");

    [Fact]
    public void ExtensionPointsAreInterfaces_flags_an_abstract_class_consumers_can_derive_from() =>
        AssertFlags(
            ApiRules.ExtensionPointsAreInterfaces(FixtureTypes),
            Fixtures + "BadDerivableBase",
            Fixtures + "BadAbstractExtensionPoint");

    [Fact]
    public void ExtensionPointsHaveManagedDefaults_flags_classes_and_interfaces_without_a_default_or_a_registration() =>
        AssertFlags(
            ApiRules.ExtensionPointsHaveManagedDefaults(
                [typeof(IGoodExtensionPoint), typeof(IBadNoDefault), typeof(IBadUnregistered), typeof(BadAbstractExtensionPoint)],
                [typeof(RuleFixtureTests).Assembly]),
            Fixtures + "IBadNoDefault",
            Fixtures + "IBadUnregistered",
            Fixtures + "BadAbstractExtensionPoint");

    [Fact]
    public void GlossaryWords_flags_avoided_nouns_in_type_names_and_avoided_verbs_leading_method_names() =>
        AssertFlags(
            ApiRules.GlossaryWords(FixtureTypes),
            Fixtures + "BadFontProvider",
            Fixtures + "BadFontProvider.LoadBad()");

    [Fact]
    public void Nullable_flags_oblivious_members_and_a_missing_directive() =>
        AssertFlags(
            PublicApiFiles.Nullable(
                "Fixture",
                [
                    ("A/PublicAPI.Unshipped.txt", ["#nullable enable", "Broadside.X.Y() -> void", "~Broadside.X.Z(string s) -> void"]),
                    ("B/PublicAPI.Shipped.txt", ["Broadside.X.W() -> void"]),
                    ("C/PublicAPI.Shipped.txt", []),
                ]),
            "~Broadside.X.Z(string s) -> void",
            "Fixture/PublicAPI.Shipped.txt");

    [Fact]
    public void Signatures_flags_each_rule_a_PublicAPI_line_can_show()
    {
        const string good = "Broadside.Rendering.Fixture.Good";
        string[] bad =
        [
            $"{good}.BadToken(System.Threading.CancellationToken cancellationToken, int count) -> void",
            $"{good}.BadNoTokenAsync() -> System.Threading.Tasks.Task!",
            $"{good}.BadUnnamed(System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask",
            $"{good}.Place(int x, int y = 0) -> void",
            $"{good}.Items.get -> System.Collections.Generic.List<int>!",
            $"{good}.Values.get -> int[]!",
            $"{good}.LoadThing() -> void",
        ];
        string[] lines =
        [
            "#nullable enable",
            "Broadside.Rendering.Fixture.PdfBadType",
            "Broadside.Elsewhere.BadNamespace",
            "Broadside.Rendering.Fixture.FixtureRenderer",
            good,
            $"{good}.Good() -> void",
            $"{good}.GoodAsync(System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<int>!",
            $"{good}.Place(int x) -> void",
            $"static {good}.Shared.get -> {good}!",
            $"{good}.ExportValue.get -> string!",
            "Broadside.Rendering.Fixture.PdfGoodFixtureExtensions",
            $"static Broadside.Rendering.Fixture.PdfGoodFixtureExtensions.Do(this {good}! good) -> void",
            .. bad,
        ];

        AssertFlags(
            PublicApiFiles.Signatures(lines.Select(l => PublicApiLine.Parse("Fixture", l)).OfType<PublicApiLine>()),
            ["Broadside.Rendering.Fixture.PdfBadType", "Broadside.Elsewhere.BadNamespace", "Broadside.Rendering.Fixture.FixtureRenderer", .. bad]);
    }

    [Fact]
    public void A_PublicAPI_line_parses_into_member_parameters_and_result()
    {
        PublicApiLine line = PublicApiLine.Parse(
            "Fixture",
            "static Broadside.PdfDocument.OpenAsync(System.ReadOnlyMemory<byte> bytes, (int A, int B) pair, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.Task<Broadside.PdfDocument!>!")!;

        Assert.Equal("Broadside.PdfDocument.OpenAsync", line.Member);
        Assert.Equal("Broadside.PdfDocument", line.Container);
        Assert.Equal("static", line.Modifiers);
        Assert.Equal(3, line.Parameters!.Count);
        Assert.Equal("System.Threading.Tasks.Task<Broadside.PdfDocument!>!", line.Result);
        Assert.True(PublicApiLine.Parse("Fixture", "Broadside.PdfDocument")!.IsType);
        Assert.Null(PublicApiLine.Parse("Fixture", "#nullable enable"));
    }
}
