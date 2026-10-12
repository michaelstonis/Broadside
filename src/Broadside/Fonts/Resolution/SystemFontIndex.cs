using System.Collections.Frozen;

namespace Broadside.Fonts.Resolution;

/// <summary>
/// Installed font faces, looked up by PostScript name and by family: built over the faces of some or all of a set of directories'
/// font files (headers only, <see cref="SystemFontCatalog"/>), immutable afterwards.
/// </summary>
/// <remarks>
/// Faces are kept in scan order (<see cref="FontFileListing"/>: directory order, then ordinal path order inside each directory, and
/// faces in file order), so the index, and every lookup's tie-break, is the same on every run. Names containing a vertical bar and
/// faces without outlines are skipped by the scanner. PDFBox's <c>FileSystemFontProvider</c> scans the same way.
/// </remarks>
internal sealed class SystemFontIndex
{
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

    /// <summary>Scans directories, recursively, skipping what cannot be read: every file of their <see cref="FontFileListing"/>.</summary>
    /// <param name="directories">The directories, in priority order.</param>
    /// <returns>The index.</returns>
    public static SystemFontIndex Build(IEnumerable<string> directories) =>
        new([.. FontFileListing.Create(directories, withStamps: false).Files.SelectMany(file => ScanFile(file.Path))]);

    /// <summary>Builds an index over faces already scanned, in scan order.</summary>
    internal static SystemFontIndex FromFaces(List<SystemFontFace> faces) => new(faces);

    /// <summary>Reads the faces of one font file (headers only); none when it cannot be read.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The faces, in file order.</returns>
    internal static List<SystemFontFace> ScanFile(string path)
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
