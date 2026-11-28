---
title: "Building documents"
---

# Building documents

The document builder in `Broadside.Layout` describes a document as nested content in plain C#: lambdas for structure, short modifier chains for detail. It lays the content out through the flow engine and returns an ordinary `PdfDocument`, tagged and with its fonts embedded. It is a front over flow layout, not a second layout engine: every element becomes a flow block, and a test proves the bytes match the same blocks built by hand.

The builder is stable 1.x API: it follows the same versioning promise as the rest of Broadside, and using it needs no opt-in.

Every sample on this page is compiled and run in CI from [`samples/Broadside.BuilderSamples`](https://github.com/michaelstonis/Broadside/tree/main/samples/Broadside.BuilderSamples), and its output is checked with qpdf and veraPDF.

## Hello

```csharp
using Broadside;
using Broadside.Layout.Flow;

using PdfDocument pdf = PdfDocument.Create(doc => doc
    .WithTitle("Hello")
    .WithLanguage("en-US")
    .Content(c =>
    {
        c.Heading(1, "Hello, Broadside");
        c.Paragraph("This page is tagged, embeds its font and opens in any reader.");
    }));

pdf.Save("hello.pdf");
```

With nothing configured:

- The page is A4 with 20 mm margins (`Theme.Default.Page`). It never comes from the machine's region; `Theme.Letter` gives US Letter.
- The body font is Helvetica's metric twin, Liberation Sans, embedded and subset on save.
- The output is tagged `Document` > `H1`, `P`, with the language and the title written and the title shown in the window.
- Your lambdas run once, top to bottom, on the calling thread. Layout runs after the outermost lambda returns.
- The same input and options give the same bytes. Fix the clock with `new PdfOptions().UseTimeProvider(...)` for byte-identical output.

## The shape of the API

- **Structure uses lambdas; detail uses chains.** A method that opens a scope (`Section`, `Content`, `Table`, `Row`, `List`, `Columns`, `Group`) takes a lambda. A method that adds an element returns its builder, so its modifiers chain: `c.Paragraph(text).Justify().SpaceAfter(6)`.
- **Each scope offers only what is valid there.** A table row has `Cell` and `HeaderCell`, not `Heading`.
- **Builders are `ref struct` handles.** A nested lambda cannot capture the builder of an outer scope, so content can never land in the wrong place, and a builder cannot be kept or used across `await`. Reusable pieces are methods (see [Components](#components-and-themes)).
- **Modifiers are bare** (`Bold()`, `FontSize(9)`, `AlignRight()`): they describe the element. Document settings that mirror an options type keep its names (`WithTitle`, `WithPdfUAConformance`).

## Units and values

- `Length` stores points, PDF's unit. A `double` converts to it as points.
- `Length.Millimeters(20)`, `Length.Centimeters(2)`, `Length.Inches(1)` make the others. `using Broadside.Layout.Flow.Units;` adds `20.Mm`, `2.5.Cm`, `1.In` and `12.Pt`.
- Relative widths are `ColumnWidth` values (`Proportional`, `Percent`, `Auto`), and a `Length` converts to `ColumnWidth.Fixed`. Images and drawings have `FillWidth()`.
- Colors are the core's `PdfColor`; `PdfColor.FromHex("#0B5FFF")` reads CSS-style hex.

## A report with a header, a footer and page numbers

```csharp
using Broadside.Layout.Flow.Units;

PdfDocument.Create(doc => doc
    .WithTitle($"Field report, {report.Quarter}")
    .WithAuthor(report.Team)
    .WithLanguage("en-GB")
    .WithTheme(Theme.Default.WithBaseSize(10.5))
    .Section(s => s
        .Size(PageSize.A4)
        .Margins(top: 25.Mm, right: 20.Mm, bottom: 25.Mm, left: 20.Mm)
        .FirstPageHeader(h => { })                      // an empty header draws nothing
        .Header(h => h.Left($"Field report, {report.Quarter}").Right(report.Team))
        .Footer(f => f.Center($"Page {PageField.Number} of {PageField.Count}"))
        .Content(c =>
        {
            c.Heading(1, "Field report");
            c.Paragraph(report.Summary).Justify();
            foreach (Region region in report.Regions)
            {
                c.Heading(2, region.Name).KeepWithNext();
                foreach (string note in region.Notes)
                {
                    c.Paragraph(note);
                }

                if (region.Incidents.Count == 0)
                {
                    c.Paragraph("No incidents were reported.").Italic().Color(Theme.Default.Muted);
                }
            }
        })));
```

`$"Page {PageField.Number} of {PageField.Count}"` is a typed interpolated string. `PageField.Number`, `PageField.Count` and `PageField.Label` (the page label) are filled in per page. Literal text is always literal: a string containing `{page}` prints `{page}`, so user data can never inject a field. Headers, footers and everything a section draws around the content are artifacts, skipped by assistive technology.

## Tables

```csharp
c.Table(t =>
{
    t.Columns(ColumnWidth.Proportional(3), ColumnWidth.Proportional(1), ColumnWidth.Proportional(1), 28.Mm);
    t.Header(h =>
    {
        h.Cell("Region");
        h.Cell("Q3").AlignRight();
        h.Cell("Q4").AlignRight();
        h.Cell("Change").AlignRight();
    });

    foreach (RegionResult r in results)
    {
        t.Row(row =>
        {
            row.HeaderCell(r.Name);                     // TH with Row scope
            row.Cell(r.Q3.ToString("N0", culture)).AlignRight();
            row.Cell(r.Q4.ToString("N0", culture)).AlignRight();
            row.Cell(r.Change.ToString("P1", culture)).AlignRight()
               .Color(r.Change < 0 ? theme.Negative : theme.Text);
        });
    }
})
.Caption("Table 1. Units shipped by region")
.Striped()
.Border(0.5, theme.Border);
```

- `Header` rows repeat on every page and are tagged `TH` with column scope; `HeaderCell` is a `TH` with row scope.
- `Cell(...).ColumnSpan(2).RowSpan(3)` span cells. `Caption` writes a `Caption` inside the `Table`.
- `t.Row("North", "120", "135")` is the fast path for plain rows: it allocates no closure, for tables of thousands of rows.
- `.Width(80.Mm).AlignRight()` makes a narrow table, such as an invoice's totals, sit at the right edge.

## Images and drawings

```csharp
ImageSource logo = ImageSource.FromFile("assets/logo.png");   // read once, embedded once per document

c.Image(logo, "Acme Instruments logo").Width(40.Mm);
c.DecorativeImage(logo).Width(20.Mm).AlignCenter();           // an artifact

c.Drawing(120.Mm, 50.Mm, "Bar chart of units shipped in Q4: North 135, South 104, East 151, West 112.", (canvas, box) =>
{
    double slot = box.Width / results.Count;
    double max = results.Max(r => r.Q4);
    canvas.SetFillColor(theme.Accent);
    for (int i = 0; i < results.Count; i++)
    {
        canvas.Rectangle((i * slot) + (slot * 0.2), 0, slot * 0.6, box.Height * results[i].Q4 / max);
    }

    canvas.Fill();
})
.Caption("Figure 1. Units shipped in Q4");
```

SVG stays vector. `Svg` imports the file once per document as a form XObject and places it as a figure, at its own size or scaled to a width or height you give; an SVG `ImageSource` passed to `Image` or `DecorativeImage` is drawn the same way (sources are recognized by their bytes: PNG, JPEG, or markup starting with `<svg` or `<?xml`):

```csharp
ImageSource chart = ImageSource.FromFile("assets/q4-chart.svg");

c.Svg(chart, "Bar chart of units shipped in Q4: North 135, South 104, East 151, West 112.", width: 120.Mm)
    .Caption("Figure 2. Units shipped in Q4");
```

What the importer skips or repairs is recorded on the document as `SvgDiagnostics` codes; see [Writing](writing.md#svg) for the supported subset.

Alternate text is a required argument; `DecorativeImage` and `DecorativeDrawing` are the explicit way out. A drawing's callback gets the page's canvas with (0, 0) at the box's bottom-left corner, inside the figure's marked content. Whatever it leaves unbalanced (an open `SaveState`, marked content, a text object) is balanced again and reported as a `DrawingStateUnbalanced` diagnostic.

## Barcodes

Barcodes and QR codes are vector drawings: each run of dark modules is one filled rectangle, so they stay sharp at any print resolution and add no image to the file. The encoders are managed code in `Broadside.Layout`; there is nothing else to install.

```csharp
c.Barcode(Barcode.QrCode("https://example.com/orders/2026-0042"), 30.Mm, 30.Mm);    // Alt: "QR code: https://example.com/orders/2026-0042"
c.Barcode(Barcode.Gs1128("(01)09501101530003(17)250101(10)ABC123"), 80.Mm, 22.Mm, "Shipping label barcode");
c.Barcode(Barcode.Ean13("590123412345"), 40.Mm, 25.Mm).AlignRight();               // the check digit is added
c.Barcode(Barcode.DataMatrix("PO 4500012345"), 15.Mm, 15.Mm, options: new BarcodeOptions().WithQuietZone(false));
```

- `Barcode.Code128`, `Gs1128`, `Code39`, `Ean13`, `UpcA`, `QrCode` and `DataMatrix` encode when called and throw `ArgumentException` naming the character or length that cannot be encoded, such as a lowercase letter in Code 39 or text too long for the largest QR code.
- Code 128 switches code sets to keep the symbol short. QR codes take the smallest version that holds the data, mix numeric, alphanumeric and byte segments, and try all eight masks; `QrErrorCorrection` sets the level (Medium by default). Text outside Latin-1 is written as UTF-8 after an ECI header.
- A barcode is a `Figure`. Its alternate text defaults to the kind and the data; pass your own when the data means nothing read aloud. Linear symbols print their digits under the bars in the current style's font.
- The box includes the quiet zone, the light margin scanners need, unless `WithQuietZone(false)` says the page already leaves it. A two-dimensional symbol keeps square modules and is centered in its box.

Outside the builder, draw on any canvas: a page, a template's decoration, a form XObject.

```csharp
using PdfCanvas canvas = page.CreateCanvas();
canvas.DrawBarcode(Barcode.Code128("INV-2026-0042"), new PdfRectangle(400, 720, 560, 770),
    new BarcodeOptions().WithFont(font, 8).WithBackground(PdfColor.FromGray(1)));
```

On a bare canvas the human-readable text needs a font from `WithFont`; without one, only the bars are drawn.

## Columns, components and themes

`Columns` places blocks side by side, without pretending to be a table: each column is tagged as one `Div`, read in column order.

```csharp
c.Columns(cols =>
{
    cols.Column(col =>
    {
        col.Image(theme.Logo!, $"{invoice.Seller.Name} logo").Width(35.Mm);
        col.AddressBlock(invoice.Seller);
    });
    cols.Column(col =>
    {
        col.Heading(1, "Invoice").AlignRight();
        col.Paragraph($"No. {invoice.Number}").AlignRight();
    });
});
```

<a id="components-and-themes"></a>**Components are extension methods.** There is no interface to implement and no lifecycle; a component can take any parameters, loop, branch and call other components:

```csharp
public static class InvoiceComponents
{
    private static readonly Style AddressStyle = new() { LineHeight = 1.25 };

    public static void AddressBlock(this ContainerBuilder c, Address address) =>
        c.Group(g =>
        {
            g.Paragraph(address.Name).Bold().SpaceAfter(0);
            foreach (string line in address.Lines)
            {
                g.Paragraph(line).SpaceAfter(0);
            }

            g.Paragraph(address.Country);
        })
        .Style(AddressStyle)
        .KeepTogether();
}
```

**Themes are immutable records**, safe to share across concurrent builds:

```csharp
public static readonly Theme Acme = Theme.Default with
{
    BaseSize = 10,
    Accent = PdfColor.FromHex("#0B5FFF"),
    Headings = Theme.Default.Headings with { H1 = new Style { FontSize = 22, SpaceAfter = 4.Mm } },
    Logo = ImageSource.FromFile("assets/acme.png"),
};
```

A `Theme` holds the fonts (`BodyFont`, `HeadingFont`, `MonospaceFont`), the base size and line height, a small palette (`Text`, `Muted`, `Accent`, `Border`, `Subtle`, `Negative`), element styles (`Body`, `Headings`, `Caption`, `Link`, `TableHeader`, `HeaderFooter`), the table style, the page setup and an optional logo. A `FontFamily` comes from font programs (`FontFamily.FromPrograms(regular, bold, italic, boldItalic)`), from the document's font resolver (`FontFamily.Resolve("Noto Sans")`) or from the Standard 14 metrics (`FontFamily.Standard(Standard14Family.Times)`), and `WithFallback` adds a family for characters the first cannot encode.

### How styles are inherited

A `Style` is a partial style: every property is optional, and an unset one inherits. The nearest setting wins, by lexical scope:

1. **Theme.** `Theme.Body` is the root of all text; `Theme.Headings`, `Theme.Caption`, `Theme.TableHeader` and `Theme.Link` apply to those elements over it.
2. **Scope.** A section, group, columns block, list, table, row or cell carries a style everything inside inherits.
3. **Element.** Modifiers (`.Bold()`, `.FontSize(9)`, `.Style(note)`) apply to the element and its spans.
4. **Span.** `t.Text("x").Italic()` applies to that span only.

Text properties (font, size, weight, color, language) inherit; box properties (space before and after) do not, so a group's `SpaceAfter` never adds space after each child. A language is written only where it differs from its parent's.

## An invoice with an attached file

```csharp
PdfDocument.Create(doc => doc
    .WithTitle($"Invoice {invoice.Number}")
    .WithLanguage("en-GB")
    .WithTheme(theme)
    .WithPdfAConformance(PdfAConformance.PdfA3u)
    .AttachFile("invoice.xml", invoice.ToXml(), PdfFileRelationship.Alternative)
    .Section(s => s
        .Footer(f => f.Left(invoice.Seller.Name).Right($"Page {PageField.Number} of {PageField.Count}"))
        .Content(c => { /* columns, the lines table, the totals table */ })));
```

`AttachFile` embeds the file, lists it among the document's attachments and associates it with the document, with its MIME type from the file name. Under PDF/A, files of any kind may be attached at PDF/A-3 and PDF/A-4f only. The full invoice is in the samples.

## Links, a table of contents and the outline

```csharp
PdfDocument.Create(doc => doc
    .WithTitle("Broadside user guide")
    .WithLanguage("en-US")
    .WithOutlineDepth(3)                              // the default is 3; 0 turns the outline off
    .Content(c =>
    {
        c.Heading(1, "Broadside user guide");
        c.TableOfContents().Title("Contents").Depth(2);
        c.PageBreak();

        c.Heading(2, "Installing").Id("install");
        c.List(l =>
        {
            l.Item("Add the Broadside package.");
            l.Item(i =>
            {
                i.Paragraph("Add the optional packages you need:");
                i.List(sub => sub.Item("Broadside.Rendering, for PNG output")).Bullet("–");
            });
        }).Numbered();

        c.Heading(2, "Reading files");
        c.Paragraph(t =>
        {
            t.Text("Install the packages first (see ");
            t.Link("Installing", to: "install");
            t.Text(" on page ");
            t.PageOf("install");
            t.Text("), or read the ");
            t.Link("online guide", new Uri("https://example.org/guide"));
            t.Text(".");
        });
    }));
```

- **Outline.** Headings build the document outline (bookmarks) down to `WithOutlineDepth`, 3 by default, each entry going to its heading.
- **Ids.** `.Id("install")` on a heading, paragraph, image, drawing or table names it: it becomes a named destination that `t.Link(text, to: id)` and `t.PageOf(id)` reach. A link to an id no element has is an `AnchorNotFound` diagnostic, and its text stays plain.
- **Table of contents.** `TableOfContents()` lists the headings down to `Depth` (3 by default), each linked to its heading, with a dot leader and the page number, tagged `TOC` and `TOCI`. `Title` adds a title above it in the level-2 heading look, tagged as a paragraph so it does not list itself.
- **Forward references.** Page numbers in a table of contents or from `PageOf` are known only after layout, so layout runs again until they are stable, at most three passes; if they still change, a `ForwardReferencesNotConverged` diagnostic says so and the last pass stands. Your lambdas never run twice.

## Forms

```csharp
PdfDocument.Create(doc => doc
    .WithTitle("Equipment loan request")
    .WithLanguage("en-GB")
    .WithPdfUAConformance(PdfUAConformance.PdfUA1)
    .Content(c =>
    {
        c.Heading(1, "Equipment loan request");
        c.TextField("name", "Full name").Width(90.Mm).Required();
        c.ComboBox("site", "Site", ["Leeds", "Manchester", "York"]).Value("Leeds");
        c.TextField("items", "Items requested").Multiline(lines: 4);
        c.RadioGroup("collection", "Collection", ["Pick up", "Deliver to my desk"]).Value("Pick up");
        c.CheckBox("terms", "I will return the equipment in working order").Required();
        c.SignatureField("signature", "Signature").Size(70.Mm, 20.Mm);
    }));
```

Fields are flow blocks: layout places each one and its label, and the AcroForm field is created where it lands. The label is a required argument: it is drawn above the field (beside it with `.LabelBeside()`, and always beside a check box or radio button) and written as the field's alternate name. Each field is tagged `Form` with its label and its widget, pages with fields get `/Tabs /S`, and the appearances come from the forms writer. A field name used twice throws.

## Accessible by default

- **Tagged.** The structure is `Document`, then a `Sect` for each section after the first, then each element's type: `H1` to `H6`, `P`, `L`/`LI`/`Lbl`/`LBody`, `Table`/`THead`/`TBody`/`TR`/`TH`/`TD`, `Figure` with `Alt` and `BBox`, `Caption`, `Link`, a `Div` per column. Headers, footers, backgrounds, watermarks, decorations, rules and decorative images and drawings are artifacts. `WithTagging(false)` writes untagged output.
- **Language and title.** `WithLanguage` writes the catalog's language; `.Language("fr-FR")` on an element or span writes it where it changes. `WithTitle` writes the title and shows it in the window. The builder never takes either from the machine.
- **Diagnostics, not surprises.** A missing language or title, a skipped heading level (`H1` then `H3`), alternate text that looks like a file name, a missing font face or a character no face encodes is a diagnostic on `PdfDocument.Diagnostics`, the one list that layout diagnostics land on too. In strict mode (`PdfDocument.Create(new PdfOptions().UseStrict(), doc => ...)`) the first one throws.

### Profiles

`WithPdfUAConformance(PdfUAConformance.PdfUA1)` and `WithPdfAConformance(PdfAConformance.PdfA2u)` (and the other levels) declare what the document is for:

| Precondition | No profile | With a profile |
|---|---|---|
| A language (`WithLanguage`) | diagnostic | error under PDF/UA |
| A title (`WithTitle`) | diagnostic | error under PDF/UA |
| Tagged output | allowed off | error under PDF/UA when off |
| No skipped heading level | diagnostic | error under PDF/UA-1 |
| Attached files | allowed | error under PDF/A-2 and PDF/A-4 (allowed at PDF/A-3 and PDF/A-4f) |
| PDF/UA and PDF/A on the same base version | n/a | error for PDF/UA-1 with PDF/A-4, or PDF/UA-2 with PDF/A-2 or PDF/A-3 |
| Appending to an untagged document | diagnostic | error under PDF/UA |

An unmet precondition under a profile throws `InvalidOperationException` at `Create`, naming every one. A profile also picks the structure namespace (PDF 1.7 for PDF/UA-1, PDF/A-2 and PDF/A-3; PDF 2.0 for PDF/UA-2 and PDF/A-4) and becomes the document's `DefaultSaveOptions`, so `pdf.Save(stream)` checks and identifies the file without asking twice. Options passed to `Save` explicitly win; a different conformance there is recorded as a `ConformanceOverridden` diagnostic. The save's own PDF/A and PDF/UA checks still run; the builder's are earlier and friendlier, not a replacement.

## Performance

- Builders are `ref struct` handles: adding an element allocates no builder.
- Styles resolve once per distinct look: 10,000 paragraphs in three looks resolve three styles.
- Fonts and images are embedded once per document per source.
- Closures in loops are the cost you control. Use the fast paths for bulk content: `c.Paragraph(string)`, and `t.Row(params ReadOnlySpan<string>)` for plain table rows.
- Layout is CPU-bound and synchronous; read inputs asynchronously before building, and save with `SaveAsync` at the I/O boundary.
- One build is single-threaded, but themes, styles, font families and image sources are immutable: a server builds many documents in parallel from one static theme.

`bench/Broadside.Benchmarks/DocumentBuilderBenchmarks.cs` measures a one-page document, a 100-page report, a 200-row table and 1,000 invoices (serially and in parallel), each against the same document built by hand with the flow engine.

## Escape hatches

Four levels, each one step lower:

```csharp
PdfDocument pdf = PdfDocument.Create(doc => doc
    .WithTitle("Escape hatches")
    .WithLanguage("en")
    .Section(s => s
        // 1. Per-page drawing after layout, as an artifact: crop marks, letterhead art, a page border.
        .Decorate((canvas, page) =>
        {
            canvas.SetStrokeColor(PdfColor.FromGray(0.8f));
            canvas.Rectangle(page.Margins.Left, page.Margins.Bottom, page.ContentWidth, page.ContentHeight);
            canvas.Stroke();
        })
        .Content(c =>
        {
            // 2. Any engine block, built by hand, with a font embedded in the document being built.
            PdfCanvasFont times = c.Document.EmbedFont(Standard14FontResolver.GetProgram(Standard14Font.TimesRoman));
            c.Add(new Paragraph("Built with the engine directly.", new ParagraphStyle(times, 11)));

            // 3. PdfCanvas inside the flow.
            c.Drawing(40.Mm, 40.Mm, "A red square.", (canvas, box) =>
            {
                canvas.SetFillColor(PdfColor.FromRgb(0.8f, 0.1f, 0.1f));
                canvas.Rectangle(0, 0, box.Width, box.Height);
                canvas.Fill();
            });
        })));

// 4. The result is a plain PdfDocument: every core API and the COS layer are available.
pdf.Pages[0].AddTextAnnotation(new PdfRectangle(500, 760, 520, 780), "Reviewed");
```

`Background(color)`, `Background(image)` and `Watermark(text)` on a section are shortcuts over the page template's own entries. `existing.AppendPages(doc => ...)` lays builder content out after the last page of an opened document, under its existing structure tree; save it with `new PdfSaveOptions().WithIncrementalUpdate(true)` to keep its bytes and signatures intact.

## Testing your templates with Verify

A template is a function, so a test calls it with fixed data and a fixed clock, and snapshots what matters. Raw PDF bytes make poor snapshots; the structure tree and the text read well in a diff, and a hash of the bytes guards against any change:

```csharp
[Fact]
public Task The_invoice_matches_its_snapshot()
{
    var options = new PdfOptions().UseTimeProvider(new FixedTimeProvider(new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero)));
    using PdfDocument pdf = Templates.CreateInvoice(options, TestData.Invoice, AcmeTheme.Instance);
    using var stream = new MemoryStream();
    pdf.Save(stream);
    using PdfDocument reopened = PdfDocument.Open(stream.ToArray());
    string text = string.Join("\n--- page ---\n", reopened.Pages.Select(page => page.ExtractText().PlainText));
    return Verify($"{StructureOf(reopened)}\n{text}\n{Convert.ToHexString(SHA256.HashData(stream.ToArray()))}");
}
```

`StructureOf` is a few lines that walk `reopened.StructureTree` and print each element's type, `Alt` and `Lang` indented by depth; `tests/Broadside.Layout.Tests/Builder/BuilderTestSupport.cs` has one to copy. The builder itself ships no Verify dependency.
