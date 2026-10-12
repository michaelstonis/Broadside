namespace Broadside.Fonts;

/// <summary>What an <see cref="IFontResolver"/> found for a font: the bytes of a font program and how it matches.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.2.1 (Table 109, <c>BaseFont</c>: the name "may be used to find the font program in the PDF processor or its
/// environment") and §9.6.2.2. The bytes are read by the engine's font program parsers, exactly as an embedded program would be, so
/// any format they read is allowed (TrueType, OpenType, a TrueType collection, bare CFF, Type 1).
/// </para>
/// <para>
/// The bytes are shared by every document and thread of the engine and must not change afterwards; a document parses each distinct
/// (bytes, face) once, so a resolver should return the same memory for the same program rather than a fresh copy each time.
/// </para>
/// </remarks>
public sealed class FontResolution
{
    /// <summary>Initializes a new instance of the <see cref="FontResolution"/> class.</summary>
    /// <param name="data">The font program's bytes.</param>
    /// <param name="name">The name of the font found, such as its PostScript name; diagnostics show it.</param>
    /// <param name="matchKind">How the font found relates to the font asked for.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or <paramref name="data"/> is empty.</exception>
    public FontResolution(ReadOnlyMemory<byte> data, string name, FontMatchKind matchKind)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (data.IsEmpty)
        {
            throw new ArgumentException("A font program has at least one byte.", nameof(data));
        }

        Data = data;
        Name = name;
        MatchKind = matchKind;
    }

    /// <summary>Gets the font program's bytes.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the name of the font found.</summary>
    public string Name { get; }

    /// <summary>Gets how the font found relates to the font asked for.</summary>
    public FontMatchKind MatchKind { get; }

    /// <summary>Gets which font of a font collection (a TrueType collection, "ttcf") to read; default 0.</summary>
    public int FaceIndex { get; init; }
}
