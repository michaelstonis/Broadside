using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;
using Java.Interop;
using AndroidCanvas = Android.Graphics.Canvas;
using ByteBuffer = Java.Nio.ByteBuffer;
using Matrix = Broadside.Graphics.Matrix;
using NativeFont = Android.Graphics.Fonts.Font;
using Paint = Android.Graphics.Paint;
using PaintFlags = Android.Graphics.PaintFlags;
using RectF = Android.Graphics.RectF;

namespace Broadside.Rendering.Android;

/// <summary>Text (ISO 32000-2 §9.2.4, §9.3.6, §9.4.3, §9.9): glyph runs, natively for TrueType and OpenType programs.</summary>
public sealed partial class AndroidCanvasBackend
{
    /// <summary>Gets how many Android fonts this backend has built from font programs (once per program while it is kept).</summary>
    internal int FontsCreated => _backend.FontsCreated;

    /// <summary>Gets how many glyphs the last render drew through Android's font engine.</summary>
    internal int NativeGlyphs => _backend.NativeGlyphs;

    private sealed partial class Backend
    {
        /// <summary>The text size native glyphs are drawn at: the canvas matrix of each run scales it to the glyphs' device size.</summary>
        private const float DrawSize = 16;

        /// <summary>The text size a native glyph's bounds are measured at, and how far (in pixels at that size) they may be from the outline's.</summary>
        private const float CheckSize = 64;

        private const float CheckTolerance = 2.5f;

        /// <summary>The most programs given an Android font; past it every font is released and made again on use.</summary>
        private const int MaxFaces = 64;

        private static readonly Diagnostic NativeGlyphsUnavailable = new(
            "BackendNativeGlyphsUnavailable",
            DiagnosticSeverity.Information,
            "Some glyphs of an embedded TrueType or OpenType program (ISO 32000-2 §9.9) were drawn from their outlines rather than by " +
            "Android's font engine: this Android version (before 12, API level 31) cannot draw glyphs by id, Android could not load " +
            "the program, or its font engine reads a glyph differently from the program's outline.");

        private static JniMethodInfo? _drawGlyphs;

        private readonly Paint _checkPaint = new(PaintFlags.AntiAlias | PaintFlags.SubpixelText | PaintFlags.LinearText);
        private readonly RectF _bounds = new();

        // A face per program: weak keys, so a backend kept for a viewer's lifetime does not keep closed documents alive.
        private ConditionalWeakTable<FontProgram, Face> _faces = [];
        private int _faceCount;
        private FontProgram? _lastProgram;
#pragma warning disable CA2213 // The faces belong to _faces, which ReleaseFaces disposes.
        private Face? _lastFace;
        private bool _nativeReported;

        // The glyphs of the current native batch: ids, positions on the canvas under _batch, and the Java arrays they are copied into.
        private int[] _glyphIds = new int[32];
        private float[] _positions = new float[64];
        private int _batchCount;
        private Face? _batchFace;
#pragma warning restore CA2213
        private Matrix _batch;
        private Matrix _batchInverse;
        private JniObjectReference _javaGlyphs;
        private JniObjectReference _javaPositions;
        private int _javaCapacity;

        // A glyph's outline mapped to user space, for stroking with a pen in user space.
        private PathPoint[] _glyphPoints = new PathPoint[64];

        /// <summary>Gets how many Android fonts this backend has built from font programs.</summary>
        public int FontsCreated { get; private set; }

        /// <summary>Gets how many glyphs the current or last render drew through Android's font engine.</summary>
        public int NativeGlyphs { get; private set; }

        /// <summary>Gets or sets a value indicating whether every glyph is drawn from its outline, never by Android's font engine.</summary>
        public bool ForceOutlines { get; set; }

        /// <summary>
        /// Draws each glyph of the run (§9.3.6, Table 104): filled under glyph space → user space → device, by Android's font engine
        /// from a font built from the embedded TrueType or OpenType program's bytes (unless outlines are forced, the Android version
        /// cannot, or the engine's glyph is not the outline's), else from its outline with the non-zero winding rule; then stroked
        /// from its outline with the run's line parameters in user space. A run with neither fill nor stroke (mode 3) paints nothing.
        /// </summary>
        /// <remarks>
        /// ISO 32000-2 §9.2.4 (glyph outlines, the font matrix), §9.3.6 and §8.4.3 (text strokes use the graphics state's line
        /// parameters in user space), §9.9 (embedded font programs). Consecutive filled glyphs sharing the same transform but for its
        /// translation are drawn in one call; a run that also strokes is drawn glyph by glyph, each glyph filled before it is stroked.
        /// </remarks>
        private void DrawGlyphsCore(in GlyphRun run)
        {
            if (run.Glyphs.IsEmpty || !(run.Fills || run.Strokes))
            {
                return;
            }

            bool fills = false;
            if (run.Fills && _compositing.FillAlpha != 0)
            {
                PdfColorConverter converter = Converter(run.FillColor);
                if (!converter.PaintsNothing)
                {
                    SetColor(_fill, converter, run.FillColor.Components, _compositing.FillAlpha);
                    SetBlendMode(_fill, ref _fillBlendMode);
                    fills = true;
                }
            }

            bool strokes = run.Strokes && _compositing.StrokeAlpha != 0;
            if (!fills && !strokes)
            {
                return;
            }

            Face? face = fills ? GetFace(run.Program, run.FontMatrix) : null;
            for (int index = 0; index < run.Glyphs.Length; index++)
            {
                PathView outline = run.GetOutline(index);
                if (outline.IsEmpty)
                {
                    continue;
                }

                Matrix glyphToUser = run.GetGlyphTransform(index);
                if (fills)
                {
                    Matrix glyphToDevice = glyphToUser * _toDevice;
                    if (!AddNative(face, run.Glyphs[index].GlyphId, outline, glyphToDevice))
                    {
                        FlushNative();
                        FillOutline(outline, glyphToDevice);
                    }
                }

                if (strokes)
                {
                    FlushNative();
                    StrokePathCore(ToUserSpace(outline, glyphToUser), run.StrokeStyle, run.StrokeColor);
                }
            }

            FlushNative();
        }

        /// <summary>Resets the text state at the start of a render.</summary>
        private void BeginText()
        {
            _nativeReported = false;
            _batchCount = 0;
            NativeGlyphs = 0;
        }

        /// <summary>Releases the Android fonts and Java arrays.</summary>
        private void DisposeText()
        {
            ReleaseFaces();
            JniObjectReference.Dispose(ref _javaGlyphs);
            JniObjectReference.Dispose(ref _javaPositions);
            _javaCapacity = 0;
            _checkPaint.Dispose();
            _bounds.Dispose();
        }

        /// <summary>Fills a glyph outline mapped to the device with the non-zero rule; one-point contours paint nothing (§9.2.4).</summary>
        private void FillOutline(PathView outline, in Matrix glyphToDevice)
        {
            Matrix toDevice = _toDevice;
            _toDevice = glyphToDevice;
            BuildDevicePath(outline, FillRule.NonZero, degenerateDots: false);
            _toDevice = toDevice;
            _canvas!.DrawPath(_path, _fill);
        }

        /// <summary>Maps a glyph outline's points to user space, into a buffer kept for the next glyph.</summary>
        private PathView ToUserSpace(PathView outline, in Matrix glyphToUser)
        {
            ReadOnlySpan<PathPoint> points = outline.Points;
            if (_glyphPoints.Length < points.Length)
            {
                _glyphPoints = new PathPoint[Math.Max(points.Length, _glyphPoints.Length * 2)];
            }

            for (int index = 0; index < points.Length; index++)
            {
                _glyphPoints[index] = glyphToUser.Transform(points[index]);
            }

            return new PathView(outline.Verbs, _glyphPoints.AsSpan(0, points.Length));
        }

        /// <summary>
        /// Adds a glyph to the native batch when its program has an Android font whose glyph agrees with the outline; false when the
        /// glyph must be drawn from its outline instead. A glyph whose transform differs from the batch's but for its translation
        /// draws the batch first.
        /// </summary>
        private bool AddNative(Face? face, int glyphId, PathView outline, in Matrix glyphToDevice)
        {
            if (face is null || !OperatingSystem.IsAndroidVersionAtLeast(31) || !Agrees(face, glyphId, outline))
            {
                return false;
            }

            // Android's text space at DrawSize is y-down and DrawSize units per em: back to em (the program's text space), to glyph
            // space, then to the device.
            Matrix toDevice = face.CanvasToGlyph * glyphToDevice;
            if (_batchCount > 0 && (!ReferenceEquals(face, _batchFace) || toDevice.A != _batch.A || toDevice.B != _batch.B || toDevice.C != _batch.C || toDevice.D != _batch.D))
            {
                FlushNative();
            }

            if (_batchCount == 0)
            {
                if (!toDevice.TryInvert(out _batchInverse))
                {
                    return false;
                }

                _batch = toDevice;
                _batchFace = face;
            }

            if (_batchCount == _glyphIds.Length)
            {
                Array.Resize(ref _glyphIds, _glyphIds.Length * 2);
                Array.Resize(ref _positions, _positions.Length * 2);
            }

            PathPoint position = _batchInverse.Transform(toDevice.E, toDevice.F);
            _glyphIds[_batchCount] = glyphId;
            _positions[2 * _batchCount] = (float)position.X;
            _positions[(2 * _batchCount) + 1] = (float)position.Y;
            _batchCount++;
            return true;
        }

        /// <summary>Draws the native batch under its canvas matrix with one <c>Canvas.drawGlyphs</c> call.</summary>
        private void FlushNative()
        {
            int count = _batchCount;
            _batchCount = 0;
            if (count == 0 || !OperatingSystem.IsAndroidVersionAtLeast(31) || _batchFace?.Font is not NativeFont font || !SetMatrix(_batch))
            {
                return;
            }

            AndroidCanvas canvas = _canvas!;
            int saved = canvas.Save();
            canvas.Concat(_matrix);
            DrawGlyphs(canvas, font, count);
            canvas.RestoreToCount(saved);
            NativeGlyphs += count;
        }

        /// <summary>
        /// Calls <c>Canvas.drawGlyphs</c> through JNI with Java arrays this backend keeps: the binding would create two Java arrays
        /// per call. The arrays grow (and are made again) only when a batch is longer than any before.
        /// </summary>
        [SupportedOSPlatform("android31.0")]
        private unsafe void DrawGlyphs(AndroidCanvas canvas, NativeFont font, int count)
        {
            if (_javaCapacity < _glyphIds.Length)
            {
                JniObjectReference.Dispose(ref _javaGlyphs);
                JniObjectReference.Dispose(ref _javaPositions);
                JniObjectReference glyphs = JniEnvironment.Arrays.NewIntArray(_glyphIds.Length);
                JniObjectReference positions = JniEnvironment.Arrays.NewFloatArray(_positions.Length);
                _javaGlyphs = glyphs.NewGlobalRef();
                _javaPositions = positions.NewGlobalRef();
                JniObjectReference.Dispose(ref glyphs);
                JniObjectReference.Dispose(ref positions);
                _javaCapacity = _glyphIds.Length;
            }

            fixed (int* ids = _glyphIds)
            fixed (float* points = _positions)
            {
                JniEnvironment.Arrays.SetIntArrayRegion(_javaGlyphs, 0, count, ids);
                JniEnvironment.Arrays.SetFloatArrayRegion(_javaPositions, 0, 2 * count, points);
            }

            JniMethodInfo method = _drawGlyphs ??= DrawGlyphsMethod();
            JniArgumentValue* arguments = stackalloc JniArgumentValue[7];
            arguments[0] = new JniArgumentValue(_javaGlyphs);
            arguments[1] = new JniArgumentValue(0);
            arguments[2] = new JniArgumentValue(_javaPositions);
            arguments[3] = new JniArgumentValue(0);
            arguments[4] = new JniArgumentValue(count);
            arguments[5] = new JniArgumentValue(font.PeerReference);
            arguments[6] = new JniArgumentValue(_fill.PeerReference);
            JniEnvironment.InstanceMethods.CallVoidMethod(canvas.PeerReference, method, arguments);
        }

        private static JniMethodInfo DrawGlyphsMethod()
        {
            using var type = new JniType("android/graphics/Canvas");
            return type.GetInstanceMethod("drawGlyphs", "([II[FIILandroid/graphics/fonts/Font;Landroid/graphics/Paint;)V");
        }

        /// <summary>
        /// Whether Android's glyph is the program's: its bounds at <see cref="CheckSize"/> are those of the outline's points (control
        /// points included, as the engine measures them), within <see cref="CheckTolerance"/>. Checked once per glyph per font.
        /// </summary>
        private bool Agrees(Face face, int glyphId, PathView outline)
        {
            if ((uint)glyphId >= (uint)face.Status.Length || face.Font is not NativeFont font || !OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                return false;
            }

            ref byte status = ref face.Status[glyphId];
            if (status == 0)
            {
                status = Measure(font, glyphId, outline, face.FontMatrix) ? (byte)1 : (byte)2;
                if (status == 2)
                {
                    ReportNative();
                }
            }

            return status == 1;
        }

        [SupportedOSPlatform("android31.0")]
        private bool Measure(NativeFont font, int glyphId, PathView outline, in Matrix fontMatrix)
        {
            double left = double.MaxValue;
            double bottom = double.MaxValue;
            double right = double.MinValue;
            double top = double.MinValue;
            foreach (PathPoint point in outline.Points)
            {
                (left, right) = (Math.Min(left, point.X), Math.Max(right, point.X));
                (bottom, top) = (Math.Min(bottom, point.Y), Math.Max(top, point.Y));
            }

            if (!(left <= right))
            {
                return false;
            }

            font.GetGlyphBounds(glyphId, _checkPaint, _bounds);
            Matrix f = fontMatrix;
            return Near(_bounds.Left, ((f.A * left) + f.E) * CheckSize)
                && Near(_bounds.Right, ((f.A * right) + f.E) * CheckSize)
                && Near(_bounds.Top, -((f.D * top) + f.F) * CheckSize)
                && Near(_bounds.Bottom, -((f.D * bottom) + f.F) * CheckSize);

            static bool Near(float measured, double expected) => Math.Abs(measured - expected) <= CheckTolerance;
        }

        /// <summary>
        /// Returns the face of the run's program: an Android font for an sfnt program whose font matrix only scales and moves, unless
        /// outlines are forced; else none (outlines). On Android before 12 an sfnt program is drawn from its outlines and reported.
        /// </summary>
        private Face? GetFace(FontProgram? program, in Matrix fontMatrix)
        {
            if (ForceOutlines || program is null || fontMatrix.B != 0 || fontMatrix.C != 0 || !(fontMatrix.A > 0) || !(fontMatrix.D > 0))
            {
                return null;
            }

            Face? face = _lastFace;
            if (!ReferenceEquals(program, _lastProgram) || face is null)
            {
                if (!_faces.TryGetValue(program, out face))
                {
                    if (_faceCount >= MaxFaces)
                    {
                        ReleaseFaces();
                    }

                    face = OperatingSystem.IsAndroidVersionAtLeast(31) ? CreateFace(program, fontMatrix) : Face.Unsupported(program);
                    FontsCreated += face.Font is null ? 0 : 1;
                    _faces.Add(program, face);
                    _faceCount++;
                }

                _lastProgram = program;
                _lastFace = face;
            }

            if (face.Font is null)
            {
                if (face.Reported)
                {
                    ReportNative();
                }

                return null;
            }

            return face;
        }

        /// <summary>
        /// Builds an Android font from a TrueType or OpenType program's bytes (§9.9), copied once into a direct buffer the font keeps;
        /// an sfnt Android cannot load is drawn from its outlines and reported.
        /// </summary>
        [SupportedOSPlatform("android31.0")]
        private static Face CreateFace(FontProgram program, in Matrix fontMatrix)
        {
            if (!program.TryGetSfntData(out ReadOnlyMemory<byte> data, out int faceIndex) || data.IsEmpty)
            {
                return Face.None;
            }

            ByteBuffer? buffer = null;
            try
            {
                buffer = ByteBuffer.AllocateDirect(data.Length)!;
                buffer.Put(data.ToArray());
                buffer.Rewind();
                using var builder = new NativeFont.Builder(buffer);
                builder.SetTtcIndex(faceIndex);
                NativeFont font = builder.Build();
                return new Face(font, buffer, Math.Min(program.GlyphCount, ushort.MaxValue + 1), fontMatrix);
            }
#pragma warning disable CA1031 // Any failure to load the program means its glyphs are drawn from their outlines, which is reported.
            catch (Java.Lang.Exception)
#pragma warning restore CA1031
            {
                buffer?.Dispose();
                return new Face(null, null, 0, fontMatrix) { Reported = true };
            }
        }

        private void ReportNative()
        {
            if (!_nativeReported)
            {
                _nativeReported = true;
                Diagnostics.Add(NativeGlyphsUnavailable);
            }
        }

        private void ReleaseFaces()
        {
            foreach (KeyValuePair<FontProgram, Face> entry in _faces)
            {
                entry.Value.Dispose();
            }

            _faces = [];
            _faceCount = 0;
            _lastProgram = null;
            _lastFace = null;
            _batchFace = null;
            _batchCount = 0;
        }

        /// <summary>
        /// A program's Android font and the buffer holding its bytes, the per-glyph agreement checks made so far (0 unchecked, 1 drawn
        /// natively, 2 from the outline), and the matrix that maps Android's text space to the program's glyph space.
        /// </summary>
        private sealed class Face : IDisposable
        {
            /// <summary>The face of a program that is not an sfnt (outlines, by design, not reported).</summary>
            public static readonly Face None = new(null, null, 0, Matrix.Identity);

            private readonly ByteBuffer? _buffer;

            public Face(NativeFont? font, ByteBuffer? buffer, int glyphCount, in Matrix fontMatrix)
            {
                Font = font;
                _buffer = buffer;
                Status = glyphCount > 0 ? new byte[glyphCount] : [];
                FontMatrix = fontMatrix;

                // Canvas (DrawSize units per em, y down) -> em -> glyph space (the inverse of the font matrix, which only scales and moves).
                Matrix inverseFont = new(1 / fontMatrix.A, 0, 0, 1 / fontMatrix.D, -fontMatrix.E / fontMatrix.A, -fontMatrix.F / fontMatrix.D);
                CanvasToGlyph = new Matrix(1 / DrawSize, 0, 0, -1 / DrawSize, 0, 0) * inverseFont;
            }

            public NativeFont? Font { get; }

            public byte[] Status { get; }

            public Matrix FontMatrix { get; }

            public Matrix CanvasToGlyph { get; }

            /// <summary>Gets a value indicating whether drawing this program from its outlines is reported (a native font was expected).</summary>
            public bool Reported { get; init; }

            /// <summary>The face of an sfnt program on an Android version that cannot draw glyphs by id: outlines, reported.</summary>
            public static Face Unsupported(FontProgram program) =>
                program.TryGetSfntData(out _, out _) ? new Face(null, null, 0, Matrix.Identity) { Reported = true } : None;

            public void Dispose()
            {
                Font?.Dispose();
                _buffer?.Dispose();
            }
        }
    }
}
