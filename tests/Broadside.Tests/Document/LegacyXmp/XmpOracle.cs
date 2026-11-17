using System.Text;
using System.Text.RegularExpressions;
using Broadside.Diagnostics;
using Broadside.Objects;

namespace Broadside.Tests.Document.LegacyXmp;

/// <summary>
/// Compares the XMP reader with the reader it replaced (issue #545), which read the XML with the BCL's <c>XmlReader</c> and is kept
/// here, in the test project only, as the oracle: the same bytes must read to the same packet (every property, item, field and
/// qualifier, with its kind, value, language and URI flag, and <c>rdf:about</c>) with the same diagnostics.
/// </summary>
/// <remarks>
/// Diagnostics are compared by code, severity and message. The one message that cannot match word for word is the detail of
/// <c>XmpMalformed</c> when the XML is not well-formed: the old message quoted the BCL's exception text, the new one its own
/// reason; both are replaced by <c>(...)</c> before comparing. ISO 16684-1 §7; ISO 32000-2 §14.3.2.
/// </remarks>
internal static partial class XmpOracle
{
    private static readonly CosName TypeKey = new("Type");
    private static readonly CosName SubtypeKey = new("Subtype");
    private static readonly CosName SizeKey = new("Size");
    private static readonly CosName MetadataType = new("Metadata");
    private static readonly CosName XmlSubtype = new("XML");

    /// <summary>Describes what the new reader reads from <paramref name="data"/>.</summary>
    public static string Current(ReadOnlySpan<byte> data) => Describe(data, legacy: false);

    /// <summary>Describes what the old, <c>System.Xml</c>-based reader reads from <paramref name="data"/>.</summary>
    public static string Legacy(ReadOnlySpan<byte> data) => Describe(data, legacy: true);

    /// <summary>Returns <see langword="null"/> when both readers agree, else both descriptions.</summary>
    public static string? Difference(ReadOnlySpan<byte> data)
    {
        string legacy = Legacy(data);
        string current = Current(data);
        return legacy == current ? null : $"--- legacy\n{legacy}\n--- current\n{current}";
    }

    /// <summary>
    /// The decoded data of every metadata stream of a PDF (every object whose dictionary has <c>Type</c> <c>Metadata</c> or
    /// <c>Subtype</c> <c>XML</c>), read leniently; nothing when the file does not open. Objects that fail to resolve or decode are
    /// skipped.
    /// </summary>
    public static List<byte[]> MetadataPackets(byte[] file)
    {
        var packets = new List<byte[]>();
        PdfDocument? document = null;
        try
        {
            document = PdfDocument.Open(file);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            document?.Dispose();
            return packets;
        }

        using (document)
        {
            long size;
            try
            {
                size = document.Trailer.TryGetValue(SizeKey, out CosObject? entry) && entry is CosInteger integer ? integer.Value : 0;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return packets;
            }

            for (int number = 1; number < Math.Min(size, 1_000_000); number++)
            {
                try
                {
                    if (document.Resolve(new CosReference(number, 0)) is CosStream stream
                        && ((stream.Dictionary.TryGetValue(TypeKey, out CosObject? type) && type is CosName typeName && typeName.Equals(MetadataType))
                            || (stream.Dictionary.TryGetValue(SubtypeKey, out CosObject? subtype) && subtype is CosName subtypeName && subtypeName.Equals(XmlSubtype))))
                    {
                        packets.Add(document.DecodeStream(stream).ToArray());
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // A damaged object: the corpus gates cover it; here only packets matter.
                }
            }
        }

        return packets;
    }

    private static string Describe(ReadOnlySpan<byte> data, bool legacy)
    {
        var diagnostics = new List<string>();
        void Report(DiagnosticCode code, DiagnosticSeverity severity, string message) =>
            diagnostics.Add($"{code.Name}|{severity}|{NotWellFormed().Replace(message, "$1(...)$2")}");

        XmpPacket? packet = legacy ? LegacyXmpPacketReader.Read(data, Report) : XmpPacketReader.Read(data, Report);
        var builder = new StringBuilder();
        _ = builder.Append(packet is null ? "null" : $"about={packet.About ?? "<null>"}").Append('\n');
        foreach (XmpProperty property in packet?.Properties ?? [])
        {
            Append(builder, property, 0);
        }

        foreach (string diagnostic in diagnostics)
        {
            _ = builder.Append("! ").Append(diagnostic).Append('\n');
        }

        return builder.ToString();
    }

    private static void Append(StringBuilder builder, XmpProperty property, int depth)
    {
        _ = builder.Append(' ', depth * 2)
            .Append($"{{{property.NamespaceName}}}{property.Name} {property.Kind} uri={property.IsUri} lang={property.Language ?? "<null>"}")
            .Append(property.Value is null ? " value=<null>" : $" value=[{Escape(property.Value)}]")
            .Append('\n');
        foreach ((string label, IReadOnlyList<XmpProperty> children) in new[] { ("items", property.Items), ("fields", property.Fields), ("qualifiers", property.Qualifiers) })
        {
            if (children.Count > 0)
            {
                _ = builder.Append(' ', (depth * 2) + 1).Append(label).Append('\n');
                foreach (XmpProperty child in children)
                {
                    Append(builder, child, depth + 1);
                }
            }
        }
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            _ = c is < ' ' or (>= '\u007F' and <= '\u00A0') or (>= '\uD800' and <= '\uDFFF') ? builder.Append($"\\u{(int)c:X4}") : builder.Append(c);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^(The XMP packet is not well-formed XML )\(.*\)(; the packet is ignored\.)$", RegexOptions.Singleline)]
    private static partial Regex NotWellFormed();
}
