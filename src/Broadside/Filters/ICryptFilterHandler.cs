using System.Buffers;
using Broadside.Objects;

namespace Broadside.Filters;

/// <summary>
/// The slot the pipeline decodes a <c>Crypt</c> filter through (ISO 32000-2 §7.4.10). Issue #42 supplies the security handler's
/// implementation, set on <see cref="StreamDecoder.CryptFilter"/> when the document's <c>Encrypt</c> dictionary is read; until then
/// <see cref="IdentityCryptFilterHandler"/> decodes only the <c>Identity</c> crypt filter.
/// </summary>
/// <remarks>
/// The standard implementation (issue #42, <c>DocumentDecryptor</c>) decrypts a stream whose chain starts with <c>Crypt</c> when the
/// stream's object loads (it needs the object number for Algorithm 1), with the named crypt filter; this stage then passes the data
/// of a crypt filter the document defines through and returns <see langword="false"/> for an unknown one.
/// </remarks>
internal interface ICryptFilterHandler
{
    /// <summary>Decrypts <paramref name="data"/> with the crypt filter named <paramref name="cryptFilterName"/>.</summary>
    /// <param name="cryptFilterName">The <c>Name</c> parameter, <c>Identity</c> when absent (Table 14).</param>
    /// <param name="data">The stream's data.</param>
    /// <param name="output">Where to write the decrypted data.</param>
    /// <param name="context">The Crypt filter's context; its parameters may hold entries private to the security handler.</param>
    /// <returns><see langword="false"/> when no crypt filter of that name is known; nothing was written.</returns>
    bool TryDecrypt(CosName cryptFilterName, ReadOnlySpan<byte> data, IBufferWriter<byte> output, FilterContext context);

    /// <summary>
    /// Whether the stream's data is encrypted with a crypt filter the credentials do not unlock (§7.6.6; §7.6.5 Table 25, an
    /// <c>AuthEvent /EFOpen</c> filter when the document opened without the user password): the pipeline then leaves the data as it
    /// is and runs none of its filters, and the security handler has said why.
    /// </summary>
    /// <param name="streamDictionary">The stream's dictionary.</param>
    /// <returns><see langword="true"/> when the stream stays encrypted.</returns>
    bool IsLocked(CosDictionary streamDictionary);
}

/// <summary>The <see cref="ICryptFilterHandler"/> of an unencrypted document: only <c>Identity</c>, which copies the data (§7.6.6 Table 26).</summary>
internal sealed class IdentityCryptFilterHandler : ICryptFilterHandler
{
    /// <summary>Gets the shared instance; it holds no state.</summary>
    public static IdentityCryptFilterHandler Instance { get; } = new();

    /// <inheritdoc/>
    public bool TryDecrypt(CosName cryptFilterName, ReadOnlySpan<byte> data, IBufferWriter<byte> output, FilterContext context)
    {
        if (!cryptFilterName.Equals(FilterNames.Identity))
        {
            return false;
        }

        output.Write(data);
        return true;
    }

    /// <inheritdoc/>
    public bool IsLocked(CosDictionary streamDictionary) => false;
}
