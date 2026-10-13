using Broadside.Annotations;
using Broadside.Security;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>
/// Every pdf.js file with a form or annotations flattened, fields only and then every annotation: the document saves, and the
/// saved file re-reads with no form, no widgets (no annotations at all for the second) and its pages interpretable. Skips when the
/// corpus is not fetched. ISO 32000-2 §12.5.5.
/// </summary>
[Trait("Category", "Corpus")]
public sealed class RealWorldFlattenTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_form_and_annotation_of_the_pdfjs_files_flattens_saves_and_re_reads()
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files("pdfjs");
        if (files.Count == 0)
        {
            Assert.Skip($"Corpus 'pdfjs' not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var options = new PdfOptions().UseSystemFontResolver(null);
        int forms = 0;
        int annotated = 0;
        int refused = 0;
        foreach (string file in files)
        {
            if (RealWorldCorpusTests.Unreadable.ContainsKey(file))
            {
                continue;
            }

            (bool form, bool annotations, bool xfa) = FlattenFile(file, options);
            forms += form ? 1 : 0;
            annotated += annotations ? 1 : 0;
            refused += xfa ? 1 : 0;
        }

        output.WriteLine($"{forms} forms, {annotated} annotated files flattened; {refused} XFA forms refused");
        Assert.True(forms >= 50, $"Only {forms} forms were flattened; the corpus looks incomplete.");
    }

    private static (bool Form, bool Annotations, bool Xfa) FlattenFile(string file, PdfOptions options)
    {
        bool form = false;
        bool annotations = false;
        foreach (bool everything in new[] { false, true })
        {
            using PdfDocument? document = OpenOrNull(file, options);
            if (document is null || document.IsEncrypted)
            {
                return default;
            }

            if (document.AcroForm?.Xfa is not null)
            {
                Assert.Throws<InvalidOperationException>(() => document.Flatten());
                return (false, false, true);
            }

            bool hadForm = document.AcroForm is not null;
            if (!document.Flatten(new PdfFlattenOptions().WithAnnotations(everything)))
            {
                continue;
            }

            form |= hadForm && !everything;
            annotations |= everything;
            using var stream = new MemoryStream();
            document.Save(stream);
            using PdfDocument reopened = PdfDocument.Open(stream.ToArray(), options);
            Assert.True(reopened.AcroForm is null, file);
            foreach (PdfPage page in reopened.Pages)
            {
                IReadOnlyList<PdfAnnotation> left = page.Annotations;
                Assert.True(everything ? left.Count == 0 : left.All(annotation => annotation.Kind != PdfAnnotationKind.Widget), $"{file} {everything}");
                page.ProcessContent(new Nothing());
            }
        }

        return (form, annotations, false);
    }

    private sealed class Nothing : Broadside.Content.ContentProcessor;

    private static PdfDocument? OpenOrNull(string file, PdfOptions options)
    {
        try
        {
            return PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)), options);
        }
        catch (Exception exception) when (exception is PdfPasswordException or PdfCertificateException or NotSupportedException)
        {
            return null;
        }
    }
}
