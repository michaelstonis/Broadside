namespace Broadside.Fonts;

/// <summary>
/// The font resolver extension point: finds a font program for a font that is not embedded, and the named resources fonts need
/// (predefined CMaps, CID-to-Unicode tables). Register one with <see cref="PdfOptions.UseFontResolver"/>.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.2.2 (the Standard 14 fonts, "or their font metrics and suitable substitution fonts, shall be available"),
/// §9.8 (the font descriptor, which "enables a PDF processor to synthesise a substitute font or select a similar font"), §9.7.5.2
/// (predefined CMaps) and §9.10.2 (CID-to-Unicode tables). ADR 0007 and ADR 0009.
/// </para>
/// <para>
/// An engine asks its resolvers in order: those registered with <see cref="PdfOptions.UseFontResolver"/>, in registration order,
/// then the operating system's (<see cref="SystemFontResolver"/> unless replaced with
/// <see cref="PdfOptions.UseSystemFontResolver"/>). The first answer wins. When none answers for a font that is not one of the
/// Standard 14 fonts, the engine picks the Standard 14 font most similar to it (<see cref="FontMatchKind.Similar"/>) and asks
/// again for that; when that fails too, it records a diagnostic and the font's glyphs are not drawn (its widths and glyph names
/// still apply).
/// </para>
/// <para>
/// Both members have a default that finds nothing, so a resolver implements only what it supplies. A resolver is shared by every
/// document and thread of the engine: it must be thread-safe, keep no per-call state, and must not throw for a font or resource it
/// does not have.
/// </para>
/// </remarks>
public interface IFontResolver
{
    /// <summary>Finds a font program for a font that is not embedded, or whose embedded program cannot be read.</summary>
    /// <param name="query">What is known about the font: its name, type and font descriptor facts.</param>
    /// <returns>The program found, or <see langword="null"/> when this resolver has none for the font.</returns>
    FontResolution? ResolveFont(FontQuery query) => null;

    /// <summary>Finds a named resource: the bytes of a CMap file in the text syntax of Adobe Technical Note #5014.</summary>
    /// <param name="kind">The kind of resource.</param>
    /// <param name="name">Its name, as <see cref="FontResourceKind"/> describes for each kind.</param>
    /// <param name="data">The file's bytes; they must not change afterwards.</param>
    /// <returns><see langword="true"/> when this resolver has the resource.</returns>
    bool TryResolveResource(FontResourceKind kind, string name, out ReadOnlyMemory<byte> data)
    {
        data = default;
        return false;
    }
}
