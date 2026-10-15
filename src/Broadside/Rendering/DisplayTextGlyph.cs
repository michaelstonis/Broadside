using System.Runtime.InteropServices;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// Where one shown character code lies on the page and the text it stands for, as a display list keeps it for hit-testing: its box
/// as a parallelogram (origin corner and the two edge vectors, in single precision) and a slice of the list's text, 40 bytes.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4.4 (the text rendering matrix and the CTM), §9.2.4 (w0) and §9.10.2 (Unicode). The box is the one
/// <see cref="Text.TextGlyph.Quad"/> describes: from the origin to w0 along the baseline, from the font's descent to its ascent
/// across it. A box mapped from text space by a matrix is always a parallelogram, so three points describe it.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly struct DisplayTextGlyph
{
    private const byte Vertical = 0x80;

    private readonly float _x;
    private readonly float _y;
    private readonly float _baselineX;
    private readonly float _baselineY;
    private readonly float _acrossX;
    private readonly float _acrossY;
    private readonly int _textStart;
    private readonly uint _characterCode;
    private readonly ushort _textLength;
    private readonly byte _codeLength;
    private readonly byte _mode;

    /// <summary>Initializes a new instance of the <see cref="DisplayTextGlyph"/> struct.</summary>
    /// <param name="quad">The box, a parallelogram: only its bottom-left, bottom-right and top-left corners are kept.</param>
    /// <param name="textStart">Where the code's text starts in the list's text.</param>
    /// <param name="textLength">How many UTF-16 units the code's text takes (at most <see cref="Fonts.FontUnicode.MaxLength"/>).</param>
    /// <param name="characterCode">The character code.</param>
    /// <param name="codeLength">The number of bytes the code takes.</param>
    /// <param name="mode">The text rendering mode.</param>
    /// <param name="isVertical">Whether the font writes vertically.</param>
    public DisplayTextGlyph(in Quad quad, int textStart, int textLength, uint characterCode, int codeLength, TextRenderingMode mode, bool isVertical)
    {
        _x = (float)quad.BottomLeft.X;
        _y = (float)quad.BottomLeft.Y;
        _baselineX = (float)(quad.BottomRight.X - quad.BottomLeft.X);
        _baselineY = (float)(quad.BottomRight.Y - quad.BottomLeft.Y);
        _acrossX = (float)(quad.TopLeft.X - quad.BottomLeft.X);
        _acrossY = (float)(quad.TopLeft.Y - quad.BottomLeft.Y);
        _textStart = textStart;
        _textLength = (ushort)textLength;
        _characterCode = characterCode;
        _codeLength = (byte)codeLength;
        _mode = (byte)((byte)mode | (isVertical ? Vertical : 0));
    }

    /// <summary>Gets the box's bottom-left corner.</summary>
    public PathPoint BottomLeft => new(_x, _y);

    /// <summary>Gets the vector from the bottom-left to the bottom-right corner (along the baseline).</summary>
    public PathPoint Baseline => new(_baselineX, _baselineY);

    /// <summary>Gets the vector from the bottom-left to the top-left corner (across the baseline).</summary>
    public PathPoint Across => new(_acrossX, _acrossY);

    /// <summary>Gets the box as four corners.</summary>
    public Quad Quad
    {
        get
        {
            double x = _x;
            double y = _y;
            return new Quad(
                new PathPoint(x, y),
                new PathPoint(x + _baselineX, y + _baselineY),
                new PathPoint(x + _baselineX + _acrossX, y + _baselineY + _acrossY),
                new PathPoint(x + _acrossX, y + _acrossY));
        }
    }

    /// <summary>Gets where the code's text starts in the list's text.</summary>
    public int TextStart => _textStart;

    /// <summary>Gets how many UTF-16 units the code's text takes.</summary>
    public int TextLength => _textLength;

    /// <summary>Gets the character code.</summary>
    public uint CharacterCode => _characterCode;

    /// <summary>Gets the number of bytes the code takes.</summary>
    public int CodeLength => _codeLength;

    /// <summary>Gets the text rendering mode.</summary>
    public TextRenderingMode Mode => (TextRenderingMode)(_mode & ~Vertical);

    /// <summary>Gets a value indicating whether the font writes vertically.</summary>
    public bool IsVertical => (_mode & Vertical) != 0;
}
