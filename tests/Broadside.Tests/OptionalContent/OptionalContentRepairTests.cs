using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Tests.OptionalContent;

/// <summary>Malformed optional content is repaired with a diagnostic in lenient mode and throws in strict mode. ISO 32000-2 §8.11.</summary>
public class OptionalContentRepairTests
{
    private const string TwoGroups = "<< /Type /OCG /Name (A) >>";

    public static TheoryData<string, string[], string, bool[]> Repairs => new()
    {
        // D missing: an empty configuration, every group ON.
        { "/OCGs [4 0 R 5 0 R]", [TwoGroups, "<< /Type /OCG /Name (B) >>"], "OptionalContentConfigMissing", [true, true] },
        // BaseState OFF in D: honoured.
        { "/OCGs [4 0 R 5 0 R] /D << /BaseState /OFF /ON [5 0 R] >>", [TwoGroups, "<< /Type /OCG /Name (B) >>"], "OptionalContentBaseStateInvalid", [false, true] },
        // BaseState Unchanged in D: read as ON.
        { "/OCGs [4 0 R 5 0 R] /D << /BaseState /Unchanged /OFF [5 0 R] >>", [TwoGroups, "<< /Type /OCG /Name (B) >>"], "OptionalContentBaseStateInvalid", [true, false] },
        // A group in both ON and OFF: OFF wins.
        { "/OCGs [4 0 R 5 0 R] /D << /ON [4 0 R] /OFF [4 0 R] >>", [TwoGroups, "<< /Type /OCG /Name (B) >>"], "OptionalContentGroupOnAndOff", [false, true] },
        // A group in OFF that OCGs does not list: not optional content.
        { "/OCGs [4 0 R] /D << /OFF [5 0 R] >>", [TwoGroups, "<< /Type /OCG /Name (B) >>"], "OptionalContentGroupNotListed", [true] },
        // Intent other than View in D: honoured.
        { "/OCGs [4 0 R] /D << /Intent /Design >>", [TwoGroups], "OptionalContentIntentInvalid", [true] },
        // An OCGs entry that is an OCMD: ignored.
        { "/OCGs [4 0 R 5 0 R] /D << >>", [TwoGroups, "<< /Type /OCMD /OCGs [4 0 R] >>"], "OptionalContentGroupInvalid", [true] },
    };

    [Theory]
    [MemberData(nameof(Repairs))]
    public void A_malformed_configuration_is_repaired_with_a_diagnostic(string properties, string[] objects, string code, bool[] expected)
    {
        using PdfDocument document = PdfDocument.Open(OptionalContentPdf.Build(properties, objects));
        PdfOptionalContentProperties optionalContent = document.OptionalContent!;

        PdfOptionalContentState state = optionalContent.GetDefaultStates();

        Assert.Equal(expected, optionalContent.Groups.Select(state.IsOn));
        Assert.Contains(code, document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Theory]
    [MemberData(nameof(Repairs))]
    public void A_malformed_configuration_throws_in_strict_mode(string properties, string[] objects, string code, bool[] expected)
    {
        _ = expected;
        using PdfDocument document = PdfDocument.Open(OptionalContentPdf.Build(properties, objects), new PdfOptions().UseStrict());

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => document.OptionalContent!.GetDefaultStates());

        Assert.Equal(code, exception.Diagnostic.Code);
    }

    [Fact]
    public void Missing_OCGs_make_nothing_optional()
    {
        using PdfDocument document = PdfDocument.Open(OptionalContentPdf.Build("/D << >>", TwoGroups));

        PdfOptionalContentProperties optionalContent = document.OptionalContent!;

        Assert.Empty(optionalContent.Groups);
        Assert.True(optionalContent.IsVisible(new CosReference(4, 0), optionalContent.GetDefaultStates()));
        Assert.Equal(["OptionalContentGroupsMissing", "OptionalContentGroupNotListed"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    public static TheoryData<string, bool> InvalidExpressions => new()
    {
        // Unknown operator: falls back to OCGs/P (A is OFF, AnyOn -> hidden).
        { "/VE [/Xor 4 0 R] /OCGs [4 0 R]", false },
        // Not with two operands: the first is used (not A = visible).
        { "/VE [/Not 4 0 R 5 0 R]", true },
        // An operand that is not a group or an expression: ignored (Or(B) = B ON = visible).
        { "/VE [/Or 42 5 0 R]", true },
        // Too deep: falls back to OCGs/P; without OCGs it has no effect (visible).
        { "/VE " + string.Concat(Enumerable.Repeat("[/Not ", 40)) + "4 0 R" + new string(']', 40), true },
        // An unknown policy: AnyOn.
        { "/OCGs [4 0 R] /P /Sometimes", false },
    };

    [Theory]
    [MemberData(nameof(InvalidExpressions))]
    public void An_invalid_visibility_expression_or_policy_is_repaired(string membership, bool visible)
    {
        using PdfDocument document = PdfDocument.Open(OptionalContentPdf.Build(
            "/OCGs [4 0 R 5 0 R] /D << /OFF [4 0 R] >>", TwoGroups, "<< /Type /OCG /Name (B) >>", $"<< /Type /OCMD {membership} >>"));
        PdfOptionalContentProperties optionalContent = document.OptionalContent!;

        Assert.Equal(visible, optionalContent.IsVisible(new CosReference(6, 0), optionalContent.GetDefaultStates()));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code is "VisibilityExpressionInvalid" or "VisibilityPolicyInvalid");
    }

    [Fact]
    public void A_self_referencing_visibility_expression_terminates()
    {
        using PdfDocument document = PdfDocument.Open(OptionalContentPdf.Build(
            "/OCGs [4 0 R] /D << >>", TwoGroups, "<< /Type /OCMD /VE 6 0 R /OCGs [4 0 R] /P /AllOff >>", "[/And 4 0 R 6 0 R]"));
        PdfOptionalContentProperties optionalContent = document.OptionalContent!;

        Assert.False(optionalContent.IsVisible(new CosReference(5, 0), optionalContent.GetDefaultStates()));
        Assert.Null(optionalContent.FindMembership(new CosReference(5, 0))!.VisibilityExpression);
        Assert.Contains("VisibilityExpressionInvalid", document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_membership_lists_its_groups_policy_and_parsed_expression()
    {
        using PdfDocument document = PdfDocument.Open(OptionalContentPdf.Build(
            "/OCGs [4 0 R 5 0 R] /D << >>", TwoGroups, "<< /Type /OCG /Name (B) >>",
            "<< /Type /OCMD /OCGs 4 0 R /P /AllOff /VE [/Or 4 0 R [/Not 5 0 R] null] >>"));

        PdfOptionalContentMembership membership = document.OptionalContent!.FindMembership(new CosReference(6, 0))!;

        Assert.Equal(["A"], membership.Groups.Select(group => group.Name));
        Assert.Equal(PdfVisibilityPolicy.AllOff, membership.Policy);
        Assert.Equal("Or(A, Not(B))", membership.VisibilityExpression!.ToString());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_inline_OC_property_list_is_honoured_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(OptionalContentPdf.Build("/OCGs [4 0 R] /D << /OFF [4 0 R] >>", TwoGroups));
        var tracker = new PdfOptionalContentTracker(document.OptionalContent, null);
        var inline = new CosDictionary { [new CosName("Type")] = new CosName("OCMD"), [new CosName("OCGs")] = new CosReference(4, 0) };

        tracker.BeginMarkedContent(new CosName("OC"), inline, inlineProperties: true);

        Assert.False(tracker.IsVisible);
        Assert.Equal(["OptionalContentInlineProperties"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void A_state_from_another_document_is_refused()
    {
        using PdfDocument first = PdfDocument.Open(OptionalContentPdf.Build("/OCGs [4 0 R] /D << >>", TwoGroups));
        using PdfDocument second = PdfDocument.Open(OptionalContentPdf.Build("/OCGs [4 0 R] /D << >>", TwoGroups));

        PdfOptionalContentState state = first.OptionalContent!.GetDefaultStates();

        Assert.Throws<ArgumentException>(() => second.OptionalContent!.IsVisible(new CosReference(4, 0), state));
        Assert.Throws<ArgumentException>(() => state.IsOn(second.OptionalContent!.Groups[0]));
    }
}
