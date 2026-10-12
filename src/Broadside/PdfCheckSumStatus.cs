namespace Broadside;

/// <summary>The outcome of comparing an embedded file's data with the <c>CheckSum</c> its parameters record.</summary>
/// <remarks>ISO 32000-2 §7.11.4.1, Table 45: <c>CheckSum</c> is the 16-byte MD5 digest (RFC 1321) of the decoded file data.</remarks>
public enum PdfCheckSumStatus
{
    /// <summary>The file records no checksum, or it is not a 16-byte string.</summary>
    Absent,

    /// <summary>The MD5 digest of the decoded data equals the recorded checksum.</summary>
    Matches,

    /// <summary>The MD5 digest of the decoded data differs from the recorded checksum.</summary>
    DoesNotMatch,
}
