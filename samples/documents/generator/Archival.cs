using System.Globalization;
using System.Text;
using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// Embedded files and archival conformance: a PDF portfolio (a collection with a schema, folders and a cover sheet) holding other
/// samples and data files; the conservative invoice converted to PDF/A-3b with its data as an XML associated file, as electronic
/// invoices carry it; and the scientific paper converted to PDF/A-2b by Broadside's converter.
/// </summary>
internal static class Archival
{
    private static readonly DateTimeOffset Filed = new(2027, 6, 1, 9, 0, 0, TimeSpan.Zero);

    public static void Write(GeneratorContext context)
    {
        Portfolio(context);
        InvoicePdfA3(context);
        PaperPdfA2(context);
    }

    private static void Portfolio(GeneratorContext context)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, "Elm Street water main: project file", "A PDF portfolio with a schema, folders and a cover sheet.");
        PdfCanvasFont regular = context.Font(document, "firasans/FiraSans-Regular.ttf");
        PdfCanvasFont bold = context.Font(document, "firasans/FiraSans-Bold.ttf");
        PdfPage cover = document.Pages.Add(new PdfRectangle(0, 0, 612, 792));
        using (PdfCanvas canvas = cover.CreateCanvas())
        {
            canvas.SetFillColor(Rgb(0.09, 0.31, 0.39));
            Text(canvas, bold, 24, 72, 700, "Elm Street water main");
            canvas.SetFillColor(Gray(0.15));
            Text(canvas, regular, 13, 72, 676, "Project file · a PDF portfolio");
            string[] lines =
            [
                "This file is a portfolio: the documents of the project are attached to it, sorted into",
                "folders, with a name, a description, a category and a due date for each.",
                "A viewer that shows portfolios lists them in place of this page; others show this",
                "cover sheet and list the files in their attachments panel.",
            ];
            double y = 630;
            foreach (string line in lines)
            {
                Text(canvas, regular, 11, 72, y, line);
                y -= 16;
            }
        }

        (string Key, int Folder, string Name, byte[] Bytes, string Type, string Description, string Category, DateTimeOffset Due)[] files =
        [
            ("invoice-colourful.pdf", 1, "invoice-colourful.pdf", File.ReadAllBytes(context.OutputPath("invoice-colourful.pdf")), "application/pdf", "Contractor's invoice for the survey", "Finance", new DateTimeOffset(2027, 6, 30, 0, 0, 0, TimeSpan.Zero)),
            ("budget.csv", 1, "budget.csv", Encoding.UTF8.GetBytes(Budget()), "text/csv", "Capital budget lines for the main", "Finance", new DateTimeOffset(2027, 7, 15, 0, 0, 0, TimeSpan.Zero)),
            ("notice-trilingual.pdf", 2, "notice-trilingual.pdf", File.ReadAllBytes(context.OutputPath("notice-trilingual.pdf")), "application/pdf", "Notice to residents in three languages", "Communications", new DateTimeOffset(2027, 4, 19, 0, 0, 0, TimeSpan.Zero)),
            ("press-release.txt", 2, "press-release.txt", Encoding.UTF8.GetBytes(PressRelease()), "text/plain", "Press release, final text", "Communications", new DateTimeOffset(2027, 4, 26, 0, 0, 0, TimeSpan.Zero)),
            ("paper-ghostscript.pdf", 3, "paper-ghostscript.pdf", File.ReadAllBytes(context.OutputPath("paper-ghostscript.pdf")), "application/pdf", "Background reading (a paper on cooling, for the canteen)", "Reference", new DateTimeOffset(2027, 12, 31, 0, 0, 0, TimeSpan.Zero)),
            ("readme.txt", 0, "readme.txt", Encoding.UTF8.GetBytes("Elm Street water main project file. Finance, communications and reference folders.\n"), "text/plain", "What this portfolio holds", "General", new DateTimeOffset(2027, 6, 1, 0, 0, 0, TimeSpan.Zero)),
        ];
        foreach ((string key, int folder, string name, byte[] bytes, string type, string description, string category, DateTimeOffset due) in files)
        {
            PdfFileSpecification spec = document.CreateEmbeddedFile(name, bytes, new PdfEmbeddedFileOptions()
                .WithMediaType(type).WithDescription(description).WithCreationDate(Filed).WithModificationDate(Filed));
            spec.Dictionary![new CosName("CI")] = new CosDictionary
            {
                [new CosName("Type")] = new CosName("CollectionItem"),
                [new CosName("Category")] = Utf16(category),
                [new CosName("Due")] = Ascii("D:" + due.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "000000Z"),
            };
            document.AddEmbeddedFile(folder == 0 ? key : $"<{folder}>{key}", spec);
        }

        CosReference root = document.AddObject(new CosDictionary());
        string[] folders = ["Finance", "Communications", "Reference"];
        var references = folders.Select(_ => document.AddObject(new CosDictionary())).ToArray();
        var rootFolder = (CosDictionary)document.Resolve(root);
        rootFolder[new CosName("Type")] = new CosName("Folder");
        rootFolder[new CosName("ID")] = new CosInteger(0);
        rootFolder[new CosName("Name")] = Utf16("Elm Street");
        rootFolder[new CosName("Child")] = references[0];
        for (int i = 0; i < folders.Length; i++)
        {
            var folder = (CosDictionary)document.Resolve(references[i]);
            folder[new CosName("Type")] = new CosName("Folder");
            folder[new CosName("ID")] = new CosInteger(i + 1);
            folder[new CosName("Name")] = Utf16(folders[i]);
            folder[new CosName("Parent")] = root;
            folder[new CosName("CreationDate")] = Ascii("D:20270601090000Z");
            if (i + 1 < folders.Length)
            {
                folder[new CosName("Next")] = references[i + 1];
            }
        }

        static CosDictionary Field(string subtype, string name, int order) => new()
        {
            [new CosName("Type")] = new CosName("CollectionField"),
            [new CosName("Subtype")] = new CosName(subtype),
            [new CosName("N")] = Utf16(name),
            [new CosName("O")] = new CosInteger(order),
        };
        document.Catalog[new CosName("Collection")] = new CosDictionary
        {
            [new CosName("Type")] = new CosName("Collection"),
            [new CosName("Schema")] = new CosDictionary
            {
                [new CosName("Type")] = new CosName("CollectionSchema"),
                [new CosName("FileName")] = Field("F", "Name", 0),
                [new CosName("Description")] = Field("Desc", "Description", 1),
                [new CosName("Category")] = Field("S", "Category", 2),
                [new CosName("Due")] = Field("D", "Due date", 3),
                [new CosName("Size")] = Field("Size", "Size", 4),
            },
            [new CosName("D")] = Utf16("readme.txt"),
            [new CosName("View")] = new CosName("D"),
            [new CosName("Sort")] = new CosDictionary
            {
                [new CosName("Type")] = new CosName("CollectionSort"),
                [new CosName("S")] = new CosArray([new CosName("Category"), new CosName("FileName")]),
                [new CosName("A")] = CosBoolean.True,
            },
            [new CosName("Folders")] = root,
        };
        document.Catalog[new CosName("PageMode")] = new CosName("UseAttachments");
        document.Catalog[new CosName("Extensions")] = new CosDictionary
        {
            [new CosName("ADBE")] = new CosDictionary { [new CosName("BaseVersion")] = new CosName("1.7"), [new CosName("ExtensionLevel")] = new CosInteger(3) },
        };
        document.Save(context.OutputPath("portfolio.pdf"));
    }

    private static void InvoicePdfA3(GeneratorContext context)
    {
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(context.OutputPath("invoice-conservative.pdf")));
        PdfFileSpecification xml = document.CreateEmbeddedFile("invoice.xml", Encoding.UTF8.GetBytes(InvoiceXml()), new PdfEmbeddedFileOptions()
            .WithMediaType("application/xml").WithDescription("The invoice as structured data").WithRelationship(PdfFileRelationship.Alternative)
            .WithCreationDate(Created).WithModificationDate(Created));
        document.AddEmbeddedFile("invoice.xml", xml);
        document.AddAssociatedFile(document.Catalog, xml);
        Convert(document, PdfAConformance.PdfA3b, context.OutputPath("invoice-pdfa3.pdf"));
    }

    private static void PaperPdfA2(GeneratorContext context)
    {
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(context.OutputPath("paper.pdf")));
        Convert(document, PdfAConformance.PdfA2b, context.OutputPath("paper-pdfa2b.pdf"));
    }

    private static void Convert(PdfDocument document, PdfAConformance conformance, string path)
    {
        PdfAConversion conversion = document.ConvertToPdfA(conformance);
        if (!conversion.IsConformant)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} is not {conformance}: " +
                string.Join("; ", conversion.Violations.Select(static v => v.ToString())) + " | not converted: " + string.Join("; ", conversion.NotConverted));
        }

        document.Save(path, conversion.CreateSaveOptions().WithUpdateModificationDate(false));
    }

    private static string InvoiceXml()
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        xml.Append("<!-- A sample invoice written for the Broadside sample documents; every party and number is invented. -->\n");
        xml.Append("<Invoice xmlns=\"urn:example:broadside:sample-invoice:1\">\n");
        xml.Append("  <Number>HF/27/0318</Number>\n  <IssueDate>2027-02-28</IssueDate>\n  <DueDate>2027-03-30</DueDate>\n  <Currency>GBP</Currency>\n");
        xml.Append("  <Seller><Name>Harrow &amp; Finch LLP</Name><Address>17 Cornmarket Yard, Ashcombe AC1 2BX</Address></Seller>\n");
        xml.Append("  <Buyer><Name>Linden Housing Association</Name><Address>3 Station Approach, Ashcombe AC4 9QT</Address><Reference>LHA/PO/55120</Reference></Buyer>\n");
        xml.Append("  <Lines>\n");
        xml.Append("    <Line><Description>Professional fees, 6 January to 27 February 2027</Description><Amount>12150.00</Amount></Line>\n");
        xml.Append("    <Line><Description>Disbursements at cost</Description><Amount>2206.80</Amount></Line>\n");
        xml.Append("  </Lines>\n  <TaxRate>20</TaxRate>\n</Invoice>\n");
        return xml.ToString();
    }

    private static string Budget() =>
        "line,description,2027,2028,total\n" +
        "1,Pipe and fittings,640000,150000,790000\n" +
        "2,Excavation and reinstatement,410000,110000,520000\n" +
        "3,Traffic management,90000,20000,110000\n" +
        "4,Connections to properties,100000,30000,130000\n";

    private static string PressRelease() =>
        "Riverside Water will replace the water main under Elm Street between 3rd and 6th Avenue from May 3 to May 21, 2027.\n" +
        "Residents will be told two days before their supply is interrupted. The work is part of the authority's capital programme.\n" +
        "(Riverside Water is invented for the Broadside sample documents.)\n";

    private static CosString Ascii(string text) => new(Encoding.ASCII.GetBytes(text));

    private static CosString Utf16(string text) => new([0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes(text)]);

    private static void Text(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, string text)
    {
        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.MoveText(x, y);
        canvas.ShowText(text);
        canvas.EndText();
    }
}
