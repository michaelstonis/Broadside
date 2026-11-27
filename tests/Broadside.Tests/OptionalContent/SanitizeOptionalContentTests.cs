using Broadside.Content;
using Broadside.Objects;
using Broadside.Tests.Content;
using Broadside.TestSupport;

namespace Broadside.Tests.OptionalContent;

/// <summary>
/// Sanitizing optional content (issue #648) on <c>optional-content.pdf</c>: <see cref="PdfSanitizeOptionalContent.RemoveHidden"/>
/// removes what the default configuration hides and <see cref="PdfSanitizeOptionalContent.FlattenVisible"/> also every marker and
/// <c>OCProperties</c>; either way the page draws exactly what it drew in the default view. ISO 32000-2 §8.11.2 to §8.11.4.
/// </summary>
public sealed class SanitizeOptionalContentTests
{
    private static readonly CosName OC = new("OC");

    public static TheoryData<PdfSanitizeOptionalContent> Modes => new(PdfSanitizeOptionalContent.RemoveHidden, PdfSanitizeOptionalContent.FlattenVisible);

    [Theory]
    [MemberData(nameof(Modes))]
    public void The_page_draws_what_the_default_view_drew(PdfSanitizeOptionalContent mode)
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("optional-content.pdf"));
        List<string> before = Drawn(document);

        PdfSanitization result = document.Sanitize(new PdfSanitizeOptions().WithOptionalContent(mode));

        Assert.Contains(result.Removed, line => line.StartsWith("hidden content operators (optional content)", StringComparison.Ordinal));
        using PdfDocument reopened = PdfDocument.Open(Save(document, result.CreateSaveOptions()));
        Assert.Equal(before, Drawn(reopened));
        Assert.Single(reopened.Pages[0].Annotations);
    }

    [Fact]
    public void Remove_hidden_drops_the_hidden_sections_and_form_and_keeps_the_layers_still_named()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("optional-content.pdf"));
        PdfSanitization result = document.Sanitize();

        // m1 (all of A, B off: hidden) and the nested /a in /b go, with their markers; the form whose OC is B goes from the resources.
        Assert.Contains("hidden content operators (optional content): 3", result.Removed);
        Assert.Contains("optional content markers (BDC /OC … EMC): 3", result.Removed);
        Assert.Contains("hidden XObjects (optional content): 1", result.Removed);
        string content = Content(document);
        Assert.DoesNotContain("/m1", content, StringComparison.Ordinal);
        Assert.DoesNotContain("/b BDC", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Do", content, StringComparison.Ordinal);
        Assert.Contains("/OC /m2 BDC", content, StringComparison.Ordinal);

        // B is off but m2 (VE Or A Not B) still names it, so every group stays.
        Assert.Equal(["A", "B", "C", "D2"], document.OptionalContent!.Groups.Select(group => group.Name));
    }

    [Fact]
    public void Remove_hidden_removes_an_off_group_nothing_names_any_more()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("optional-content.pdf"));
        var properties = (CosDictionary)document.Resolve(document.Pages[0].Resources![new CosName("Properties")]);
        properties.Remove(new CosName("m2"));

        PdfSanitization result = document.Sanitize();

        Assert.Contains("optional content groups: 1", result.Removed);
        Assert.Equal(["A", "C", "D2"], document.OptionalContent!.Groups.Select(group => group.Name));
        Assert.DoesNotContain(new CosName("b"), properties.Keys);
        Assert.Empty(document.OptionalContent.DefaultConfiguration.Off);
    }

    [Fact]
    public void Flatten_visible_leaves_no_optional_content()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("optional-content.pdf"));
        PdfSanitization result = document.Sanitize(new PdfSanitizeOptions().WithOptionalContent(PdfSanitizeOptionalContent.FlattenVisible));

        Assert.Contains("optional content groups: 4", result.Removed);
        Assert.Null(document.OptionalContent);
        Assert.DoesNotContain("/OC", Content(document), StringComparison.Ordinal);
        Assert.False(document.Pages[0].Annotations[0].Dictionary.ContainsKey(OC));
        Assert.DoesNotContain(Security.SanitizeTests.ReachableDictionaries(document), dictionary => dictionary.ContainsKey(OC));
    }

    [Fact]
    public void Keep_changes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Bytes("optional-content.pdf"));
        string content = Content(document);

        PdfSanitization result = document.Sanitize(new PdfSanitizeOptions().WithOptionalContent(PdfSanitizeOptionalContent.Keep));

        Assert.Empty(result.Removed);
        Assert.Equal(content, Content(document));
        Assert.Equal(4, document.OptionalContent!.Groups.Count);
    }

    /// <summary>What the page and its annotations' appearances paint in the default view, in order.</summary>
    private static List<string> Drawn(PdfDocument document)
    {
        var recorder = new RecordingProcessor(ContentEvents.Paths | ContentEvents.Glyphs | ContentEvents.Images | ContentEvents.Shadings);
        document.Pages[0].ProcessContent(recorder);
        // A path ended with n and no clip paints nothing: hidden paths are ended that way.
        return [.. recorder.Lines.Where(line => !line.StartsWith("BeginRun", StringComparison.Ordinal) && line != "EndRun"
            && !(line.StartsWith("Paint None", StringComparison.Ordinal) && !line.Contains(" clip=", StringComparison.Ordinal)))];
    }

    private static string Content(PdfDocument document)
    {
        CosObject contents = document.Resolve(document.Pages[0].Dictionary[new CosName("Contents")]);
        return System.Text.Encoding.Latin1.GetString(document.DecodeStream((CosStream)contents).Span);
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions options)
    {
        using var output = new MemoryStream();
        document.Save(output, options);
        return output.ToArray();
    }
}

