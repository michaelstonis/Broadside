using Broadside.Annotations;
using Broadside.Objects;
using Broadside.Tests.Document;

namespace Broadside.Tests.Annotations;

/// <summary>Annotation views stay live views (ADR 0004): what they derive from the page tree follows changes to it. ISO 32000-2 §12.5.2.</summary>
public sealed class AnnotationLiveViewTests
{
    [Fact]
    public void A_reply_target_added_to_another_page_after_a_first_lookup_is_found_on_that_page()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /Annots [5 0 R 6 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] /Annots [] >>",
            "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /P 3 0 R /IRT 7 0 R >>",
            "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /P 3 0 R /IRT 8 0 R >>",
            "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] >>",
            "<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] /P 4 0 R >>");
        using PdfDocument document = PdfDocument.Open(file);
        IReadOnlyList<PdfAnnotation> first = document.Pages[0].Annotations;
        Assert.Null(((PdfMarkupAnnotation)first[0]).InReplyTo!.Page);

        ((CosArray)document.Resolve(document.Pages[1].Dictionary[new CosName("Annots")])).Add(new CosReference(8, 0));
        PdfAnnotation target = ((PdfMarkupAnnotation)first[1]).InReplyTo!;

        Assert.Same(document.Pages[1], target.Page);
        Assert.Same(document.Pages[1].Annotations[0], target);
    }
}
