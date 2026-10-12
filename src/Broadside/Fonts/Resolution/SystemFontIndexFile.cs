using System.Text;

namespace Broadside.Fonts.Resolution;

/// <summary>
/// The stored form of a system font index: for each font file read so far, its path, length and last write time, and the faces
/// found in it. A file whose length or time no longer matches is read again; the store is only ever a cache of the scan.
/// </summary>
/// <remarks>
/// <para>
/// Layout (little-endian, strings as <see cref="BinaryWriter"/> writes them, UTF-8 with a 7-bit encoded length): the magic
/// <c>BSFI</c>, a format version, the directories the index covers, then the files, each with its faces' scan facts. A file of
/// another version, of other directories, or that does not read cleanly is ignored as a whole.
/// </para>
/// <para>
/// The store lives where the caller says and anyone with access there could change it, so it is read as untrusted data: every
/// count is capped, every string is checked, and nothing read throws.
/// </para>
/// </remarks>
internal static class SystemFontIndexFile
{
    /// <summary>The format version; bump it whenever the layout or the scan's facts change.</summary>
    private const int Version = 1;

    /// <summary>The largest store read.</summary>
    private const int MaxLength = 64 << 20;

    private const int MaxDirectories = 1024;
    private const int MaxFiles = 100_000;
    private const int MaxFaces = 256;

    private static ReadOnlySpan<byte> Magic => "BSFI"u8;

    /// <summary>Reads a store from its bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="directories">The directories the index must cover, in order.</param>
    /// <returns>The stored files by path, or <see langword="null"/> when the store is not one for these directories or does not read cleanly.</returns>
    public static Dictionary<string, StoredFontFile>? Read(ReadOnlySpan<byte> data, IReadOnlyList<string> directories)
    {
        if (data.Length < Magic.Length + 4 || data.Length > MaxLength || !data.StartsWith(Magic))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(data[Magic.Length..].ToArray(), writable: false);
            using var reader = new BinaryReader(stream, new UTF8Encoding(false, throwOnInvalidBytes: true));
            if (reader.ReadInt32() != Version)
            {
                return null;
            }

            int directoryCount = Count(reader, MaxDirectories);
            if (directoryCount != directories.Count)
            {
                return null;
            }

            for (int index = 0; index < directoryCount; index++)
            {
                if (!string.Equals(reader.ReadString(), directories[index], StringComparison.Ordinal))
                {
                    return null;
                }
            }

            int fileCount = Count(reader, MaxFiles);
            var files = new Dictionary<string, StoredFontFile>(fileCount, StringComparer.Ordinal);
            for (int index = 0; index < fileCount; index++)
            {
                string path = reader.ReadString();
                long length = reader.ReadInt64();
                long writeTime = reader.ReadInt64();
                int faceCount = Count(reader, MaxFaces);
                var faces = new SystemFontFace[faceCount];
                for (int face = 0; face < faceCount; face++)
                {
                    faces[face] = ReadFace(reader, path);
                }

                if (path.Length == 0 || !files.TryAdd(path, new StoredFontFile(length, writeTime, faces)))
                {
                    return null;
                }
            }

            return stream.Position == stream.Length ? files : null;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or FormatException or DecoderFallbackException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Writes a store.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="directories">The directories the index covers, in order.</param>
    /// <param name="files">The files read, in scan order, with their faces.</param>
    public static void Write(Stream output, IReadOnlyList<string> directories, IEnumerable<(FontFileEntry File, SystemFontFace[] Faces)> files)
    {
        output.Write(Magic);
        using var writer = new BinaryWriter(output, new UTF8Encoding(false), leaveOpen: true);
        writer.Write(Version);
        writer.Write(directories.Count);
        foreach (string directory in directories)
        {
            writer.Write(directory);
        }

        List<(FontFileEntry File, SystemFontFace[] Faces)> list = [.. files];
        writer.Write(list.Count);
        foreach ((FontFileEntry file, SystemFontFace[] faces) in list)
        {
            writer.Write(file.Path);
            writer.Write(file.Length);
            writer.Write(file.WriteTime);
            writer.Write(faces.Length);
            foreach (SystemFontFace face in faces)
            {
                writer.Write(face.FaceIndex);
                writer.Write(face.PostScriptName);
                writer.Write(face.Family);
                WriteOptional(writer, face.LegacyFamily);
                WriteOptional(writer, face.Subfamily);
                writer.Write(face.Weight);
                writer.Write(face.WidthClass);
                writer.Write((byte)((face.IsBold ? 1 : 0) | (face.IsItalic ? 2 : 0) | (face.IsFixedPitch ? 4 : 0) | (face.HasSymbolCmap ? 8 : 0)));
            }
        }
    }

    private static SystemFontFace ReadFace(BinaryReader reader, string path)
    {
        int faceIndex = reader.ReadInt32();
        string postScriptName = reader.ReadString();
        string family = reader.ReadString();
        string? legacy = ReadOptional(reader);
        string? subfamily = ReadOptional(reader);
        int weight = reader.ReadInt32();
        int widthClass = reader.ReadInt32();
        byte flags = reader.ReadByte();
        if (faceIndex is < 0 or >= MaxFaces || postScriptName.Length == 0 || family.Length == 0 || weight is < 1 or > 1000 || (flags & ~0xF) != 0)
        {
            throw new InvalidDataException();
        }

        return new SystemFontFace(path, faceIndex, postScriptName, family, legacy, subfamily, weight, widthClass, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, (flags & 8) != 0);
    }

    private static int Count(BinaryReader reader, int max)
    {
        int count = reader.ReadInt32();
        return count is >= 0 && count <= max ? count : throw new InvalidDataException();
    }

    private static void WriteOptional(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            writer.Write(value);
        }
    }

    private static string? ReadOptional(BinaryReader reader) => reader.ReadBoolean() ? reader.ReadString() : null;
}

/// <summary>One font file as a stored index recorded it.</summary>
/// <param name="Length">Its length when it was read.</param>
/// <param name="WriteTime">Its last write time (UTC ticks) when it was read.</param>
/// <param name="Faces">The faces found in it.</param>
internal sealed record StoredFontFile(long Length, long WriteTime, SystemFontFace[] Faces);
