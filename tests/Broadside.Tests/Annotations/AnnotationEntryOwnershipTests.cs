using System.Reflection;
using System.Text.RegularExpressions;
using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.TestSupport;

namespace Broadside.Tests.Annotations;

/// <summary>
/// Entries that only some annotation subtypes define are read from those subtypes, or from the markup annotation class for the
/// entries of ISO 32000-2 §12.5.6.2 (Table 170); the base class keeps the entries of §12.5.2 (Table 166).
/// </summary>
public sealed class AnnotationEntryOwnershipTests
{
    /// <summary>
    /// The base-class readers the subtypes no longer use, kept until the contraction ticket deletes them. All are
    /// <c>private protected</c>, so no caller outside the annotation class hierarchy can use them.
    /// </summary>
    private static readonly string[] RetiredBaseMembers =
    [
        "ReadByteString", "ReadTextOrStream", "ReadChoice", "ReadAction", "ReadForm", "ReadDate", "ReadPoints", "ReadPointLists",
        "ReadPath", "ReadQuadPoints", "ReadRectangleDifferences", "ParseLineEnding", "ReadLineEndings", "ReadBorderEffect",
        "ReadIconName",
    ];

    [Fact]
    public void Markup_entries_are_read_from_the_markup_class_and_other_entries_from_the_subtype()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        IReadOnlyList<PdfAnnotation> annotations = document.Pages[0].Annotations;

        // Table 170 entries (title, popup, rich text, dates, subject, reply, intent, external data) live on the markup class.
        PdfMarkupAnnotation note = Assert.IsAssignableFrom<PdfMarkupAnnotation>(annotations[0]);
        Assert.Equal(("Alice", "Review"), (note.Title, note.Subject));
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero), note.CreationDate?.Value);
        Assert.Same(annotations[1], note.Popup);
        Assert.Same(annotations[0], Assert.IsType<PdfPopupAnnotation>(annotations[1]).Parent);
        PdfMarkupAnnotation reply = Assert.IsAssignableFrom<PdfMarkupAnnotation>(annotations[2]);
        Assert.Same(note, reply.InReplyTo);
        Assert.Equal(PdfReplyType.Reply, reply.ReplyType);
        PdfMarkupAnnotation freeText = Assert.IsAssignableFrom<PdfMarkupAnnotation>(annotations[4]);
        Assert.Equal(("<p>rich</p>", new Broadside.Objects.CosName("FreeTextCallout")), (freeText.RichText, freeText.Intent));

        // Entries only one subtype defines are on that subtype, and not on the base class or the markup class.
        Assert.Equal("Comment", Assert.IsType<PdfTextAnnotation>(annotations[0]).IconName);
        Assert.Equal(PdfHighlightMode.Outline, Assert.IsType<PdfLinkAnnotation>(annotations[3]).HighlightMode);
        Assert.Equal([new PathPoint(10, 10), new PathPoint(50, 50), new PathPoint(60, 50)], Assert.IsType<PdfFreeTextAnnotation>(annotations[4]).CalloutLine);
        Assert.Equal((PdfLineEnding.Circle, PdfLineEnding.ClosedArrow), (Assert.IsType<PdfLineAnnotation>(annotations[5]).StartLineEnding, Assert.IsType<PdfLineAnnotation>(annotations[5]).EndLineEnding));
        Assert.Equal(new PdfRectangleDifferences(1, 2, 3, 4), Assert.IsType<PdfSquareAnnotation>(annotations[6]).RectangleDifferences);
        Assert.Equal(PdfBorderEffectStyle.Cloudy, Assert.IsType<PdfSquareAnnotation>(annotations[6]).BorderEffect!.Style);
        Assert.Equal(3, Assert.IsType<PdfPolygonAnnotation>(annotations[8]).Vertices.Count);
        Assert.Equal(2, Assert.IsType<PdfStrikeOutAnnotation>(annotations[13]).QuadPoints.Count);
        Assert.Equal(2, Assert.IsType<PdfInkAnnotation>(annotations[16]).InkList.Count);
        Assert.Equal("Paperclip", Assert.IsType<PdfFileAttachmentAnnotation>(annotations[17]).IconName);
        Assert.Equal("Mic", Assert.IsType<PdfSoundAnnotation>(annotations[18]).IconName);
        Assert.NotNull(Assert.IsType<PdfRedactAnnotation>(annotations[25]).DefaultAppearance);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), Assert.IsType<PdfTrapNetAnnotation>(annotations[29]).LastModified?.Value);

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_base_class_declares_no_public_member_for_an_entry_that_only_some_subtypes_define()
    {
        string[] subtypeOnly =
        [
            "QuadPoints", "Vertices", "InkList", "Path", "IconName", "BorderEffect", "RectangleDifferences", "LineEnding",
            "StartLineEnding", "EndLineEnding", "HighlightMode", "Action", "Title", "Popup", "RichText", "CreationDate", "Subject",
            "ReplyType", "Intent", "InReplyTo", "ExternalData", "InteriorColor", "DefaultAppearance",
        ];

        string[] declared = [.. typeof(PdfAnnotation).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(member => member.Name)];

        Assert.Empty(declared.Intersect(subtypeOnly));
    }

    [Fact]
    public void The_retired_base_members_exist_and_no_internal_caller_uses_them()
    {
        // The retired members are still declared, so callers outside this repository's tickets keep compiling.
        const BindingFlags nonPublic = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        string[] declared = [.. typeof(PdfAnnotation).GetMethods(nonPublic).Select(method => method.Name)];
        Assert.Empty(RetiredBaseMembers.Except(declared));
        Assert.All(
            RetiredBaseMembers,
            name => Assert.All(
                typeof(PdfAnnotation).GetMethods(nonPublic).Where(method => method.Name == name),
                method => Assert.NotNull(method.GetCustomAttribute<ObsoleteAttribute>())));

        // No source file other than the base class itself calls one. PdfAppearanceCharacteristics and PdfDocumentInformation have
        // readers of their own that happen to share a name; neither derives from PdfAnnotation. The replacement readers are
        // extension methods under other names, and AnnotationEntryReaders declares ParseLineEnding as a static helper.
        var call = new Regex(@"(?<![\w.])(" + string.Join('|', RetiredBaseMembers) + @")\s*[<(]", RegexOptions.CultureInvariant);
        string source = Path.GetFullPath(Path.Combine(Corpus.Directory, "..", "..", "src", "Broadside"));
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name is "PdfAnnotation.cs" or "PdfAppearanceCharacteristics.cs" or "AnnotationEntryReaders.cs" or "PdfDocumentInformation.cs" || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            foreach ((string line, int number) in File.ReadLines(file).Select((line, index) => (line, index + 1)))
            {
                if (!line.TrimStart().StartsWith("//", StringComparison.Ordinal) && call.IsMatch(line))
                {
                    offenders.Add($"{name}:{number}: {line.Trim()}");
                }
            }
        }

        Assert.Empty(offenders);
    }
}
