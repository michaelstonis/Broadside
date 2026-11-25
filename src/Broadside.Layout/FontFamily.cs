using Broadside.Fonts;
using Broadside.Fonts.Standard14;

namespace Broadside.Layout.Flow;

/// <summary>The Standard 14 families a <see cref="FontFamily"/> can stand on, each embedded as its metric-compatible Liberation program.</summary>
/// <remarks>ISO 32000-2 §9.6.2.2.</remarks>
public enum Standard14Family
{
    /// <summary>Helvetica, embedded as Liberation Sans.</summary>
    Helvetica,

    /// <summary>Times, embedded as Liberation Serif.</summary>
    Times,

    /// <summary>Courier, embedded as Liberation Mono.</summary>
    Courier,
}

/// <summary>
/// A font family for the document builder: up to four faces (regular, bold, italic, bold italic) named independently of any document,
/// embedded in each document on first use and subset when it is saved. <c>Bold()</c> and <c>Italic()</c> pick the face; a missing face
/// falls back to the regular one with a diagnostic (a bold face is never synthesized). Characters a face cannot encode are looked up in the
/// <see cref="Fallbacks"/>, in order.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.9 (embedded font programs): every face is embedded, so the document does not depend on the reader's fonts. A family is
/// immutable and safe to share across documents and threads; the program bytes are parsed once per document.
/// </para>
/// <para>Minimum PDF version: 1.2 for a TrueType program, 1.6 for an OpenType one; the document raises its version as needed.</para>
/// </remarks>
public sealed class FontFamily
{
    private static readonly FontFamily[] NoFallbacks = [];

    private readonly ReadOnlyMemory<byte>[]? _programs;
    private readonly Standard14Family? _standard;
    private readonly string? _resolve;
    private readonly FontFamily[] _fallbacks;

    private FontFamily(string name, ReadOnlyMemory<byte>[]? programs, Standard14Family? standard, string? resolve, FontFamily[] fallbacks)
    {
        Name = name;
        _programs = programs;
        _standard = standard;
        _resolve = resolve;
        _fallbacks = fallbacks;
    }

    /// <summary>Gets a name for the family in diagnostics.</summary>
    public string Name { get; }

    /// <summary>Gets the families searched, in order, for characters this family's faces cannot encode.</summary>
    public IReadOnlyList<FontFamily> Fallbacks => _fallbacks;

    /// <summary>Returns a family made of the given font programs (TrueType or OpenType bytes, such as a file's contents).</summary>
    /// <param name="regular">The regular face.</param>
    /// <param name="bold">The bold face, or empty for none.</param>
    /// <param name="italic">The italic face, or empty for none.</param>
    /// <param name="boldItalic">The bold italic face, or empty for none.</param>
    /// <returns>The family.</returns>
    /// <exception cref="ArgumentException"><paramref name="regular"/> is empty.</exception>
    /// <remarks>The programs are parsed when a document first uses the face; a program that does not parse throws then.</remarks>
    public static FontFamily FromPrograms(ReadOnlyMemory<byte> regular, ReadOnlyMemory<byte> bold = default, ReadOnlyMemory<byte> italic = default, ReadOnlyMemory<byte> boldItalic = default)
    {
        if (regular.IsEmpty)
        {
            throw new ArgumentException("The regular face shall be a font program.", nameof(regular));
        }

        return new FontFamily("font programs", [regular, bold, italic, boldItalic], null, null, NoFallbacks);
    }

    /// <summary>
    /// Returns a family found by name through the document's font resolvers (<see cref="PdfOptions.UseFontResolver"/> and the system
    /// font resolver). When a document's resolvers do not find it, the family falls back to Helvetica (embedded Liberation Sans) with a
    /// diagnostic.
    /// </summary>
    /// <param name="familyName">The family name, such as <c>Noto Sans</c>.</param>
    /// <returns>The family.</returns>
    /// <exception cref="ArgumentException"><paramref name="familyName"/> is null or empty.</exception>
    public static FontFamily Resolve(string familyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(familyName);
        return new FontFamily(familyName, null, null, familyName, NoFallbacks);
    }

    /// <summary>Returns a Standard 14 family, embedded as its metric-compatible Liberation program (all four faces).</summary>
    /// <param name="family">The family.</param>
    /// <returns>The family.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="family"/> is not a defined value.</exception>
    public static FontFamily Standard(Standard14Family family) => family switch
    {
        Standard14Family.Helvetica => Helvetica,
        Standard14Family.Times => Times,
        Standard14Family.Courier => Courier,
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown Standard 14 family."),
    };

    /// <summary>Returns a copy that also searches <paramref name="fallback"/> for characters the faces cannot encode, after the current fallbacks.</summary>
    /// <param name="fallback">The fallback family.</param>
    /// <returns>The new family.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fallback"/> is null.</exception>
    public FontFamily WithFallback(FontFamily fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        return new FontFamily(Name, _programs, _standard, _resolve, [.. _fallbacks, fallback]);
    }

    /// <inheritdoc/>
    public override string ToString() => Name;

    private static FontFamily Helvetica { get; } = new("Helvetica", null, Standard14Family.Helvetica, null, NoFallbacks);

    private static FontFamily Times { get; } = new("Times", null, Standard14Family.Times, null, NoFallbacks);

    private static FontFamily Courier { get; } = new("Courier", null, Standard14Family.Courier, null, NoFallbacks);

    /// <summary>Embeds <paramref name="face"/> in <paramref name="document"/>, or returns null when the family has no such face.</summary>
    internal PdfCanvasFont? Embed(PdfDocument document, FontFace face)
    {
        if (_programs is { } programs)
        {
            ReadOnlyMemory<byte> program = programs[(int)face];
            return program.IsEmpty ? null : document.EmbedFont(program);
        }

        if (_standard is { } standard)
        {
            return document.EmbedFont(Standard14FontResolver.GetProgram(ProgramOf(standard, face)));
        }

        var query = new FontQuery(_resolve!)
        {
            FontFamily = _resolve,
            FontWeight = face is FontFace.Bold or FontFace.BoldItalic ? 700 : 400,
            ItalicAngle = face is FontFace.Italic or FontFace.BoldItalic ? -12 : 0,
        };

        return document.EmbedFont(query);
    }

    /// <summary>True when this family is found by name through the document's resolvers (and may therefore be missing).</summary>
    internal bool IsResolved => _resolve is not null;

    private static Standard14Font ProgramOf(Standard14Family family, FontFace face) => (family, face) switch
    {
        (Standard14Family.Times, FontFace.Regular) => Standard14Font.TimesRoman,
        (Standard14Family.Times, FontFace.Bold) => Standard14Font.TimesBold,
        (Standard14Family.Times, FontFace.Italic) => Standard14Font.TimesItalic,
        (Standard14Family.Times, _) => Standard14Font.TimesBoldItalic,
        (Standard14Family.Courier, FontFace.Regular) => Standard14Font.Courier,
        (Standard14Family.Courier, FontFace.Bold) => Standard14Font.CourierBold,
        (Standard14Family.Courier, FontFace.Italic) => Standard14Font.CourierOblique,
        (Standard14Family.Courier, _) => Standard14Font.CourierBoldOblique,
        (_, FontFace.Regular) => Standard14Font.Helvetica,
        (_, FontFace.Bold) => Standard14Font.HelveticaBold,
        (_, FontFace.Italic) => Standard14Font.HelveticaOblique,
        _ => Standard14Font.HelveticaBoldOblique,
    };
}

/// <summary>One of the four faces of a <see cref="FontFamily"/>.</summary>
internal enum FontFace
{
    Regular,
    Bold,
    Italic,
    BoldItalic,
}
