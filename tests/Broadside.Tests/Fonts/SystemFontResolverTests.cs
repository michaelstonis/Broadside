using Broadside.Fonts;
using Broadside.TestSupport;

namespace Broadside.Tests.Fonts;

/// <summary>
/// The operating-system font resolver over a directory of installed fonts built for the test: names, families and styles from the
/// fonts' "name", "OS/2" and "post" tables. ISO 32000-2 §9.6.2.1 (BaseFont), §9.6.2.2, §9.6.3, §9.8.
/// </summary>
public sealed class SystemFontResolverTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("broadside-fonts-").FullName;
    private readonly string _store = Directory.CreateTempSubdirectory("broadside-font-index-").FullName;

    public SystemFontResolverTests()
    {
        Install("arial.ttf", SystemFontBuilder.Font("ArialMT", "Arial"));
        Install("arialbd.ttf", SystemFontBuilder.Font("Arial-BoldMT", "Arial", "Bold", weight: 700));
        Install("ariali.ttf", SystemFontBuilder.Font("Arial-ItalicMT", "Arial", "Italic", italic: true));
        Install("Sub/arialbi.ttf", SystemFontBuilder.Font("Arial-BoldItalicMT", "Arial", "Bold Italic", weight: 700, italic: true));
        Install("ARIALN.TTF", SystemFontBuilder.Font("ArialNarrow", "Arial Narrow", widthClass: 3));
        Install("times.ttf", SystemFontBuilder.Font("TimesNewRomanPSMT", "Times New Roman"));
        Install("timesbd.ttf", SystemFontBuilder.Font("TimesNewRomanPS-BoldMT", "Times New Roman", "Bold", weight: 700));
        Install("cour.ttf", SystemFontBuilder.Font("CourierNewPSMT", "Courier New", fixedPitch: true));
        Install("light.otf", SystemFontBuilder.Font("Broadside-Light", "Broadside Light", "Regular", weight: 300, typographicFamily: "Broadside"));
        Install("wingding.ttf", SystemFontBuilder.Font("Wingdings-Regular", "Wingdings", symbolCmap: true));
        Install("readme.txt", "not a font"u8.ToArray());
        Install("junk.ttf", "\0\u0001\0\0 truncated"u8.ToArray());
        Install("empty.ttf", []);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        Directory.Delete(_store, recursive: true);
    }

    [Fact]
    public void A_PostScript_name_finds_the_font_of_that_name_ignoring_case()
    {
        var resolver = new SystemFontResolver([_directory]);

        FontResolution resolution = Assert.IsType<FontResolution>(resolver.ResolveFont(new FontQuery("ABCDEF+timesnewromanps-boldmt")));

        Assert.Equal("TimesNewRomanPS-BoldMT", resolution.Name);
        Assert.Equal(FontMatchKind.Exact, resolution.MatchKind);
        Assert.Equal(File.ReadAllBytes(Path.Combine(_directory, "timesbd.ttf")), resolution.Data.ToArray());
        Assert.Equal(0, resolution.FaceIndex);
    }

    [Theory]
    [InlineData("ABCDEF+Arial,BoldItalic", "Arial-BoldItalicMT")]
    [InlineData("Arial,Bold", "Arial-BoldMT")]
    [InlineData("Arial-Italic", "Arial-ItalicMT")]
    [InlineData("Arial", "ArialMT")]
    [InlineData("TimesNewRoman,Bold", "TimesNewRomanPS-BoldMT")]
    [InlineData("Times New Roman", "TimesNewRomanPSMT")]
    [InlineData("ArialNarrow-Bold", "ArialNarrow")]
    public void A_family_and_style_find_the_best_face_of_the_family(string name, string expected)
    {
        var resolver = new SystemFontResolver([_directory]);

        FontResolution? resolution = resolver.ResolveFont(new FontQuery(name));

        Assert.Equal(expected, resolution?.Name);
        Assert.NotEqual(FontMatchKind.Similar, resolution?.MatchKind);
    }

    [Fact]
    public void Descriptor_facts_choose_the_style_when_the_name_states_none()
    {
        var resolver = new SystemFontResolver([_directory]);

        FontResolution? italic = resolver.ResolveFont(new FontQuery("Arial") { ItalicAngle = -12 });
        FontResolution? bold = resolver.ResolveFont(new FontQuery("Arial") { FontWeight = 700 });
        FontResolution? both = resolver.ResolveFont(new FontQuery("Arial") { Flags = PdfFontFlags.Italic | PdfFontFlags.ForceBold });

        Assert.Equal("Arial-ItalicMT", italic?.Name);
        Assert.Equal("Arial-BoldMT", bold?.Name);
        Assert.Equal("Arial-BoldItalicMT", both?.Name);
    }

    [Fact]
    public void The_typographic_family_name_is_preferred_to_the_legacy_one()
    {
        var resolver = new SystemFontResolver([_directory]);

        FontResolution? resolution = resolver.ResolveFont(new FontQuery("Broadside") { FontWeight = 300 });

        Assert.Equal("Broadside-Light", resolution?.Name);
        Assert.Equal(FontMatchKind.Family, resolution?.MatchKind);
    }

    [Theory]
    [InlineData(Standard14Font.Helvetica, "ArialMT")]
    [InlineData(Standard14Font.HelveticaBoldOblique, "Arial-BoldItalicMT")]
    [InlineData(Standard14Font.TimesBold, "TimesNewRomanPS-BoldMT")]
    [InlineData(Standard14Font.CourierOblique, "CourierNewPSMT")]
    public void A_Standard_14_font_is_found_through_its_usual_stand_ins(Standard14Font font, string expected)
    {
        var resolver = new SystemFontResolver([_directory]);
        string name = font switch
        {
            Standard14Font.Helvetica => "Helvetica",
            Standard14Font.HelveticaBoldOblique => "Helvetica-BoldOblique",
            Standard14Font.TimesBold => "Times-Bold",
            _ => "Courier-Oblique",
        };

        FontResolution? resolution = resolver.ResolveFont(new FontQuery(name) { Standard14 = font });

        Assert.Equal(expected, resolution?.Name);
        Assert.Equal(FontMatchKind.Standard14, resolution?.MatchKind);
    }

    [Fact]
    public void A_font_of_an_unknown_family_is_not_found()
    {
        var resolver = new SystemFontResolver([_directory]);

        Assert.Null(resolver.ResolveFont(new FontQuery("Calibri") { Flags = PdfFontFlags.Nonsymbolic }));
        Assert.Null(resolver.ResolveFont(new FontQuery("Symbol") { Standard14 = Standard14Font.Symbol }));
        Assert.Null(resolver.ResolveFont(new FontQuery("ZapfDingbats") { Standard14 = Standard14Font.ZapfDingbats }));
    }

    [Fact]
    public void A_font_in_a_collection_is_found_with_its_face_index()
    {
        Install(
            "Family.ttc",
            TrueTypeBuilder.Collection(SystemFontBuilder.Font("Collected-Regular", "Collected"), SystemFontBuilder.Font("Collected-Bold", "Collected", "Bold", weight: 700)));
        var resolver = new SystemFontResolver([_directory]);

        FontResolution? regular = resolver.ResolveFont(new FontQuery("Collected"));
        FontResolution? bold = resolver.ResolveFont(new FontQuery("Collected-Bold"));

        Assert.Equal(("Collected-Regular", 0), (regular?.Name, regular?.FaceIndex));
        Assert.Equal(("Collected-Bold", 1, FontMatchKind.Exact), (bold?.Name, bold?.FaceIndex, bold?.MatchKind));
        Assert.Equal(regular?.Data, bold?.Data);
    }

    [Fact]
    public void The_same_font_returns_the_same_memory_so_documents_parse_it_once()
    {
        var resolver = new SystemFontResolver([_directory]);

        FontResolution? first = resolver.ResolveFont(new FontQuery("ArialMT"));
        FontResolution? second = resolver.ResolveFont(new FontQuery("Arial,Regular"));

        Assert.Equal(first?.Data, second?.Data);
    }

    [Fact]
    public void Missing_directories_and_unreadable_files_find_nothing_and_never_throw()
    {
        var resolver = new SystemFontResolver([Path.Combine(_directory, "does-not-exist")]);

        Assert.Null(resolver.ResolveFont(new FontQuery("Arial")));
    }

    [Fact]
    public void A_document_draws_a_non_embedded_font_with_the_installed_font()
    {
        var options = new PdfOptions().UseSystemFontResolver(new SystemFontResolver([_directory]));
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-standard14-alias.pdf"), options);
        PdfTrueTypeFont font = Assert.IsType<PdfTrueTypeFont>(Assert.Single(document.Pages).GetFont("F1"));

        FontSubstitute substitute = Assert.IsType<FontSubstitute>(font.Substitute);
        Assert.Equal("Arial-BoldMT", substitute.Name);
        Assert.Equal(FontMatchKind.Family, substitute.MatchKind);
        Assert.Equal(0, font.GetGlyphId((byte)'H'));
        Assert.Contains(document.Diagnostics, diagnostic => diagnostic.Code == "FontSubstituted" && diagnostic.Message.Contains("Arial-BoldMT", StringComparison.Ordinal));
    }

    [Fact]
    public void A_font_whose_file_name_does_not_name_its_family_is_still_found()
    {
        // light.otf holds the family Broadside: no file name points at it, so every file is read.
        var resolver = new SystemFontResolver([_directory]);

        FontResolution? resolution = resolver.ResolveFont(new FontQuery("Broadside-Light"));

        Assert.Equal(("Broadside-Light", FontMatchKind.Exact), (resolution?.Name, resolution?.MatchKind));
    }

    [Fact]
    public void With_an_index_path_a_new_resolver_finds_fonts_from_the_stored_index_without_reading_them()
    {
        string index = Path.Combine(_store, "cache", "fonts.index");
        FontResolution? first = new SystemFontResolver([_directory], index).ResolveFont(new FontQuery("TimesNewRoman,Bold"));
        Assert.Equal("TimesNewRomanPS-BoldMT", first?.Name);
        Assert.True(File.Exists(index));

        // Zeros of the same length and time: only the stored index still knows the font's name (a new resolver is a new process).
        string path = Path.Combine(_directory, "timesbd.ttf");
        DateTime time = File.GetLastWriteTimeUtc(path);
        byte[] zeros = new byte[new FileInfo(path).Length];
        File.WriteAllBytes(path, zeros);
        File.SetLastWriteTimeUtc(path, time);
        FontResolution? second = new SystemFontResolver([_directory], index).ResolveFont(new FontQuery("TimesNewRoman,Bold"));
        FontResolution? unstored = new SystemFontResolver([_directory]).ResolveFont(new FontQuery("TimesNewRoman,Bold"));

        Assert.Equal(("TimesNewRomanPS-BoldMT", FontMatchKind.Family), (second?.Name, second?.MatchKind));
        Assert.Equal(zeros, second?.Data.ToArray());
        Assert.Equal("TimesNewRomanPSMT", unstored?.Name);
    }

    [Fact]
    public void Adding_a_font_file_invalidates_the_stored_index()
    {
        string index = Path.Combine(_store, "fonts.index");
        Assert.Null(new SystemFontResolver([_directory], index).ResolveFont(new FontQuery("Calibri")));
        byte[] stored = File.ReadAllBytes(index);

        Install("Fonts/calibri.ttf", SystemFontBuilder.Font("Calibri", "Calibri"));
        FontResolution? resolution = new SystemFontResolver([_directory], index).ResolveFont(new FontQuery("Calibri"));

        Assert.Equal(("Calibri", FontMatchKind.Exact), (resolution?.Name, resolution?.MatchKind));
        Assert.NotEqual(stored, File.ReadAllBytes(index));
    }

    [Fact]
    public void Changing_a_font_file_invalidates_its_entry_in_the_stored_index()
    {
        string index = Path.Combine(_store, "fonts.index");
        Assert.Equal("CourierNewPSMT", new SystemFontResolver([_directory], index).ResolveFont(new FontQuery("CourierNewPSMT"))?.Name);

        Install("cour.ttf", SystemFontBuilder.Font("CourierNewPS-Renamed", "Courier New", fixedPitch: true));
        File.SetLastWriteTimeUtc(Path.Combine(_directory, "cour.ttf"), DateTime.UtcNow.AddMinutes(1));
        var resolver = new SystemFontResolver([_directory], index);

        Assert.Equal("CourierNewPS-Renamed", resolver.ResolveFont(new FontQuery("CourierNewPS-Renamed"))?.Name);
        Assert.Equal(FontMatchKind.Family, resolver.ResolveFont(new FontQuery("CourierNewPSMT"))?.MatchKind);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x42, 0x53, 0x46, 0x49, 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F })]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46 })]
    public void A_damaged_stored_index_is_ignored_and_replaced(byte[] damaged)
    {
        string index = Path.Combine(_store, "fonts.index");
        File.WriteAllBytes(index, damaged);

        FontResolution? resolution = new SystemFontResolver([_directory], index).ResolveFont(new FontQuery("Arial,Bold"));

        Assert.Equal("Arial-BoldMT", resolution?.Name);
        Assert.NotEqual(damaged, File.ReadAllBytes(index));
        Assert.Equal("Arial-BoldMT", new SystemFontResolver([_directory], index).ResolveFont(new FontQuery("Arial,Bold"))?.Name);
    }

    [Fact]
    public void A_stored_index_of_other_directories_is_not_used()
    {
        string index = Path.Combine(_store, "fonts.index");
        string other = Directory.CreateTempSubdirectory("broadside-fonts-other-").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(other, "arial.ttf"), SystemFontBuilder.Font("Other-Arial", "Arial"));
            Assert.Equal("Other-Arial", new SystemFontResolver([other], index).ResolveFont(new FontQuery("Arial"))?.Name);

            Assert.Equal("ArialMT", new SystemFontResolver([_directory], index).ResolveFont(new FontQuery("Arial"))?.Name);
        }
        finally
        {
            Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void Without_an_index_path_nothing_is_written()
    {
        string[] before = [.. Directory.EnumerateFileSystemEntries(_directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)];
        var resolver = new SystemFontResolver([_directory]);

        Assert.Equal("ArialMT", resolver.ResolveFont(new FontQuery("Arial"))?.Name);
        Assert.Null(resolver.ResolveFont(new FontQuery("Calibri")));

        Assert.Null(resolver.IndexPath);
        Assert.Null(new PdfOptions().SystemFontIndexPath);
        Assert.Equal(before, Directory.EnumerateFileSystemEntries(_directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_store));
    }

    [Fact]
    public void The_options_index_path_makes_the_default_resolver_keep_its_index_there()
    {
        string index = Path.Combine(_store, "options", "fonts.index");
        PdfOptions options = new PdfOptions().WithSystemFontIndexPath(index);
        using PdfDocument document = PdfDocument.Open(Corpus.Path("text-nonembedded-substitute.pdf"), options);
        PdfSimpleFont font = Assert.IsAssignableFrom<PdfSimpleFont>(Assert.Single(document.Pages).GetFont("F1"));

        _ = font.Substitute;

        Assert.Equal(index, options.SystemFontIndexPath);
        Assert.True(File.Exists(index));
    }

    [Fact]
    public void Concurrent_first_lookups_of_one_resolver_agree()
    {
        string index = Path.Combine(_store, "fonts.index");
        var resolver = new SystemFontResolver([_directory], index);
        string[] names = ["Arial,Bold", "TimesNewRoman", "Broadside-Light", "CourierNewPSMT", "Calibri", "ArialNarrow", "Wingdings-Regular", "Arial,Italic"];

        string?[][] results = [.. Enumerable.Range(0, 8).AsParallel().WithDegreeOfParallelism(8).Select(_ => names.Select(name => resolver.ResolveFont(new FontQuery(name))?.Name).ToArray())];

        string?[] expected = ["Arial-BoldMT", "TimesNewRomanPSMT", "Broadside-Light", "CourierNewPSMT", null, "ArialNarrow", "Wingdings-Regular", "Arial-ItalicMT"];
        Assert.All(results, result => Assert.Equal(expected, result));
        Assert.Equal(expected, names.Select(name => new SystemFontResolver([_directory], index).ResolveFont(new FontQuery(name))?.Name));
    }

    [Fact]
    public void The_default_resolver_reads_the_platform_font_directories()
    {
        var resolver = new SystemFontResolver();

        if (OperatingSystem.IsMacOS())
        {
            Assert.Contains("/System/Library/Fonts", resolver.Directories);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.Contains("/usr/share/fonts", resolver.Directories);
        }
        else if (OperatingSystem.IsWindows())
        {
            Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), resolver.Directories);
        }
    }

    private void Install(string relativePath, byte[] data)
    {
        string path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }
}
