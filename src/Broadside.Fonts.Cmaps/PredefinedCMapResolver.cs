using System.Collections.Frozen;
using System.IO.Compression;

namespace Broadside.Fonts.Cmaps;

/// <summary>
/// The Adobe predefined CMaps of ISO 32000-2 Table 116 and the CID-to-Unicode tables of the Adobe CJK character collections, as a
/// font resolver that supplies named resources only. Register it with <c>PdfOptions.UsePredefinedCMaps()</c>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.7.5.2 (Table 116): a Type 0 font's <c>Encoding</c>, or an embedded CMap's <c>UseCMap</c>, may name a predefined
/// CMap instead of embedding one. The core has Identity-H and Identity-V; this package has the other 59, from Adobe's
/// cmap-resources (BSD-3-Clause), and answers <see cref="FontResourceKind.CMap"/> with their names, exactly as Table 116 spells
/// them. It also answers <see cref="FontResourceKind.CidToUnicode"/> for <c>Adobe-CNS1-UCS2</c>, <c>Adobe-GB1-UCS2</c>,
/// <c>Adobe-Japan1-UCS2</c>, <c>Adobe-Korea1-UCS2</c> and <c>Adobe-KR-UCS2</c> (§9.10.2), from Adobe's mapping-resources-pdf
/// (BSD-3-Clause). Every other request, and every font, is left to the next resolver (ADR 0009).
/// </para>
/// <para>
/// The files are the upstream CMap files, byte for byte, stored Deflate-compressed as assembly resources; each is inflated on its
/// first request and the bytes are shared by every engine and thread for the life of the process. The core parses them with its
/// own CMap parser (Adobe TN 5014) and caches the parsed CMap per document.
/// </para>
/// </remarks>
public sealed class PredefinedCMapResolver : IFontResolver
{
    private const string CMapPrefix = "Broadside.Fonts.Cmaps.CMap.";
    private const string CidToUnicodePrefix = "Broadside.Fonts.Cmaps.CidToUnicode.";

    private static readonly FrozenDictionary<string, Lazy<byte[]>> CMaps = Index(CMapPrefix);
    private static readonly FrozenDictionary<string, Lazy<byte[]>> CidToUnicodeTables = Index(CidToUnicodePrefix);

    /// <inheritdoc/>
    /// <remarks>
    /// ISO 32000-2 §9.7.5.2 (Table 116) and §9.10.2. Names are matched exactly (ordinal, case-sensitive); the data is the text of
    /// the upstream CMap file (Adobe TN 5014 syntax) and stays valid and unchanged for the life of the process.
    /// </remarks>
    public bool TryResolveResource(FontResourceKind kind, string name, out ReadOnlyMemory<byte> data)
    {
        ArgumentNullException.ThrowIfNull(name);
        FrozenDictionary<string, Lazy<byte[]>>? files = kind switch
        {
            FontResourceKind.CMap => CMaps,
            FontResourceKind.CidToUnicode => CidToUnicodeTables,
            _ => null,
        };
        if (files is not null && files.TryGetValue(name, out Lazy<byte[]>? file))
        {
            data = file.Value;
            return true;
        }

        data = default;
        return false;
    }

    private static FrozenDictionary<string, Lazy<byte[]>> Index(string prefix) =>
        typeof(PredefinedCMapResolver).Assembly.GetManifestResourceNames()
            .Where(resource => resource.StartsWith(prefix, StringComparison.Ordinal))
            .ToFrozenDictionary(
                resource => resource[prefix.Length..],
                resource => new Lazy<byte[]>(() => Inflate(resource), LazyThreadSafetyMode.ExecutionAndPublication),
                StringComparer.Ordinal);

    private static byte[] Inflate(string resource)
    {
        using Stream stream = typeof(PredefinedCMapResolver).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The predefined CMaps package is missing its resource {resource}.");
        using var inflater = new DeflateStream(stream, CompressionMode.Decompress);
        using var buffer = new MemoryStream(capacity: (int)Math.Min(stream.Length * 4, int.MaxValue));
        inflater.CopyTo(buffer);
        return buffer.ToArray();
    }
}
