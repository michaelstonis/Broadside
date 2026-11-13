using System.Buffers;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Tests.Filters;

namespace Broadside.Tests.Document;

/// <summary>
/// Codes third-party filters, font program parsers and security handlers report (issue #248): owner-prefixed, outside the catalog,
/// never colliding with Broadside's own names, and never using the reserved <c>Broadside</c> owner.
/// </summary>
public sealed class ThirdPartyDiagnosticCodeTests
{
    [Fact]
    public void A_third_party_code_is_named_after_its_owner_and_lies_outside_the_catalog()
    {
        DiagnosticCode code = DiagnosticCode.Create("Contoso.Imaging", "LicenseExpired", DiagnosticSeverity.Warning, "ISO 32000-2 §7.4");

        Assert.Equal("Contoso.Imaging.LicenseExpired", code.Name);
        Assert.Equal("Contoso.Imaging", code.Owner);
        Assert.Equal(DiagnosticArea.None, code.Area);
        Assert.Equal(DiagnosticSeverity.Warning, code.DefaultSeverity);
        Assert.Equal("ISO 32000-2 §7.4", code.Clause);
        Assert.Equal(DiagnosticCode.Create("Contoso.Imaging", "LicenseExpired", DiagnosticSeverity.Error), code);
        Assert.False(DiagnosticCatalog.TryGet(code.Name, out _));
    }

    [Theory]
    [InlineData("Broadside")]
    [InlineData("broadside")]
    [InlineData("BROADSIDE.Fonts")]
    public void The_Broadside_owner_is_reserved(string owner)
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => DiagnosticCode.Create(owner, "FontGlyphInvalid", DiagnosticSeverity.Warning));

        Assert.Equal("owner", exception.ParamName);
    }

    [Theory]
    [InlineData("", "Valid")]
    [InlineData("Contoso.", "Valid")]
    [InlineData(".Contoso", "Valid")]
    [InlineData("Con toso", "Valid")]
    [InlineData("9Contoso", "Valid")]
    [InlineData("Contoso", "")]
    [InlineData("Contoso", "lowerCase")]
    [InlineData("Contoso", "Has.Dot")]
    [InlineData("Contoso", "Has-Dash")]
    public void A_malformed_owner_or_name_is_rejected(string owner, string name) =>
        Assert.Throws<ArgumentException>(() => DiagnosticCode.Create(owner, name, DiagnosticSeverity.Warning));

    [Fact]
    public void An_owner_that_only_begins_with_the_letters_of_Broadside_is_allowed() =>
        Assert.Equal("BroadsideFans.Noted", DiagnosticCode.Create("BroadsideFans", "Noted", DiagnosticSeverity.Information).Name);

    [Fact]
    public void No_catalog_name_contains_the_owner_separator_so_prefixed_codes_cannot_collide() =>
        Assert.All(DiagnosticCatalog.All, code => Assert.False(code.Name.Contains('.', StringComparison.Ordinal), code.Name));

    [Fact]
    public void A_third_party_filter_code_reaches_the_document_with_its_owner()
    {
        using PdfDocument document = PdfDocument.Open(
            FilterTesting.FileWithStream("/Filter /OwnedDecode", "data"u8),
            new PdfOptions().UseFilter(new OwnedFilter()));

        _ = document.DecodeStream((CosStream)document.Resolve(FilterTesting.StreamReference));

        Diagnostic diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal("Contoso.FeatureNoted", diagnostic.Code);
        Assert.Same(OwnedFilter.FeatureNoted, diagnostic.Kind);
        Assert.Equal("Contoso", diagnostic.Kind.Owner);
    }

    [Fact]
    public void A_stand_alone_font_program_context_keeps_a_third_party_code_once()
    {
        DiagnosticCode code = DiagnosticCode.Create("Contoso", "HintsDropped", DiagnosticSeverity.Information);
        var context = new FontProgramContext();

        context.Report(code, DiagnosticSeverity.Information, "First.");
        context.Report(code, DiagnosticSeverity.Information, "Second.");

        Diagnostic diagnostic = Assert.Single(context.Diagnostics);
        Assert.Same(code, diagnostic.Kind);
    }

    [Fact]
    public void Reporting_no_code_is_an_argument_error()
    {
        Assert.Throws<ArgumentNullException>(() => new FilterContext().Report(null!, DiagnosticSeverity.Warning, "Nothing."));
        Assert.Throws<ArgumentNullException>(() => new FontProgramContext().Report(null!, DiagnosticSeverity.Warning, "Nothing."));
    }

    private sealed class OwnedFilter : IStreamFilter
    {
        public static DiagnosticCode FeatureNoted { get; } = DiagnosticCode.Create("Contoso", "FeatureNoted", DiagnosticSeverity.Information);

        public CosName Name { get; } = new("OwnedDecode");

        public void Decode(ReadOnlyMemory<byte> encoded, IBufferWriter<byte> output, FilterContext context)
        {
            context.Report(FeatureNoted, DiagnosticSeverity.Information, "The stream uses a feature that is read and preserved but not supported.");
            output.Write(encoded.Span);
        }
    }
}
