using Broadside.Objects;

namespace Broadside.Tests.OptionalContent;

/// <summary>Optional content views follow changes to the COS objects they were read from (ADR 0004). ISO 32000-2 §8.11.</summary>
public sealed class OptionalContentLiveViewTests
{
    private static readonly CosName OCGs = new("OCGs");

    [Fact]
    public void A_group_added_to_OCGs_after_a_first_read_is_listed()
    {
        using PdfDocument document = PdfDocument.Open(OptionalContentPdf.Build("/OCGs [4 0 R] /D << >>", "<< /Type /OCG /Name (A) >>", "<< /Type /OCG /Name (B) >>"));
        Assert.Equal(["A"], document.OptionalContent!.Groups.Select(group => group.Name));

        ((CosArray)document.OptionalContent.Dictionary[OCGs]).Add(new CosReference(5, 0));

        Assert.Equal(["A", "B"], document.OptionalContent!.Groups.Select(group => group.Name));
    }

    [Fact]
    public void A_group_intent_changed_after_a_first_read_decides_whether_the_group_counts()
    {
        using PdfDocument document = PdfDocument.Open(OptionalContentPdf.Build("/OCGs [4 0 R] /D << /OFF [4 0 R] >>", "<< /Type /OCG /Name (A) >>"));
        CosReference group = new(4, 0);
        Assert.False(document.OptionalContent!.IsVisible(group, document.OptionalContent.GetDefaultStates()));

        ((CosDictionary)document.Resolve(group))[new CosName("Intent")] = new CosName("Design");
        PdfOptionalContentProperties optionalContent = document.OptionalContent!;

        Assert.True(optionalContent.IsVisible(group, optionalContent.GetDefaultStates()));
    }

    [Fact]
    public void A_membership_policy_changed_after_it_was_compiled_is_used()
    {
        using PdfDocument document = PdfDocument.Open(OptionalContentPdf.Build(
            "/OCGs [4 0 R 5 0 R] /D << /OFF [5 0 R] >>",
            "<< /Type /OCG /Name (A) >>",
            "<< /Type /OCG /Name (B) >>",
            "<< /Type /OCMD /OCGs [4 0 R 5 0 R] /P /AllOn >>"));
        PdfOptionalContentProperties optionalContent = document.OptionalContent!;
        PdfOptionalContentState state = optionalContent.GetDefaultStates();
        CosReference membership = new(6, 0);
        Assert.False(optionalContent.IsVisible(membership, state));

        ((CosDictionary)document.Resolve(membership))[new CosName("P")] = new CosName("AnyOn");

        Assert.True(optionalContent.IsVisible(membership, state));
    }
}
