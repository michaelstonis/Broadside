namespace Broadside;

/// <summary>The outcome of checking an encrypted document's integrity MAC.</summary>
/// <remarks>ISO/TS 32004, Annex B.</remarks>
public enum PdfIntegrityStatus
{
    /// <summary>The document carries no PDF MAC token (and its permissions do not require one).</summary>
    None,

    /// <summary>The PDF MAC token is valid and covers the whole file.</summary>
    Verified,

    /// <summary>The PDF MAC token is invalid, does not match the file, or does not cover all of it; the diagnostics say why.</summary>
    Failed,

    /// <summary>A token is present or required but cannot be checked here (an unavailable digest, a location not supported).</summary>
    NotVerified,

    /// <summary>The permissions require a PDF MAC token and the document has none.</summary>
    Missing,
}
