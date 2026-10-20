using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace Broadside.Tools.ApkAotCheck;

/// <summary>What the check found for one ABI of the APK.</summary>
/// <param name="Abi">The ABI folder under <c>lib/</c>, for example <c>arm64-v8a</c>.</param>
/// <param name="AssemblyCount">Assemblies in the ABI's assembly store.</param>
/// <param name="AotImageCount">AOT images (<c>libaot-*.so</c>) for the ABI.</param>
/// <param name="Problems">One line per mismatch; empty when the ABI is consistent.</param>
internal sealed record AbiReport(string Abi, int AssemblyCount, int AotImageCount, IReadOnlyList<string> Problems);

/// <summary>
/// Mono writes into every AOT image the MVID of the assembly it compiled and of each assembly that one references, and refuses
/// an image at startup when they disagree with the loaded assemblies (#366 aborted instead, in class layout). This reads each
/// ABI's assembly store from the APK, takes every assembly's MVID, and requires that <c>libaot-X.so</c> holds X's MVID and no
/// MVID of an assembly the APK does not ship.
/// </summary>
internal static partial class ApkAotChecker
{
    private const string AotImagePrefix = "libaot-";
    private const string AssemblyStoreFile = "libassembly-store.so";

    public static IReadOnlyList<AbiReport> Check(string apkPath)
    {
        using ZipArchive apk = ZipFile.OpenRead(apkPath);
        var abis = new SortedDictionary<string, List<ZipArchiveEntry>>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in apk.Entries)
        {
            string[] parts = entry.FullName.Split('/');
            if (parts.Length == 3 && parts[0] == "lib")
            {
                if (!abis.TryGetValue(parts[1], out List<ZipArchiveEntry>? entries))
                {
                    abis[parts[1]] = entries = [];
                }

                entries.Add(entry);
            }
        }

        var reports = new List<AbiReport>();
        foreach ((string abi, List<ZipArchiveEntry> entries) in abis)
        {
            reports.Add(CheckAbi(abi, entries));
        }

        return reports;
    }

    private static AbiReport CheckAbi(string abi, List<ZipArchiveEntry> entries)
    {
        var problems = new List<string>();
        ZipArchiveEntry? store = entries.Find(e => e.Name == AssemblyStoreFile);
        List<ZipArchiveEntry> images = entries.FindAll(e => e.Name.StartsWith(AotImagePrefix, StringComparison.Ordinal) && e.Name.EndsWith(".so", StringComparison.Ordinal));
        if (store is null)
        {
            if (images.Count > 0)
            {
                problems.Add($"{abi}: {images.Count} AOT images but no {AssemblyStoreFile} to check them against");
            }

            return new AbiReport(abi, 0, images.Count, problems);
        }

        Dictionary<string, string> mvids = AssemblyStore.ReadMvids(ReadAll(store));
        var shipped = new HashSet<string>(mvids.Values, StringComparer.Ordinal);
        foreach (ZipArchiveEntry image in images)
        {
            // libaot-Microsoft.Maui.dll.so is the image of Microsoft.Maui.dll.
            string assembly = image.Name[AotImagePrefix.Length..^".so".Length];
            HashSet<string> guids = FindGuids(ReadAll(image));
            if (!mvids.TryGetValue(assembly, out string? mvid))
            {
                problems.Add($"{abi}: {image.Name} has no {assembly} in the assembly store");
                continue;
            }

            if (!guids.Contains(mvid))
            {
                problems.Add($"{abi}: {image.Name} was compiled from a different {assembly} than the one packaged (MVID {mvid})");
            }

            foreach (string guid in guids.Where(g => !shipped.Contains(g)).Order(StringComparer.Ordinal))
            {
                problems.Add($"{abi}: {image.Name} was compiled against an assembly with MVID {guid}, which the APK does not ship");
            }
        }

        return new AbiReport(abi, mvids.Count, images.Count, problems);
    }

    /// <summary>Mono writes GUIDs as upper-case <c>D</c>-format strings (<c>mono_guid_to_string</c>).</summary>
    private static HashSet<string> FindGuids(byte[] image)
    {
        var guids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in GuidPattern().Matches(Encoding.Latin1.GetString(image)))
        {
            guids.Add(match.Value);
        }

        return guids;
    }

    internal static string FormatMvid(Guid mvid) => mvid.ToString("D").ToUpperInvariant();

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        var buffer = new MemoryStream(checked((int)entry.Length));
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    [GeneratedRegex("[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex GuidPattern();
}

/// <summary>
/// The .NET for Android assembly store, version 3: an ELF shared library whose <c>payload</c> section holds the header
/// (<c>XABA</c>, version, entry count, index entry count, index size), the hash index, one 28-byte descriptor per assembly
/// (mapping index, data offset, data size, then debug and config offsets and sizes), the names (length-prefixed UTF-8) and the
/// data. Compressed assemblies start with <c>XALZ</c>, the descriptor index and the uncompressed size, then one LZ4 block.
/// </summary>
internal static class AssemblyStore
{
    private const uint Magic = 0x41424158; // "XABA"
    private const uint CompressedMagic = 0x5A4C4158; // "XALZ"
    private const int HeaderSize = 20;
    private const int DescriptorSize = 28;

    public static Dictionary<string, string> ReadMvids(byte[] library)
    {
        ReadOnlySpan<byte> store = Elf.Section(library, "payload");
        if (BinaryPrimitives.ReadUInt32LittleEndian(store) != Magic)
        {
            throw new InvalidDataException("The assembly store's payload does not start with XABA.");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(store[4..]);
        if ((version & 0xFFFF) != 3)
        {
            throw new InvalidDataException($"Assembly store version 0x{version:X8} is not 3; teach tools/ApkAotCheck the new layout.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(store[8..]);
        int indexSize = BinaryPrimitives.ReadInt32LittleEndian(store[16..]);
        int descriptors = HeaderSize + indexSize;
        int name = descriptors + (count * DescriptorSize);
        var mvids = new Dictionary<string, string>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> descriptor = store.Slice(descriptors + (i * DescriptorSize), DescriptorSize);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(descriptor[4..]);
            int size = BinaryPrimitives.ReadInt32LittleEndian(descriptor[8..]);
            int nameLength = BinaryPrimitives.ReadInt32LittleEndian(store[name..]);
            string assembly = Encoding.UTF8.GetString(store.Slice(name + 4, nameLength));
            name += 4 + nameLength;
            mvids[assembly] = ApkAotChecker.FormatMvid(ReadMvid(Decompress(store.Slice(offset, size))));
        }

        return mvids;
    }

    private static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != CompressedMagic)
        {
            return data.ToArray();
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        return Lz4.DecodeBlock(data[12..], length);
    }

    private static Guid ReadMvid(byte[] assembly)
    {
        using var pe = new PEReader(new MemoryStream(assembly, writable: false));
        MetadataReader metadata = pe.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
    }
}

/// <summary>Just enough ELF64 (little-endian) to find a section by name.</summary>
internal static class Elf
{
    public static ReadOnlySpan<byte> Section(byte[] file, string name)
    {
        ReadOnlySpan<byte> elf = file;
        if (!elf.StartsWith("\u007FELF"u8) || elf[4] != 2 || elf[5] != 1)
        {
            throw new InvalidDataException("Not a little-endian ELF64 file.");
        }

        long sectionTable = BinaryPrimitives.ReadInt64LittleEndian(elf[0x28..]);
        int entrySize = BinaryPrimitives.ReadUInt16LittleEndian(elf[0x3A..]);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(elf[0x3C..]);
        int namesIndex = BinaryPrimitives.ReadUInt16LittleEndian(elf[0x3E..]);
        long names = BinaryPrimitives.ReadInt64LittleEndian(elf.Slice(checked((int)sectionTable + (namesIndex * entrySize)), entrySize)[0x18..]);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> header = elf.Slice(checked((int)sectionTable + (i * entrySize)), entrySize);
            ReadOnlySpan<byte> sectionName = elf[checked((int)names + BinaryPrimitives.ReadInt32LittleEndian(header))..];
            sectionName = sectionName[..sectionName.IndexOf((byte)0)];
            if (Encoding.ASCII.GetString(sectionName) == name)
            {
                long offset = BinaryPrimitives.ReadInt64LittleEndian(header[0x18..]);
                long size = BinaryPrimitives.ReadInt64LittleEndian(header[0x20..]);
                return elf.Slice(checked((int)offset), checked((int)size));
            }
        }

        throw new InvalidDataException($"No ELF section named {name}.");
    }
}

/// <summary>The LZ4 block format (lz4_Block_format.md): tokens of literals then a back-reference, no frame.</summary>
internal static class Lz4
{
    public static byte[] DecodeBlock(ReadOnlySpan<byte> source, int length)
    {
        byte[] output = new byte[length];
        int s = 0;
        int d = 0;
        while (s < source.Length)
        {
            byte token = source[s++];
            int literals = ReadLength(source, ref s, token >> 4);
            source.Slice(s, literals).CopyTo(output.AsSpan(d));
            s += literals;
            d += literals;
            if (s >= source.Length)
            {
                break; // The last sequence is literals only.
            }

            int distance = BinaryPrimitives.ReadUInt16LittleEndian(source[s..]);
            s += 2;
            int match = ReadLength(source, ref s, token & 0xF) + 4;

            // Byte by byte: a match may overlap the bytes it is producing.
            for (int i = 0; i < match; i++, d++)
            {
                output[d] = output[d - distance];
            }
        }

        if (d != length)
        {
            throw new InvalidDataException($"LZ4 block decoded to {d} bytes, expected {length}.");
        }

        return output;
    }

    private static int ReadLength(ReadOnlySpan<byte> source, ref int s, int length)
    {
        if (length == 15)
        {
            byte next;
            do
            {
                next = source[s++];
                length += next;
            }
            while (next == 255);
        }

        return length;
    }
}
