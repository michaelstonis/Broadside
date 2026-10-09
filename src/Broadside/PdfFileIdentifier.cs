namespace Broadside;

/// <summary>The file identifier: the two byte strings of the trailer's <c>ID</c> entry.</summary>
/// <remarks>
/// ISO 32000-2 §14.4 and §7.5.5, Table 15. The first identifier is permanent, set when the file is first written; the second changes
/// with every revision. Each "shall have a minimum length of 16 bytes"; shorter ones (some producers write 8) are kept as they are,
/// with a <c>FileIdentifierInvalid</c> diagnostic. The ID is required in PDF 2.0 and in encrypted files, where it is never
/// encrypted. Read when <see cref="PdfDocument.FileIdentifier"/> is called; the bytes are a snapshot.
/// </remarks>
public sealed class PdfFileIdentifier
{
    internal PdfFileIdentifier(ReadOnlyMemory<byte> permanent, ReadOnlyMemory<byte> changing)
    {
        Permanent = permanent;
        Changing = changing;
    }

    /// <summary>Gets the permanent identifier, <c>ID[0]</c>, based on the file's contents when it was created.</summary>
    /// <remarks>ISO 32000-2 §14.4.</remarks>
    public ReadOnlyMemory<byte> Permanent { get; }

    /// <summary>Gets the changing identifier, <c>ID[1]</c>, based on the file's contents when it was last updated.</summary>
    /// <remarks>ISO 32000-2 §14.4.</remarks>
    public ReadOnlyMemory<byte> Changing { get; }

    /// <summary>Returns both identifiers in hexadecimal, as a trailer writes them.</summary>
    /// <returns>The text, such as <c>[&lt;0123...&gt; &lt;4567...&gt;]</c>.</returns>
    public override string ToString() => $"[<{Convert.ToHexString(Permanent.Span)}> <{Convert.ToHexString(Changing.Span)}>]";
}
