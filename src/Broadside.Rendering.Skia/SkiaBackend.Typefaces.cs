using System.Runtime.CompilerServices;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using SkiaSharp;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>
    /// Native glyphs (open decision 7 of the spec): TrueType and OpenType programs drawn by Skia's font engine from a typeface loaded
    /// from the program's bytes, one per program per backend, each glyph checked once against its outline.
    /// </summary>
    private sealed partial class Replayer
    {
        /// <summary>The most typefaces kept; past it every typeface and glyph is released and loaded again on use.</summary>
        private const int MaxFaces = 256;

        /// <summary>The most glyphs kept, over all typefaces; past it every glyph is released and checked again on use.</summary>
        private const int MaxGlyphs = 1 << 16;

        private static readonly Diagnostic GlyphsOutlined = new(
            "BackendNativeGlyphsUnavailable",
            DiagnosticSeverity.Information,
            "Skia could not load an embedded TrueType or OpenType program, or its font engine reads some of the program's glyphs " +
            "differently from their outlines (ISO 32000-2 §9.9); those glyphs were drawn from their outlines.");

        // A face per program: weak keys, so a backend kept for a viewer's lifetime does not keep closed documents alive.
        private ConditionalWeakTable<FontProgram, Face> _faces = [];
        private FontProgram? _lastProgram;
        private Face? _lastFace;
        private int _faceCount;
        private int _glyphCount;

        /// <summary>Gets how many typefaces this replayer has created from font programs.</summary>
        public int TypefacesCreated { get; private set; }

        /// <summary>
        /// Draws a glyph natively when its program has a typeface whose glyph agrees with the outline; false when the glyph must be
        /// drawn from its outline instead.
        /// </summary>
        private bool TryFillNative(Face? face, int glyphId, PathView outline, in Matrix glyphToDevice)
        {
            if (face?.Font is not SKFont font || (uint)glyphId > ushort.MaxValue)
            {
                return false;
            }

            if (!face.Glyphs.TryGetValue(glyphId, out SKTextBlob? blob))
            {
                if (_glyphCount >= MaxGlyphs)
                {
                    ReleaseGlyphs();
                }

                blob = CreateGlyph(font, (ushort)glyphId, outline);
                face.Glyphs.Add(glyphId, blob);
                _glyphCount++;
            }

            if (blob is null)
            {
                Report(4, GlyphsOutlined);
                return false;
            }

            // Skia's glyph space at a size of one em per unit is y-down font units: flip it to the program's glyph space.
            SKMatrix toDevice = ToSkia(Matrix.CreateScale(1, -1) * glyphToDevice);
            SKCanvas canvas = _canvas!;
            canvas.SetMatrix(in _base);
            canvas.Concat(in toDevice);
            canvas.DrawText(blob, 0, 0, _fill);
            return true;
        }

        /// <summary>Returns the face of the run's program: a typeface for an sfnt program unless outlines are forced, else none.</summary>
        private Face? GetFace(FontProgram? program)
        {
            if (ForceOutlines || program is null)
            {
                return null;
            }

            if (ReferenceEquals(program, _lastProgram))
            {
                return _lastFace;
            }

            if (!_faces.TryGetValue(program, out Face? face))
            {
                if (_faceCount >= MaxFaces)
                {
                    ReleaseFaces();
                }

                face = CreateFace(program);
                _faces.Add(program, face);
                _faceCount++;
            }

            _lastProgram = program;
            _lastFace = face;
            return face;
        }

        /// <summary>
        /// Loads a TrueType or OpenType program into a typeface (ISO 32000-2 §9.9), unhinted and with its glyphs positioned exactly
        /// (subpixel, no baseline snapping), so that Skia draws them where their outlines are; a typeface whose glyph count differs
        /// from the program's is not used.
        /// </summary>
        private Face CreateFace(FontProgram program)
        {
            if (!program.TryGetSfntData(out ReadOnlyMemory<byte> data, out int faceIndex) || data.IsEmpty)
            {
                return Face.None;
            }

            TypefacesCreated++;
            using SKData bytes = SKData.CreateCopy(data.Span);
            SKTypeface? typeface = SKTypeface.FromData(bytes, faceIndex);
            if (typeface is null || typeface.GlyphCount != program.GlyphCount || typeface.UnitsPerEm <= 0)
            {
                typeface?.Dispose();
                return Face.Unavailable;
            }

            return new Face(typeface);
        }

        /// <summary>
        /// Builds the text blob of one glyph at the origin, or returns null when Skia's outline of the glyph does not have the bounds
        /// of the program's (a component placed differently, a side bearing applied differently): such a glyph is drawn from its outline.
        /// </summary>
        private static SKTextBlob? CreateGlyph(SKFont font, ushort glyphId, PathView outline)
        {
            using SKPath? path = font.GetGlyphPath(glyphId);
            if (path is null || path.IsEmpty || !Bounds(outline, out double left, out double bottom, out double right, out double top))
            {
                return null;
            }

            // Font units at a size of one em per unit; Skia's y axis points down.
            SKRect bounds = path.Bounds;
            double tolerance = 0.5 + (font.Size / 1000.0);
            if (Math.Abs(bounds.Left - left) > tolerance || Math.Abs(bounds.Right - right) > tolerance
                || Math.Abs(-bounds.Bottom - bottom) > tolerance || Math.Abs(-bounds.Top - top) > tolerance)
            {
                return null;
            }

            Span<byte> id = [(byte)glyphId, (byte)(glyphId >> 8)];
            if (!BitConverter.IsLittleEndian)
            {
                id.Reverse();
            }

            return SKTextBlob.Create(id, SKTextEncoding.GlyphId, font, SKPoint.Empty);
        }

        /// <summary>The bounds of an outline's points, control points included (as Skia's path bounds are).</summary>
        private static bool Bounds(PathView outline, out double left, out double bottom, out double right, out double top)
        {
            (left, bottom, right, top) = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
            foreach (PathPoint point in outline.Points)
            {
                (left, right) = (Math.Min(left, point.X), Math.Max(right, point.X));
                (bottom, top) = (Math.Min(bottom, point.Y), Math.Max(top, point.Y));
            }

            return left <= right;
        }

        private void ReleaseGlyphs()
        {
            foreach (KeyValuePair<FontProgram, Face> entry in _faces)
            {
                entry.Value.ReleaseGlyphs();
            }

            _glyphCount = 0;
        }

        private void ReleaseFaces()
        {
            foreach (KeyValuePair<FontProgram, Face> entry in _faces)
            {
                entry.Value.Release();
            }

            _faces = [];
            _faceCount = 0;
            _glyphCount = 0;
            _lastProgram = null;
            _lastFace = null;
        }

        /// <summary>A program's typeface, its font at one em per font unit, and its glyphs checked so far (null: drawn from the outline).</summary>
        private sealed class Face
        {
            /// <summary>The face of a program that is not an sfnt (outlines, by design).</summary>
            public static readonly Face None = new(null);

            /// <summary>The face of an sfnt program Skia could not load (outlines, reported).</summary>
            public static readonly Face Unavailable = new(null);

            private readonly SKTypeface? _typeface;

            public Face(SKTypeface? typeface)
            {
                _typeface = typeface;
                if (typeface is not null)
                {
                    Font = new SKFont(typeface, typeface.UnitsPerEm)
                    {
                        Hinting = SKFontHinting.None,
                        Edging = SKFontEdging.Antialias,
                        Subpixel = true,
                        LinearMetrics = true,
                        BaselineSnap = false,
                        EmbeddedBitmaps = false,
                        ForceAutoHinting = false,
                    };
                }
            }

            /// <summary>Gets the font: the typeface at a size of one em per font unit, unhinted, positioned exactly.</summary>
            public SKFont? Font { get; }

            public Dictionary<int, SKTextBlob?> Glyphs { get; } = [];

            public void ReleaseGlyphs()
            {
                if (Font is null)
                {
                    return;
                }

                foreach (SKTextBlob? blob in Glyphs.Values)
                {
                    blob?.Dispose();
                }

                Glyphs.Clear();
            }

            /// <summary>Releases the glyphs, the font and the typeface.</summary>
            public void Release()
            {
                ReleaseGlyphs();
                Font?.Dispose();
                _typeface?.Dispose();
            }
        }
    }
}
