using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// Runs a <see cref="ContentEditor"/> over a page's or a form's content and stores the result as one unfiltered content stream
/// (a save compresses it, issue #142).
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.8.2, §7.3.8.2 (the stream's data and its filter entries), §7.3.10 and Table 31 (<c>Contents</c>). A page's
/// result goes into a new indirect stream (<see cref="PdfDocument.AddObject"/>) that the page's <c>Contents</c> then names alone, so
/// a stream the page shares with another page, or that anything else refers to, is never written into; the old streams stay as
/// objects until a save that removes unreachable objects drops them. A form's result goes into the form's own stream, which is the form:
/// every use of it shows the change.
/// </remarks>
internal static class ContentRewriting
{
    /// <summary>The stream dictionary entries that describe the old data's encoding or location (Table 5), dropped with it.</summary>
    private static readonly CosName[] EncodingEntries =
    [
        FilterNames.Filter,
        FilterNames.DecodeParms,
        FilterNames.F,
        new("FFilter"),
        new("FDecodeParms"),
        FilterNames.DL,
    ];

    public static bool RewritePage(PdfPage page, PdfDocument document, ContentEditor editor, ContentOptions options)
    {
        ReadOnlyMemory<byte> content = ReadOnlyMemory<byte>.Empty;
        ContentPart[]? parts = null;
        CosObject? entry = page.Dictionary.TryGetValue(KnownNames.Contents, out CosObject? value) ? value : null;
        switch (document.Resolve(entry))
        {
            case CosStream stream:
                content = document.ContentResources.GetContent(stream);
                break;
            case CosArray array:
                ContentResourceCache.JoinedContent joined = document.ContentResources.GetContent(array);
                content = joined.Data;
                parts = joined.Parts;
                break;
        }

        ContentProcessor processor = editor.Begin(content, parts);
        try
        {
            ContentInterpreter.RunPage(page, document, processor, options);
            if (!editor.Finish())
            {
                return false;
            }

            page.Dictionary[KnownNames.Contents] = document.AddObject(new CosStream(new CosDictionary(), editor.Output.ToArray()));
            return true;
        }
        finally
        {
            editor.End();
        }
    }

    public static bool RewriteForm(PdfFormXObject form, PdfDocument document, ContentEditor editor, ContentOptions options)
    {
        ContentProcessor processor = editor.Begin(document.ContentResources.GetContent(form.Stream), parts: null);
        try
        {
            ContentInterpreter.RunForm(form, document, processor, options);
            if (!editor.Finish())
            {
                return false;
            }

            Store(form.Stream, editor.Output);
            return true;
        }
        finally
        {
            editor.End();
        }
    }

    /// <summary>
    /// Runs <paramref name="editor"/> over <paramref name="content"/> as a page's only stream and returns the rewritten bytes (the
    /// content itself when there are no edits); for fuzzing and benchmarks, which have content without a page around it.
    /// </summary>
    internal static ReadOnlySpan<byte> RewriteBytes(ReadOnlyMemory<byte> content, PdfDocument document, ContentEditor editor, ContentOptions options)
    {
        ContentProcessor processor = editor.Begin(content, parts: null);
        try
        {
            ContentInterpreter.RunBytes(content.Span, document, processor, options);
            return editor.Finish() ? editor.Output : content.Span;
        }
        finally
        {
            editor.End();
        }
    }

    /// <summary>Replaces a stream's data with unfiltered content and drops what described the old encoding.</summary>
    private static void Store(CosStream stream, ReadOnlySpan<byte> data)
    {
        foreach (CosName key in EncodingEntries)
        {
            stream.Dictionary.Remove(key);
        }

        stream.EncodedData = data.ToArray();
    }
}
