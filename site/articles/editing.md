---
title: "Editing"
---

# Editing

This guide changes existing files: saving incrementally or in full, page operations, merging and splitting, forms, annotations, metadata, attachments and encryption. The code comes from the [`Editing`](https://github.com/michaelstonis/Broadside/tree/main/samples/guides/Editing) sample, which writes its own input files, then edits them. CI builds and runs it on every change, and checks every file it writes with `qpdf --check` (and the signed ones with poppler's `pdfsig`).

Edits go through the document model: a `PdfPage`, a form field or the document's properties are live views over the file's objects, so a change is a change to the file, written by the next save. Every feature declares the PDF version it needs, and a save raises the file's version to it, with a `VersionRaised` diagnostic.

| Feature | Minimum PDF version |
|---|---|
| Incremental update, page operations, text and link annotations | 1.0 |
| Interactive forms | 1.2 |
| Embedded files, most markup annotations (highlight, free text, stamp, ink) | 1.3 |
| XMP metadata stream | 1.4 |
| Cross-reference streams and object streams | 1.5 |
| AES-128 encryption | 1.6 |
| AES-256 encryption | 2.0 |
| Associated files (`AF`) | 2.0 |

## Incremental and full saves

A full save writes the whole file again. An incremental save appends only what changed after the original bytes, which stay exactly as they were. That is what keeps an existing signature valid: the signature covers the bytes of its revision, and an update after it adds a revision without touching them.

<!-- snippet: samples/guides/Editing/Program.cs#incremental -->
```csharp
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
```
<!-- endsnippet -->

The sample checks both claims: the signed file is a byte-for-byte prefix of the updated one, and the signature validates as `Valid`, with the annotation listed as a permitted change made after signing. Saving a signed document in full would rewrite the signed bytes and break its signatures. After an incremental save the document reads from the file it wrote, so the next incremental save appends only what changes after it.

An incremental save keeps the document's encryption, so it is refused with a `NotSupportedException` when that encryption cannot be kept as it is. One case comes from damaged files: when the trailer's `Encrypt` entry (or the one an earlier revision's trailer supplies) is not an encryption dictionary, the document opens as not encrypted with an `EncryptDictionaryInvalid` error, and an update appended to it would repeat the damaged entry and could leave a file that no longer opens. Save such a document as a complete file instead; a full save drops the damaged entry.

An incremental save is also refused, with a `NotSupportedException`, when the file's cross-reference information is damaged and had to be rebuilt: rebuilt by scanning the whole file, or read through a `Prev` or `XRefStm` offset that points at no cross-reference section (the nearest section is read instead) or a `Prev` chain that loops. The update would point back at those damaged sections, and a reader of the updated file can repair them differently, reach stale or damaged entries, or fail to open it. Save a complete file instead: it is written from what the document read. Small repairs that leave the chain as the file states it do not stop an incremental save: a wrong `startxref` offset, a malformed table line, or an entry whose object sits a few bytes from its stated offset. The document's diagnostics show which repairs a file needed.

A full save can also reorganize the file: a cross-reference stream with object streams for smaller files, dropping unreachable objects, or linearizing for fast first-page display on the web (`WithLinearize(true)`):

<!-- snippet: samples/guides/Editing/Program.cs#full-save -->
```csharp
using PdfDocument document = PdfDocument.Open("contract.pdf");
document.Properties.Title = "Contract";
PdfSaveOptions options = new PdfSaveOptions()
    .WithCrossReferenceLayout(PdfCrossReferenceLayout.StreamWithObjectStreams) // smaller files; needs PDF 1.5
    .WithRemoveUnreachableObjects(true);                                         // drop objects nothing refers to
document.Save("contract-compact.pdf", options);
```
<!-- endsnippet -->

Conformance: [ISO 32000-2 §7.5 (file structure, incremental updates §7.5.6, cross-reference streams §7.5.8)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Pages

`Pages` inserts, moves and removes pages, and a page's `Rotation` turns it in quarter turns:

<!-- snippet: samples/guides/Editing/Program.cs#pages -->
```csharp
using PdfDocument document = PdfDocument.Open("chapter1.pdf");
document.Pages.Insert(0, new PdfRectangle(0, 0, 612, 792));                 // a new, empty first page
document.Pages.Move(2, 1);                                                  // swap the two chapter pages
document.Pages[2].Rotation = 90;                                            // turn the last page a quarter clockwise
document.Pages.RemoveAt(0);                                                 // and remove the cover again
document.Save("chapter1-edited.pdf");
```
<!-- endsnippet -->

Conformance: [ISO 32000-2 §7.7.3 (page tree)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Merging and splitting

`PdfDocument.Merge` makes one document from several, with their outlines, named destinations, form fields and page labels; names that collide are renamed. `Split` and `ExtractPages` make new documents from page ranges.

<!-- snippet: samples/guides/Editing/Program.cs#merge-split -->
```csharp
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
```
<!-- endsnippet -->

`AppendPages` adds the pages of another document (or a range of them) to an existing one.

## Drawing a page onto another page

`CreateFormXObject(page)` makes a page, of the same document or another one, into a form XObject that a canvas draws with `DrawForm`, scaled and placed: a letterhead under every page, a 2-up handout, a cover template. The form shows the page's crop box as a viewer shows it (rotated, `UserUnit` applied) with its lower-left corner at the origin, so one `Transform` places it. Making the same page into a form again returns the same form, and pages of one source that share fonts copy them once.

<!-- snippet: samples/guides/Editing/Program.cs#page-as-form -->
```csharp
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
```
<!-- endsnippet -->

`PdfPageFormOptions` chooses another box (`WithBoundary`), leaves the rotation out (`WithRotation(false)`), draws the page's printable annotations into the form (`WithAnnotations()`), or keeps its ties to the structure tree (`WithStructure()`). A page of the same document shares its resources with the form; the form's content is a snapshot of the page when it was made.

Conformance: [ISO 32000-2 §8.10.1 (form XObjects)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Stamps, watermarks and page numbers

`Stamp` draws text, an image, a form or another page on existing pages: a watermark, a logo, a letterhead. `AddHeaderFooter` adds up to three texts at the top and three at the bottom. Texts may hold `{page}`, `{pages}` and `{label}` (the page label, or the number when the document has none). Each stamp is marked as an artifact, so text extraction and screen readers skip it, and it is placed in the page's box as the page is viewed, so a rotated page gets an upright stamp.

<!-- snippet: samples/guides/Editing/Program.cs#stamps -->
```csharp
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
```
<!-- endsnippet -->

A stamp draws over the page's content by default. `WithLayer(PdfStampLayer.Background)` puts it under the content instead, but a page that paints an opaque white background hides it; use the foreground with an opacity below 1 for such pages. `WithFitToPage()` scales the stamp to the box, `WithOptionalContentGroup` lets a viewer hide it, and one image, form or font serves every page it is drawn on. In a PDF 2.0 document, a header or footer that shows the page number is marked with the `PageNum` artifact subtype.

Conformance: [ISO 32000-2 §14.8.2.2 (artifacts)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Forms

Fields are found by their fully qualified names. Setting a value regenerates the field's appearance, so the value shows in every reader; `WriteAppearances` does it again for a field you changed through its dictionary. `Flatten` draws every field's appearance into its page and removes the form, for a file that can no longer be filled in; `new PdfFlattenOptions().WithAnnotations(true)` flattens the other annotations too:

<!-- snippet: samples/guides/Editing/Program.cs#forms -->
```csharp
using PdfDocument document = PdfDocument.Open("application.pdf");
PdfAcroForm form = document.AcroForm!;
((PdfTextField)form.FindField("name")!).SetValue("Ada Lovelace"); // regenerates the field's appearance
((PdfCheckBoxField)form.FindField("agree")!).SetChecked(true);
document.Save("application-filled.pdf");

document.Flatten(); // draws every field's appearance into its page and removes the form
document.Save("application-flattened.pdf");
```
<!-- endsnippet -->

`CreateAcroForm` and its `Add*` methods add text fields, check boxes, radio buttons, combo and list boxes, push buttons and signature fields. Forms that carry XFA are read and preserved, not rendered.

Conformance: [ISO 32000-2 §12.7 (forms)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Annotations

Each page has `Add*Annotation` methods for the standard types: text notes, free text, lines, squares and circles, polygons, ink, text markup (highlight, underline, squiggly, strike-out), stamps and links. Each draws its normal appearance stream, so readers that do not generate appearances show it the same way:

<!-- snippet: samples/guides/Editing/Program.cs#annotations -->
```csharp
using PdfDocument document = PdfDocument.Open("contract.pdf");
PdfPage page = document.Pages[0];
var yellow = new PdfDeviceColor(1, 1, 0);

page.AddHighlightAnnotation(
    [new PdfQuadrilateral(new PathPoint(72, 718), new PathPoint(300, 718), new PathPoint(72, 734), new PathPoint(300, 734))],
    new PdfAnnotationOptions().WithColor(yellow).WithTitle("Reviewer").WithContents("Check this clause."));
page.AddFreeTextAnnotation(new PdfRectangle(72, 600, 300, 640), "Approved by legal", new PdfAnnotationOptions().WithFontSize(12));
page.AddLinkAnnotation(new PdfRectangle(72, 560, 200, 580), new Uri("https://example.com/terms"));
document.Save("contract-annotated.pdf");
```
<!-- endsnippet -->

`PdfPage.Annotations` lists a page's annotations, and `RemoveAnnotation` removes one.

Conformance: [ISO 32000-2 §12.5 (annotations)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Metadata

`Properties` holds the title, author, subject, keywords, creator, producer and dates. A save writes them to the document information dictionary and to the XMP metadata stream together, so the two agree:

<!-- snippet: samples/guides/Editing/Program.cs#metadata -->
```csharp
using PdfDocument document = PdfDocument.Open("contract.pdf");
document.Properties.Title = "Service contract";
document.Properties.Author = "Acme Legal";
document.Properties.Keywords = "contract, services";
document.Save("contract-metadata.pdf"); // writes the document information dictionary and the XMP metadata stream together

using PdfDocument saved = PdfDocument.Open("contract-metadata.pdf");
Console.WriteLine($"{saved.Properties.Title} by {saved.Properties.Author}");
Console.WriteLine($"XMP dc:title: {saved.Metadata?.Packet?.GetLanguageAlternative(XmpNamespaces.DublinCore, "title")}");
```
<!-- endsnippet -->

Conformance: [ISO 32000-2 §14.3 (metadata)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Attachments

`AddEmbeddedFile` attaches a file to the document, with a description, a MIME type and dates; `EmbeddedFiles` lists them and `RemoveEmbeddedFile` removes one. `AddAssociatedFile` relates a file to the document or a part of it (PDF 2.0, and PDF/A-3):

<!-- snippet: samples/guides/Editing/Program.cs#attachments -->
```csharp
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
```
<!-- endsnippet -->

Conformance: [ISO 32000-2 §7.11 (file specifications, embedded files), §14.13 (associated files)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Encryption

A save encrypts with a user password (needed to open the file; none here) and an owner password (needed to change the permissions). AES-256 is the default. `PdfSaveOptions.WithRemoveEncryption(true)` saves an encrypted document without encryption, and `WithPublicKeyEncryption` encrypts for certificates instead of passwords:

<!-- snippet: samples/guides/Editing/Program.cs#encryption -->
```csharp
using PdfDocument document = PdfDocument.Open("contract.pdf");
PdfEncryptionOptions encryption = new PdfEncryptionOptions(userPassword: null, ownerPassword: "owner-secret")
    .WithAlgorithm(PdfEncryptionAlgorithm.Aes256)          // the default; AES-128 and RC4 exist for old readers
    .WithPermissions(PdfPermissions.Print | PdfPermissions.PrintHighQuality);
document.Save("contract-encrypted.pdf", new PdfSaveOptions().WithEncryption(encryption));
```
<!-- endsnippet -->

Permissions are honored by readers, not enforced by the cryptography: anyone with the file and no user password can read it. Use a user password to keep a file private. Opening encrypted files is covered in [Reading](reading.md#passwords-and-certificates).

Conformance: [ISO 32000-2 §7.6 (encryption)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md), [ISO/TS 32003 (AES-GCM)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-ts-32003.md) and [ISO/TS 32004 (integrity protection)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-ts-32004.md).
