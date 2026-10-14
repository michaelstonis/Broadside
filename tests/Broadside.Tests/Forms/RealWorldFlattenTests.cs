using Broadside.Annotations;
using Broadside.Objects;
using Broadside.Security;
using Broadside.Structure;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Forms;

/// <summary>
/// Every pdf.js file with a form or annotations flattened, fields only and then every annotation: the document saves, and the
/// saved file re-reads with no form, no widgets (no annotations at all for the second) and its pages interpretable; a tagged file's
/// structure tree refers to no removed annotation and reads with no structure diagnostic it did not have before (issue #281). Skips
/// when the corpus is not fetched. ISO 32000-2 §12.5.5, §14.7.5.3, §14.7.5.4.
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
        int tagged = 0;
        foreach (string file in files)
        {
            if (RealWorldCorpusTests.Unreadable.ContainsKey(file))
            {
                continue;
            }

            (bool form, bool annotations, bool xfa, bool structure) = FlattenFile(file, options);
            tagged += structure ? 1 : 0;
            forms += form ? 1 : 0;
            annotated += annotations ? 1 : 0;
            refused += xfa ? 1 : 0;
        }

        output.WriteLine($"{forms} forms, {annotated} annotated files flattened ({tagged} tagged); {refused} XFA forms refused");
        Assert.True(forms >= 50, $"Only {forms} forms were flattened; the corpus looks incomplete.");
    }

    private static (bool Form, bool Annotations, bool Xfa, bool Tagged) FlattenFile(string file, PdfOptions options)
    {
        Pass fields = FlattenOnce(file, options, everything: false);
        if (fields.Skipped || fields.Xfa)
        {
            return (false, false, fields.Xfa, false);
        }

        Pass all = FlattenOnce(file, options, everything: true);
        return (fields.Flattened && fields.HadForm, all.Flattened, false, fields.Tagged || all.Tagged);
    }

    /// <summary>Flattens one opened copy of <paramref name="file"/>, saves it and checks what re-reads.</summary>
    private static Pass FlattenOnce(string file, PdfOptions options, bool everything)
    {
        using PdfDocument? document = OpenOrNull(file, options);
        if (document is null || document.IsEncrypted)
        {
            return new Pass(Skipped: true, Xfa: false, HadForm: false, Flattened: false, Tagged: false);
        }

        if (document.AcroForm?.Xfa is not null)
        {
            Assert.Throws<InvalidOperationException>(() => document.Flatten());
            return new Pass(Skipped: false, Xfa: true, HadForm: true, Flattened: false, Tagged: false);
        }

        bool hadForm = document.AcroForm is not null;
        if (!document.Flatten(new PdfFlattenOptions().WithAnnotations(everything)))
        {
            return new Pass(Skipped: false, Xfa: false, hadForm, Flattened: false, Tagged: false);
        }

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

        return new Pass(Skipped: false, Xfa: false, hadForm, Flattened: true, CheckStructure(file, options, reopened, everything));
    }

    private static readonly string[] StructureCodePrefixes = ["StructTree", "StructElem", "ParentTree", "Mcid", "IdTree"];

    /// <summary>
    /// Checks a flattened file's structure tree against the original's: no object reference to a removed annotation, no structure
    /// diagnostic the original did not read with. Returns whether there was a tree to check.
    /// </summary>
    private static bool CheckStructure(string file, PdfOptions options, PdfDocument reopened, bool everything)
    {
        if (reopened.StructureTree is null)
        {
            return false;
        }

        HashSet<string> before;
        HashSet<CosReference> removed;
        using (PdfDocument original = PdfDocument.Open(File.ReadAllBytes(RealWorldCorpus.PathOf(file)), options))
        {
            before = original.StructureTree is null ? [] : StructureCodes(original);
            removed = Listed(original);
        }

        // The save keeps object numbers, so an annotation the original's pages list and the flattened file's do not was removed.
        removed.ExceptWith(Listed(reopened));
        string what = $"{file} {everything}";
        foreach (PdfStructureElement element in reopened.StructureTree.Elements)
        {
            foreach (PdfObjectReference reference in element.Children.OfType<PdfObjectReference>())
            {
                Assert.False(reference.ObjectReference is { } target && removed.Contains(target), $"{what}: {element} refers to the removed annotation {reference.ObjectReference}");
            }
        }

        HashSet<string> after = StructureCodes(reopened);
        after.ExceptWith(before);
        Assert.True(after.Count == 0, $"{what}: {string.Join(", ", after)}");
        return true;
    }

    /// <summary>Reads the whole structure tree and every page's MCID lookups; returns the structure diagnostic codes the document then has.</summary>
    private static HashSet<string> StructureCodes(PdfDocument document)
    {
        PdfStructureTreeRoot tree = document.StructureTree!;
        foreach (PdfStructureElement element in tree.Elements)
        {
            _ = element.Children;
        }

        foreach (PdfPage page in document.Pages)
        {
            _ = tree.FindElement(page, 0);
        }

        return [.. document.Diagnostics.Select(diagnostic => diagnostic.Code).Where(code => StructureCodePrefixes.Any(prefix => code.StartsWith(prefix, StringComparison.Ordinal)))];
    }

    /// <summary>The references of the annotations the document's pages list.</summary>
    private static HashSet<CosReference> Listed(PdfDocument document) =>
        [.. document.Pages.SelectMany(page => page.Annotations).Select(annotation => annotation.Reference).OfType<CosReference>()];

    private sealed class Nothing : Broadside.Content.ContentProcessor;

    private readonly record struct Pass(bool Skipped, bool Xfa, bool HadForm, bool Flattened, bool Tagged);

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
