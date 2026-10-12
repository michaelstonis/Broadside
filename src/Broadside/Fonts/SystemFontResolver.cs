using System.Collections.Concurrent;
using Broadside.Fonts.Resolution;

namespace Broadside.Fonts;

/// <summary>
/// The operating system's installed fonts as a font resolver: the font resolver every engine asks last, unless replaced or turned
/// off with <see cref="PdfOptions.UseSystemFontResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.2.1 (Table 109: <c>BaseFont</c> "may be used to find the font program in the PDF processor or its
/// environment"), §9.6.2.2 and §9.8. The font directories are read in managed code (no fontconfig, CoreText or DirectWrite): TrueType
/// and OpenType files and collections (<c>.ttf</c>, <c>.otf</c>, <c>.ttc</c>, <c>.otc</c>), headers only, on the first font asked
/// for, once per instance. The engines' default instance is shared by the process, so its directories are scanned once.
/// </para>
/// <para>
/// A font is looked for, in order: by PostScript name ignoring case (also without hyphens, with the Windows <c>Name,Style</c> comma
/// read as a hyphen, and with <c>-Regular</c>), <see cref="FontMatchKind.Exact"/>; by family and style, the family taken from the
/// name with style words removed or from the descriptor's <c>FontFamily</c>, compared with the fonts' typographic and legacy family
/// names, <see cref="FontMatchKind.Family"/>; and for a Standard 14 font, through the fonts that usually stand in for it (Helvetica:
/// Helvetica, Arial, Liberation Sans, Arimo, Nimbus Sans, TeX Gyre Heros, FreeSans; Times: Times, Times New Roman, Liberation Serif,
/// Tinos, Nimbus Roman, TeX Gyre Termes, FreeSerif; Courier: Courier, Courier New, Liberation Mono, Cousine, Nimbus Mono, TeX Gyre
/// Cursor, FreeMono; Symbol: Symbol, Standard Symbols PS; ZapfDingbats: ZapfDingbats, Dingbats, D050000L, never Wingdings),
/// <see cref="FontMatchKind.Standard14"/>. Among the faces of a family, the one closest in weight, slope and width wins; ties go to
/// the first in directory order.
/// </para>
/// <para>
/// The directories: on Windows, the Fonts folder and the per-user <c>%LOCALAPPDATA%\Microsoft\Windows\Fonts</c>; on macOS,
/// <c>/System/Library/Fonts</c> (with <c>Supplemental</c>), <c>/Library/Fonts</c>, <c>~/Library/Fonts</c> and
/// <c>/Network/Library/Fonts</c>; on iOS and Mac Catalyst, <c>/System/Library/Fonts</c>; on Android, <c>/system/fonts</c>,
/// <c>/product/fonts</c> and <c>/system/product/fonts</c>; elsewhere (Linux, FreeBSD), <c>$XDG_DATA_HOME/fonts</c>,
/// <c>~/.fonts</c>, each <c>$XDG_DATA_DIRS</c> entry's <c>fonts</c> and <c>/usr/X11R6/lib/X11/fonts</c>; in the browser, none.
/// </para>
/// </remarks>
public sealed class SystemFontResolver : IFontResolver
{
    /// <summary>The most bytes of one font file loaded.</summary>
    private const long MaxFileLength = 256L << 20;

    private readonly Lazy<SystemFontIndex> _index;
    private readonly ConcurrentDictionary<string, ReadOnlyMemory<byte>?> _files = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="SystemFontResolver"/> class over the platform's font directories.</summary>
    public SystemFontResolver()
        : this(PlatformDirectories())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SystemFontResolver"/> class over the given directories.</summary>
    /// <param name="directories">The directories, searched recursively, earlier ones first; missing ones are skipped.</param>
    public SystemFontResolver(IEnumerable<string> directories)
    {
        ArgumentNullException.ThrowIfNull(directories);
        Directories = [.. directories];
        _index = new Lazy<SystemFontIndex>(() => SystemFontIndex.Build(Directories), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the directories read, in the order they are searched.</summary>
    public IReadOnlyList<string> Directories { get; }

    /// <summary>Gets the instance engines use by default, shared by the process so the font directories are scanned once.</summary>
    internal static SystemFontResolver Shared { get; } = new();

    /// <summary>Gets the faces found (scanning the directories on first use).</summary>
    internal SystemFontIndex Index => _index.Value;

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §9.6.2.1, §9.6.2.2 and §9.8; the order is described on <see cref="SystemFontResolver"/>.</remarks>
    public FontResolution? ResolveFont(FontQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Directories.Count == 0)
        {
            return null;
        }

        SystemFontIndex index = Index;
        if (FindByName(index, query) is { } exact)
        {
            return Load(exact, FontMatchKind.Exact);
        }

        if (FindByFamily(index, query, query.FamilyKey) is { } family)
        {
            return Load(family, FontMatchKind.Family);
        }

        if (query.FontFamily is { Length: > 0 } descriptorFamily && FindByFamily(index, query, FontNameParser.FamilyKey(descriptorFamily)) is { } byDescriptor)
        {
            return Load(byDescriptor, FontMatchKind.Family);
        }

        if (query.Standard14 is { } standard14)
        {
            foreach (string key in StandIns.For(standard14))
            {
                if (FindByFamily(index, query, key) is { } standIn)
                {
                    return Load(standIn, FontMatchKind.Standard14);
                }
            }
        }

        return null;
    }

    /// <summary>The platform's font directories, in priority order.</summary>
    internal static List<string> PlatformDirectories()
    {
        var directories = new List<string>();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsBrowser() || OperatingSystem.IsWasi())
        {
            return directories;
        }

        if (OperatingSystem.IsWindows())
        {
            directories.Add(Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
            directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts"));
        }
        else if (OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsMacCatalyst())
        {
            directories.Add("/System/Library/Fonts");
        }
        else if (OperatingSystem.IsMacOS())
        {
            directories.Add("/System/Library/Fonts");
            directories.Add("/Library/Fonts");
            directories.Add(Path.Combine(home, "Library", "Fonts"));
            directories.Add("/Network/Library/Fonts");
        }
        else if (OperatingSystem.IsAndroid())
        {
            directories.AddRange(["/system/fonts", "/product/fonts", "/system/product/fonts"]);
        }
        else
        {
            string? dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            directories.Add(Path.Combine(string.IsNullOrEmpty(dataHome) ? Path.Combine(home, ".local", "share") : dataHome, "fonts"));
            directories.Add(Path.Combine(home, ".fonts"));
            string? dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
            foreach (string dataDir in string.IsNullOrEmpty(dataDirs) ? ["/usr/local/share", "/usr/share"] : dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
            {
                directories.Add(Path.Combine(dataDir, "fonts"));
            }

            directories.Add("/usr/X11R6/lib/X11/fonts");
        }

        return [.. directories.Where(directory => !string.IsNullOrEmpty(directory)).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The name ladder: as written, without hyphens, comma as hyphen, family plus <c>-Regular</c> (PDFBox <c>FontMapperImpl</c>).</summary>
    private static SystemFontFace? FindByName(SystemFontIndex index, FontQuery query)
    {
        string name = query.PostScriptName;
        string[] candidates =
        [
            name,
            name.Replace("-", string.Empty, StringComparison.Ordinal),
            name.Replace(',', '-'),
            query.FamilyName + "-Regular",
        ];
        foreach (string candidate in candidates)
        {
            if (index.ByPostScriptName.TryGetValue(candidate, out SystemFontFace? face))
            {
                // "-Regular" only stands for a face the query asks no style of.
                if (candidate == candidates[3] && (query.IsBold || query.IsItalic))
                {
                    continue;
                }

                return face;
            }
        }

        return null;
    }

    /// <summary>The face of a family closest to the query's style: weight, slope and width (score as in the ticket notes, §5.7).</summary>
    private static SystemFontFace? FindByFamily(SystemFontIndex index, FontQuery query, string key)
    {
        if (key.Length == 0 || !index.ByFamily.TryGetValue(key, out IReadOnlyList<SystemFontFace>? faces))
        {
            return null;
        }

        int wanted = query.IsBold ? Math.Max(query.Weight, 700) : Math.Min(query.Weight, 500);
        SystemFontFace? best = null;
        double bestScore = double.NegativeInfinity;
        foreach (SystemFontFace face in faces)
        {
            double score = 0;
            score += face.IsItalic == query.IsItalic ? 2 : 0;
            score += face.IsBold == query.IsBold ? 2 : 0;
            score -= Math.Abs(face.Weight - wanted) / 100.0;
            score -= (face.WidthClass < 5) != query.IsCondensed ? 3 : 0;
            if (score > bestScore)
            {
                best = face;
                bestScore = score;
            }
        }

        return best;
    }

    private FontResolution? Load(SystemFontFace face, FontMatchKind kind)
    {
        ReadOnlyMemory<byte>? data = _files.GetOrAdd(face.Path, static path => ReadFile(path));
        return data is { IsEmpty: false } bytes ? new FontResolution(bytes, face.PostScriptName, kind) { FaceIndex = face.FaceIndex } : null;
    }

    private static ReadOnlyMemory<byte>? ReadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Length is > 0 and <= MaxFileLength ? File.ReadAllBytes(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The family keys of the fonts that usually stand in for each Standard 14 family, in order (pdf.js and PDFBox lists).</summary>
    private static class StandIns
    {
        private static readonly string[] Helvetica = ["helvetica", "arial", "liberationsans", "arimo", "nimbussans", "nimbussansl", "texgyreheros", "freesans"];
        private static readonly string[] Times = ["times", "timesnewroman", "liberationserif", "tinos", "nimbusroman", "nimbusromanno9l", "texgyretermes", "freeserif"];
        private static readonly string[] Courier = ["courier", "couriernew", "liberationmono", "cousine", "nimbusmono", "nimbusmonops", "nimbusmonol", "texgyrecursor", "freemono"];
        private static readonly string[] Symbol = ["symbol", "standardsymbolsps", "standardsyml"];
        private static readonly string[] ZapfDingbats = ["zapfdingbats", "zapfdingbatsitc", "itczapfdingbats", "dingbats", "d050000l"];

        public static string[] For(Standard14Font font) => font switch
        {
            <= Standard14Font.CourierBoldOblique => Courier,
            <= Standard14Font.HelveticaBoldOblique => Helvetica,
            <= Standard14Font.TimesBoldItalic => Times,
            Standard14Font.Symbol => Symbol,
            _ => ZapfDingbats,
        };
    }
}
