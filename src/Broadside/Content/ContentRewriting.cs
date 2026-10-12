using Broadside.Filters;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// Runs a <see cref="ContentEditor"/> over a page's or a form's content and stores the result as one unfiltered content stream
/// (a save compresses it, issue #142).
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.8.2, §7.3.8.2 (the stream's data and its filter entries), Table 31 (<c>Contents</c>). The result is written into
/// an existing stream, because a document has no way yet to add an indirect object: a form's own stream, or the page's content
/// stream (for an array, its first stream no other page uses), which the page's <c>Contents</c> then names alone.
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

            (CosStream target, CosObject reference) = PageTarget(page, document, entry!);
            Store(target, editor.Output);
            if (!ReferenceEquals(reference, entry))
            {
                page.Dictionary[KnownNames.Contents] = reference;
            }

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

    /// <summary>
    /// Chooses the stream a page's rewritten content goes into and what <c>Contents</c> names afterwards: the page's stream, or the
    /// first stream of its array, that no other page's <c>Contents</c> uses.
    /// </summary>
    /// <exception cref="InvalidOperationException">Every candidate stream is shared with another page.</exception>
    private static (CosStream Stream, CosObject Reference) PageTarget(PdfPage page, PdfDocument document, CosObject entry)
    {
        HashSet<CosReference> shared = SharedStreams(page, document);
        if (document.Resolve(entry) is CosArray array)
        {
            foreach (CosObject element in array)
            {
                if (document.Resolve(element) is CosStream stream && (element is not CosReference reference || !shared.Contains(reference)))
                {
                    return (stream, element);
                }
            }
        }
        else if (entry is not CosReference reference || !shared.Contains(reference))
        {
            return ((CosStream)document.Resolve(entry), entry);
        }

        throw new InvalidOperationException(
            "The page's content streams are all used by other pages too; writing the rewritten content into one would change those pages.");
    }

    /// <summary>The content streams other pages' <c>Contents</c> name.</summary>
    private static HashSet<CosReference> SharedStreams(PdfPage page, PdfDocument document)
    {
        var shared = new HashSet<CosReference>();
        foreach (PdfPage other in document.Pages)
        {
            if (ReferenceEquals(other.Dictionary, page.Dictionary) || !other.Dictionary.TryGetValue(KnownNames.Contents, out CosObject? contents))
            {
                continue;
            }

            if (contents is CosReference reference)
            {
                shared.Add(reference);
            }

            if (document.Resolve(contents) is CosArray array)
            {
                foreach (CosObject element in array)
                {
                    if (element is CosReference part)
                    {
                        shared.Add(part);
                    }
                }
            }
        }

        return shared;
    }
}
