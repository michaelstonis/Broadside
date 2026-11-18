using Broadside.Objects;
using Broadside.Rendering;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Issue #578: a document's cached display lists and decoded images are current while none of its own objects changes; a change to
/// another document's objects leaves them current, and a change to anything the list reads (resources, inherited page attributes,
/// form XObjects, objects shared with another document) still makes them stale.
/// </summary>
/// <remarks>
/// In the non-parallel collection: a change to an object that belongs to two documents makes every document's caches stale, and the
/// allocation test measures its own thread.
/// </remarks>
[Collection(HeavyTestCollection.Name)]
public sealed class PerDocumentEpochTests
{
    private static readonly CosName Rotate = new("Rotate");
    private static readonly CosName Resources = new("Resources");
    private static readonly CosName MediaBox = new("MediaBox");
    private static readonly CosName XObject = new("XObject");
    private static readonly CosName ExtGState = new("ExtGState");

    [Fact]
    public void Editing_one_document_leaves_the_display_lists_and_images_of_another_cached()
    {
        using PdfDocument viewed = PdfDocument.Open(FormAndImagePage());
        using PdfDocument edited = PdfDocument.Open(FormAndImagePage());
        DisplayList list = viewed.Pages[0].CreateDisplayList();
        DisplayList otherList = edited.Pages[0].CreateDisplayList();
        long epoch = viewed.Epoch;

        PdfPage page = edited.Pages[0];
        page.Dictionary[Rotate] = new CosInteger(90);
        ((CosStream)edited.Resolve(new CosReference(5, 0))).EncodedData = "0 0 1 rg 0 0 5 5 re f"u8.ToArray();
        edited.Trailer[new CosName("Edited")] = CosBoolean.True;
        CosReference added = edited.AddObject(new CosDictionary { [new CosName("Key")] = new CosArray { new CosInteger(1) } });
        ((CosDictionary)edited.Resolve(added))[new CosName("Key")] = CosBoolean.False;
        Assert.True(edited.RemoveObject(added));

        Assert.Equal(epoch, viewed.Epoch);
        Assert.Same(list, viewed.Pages[0].CreateDisplayList());
        Assert.Equal(1, viewed.CacheStatistics.DisplayLists.Hits);
        Assert.Equal(1, viewed.CacheStatistics.DisplayLists.Misses);
        viewed.Caches.DisplayLists.Clear();
        _ = viewed.Pages[0].CreateDisplayList();
        Assert.Equal(1, viewed.CacheStatistics.Images.Hits);
        Assert.Equal(1, viewed.CacheStatistics.Images.Misses);
        Assert.NotSame(otherList, edited.Pages[0].CreateDisplayList());
    }

    [Fact]
    public void An_edit_to_anything_the_list_reads_makes_it_stale()
    {
        using PdfDocument document = PdfDocument.Open(InheritedPage());
        CosDictionary pages = (CosDictionary)document.Resolve(new CosReference(2, 0));
        var form = (CosStream)document.Resolve(new CosReference(5, 0));
        var edits = new Action[]
        {
            () => form.EncodedData = "0 1 0 rg 0 0 20 20 re f"u8.ToArray(),
            () => form.Dictionary[new CosName("Matrix")] = new CosArray { new CosInteger(2), new CosInteger(0), new CosInteger(0), new CosInteger(2), new CosInteger(0), new CosInteger(0) },
            () => ((CosDictionary)pages[Resources])[ExtGState] = new CosDictionary(),
            () => ((CosArray)pages[MediaBox])[2] = new CosInteger(300),
            () => ((CosDictionary)((CosDictionary)pages[Resources])[XObject])[new CosName("Fm1")] = new CosReference(5, 0),
            () => document.AddObject(new CosDictionary()),
        };

        foreach (Action edit in edits)
        {
            DisplayList before = document.Pages[0].CreateDisplayList();
            Assert.Same(before, document.Pages[0].CreateDisplayList());

            edit();

            Assert.NotSame(before, document.Pages[0].CreateDisplayList());
        }
    }

    [Fact]
    public void Pages_copied_into_another_document_belong_to_it_and_their_edits_stay_there()
    {
        using PdfDocument source = PdfDocument.Open(FormAndImagePage());
        using PdfDocument target = PdfDocument.Create();
        target.AppendPages(source);
        PdfPage copy = target.Pages[^1];
        DisplayList sourceList = source.Pages[0].CreateDisplayList();
        DisplayList copyList = copy.CreateDisplayList();

        Assert.Same(target.Owner, copy.Dictionary.Owner);
        Assert.Same(target.Owner, ((CosDictionary)copy.Dictionary[Resources]).Owner);
        copy.Dictionary[Rotate] = new CosInteger(180);
        ((CosDictionary)copy.Dictionary[Resources])[ExtGState] = new CosDictionary();

        Assert.Same(sourceList, source.Pages[0].CreateDisplayList());
        DisplayList editedCopy = copy.CreateDisplayList();
        Assert.NotSame(copyList, editedCopy);

        source.Pages[0].Dictionary[Rotate] = new CosInteger(90);

        Assert.Same(editedCopy, copy.CreateDisplayList());
        Assert.NotSame(sourceList, source.Pages[0].CreateDisplayList());
    }

    [Fact]
    public void A_container_put_into_two_documents_makes_both_documents_lists_stale_when_it_changes()
    {
        using PdfDocument first = PdfDocument.Open(FormAndImagePage());
        using PdfDocument second = PdfDocument.Open(FormAndImagePage());
        var resources = (CosDictionary)first.Pages[0].Dictionary[Resources];
        var xobjects = (CosDictionary)resources[XObject];
        second.Pages[0].Dictionary[new CosName("PieceInfo")] = new CosDictionary { [new CosName("Borrowed")] = xobjects };
        Assert.True(xobjects.Owner!.IsShared);
        Assert.Same(first.Owner, resources.Owner);

        DisplayList firstList = first.Pages[0].CreateDisplayList();
        DisplayList secondList = second.Pages[0].CreateDisplayList();
        xobjects[new CosName("Unused")] = new CosDictionary();

        Assert.NotSame(firstList, first.Pages[0].CreateDisplayList());
        Assert.NotSame(secondList, second.Pages[0].CreateDisplayList());
    }

    [Fact]
    public void Objects_belong_to_the_document_that_loads_or_adds_them_down_to_every_direct_object()
    {
        using PdfDocument document = PdfDocument.Open(FormAndImagePage());
        using PdfDocument other = PdfDocument.Open(FormAndImagePage());
        CosDictionary page = document.Pages[0].Dictionary;
        Assert.Same(document.Owner, page.Owner);
        Assert.Same(document.Owner, ((CosArray)page[MediaBox]).Owner);
        Assert.Same(document.Owner, ((CosDictionary)((CosDictionary)page[Resources])[XObject]).Owner);
        Assert.Same(document.Owner, ((CosStream)document.Resolve(new CosReference(5, 0))).Dictionary.Owner);
        Assert.Same(document.Owner, document.Trailer.Owner);
        Assert.NotSame(document.Owner, other.Owner);

        // A deep chain of new direct objects belongs to the document once its top is put into it, past the recursion limit too.
        var top = new CosArray();
        CosArray deepest = top;
        for (int level = 0; level < 500; level++)
        {
            var next = new CosArray();
            deepest.Add(new CosDictionary { [new CosName("Next")] = next });
            deepest = next;
        }

        Assert.Null(top.Owner);
        long before = document.Epoch;
        page[new CosName("Chain")] = top;
        Assert.Same(document.Owner, deepest.Owner);
        long after = document.Epoch;
        deepest.Add(CosNull.Instance);
        Assert.Equal(after + 1, document.Epoch);
        Assert.True(after > before);

        // Put into a second document, the chain belongs to both: its changes move both epochs.
        other.Pages[0].Dictionary[new CosName("Chain")] = top;
        Assert.True(deepest.Owner!.IsShared);
        long documentBefore = document.Epoch;
        long otherBefore = other.Epoch;
        deepest.Add(CosNull.Instance);
        Assert.True(document.Epoch > documentBefore);
        Assert.True(other.Epoch > otherBefore);
    }

    [Fact]
    public void Finding_a_kept_list_after_another_document_changes_allocates_nothing()
    {
        using PdfDocument viewed = PdfDocument.Open(FormAndImagePage());
        using PdfDocument edited = PdfDocument.Open(FormAndImagePage());
        PdfPage page = viewed.Pages[0];
        CosDictionary other = edited.Pages[0].Dictionary;
        DisplayList list = page.CreateDisplayList();
        CosInteger angle = new(90);
        DisplayList? found = null;
        Action findAfterEdit = () =>
        {
            other[Rotate] = angle;
            found = page.CreateDisplayList();
        };

        long allocated = Allocations.Measure(findAfterEdit);

        Assert.Equal(0, allocated);
        Assert.Same(list, found);
        Assert.Equal(1, viewed.CacheStatistics.DisplayLists.Misses);
    }

    [Fact]
    public void Building_objects_that_belong_to_no_document_yet_moves_no_epoch_until_they_are_put_into_one()
    {
        using PdfDocument document = PdfDocument.Open(FormAndImagePage());
        long before = document.Epoch;

        var stream = new CosStream(new CosDictionary(), ReadOnlyMemory<byte>.Empty);
        var loose = new CosDictionary { [new CosName("Kids")] = new CosArray { stream } };
        stream.EncodedData = "0 0 m"u8.ToArray();
        stream.Dictionary[new CosName("Filter")] = new CosName("FlateDecode");

        Assert.Equal(before, document.Epoch);
        document.Pages[0].Dictionary[new CosName("Loose")] = loose;
        Assert.True(document.Epoch > before);
        Assert.Same(document.Owner, stream.Dictionary.Owner);
        long attached = document.Epoch;
        stream.EncodedData = "1 1 m"u8.ToArray();
        Assert.Equal(attached + 1, document.Epoch);
    }

    /// <summary>A page that paints a form XObject (object 5) and an image XObject (object 6).</summary>
    private static byte[] FormAndImagePage() => ContentSamples.OnePage(
        "/Fm0 Do /Im0 Do"u8.ToArray(),
        "/XObject << /Fm0 5 0 R /Im0 6 0 R >>",
        ContentSamples.Stream("1 0 0 rg 0 0 10 10 re f", "/Type /XObject /Subtype /Form /BBox [0 0 100 100]"),
        ContentSamples.Stream("\u0000\u0080ÿ@", "/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8"));

    /// <summary>A page that inherits its resources and media box from the page tree node and paints a form XObject (object 5).</summary>
    private static byte[] InheritedPage() => ContentSamples.File(
    [
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] /Resources << /XObject << /Fm0 5 0 R >> >> >>",
        "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>",
        ContentSamples.Stream("/Fm0 Do"),
        ContentSamples.Stream("1 0 0 rg 0 0 10 10 re f", "/Type /XObject /Subtype /Form /BBox [0 0 100 100]"),
    ]);
}
