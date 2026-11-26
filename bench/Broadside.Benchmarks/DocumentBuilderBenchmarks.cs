using System.Diagnostics.CodeAnalysis;
using BenchmarkDotNet.Attributes;
using Broadside.Fonts;
using Broadside.Fonts.Standard14;
using Broadside.Graphics;
using Broadside.Layout.Flow;

namespace Broadside.Benchmarks;

/// <summary>
/// The document builder against the same documents built by hand with the flow engine (issue #397, spec section 8 "Benchmarks"): the
/// builder is a front, so builder plus layout should stay within +5 % of the time and +10 % of the bytes of its <c>*Flow</c> twin. Each
/// case builds and lays out a whole document (saving is left out): <c>Hello</c> one page; <c>Report100</c> about 100 pages of headings
/// and paragraphs with a table of contents, an outline, a header and a page-numbered footer; <c>Table10k</c> a 10,000-row table over
/// about 300 pages through closures and through the <c>Row(params ReadOnlySpan&lt;string&gt;)</c> fast path; <c>Invoice</c> 1,000
/// invoices from one shared theme, serially and in parallel (the mean is per invoice).
/// </summary>
/// <remarks>
/// The report opens with a table of contents and builds its outline, so it exercises the forward references (layout runs until the
/// page numbers settle). The table has the spec's 10,000 rows: laying it out is linear in its rows (issue #541; it was quadratic,
/// through each tagged cell's header lookup and each MCID allocation walking the whole structure tree), and
/// <c>TableAllocationTests</c> in the layout tests fails the build if it turns quadratic again.
/// <para>
/// Every case except <c>Hello</c> and <c>HelloFlow</c> is in the <see cref="GateAllocations.NoAllocationGate"/> category (the
/// regression gate compares their time but not their allocated bytes exactly): they allocate 5 MB to 300 MB per call, so garbage
/// collections run inside the call, and each one can clear weakly held caches halfway through it; the figure then moves by bytes to
/// tens of kilobytes with where the collection falls, in the hand-built twins alike. <c>InvoiceParallel</c> also depends on how the
/// thread pool schedules its work. <c>bench/baselines/gate/README.md</c> has the rule.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "BenchmarkDotNet runs instance methods.")]
public class DocumentBuilderBenchmarks
{
    private const int ReportParagraphs = 1_200;
    private const int TableRows = 10_000;
    private const int Invoices = 1_000;

    private static readonly Theme SharedTheme = Theme.Default.WithBaseSize(10);
    private static readonly string[] Lines = ["Flow meter, 50 mm", "Calibration service", "Mounting kit", "Pressure sensor", "Site survey"];

    private string[] _paragraphs = [];
    private string[][] _rows = [];

    [GlobalSetup]
    public void Setup()
    {
        _paragraphs = [.. Enumerable.Range(0, ReportParagraphs).Select(index =>
            $"Paragraph {index}: the meters were read on schedule, the readings agree with the telemetry within tolerance, and the next visit is booked for the following quarter.")];
        _rows = [.. Enumerable.Range(0, TableRows).Select(index => new[] { $"Item {index}", $"{index % 97}", $"{index * 3 % 1000}.00" })];
    }

    /// <summary>One page with a heading and a paragraph, through the builder.</summary>
    [Benchmark]
    public int Hello()
    {
        using PdfDocument document = PdfDocument.Create(doc => doc
            .WithTitle("Hello")
            .WithLanguage("en-US")
            .Content(c =>
            {
                c.Heading(1, "Hello, Broadside");
                c.Paragraph("This page is tagged, embeds its font and opens in any reader.");
            }));
        return document.Pages.Count;
    }

    /// <summary>The same page built by hand.</summary>
    [Benchmark]
    public int HelloFlow()
    {
        using PdfDocument document = NewDocument();
        (PdfCanvasFont regular, PdfCanvasFont bold) = Fonts(document);
        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en-US").WithTitle("Hello") };
        flow.Add(new Paragraph("Hello, Broadside", new ParagraphStyle(bold, 22).WithSpaceBefore(12).WithSpaceAfter(6)) { StructureType = "H1" });
        flow.Add(new Paragraph("This page is tagged, embeds its font and opens in any reader.", new ParagraphStyle(regular, 11).WithSpaceAfter(6)));
        flow.Render(document);
        return document.Pages.Count;
    }

    /// <summary>About 100 pages of headings and paragraphs with a header and a "Page n of N" footer, through the builder.</summary>
    [Benchmark]
    [BenchmarkCategory(GateAllocations.NoAllocationGate)]
    public int Report100()
    {
        string[] paragraphs = _paragraphs;
        using PdfDocument document = PdfDocument.Create(doc => doc
            .WithTitle("Report")
            .WithLanguage("en-GB")
            .Section(s => s
                .Header(h => h.Left("Field report").Right("Operations"))
                .Footer(f => f.Center($"Page {PageField.Number} of {PageField.Count}"))
                .Content(c =>
                {
                    c.Heading(1, "Field report");
                    c.TableOfContents().Title("Contents").Depth(2);
                    c.PageBreak();
                    for (int index = 0; index < paragraphs.Length; index++)
                    {
                        if (index % 12 == 0)
                        {
                            c.Heading(2, $"Section {(index / 12) + 1}").KeepWithNext();
                        }

                        c.Paragraph(paragraphs[index]);
                    }
                })));
        return document.Pages.Count;
    }

    /// <summary>The same report built by hand.</summary>
    [Benchmark]
    [BenchmarkCategory(GateAllocations.NoAllocationGate)]
    public int Report100Flow()
    {
        using PdfDocument document = NewDocument();
        (PdfCanvasFont regular, PdfCanvasFont bold) = Fonts(document);
        var small = new TextStyle(regular, 8.5).WithColor(Theme.Default.Muted);
        var template = new PageTemplate(PageSetup.A4)
        {
            Header = new HeaderFooter(small) { Left = "Field report", Right = "Operations" },
            Footer = new HeaderFooter(small) { Center = "Page {page} of {pages}" },
        };
        var flow = new FlowDocument(template) { Tagging = FlowTagging.Default.WithLanguage("en-GB").WithTitle("Report"), OutlineDepth = 3 };
        var body = new ParagraphStyle(regular, 11).WithSpaceAfter(6);
        var h2 = new ParagraphStyle(bold, 11 * 1.6).WithSpaceBefore(12).WithSpaceAfter(6);
        flow.Add(new Paragraph("Field report", new ParagraphStyle(bold, 22).WithSpaceBefore(12).WithSpaceAfter(6)) { StructureType = "H1" });
        flow.Add(new BlockGroup(new Paragraph("Contents", h2)) { KeepWithNext = true });
        flow.Add(new TableOfContents(body) { Depth = 2 });
        flow.Add(new PageBreak());
        for (int index = 0; index < _paragraphs.Length; index++)
        {
            if (index % 12 == 0)
            {
                flow.Add(new Paragraph($"Section {(index / 12) + 1}", h2) { StructureType = "H2", KeepWithNext = true });
            }

            flow.Add(new Paragraph(_paragraphs[index], body));
        }

        flow.Render(document);
        return document.Pages.Count;
    }

    /// <summary>A 10,000-row table with a repeated header row, each row described through a closure.</summary>
    [Benchmark]
    [BenchmarkCategory(GateAllocations.NoAllocationGate)]
    public int Table10k()
    {
        string[][] rows = _rows;
        using PdfDocument document = PdfDocument.Create(doc => doc.WithTitle("Table").WithLanguage("en").Content(c => c.Table(t =>
        {
            t.Columns(ColumnWidth.Proportional(3), ColumnWidth.Proportional(1), ColumnWidth.Proportional(1));
            t.Header(h =>
            {
                h.Cell("Item");
                h.Cell("Qty");
                h.Cell("Amount");
            });
            foreach (string[] row in rows)
            {
                t.Row(r =>
                {
                    r.Cell(row[0]);
                    r.Cell(row[1]);
                    r.Cell(row[2]);
                });
            }
        })));
        return document.Pages.Count;
    }

    /// <summary>The same table through the <c>Row(params ReadOnlySpan&lt;string&gt;)</c> fast path, which allocates no closure per row.</summary>
    [Benchmark]
    [BenchmarkCategory(GateAllocations.NoAllocationGate)]
    public int Table10kFastPath()
    {
        string[][] rows = _rows;
        using PdfDocument document = PdfDocument.Create(doc => doc.WithTitle("Table").WithLanguage("en").Content(c => c.Table(t =>
        {
            t.Columns(ColumnWidth.Proportional(3), ColumnWidth.Proportional(1), ColumnWidth.Proportional(1));
            t.Header(h =>
            {
                h.Cell("Item");
                h.Cell("Qty");
                h.Cell("Amount");
            });
            foreach (string[] row in rows)
            {
                t.Row(row);
            }
        })));
        return document.Pages.Count;
    }

    /// <summary>The same table built by hand.</summary>
    [Benchmark]
    [BenchmarkCategory(GateAllocations.NoAllocationGate)]
    public int Table10kFlow()
    {
        using PdfDocument document = NewDocument();
        (PdfCanvasFont regular, PdfCanvasFont bold) = Fonts(document);
        var cell = new ParagraphStyle(regular, 11);
        var header = new ParagraphStyle(bold, 11);
        var table = new Table(ColumnWidth.Proportional(3), ColumnWidth.Proportional(1), ColumnWidth.Proportional(1)) { HeaderRows = 1, Style = Theme.Default.Table, SpaceAfter = 6 };
        table.AddRow(new TableCell("Item", header), new TableCell("Qty", header), new TableCell("Amount", header));
        foreach (string[] row in _rows)
        {
            table.AddRow(new TableCell(row[0], cell), new TableCell(row[1], cell), new TableCell(row[2], cell));
        }

        var flow = new FlowDocument(new PageTemplate(PageSetup.A4)) { Tagging = FlowTagging.Default.WithLanguage("en").WithTitle("Table") };
        flow.Add(table);
        flow.Render(document);
        return document.Pages.Count;
    }

    /// <summary>1,000 one-page invoices from one shared theme, one after another, through the builder; the mean is per invoice.</summary>
    [Benchmark(OperationsPerInvoke = Invoices)]
    [BenchmarkCategory(GateAllocations.NoAllocationGate)]
    public int InvoiceSerial()
    {
        int pages = 0;
        for (int index = 0; index < Invoices; index++)
        {
            pages += BuildInvoice(index);
        }

        return pages;
    }

    /// <summary>The same 1,000 invoices built on every core at once from the same theme; the mean is per invoice.</summary>
    [Benchmark(OperationsPerInvoke = Invoices)]
    [BenchmarkCategory(GateAllocations.NoAllocationGate)]
    public int InvoiceParallel()
    {
        int pages = 0;
        Parallel.For(0, Invoices, index => Interlocked.Add(ref pages, BuildInvoice(index)));
        return pages;
    }

    /// <summary>The same 1,000 invoices built by hand, one after another; the mean is per invoice.</summary>
    [Benchmark(OperationsPerInvoke = Invoices)]
    [BenchmarkCategory(GateAllocations.NoAllocationGate)]
    public int InvoiceSerialFlow()
    {
        int pages = 0;
        for (int index = 0; index < Invoices; index++)
        {
            pages += BuildInvoiceFlow(index);
        }

        return pages;
    }

    private static int BuildInvoice(int number)
    {
        using PdfDocument document = PdfDocument.Create(doc => doc
            .WithTitle($"Invoice {number}")
            .WithLanguage("en-GB")
            .WithTheme(SharedTheme)
            .Section(s => s
                .Footer(f => f.Left("Acme Instruments Ltd").Right($"Page {PageField.Number} of {PageField.Count}"))
                .Content(c =>
                {
                    c.Heading(1, "Invoice");
                    c.Paragraph($"No. {number}").AlignRight();
                    c.Table(t =>
                    {
                        t.Columns(ColumnWidth.Proportional(1), ColumnWidth.Fixed(60), ColumnWidth.Fixed(80));
                        t.Header(h =>
                        {
                            h.Cell("Description");
                            h.Cell("Qty").AlignRight();
                            h.Cell("Amount").AlignRight();
                        });
                        for (int line = 0; line < Lines.Length; line++)
                        {
                            t.Row(r =>
                            {
                                r.Cell(Lines[line]);
                                r.Cell("2").AlignRight();
                                r.Cell("120.00").AlignRight();
                            });
                        }
                    }).Striped();
                    c.Paragraph("Total due 600.00").AlignRight().Bold();
                })));
        return document.Pages.Count;
    }

    private static int BuildInvoiceFlow(int number)
    {
        using PdfDocument document = NewDocument();
        (PdfCanvasFont regular, PdfCanvasFont bold) = Fonts(document);
        var small = new TextStyle(regular, 8.5).WithColor(Theme.Default.Muted);
        var template = new PageTemplate(PageSetup.A4) { Footer = new HeaderFooter(small) { Left = "Acme Instruments Ltd", Right = "Page {page} of {pages}" } };
        var flow = new FlowDocument(template) { Tagging = FlowTagging.Default.WithLanguage("en-GB").WithTitle($"Invoice {number}") };
        var body = new ParagraphStyle(regular, 10).WithSpaceAfter(6);
        var cell = new ParagraphStyle(regular, 10);
        var right = cell.WithAlignment(TextAlignment.Right);
        var header = new ParagraphStyle(bold, 10);
        flow.Add(new Paragraph("Invoice", new ParagraphStyle(bold, 20).WithSpaceBefore(12).WithSpaceAfter(6)) { StructureType = "H1" });
        flow.Add(new Paragraph($"No. {number}", body.WithAlignment(TextAlignment.Right)));
        var table = new Table(ColumnWidth.Proportional(1), ColumnWidth.Fixed(60), ColumnWidth.Fixed(80)) { HeaderRows = 1, Style = Theme.Default.Table, SpaceAfter = 6 };
        table.AddRow(new TableCell("Description", header), new TableCell("Qty", header.WithAlignment(TextAlignment.Right)), new TableCell("Amount", header.WithAlignment(TextAlignment.Right)));
        for (int line = 0; line < Lines.Length; line++)
        {
            table.Add(new TableRow(new TableCell(Lines[line], cell), new TableCell("2", right), new TableCell("120.00", right)) { Background = line % 2 == 1 ? Theme.Default.Subtle : null });
        }

        flow.Add(table);
        flow.Add(new Paragraph("Total due 600.00", new ParagraphStyle(bold, 10).WithSpaceAfter(6).WithAlignment(TextAlignment.Right)));
        flow.Render(document);
        return document.Pages.Count;
    }

    private static PdfDocument NewDocument()
    {
        PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        return document;
    }

    private static (PdfCanvasFont Regular, PdfCanvasFont Bold) Fonts(PdfDocument document) =>
        (document.EmbedFont(Standard14FontResolver.GetProgram(Standard14Font.Helvetica)),
         document.EmbedFont(Standard14FontResolver.GetProgram(Standard14Font.HelveticaBold)));
}
