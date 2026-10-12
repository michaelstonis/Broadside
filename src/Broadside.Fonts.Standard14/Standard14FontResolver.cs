namespace Broadside.Fonts.Standard14;

/// <summary>
/// The glyphs of the Standard 14 fonts as a font resolver: Liberation Sans, Serif and Mono 2.1.5 for Helvetica, Times and Courier,
/// and Foxit Symbol and Dingbats for Symbol and ZapfDingbats. Register it with <c>PdfOptions.UseStandard14Fonts()</c>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.2.2: the Standard 14 fonts, "or their font metrics and suitable substitution fonts, shall be available to the
/// PDF processor". The metrics are in the core; this package supplies the glyphs, so pages that use non-embedded Standard 14 fonts
/// render the same on every machine, without operating-system fonts (ADR 0007). It answers a query whose
/// <see cref="FontQuery.Standard14"/> is set (any of the fourteen names, or a name the core reads as one of them, such as
/// <c>Arial,Bold</c>) with <see cref="FontMatchKind.Standard14"/>, and the PostScript names of its own fonts (such as
/// <c>LiberationSans-Bold</c>) with <see cref="FontMatchKind.Exact"/>; nothing else.
/// </para>
/// <para>
/// The programs are the upstream files, byte for byte (Liberation from its 2.1.5 release, SIL Open Font License 1.1; "Liberation"
/// is a Reserved Font Name, so the files are never modified; Foxit Symbol and Dingbats from PDFium, BSD-3-Clause, bare CFF programs
/// read by the core's CFF parser). Each is read from the assembly's resources once per process and shared by every engine. The
/// Foxit programs carry StandardEncoding as their built-in encoding; the core maps Symbol and ZapfDingbats codes to glyph names with
/// those fonts' own encodings (Annex D.5, D.6) and selects glyphs by name, so that does not matter.
/// </para>
/// </remarks>
public sealed class Standard14FontResolver : IFontResolver
{
    /// <summary>The program of each Standard 14 font, in <see cref="Standard14Font"/> order: (resource file, font name).</summary>
    private static readonly (string File, string Name)[] Faces =
    [
        ("LiberationMono-Regular.ttf", "LiberationMono"),
        ("LiberationMono-Bold.ttf", "LiberationMono-Bold"),
        ("LiberationMono-Italic.ttf", "LiberationMono-Italic"),
        ("LiberationMono-BoldItalic.ttf", "LiberationMono-BoldItalic"),
        ("LiberationSans-Regular.ttf", "LiberationSans"),
        ("LiberationSans-Bold.ttf", "LiberationSans-Bold"),
        ("LiberationSans-Italic.ttf", "LiberationSans-Italic"),
        ("LiberationSans-BoldItalic.ttf", "LiberationSans-BoldItalic"),
        ("LiberationSerif-Regular.ttf", "LiberationSerif"),
        ("LiberationSerif-Bold.ttf", "LiberationSerif-Bold"),
        ("LiberationSerif-Italic.ttf", "LiberationSerif-Italic"),
        ("LiberationSerif-BoldItalic.ttf", "LiberationSerif-BoldItalic"),
        ("FoxitSymbol.pfb", "FoxitSymbol"),
        ("FoxitDingbats.pfb", "FoxitDingbats"),
    ];

    private static readonly Lazy<byte[]>[] Programs = [.. Faces.Select(face => new Lazy<byte[]>(() => Load(face.File), LazyThreadSafetyMode.ExecutionAndPublication))];

    /// <summary>Returns the bytes of the program that stands in for a Standard 14 font, exactly as upstream ships it.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The program: TrueType for the Latin fonts, bare CFF for Symbol and ZapfDingbats. The same memory on every call.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="font"/> is not one of the fourteen.</exception>
    /// <remarks>ISO 32000-2 §9.6.2.2.</remarks>
    public static ReadOnlyMemory<byte> GetProgram(Standard14Font font)
    {
        if ((uint)font >= (uint)Faces.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(font), font, "Not a Standard 14 font.");
        }

        return Programs[(int)font].Value;
    }

    /// <inheritdoc/>
    /// <remarks>ISO 32000-2 §9.6.2.2.</remarks>
    public FontResolution? ResolveFont(FontQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Standard14 is { } font && (uint)font < (uint)Faces.Length)
        {
            return new FontResolution(GetProgram(font), Faces[(int)font].Name, FontMatchKind.Standard14);
        }

        for (int index = 0; index < Faces.Length; index++)
        {
            if (string.Equals(query.PostScriptName, Faces[index].Name, StringComparison.OrdinalIgnoreCase))
            {
                return new FontResolution(GetProgram((Standard14Font)index), Faces[index].Name, FontMatchKind.Exact);
            }
        }

        return null;
    }

    private static byte[] Load(string file)
    {
        using Stream stream = typeof(Standard14FontResolver).Assembly.GetManifestResourceStream("Broadside.Fonts.Standard14." + file)
            ?? throw new InvalidOperationException($"The Standard 14 fonts package is missing its resource {file}.");
        byte[] data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }
}
