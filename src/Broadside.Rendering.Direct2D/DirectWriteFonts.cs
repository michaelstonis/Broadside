using System.Runtime.CompilerServices;
using Broadside.Fonts;
using Broadside.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.DirectWrite;

namespace Broadside.Rendering.Direct2D;

/// <summary>
/// The DirectWrite side of one <see cref="Direct2DRenderer"/>: an isolated DirectWrite factory, an in-memory font file loader, the
/// rendering parameters glyphs are drawn with, and one font face per TrueType or OpenType program, loaded from the program's bytes
/// the first time a glyph run of it is drawn.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.9 (Table 124: <c>FontFile2</c> and <c>FontFile3</c> <c>OpenType</c>) and open decision 7 of the spec: native glyph
/// rendering covers TrueType and OpenType programs only, through <see cref="FontProgram.TryGetSfntData"/>, so a program that needed a
/// repair is never handed to DirectWrite. A face is used only when DirectWrite reads the file as the program does: the same glyph
/// count, and design units that the program's font matrix maps to text space (§9.2.4, <c>[1/unitsPerEm 0 0 1/unitsPerEm 0 0]</c>).
/// </para>
/// <para>
/// Faces are kept per program with a weak key, so a renderer kept for a viewer's lifetime does not keep closed documents alive; past
/// <see cref="MaxFaces"/> every face and the loader's copies of the files are released and loaded again on use. Everything native is
/// released on <see cref="Dispose"/>.
/// </para>
/// </remarks>
internal sealed unsafe class DirectWriteFonts : IDisposable
{
    /// <summary>The most faces kept; past it every face (and the loader holding the files' bytes) is released.</summary>
    private const int MaxFaces = 256;

    private readonly List<Face> _created = [];
    private IDWriteFactory5* _factory;
    private IDWriteInMemoryFontFileLoader* _loader;
    private IDWriteRenderingParams3* _parameters;
    private ConditionalWeakTable<FontProgram, Face> _faces = [];
    private FontProgram? _lastProgram;
    private Face? _lastFace;
    private bool _unavailable;

    /// <summary>Gets how many font faces DirectWrite has created from font programs since the renderer was created.</summary>
    public int FacesCreated { get; private set; }

    /// <summary>
    /// Gets the rendering parameters glyphs are drawn with: grayscale, natural symmetric anti-aliasing with no grid fitting (glyphs are
    /// drawn where their outlines are, §9.2.4), and gamma 1 with no contrast enhancement, so that coverage is linear like the software
    /// rasterizer's; <see langword="null"/> when DirectWrite is not available.
    /// </summary>
    public IDWriteRenderingParams* RenderingParameters => EnsureFactory() ? (IDWriteRenderingParams*)_parameters : null;

    /// <summary>
    /// Returns the face DirectWrite draws <paramref name="program"/>'s glyphs with, or <see langword="null"/> when it cannot:
    /// <paramref name="isSfnt"/> tells whether the program was a TrueType or OpenType program, which a native backend should have
    /// drawn natively (so a fallback to outlines is worth reporting).
    /// </summary>
    public Face? GetFace(FontProgram program, out bool isSfnt)
    {
        isSfnt = program.Format is FontProgramFormat.TrueType or FontProgramFormat.OpenType;
        if (ReferenceEquals(program, _lastProgram))
        {
            return _lastFace!.Pointer is null ? null : _lastFace;
        }

        if (!_faces.TryGetValue(program, out Face? face))
        {
            if (_created.Count >= MaxFaces)
            {
                ReleaseFaces();
            }

            face = isSfnt ? Create(program) : Face.None;
            _faces.Add(program, face);
            if (face.Pointer is not null)
            {
                _created.Add(face);
            }
        }

        _lastProgram = program;
        _lastFace = face;
        return face.Pointer is null ? null : face;
    }

    public void Dispose()
    {
        ReleaseFaces();
        Release(ref _parameters);
        Release(ref _factory);
    }

    private static void Release<T>(ref T* pointer)
        where T : unmanaged
    {
        if (pointer is not null)
        {
            ((Windows.Win32.System.Com.IUnknown*)pointer)->Release();
            pointer = null;
        }
    }

    /// <summary>Creates the isolated factory and the rendering parameters once; false when DirectWrite is not available.</summary>
    private bool EnsureFactory()
    {
        if (_factory is not null)
        {
            return true;
        }

        if (_unavailable)
        {
            return false;
        }

        if (PInvoke.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_ISOLATED, out IDWriteFactory5* factory).Failed)
        {
            _unavailable = true;
            return false;
        }

        try
        {
            IDWriteRenderingParams3* parameters;
            factory->CreateCustomRenderingParams(
                1f,
                0f,
                0f,
                0f,
                DWRITE_PIXEL_GEOMETRY.DWRITE_PIXEL_GEOMETRY_FLAT,
                DWRITE_RENDERING_MODE1.DWRITE_RENDERING_MODE1_NATURAL_SYMMETRIC,
                DWRITE_GRID_FIT_MODE.DWRITE_GRID_FIT_MODE_DISABLED,
                &parameters);
            _parameters = parameters;
        }
        catch (Exception exception) when (exception is global::System.Runtime.InteropServices.COMException or ArgumentException)
        {
            factory->Release();
            _unavailable = true;
            return false;
        }

        _factory = factory;
        return true;
    }

    /// <summary>Creates the in-memory font file loader, registered with the factory, when there is none.</summary>
    private void EnsureLoader()
    {
        if (_loader is not null)
        {
            return;
        }

        IDWriteInMemoryFontFileLoader* loader;
        _factory->CreateInMemoryFontFileLoader(&loader);
        try
        {
            _factory->RegisterFontFileLoader((IDWriteFontFileLoader*)loader);
        }
        catch
        {
            loader->Release();
            throw;
        }

        _loader = loader;
    }

    /// <summary>
    /// Loads a TrueType or OpenType program into a DirectWrite font face (ISO 32000-2 §9.9); <see cref="Face.Unavailable"/> when the
    /// program has no intact sfnt bytes, DirectWrite rejects them, or it reads them differently from the program.
    /// </summary>
    private Face Create(FontProgram program)
    {
        if (!program.TryGetSfntData(out ReadOnlyMemory<byte> data, out int faceIndex) || data.IsEmpty || faceIndex < 0 || !EnsureFactory())
        {
            return Face.Unavailable;
        }

        IDWriteFontFile* file = null;
        IDWriteFontFace* face = null;
        try
        {
            EnsureLoader();
            fixed (byte* bytes = data.Span)
            {
                // No owner object: the loader keeps its own copy of the bytes, released with the loader.
                _loader->CreateInMemoryFontFileReference((IDWriteFactory*)_factory, bytes, (uint)data.Length, null, &file);
            }

            BOOL supported;
            DWRITE_FONT_FILE_TYPE fileType;
            DWRITE_FONT_FACE_TYPE faceType;
            uint faces;
            file->Analyze(&supported, &fileType, &faceType, &faces);
            if (!supported || (uint)faceIndex >= faces
                || faceType is not (DWRITE_FONT_FACE_TYPE.DWRITE_FONT_FACE_TYPE_TRUETYPE or DWRITE_FONT_FACE_TYPE.DWRITE_FONT_FACE_TYPE_CFF
                    or DWRITE_FONT_FACE_TYPE.DWRITE_FONT_FACE_TYPE_TRUETYPE_COLLECTION))
            {
                return Face.Unavailable;
            }

            _factory->CreateFontFace(faceType, 1, &file, (uint)faceIndex, DWRITE_FONT_SIMULATIONS.DWRITE_FONT_SIMULATIONS_NONE, &face);
            FacesCreated++;
            DWRITE_FONT_METRICS metrics;
            face->GetMetrics(&metrics);
            if (face->GetGlyphCount() != program.GlyphCount || metrics.designUnitsPerEm == 0 || !MapsDesignUnits(program.FontMatrix, metrics.designUnitsPerEm))
            {
                return Face.Unavailable;
            }

            var result = new Face(face, metrics.designUnitsPerEm, program.GlyphCount);
            face = null;
            return result;
        }
        catch (Exception exception) when (exception is global::System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // DirectWrite validates the file and refuses what it cannot read (DWRITE_E_FILEFORMAT): its glyphs draw from their outlines.
            return Face.Unavailable;
        }
        finally
        {
            Release(ref face);
            Release(ref file);
        }
    }

    /// <summary>Whether the font matrix is <c>[1/unitsPerEm 0 0 1/unitsPerEm 0 0]</c>, the mapping DirectWrite applies to design units.</summary>
    private static bool MapsDesignUnits(in Matrix fontMatrix, ushort unitsPerEm)
    {
        double expected = 1.0 / unitsPerEm;
        const double tolerance = 1e-9;
        return Math.Abs(fontMatrix.A - expected) <= tolerance * expected && Math.Abs(fontMatrix.D - expected) <= tolerance * expected
            && fontMatrix.B == 0 && fontMatrix.C == 0 && fontMatrix.E == 0 && fontMatrix.F == 0;
    }

    /// <summary>Releases every face and the loader that holds the files' bytes; they load again on use.</summary>
    private void ReleaseFaces()
    {
        foreach (Face face in _created)
        {
            face.Release();
        }

        _created.Clear();
        _faces = [];
        _lastProgram = null;
        _lastFace = null;
        if (_loader is not null)
        {
            if (_factory is not null)
            {
                _factory->UnregisterFontFileLoader((IDWriteFontFileLoader*)_loader);
            }

            Release(ref _loader);
        }
    }

    /// <summary>A DirectWrite font face of one program, or none (<see cref="Pointer"/> null) when its glyphs draw from their outlines.</summary>
    internal sealed class Face
    {
        public Face(IDWriteFontFace* pointer, ushort unitsPerEm, int glyphCount)
        {
            Pointer = pointer;
            UnitsPerEm = unitsPerEm;
            GlyphCount = glyphCount;
        }

        /// <summary>Gets the face of a program that is not an sfnt: drawn from outlines by design.</summary>
        public static Face None { get; } = new(null, 0, 0);

        /// <summary>Gets the face of an sfnt program DirectWrite could not read as the program does.</summary>
        public static Face Unavailable { get; } = new(null, 0, 0);

        public IDWriteFontFace* Pointer { get; private set; }

        /// <summary>Gets the design units per em, the em size at which one DirectWrite unit is one design unit.</summary>
        public ushort UnitsPerEm { get; }

        public int GlyphCount { get; }

        public void Release()
        {
            if (Pointer is not null)
            {
                Pointer->Release();
                Pointer = null;
            }
        }
    }
}
