using System.Text;
using Broadside.Content;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Content;

/// <summary>
/// Content stream rewriting (ISO 32000-2 §7.8.2; Table 31 for <c>Contents</c> arrays): an editor sees every operator of a page or
/// form with its byte range and removes, replaces, inserts or wraps operators; what it does not touch is copied byte for byte.
/// </summary>
public sealed class ContentRewritingTests
{
    private static readonly CosName Contents = new("Contents");
    private static readonly CosName Filter = new("Filter");

    [Fact]
    public void Removing_every_image_paint_from_image_smask_pdf_leaves_no_image_events_and_the_other_operators_byte_identical()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("image-smask.pdf"));
        PdfPage page = document.Pages[0];

        bool rewritten = page.RewriteContent(new ImageRemover());

        Assert.True(rewritten);
        Assert.Equal("q 100 0 0 100 72 600 cm  Q", Decoded(document, page));
        Assert.Equal(["q", "100 0 0 100 72 600 cm", "Q"], OperatorBytes(document, page));
        var events = new RecordingProcessor();
        page.ProcessContent(events);
        Assert.Equal(0, events.Count("PaintImage"));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_text_object_wrapped_in_marked_content_with_an_mcid_reads_back_with_the_mcid()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14.pdf"));
        PdfPage page = document.Pages[0];

        page.RewriteContent(new TextTagger());

        Assert.Equal("/P <</MCID 0>> BDC\nBT /F1 24 Tf 72 700 Td (Hello, Broadside) Tj ET\nEMC", Decoded(document, page));
        var marked = new MarkedContentReader();
        page.ProcessContent(marked);
        Assert.Equal(["P mcid=0 glyphs=16"], marked.Sequences);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_identity_rewrite_reproduces_the_original_bytes()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("flate-stream.pdf"));
        PdfPage page = document.Pages[0];
        string original = Decoded(document, page);

        bool rewritten = page.RewriteContent(new SelfReplacer());

        Assert.True(rewritten);
        Assert.Equal(original, Decoded(document, page));
        Assert.False(ContentStream(document, page).Dictionary.ContainsKey(Filter));
    }

    [Fact]
    public void An_editor_that_edits_nothing_leaves_the_page_untouched()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("flate-stream.pdf"));
        PdfPage page = document.Pages[0];
        CosStream stream = ContentStream(document, page);

        bool rewritten = page.RewriteContent(new OperatorCounter());

        Assert.False(rewritten);
        Assert.False(stream.IsDirty);
        Assert.False(page.Dictionary.IsDirty);
    }

    [Fact]
    public void A_contents_array_is_rewritten_as_one_stream_with_the_white_space_joins_of_its_parts()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q 0 0 10 10", "re f", "Q"));
        PdfPage page = document.Pages[0];

        page.RewriteContent(new Inserter(ContentOperatorCode.Rectangle, "1 0 0 rg"));

        Assert.IsType<CosReference>(page.Dictionary[Contents]);
        Assert.Equal("q 1 0 0 rg\n0 0 10 10\nre f\nQ\n", Decoded(document, page));
        Assert.Equal(["q", "1 0 0 rg", "0 0 10 10\nre", "f", "Q"], OperatorBytes(document, page));
    }

    [Fact]
    public void Wraps_nest_wider_outside_narrower_and_later_outside_earlier_with_insertions_innermost()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("0 g 1 g 2 g"));
        PdfPage page = document.Pages[0];

        page.RewriteContent(new ScriptedEditor(editor =>
        {
            editor.Wrap(1, 1, "/A BMC"u8, "EMC"u8);
            editor.Wrap(0, 2, "/B BMC"u8, "EMC"u8);
            editor.Wrap(1, 1, "/C BMC"u8, "EMC"u8);
            editor.Wrap(1, 2, "/D BMC"u8, "EMC"u8);
            editor.InsertBefore(1, "q"u8);
            editor.InsertAfter(1, "Q"u8);
        }));

        Assert.Equal("/B BMC\n0 g /D BMC\n/C BMC\n/A BMC\nq\n1 g\nQ\nEMC\nEMC\n 2 g\nEMC\nEMC", Decoded(document, page));
        var events = new RecordingProcessor(ContentEvents.MarkedContent);
        page.ProcessContent(events);
        Assert.Equal(4, events.Count("BeginMarkedContent"));
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void The_last_replacement_of_an_operator_wins_and_an_empty_one_removes_it()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("0 0 1 rg 0 0 10 10 re f"));
        PdfPage page = document.Pages[0];

        page.RewriteContent(new ScriptedEditor(editor =>
        {
            editor.Remove(0);
            editor.Replace(0, "1 0 0 rg"u8);
            editor.Replace(2, "f*"u8);
            editor.Replace(1, []);
        }));

        Assert.Equal("1 0 0 rg\n  f*", Decoded(document, page));
    }

    [Fact]
    public void A_removal_that_would_join_two_tokens_leaves_a_space_between_them()
    {
        // "BQT*" reads as the glued operators B, Q and T*; without a separator, "BT*" would read as one unknown keyword.
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("0 0 m 1 1 l BQT*"));
        PdfPage page = document.Pages[0];

        page.RewriteContent(new ScriptedEditor(editor => editor.Remove(3)));

        Assert.Equal("0 0 m 1 1 l B T*", Decoded(document, page));
        Assert.Equal(["0 0 m", "1 1 l", "B", "T*"], OperatorBytes(document, page));
    }

    [Fact]
    public void Inserted_text_that_ends_in_a_comment_does_not_swallow_the_next_operator()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q Q"));
        PdfPage page = document.Pages[0];

        page.RewriteContent(new ScriptedEditor(editor => editor.InsertAfter(0, "% a note"u8)));

        Assert.Equal("q\n% a note\n Q", Decoded(document, page));
        Assert.Equal(["q", "Q"], OperatorBytes(document, page));
    }

    [Fact]
    public void Events_of_nested_content_belong_to_the_operator_that_started_it()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("form-xobject-nested.pdf"));
        PdfPage page = document.Pages[0];

        page.RewriteContent(new NestedPathRemover());

        Assert.Equal("q  Q \n", Decoded(document, page));
    }

    [Fact]
    public void A_form_xobject_is_rewritten_in_its_own_stream_as_a_form_run()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("form-xobject-nested.pdf"));
        PdfPage page = document.Pages[0];
        var forms = new FormCollector();
        page.ProcessContent(forms);
        PdfFormXObject form = forms.Forms[0];
        var editor = new XObjectRemover();

        bool rewritten = form.RewriteContent(editor);

        Assert.True(rewritten);
        Assert.Equal(ContentRunKind.Form, editor.RunKind);
        Assert.Equal("0 0 10 10 re f ", Encoding.Latin1.GetString(form.Decode().Span));
        var after = new FormCollector();
        page.ProcessContent(after);
        Assert.Equal(2, after.Forms.Count);
    }

    [Fact]
    public void Rewriting_a_page_whose_streams_another_page_shows_leaves_that_page_with_its_original_content()
    {
        using PdfDocument document = PdfDocument.Open(new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents [5 0 R 6 0 R] >>",
            ContentPdf.Stream("q Q"),
            ContentPdf.Stream("0 g")));
        PdfPage first = document.Pages[0];
        PdfPage second = document.Pages[1];

        bool firstRewritten = first.RewriteContent(new ScriptedEditor(editor => editor.Remove(0)));

        Assert.True(firstRewritten);
        Assert.Equal(" Q", Decoded(document, first));
        Assert.Equal("q Q", DecodedObject(document, 5));
        Assert.Equal("0 g", DecodedObject(document, 6));
        Assert.Equal(["q in 5", "Q in 5", "g in 6"], OperatorSources(second));

        second.RewriteContent(new ScriptedEditor(editor => editor.Remove(2)));

        Assert.Equal(" Q", Decoded(document, first));
        Assert.Equal("q Q\n\n", Decoded(document, second));
        Assert.Equal("q Q", DecodedObject(document, 5));
        Assert.NotEqual(first.Dictionary[Contents], second.Dictionary[Contents]);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_rewritten_page_saves_incrementally_and_reopens_with_the_rewritten_content_beside_the_untouched_original()
    {
        byte[] original = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Contents 5 0 R >>",
            ContentPdf.Stream("0 0 10 10 re f"));
        using PdfDocument document = PdfDocument.Open(original);
        document.Pages[0].RewriteContent(new ScriptedEditor(editor => editor.Wrap(0, 1, "q"u8, "Q"u8)));
        using var saved = new MemoryStream();

        document.Save(saved, new PdfSaveOptions().WithIncrementalUpdate(true));

        byte[] bytes = saved.ToArray();
        Assert.Equal(original, bytes[..original.Length]);
        using PdfDocument reopened = PdfDocument.Open(bytes);
        Assert.Equal("q\n0 0 10 10 re f\nQ", Decoded(reopened, reopened.Pages[0]));
        Assert.Equal("0 0 10 10 re f", Decoded(reopened, reopened.Pages[1]));
        Assert.Empty(reopened.Diagnostics);
    }

    [Fact]
    public void Edits_are_refused_outside_a_rewrite_and_for_operators_not_numbered_yet()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q Q"));
        PdfPage page = document.Pages[0];
        var idle = new ScriptedEditor(_ => { });
        page.ProcessContent(idle);

        Assert.Equal(-1, idle.CurrentOperator);
        Assert.Throws<InvalidOperationException>(() => idle.Remove(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.RewriteContent(new ScriptedEditor(editor => editor.Remove(2))));
        Assert.Throws<ArgumentOutOfRangeException>(() => page.RewriteContent(new ScriptedEditor(editor => editor.Wrap(1, 0, "q"u8, "Q"u8))));
        Assert.Equal("q Q", Decoded(document, page));
    }

    [Fact]
    public void A_rewritten_page_saves_compressed_and_reopens_with_the_rewritten_content()
    {
        string content = Encoding.ASCII.GetString(ContentSamples.PathHeavy(16 * 1024));
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(content));
        page(document).RewriteContent(new ScriptedEditor(editor => editor.Wrap(0, editor.OperatorCount - 1, "q"u8, "Q"u8)));
        string rewritten = Decoded(document, page(document));
        using var saved = new MemoryStream();

        document.Save(saved);

        using PdfDocument reopened = PdfDocument.Open(saved.ToArray());
        Assert.Equal("q\n" + content.TrimEnd('\n') + "\nQ\n", rewritten);
        Assert.Equal(new CosName("FlateDecode"), ContentStream(reopened, page(reopened)).Dictionary[Filter]);
        Assert.Equal(rewritten, Decoded(reopened, page(reopened)));
        Assert.Empty(reopened.Diagnostics);

        static PdfPage page(PdfDocument document) => document.Pages[0];
    }

    private static CosStream ContentStream(PdfDocument document, PdfPage page) => (CosStream)document.Resolve(page.Dictionary[Contents]);

    private static string DecodedObject(PdfDocument document, int number) =>
        Encoding.Latin1.GetString(document.DecodeStream((CosStream)document.Resolve(new CosReference(number, 0))).Span);

    /// <summary>Every operator a page's content holds, its <c>Contents</c> array parts included, as "keyword in object number".</summary>
    private static List<string> OperatorSources(PdfPage page)
    {
        var sources = new SourceRecorder();
        page.ProcessContent(sources);
        return sources.Sources;
    }

    private static string Decoded(PdfDocument document, PdfPage page) =>
        Encoding.Latin1.GetString(document.DecodeStream(ContentStream(document, page)).Span);

    /// <summary>The source bytes of every operator of a single-stream page, from the ranges the interpreter reports.</summary>
    private static List<string> OperatorBytes(PdfDocument document, PdfPage page)
    {
        string content = Decoded(document, page);
        var ranges = new RangeRecorder();
        page.ProcessContent(ranges);
        return [.. ranges.Ranges.Select(range => content.Substring(range.Offset, range.Length))];
    }

    private sealed class SourceRecorder : ContentProcessor
    {
        public List<string> Sources { get; } = [];

        public override ContentEvents Events => ContentEvents.Operators;

        public override void VisitOperator(in ContentOperator op, ContentContext context) =>
            Sources.Add($"{Encoding.Latin1.GetString(op.Keyword)} in {op.Stream?.ObjectNumber}");
    }

    private sealed class RangeRecorder : ContentProcessor
    {
        public List<(int Offset, int Length)> Ranges { get; } = [];

        public override ContentEvents Events => ContentEvents.Operators;

        public override void VisitOperator(in ContentOperator op, ContentContext context) => Ranges.Add((op.Offset, op.Length));
    }

    /// <summary>Removes the operator that paints each image of the page's own content.</summary>
    private sealed class ImageRemover : ContentEditor
    {
        public override ContentEvents Events => ContentEvents.Images;

        public override void PaintImage(in ImageEvent image, ContentContext context)
        {
            if (context.Depth == 0)
            {
                Remove(CurrentOperator);
            }
        }
    }

    /// <summary>Wraps each text object in a <c>/P</c> marked-content sequence with the next MCID.</summary>
    private sealed class TextTagger : ContentEditor
    {
        private int _begin;
        private int _mcid;

        public override ContentEvents Events => ContentEvents.Text;

        public override void BeginText(ContentContext context) => _begin = CurrentOperator;

        public override void EndText(ContentContext context) =>
            Wrap(_begin, CurrentOperator, Encoding.ASCII.GetBytes($"/P <</MCID {_mcid++}>> BDC"), "EMC"u8);
    }

    /// <summary>Replaces every operator with its own bytes.</summary>
    private sealed class SelfReplacer : ContentEditor
    {
        public override ContentEvents Events => ContentEvents.None;

        public override void EndRun(ContentContext context)
        {
            for (int index = 0; index < OperatorCount; index++)
            {
                ContentOperatorRange range = GetOperator(index);
                Replace(index, Content.Span.Slice(range.Offset, range.Length));
            }
        }
    }

    /// <summary>Counts operators and edits nothing.</summary>
    private sealed class OperatorCounter : ContentEditor
    {
        public override ContentEvents Events => ContentEvents.None;
    }

    /// <summary>Inserts text before every operator with the given code.</summary>
    private sealed class Inserter(ContentOperatorCode code, string text) : ContentEditor
    {
        public override ContentEvents Events => ContentEvents.Operators;

        public override void VisitOperator(in ContentOperator op, ContentContext context)
        {
            if (op.Code == code)
            {
                InsertBefore(CurrentOperator, Encoding.ASCII.GetBytes(text));
            }
        }
    }

    /// <summary>Runs its script at the end of the run, when every operator is numbered.</summary>
    private sealed class ScriptedEditor(Action<ContentEditor> script) : ContentEditor
    {
        public override ContentEvents Events => ContentEvents.None;

        public override void EndRun(ContentContext context) => script(this);
    }

    /// <summary>Removes the operators whose nested content paints a path.</summary>
    private sealed class NestedPathRemover : ContentEditor
    {
        public override ContentEvents Events => ContentEvents.Paths;

        public override void PaintPath(in PathEvent path, ContentContext context)
        {
            if (context.Depth > 0)
            {
                Remove(CurrentOperator);
            }
        }
    }

    /// <summary>Removes every <c>Do</c> and remembers the kind of run.</summary>
    private sealed class XObjectRemover : ContentEditor
    {
        public ContentRunKind RunKind { get; private set; }

        public override ContentEvents Events => ContentEvents.Operators;

        public override void BeginRun(ContentContext context) => RunKind = context.RunKind;

        public override void VisitOperator(in ContentOperator op, ContentContext context)
        {
            if (op.Code == ContentOperatorCode.PaintXObject && context.Depth == 0)
            {
                Remove(CurrentOperator);
            }
        }
    }

    /// <summary>Collects the forms painted, at any depth.</summary>
    private sealed class FormCollector : ContentProcessor
    {
        public List<PdfFormXObject> Forms { get; } = [];

        public override ContentEvents Events => ContentEvents.Forms;

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
        {
            Forms.Add(form.Form!);
            return ContentVisit.Enter;
        }
    }

    /// <summary>Records each marked-content sequence's tag, MCID and the number of glyphs shown inside it.</summary>
    private sealed class MarkedContentReader : ContentProcessor
    {
        private string? _open;
        private int _glyphs;

        public List<string> Sequences { get; } = [];

        public override ContentEvents Events => ContentEvents.MarkedContent | ContentEvents.Glyphs;

        public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context)
        {
            _open = $"{Encoding.ASCII.GetString(mc.Tag)} mcid={mc.Mcid}";
            _glyphs = 0;
        }

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context) => _glyphs++;

        public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context) => Sequences.Add($"{_open} glyphs={_glyphs}");
    }
}
