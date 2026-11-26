using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Broadside;
using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Images;
using Broadside.Samples;
using Broadside.Signatures;
using Broadside.Text;

Sample.Start("Editing");

using X509Certificate2 certificate = SelfSigned("CN=Contract signer");
WriteInputs(certificate);

SaveIncrementally(certificate);
SaveInFull();
ReduceFileSize();
EditPages();
MergeAndSplit();
DrawAPageOnAnotherPage();
StampPages();
FillAForm();
ExchangeFormData();
Annotate();
Redact();
ExchangeAnnotations();
EditMetadata();
AttachAFile();
Encrypt();
Sample.Finish();

static void SaveIncrementally(X509Certificate2 certificate)
{
    #region incremental
    byte[] original = File.ReadAllBytes("contract-signed.pdf");
    using (PdfDocument document = PdfDocument.Open(original))
    {
        document.Pages[0].AddTextAnnotation(new PdfRectangle(500, 720, 520, 740), "Reviewed, no changes needed.");
        document.Save("contract-reviewed.pdf", new PdfSaveOptions().WithIncrementalUpdate(true));
    }

    // The update is appended: every byte of the signed file is still there, unchanged, and the signature still verifies.
    byte[] updated = File.ReadAllBytes("contract-reviewed.pdf");
    bool untouched = updated.AsSpan(0, original.Length).SequenceEqual(original);
    using PdfDocument reviewed = PdfDocument.Open(updated);
    PdfSignatureValidation signature = reviewed.ValidateSignatures(new PdfSignatureValidationOptions().WithTrustAnchors(certificate))[0];
    Console.WriteLine($"Original bytes untouched: {untouched}; signature: {signature.Status}; covers the whole file: {signature.CoversWholeFile}");
    foreach (PdfModification modification in signature.Modifications)
    {
        Console.WriteLine($"  changed after signing: {modification.Description} (permitted: {modification.IsPermitted})");
    }
    #endregion

    Sample.Expect(untouched, "the signed bytes are a prefix of the updated file");
    Sample.Expect(signature.Status == PdfSignatureStatus.Valid, "the signature survives the incremental save");
    Sample.Expect(!signature.CoversWholeFile && reviewed.Revisions.Count == 3, "the annotation is a third revision");
}

static void SaveInFull()
{
    #region full-save
    using PdfDocument document = PdfDocument.Open("contract.pdf");
    document.Properties.Title = "Contract";
    PdfSaveOptions options = new PdfSaveOptions()
        .WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams) // smaller files; needs PDF 1.5
        .WithRemoveUnreachableObjects(true);                                         // drop objects nothing refers to
    document.Save("contract-compact.pdf", options);
    #endregion

    using PdfDocument saved = PdfDocument.Open("contract-compact.pdf");
    Sample.Expect(saved.Revisions.Count == 1 && saved.Properties.Title == "Contract", "a full save writes one revision");
}

static void ReduceFileSize()
{
    #region reduce-size
    using PdfDocument document = PdfDocument.Open("brochure.pdf");

    // A dry run first: where each image is painted largest, and what the save would do to it.
    var optimization = new PdfImageOptimization()
        .WithMaxResolution(150)        // at most 150 pixels per inch where the image is painted largest
        .WithJpegQuality(80)           // re-encode photographs as JPEG at quality 80
        .WithRecompressLossless();     // also images stored losslessly (Flate)
    foreach (PdfImageOptimizationResult image in document.PlanImageOptimization(optimization))
    {
        Console.WriteLine($"{image.Image.Width} x {image.Image.Height} at {image.EffectiveResolution:F0} dpi: {image.Actions} to {image.TargetWidth} x {image.TargetHeight}");
    }

    document.Save("brochure-small.pdf", new PdfSaveOptions().WithImageOptimization(optimization));
    #endregion

    long before = new FileInfo("brochure.pdf").Length;
    long after = new FileInfo("brochure-small.pdf").Length;
    Console.WriteLine($"{before} bytes before, {after} after");
    using PdfDocument saved = PdfDocument.Open("brochure-small.pdf");
    Sample.Expect(after < before && saved.Pages[0].ExtractImages()[0].Image.Width == 300, "the photo is resampled to 150 dpi and the file is smaller");
}

static void EditPages()
{
    #region pages
    using PdfDocument document = PdfDocument.Open("chapter1.pdf");
    document.Pages.Insert(0, new PdfRectangle(0, 0, 612, 792));                 // a new, empty first page
    document.Pages.Move(2, 1);                                                  // swap the two chapter pages
    document.Pages[2].Rotation = 90;                                            // turn the last page a quarter clockwise
    document.Pages.RemoveAt(0);                                                 // and remove the cover again
    document.Save("chapter1-edited.pdf");
    #endregion

    using PdfDocument saved = PdfDocument.Open("chapter1-edited.pdf");
    Sample.Expect(saved.Pages.Count == 2 && saved.Pages[1].Rotation == 90, "two pages, the last rotated");
    Sample.Expect(saved.Pages[0].ExtractText().Text.Contains("Chapter 1, page 2", StringComparison.Ordinal), "the pages were swapped");
}

static void MergeAndSplit()
{
    #region merge-split
    using PdfDocument first = PdfDocument.Open("chapter1.pdf");
    using PdfDocument second = PdfDocument.Open("chapter2.pdf");
    using (PdfDocument book = PdfDocument.Merge(first, second)) // pages, fonts, links, outlines and form fields are copied
    {
        book.Save("book.pdf");
    }

    using PdfDocument whole = PdfDocument.Open("book.pdf");
    IReadOnlyList<PdfDocument> parts = whole.Split(0..1, 1..4); // one new document per range
    for (int i = 0; i < parts.Count; i++)
    {
        using PdfDocument part = parts[i];
        part.Save($"book-part{i + 1}.pdf");
    }

    using PdfDocument lastPage = whole.ExtractPages(^1..);    // a single range
    lastPage.Save("book-last-page.pdf");
    #endregion

    Sample.Expect(whole.Pages.Count == 4, "four pages merged");
    using PdfDocument part2 = PdfDocument.Open("book-part2.pdf");
    Sample.Expect(part2.Pages.Count == 3, "the second part has three pages");
}

static void DrawAPageOnAnotherPage()
{
    #region page-as-form
    using PdfDocument letterhead = PdfDocument.Open("chapter1.pdf");
    using PdfDocument handout = PdfDocument.Create();
    PdfPage sheet = handout.Pages[0];
    using (PdfCanvas canvas = sheet.CreateCanvas())
    {
        for (int i = 0; i < 2; i++)
        {
            // The page as a form XObject: its crop box, rotated as a viewer shows it, lower-left corner at the origin.
            PdfFormXObject form = handout.CreateFormXObject(letterhead.Pages[i]);
            canvas.SaveState();
            canvas.Transform(new Matrix(0.5, 0, 0, 0.5, 153, 396 - (i * 396)));  // half size, one above the other
            canvas.DrawForm(form);
            canvas.RestoreState();
        }
    }

    handout.Save("chapter1-2-up.pdf");
    #endregion

    using PdfDocument saved = PdfDocument.Open("chapter1-2-up.pdf");
    string text = saved.Pages[0].ExtractText().Text;
    Sample.Expect(text.Contains("page 1", StringComparison.Ordinal) && text.Contains("page 2", StringComparison.Ordinal), "both pages drawn on one sheet");
}

static void StampPages()
{
    #region stamps
    using PdfDocument document = PdfDocument.Open("book.pdf");
    PdfCanvasFont bold = document.GetStandard14Font(Standard14Font.HelveticaBold);
    document.Stamp(PdfStamp.FromText("CONFIDENTIAL", bold, 60)       // centered as the page is viewed
        .WithRotation(45)
        .WithOpacity(0.25)
        .WithTextColor(PdfColor.FromRgb(0.8f, 0, 0)));
    document.Stamp(PdfStamp.FromPage(document.Pages[0])               // the cover, small, in the corner of the others
        .WithScale(0.2)
        .WithHorizontalAlignment(PdfStampAlignment.End)
        .WithVerticalAlignment(PdfStampAlignment.Start), 1..);
    document.AddHeaderFooter(new PdfHeaderFooter(document.GetStandard14Font(Standard14Font.Helvetica), 9)
    {
        HeaderLeft = "Acme Corporation",
        FooterCenter = "Page {page} of {pages}",
    });
    document.Save("book-stamped.pdf");
    #endregion

    using PdfDocument saved = PdfDocument.Open("book-stamped.pdf");
    string body = saved.Pages[1].ExtractText().Text;
    string all = saved.Pages[1].ExtractText(new TextExtractionOptions().WithArtifacts()).Text;
    Sample.Expect(!body.Contains("CONFIDENTIAL", StringComparison.Ordinal), "stamps are artifacts, left out of extracted text");
    Sample.Expect(all.Contains("Page 2 of 4", StringComparison.Ordinal), "page numbers drawn on every page");
}

static void FillAForm()
{
    #region forms
    using PdfDocument document = PdfDocument.Open("application.pdf");
    PdfAcroForm form = document.AcroForm!;
    ((PdfTextField)form.FindField("name")!).SetValue("Ada Lovelace"); // regenerates the field's appearance
    ((PdfCheckBoxField)form.FindField("agree")!).SetChecked(true);
    document.Save("application-filled.pdf");

    document.Flatten(); // draws every field's appearance into its page and removes the form
    document.Save("application-flattened.pdf");
    #endregion

    using PdfDocument filled = PdfDocument.Open("application-filled.pdf");
    Sample.Expect(((PdfTextField)filled.AcroForm!.FindField("name")!).Value == "Ada Lovelace", "the filled value");
    using PdfDocument flat = PdfDocument.Open("application-flattened.pdf");
    Sample.Expect(flat.AcroForm is null || flat.AcroForm.Fields.Count == 0, "no fields after flattening");
    Sample.Expect(flat.Pages[0].ExtractText().Text.Contains("Ada Lovelace", StringComparison.Ordinal), "the value is page content");
}

static void ExchangeFormData()
{
    #region form-data
    using (PdfDocument filled = PdfDocument.Open("application-filled.pdf"))
    using (FileStream xfdf = File.Create("application.xfdf"))
    {
        filled.SaveFormData(xfdf); // XFDF by default; WithFormat(PdfFormDataFormat.Fdf) writes FDF
    }

    using PdfDocument blank = PdfDocument.Open("application.pdf");
    using (FileStream xfdf = File.OpenRead("application.xfdf"))
    {
        PdfFormDataImport import = blank.ImportFormData(xfdf); // the format is sniffed; values regenerate appearances
        Console.WriteLine($"{import.FieldsSet.Count} fields set, {import.FieldsNotFound.Count} names not in this form");
    }

    blank.Save("application-imported.pdf");
    #endregion

    using PdfDocument imported = PdfDocument.Open("application-imported.pdf");
    Sample.Expect(((PdfTextField)imported.AcroForm!.FindField("name")!).Value == "Ada Lovelace", "the imported value");
}

static void Annotate()
{
    #region annotations
    using PdfDocument document = PdfDocument.Open("contract.pdf");
    PdfPage page = document.Pages[0];
    var yellow = new PdfDeviceColor(1, 1, 0);

    page.AddHighlightAnnotation(
        [new PdfQuadrilateral(new PathPoint(72, 718), new PathPoint(300, 718), new PathPoint(72, 734), new PathPoint(300, 734))],
        new PdfAnnotationOptions().WithColor(yellow).WithTitle("Reviewer").WithContents("Check this clause."));
    page.AddFreeTextAnnotation(new PdfRectangle(72, 600, 300, 640), "Approved by legal", new PdfAnnotationOptions().WithFontSize(12));
    page.AddLinkAnnotation(new PdfRectangle(72, 560, 200, 580), new Uri("https://example.com/terms"));
    document.Save("contract-annotated.pdf");
    #endregion

    using PdfDocument saved = PdfDocument.Open("contract-annotated.pdf");
    Sample.Expect(saved.Pages[0].Annotations.Count == 3, "three annotations");
}

static void Redact()
{
    #region redaction
    using PdfDocument document = PdfDocument.Open("contract.pdf");
    PdfPage page = document.Pages[0];

    // Mark the area for review, then apply every mark: the glyphs under it leave the content and a black box takes their place.
    page.AddRedactAnnotation(new PdfRectangle(70, 712, 190, 736), new PdfRedactionOptions().WithOverlayText("WITHHELD"));
    PdfRedactionResult result = document.Redact();

    // A full save with the result's options: an incremental update would keep the original content in the file.
    document.Save("contract-redacted.pdf", result.CreateSaveOptions());
    #endregion

    using PdfDocument saved = PdfDocument.Open("contract-redacted.pdf");
    Sample.Expect(result.GlyphsRemoved > 0, "glyphs removed");
    Sample.Expect(!saved.Pages[0].ExtractText().Text.Contains("Service", StringComparison.Ordinal), "the redacted words are gone");
}

static void ExchangeAnnotations()
{
    #region annotation-data
    using (PdfDocument reviewed = PdfDocument.Open("contract-annotated.pdf"))
    using (FileStream comments = File.Create("contract-comments.xfdf"))
    {
        reviewed.SaveFormData(comments, new PdfFormDataOptions().WithFields(false).WithAnnotations());
    }

    using PdfDocument copy = PdfDocument.Open("contract.pdf");
    using (FileStream comments = File.OpenRead("contract-comments.xfdf"))
    {
        // An annotation whose name (NM) is already on its page replaces that one; the others are added.
        copy.ImportFormData(comments, new PdfFormDataOptions().WithFields(false).WithAnnotations());
    }

    copy.Save("contract-with-comments.pdf");
    #endregion

    using PdfDocument withComments = PdfDocument.Open("contract-with-comments.pdf");
    Sample.Expect(withComments.Pages[0].Annotations.Count == 2, "the two markup annotations; the link is not markup");
}

static void EditMetadata()
{
    #region metadata
    using PdfDocument document = PdfDocument.Open("contract.pdf");
    document.Properties.Title = "Service contract";
    document.Properties.Author = "Acme Legal";
    document.Properties.Keywords = "contract, services";
    document.Save("contract-metadata.pdf"); // writes the document information dictionary and the XMP metadata stream together

    using PdfDocument saved = PdfDocument.Open("contract-metadata.pdf");
    Console.WriteLine($"{saved.Properties.Title} by {saved.Properties.Author}");
    Console.WriteLine($"XMP dc:title: {saved.Metadata?.Packet?.GetLanguageAlternative(XmpNamespaces.DublinCore, "title")}");
    #endregion

    Sample.Expect(saved.Properties.Title == "Service contract", "the title");
}

static void AttachAFile()
{
    #region attachments
    using PdfDocument document = PdfDocument.Open("contract.pdf");
    byte[] csv = "item,amount\nconsulting,1200\n"u8.ToArray();
    document.AddEmbeddedFile("rates.csv", csv, new PdfEmbeddedFileOptions()
        .WithDescription("The rate card")
        .WithMediaType("text/csv"));
    document.Save("contract-attachment.pdf");

    using PdfDocument saved = PdfDocument.Open("contract-attachment.pdf");
    foreach (PdfEmbeddedFileEntry attachment in saved.EmbeddedFiles)
    {
        Console.WriteLine($"Attached: {attachment.FileName}");
    }
    #endregion

    Sample.Expect(saved.EmbeddedFiles.Count == 1 && saved.EmbeddedFiles[0].FileName == "rates.csv", "one attachment");
}

static void Encrypt()
{
    #region encryption
    using PdfDocument document = PdfDocument.Open("contract.pdf");
    PdfEncryptionOptions encryption = new PdfEncryptionOptions(userPassword: null, ownerPassword: "owner-secret")
        .WithAlgorithm(PdfEncryptionAlgorithm.Aes256)          // the default; AES-128 and RC4 exist for old readers
        .WithPermissions(PdfPermissions.Print | PdfPermissions.PrintHighQuality);
    document.Save("contract-encrypted.pdf", new PdfSaveOptions().WithEncryption(encryption));
    #endregion

    using PdfDocument saved = PdfDocument.Open("contract-encrypted.pdf"); // no user password: it opens, with restricted permissions
    Sample.Expect(saved.IsEncrypted && !saved.Permissions.HasFlag(PdfPermissions.Modify), "encrypted, printing only");
}

// The input files: a one-page contract and a signed copy of it, two two-page chapters and a form.
static void WriteInputs(X509Certificate2 certificate)
{
    WriteText("contract.pdf", "Service contract: the parties agree to the terms below.");
    using (PdfDocument contract = PdfDocument.Open(File.ReadAllBytes("contract.pdf")))
    using (FileStream output = File.Create("contract-signed.pdf"))
    {
        contract.Sign(output, new PdfCertificateSigner(certificate), new PdfSignatureOptions().WithReason("Agreed"));
    }

    WriteText("chapter1.pdf", "Chapter 1, page 1", "Chapter 1, page 2");
    WriteText("chapter2.pdf", "Chapter 2, page 1", "Chapter 2, page 2");

    using PdfDocument application = PdfDocument.Create();
    PdfAcroForm form = application.CreateAcroForm();
    form.AddTextField("name", application.Pages[0], new PdfRectangle(72, 700, 372, 724));
    form.AddCheckBox("agree", application.Pages[0], new PdfRectangle(72, 660, 90, 678));
    application.Save("application.pdf");

    // A brochure with a 1200 x 1200 photo painted 2 inches wide: 600 dpi, far more than a screen or an email needs.
    using PdfDocument brochure = PdfDocument.Create();
    byte[] photo = new byte[1200 * 1200 * 3];
    for (int i = 0; i < photo.Length; i++)
    {
        photo[i] = (byte)((i / 3 % 1200) * 255 / 1199 ^ (i * 7919 % 23));
    }

    PdfImage image = brochure.CreateImage(photo, new PdfImageLayout(1200, 1200, PdfDeviceRgbColorSpace.Instance), PdfImageCompression.Flate);
    using (PdfCanvas canvas = brochure.Pages[0].CreateCanvas())
    {
        canvas.DrawImage(image, 72, 500, 144, 144);
    }

    brochure.Save("brochure.pdf");
}

static void WriteText(string path, params string[] pages)
{
    using PdfDocument document = PdfDocument.Create();
    PdfCanvasFont font = document.GetStandard14Font(Standard14Font.Helvetica);
    for (int i = 0; i < pages.Length; i++)
    {
        PdfPage page = i == 0 ? document.Pages[0] : document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        using PdfCanvas canvas = page.CreateCanvas();
        canvas.BeginText();
        canvas.SetFont(font, 14);
        canvas.MoveText(72, 720);
        canvas.ShowText(pages[i]);
        canvas.EndText();
    }

    document.Save(path);
}

static X509Certificate2 SelfSigned(string subject)
{
    using var key = RSA.Create(2048);
    var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, critical: true));
    return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
}
