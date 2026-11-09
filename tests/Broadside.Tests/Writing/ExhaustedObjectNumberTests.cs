using System.Globalization;
using Broadside.Annotations;
using Broadside.Diagnostics;
using Broadside.Forms;
using Broadside.Objects;

namespace Broadside.Tests.Writing;

/// <summary>
/// Adding objects to a file that holds object 2147483647 (issue #387): no number is left above it, so a new object takes the lowest
/// number the file neither lists nor refers to (ISO 32000-2 §7.3.10) and the document records <c>ObjectNumberOutOfRange</c>; strict
/// mode refuses instead. Everything built on adding objects (annotations, form fields, page copies) keeps working, and a full save
/// renumbers and stays small (issue #371).
/// </summary>
public sealed class ExhaustedObjectNumberTests
{
    private const string OutOfRange = "ObjectNumberOutOfRange";

    public static TheoryData<string> Edits => ["annotation", "form field", "merged page"];

    [Fact]
    public void A_new_object_takes_the_lowest_number_neither_listed_nor_referenced_and_records_a_warning()
    {
        // Objects 1 to 3 are listed, 4 is a dangling reference (it must stay null, §7.3.10), so the new objects are 5 and 6.
        using PdfDocument document = PdfDocument.Open(FileHoldingTheLargestNumber("/Dangling 4 0 R"));

        CosReference first = document.AddObject(new CosInteger(7));
        CosReference second = document.AddObject(new CosInteger(8));

        Assert.Equal((5, 6), (first.ObjectNumber, second.ObjectNumber));
        Assert.Equal(7L, ((CosInteger)document.Resolve(first)).Value);
        Assert.IsType<CosNull>(document.Resolve(new CosReference(4, 0)));
        Diagnostic diagnostic = Assert.Single(document.Diagnostics, item => item.Code == OutOfRange);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Fact]
    public void In_strict_mode_adding_an_object_throws_and_adds_nothing()
    {
        using PdfDocument document = PdfDocument.Open(FileHoldingTheLargestNumber(string.Empty), new PdfOptions().UseStrict());

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => document.AddObject(new CosInteger(7)));

        Assert.Equal(OutOfRange, exception.Diagnostic.Code);
        Assert.IsType<CosNull>(document.Resolve(new CosReference(4, 0)));
    }

    [Theory]
    [MemberData(nameof(Edits))]
    public void An_edit_that_adds_objects_works_and_a_full_save_stays_small(string edit)
    {
        using PdfDocument document = PdfDocument.Open(FileHoldingTheLargestNumber(string.Empty));
        PdfPage page = document.Pages[0];
        switch (edit)
        {
            case "annotation":
                page.AddTextAnnotation(new PdfRectangle(72, 700, 92, 720), "A comment");
                break;
            case "form field":
                document.CreateAcroForm().AddTextField("name", page, new PdfRectangle(72, 700, 272, 724));
                break;
            default:
                using (PdfDocument source = PdfDocument.Open(Document.TestPdf.OnePage(string.Empty)))
                {
                    document.AppendPages(source);
                }

                break;
        }

        using var output = new MemoryStream();
        document.Save(output, new PdfSaveOptions().WithUpdateModificationDate(false));

        Assert.True(output.Length < 8192, $"{output.Length} bytes.");
        Assert.Contains(document.Diagnostics, item => item.Code == OutOfRange);
        using PdfDocument reopened = PdfDocument.Open(output.ToArray());
        Assert.Empty(reopened.Diagnostics);
        Assert.True(((CosInteger)reopened.Trailer[new CosName("Size")]).Value < 32);
        Assert.True(CosObject.DeepEquals(new CosDictionary { [new CosName("Value")] = new CosInteger(1) }, reopened.Resolve(reopened.Catalog[new CosName("Extra")])));
        switch (edit)
        {
            case "annotation":
                Assert.IsType<PdfTextAnnotation>(Assert.Single(reopened.Pages[0].Annotations));
                break;
            case "form field":
                Assert.Equal("name", Assert.Single(reopened.AcroForm!.Fields).FullyQualifiedName);
                break;
            default:
                Assert.Equal(2, reopened.Pages.Count);
                break;
        }
    }

    /// <summary>A one-page file and an update adding object 2147483647, which the catalog names as <c>Extra</c>.</summary>
    private static byte[] FileHoldingTheLargestNumber(string catalogEntries) =>
        Document.TestPdf.AppendUpdate(
            Document.TestPdf.OnePage(string.Empty, $"/Extra {int.MaxValue} 0 R {catalogEntries}"),
            string.Create(CultureInfo.InvariantCulture, $"/Size {(long)int.MaxValue + 1} /Root 1 0 R"),
            (int.MaxValue, 0, "<< /Value 1 >>"));
}
