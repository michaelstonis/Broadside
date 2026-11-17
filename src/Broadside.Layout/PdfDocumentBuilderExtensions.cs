using System.Diagnostics.CodeAnalysis;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Layout.Flow;

/// <summary>
/// The entry points of the document builder on <see cref="PdfDocument"/>: <c>PdfDocument.Create(doc =&gt; ...)</c> writes a new document,
/// and <c>document.AppendPages(doc =&gt; ...)</c> adds pages to an opened one. Both are visible once <c>Broadside.Layout.Flow</c> is
/// imported.
/// </summary>
/// <remarks>
/// <para>
/// The lambdas run once, in order, on the calling thread; the content they describe is then resolved into flow blocks and laid out by
/// <see cref="FlowDocument"/>, so the builder writes exactly what the same blocks built by hand would. The same input and options (with a
/// fixed <see cref="PdfOptions.UseTimeProvider"/>) give the same bytes.
/// </para>
/// <para>
/// Programming errors (a null text, a heading level of 7, a column span past the last column) throw at the call. Content problems found
/// while building (a skipped heading level, a character no face encodes, a missing face) are diagnostics on the document.
/// </para>
/// <para>The documents are tagged PDF (ISO 32000-2 §14.8) by default.</para>
/// </remarks>
[Experimental(Experiments.DocumentBuilder, UrlFormat = Experiments.DocumentBuilderUrl)]
[SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "C# 14 extension blocks, which the analyzer sees as nested types.")]
[SuppressMessage("Naming", "CA1708:Identifiers should differ by more than case", Justification = "C# 14 extension blocks, which the analyzer sees as members named alike.")]
public static class PdfDocumentBuilderExtensions
{
    /// <summary>The entry points on the type.</summary>
    extension(PdfDocument)
    {
        /// <summary>Writes a new document described by <paramref name="build"/>.</summary>
        /// <param name="build">Describes the document.</param>
        /// <returns>The document, ready to save; dispose it when done.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
        /// <exception cref="ArgumentException">A described element cannot be laid out.</exception>
        /// <remarks>ISO 32000-2 §7.7.2 (document catalog) and §14.8 (tagged PDF): the output is tagged unless <see cref="DocumentBuilder.WithTagging"/> turns it off.</remarks>
        public static PdfDocument Create(Action<DocumentBuilder> build) => Create(new PdfOptions(), build);

        /// <summary>Writes a new document described by <paramref name="build"/>, with <paramref name="options"/> (strict mode, font resolvers, time provider).</summary>
        /// <param name="options">The document's options.</param>
        /// <param name="build">Describes the document.</param>
        /// <returns>The document, ready to save; dispose it when done.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">A described element cannot be laid out.</exception>
        public static PdfDocument Create(PdfOptions options, Action<DocumentBuilder> build)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(build);
            PdfDocument document = PdfDocument.Create(options);
            try
            {
                // A created document starts with one empty page; the builder's document holds only the pages it lays out.
                while (document.Pages.Count > 0)
                {
                    document.Pages.RemoveAt(0);
                }

                Build(document, appending: false, build);
                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }
    }

    /// <summary>The entry points on a document.</summary>
    /// <param name="document">The document to append to.</param>
    extension(PdfDocument document)
    {
        /// <summary>
        /// Lays out the content described by <paramref name="build"/> on new pages after the document's last page. In a tagged document the
        /// new structure goes under its existing <c>Document</c> element; in an untagged one the new content is tagged and the old is not,
        /// which a diagnostic reports (tag the old content first with auto-tagging). A title or language the document already has is kept,
        /// with a diagnostic. Save with <c>new PdfSaveOptions().WithIncrementalUpdate(true)</c> to keep existing bytes and signatures intact.
        /// </summary>
        /// <param name="build">Describes the content.</param>
        /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
        /// <exception cref="ArgumentException">A described element cannot be laid out.</exception>
        /// <remarks>ISO 32000-2 §7.5.6 (incremental updates) and §14.7.2 (structure hierarchy).</remarks>
        public void AppendPages(Action<DocumentBuilder> build)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(build);
            Build(document, appending: true, build);
        }
    }

    private static void Build(PdfDocument document, bool appending, Action<DocumentBuilder> build)
    {
        var context = new BuildContext(document, appending);
        try
        {
            build(new DocumentBuilder(context));
        }
        finally
        {
            context.Closed = true;
        }

        List<string> unmet = Preconditions(context);
        FlowDocument flow = new DocumentAssembler(context).Assemble();
        if (context.PdfUA == PdfUAConformance.PdfUA1 && context.Diagnostics.Find(diagnostic => diagnostic.Code == LayoutDiagnostics.HeadingLevelSkipped.Name) is { } skipped)
        {
            unmet.Add($"PDF/UA-1 does not allow a skipped heading level: {skipped.Message}");
        }

        if (unmet.Count > 0)
        {
            throw new InvalidOperationException(
                $"The document does not meet what its declared conformance requires:{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", unmet)}");
        }

        WriteDocumentEntries(context, flow);
        AttachFiles(context);

        // One list to look at: the builder's findings, then the layout's (Render records those), on the document; strict mode throws the first.
        foreach (Diagnostic diagnostic in context.Diagnostics)
        {
            document.ReportDiagnostic(diagnostic);
        }

        _ = flow.Render(document);
        if (context.PdfA != PdfAConformance.None || context.PdfUA != PdfUAConformance.None)
        {
            document.DefaultSaveOptions = new PdfSaveOptions().WithPdfAConformance(context.PdfA).WithPdfUAConformance(context.PdfUA);
        }
    }

    /// <summary>
    /// What a declared PDF/UA or PDF/A conformance requires that the description lacks, each as a sentence: checked before anything is
    /// laid out, so asking for a conformance never silently writes a file without it.
    /// </summary>
    private static List<string> Preconditions(BuildContext context)
    {
        var unmet = new List<string>();
        PdfDocument document = context.Document;
        PdfUAConformance ua = context.PdfUA;
        PdfAConformance a = context.PdfA;
        if (ua != PdfUAConformance.None)
        {
            string name = ua == PdfUAConformance.PdfUA1 ? "PDF/UA-1" : "PDF/UA-2";
            if (!context.Tagging)
            {
                unmet.Add($"{name} needs tagged output; remove WithTagging(false) (ISO 32000-2 §14.8).");
            }

            if (context.Language is null && (!context.Appending || document.Language is not { Length: > 0 }))
            {
                unmet.Add($"{name} needs the document's language; set it with WithLanguage (ISO 32000-2 §14.9.2).");
            }

            if (context.Title is null && (!context.Appending || document.Properties.Title is not { Length: > 0 }))
            {
                unmet.Add($"{name} needs a title; set it with WithTitle (ISO 32000-2 §14.3.3).");
            }

            if (context.Appending && document.StructureTree is null && document.Pages.Count > 0)
            {
                unmet.Add($"{name} cannot append to an untagged document, whose earlier pages would stay untagged; tag them first with AutoTag (ISO 32000-2 §14.8).");
            }

            bool part4 = a is PdfAConformance.PdfA4 or PdfAConformance.PdfA4f;
            if (a != PdfAConformance.None && (ua == PdfUAConformance.PdfUA1) == part4)
            {
                unmet.Add(ua == PdfUAConformance.PdfUA1
                    ? "PDF/UA-1 is written as PDF 1.7 and PDF/A-4 as PDF 2.0; combine PDF/UA-1 with PDF/A-2 or PDF/A-3, or PDF/A-4 with PDF/UA-2."
                    : "PDF/UA-2 is written as PDF 2.0 and PDF/A-2 and PDF/A-3 as PDF 1.7; combine PDF/UA-2 with PDF/A-4, or PDF/A-2 or PDF/A-3 with PDF/UA-1.");
            }
        }

        if (a != PdfAConformance.None && context.Attachments.Count > 0
            && a is not (PdfAConformance.PdfA3b or PdfAConformance.PdfA3u or PdfAConformance.PdfA4f))
        {
            unmet.Add($"PDF/A-{(a is PdfAConformance.PdfA4 ? "4" : "2")} does not allow attaching files of any kind; use PDF/A-3 or PDF/A-4f to attach {context.Attachments[0].FileName} (ISO 19005-3, 6.8; ISO 19005-4, 6.9).");
        }

        return unmet;
    }

    /// <summary>Embeds each attached file, lists it among the document's attachments and associates it with the catalog.</summary>
    private static void AttachFiles(BuildContext context)
    {
        PdfDocument document = context.Document;
        foreach (AttachmentNode attachment in context.Attachments)
        {
            PdfEmbeddedFileOptions options = new PdfEmbeddedFileOptions()
                .WithMediaType(attachment.MediaType)
                .WithRelationship(attachment.Relationship)
                .WithModificationDate(document.Properties.CreationDate);
            PdfFileSpecification file = document.CreateEmbeddedFile(attachment.FileName, attachment.Contents, options);
            _ = document.AddEmbeddedFile(attachment.FileName, file);
            document.AddAssociatedFile(document.Catalog, file);
        }
    }

    /// <summary>Writes the metadata and sets up tagging: the catalog's language, the title with <c>DisplayDocTitle</c>, the other Info entries.</summary>
    private static void WriteDocumentEntries(BuildContext context, FlowDocument flow)
    {
        PdfDocument document = context.Document;
        string? title = context.Title;
        string? language = context.Language;
        if (context.Appending)
        {
            if (title is not null && document.Properties.Title is { Length: > 0 } existingTitle)
            {
                context.Report(LayoutDiagnostics.DocumentMetadataKept, $"The document's title \"{existingTitle}\" is kept; the appended content's title is ignored.");
                title = null;
            }

            if (language is not null && document.Language is { Length: > 0 } existingLanguage)
            {
                context.Report(LayoutDiagnostics.DocumentMetadataKept, $"The document's language \"{existingLanguage}\" is kept; the appended content's language is ignored.");
                language = null;
            }
        }

        if (context.Author is { } author)
        {
            document.Properties.Author = author;
        }

        if (context.Subject is { } subject)
        {
            document.Properties.Subject = subject;
        }

        if (context.Keywords is { } keywords)
        {
            document.Properties.Keywords = keywords;
        }

        if (!context.Tagging)
        {
            if (title is not null)
            {
                document.Properties.Title = title;
            }

            if (language is not null)
            {
                document.Catalog[new CosName("Lang")] = FlowDocument.TextString(language);
            }

            return;
        }

        if (!context.Appending || document.Language is not { Length: > 0 })
        {
            if ((language ?? document.Language) is null)
            {
                context.Report(LayoutDiagnostics.DocumentLanguageMissing, "The tagged document has no language; set one with WithLanguage (ISO 32000-2 §14.9.2).");
            }
        }

        if ((title ?? document.Properties.Title) is null)
        {
            context.Report(LayoutDiagnostics.DocumentTitleMissing, "The tagged document has no title; set one with WithTitle (ISO 32000-2 §14.3.3).");
        }

        if (context.Appending)
        {
            if (document.StructureTree is { } tree)
            {
                flow.StructureParent = DocumentElementOf(tree);
            }
            else if (document.Pages.Count > 0)
            {
                context.Report(
                    LayoutDiagnostics.AppendedToUntaggedDocument,
                    "Tagged pages were appended to an untagged document, so its earlier pages stay untagged; tag them first with AutoTag (ISO 32000-2 §14.8).");
            }
        }

        FlowTagging tagging = FlowTagging.Default.WithLanguage(language).WithTitle(title);

        // The namespace the declared profile reads: PDF 1.7 for PDF/UA-1, PDF/A-2 and PDF/A-3; PDF 2.0 for PDF/UA-2 and PDF/A-4 (§14.8.6).
        if (context.PdfUA == PdfUAConformance.PdfUA1 || context.PdfA is PdfAConformance.PdfA2b or PdfAConformance.PdfA2u or PdfAConformance.PdfA3b or PdfAConformance.PdfA3u)
        {
            tagging = tagging.WithNamespace(FlowStructureNamespace.Pdf17);
        }
        else if (context.PdfUA == PdfUAConformance.PdfUA2 || context.PdfA is PdfAConformance.PdfA4 or PdfAConformance.PdfA4f)
        {
            tagging = tagging.WithNamespace(FlowStructureNamespace.Pdf20);
        }

        flow.Tagging = tagging;
    }

    /// <summary>The structure tree's root <c>Document</c> element, or null when its root holds anything else.</summary>
    private static PdfStructureElement? DocumentElementOf(PdfStructureTreeRoot tree) =>
        tree.Children is [{ StructureType.Value: "Document" } document] ? document : null;
}
