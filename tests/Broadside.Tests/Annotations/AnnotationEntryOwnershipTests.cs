using System.Reflection;
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
    /// Readers for entries only some subtypes define; they live in <c>AnnotationEntryReaders</c> and must not return to the base
    /// class, which keeps only the readers of the entries every annotation has.
    /// </summary>
    private static readonly string[] SubtypeReaders =
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
    public void The_base_class_declares_no_reader_for_an_entry_that_only_some_subtypes_define()
    {
        const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        MethodInfo[] methods = typeof(PdfAnnotation).GetMethods(members);

        Assert.DoesNotContain(methods, method => SubtypeReaders.Contains(method.Name));
        Assert.DoesNotContain(methods, method => method.GetCustomAttribute<ObsoleteAttribute>() is not null);
    }
}
