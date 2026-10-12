using System.Collections.Frozen;

namespace Broadside.Fonts.Resolution;

/// <summary>
/// The installed font faces of a set of directories, looked up by PostScript name and by family: built once by scanning the
/// directories' font files (headers only), immutable afterwards.
/// </summary>
/// <remarks>
/// Files are visited in directory order, then by ordinal path order inside each directory, and faces in file order, so the index,
/// and every lookup's tie-break, is the same on every run. Names containing a vertical bar and faces without outlines are skipped by
/// the scanner. PDFBox's <c>FileSystemFontProvider</c> scans the same way; this index is not persisted.
/// </remarks>
internal sealed class SystemFontIndex
{
    /// <summary>The font file extensions read: TrueType, OpenType and their collections.</summary>
    private static readonly string[] Extensions = [".ttf", ".otf", ".ttc", ".otc"];

    /// <summary>The most font files scanned, a guard against directories that are not font directories.</summary>
    private const int MaxFiles = 100_000;

    private SystemFontIndex(List<SystemFontFace> faces)
    {
        Faces = faces;
        var byName = new Dictionary<string, SystemFontFace>(StringComparer.OrdinalIgnoreCase);
        var byFamily = new Dictionary<string, List<SystemFontFace>>(StringComparer.Ordinal);
        foreach (SystemFontFace face in faces)
        {
            byName.TryAdd(face.PostScriptName, face);
            byName.TryAdd(face.PostScriptName.Replace("-", string.Empty, StringComparison.Ordinal), face);
            foreach (string key in FamilyKeys(face))
            {
                if (!byFamily.TryGetValue(key, out List<SystemFontFace>? list))
                {
                    list = [];
                    byFamily.Add(key, list);
                }

                list.Add(face);
            }
        }

        ByPostScriptName = byName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        ByFamily = byFamily.ToFrozenDictionary(pair => pair.Key, pair => (IReadOnlyList<SystemFontFace>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>Gets every face, in scan order.</summary>
    public IReadOnlyList<SystemFontFace> Faces { get; }

    /// <summary>Gets the faces by PostScript name (and by that name without hyphens), ignoring case; the first face of a name wins.</summary>
    public FrozenDictionary<string, SystemFontFace> ByPostScriptName { get; }

    /// <summary>Gets the faces by family key (<see cref="FontNameParser.FamilyKey"/>) of their typographic family, legacy family and PostScript name's family.</summary>
    public FrozenDictionary<string, IReadOnlyList<SystemFontFace>> ByFamily { get; }

    /// <summary>Scans directories, recursively, skipping what cannot be read.</summary>
    /// <param name="directories">The directories, in priority order.</param>
    /// <returns>The index.</returns>
    public static SystemFontIndex Build(IEnumerable<string> directories)
    {
        var faces = new List<SystemFontFace>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int files = 0;
        foreach (string directory in directories)
        {
            foreach (string path in EnumerateFonts(directory))
            {
                if (files++ >= MaxFiles)
                {
                    return new SystemFontIndex(faces);
                }

                if (seen.Add(path))
                {
                    faces.AddRange(ScanFile(path));
                }
            }
        }

        return new SystemFontIndex(faces);
    }

    /// <summary>Builds an index over faces already scanned (fuzzing and tests).</summary>
    internal static SystemFontIndex FromFaces(List<SystemFontFace> faces) => new(faces);

    private static List<string> EnumerateFonts(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return [];
            }

            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 16, AttributesToSkip = 0 };
            List<string> paths = [.. Directory.EnumerateFiles(directory, "*", options).Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))];
            paths.Sort(StringComparer.Ordinal);
            return paths;
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

    private static List<SystemFontFace> ScanFile(string path)
    {
        try
        {
            using Microsoft.Win32.SafeHandles.SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = RandomAccess.GetLength(handle);
            return FontFaceScanner.Scan(new HandleFontFileReader(handle, length), path);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static HashSet<string> FamilyKeys(SystemFontFace face)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal)
        {
            FontNameParser.FamilyKey(face.Family),
            FontNameParser.FamilyKey(FontNameParser.Parse(face.PostScriptName).FamilyName),
        };
        if (face.LegacyFamily is { } legacy)
        {
            keys.Add(FontNameParser.FamilyKey(legacy));
        }

        keys.Remove(string.Empty);
        return keys;
    }
}
