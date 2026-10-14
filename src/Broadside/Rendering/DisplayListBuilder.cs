using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Rendering;

/// <summary>
/// The display-list builder: a content processor that turns the interpreter's events into display-list records (ADR 0006; the
/// spec's "one content interpreter, many processors"). Split by operation category: this file holds the run, the state stack,
/// transforms and the final copy; <c>DisplayListBuilder.Paths.cs</c> holds path fills, path interning and colours. Later tickets add
/// their category in a file of their own.
/// </summary>
/// <remarks>
/// One builder per thread is reused (<see cref="Build(PdfPage, PdfDocument, ContentOptions)"/>), so its growable tables stay warm and building a page allocates only the
/// list itself: a fixed number of arrays sized to the page. The builder keeps no reference into the document between builds.
/// </remarks>
internal sealed partial class DisplayListBuilder : ContentProcessor
{
    [ThreadStatic]
    private static DisplayListBuilder? _cached;

    private DisplayOperation[] _operations = new DisplayOperation[64];
    private int _operationCount;

    private Matrix[] _transforms = new Matrix[16];
    private int _transformCount;

    // The transform the backend holds at this point of the replay, and the ones saved by the open SaveState records.
    private Matrix _current = Matrix.Identity;
    private Matrix[] _saved = new Matrix[16];
    private int _savedCount;

    // Where the open SaveState records are in the operations, parallel to _saved.
    private int[] _savedAt = new int[16];
    private int _savedAtCount;

    /// <inheritdoc/>
    public override ContentEvents Events => ContentEvents.Paths | ContentEvents.StateStack | ContentEvents.Clips | ContentEvents.Images | ContentEvents.Glyphs | ContentEvents.Shadings | ContentEvents.Forms;

    /// <summary>Builds the display list of <paramref name="page"/>.</summary>
    /// <param name="page">The page.</param>
    /// <param name="document">The page's document.</param>
    /// <param name="options">Limits and cancellation for the content run.</param>
    /// <returns>The list.</returns>
    public static DisplayList Build(PdfPage page, PdfDocument document, ContentOptions options)
    {
        DisplayListBuilder builder = _cached ?? new DisplayListBuilder();
        _cached = null;
        try
        {
            int pageGroup = builder.OpenPageGroup(page, document);
            ContentInterpreter.RunPage(page, document, builder, options);
            builder.ClosePageGroup(pageGroup);
            return builder.ToDisplayList(document);
        }
        finally
        {
            builder.Reset();
            _cached = builder;
        }
    }

    /// <summary>Copies what was built into a list of exactly the needed size.</summary>
    /// <param name="document">The document the content came from.</param>
    /// <returns>The list.</returns>
    public DisplayList ToDisplayList(PdfDocument document) => new(
        document,
        _operations.AsSpan(0, _operationCount).ToArray(),
        _verbs.AsSpan(0, _verbCount).ToArray(),
        _points.AsSpan(0, _pointCount).ToArray(),
        _paths.AsSpan(0, _pathCount).ToArray(),
        _colors.AsSpan(0, _colorCount).ToArray(),
        _components.AsSpan(0, _componentCount).ToArray(),
        _transforms.AsSpan(0, _transformCount).ToArray())
    {
        Strokes = _strokes.AsSpan(0, _strokeCount).ToArray(),
        Dashes = _dashes.AsSpan(0, _dashCount).ToArray(),
        Images = _images.AsSpan(0, _imageCount).ToArray(),
        ImagePaints = _imagePaints.AsSpan(0, _imagePaintCount).ToArray(),
        GlyphRuns = _glyphRuns.AsSpan(0, _glyphRunCount).ToArray(),
        Glyphs = _glyphs.AsSpan(0, _glyphCount).ToArray(),
        ShadingPaints = _shadingPaints.AsSpan(0, _shadingPaintCount).ToArray(),
        ImageMasks = _imageMasks.AsSpan(0, _imageMaskCount).ToArray(),
        Compositings = _compositings.AsSpan(0, _compositingCount).ToArray(),
        Groups = _groups.AsSpan(0, _groupCount).ToArray(),

        Annotations = _annotations.AsSpan(0, _annotationCount).ToArray(),
    };

    /// <summary>Forgets everything built, keeping the tables' capacity and no reference into a document.</summary>
    public void Reset()
    {
        _operationCount = 0;
        _transformCount = 0;
        _current = Matrix.Identity;
        _savedCount = 0;
        _savedAtCount = 0;
        ResetPaths();
        ResetStrokes();
        ResetImages();
        ResetText();
        ResetShadings();
        ResetCompositing();
        ResetGroups();

        ResetAnnotations();
    }

    /// <inheritdoc/>
    public override void SaveState(ContentContext context) => Save();

    /// <inheritdoc/>
    public override void RestoreState(ContentContext context) => Restore();

    /// <summary>Records a save of the state (§8.4.2 <c>q</c>).</summary>
    private void Save()
    {
        Append(ref _saved, ref _savedCount, _current);
        Append(ref _savedAt, ref _savedAtCount, _operationCount);
        SaveCompositing();
        AddOperation(new DisplayOperation(DisplayOperationKind.SaveState, default, 0, 0));
    }

    /// <summary>Records a restore of the state (§8.4.2 <c>Q</c>), dropping the save and what follows it when nothing was painted since.</summary>
    private void Restore()
    {
        if (_savedCount == 0)
        {
            return;
        }

        _current = _saved[--_savedCount];
        int saveAt = _savedAt[--_savedAtCount];
        RestoreCompositing();

        // A save with nothing painted after it restores to what it saved: drop the pair and the state changes between them.
        if (!PaintsSince(saveAt + 1))
        {
            _operationCount = saveAt;
            return;
        }

        AddOperation(new DisplayOperation(DisplayOperationKind.RestoreState, default, 0, 0));
    }

    /// <summary>Records the CTM before a paint, when it differs from the transform the backend holds there.</summary>
    /// <param name="ctm">The CTM of the paint.</param>
    private void SetTransform(in Matrix ctm)
    {
        if (ctm == _current)
        {
            return;
        }

        _current = ctm;
        AddOperation(new DisplayOperation(DisplayOperationKind.SetTransform, default, _transformCount, 0));
        Append(ref _transforms, ref _transformCount, ctm);
    }

    /// <summary>Returns whether any operation from <paramref name="start"/> on is something other than a state change.</summary>
    private bool PaintsSince(int start)
    {
        for (int index = _operationCount - 1; index >= start; index--)
        {
            if (_operations[index].Kind is not (DisplayOperationKind.SetTransform or DisplayOperationKind.IntersectClip or DisplayOperationKind.SetCompositing))
            {
                return true;
            }
        }

        return false;
    }

    private void AddOperation(in DisplayOperation operation) => Append(ref _operations, ref _operationCount, operation);

    /// <summary>Appends to a growable table, doubling it when full.</summary>
    private static void Append<T>(ref T[] table, ref int count, in T item)
    {
        if (count == table.Length)
        {
            Array.Resize(ref table, table.Length * 2);
        }

        table[count++] = item;
    }
}
