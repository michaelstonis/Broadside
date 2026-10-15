using System.Runtime.InteropServices;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Rendering;

/// <summary>
/// The device colourants of an overprint-preview render: one plane of 8-bit tints per colourant (0 no ink, 255 full ink), the four
/// process colourants first (cyan, magenta, yellow, black), then one per spot colourant in the order the page first paints them,
/// each with its process equivalent at tint 1. <see cref="Finish"/> converts them for display.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.6.7 and §11.7.4: overprinting acts on device colourants, so a preview keeps them separate while the page is
/// painted (§11.7.3: spot colourants are components of their own) and converts them only at the end, as print-preview viewers do.
/// The display conversion follows Ghostscript's overprint simulation: each spot adds tint × its equivalent CMYK to the process
/// colourants, each sum clipped at 1, and the CMYK result converts like DeviceCMYK; a pixel with no ink is the white paper.
/// </para>
/// <para>
/// Planes are pooled arrays kept from render to render, so a warm render allocates for its new spot colourants at most, never per
/// paint or pixel.
/// </para>
/// </remarks>
internal sealed class ColorantPlanes : IDisposable
{
    /// <summary>The number of process colourants, which take planes 0 to 3.</summary>
    public const int ProcessCount = 4;

    private readonly Dictionary<CosName, int> _spots = [];
    private byte[][] _planes = new byte[8][];
    private CosName[] _spotNames = new CosName[4];
    private byte[] _equivalents = new byte[16];
    private byte[] _cmykRow = [];
    private byte[] _runRow = [];
    private byte[] _rgbRow = [];

    public ColorantPlanes()
    {
        for (int index = 0; index < _planes.Length; index++)
        {
            _planes[index] = [];
        }
    }

    /// <summary>Gets the width of the planes in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the height of the planes in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the number of planes: the process colourants and every spot colourant registered so far.</summary>
    public int Count { get; private set; }

    /// <summary>Returns the tints of plane <paramref name="index"/>, row by row.</summary>
    public Span<byte> Plane(int index) => _planes[index].AsSpan(0, Width * Height);

    /// <summary>Starts a render: the process planes, cleared to no ink, and no spot colourant.</summary>
    public void Begin(int width, int height)
    {
        Width = width;
        Height = height;
        Count = 0;
        _spots.Clear();
        for (int index = 0; index < ProcessCount; index++)
        {
            AddPlane();
        }
    }

    /// <summary>Returns the plane of the spot colourant <paramref name="name"/>, if it has one yet.</summary>
    public bool TryGetSpot(CosName name, out int plane) => _spots.TryGetValue(name, out plane);

    /// <summary>Adds a plane, with no ink yet, for the spot colourant <paramref name="name"/>.</summary>
    /// <param name="name">The colourant's name, not registered yet.</param>
    /// <param name="equivalent">Its process equivalent at tint 1: cyan, magenta, yellow and black, 0 to 1.</param>
    /// <returns>The plane index.</returns>
    public int AddSpot(CosName name, ReadOnlySpan<float> equivalent)
    {
        int plane = AddPlane();
        int spot = plane - ProcessCount;
        if (spot == _spotNames.Length)
        {
            Array.Resize(ref _spotNames, spot * 2);
            Array.Resize(ref _equivalents, spot * 8);
        }

        _spotNames[spot] = name;
        for (int c = 0; c < ProcessCount; c++)
        {
            _equivalents[(spot * ProcessCount) + c] = (byte)((Math.Clamp(equivalent[c], 0f, 1f) * 255f) + 0.5f);
        }

        _spots.Add(name, plane);
        return plane;
    }

    /// <summary>
    /// Converts every pixel's colourants to opaque premultiplied BGRA in the top-left <see cref="Width"/> × <see cref="Height"/>
    /// pixels of <paramref name="target"/>, through <paramref name="converter"/> (DeviceCMYK to RGB), then forgets the spot colourants.
    /// </summary>
    public void Finish(Bitmap target, PdfColorConverter converter)
    {
        int width = Width;
        Pooled.Ensure(ref _cmykRow, width * ProcessCount);
        Pooled.Ensure(ref _runRow, width * ProcessCount);
        Pooled.Ensure(ref _rgbRow, width * 3);
        Span<byte> cmyk = _cmykRow.AsSpan(0, width * ProcessCount);
        Span<byte> rgb = _rgbRow.AsSpan(0, width * 3);
        int spots = Count - ProcessCount;
        for (int y = 0; y < Height; y++)
        {
            int offset = y * width;
            ReadOnlySpan<byte> c = _planes[0].AsSpan(offset, width);
            ReadOnlySpan<byte> m = _planes[1].AsSpan(offset, width);
            ReadOnlySpan<byte> ye = _planes[2].AsSpan(offset, width);
            ReadOnlySpan<byte> k = _planes[3].AsSpan(offset, width);
            for (int x = 0; x < width; x++)
            {
                cmyk[ProcessCount * x] = c[x];
                cmyk[(ProcessCount * x) + 1] = m[x];
                cmyk[(ProcessCount * x) + 2] = ye[x];
                cmyk[(ProcessCount * x) + 3] = k[x];
            }

            for (int spot = 0; spot < spots; spot++)
            {
                ReadOnlySpan<byte> tints = _planes[ProcessCount + spot].AsSpan(offset, width);
                ReadOnlySpan<byte> equivalent = _equivalents.AsSpan(spot * ProcessCount, ProcessCount);
                for (int x = 0; x < width; x++)
                {
                    int tint = tints[x];
                    if (tint == 0)
                    {
                        continue;
                    }

                    for (int i = 0; i < ProcessCount; i++)
                    {
                        int index = (ProcessCount * x) + i;
                        cmyk[index] = (byte)Math.Min(255, cmyk[index] + Compositor.Divide255(tint * equivalent[i]));
                    }
                }
            }

            // Pages are mostly runs of one colour: convert each run once. No ink is the paper, white, whatever the CMYK conversion
            // makes of 0 0 0 0.
            ReadOnlySpan<uint> inks = MemoryMarshal.Cast<byte, uint>(cmyk);
            Span<uint> runs = MemoryMarshal.Cast<byte, uint>(_runRow.AsSpan(0, width * ProcessCount));
            int count = 0;
            foreach (uint ink in inks)
            {
                if (ink != 0 && (count == 0 || runs[count - 1] != ink))
                {
                    runs[count++] = ink;
                }
            }

            converter.Convert(_runRow.AsSpan(0, count * ProcessCount), rgb, count);
            Span<uint> row = MemoryMarshal.Cast<byte, uint>(target.GetRow(y)[..(width * 4)]);
            int run = -1;
            for (int x = 0; x < width; x++)
            {
                uint ink = inks[x];
                if (ink == 0)
                {
                    row[x] = uint.MaxValue;
                    continue;
                }

                if (run < 0 || runs[run] != ink)
                {
                    run++;
                }

                row[x] = Pack(rgb[3 * run], rgb[(3 * run) + 1], rgb[(3 * run) + 2]);
            }
        }

        Forget();
    }

    /// <summary>Lets go of the render's colourant names; the planes stay for the next render.</summary>
    public void Forget()
    {
        _spots.Clear();
        Array.Clear(_spotNames);
        Count = 0;
    }

    /// <summary>Returns the pooled planes and rows.</summary>
    public void Dispose()
    {
        Forget();
        for (int index = 0; index < _planes.Length; index++)
        {
            Pooled.Return(ref _planes[index]);
        }

        Pooled.Return(ref _cmykRow);
        Pooled.Return(ref _runRow);
        Pooled.Return(ref _rgbRow);
    }

    private static uint Pack(byte r, byte g, byte b) => b | ((uint)g << 8) | ((uint)r << 16) | 0xFF000000u;

    private int AddPlane()
    {
        int index = Count;
        if (index == _planes.Length)
        {
            Array.Resize(ref _planes, index * 2);
            for (int i = index; i < _planes.Length; i++)
            {
                _planes[i] = [];
            }
        }

        Pooled.Ensure(ref _planes[index], Width * Height);
        _planes[index].AsSpan(0, Width * Height).Clear();
        Count = index + 1;
        return index;
    }
}
