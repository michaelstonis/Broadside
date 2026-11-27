// RS0026: both read entry points end in a defaulted cancellation token, the core's rule for asynchronous members.
#pragma warning disable RS0026
using Broadside.Annotations;

namespace Broadside.Viewer;

/// <summary>
/// The files a document carries, as an attachments panel lists them: the entries of the <c>EmbeddedFiles</c> name tree, then the
/// file attachment annotations of every page. A view over the document: each item keeps its file specification, and
/// <see cref="ExtractAsync"/> decodes the data only when asked.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.11.3 (file specifications), §7.11.4 (embedded file streams), §7.7.4 (the <c>EmbeddedFiles</c> tree) and §12.5.6.15
/// (file attachment annotations). The viewer never opens or launches a file: a shell hands the bytes to the application
/// (<see cref="AttachmentOpeningEventArgs"/>), which decides what to do with them.
/// </para>
/// <para>
/// Lenient: a broken file specification, a missing <c>EF</c> stream or a bad checksum still lists the item, with
/// <see cref="AttachmentItem.IsEmbedded"/> and <see cref="AttachmentItem.CheckSum"/> saying so; the diagnostic comes from the library.
/// A remote document whose parts could not be read lists what it could and reports <see cref="IsComplete"/> as
/// <see langword="false"/>. The reads run through the session's worker reads.
/// </para>
/// </remarks>
public sealed class DocumentAttachments
{
    private readonly DocumentSession _session;

    private DocumentAttachments(DocumentSession session, IReadOnlyList<AttachmentItem> items, bool complete)
    {
        _session = session;
        Items = items;
        IsComplete = complete;
    }

    /// <summary>Gets the files: the embedded files in tree order, then the file attachment annotations in page order.</summary>
    public IReadOnlyList<AttachmentItem> Items { get; }

    /// <summary>Gets a value indicating whether every part of the document was read (a remote document may miss some).</summary>
    public bool IsComplete { get; }

    /// <summary>Gets a value indicating whether the document has no attachments.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>Reads a session's attachments; file attachment annotations list their page but no <see cref="AttachmentItem.Annotation"/>.</summary>
    /// <param name="session">The session.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The attachments.</returns>
    /// <remarks>ISO 32000-2 §7.11.4 and §12.5.6.15.</remarks>
    public static ValueTask<DocumentAttachments> ReadAsync(DocumentSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return ReadCoreAsync(session, null, cancellationToken);
    }

    /// <summary>Reads the attachments of a layer's document; file attachment annotations also give their <see cref="AttachmentItem.Annotation"/>.</summary>
    /// <param name="layer">The annotation layer of the open document; its pages are loaded.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The attachments.</returns>
    /// <remarks>ISO 32000-2 §7.11.4 and §12.5.6.15.</remarks>
    public static ValueTask<DocumentAttachments> ReadAsync(AnnotationLayer layer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return ReadCoreAsync(layer.Session, layer, cancellationToken);
    }

    /// <summary>Decodes an attachment's data.</summary>
    /// <param name="item">One of <see cref="Items"/>.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The decoded file, empty when the item has no embedded data (<see cref="AttachmentItem.IsEmbedded"/>).</returns>
    /// <remarks>ISO 32000-2 §7.11.4.1: the stream's filters are applied.</remarks>
    public async ValueTask<ReadOnlyMemory<byte>> ExtractAsync(AttachmentItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Specification is not { } specification)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        return await _session.ReadAsync(_ => specification.EmbeddedFile?.Decode() ?? ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(true);
    }

    private static async ValueTask<DocumentAttachments> ReadCoreAsync(DocumentSession session, AnnotationLayer? layer, CancellationToken cancellationToken)
    {
        var items = new List<AttachmentItem>();
        bool complete = true;
        try
        {
            items.AddRange(await session.ReadAsync(_ => ReadEmbeddedFiles(session.Document), cancellationToken).ConfigureAwait(true));
        }
        catch (PdfRangeNotLoadedException)
        {
            complete = false;
        }

        for (int pageIndex = 0; pageIndex < session.PageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                PdfPage page = await session.ReadPageAsync(pageIndex, cancellationToken).ConfigureAwait(true);
                int index = pageIndex;
                List<(AttachmentItem Item, PdfAnnotation Annotation)> found =
                    await session.ReadAsync(_ => ReadAnnotations(page, index), cancellationToken).ConfigureAwait(true);
                if (found.Count == 0)
                {
                    continue;
                }

                if (layer is not null)
                {
                    await layer.ReadPageAsync(pageIndex, cancellationToken).ConfigureAwait(true);
                }

                foreach ((AttachmentItem item, PdfAnnotation annotation) in found)
                {
                    items.Add(layer?.FindTarget(pageIndex, annotation.Dictionary) is { } target ? item with { Annotation = target } : item);
                }
            }
            catch (PdfRangeNotLoadedException)
            {
                complete = false;
            }
        }

        return new DocumentAttachments(session, items, complete);
    }

    private static List<AttachmentItem> ReadEmbeddedFiles(PdfDocument document)
    {
        var items = new List<AttachmentItem>();
        foreach (PdfEmbeddedFileEntry entry in document.EmbeddedFiles)
        {
            items.Add(Describe(entry.File, entry.File.SafeFileName ?? entry.FileName, pageIndex: null));
        }

        return items;
    }

    private static List<(AttachmentItem, PdfAnnotation)> ReadAnnotations(PdfPage page, int pageIndex)
    {
        var found = new List<(AttachmentItem, PdfAnnotation)>();
        foreach (PdfAnnotation annotation in page.Annotations)
        {
            if (annotation is PdfFileAttachmentAnnotation attachment)
            {
                AttachmentItem item = attachment.FileSpecification is { } specification
                    ? Describe(specification, specification.SafeFileName ?? "attachment", pageIndex)
                    : new AttachmentItem("attachment", attachment.Contents, null, null, pageIndex, null, PdfCheckSumStatus.Absent);
                found.Add((item, annotation));
            }
        }

        return found;
    }

    private static AttachmentItem Describe(PdfFileSpecification specification, string fileName, int? pageIndex)
    {
        PdfEmbeddedFile? file = null;
        long? size = null;
        PdfCheckSumStatus checkSum = PdfCheckSumStatus.Absent;
        try
        {
            file = specification.EmbeddedFile;
            size = file?.Parameters?.Size;
            checkSum = file?.VerifyCheckSum() ?? PdfCheckSumStatus.Absent;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or PdfRangeNotLoadedException))
        {
            // A broken stream lists as it is; the library recorded its diagnostic (or threw it in strict mode).
        }

        return new AttachmentItem(fileName, specification.Description, file?.Subtype, size, pageIndex, null, checkSum)
        {
            Specification = specification,
            IsEmbedded = file is not null,
        };
    }
}

/// <summary>One file of a document as an attachments panel lists it.</summary>
/// <param name="FileName">The file's name, made safe to save under (<see cref="PdfFileSpecification.SafeFileName"/>).</param>
/// <param name="Description">The file specification's description (<c>Desc</c>).</param>
/// <param name="MediaType">The embedded file's media type (<c>Subtype</c>), or <see langword="null"/> when it records none.</param>
/// <param name="Size">The uncompressed size its parameters record (<c>Params Size</c>).</param>
/// <param name="PageIndex">The page of a file attachment annotation, or <see langword="null"/> for an entry of <c>EmbeddedFiles</c>.</param>
/// <param name="Annotation">The file attachment annotation in the <see cref="AnnotationLayer"/>, when it was read with one.</param>
/// <param name="CheckSum">Whether the data matches its recorded MD5 checksum.</param>
/// <remarks>ISO 32000-2 §7.11.3 (Table 43), §7.11.4.1 (Tables 44 and 45) and §12.5.6.15.</remarks>
public sealed record AttachmentItem(
    string FileName,
    string? Description,
    string? MediaType,
    long? Size,
    int? PageIndex,
    AnnotationTarget? Annotation,
    PdfCheckSumStatus CheckSum)
{
    /// <summary>Gets the file specification the item views, or <see langword="null"/> when the annotation has none.</summary>
    public PdfFileSpecification? Specification { get; init; }

    /// <summary>Gets a value indicating whether the file's data is in the document (an <c>EF</c> stream); otherwise it only names a file.</summary>
    public bool IsEmbedded { get; init; }
}

/// <summary>What an attachments panel asks the application to do with a file.</summary>
public enum AttachmentAction
{
    /// <summary>Open the file (the viewer never launches it).</summary>
    Open,

    /// <summary>Save the file somewhere.</summary>
    Save,
}

/// <summary>Raised by a shell's attachments panel with an attachment's bytes: the application opens or saves them, or ignores them.</summary>
/// <remarks>ISO 32000-2 §7.11.4. The data is the decoded file; nothing has been written or launched.</remarks>
public sealed class AttachmentOpeningEventArgs : EventArgs
{
    /// <summary>Initializes a new instance of the <see cref="AttachmentOpeningEventArgs"/> class.</summary>
    /// <param name="item">The attachment.</param>
    /// <param name="data">Its decoded data.</param>
    /// <param name="action">Whether the user asked to open or to save it.</param>
    public AttachmentOpeningEventArgs(AttachmentItem item, ReadOnlyMemory<byte> data, AttachmentAction action)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        Data = data;
        Action = action;
    }

    /// <summary>Gets the attachment.</summary>
    public AttachmentItem Item { get; }

    /// <summary>Gets its decoded data.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets whether the user asked to open or to save it.</summary>
    public AttachmentAction Action { get; }
}
