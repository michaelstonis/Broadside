using System.Runtime.CompilerServices;
using Broadside.Text;

namespace Broadside.Tests.Text;

/// <summary>
/// The bytes a <see cref="TextPage"/>'s output takes on the 64-bit heap, the floor of what extracting it can allocate (issue #284):
/// its glyph array and text, and once assembled its word and line arrays and plain text.
/// </summary>
internal static class TextPageSize
{
    /// <summary>The glyph array and the text.</summary>
    public static long Extracted(TextPage page) => ArrayBytes<TextGlyph>(page.Glyphs.Count) + StringBytes(page.Text.Length);

    /// <summary>The glyph array and the text, the word and line arrays and the plain text.</summary>
    public static long Assembled(TextPage page) =>
        Extracted(page) + ArrayBytes<TextWord>(page.Words.Count) + ArrayBytes<TextLine>(page.Lines.Count) + StringBytes(page.PlainText.Length);

    /// <summary>An array: method table, length and padding (24 bytes), then its elements rounded up to 8 bytes.</summary>
    private static long ArrayBytes<T>(int length) => 24 + RoundUp((long)length * Unsafe.SizeOf<T>());

    /// <summary>A string: method table and length (12 bytes), its UTF-16 units and a terminator, rounded up to 8 bytes.</summary>
    private static long StringBytes(int length) => RoundUp(12 + (((long)length + 1) * 2));

    private static long RoundUp(long bytes) => (bytes + 7) & ~7L;
}
