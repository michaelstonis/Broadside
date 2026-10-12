using System.Collections.Frozen;
using System.IO.Enumeration;

namespace Broadside.Fonts.Resolution;

/// <summary>
/// The font files of a set of directories, from a directory listing alone (no file is opened): each with the family key its file
/// name suggests, so a lookup can read only the files of the families it asks for.
/// </summary>
/// <remarks>
/// <para>
/// Files are listed in directory order, then by ordinal path order inside each directory; a path listed twice keeps its first
/// place. That order is the scan order of <see cref="SystemFontIndex"/> and decides every tie.
/// </para>
/// <para>
/// A file's key is its name without extension through <see cref="FontNameParser.FamilyKey"/> (<c>Times New Roman Bold.ttf</c> is
/// <c>timesnewromanbold</c>, <c>arialbd.ttf</c> <c>arialbd</c>). Files are grouped by the first three letters of their key, which
/// also catches the abbreviated names of the Windows fonts (<c>timesbd</c>, <c>cour</c>, <c>ariblk</c>). A file whose name has
/// letters outside ASCII (the Japanese names of the macOS Hiragino fonts) or fewer than three ASCII letters and digits says nothing
/// about its family, and is read for every family.
/// </para>
/// </remarks>
internal sealed class FontFileListing
{
    /// <summary>The font file extensions read: TrueType, OpenType and their collections.</summary>
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc", ".otc"];

    /// <summary>The most font files listed, a guard against directories that are not font directories.</summary>
    private const int MaxFiles = 100_000;

    /// <summary>The key letters files are grouped by.</summary>
    private const int PrefixLength = 3;

    private readonly FrozenDictionary<string, int[]> _byPrefix;
    private readonly int[] _unnamed;

    private FontFileListing(List<FontFileEntry> files)
    {
        Files = files;
        var byPrefix = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var unnamed = new List<int>();
        for (int index = 0; index < files.Count; index++)
        {
            string key = files[index].Key;
            if (key.Length < PrefixLength)
            {
                unnamed.Add(index);
                continue;
            }

            string prefix = key[..PrefixLength];
            if (!byPrefix.TryGetValue(prefix, out List<int>? list))
            {
                list = [];
                byPrefix.Add(prefix, list);
            }

            list.Add(index);
        }

        _byPrefix = byPrefix.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
        _unnamed = [.. unnamed];
    }

    /// <summary>Gets the font files, in scan order.</summary>
    public IReadOnlyList<FontFileEntry> Files { get; }

    /// <summary>Lists the font files of directories, recursively, skipping what cannot be read.</summary>
    /// <param name="directories">The directories, in priority order.</param>
    /// <param name="withStamps">Whether to read each file's length and last write time (one metadata read per file, no open).</param>
    /// <returns>The listing.</returns>
    public static FontFileListing Create(IEnumerable<string> directories, bool withStamps)
    {
        var files = new List<FontFileEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int count = 0;
        foreach (string directory in directories)
        {
            foreach (FontFileEntry file in List(directory, withStamps))
            {
                if (count++ >= MaxFiles)
                {
                    return new FontFileListing(files);
                }

                if (seen.Add(file.Path))
                {
                    files.Add(file);
                }
            }
        }

        return new FontFileListing(files);
    }

    /// <summary>
    /// Gets the files that may hold faces of the given family keys (or PostScript names reduced to keys): those whose key shares its
    /// first three letters with one of them (all of a shorter key), and those whose name says nothing about their family.
    /// </summary>
    /// <param name="keys">The keys.</param>
    /// <returns>The files' indexes in <see cref="Files"/>, ascending.</returns>
    public int[] Candidates(IEnumerable<string> keys)
    {
        var indexes = new SortedSet<int>(_unnamed);
        foreach (string key in keys)
        {
            if (key.Length >= PrefixLength)
            {
                if (_byPrefix.TryGetValue(key[..PrefixLength], out int[]? files))
                {
                    indexes.UnionWith(files);
                }

                continue;
            }

            if (key.Length == 0)
            {
                continue;
            }

            foreach (KeyValuePair<string, int[]> pair in _byPrefix)
            {
                if (pair.Key.StartsWith(key, StringComparison.Ordinal))
                {
                    indexes.UnionWith(pair.Value);
                }
            }
        }

        return [.. indexes];
    }

    /// <summary>The key that groups a lookup key's candidate files: its first three letters, or the whole of a shorter key.</summary>
    /// <param name="key">The lookup key.</param>
    /// <returns>The group.</returns>
    public static string Group(string key) => key.Length > PrefixLength ? key[..PrefixLength] : key;

    /// <summary>The key a file name suggests, or the empty string when the name says nothing about the family.</summary>
    internal static string FileKey(string path)
    {
        ReadOnlySpan<char> stem = System.IO.Path.GetFileNameWithoutExtension(path.AsSpan());
        foreach (char ch in stem)
        {
            if (!char.IsAscii(ch) && char.IsLetterOrDigit(ch))
            {
                return string.Empty;
            }
        }

        string key = FontNameParser.FamilyKey(stem.ToString());
        return key.Length < PrefixLength ? string.Empty : key;
    }

    private static List<FontFileEntry> List(string directory, bool withStamps)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 16, AttributesToSkip = 0 };
            var enumerable = new FileSystemEnumerable<FontFileEntry>(
                directory,
                withStamps
                    ? (ref FileSystemEntry entry) => Entry(entry.ToSpecifiedFullPath(), entry.Length, entry.LastWriteTimeUtc.UtcTicks)
                    : (ref FileSystemEntry entry) => Entry(entry.ToSpecifiedFullPath(), 0, 0),
                options)
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) => !entry.IsDirectory && IsFontFile(entry.FileName),
            };
            List<FontFileEntry> files = [.. enumerable];
            files.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path));
            return files;
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
        catch (System.Security.SecurityException)
        {
            return [];
        }
    }

    private static FontFileEntry Entry(string path, long length, long writeTime) => new(path, FileKey(path), length, writeTime);

    private static bool IsFontFile(ReadOnlySpan<char> name)
    {
        ReadOnlySpan<char> extension = System.IO.Path.GetExtension(name);
        foreach (string candidate in Extensions)
        {
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>One font file of a <see cref="FontFileListing"/>.</summary>
/// <param name="Path">The path, as the directory listing gives it.</param>
/// <param name="Key">The family key its name suggests; empty when the name says nothing about the family.</param>
/// <param name="Length">The length in bytes, or 0 when not read.</param>
/// <param name="WriteTime">The last write time in UTC ticks, or 0 when not read.</param>
internal readonly record struct FontFileEntry(string Path, string Key, long Length, long WriteTime);
