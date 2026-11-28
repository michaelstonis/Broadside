---
title: "Writing"
---

# Writing

This guide draws on pages with the canvas (paths, text, fonts, images, transparency) and lays out flowing content with the `Broadside.Layout` package (paragraphs, lists, tables, headers and footers). The code comes from the [`Writing`](https://github.com/michaelstonis/Broadside/tree/main/samples/guides/Writing) sample, which CI builds and runs on every change, and every file it writes is checked with `qpdf --check`.

For whole documents described as nested content (reports, invoices, forms), the [document builder](building-documents.md) sits on top of the same flow layout.

## Coordinates and versions

A page's coordinates are in points (1/72 inch), with the origin at the bottom-left corner of the page and y growing upward. `PdfDocument.Create()` starts with one empty US Letter page (612 x 792 points).

Every feature you write declares the PDF version it needs, and a save raises the file's version to the highest one used (recording a `VersionRaised` diagnostic), or refuses in strict mode. You never set the version by hand. The minimum versions of the features on this page:

| Feature | Minimum PDF version |
|---|---|
| Paths, text, the Standard 14 fonts | 1.0 |
| Images compressed with Flate | 1.2 |
| An embedded TrueType font (a Type 0 font with `ToUnicode`) | 1.2 |
| An embedded CFF or OpenType CFF font | 1.3 |
| Shadings (gradients) | 1.3 |
| Transparency: alpha, blend modes, soft masks, an image's alpha channel | 1.4 |
| Tagged content from flow layout | 1.4 (1.7 namespace) or 2.0 (2.0 namespace) |

## Paths

A canvas writes the page's content stream. Paths are built with `MoveTo`, `LineTo`, `CurveTo` and `Rectangle`, then painted with `Stroke`, `Fill` or both. `SaveState` and `RestoreState` bracket changes to the graphics state, such as a transformation:

<!-- snippet: samples/guides/Writing/Program.cs#paths -->
```csharp
using PdfDocument document = PdfDocument.Create(); // one empty US Letter page, 612 x 792 points, origin at the bottom left
using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
{
    canvas.SetStrokeColor(PdfColor.FromRgb(0.1f, 0.3f, 0.7f));
    canvas.SetLineWidth(4);
    canvas.SetLineJoin(LineJoin.Round);
    canvas.MoveTo(72, 600);
    canvas.LineTo(172, 700);
    canvas.LineTo(272, 600);
    canvas.CurveTo(300, 560, 340, 560, 372, 600);
    canvas.Stroke();

    canvas.SaveState();
    canvas.Translate(400, 600);
    canvas.Rotate(15);
    canvas.SetFillColor(PdfColor.FromHex("#E8A33D"));
    canvas.Rectangle(0, 0, 120, 80);
    canvas.Fill();
    canvas.RestoreState();
}

document.Save("paths.pdf");
```
<!-- endsnippet -->

Conformance: [ISO 32000-2 §8.4 (graphics state), §8.5 (path construction and painting)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Text

Text is drawn inside `BeginText` and `EndText`. The 14 standard fonts need no font file: every reader has them. `GetStandard14Font` returns one for the document:

<!-- snippet: samples/guides/Writing/Program.cs#text -->
```csharp
using PdfDocument document = PdfDocument.Create();
PdfCanvasFont helvetica = document.GetStandard14Font(Standard14Font.Helvetica);
PdfCanvasFont bold = document.GetStandard14Font(Standard14Font.HelveticaBold);
using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
{
    canvas.BeginText();
    canvas.SetFont(bold, 20);
    canvas.MoveText(72, 720);
    canvas.ShowText("Quarterly report");
    canvas.SetFont(helvetica, 11);
    canvas.SetTextLeading(14);
    canvas.MoveText(0, -28);
    canvas.ShowText("A line of text in a Standard 14 font, which every reader has.");
    canvas.ShowTextOnNextLine("The next line starts one leading below.");
    canvas.EndText();
}

document.Save("text.pdf");
```
<!-- endsnippet -->

Conformance: [ISO 32000-2 §9.4 (text objects), §9.6 (simple fonts)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Fonts and subsetting

The Standard 14 fonts cover Latin text only. For anything else, embed a font program: `EmbedFont` takes TrueType, OpenType (TrueType or CFF outlines) and Type 1 programs and returns a font for the canvas. Text in an embedded font is written as glyph ids with a `ToUnicode` map, so any character the font has can be shown and extracted again. A save keeps only the glyphs the document shows:

<!-- snippet: samples/guides/Writing/Program.cs#fonts -->
```csharp
using PdfDocument document = PdfDocument.Create();
byte[] program = File.ReadAllBytes("LiberationSans-Regular.ttf");
PdfCanvasFont sans = document.EmbedFont(program); // TrueType, OpenType or Type 1; a save keeps only the glyphs shown
using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
{
    canvas.BeginText();
    canvas.SetFont(sans, 14);
    canvas.MoveText(72, 720);
    canvas.ShowText("Embedded and subset: déjà vu, Ωμέγα, Ж", kerning: true);
    canvas.EndText();
}

document.Save("fonts.pdf");
```
<!-- endsnippet -->

A font whose license forbids embedding (its `OS/2` `fsType`) is refused with an `ArgumentException`. `EmbedFont(FontQuery)` embeds a font found by the document's font resolver instead of one you load.

Conformance: [ISO 32000-2 §9.7 (Type 0 fonts), §9.8 (font descriptors), §9.9 (embedded font programs), §9.10 (ToUnicode)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Searchable scans with your own OCR

Broadside does not run OCR, but it puts the output of the engine you use under a scan, so the page can be searched, selected and copied while it looks the same. Read hOCR (what Tesseract writes) or ALTO (ABBYY, Tesseract, library digitization tools) into `OcrPage`s, or build them from any engine's word boxes, and add one to each page:

<!-- snippet: samples/guides/Writing/Program.cs#ocr -->
```csharp
using PdfDocument document = PdfDocument.Open("scan.pdf"); // one scanned image per page, no text
using FileStream hocr = File.OpenRead("scan.hocr");        // tesseract scan.png scan hocr (or OcrPage.FromAlto for ALTO)
IReadOnlyList<OcrPage> pages = OcrPage.FromHocr(hocr);
for (int index = 0; index < pages.Count; index++)
{
    // Invisible text over each word's box: search, selection and copy work, and the page looks the same.
    document.Pages[index].AddTextLayer(pages[index], new TextLayerOptions().WithMinConfidence(0.5));
}

document.Save("searchable.pdf");
```
<!-- endsnippet -->

Each word becomes invisible text (rendering mode 3) sized and stretched to its box, in a built-in glyphless font that covers every script in a few hundred bytes. OCR coordinates (pixels, top-left origin) map onto the crop box as the page is displayed, so a rotated page works; `WithTarget` maps them onto another area. `WithTagging(true)` makes each line a `P` element with its language. Words the reader cannot use are skipped and listed in `OcrPage.Diagnostics`. A page with a layer saves as PDF/A-2b and 2u.

Conformance: [ISO 32000-2 §9.3.6 (text rendering mode), §14.8.2.4 (invisible content)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Images

`CreateImageFromPng` and `CreateImageFromJpeg` make an image from a file's bytes; a PNG's alpha channel becomes a soft mask, and JPEG data is kept as it is. `CreateImage` takes raw samples with their layout. `DrawImage` places an image in a rectangle:

<!-- snippet: samples/guides/Writing/Program.cs#images -->
```csharp
using PdfDocument document = PdfDocument.Create();
PdfImage logo = document.CreateImageFromPng(File.ReadAllBytes("logo.png")); // CreateImageFromJpeg keeps JPEG data as it is

// Raw samples: a 2 x 2 grey checkerboard, Flate-compressed.
PdfImage checker = document.CreateImage(
    [0, 255, 255, 0],
    new PdfImageLayout(2, 2, PdfDeviceGrayColorSpace.Instance),
    PdfImageCompression.Flate);

using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
{
    canvas.DrawImage(logo, 72, 620, 144, 144 * logo.Height / logo.Width); // x, y, width, height in points
    canvas.DrawImage(checker, 300, 620, 100, 100);
}

document.Save("images.pdf");
```
<!-- endsnippet -->

Conformance: [ISO 32000-2 §8.9 (images), §11.6.5 (soft masks)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Gradients and transparency

Shadings paint gradients; `CreateAxialShading` and `CreateRadialShading` take color stops. `SetFillAlpha`, `SetStrokeAlpha` and `SetBlendMode` set transparency for what follows, and `SetSoftMask` applies a mask made with `CreateSoftMask`:

<!-- snippet: samples/guides/Writing/Program.cs#transparency -->
```csharp
using PdfDocument document = PdfDocument.Create();
using (PdfCanvas canvas = document.Pages[0].CreateCanvas())
{
    PdfShading gradient = document.CreateAxialShading(
        new PathPoint(72, 0),
        new PathPoint(540, 0),
        [new PdfGradientStop(0, PdfColor.FromRgb(0.1f, 0.3f, 0.7f)), new PdfGradientStop(1, PdfColor.FromRgb(0.9f, 0.6f, 0.2f))]);
    canvas.SaveState();
    canvas.Rectangle(72, 560, 468, 160);
    canvas.Clip();
    canvas.EndPath();
    canvas.PaintShading(gradient);
    canvas.RestoreState();

    canvas.SetFillAlpha(0.5);               // 50 % opaque
    canvas.SetBlendMode(BlendMode.Multiply);
    canvas.SetFillColor(PdfColor.FromRgb(0.8f, 0.1f, 0.1f));
    canvas.Rectangle(150, 600, 120, 160);
    canvas.Fill();
}

document.Save("transparency.pdf");
```
<!-- endsnippet -->

Conformance: [ISO 32000-2 §8.7 (patterns and shadings), §11.6 (transparency)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md).

## Flow layout

The `Broadside.Layout` package lays out blocks (paragraphs, lists, tables, images, drawings) across as many pages as they need. A `PageTemplate` gives the page size, the margins, and the headers and footers, where `{page}` and `{pages}` are filled in on each page. Header rows of a table repeat on every page the table reaches. With `Tagging` set, the output is tagged: headings, paragraphs, lists and tables get their structure, and headers and footers are artifacts.

<!-- snippet: samples/guides/Writing/Program.cs#flow -->
```csharp
using PdfDocument document = PdfDocument.Create();
document.Pages.RemoveAt(0); // flow layout adds the pages it needs

PdfCanvasFont regular = document.EmbedFont(File.ReadAllBytes("LiberationSans-Regular.ttf"));
PdfCanvasFont bold = document.EmbedFont(File.ReadAllBytes("LiberationSans-Bold.ttf"));
var body = new ParagraphStyle(regular, 10.5).WithSpaceAfter(6);
var heading = new ParagraphStyle(bold, 16).WithSpaceAfter(8);
var small = new TextStyle(regular, 8);

var template = new PageTemplate(PageSetup.A4)
{
    Header = new HeaderFooter(small) { Left = "Field report", Right = "Acme Instruments" },
    Footer = new HeaderFooter(small) { Center = "Page {page} of {pages}" },
};
var flow = new FlowDocument(template) { Tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle("Field report") };

flow.Add(new Paragraph("Field report", heading) { StructureType = "H1" });
flow.Add(new Paragraph(
    "Paragraphs wrap to the page width and break across pages. Text runs can change the style mid-paragraph.",
    body.WithAlignment(TextAlignment.Justify)));

flow.Add(new ListBlock(new ListStyle(new TextStyle(regular, 10.5)).WithNumbering(ListNumbering.Decimal))
    .Add(new ListItem("Calibrate the sensors.", body))
    .Add(new ListItem("Record the readings.", body)));

var table = new Table(ColumnWidth.Proportional(2), ColumnWidth.Fixed(80), ColumnWidth.Fixed(80)) { HeaderRows = 1 }; // header rows repeat
table.AddRow(new TableCell("Region", body), new TableCell("Q3", body), new TableCell("Q4", body));
table.AddRow(new TableCell("North", body), new TableCell("120", body), new TableCell("135", body));
table.AddRow(new TableCell("South", body), new TableCell("98", body), new TableCell("104", body));
flow.Add(table);

FlowLayoutResult result = flow.Render(document);
document.Save("report.pdf");
Console.WriteLine($"Laid out {result.Pages.Count} page(s) with {result.Diagnostics.Count} diagnostic(s)");
```
<!-- endsnippet -->

Paragraphs also take a list of `TextRun`s for mixed styles and links, and `ParagraphStyle.WithDirection(TextDirection.RightToLeft)` sets the base direction of right-to-left text (`TextDirection.Auto` takes it from the first strong character); the Unicode bidirectional algorithm orders the runs of each line. `FlowLayoutResult.Diagnostics` lists anything layout could not do as asked, such as a character no font has.

Conformance: [ISO 32000-2 §14.7 (logical structure), §14.8 (tagged PDF)](https://github.com/michaelstonis/Broadside/blob/main/docs/conformance/iso-32000-2.md); for accessible output see [Accessibility and conformance](accessibility-and-conformance.md).
