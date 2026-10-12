using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Writing;

/// <summary>Copying object graphs from one document into another with their references remapped. ISO 32000-2 §7.3.10.</summary>
public sealed class ObjectCopyTests
{
    private static readonly CosName Type = new("Type");
    private static readonly CosName Subtype = new("Subtype");
    private static readonly CosName Kids = new("Kids");
    private static readonly CosName Count = new("Count");
    private static readonly CosName Parent = new("Parent");
    private static readonly CosName Pages = new("Pages");
    private static readonly CosName Resources = new("Resources");
    private static readonly CosName XObject = new("XObject");

    [Fact]
    public void Copying_two_pages_that_share_form_xobjects_copies_each_form_once()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("form-xobject-nested.pdf"));
        AddPageSharingResources(source);
        Assert.Equal(2, source.Pages.Count);
        using PdfDocument target = PdfDocument.Create();

        var copier = new PdfObjectCopier(source, target);
        CosReference first = copier.CopyPage(source.Pages[0]);
        CosReference second = copier.CopyPage(source.Pages[1]);
        AppendPage(target, first);
        AppendPage(target, second);

        Assert.NotEqual(first, second);
        CosObject[] forms = [.. CosGraph.Reachable(target).Select(target.Resolve).Where(value => IsForm(value))];
        Assert.Equal(3, forms.Length);
        Assert.Equal(XObjects(target, target.Pages[1]), XObjects(target, target.Pages[2]));
        foreach (int page in new[] { 1, 2 })
        {
            Assert.Equal("B", target.Pages[page].ExtractText().Text);
        }

        using PdfDocument saved = PdfDocument.Open(Save(target));
        Assert.Equal(3, saved.Pages.Count);
        Assert.Equal(3, CosGraph.Reachable(saved).Select(saved.Resolve).Count(value => IsForm(value)));
        Assert.Equal("B", saved.Pages[2].ExtractText().Text);
        Assert.Equal(0, ExternalTool.Run("qpdf", Save(target), "--check").ExitCode);
    }

    public static TheoryData<string> UnencryptedFiles => [.. Corpus.WellFormedFileNames.Where(name => !name.StartsWith("encrypted-", StringComparison.Ordinal))];

    public static TheoryData<string> EncryptedFiles => [.. Corpus.WellFormedFileNames.Where(name => name.StartsWith("encrypted-", StringComparison.Ordinal))];

    [Theory]
    [MemberData(nameof(UnencryptedFiles))]
    public void Every_page_copies_unchanged_except_for_its_place_in_the_page_tree(string fileName)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));
        using PdfDocument target = PdfDocument.Create();
        var copier = new PdfObjectCopier(source, target);

        CosReference[] copies = [.. source.Pages.Select(copier.CopyPage)];

        CosGraph.PageCopyComparer? session = null;
        for (int index = 0; index < copies.Length; index++)
        {
            session = CosGraph.AssertPageCopy(source, source.Pages[index].Reference!, target, copies[index], session);
        }

        Assert.Equal(copies, source.Pages.Select(page => session!.Bijection[page.Reference!]));
    }

    [Fact]
    public void Copied_pages_of_a_tree_with_inherited_attributes_keep_them_as_their_own_and_render_the_same_text()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        using PdfDocument target = PdfDocument.Create();
        var copier = new PdfObjectCopier(source, target);

        foreach (PdfPage page in source.Pages)
        {
            CosReference copy = copier.CopyPage(page);
            var dictionary = (CosDictionary)target.Resolve(copy);
            Assert.False(dictionary.ContainsKey(Parent));
            Assert.True(dictionary.ContainsKey(Resources));
            Assert.True(dictionary.ContainsKey(new CosName("MediaBox")));
            AppendPage(target, copy);
        }

        using PdfDocument saved = PdfDocument.Open(Save(target));
        Assert.Equal(3, saved.Pages.Count);
        for (int index = 0; index < source.Pages.Count; index++)
        {
            PdfPage original = source.Pages[index];
            PdfPage copied = saved.Pages[index + 1];
            Assert.Equal(original.MediaBox, copied.MediaBox);
            Assert.Equal(original.CropBox, copied.CropBox);
            Assert.Equal(original.Rotation, copied.Rotation);
            Assert.Equal(original.ExtractText().Text, copied.ExtractText().Text);
        }

        Assert.Equal(["Page 1", "Page 2"], saved.Pages.Skip(1).Select(page => page.ExtractText().Text.Trim()));
    }

    [Fact]
    public void A_reference_to_another_page_names_its_copy_once_the_session_copies_that_page()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("page-tree-inherited.pdf"));
        CosReference secondPage = source.Pages[1].Reference!;
        var link = new CosDictionary
        {
            [Type] = new CosName("Annot"),
            [Subtype] = new CosName("Link"),
            [new CosName("Rect")] = new CosArray([new CosInteger(0), new CosInteger(0), new CosInteger(10), new CosInteger(10)]),
            [new CosName("Dest")] = new CosArray([secondPage, new CosName("Fit")]),
            [new CosName("P")] = source.Pages[0].Reference!,
        };
        source.Pages[0].Dictionary[new CosName("Annots")] = new CosArray([source.AddObject(link)]);
        using PdfDocument target = PdfDocument.Create();
        var copier = new PdfObjectCopier(source, target);

        CosReference firstCopy = copier.CopyPage(source.Pages[0]);
        var copiedLink = (CosDictionary)target.Resolve(((CosArray)target.Resolve(((CosDictionary)target.Resolve(firstCopy))[new CosName("Annots")]))[0]);
        var destination = (CosArray)target.Resolve(copiedLink[new CosName("Dest")]);
        Assert.Equal(firstCopy, copiedLink[new CosName("P")]);
        Assert.Same(CosNull.Instance, destination[0]);

        CosReference secondCopy = copier.CopyPage(source.Pages[1]);

        Assert.Equal(secondCopy, destination[0]);
        Assert.Equal(new CosName("Fit"), destination[1]);
    }

    [Fact]
    public void A_cycle_of_two_hundred_thousand_objects_copies_without_recursion()
    {
        const int length = 200_000;
        using PdfDocument source = PdfDocument.Create();
        var next = new CosName("Next");
        var nodes = new CosDictionary[length];
        var references = new CosReference[length];
        for (int index = 0; index < length; index++)
        {
            nodes[index] = new CosDictionary { [new CosName("Index")] = new CosInteger(index) };
            references[index] = source.AddObject(nodes[index]);
        }

        for (int index = 0; index < length; index++)
        {
            nodes[index][next] = references[(index + 1) % length];
        }

        using PdfDocument target = PdfDocument.Create();
        var copier = new PdfObjectCopier(source, target);

        var first = (CosReference)copier.Copy(references[0]);

        Assert.Equal(first, copier.Copy(references[0]));
        CosReference current = first;
        var seen = new HashSet<CosReference>();
        for (int index = 0; index < length; index++)
        {
            Assert.True(seen.Add(current));
            var node = (CosDictionary)target.Resolve(current);
            Assert.Equal(new CosInteger(index), node[new CosName("Index")]);
            current = (CosReference)node[next];
        }

        Assert.Equal(first, current);
    }

    [Fact]
    public void Arrays_nested_a_hundred_thousand_deep_copy_without_recursion()
    {
        const int depth = 100_000;
        using PdfDocument source = PdfDocument.Create();
        var outer = new CosArray();
        CosArray innermost = outer;
        for (int level = 1; level < depth; level++)
        {
            var inner = new CosArray();
            innermost.Add(inner);
            innermost = inner;
        }

        innermost.Add(new CosInteger(42));
        using PdfDocument target = PdfDocument.Create();

        CosObject copy = new PdfObjectCopier(source, target).Copy(outer);

        Assert.NotSame(outer, copy);
        int levels = 0;
        CosObject current = copy;
        while (current is CosArray array)
        {
            levels++;
            current = Assert.Single(array);
        }

        Assert.Equal(depth, levels);
        Assert.Equal(new CosInteger(42), current);
    }

    [Theory]
    [MemberData(nameof(EncryptedFiles))]
    public void A_page_copied_from_an_encrypted_document_saves_decrypted_with_the_same_content(string fileName)
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path(fileName));
        Assert.True(source.IsEncrypted);
        using PdfDocument target = PdfDocument.Create();
        var copier = new PdfObjectCopier(source, target);

        AppendPage(target, copier.CopyPage(source.Pages[0]));
        byte[] bytes = Save(target);

        using PdfDocument saved = PdfDocument.Open(bytes);
        Assert.False(saved.IsEncrypted);
        Assert.Equal(DecodedContents(source, source.Pages[0]), DecodedContents(saved, saved.Pages[1]));
        Assert.DoesNotContain(saved.Diagnostics, diagnostic => diagnostic.Severity >= Diagnostics.DiagnosticSeverity.Warning);
        Assert.Equal(0, ExternalTool.Run("qpdf", bytes, "--check").ExitCode);
    }

    [Fact]
    public void Streams_under_crypt_filters_and_encrypted_strings_copy_as_plaintext()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("encrypted-crypt-filters.pdf"));
        using PdfDocument target = PdfDocument.Create();
        var copier = new PdfObjectCopier(source, target);

        CosReference page = copier.CopyPage(source.Pages[0]);
        var info = (CosDictionary)target.Resolve(copier.Copy(source.Trailer[new CosName("Info")]));

        Assert.Equal("Crypt filters", ((CosString)info[new CosName("Title")]).DecodeText());
        var contents = (CosArray)target.Resolve(((CosDictionary)target.Resolve(page))[new CosName("Contents")]);
        Assert.Equal(3, contents.Count);
        foreach (CosObject part in contents)
        {
            var stream = (CosStream)target.Resolve(part);
            string filters = stream.Dictionary.TryGetValue(new CosName("Filter"), out CosObject? filter) ? target.Resolve(filter).ToString() : string.Empty;
            Assert.DoesNotContain("Crypt", filters, StringComparison.Ordinal);
        }

        string text = DecodedContents(target, contents);
        Assert.Contains("StmF StdCF", text, StringComparison.Ordinal);
        Assert.Contains("Identity crypt filter", text, StringComparison.Ordinal);
        Assert.Equal(DecodedContents(source, source.Pages[0]), text);
    }

    [Fact]
    public void Copying_the_source_encryption_dictionary_gives_null()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("encrypted-aes-256.pdf"));
        using PdfDocument target = PdfDocument.Create();

        CosObject copy = new PdfObjectCopier(source, target).Copy(source.Trailer[new CosName("Encrypt")]);

        Assert.Same(CosNull.Instance, copy);
    }

    [Fact]
    public void A_stream_under_a_crypt_filter_the_source_has_no_key_for_is_refused()
    {
        using PdfDocument source = PdfDocument.Open(Corpus.Path("encrypted-embedded-file-open.pdf"));
        using PdfDocument target = PdfDocument.Create();
        var copier = new PdfObjectCopier(source, target);

        Assert.Throws<NotSupportedException>(() => copier.Copy(source.Catalog[new CosName("Names")]));
    }

    [Fact]
    public void A_page_whose_annotations_carry_3d_and_rich_media_assets_and_whose_actions_carry_javascript_copies_them_unchanged()
    {
        using PdfDocument source = PdfDocument.Open(PreservedContentDocument());
        using PdfDocument target = PdfDocument.Create();
        var copier = new PdfObjectCopier(source, target);

        CosReference copy = copier.CopyPage(source.Pages[0]);

        CosGraph.AssertPageCopy(source, source.Pages[0].Reference!, target, copy);
        AppendPage(target, copy);
        using PdfDocument saved = PdfDocument.Open(Save(target));
        string[] subtypes = [.. saved.Pages[1].Annotations.Select(annotation => annotation.Subtype!.Value)];
        Assert.Equal(["3D", "RichMedia", "Link"], subtypes);
        var action = (CosDictionary)saved.Resolve(saved.Pages[1].Annotations[2].Dictionary[new CosName("A")]);
        var script = (CosStream)saved.Resolve(action[new CosName("JS")]);
        Assert.Equal("app.alert('copied');"u8.ToArray(), saved.DecodeStream(script).ToArray());
    }

    /// <summary>
    /// A one-page document holding what the library preserves without interpreting: a 3D annotation with a U3D stream (§13.6), a
    /// RichMedia annotation whose content names an embedded asset (Adobe Extension Level 3, §9.6 of ISO 32000-2 Annex), and a link
    /// whose JavaScript action's script is a Flate stream (§12.6.4.17).
    /// </summary>
    private static byte[] PreservedContentDocument()
    {
        using PdfDocument document = PdfDocument.Create();
        var u3d = new CosStream(new CosDictionary { [Type] = new CosName("3D"), [Subtype] = new CosName("U3D") }, new byte[] { 0x55, 0x33, 0x44, 0x00, 1, 2, 3 });
        var threeD = new CosDictionary
        {
            [Type] = new CosName("Annot"),
            [Subtype] = new CosName("3D"),
            [new CosName("Rect")] = Rectangle(),
            [new CosName("3DD")] = document.AddObject(u3d),
        };
        var asset = new CosStream(new CosDictionary { [Type] = new CosName("EmbeddedFile") }, "<swf/>"u8.ToArray());
        var fileSpecification = new CosDictionary
        {
            [Type] = new CosName("Filespec"),
            [new CosName("F")] = new CosString("player.swf"u8.ToArray()),
            [new CosName("UF")] = new CosString("player.swf"u8.ToArray()),
            [new CosName("EF")] = new CosDictionary { [new CosName("F")] = document.AddObject(asset) },
        };
        CosReference specification = document.AddObject(fileSpecification);
        var richMedia = new CosDictionary
        {
            [Type] = new CosName("Annot"),
            [Subtype] = new CosName("RichMedia"),
            [new CosName("Rect")] = Rectangle(),
            [new CosName("RichMediaContent")] = new CosDictionary
            {
                [new CosName("Assets")] = new CosDictionary { [new CosName("Names")] = new CosArray([new CosString("player.swf"u8.ToArray()), specification]) },
                [new CosName("Configurations")] = new CosArray([new CosDictionary { [Type] = new CosName("RichMediaConfiguration"), [Subtype] = new CosName("Flash"), [new CosName("Instances")] = new CosArray([new CosDictionary { [new CosName("Asset")] = specification }]) }]),
            },
        };
        var script = new CosStream(new CosDictionary { [new CosName("Filter")] = new CosName("FlateDecode") }, ReadOnlyMemory<byte>.Empty);
        document.EncodeStream(script, "app.alert('copied');"u8);
        var link = new CosDictionary
        {
            [Type] = new CosName("Annot"),
            [Subtype] = new CosName("Link"),
            [new CosName("Rect")] = Rectangle(),
            [new CosName("A")] = new CosDictionary { [new CosName("S")] = new CosName("JavaScript"), [new CosName("JS")] = document.AddObject(script) },
        };
        document.Pages[0].Dictionary[new CosName("Annots")] = new CosArray([document.AddObject(threeD), document.AddObject(richMedia), document.AddObject(link)]);
        return Save(document);

        static CosArray Rectangle() => new([new CosInteger(10), new CosInteger(10), new CosInteger(110), new CosInteger(110)]);
    }

    /// <summary>The page's content streams, decoded and joined.</summary>
    private static string DecodedContents(PdfDocument document, PdfPage page) =>
        DecodedContents(document, page.Dictionary[new CosName("Contents")]);

    private static string DecodedContents(PdfDocument document, CosObject contents) => document.Resolve(contents) switch
    {
        CosStream stream => System.Text.Encoding.Latin1.GetString(document.DecodeStream(stream).Span),
        CosArray parts => string.Join("\n", parts.Select(part => DecodedContents(document, part))),
        _ => string.Empty,
    };

    private static bool IsForm(CosObject value) =>
        value is CosStream stream && stream.Dictionary.TryGetValue(Subtype, out CosObject? subtype) && new CosName("Form").Equals(subtype);

    private static CosObject[] XObjects(PdfDocument document, PdfPage page) =>
        [.. ((CosDictionary)document.Resolve(page.Resources![XObject])).Values];

    /// <summary>Gives <paramref name="document"/> a second page whose resources are the first page's, through the COS layer.</summary>
    private static void AddPageSharingResources(PdfDocument document)
    {
        // Read through the COS layer: the page list is the tree's shape when it is first walked.
        var root = (CosDictionary)document.Resolve(document.Catalog[Pages]);
        var first = (CosDictionary)document.Resolve(((CosArray)document.Resolve(root[Kids]))[0]);
        var page = new CosDictionary
        {
            [Type] = new CosName("Page"),
            [Resources] = first[Resources],
            [new CosName("MediaBox")] = first[new CosName("MediaBox")],
            [new CosName("Contents")] = first[new CosName("Contents")],
        };
        AppendPage(document, document.AddObject(page));
    }

    /// <summary>Appends the page <paramref name="page"/> names to the root of the page tree (§7.7.3.2), through the COS layer.</summary>
    private static void AppendPage(PdfDocument document, CosReference page)
    {
        CosReference rootReference = (CosReference)document.Catalog[Pages];
        var root = (CosDictionary)document.Resolve(rootReference);
        ((CosDictionary)document.Resolve(page))[Parent] = rootReference;
        ((CosArray)document.Resolve(root[Kids])).Add(page);
        root[Count] = new CosInteger(((CosInteger)document.Resolve(root[Count])).Value + 1);
    }

    private static byte[] Save(PdfDocument document, PdfSaveOptions? options = null)
    {
        using var output = new MemoryStream();
        document.Save(output, options ?? new PdfSaveOptions());
        return output.ToArray();
    }
}
