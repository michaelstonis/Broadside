using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.Viewer.Maui;

namespace Broadside.Viewer.Maui.Sample;

/// <summary>
/// One gallery entry: an app asset (or an https address, read through range requests when opened), what the gallery calls it, what
/// to tell the user when it opens, and the panel that shows its feature (opened with it).
/// </summary>
public sealed record GalleryEntry(string File, string Title, string? Note = null, SamplePanel? Panel = null);

/// <summary>The sample's panels: the bottom sheet on a phone, the sidebar on a tablet or desktop.</summary>
public enum SamplePanel
{
    None,
    Open,
    Search,
    Outline,
    Pages,
    Annotate,
    Form,
    Info,
    Signatures,
    View,
    Create,
}

/// <summary>
/// The sample's open document: a <see cref="DocumentSession"/> the app owns and hands to the <see cref="PdfView"/>, where it came from,
/// whether it was edited, and saving or sharing it with the edits. The shell and every panel follow <see cref="Changed"/>.
/// </summary>
public sealed class SampleDocument
{
    /// <summary>
    /// pdf.js's copy of the TraceMonkey paper: its ACM copyright notice does not allow redistribution, so the app does not bundle it
    /// and reads it through range requests when opened.
    /// </summary>
    public const string TraceMonkey = "https://mozilla.github.io/pdf.js/web/compressed.tracemonkey-pldi-09.pdf";

    /// <summary>The Blazor demo's gallery: the sample documents (#363) first, then TraceMonkey (remote), then the corpus files.</summary>
    public static readonly GalleryEntry[] Gallery =
    [
        new("documents/textbook.pdf", "Textbook chapter (WeasyPrint)"),
        new("documents/catalogue.pdf", "Product catalogue (WeasyPrint, CMYK and bleed)"),
        new("documents/newsletter.pdf", "Newsletter (Chrome)"),
        new("documents/paper.pdf", "Scientific paper with equations (Chrome)"),
        new("documents/invoice-colourful.pdf", "Invoice, colourful (WeasyPrint)"),
        new("documents/invoice-conservative-filled.pdf", "Invoice with a filled form (Broadside)", Panel: SamplePanel.Form),
        new("documents/brochure.pdf", "Brochure, print-ready (Broadside)"),
        new("documents/dictionary.pdf", "Dictionary (WeasyPrint)"),
        new("documents/showcase.pdf", "Feature showcase with layers", Panel: SamplePanel.Outline),
        new("documents/novel-pocket.pdf", "Novel, pocket edition (170 pages)"),
        new("documents/novel-hardback.pdf", "Novel, hardback edition (tagged)"),
        new("documents/novel-large-print.pdf", "Novel, large print (Broadside, PDF/UA)"),
        new("documents/contract-signed.pdf", "Agreement, certified and signed (Broadside)", Panel: SamplePanel.Signatures),
        new("documents/textbook-aes128.pdf", "Textbook, encrypted (AES-128, copying not allowed)"),
        new("documents/paper-rc4.pdf", "Paper, encrypted (RC4, printing not allowed)"),
        new("documents/dictionary-aes256-gcm.pdf", "Dictionary, encrypted (AES-256-GCM, PDF 2.0)"),
        new("documents/application-form.pdf", "Application form with calculated fields (Broadside)", Panel: SamplePanel.Form),
        new("documents/application-form-xfa.pdf", "Application form, XFA hybrid", Panel: SamplePanel.Form),
        new("documents/paper-reviewed.pdf", "Paper with a referee's annotations", Panel: SamplePanel.Annotate),
        new("documents/leaflet-multilingual.pdf", "Leaflet in twelve languages (WeasyPrint)"),
        new("documents/notice-trilingual.pdf", "Notice in English, Arabic and Hebrew (Broadside)"),
        new("documents/rashomon-vertical.pdf", "Japanese novella in vertical writing (Broadside)"),
        new("documents/report-1996.pdf", "Technical memo, 1996 style (Type 1, Type 3, LZW)"),
        new("documents/scan-memo-g4.pdf", "Scanned memo with OCR text (CCITT G4)"),
        new("documents/scan-memo-jbig2.pdf", "Scanned memo with OCR text (JBIG2)"),
        new("documents/scan-moon-memo-jpx.pdf", "Scanned memo in color with OCR text (JPEG 2000)"),
        new("documents/color-guide.pdf", "Color guide: Lab, Indexed, DeviceN, overprint (Broadside)"),
        new("documents/shakespeare.pdf", "Complete works of Shakespeare (1,168 pages)"),
        new("documents/contour-map.pdf", "Topographic map: 400,000 contour segments on one A1 page"),
        new("documents/board-pack.pdf", "Board pack: mixed sizes and rotated pages"),
        new("documents/portfolio.pdf", "Project portfolio with attached files"),
        new("documents/invoice-pdfa3.pdf", "Invoice, PDF/A-3b with its XML data"),
        new("documents/paper-pdfa2b.pdf", "Paper, PDF/A-2b"),
        new("documents/showcase-broken-xref.pdf", "Damaged: broken cross-reference offsets"),
        new("documents/novel-large-print-truncated.pdf", "Damaged: interrupted download (157 of 219 pages)"),
        new(TraceMonkey, "TraceMonkey paper (real world, range requests)"),
        new("documents/layout-two-column-tagged.pdf", "Two columns (tagged)"),
        new("documents/outline-full.pdf", "Outline", "This file has an outline and no page content, so its page is blank (as in every viewer): its bookmarks are in the Outline panel.", SamplePanel.Outline),
        new("documents/acroform-fields.pdf", "Form fields", Panel: SamplePanel.Form),
        new("documents/tagged-structure.pdf", "Tagged structure", Panel: SamplePanel.Info),
        new("documents/layout-tables.pdf", "Tables"),
        new("documents/signed.pdf", "Signed document", Panel: SamplePanel.Signatures),
        new("documents/info-dictionary.pdf", "Document information", Panel: SamplePanel.Info),
        new("documents/annotations-link.pdf", "Links", "This page has no content, only a link near its top left: tap it (the link is listed in the Annotate panel).", SamplePanel.Annotate),
        new("documents/shading-radial-cases.pdf", "Shadings", "The cone in the middle of the second row is cut flat at top and bottom by its cell's clip, as in other renderers."),
        new("documents/transparency-blend-modes.pdf", "Transparency"),
        new("documents/page-rotation.pdf", "Rotated pages"),
        new("documents/linearized.pdf", "Linearized"),
    ];

    private static readonly HttpClient Http = new();
    private CancellationTokenSource _work = new();

    /// <summary>Raised on the UI thread after any change the shell shows.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets or sets whether content tiles are drawn with SkiaSharp (default) or Broadside's managed software rasterizer.</summary>
    public static bool UseSkia { get; set; } = true;

    /// <summary>
    /// The options every document opens with: the Standard 14 fonts are bundled (documents that do not embed Helvetica, Times or
    /// Courier render like a desktop viewer), and tiles draw with the renderer <see cref="UseSkia"/> selects.
    /// </summary>
    public static ViewerOptions Options { get; } = new ViewerOptions()
        .UseEngine(new PdfEngine(new PdfOptions().UseStandard14Fonts()))
        .UseBackend(static () => new SampleTileBackend());

    /// <summary>Gets the open document's session, or null.</summary>
    public DocumentSession? Session { get; private set; }

    /// <summary>Gets the open document's file name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Gets the gallery file or address the document came from, or null for a picked or created file.</summary>
    public string? Address { get; private set; }

    /// <summary>Gets whether the document is read through range requests (not all of it is in memory).</summary>
    public bool IsRemote { get; private set; }

    /// <summary>Gets whether the document was changed since it was opened.</summary>
    public bool IsEdited { get; private set; }

    /// <summary>Gets whether a document is being opened.</summary>
    public bool IsOpening { get; private set; }

    /// <summary>Gets the last message for the user, or null.</summary>
    public string? Message { get; private set; }

    /// <summary>Gets or sets the panel the shell should open for the document just opened (taken and cleared by the shell).</summary>
    public SamplePanel? SuggestedPanel { get; set; }

    /// <summary>Opens a gallery document: an app asset read whole, or an address through range requests.</summary>
    public async Task OpenGalleryAsync(GalleryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.File.StartsWith("https://", StringComparison.Ordinal))
        {
            await OpenUrlAsync(entry.File).ConfigureAwait(true);
        }
        else
        {
            await RunOpenAsync(entry.File, false, async token =>
            {
                await using Stream asset = await FileSystem.OpenAppPackageFileAsync(entry.File).ConfigureAwait(true);
                using var buffer = new MemoryStream();
                await asset.CopyToAsync(buffer, token).ConfigureAwait(true);
                return (DocumentSession.Open(buffer.ToArray(), Options), FileName(entry.File));
            }).ConfigureAwait(true);
        }

        if (Address == entry.File && Message is null)
        {
            SuggestedPanel = entry.Panel;
            Report(entry.Note);
        }
    }

    /// <summary>Opens a document from an http(s) address through range requests: page 1 of a linearized file shows before the rest arrives.</summary>
    public Task OpenUrlAsync(string address) => RunOpenAsync(address, true, async token =>
    {
        var uri = new Uri(address, UriKind.Absolute);
        DocumentSession session = await DocumentSession.OpenAsync(Http, uri, Options, token).ConfigureAwait(true);
        return (session, FileName(uri.AbsolutePath));
    });

    /// <summary>Opens a document from bytes (a picked file, a created document).</summary>
    public Task OpenBytesAsync(string name, byte[] bytes) =>
        RunOpenAsync(null, false, _ => Task.FromResult((DocumentSession.Open(bytes, Options), name)));

    /// <summary>Records an edit of the open document.</summary>
    public void MarkEdited()
    {
        IsEdited = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves the open document with every edit; null (with a message) when it cannot be saved.</summary>
    public async Task<byte[]?> SaveAsync()
    {
        if (Session?.Document is not { } document)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream();
            await document.SaveAsync(stream, new PdfSaveOptions(), _work.Token).ConfigureAwait(true);
            return stream.ToArray();
        }
        catch (PdfRangeNotLoadedException)
        {
            Report("Saving needs the whole file: this one is read through range requests and not all of it has arrived.");
        }
#pragma warning disable CA1031 // shown to the user
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Report("The document could not be saved: " + exception.Message);
        }

        return null;
    }

    /// <summary>Saves the document with its edits into the app's cache folder and offers it to the platform's share sheet (which can also save it to Files).</summary>
    public async Task ShareAsync()
    {
        if (await SaveAsync().ConfigureAwait(true) is not { } bytes)
        {
            return;
        }

        string path = Path.Combine(FileSystem.CacheDirectory, Name.Length == 0 ? "document.pdf" : Name);
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(true);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = Name, File = new ShareFile(path, "application/pdf") }).ConfigureAwait(true);
    }

    /// <summary>Shows a message (or clears it with null).</summary>
    public void Report(string? message)
    {
        Message = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunOpenAsync(string? address, bool remote, Func<CancellationToken, Task<(DocumentSession Session, string Name)>> open)
    {
        await _work.CancelAsync().ConfigureAwait(true);
        _work.Dispose();
        _work = new CancellationTokenSource();
        CancellationToken token = _work.Token;
        IsOpening = true;
        Message = null;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            (DocumentSession session, string name) = await open(token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                await session.DisposeAsync().ConfigureAwait(true);
                return;
            }

            DocumentSession? previous = Session;
            Session = session;
            Name = name;
            Address = address;
            IsRemote = remote;
            IsEdited = false;
            Changed?.Invoke(this, EventArgs.Empty);

            // The view moves to the new session in the Changed handler; the old one closes after that.
            if (previous is not null)
            {
                await previous.DisposeAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
#pragma warning disable CA1031 // shown to the user
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Message = "The document could not be opened: " + exception.Message;
        }
        finally
        {
            IsOpening = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string FileName(string path)
    {
        string name = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
        return name.Length == 0 ? "document.pdf" : name;
    }
}

/// <summary>
/// Draws content tiles with SkiaSharp or with Broadside's managed software rasterizer, as <see cref="SampleDocument.UseSkia"/> says when
/// each tile starts; the View panel's renderer switch flips it and redraws.
/// </summary>
public sealed class SampleTileBackend : ITileBackend
{
    private readonly SkiaTileBackend _skia = new();
    private readonly SoftwareTileBackend _software = new();

    /// <inheritdoc/>
    public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, CancellationToken cancellationToken) =>
        SampleDocument.UseSkia ? _skia.Begin(list, target, deviceTransform, cancellationToken) : _software.Begin(list, target, deviceTransform, cancellationToken);

    /// <inheritdoc/>
    public ITileRender Begin(DisplayList list, Bitmap target, in Matrix deviceTransform, ColorFilter colorFilter, CancellationToken cancellationToken) =>
        SampleDocument.UseSkia
            ? ((ITileBackend)_skia).Begin(list, target, deviceTransform, colorFilter, cancellationToken)
            : _software.Begin(list, target, deviceTransform, colorFilter, cancellationToken);

    /// <inheritdoc/>
    public ValueTask PrefetchImagesAsync(DisplayList list, in Matrix deviceTransform, int width, int height, CancellationToken cancellationToken) =>
        SampleDocument.UseSkia
            ? _skia.PrefetchImagesAsync(list, deviceTransform, width, height, cancellationToken)
            : _software.PrefetchImagesAsync(list, deviceTransform, width, height, cancellationToken);

    /// <inheritdoc/>
    public void Dispose()
    {
        _skia.Dispose();
        _software.Dispose();
    }
}
