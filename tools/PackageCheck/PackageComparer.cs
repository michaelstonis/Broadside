using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Broadside.Tools.PackageCheck;

/// <summary>
/// Compares two pack outputs of the same commit, built in different folders: every <c>.nupkg</c> and <c>.snupkg</c> must exist in both
/// with byte-identical entries (assemblies, PDBs, XML docs, nuspec, content). Only the OPC bookkeeping NuGet writes with a fresh random
/// ID on every pack (<c>_rels/.rels</c>, <c>package/services/metadata/**</c>) is skipped, and zip timestamps are not compared. In the
/// static web asset manifests the Razor SDK packs (<c>build/Microsoft.AspNetCore.StaticWebAsset*.props</c>), each asset's
/// <c>LastWriteTime</c> is the file's modification time on disk, which is when git checked it out, so those elements are ignored; the
/// assets themselves are still compared byte for byte.
/// </summary>
public static partial class PackageComparer
{
    /// <summary>Compares the packages in <paramref name="first"/> and <paramref name="second"/>.</summary>
    /// <returns>The packages compared (file names) and one finding per difference.</returns>
    public static (IReadOnlyList<string> Packages, IReadOnlyList<Finding> Findings) Compare(string first, string second)
    {
        var findings = new List<Finding>();
        var left = PackageFiles(first);
        var right = PackageFiles(second);
        if (left.Count == 0)
        {
            findings.Add(new Finding(first, "no packages"));
        }

        foreach (string name in right.Keys.Except(left.Keys).Order(StringComparer.Ordinal))
        {
            findings.Add(new Finding(name, $"only in {second}"));
        }

        foreach ((string name, string path) in left.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!right.TryGetValue(name, out string? other))
            {
                findings.Add(new Finding(name, $"only in {first}"));
                continue;
            }

            using ZipArchive a = ZipFile.OpenRead(path);
            using ZipArchive b = ZipFile.OpenRead(other);
            var entriesA = a.Entries.Where(Compared).ToDictionary(e => e.FullName, StringComparer.Ordinal);
            var entriesB = b.Entries.Where(Compared).ToDictionary(e => e.FullName, StringComparer.Ordinal);
            foreach (string entry in entriesA.Keys.Union(entriesB.Keys).Order(StringComparer.Ordinal))
            {
                if (!entriesA.TryGetValue(entry, out ZipArchiveEntry? x) || !entriesB.TryGetValue(entry, out ZipArchiveEntry? y))
                {
                    findings.Add(new Finding(name, $"{entry} is in only one build"));
                }
                else if (IsStaticWebAssetManifest(entry) ? !SameIgnoringWriteTimes(x, y) : !SameBytes(x, y))
                {
                    findings.Add(new Finding(name, $"{entry} differs between the builds"));
                }
            }
        }

        return (left.Keys.Order(StringComparer.Ordinal).ToList(), findings);
    }

    private static Dictionary<string, string> PackageFiles(string directory) =>
        Directory.GetFiles(directory, "*.nupkg").Concat(Directory.GetFiles(directory, "*.snupkg"))
            .ToDictionary(p => Path.GetFileName(p), StringComparer.Ordinal);

    private static bool Compared(ZipArchiveEntry entry) =>
        !entry.FullName.StartsWith("_rels/", StringComparison.Ordinal)
        && !entry.FullName.StartsWith("package/services/metadata/", StringComparison.Ordinal)
        && !entry.FullName.EndsWith('/');

    private static bool IsStaticWebAssetManifest(string entry) =>
        entry.StartsWith("build/Microsoft.AspNetCore.StaticWebAsset", StringComparison.Ordinal) && entry.EndsWith(".props", StringComparison.Ordinal);

    private static bool SameIgnoringWriteTimes(ZipArchiveEntry x, ZipArchiveEntry y) =>
        string.Equals(WithoutWriteTimes(x), WithoutWriteTimes(y), StringComparison.Ordinal);

    private static string WithoutWriteTimes(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return WriteTime().Replace(reader.ReadToEnd(), string.Empty);
    }

    [GeneratedRegex("<LastWriteTime>[^<]*</LastWriteTime>")]
    private static partial Regex WriteTime();

    private static bool SameBytes(ZipArchiveEntry x, ZipArchiveEntry y)
    {
        if (x.Length != y.Length)
        {
            return false;
        }

        using Stream a = x.Open();
        using Stream b = y.Open();
        byte[] bufferA = new byte[81920];
        byte[] bufferB = new byte[81920];
        while (true)
        {
            int read = a.ReadAtLeast(bufferA, bufferA.Length, throwOnEndOfStream: false);
            int readB = b.ReadAtLeast(bufferB, read, throwOnEndOfStream: false);
            if (read != readB || !bufferA.AsSpan(0, read).SequenceEqual(bufferB.AsSpan(0, read)))
            {
                return false;
            }

            if (read == 0)
            {
                return true;
            }
        }
    }
}
