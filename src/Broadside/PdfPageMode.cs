namespace Broadside;

/// <summary>Which panel a viewer shows next to the pages: the catalog's <c>PageMode</c> entry and the viewer preference <c>NonFullScreenPageMode</c>.</summary>
/// <remarks>ISO 32000-2 §7.7.2, Table 29, and §12.2, Table 147. The default is <see cref="UseNone"/>.</remarks>
public enum PdfPageMode
{
    /// <summary>Neither the outline nor thumbnails (<c>UseNone</c>, the default).</summary>
    UseNone,

    /// <summary>The document outline (<c>UseOutlines</c>).</summary>
    UseOutlines,

    /// <summary>Thumbnail images (<c>UseThumbs</c>).</summary>
    UseThumbs,

    /// <summary>Full-screen mode, no menu bar, window controls or other windows (<c>FullScreen</c>; not a valid <c>NonFullScreenPageMode</c>).</summary>
    FullScreen,

    /// <summary>The optional content group panel (<c>UseOC</c>, PDF 1.5).</summary>
    UseOC,

    /// <summary>The attachments panel (<c>UseAttachments</c>, PDF 1.6; not a valid <c>NonFullScreenPageMode</c>).</summary>
    UseAttachments,
}
