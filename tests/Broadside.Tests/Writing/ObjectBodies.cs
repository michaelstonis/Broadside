using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broadside.Objects;

namespace Broadside.Tests.Writing;

/// <summary>
/// Finds the bytes of every indirect object in a file, independently of the library's reader, so a test can compare a saved file
/// with its source. An object's bytes are those from the first token after <c>N G obj</c> to the last byte before <c>endobj</c>,
/// white-space trimmed; for a member of an object stream (ISO 32000-2 §7.5.7), its slice of the decoded stream up to the next
/// member's offset, white-space trimmed. A top-level object defined twice (an incremental update) keeps its last definition.
/// </summary>
internal static partial class ObjectBodies
{
    private static readonly CosName ObjStm = new("ObjStm");
    private static readonly CosName Type = new("Type");
    private static readonly CosName N = new("N");
    private static readonly CosName First = new("First");

    /// <summary>Reads the bytes of every object in <paramref name="file"/>, keyed by object number and generation.</summary>
    public static Dictionary<(int Number, int Generation), byte[]> Read(byte[] file)
    {
        string text = Encoding.Latin1.GetString(file);
        var bodies = new Dictionary<(int, int), byte[]>();
        var objectStreams = new List<(int, int)>();
        int position = 0;
        while (Header().Match(text, position) is { Success: true } header)
        {
            int start = header.Index + header.Length;
            int streamAt = text.IndexOf("stream", start, StringComparison.Ordinal);
            int endobj = text.IndexOf("endobj", start, StringComparison.Ordinal);
            if (streamAt >= 0 && streamAt < endobj)
            {
                int endstream = text.IndexOf("endstream", streamAt, StringComparison.Ordinal);
                endobj = text.IndexOf("endobj", endstream, StringComparison.Ordinal);
            }

            var key = (int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(header.Groups[2].Value, CultureInfo.InvariantCulture));
            bodies[key] = Trim(file.AsSpan(start, endobj - start)).ToArray();
            if (ObjStmType().IsMatch(text.AsSpan(start, Math.Min(200, endobj - start))))
            {
                objectStreams.Add(key);
            }

            position = endobj + "endobj".Length;
        }

        if (objectStreams.Count > 0)
        {
            using PdfDocument document = PdfDocument.Open(file);
            foreach ((int number, int generation) in objectStreams)
            {
                if (document.Resolve(new CosReference(number, generation)) is not CosStream stream || !ObjStm.Equals(stream.Dictionary[Type]))
                {
                    continue;
                }

                byte[] data = document.DecodeStream(stream).ToArray();
                long count = ((CosInteger)stream.Dictionary[N]).Value;
                int first = (int)((CosInteger)stream.Dictionary[First]).Value;
                string[] pairs = Encoding.Latin1.GetString(data, 0, first).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var members = new List<(int Number, int Offset)>();
                for (int index = 0; index < count; index++)
                {
                    members.Add((int.Parse(pairs[2 * index], CultureInfo.InvariantCulture), first + int.Parse(pairs[(2 * index) + 1], CultureInfo.InvariantCulture)));
                }

                for (int index = 0; index < members.Count; index++)
                {
                    int end = index + 1 < members.Count ? members[index + 1].Offset : data.Length;
                    bodies.TryAdd((members[index].Number, 0), Trim(data.AsSpan(members[index].Offset, end - members[index].Offset)).ToArray());
                }
            }
        }

        return bodies;
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> bytes) => bytes.Trim("\0\t\n\f\r "u8);

    [GeneratedRegex(@"(?<![0-9])([0-9]+)[ \r\n]+([0-9]+)[ \r\n]+obj\b")]
    private static partial Regex Header();

    [GeneratedRegex(@"/Type\s*/ObjStm\b")]
    private static partial Regex ObjStmType();
}
