---
title: "Accessibility and conformance"
---

# Accessibility and conformance

This guide writes accessible PDF/UA files, tags existing files automatically, and writes and converts PDF/A files for long-term archiving. The code comes from the [`AccessibilityConformance`](https://github.com/michaelstonis/Broadside/tree/main/samples/guides/AccessibilityConformance) sample; CI builds and runs it on every change, checks its files with `qpdf --check`, and validates each against its profile with [veraPDF](https://verapdf.org), the PDF Association's reference validator.

A save that claims a profile checks the rules it can before it claims anything. A rule the file breaks is recorded as a `PdfANonConformant` or `PdfUANonConformant` diagnostic, naming the rule, and the file is then saved without the claim, never with a false one. In strict mode the save throws instead.

| Profile | Option | PDF version of the file |
|---|---|---|
| PDF/UA-1 | `WithPdfUAConformance(PdfUAConformance.PdfUA1)` | 1.5 to 1.7 (structure in the PDF 1.7 namespace) |
| PDF/UA-2, Well-Tagged PDF | `WithPdfUAConformance(PdfUAConformance.PdfUA2)`, `WithWellTaggedConformance(...)` | 2.0 |
| PDF/A-2b, 2u, 3b, 3u | `WithPdfAConformance(PdfAConformance.PdfA2b)` and the others | 1.4 to 1.7 |
| PDF/A-4, 4f | `WithPdfAConformance(PdfAConformance.PdfA4)` | 2.0 |

## Accessible output from flow layout

Flow layout tags what it lays out (headings, paragraphs, lists, tables, figures with their alternate text, links) and marks headers, footers and decorations as artifacts. Give the document a language and a title, embed its fonts, and save with a PDF/UA profile:

<!-- snippet: samples/guides/AccessibilityConformance/Program.cs#pdfua-flow -->
```csharp
using PdfDocument document = PdfDocument.Create();
document.Pages.RemoveAt(0);
PdfCanvasFont regular = document.EmbedFont(File.ReadAllBytes("LiberationSans-Regular.ttf"));
PdfCanvasFont bold = document.EmbedFont(File.ReadAllBytes("LiberationSans-Bold.ttf"));
var body = new ParagraphStyle(regular, 11);

// Flow layout tags what it lays out: headings, paragraphs, lists, tables and figures, with the page furniture as artifacts.
// PDF/UA-1 is a PDF 1.7 profile, so the structure uses the PDF 1.7 namespace; PDF/UA-2 uses the default, PDF 2.0.
FlowTagging tagging = FlowTagging.Default
    .WithLanguage("en-US")
    .WithTitle("Annual results")
    .WithNamespace(FlowStructureNamespace.Pdf17);
var flow = new FlowDocument(PageSetup.A4) { Tagging = tagging };
flow.Add(new Paragraph("Annual results", new ParagraphStyle(bold, 18)) { StructureType = "H1" });
flow.Add(new Paragraph("Revenue grew in every region.", body));
flow.Add(new ImageBlock(Chart(document), 120, 60) { AlternateText = "Revenue by region, rising in all four." });
flow.Render(document);

document.Save("annual-results-pdfua1.pdf", new PdfSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));
```
<!-- endsnippet -->

The [document builder](building-documents.md) does the same with less code, and checks the PDF/UA preconditions (language, title, heading levels) before it lays anything out.

### From Markdown

Release notes, READMEs and generated reports written in Markdown convert to PDF/UA-2 in one call. The Markdown maps onto the document builder, so its structure is the builder's: headings `H1` to `H6`, paragraphs with `Em`, `Strong`, `Code` and `Link` spans, lists, tables with header cells, figures with the image's alternate text, block quotes as `Div`.

<!-- snippet: samples/guides/AccessibilityConformance/Program.cs#pdfua-markdown -->
```csharp
// Markdown goes through the document builder, so headings, emphasis, lists, tables, links and images come out tagged.
const string markdown = """
    # Release notes

    Version 1.1 reads **Markdown**. Highlights:

    - Headings, lists and tables are tagged `H1`, `L` and `Table`.
    - Links such as [the guide](https://example.com/guide) keep their target.

    | Area   | Change          |
    |--------|-----------------|
    | Layout | Markdown to PDF |
    """;

// The first level-1 heading becomes the title; the language and the profile come from configure.
using PdfDocument document = PdfDocument.CreateFromMarkdown(markdown, configure: doc => doc
    .WithLanguage("en-US")
    .WithPdfUAConformance(PdfUAConformance.PdfUA2));
document.Save("release-notes-pdfua2.pdf"); // the declared profile is the document's default save options
```
<!-- endsnippet -->

- The first level-1 heading becomes the title unless `configure` sets one. PDF/UA still needs `WithLanguage`.
- An image without alternate text (`![](chart.png)`) is a `Figure` without `Alt`, reported as `FigureWithoutAlternateText`, which PDF/UA-2 does not allow: write the alternate text in the brackets.
- Raw HTML is dropped (`MarkdownHtmlIgnored`), and images are never fetched from the network: they come from `MarkdownOptions.WithImageResolver` or a file under `WithBaseDirectory`. See [Markdown](building-documents.md#markdown) in the builder guide.

Conformance: [ISO 14289-1 (PDF/UA-1)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-14289-1.md), [ISO 14289-2 (PDF/UA-2)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-14289-2.md), [Well-Tagged PDF 1.0](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/wtpdf-1.0.md) and [ISO 32000-2 §14.7, §14.8](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Tagging an existing file

`AutoTag` gives an untagged document a structure tree built from its [layout tree](extracting-text.md#the-layout-tree): columns, paragraphs, headings, lists, tables and figures, in reading order, with running headers and footers as artifacts. It does not invent what it cannot know: a figure gets placeholder alternate text (`AutoTaggingOptions.WithFigureAlternateText`) and is listed in `AutoTaggingResult.Figures`, so you can write real descriptions before saving. A document that is already tagged is left alone unless `WithForce()` is set.

<!-- snippet: samples/guides/AccessibilityConformance/Program.cs#auto-tag -->
```csharp
// An untagged file: the layout tree finds its columns, paragraphs, headings, lists, tables and figures, and tags them.
PdfOptions options = new PdfOptions().UseStandard14Fonts(); // metrics and programs for fonts the file does not embed
using PdfDocument document = PdfDocument.Open("layout-two-column-untagged.pdf", options);
AutoTaggingResult result = document.AutoTag(new AutoTaggingOptions().WithLanguage("en-US"));
document.Properties.Title = "Two columns";
Console.WriteLine($"Tagged: {result.IsTagged}, figures needing alternate text: {result.Figures.Count}");

// PDF/UA needs every font embedded, which the PDF/A conversion does; the file then claims both.
PdfAConversion conversion = document.ConvertToPdfA(PdfAConformance.PdfA2u);
document.Save("two-columns-pdfua1-pdfa2u.pdf", conversion.CreateSaveOptions().WithPdfUAConformance(PdfUAConformance.PdfUA1));
```
<!-- endsnippet -->

Here the file uses fonts it does not embed, which PDF/UA does not allow; the PDF/A conversion embeds them, so the file is saved as both PDF/UA-1 and PDF/A-2u.

## Writing PDF/A

A PDF/A save adds what the profile needs (an output intent and XMP metadata with the PDF/A identification) and checks the rest, such as embedded fonts, and identifies the file only when it conforms:

<!-- snippet: samples/guides/AccessibilityConformance/Program.cs#pdfa-save -->
```csharp
using PdfDocument document = PdfDocument.Create();
PdfCanvasFont sans = document.EmbedFont(File.ReadAllBytes("LiberationSans-Regular.ttf")); // PDF/A embeds every font
using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
{
    canvas.BeginText();
    canvas.SetFont(sans, 14);
    canvas.MoveText(72, 720);
    canvas.ShowText("Kept for the long term.");
    canvas.EndText();
}

document.Properties.Title = "Archive copy";
document.Save("archive-pdfa2b.pdf", new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA2b));

foreach (Diagnostic diagnostic in document.Diagnostics.Where(d => d.Code == "PdfANonConformant"))
{
    Console.WriteLine($"Not PDF/A: {diagnostic.Message}"); // the save checks the rules it can and says what fails
}
```
<!-- endsnippet -->

Files of any kind may be attached at PDF/A-3 and PDF/A-4f only; PDF/A-2 and PDF/A-4 allow attached PDF/A files.

### E-invoices (Factur-X and ZUGFeRD)

A Factur-X or ZUGFeRD 2.x invoice is a PDF/A-3 file with the CII XML invoice attached to the document and described in its XMP metadata. `AttachInvoice` embeds the XML (`text/xml`, with the relationship the profile calls for: `Data` for Minimum and Basic WL, `Alternative` for the others), lists it under `factur-x.xml` (`xrechnung.xml` for XRechnung), associates it with the document, and writes the `fx` properties with the extension schema PDF/A-3 needs. Save as PDF/A-3b or PDF/A-3u; any other save writes the invoice but records an `InvoiceNotPdfA3` warning. `ExtractInvoice` reads an invoice back:

<!-- snippet: samples/guides/AccessibilityConformance/Program.cs#einvoice -->
```csharp
using PdfDocument document = PdfDocument.Create();
PdfCanvasFont sans = document.EmbedFont(File.ReadAllBytes("LiberationSans-Regular.ttf"));
using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
{
    canvas.BeginText();
    canvas.SetFont(sans, 14);
    canvas.MoveText(72, 720);
    canvas.ShowText("Invoice INV-42"); // the human-readable invoice
    canvas.EndText();
}

// The CII XML your invoicing system produced: embedded, associated with the document and described in the XMP packet.
byte[] xml = File.ReadAllBytes("factur-x.xml");
document.AttachInvoice(xml, new PdfInvoiceOptions().WithProfile(PdfInvoiceProfile.EN16931));
document.Properties.Title = "Invoice INV-42";
document.Save("invoice-pdfa3b.pdf", new PdfSaveOptions().WithPdfAConformance(PdfAConformance.PdfA3b));

using PdfDocument received = PdfDocument.Open("invoice-pdfa3b.pdf");
PdfInvoiceAttachment? invoice = received.ExtractInvoice();
Console.WriteLine($"{invoice?.File.Name}: profile {invoice?.Profile}, {invoice?.GetXml().Length} bytes");
```
<!-- endsnippet -->

The XML is checked only to be well-formed, with a `rsm:CrossIndustryInvoice` root. Generating it and validating it against the CII schema or the EN 16931 rules is up to your invoicing system. Attaching again replaces the invoice. ZUGFeRD 1.0 and UBL invoices are not supported.

## Converting to PDF/A

`ConvertToPdfA` changes an existing document so it can conform: it embeds fonts the file only names (from the font resolvers you configure, here the bundled Standard 14 fonts), adds the output intent and metadata, and removes what the profile forbids. It reports each change, and what it could not convert, before anything is saved:

<!-- snippet: samples/guides/AccessibilityConformance/Program.cs#pdfa-convert -->
```csharp
using PdfDocument document = PdfDocument.Open("text-standard14.pdf", new PdfOptions().UseStandard14Fonts());
PdfAConversion conversion = document.ConvertToPdfA(PdfAConformance.PdfA2b);
foreach (string change in conversion.Changes)
{
    Console.WriteLine($"Changed: {change}");
}

foreach (string problem in conversion.NotConverted)
{
    Console.WriteLine($"Could not convert: {problem}");
}

if (conversion.IsConformant)
{
    document.Save("text-standard14-pdfa2b.pdf", conversion.CreateSaveOptions());
}
```
<!-- endsnippet -->

`CreateSaveOptions` returns the save options for the profile the conversion targeted.

Conformance: [ISO 19005-2 (PDF/A-2)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-19005-2.md), [ISO 19005-3 (PDF/A-3)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-19005-3.md) and [ISO 19005-4 (PDF/A-4)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-19005-4.md), by veraPDF rule.
