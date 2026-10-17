using System.Runtime.InteropServices;

namespace Broadside.Rendering.WebGL;

/// <summary>
/// The vertices and commands of one render, built by <see cref="Recorder"/> and executed by <c>broadside-webgl.js</c> in one call.
/// Vertices are four floats (device x, device y with y down, texture u, v); the first six are always the whole-viewport quad that
/// resolves a clip. Kept across renders, so a warm render allocates nothing here.
/// </summary>
internal sealed class CommandList
{
    public const int Stencil = 1;
    public const int Cover = 2;
    public const int Solid = 3;
    public const int Clip = 4;
    public const int ClipReset = 5;
    public const int Image = 6;
    public const int Mask = 7;

    public const int NonZero = 0;
    public const int EvenOdd = 1;
    public const int Union = 2;

    private float[] _vertices = new float[16384];
    private int[] _commands = new int[4096];
    private int _floats;
    private int _count;

    public int VertexCount => _floats / 4;

    public int CommandCount => _count;

    public Span<byte> VertexBytes => MemoryMarshal.AsBytes(_vertices.AsSpan(0, _floats));

    public Span<int> Commands => _commands.AsSpan(0, _count);

    public void Clear(float width, float height)
    {
        _floats = 0;
        _count = 0;
        AddQuad(0, 0, width, height);
    }

    public void Add(float x, float y)
    {
        if (_floats + 4 > _vertices.Length)
        {
            Array.Resize(ref _vertices, _vertices.Length * 2);
        }

        _vertices[_floats] = x;
        _vertices[_floats + 1] = y;
        _vertices[_floats + 2] = 0;
        _vertices[_floats + 3] = 0;
        _floats += 4;
    }

    public void Add(float x, float y, float u, float v)
    {
        if (_floats + 4 > _vertices.Length)
        {
            Array.Resize(ref _vertices, _vertices.Length * 2);
        }

        _vertices[_floats] = x;
        _vertices[_floats + 1] = y;
        _vertices[_floats + 2] = u;
        _vertices[_floats + 3] = v;
        _floats += 4;
    }

    /// <summary>Adds an axis-aligned rectangle as two triangles.</summary>
    public void AddQuad(float left, float top, float right, float bottom)
    {
        Add(left, top);
        Add(right, top);
        Add(right, bottom);
        Add(left, top);
        Add(right, bottom);
        Add(left, bottom);
    }

    public void Command(int op, int a, int b, int c)
    {
        Reserve(4);
        _commands[_count++] = op;
        _commands[_count++] = a;
        _commands[_count++] = b;
        _commands[_count++] = c;
    }

    public void Command(int op)
    {
        Reserve(1);
        _commands[_count++] = op;
    }

    public void Paint(int op, int first, int count, ReadOnlySpan<float> color)
    {
        Reserve(7);
        _commands[_count++] = op;
        _commands[_count++] = first;
        _commands[_count++] = count;
        for (int i = 0; i < 4; i++)
        {
            _commands[_count++] = BitConverter.SingleToInt32Bits(color[i]);
        }
    }

    public void Texture(int op, int texture, int first, int count, ReadOnlySpan<float> color)
    {
        Reserve(8);
        _commands[_count++] = op;
        _commands[_count++] = texture;
        _commands[_count++] = first;
        _commands[_count++] = count;
        for (int i = 0; i < 4; i++)
        {
            _commands[_count++] = BitConverter.SingleToInt32Bits(color[i]);
        }
    }

    private void Reserve(int count)
    {
        if (_count + count > _commands.Length)
        {
            Array.Resize(ref _commands, Math.Max(_commands.Length * 2, _count + count));
        }
    }
}
