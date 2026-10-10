using Broadside.Graphics;
using Broadside.Graphics.Shadings;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>The shading operator of Table 76 (<c>sh</c>) and the cells of tiling patterns (§8.7.3).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.2: <c>sh</c> names a shading in the current resources' <c>Shading</c> subdictionary and paints it in the
/// current clip, in user space; the current colour is neither used nor changed. It is reported as a
/// <see cref="ContentProcessor.PaintShading"/> event with the resolved model. A name not in the resources is skipped with a
/// diagnostic. Inside an uncoloured tiling pattern or a <c>d1</c> glyph, where colours shall not be specified (Table 73), it is
/// ignored with a diagnostic.
/// </para>
/// <para>
/// §8.7.3.1: a tiling pattern's cell runs on demand, when a processor asks through <see cref="ContentContext.RunPatternCell"/>.
/// </para>
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private void ExecuteShading(ContentOperatorCode code, ContentOperands operands, int offset)
    {
        _ = code;
        if (IgnoresColorOperators)
        {
            Report(ContentIssue.ColorOperatorIgnored, offset, "sh inside a d1 glyph or an uncoloured pattern, where colours shall not be specified; ignored.");
            return;
        }

        ReadOnlySpan<byte> name = operands[0].Bytes;
        PdfDocument document = _context.Document;
        CosObject? value = document.ColorSpaces.FindResource(_context.Resources, ShadingNames.Shading, name, out _);
        if (value is null)
        {
            Report(ContentIssue.ShadingMissing, offset, "sh names a shading that is not in the resources' Shading dictionary; nothing is painted.");
            return;
        }

        PdfShading? model = document.Shadings.GetShading(value, CurrentStream ?? _fallbackReference);
        if ((_events & ContentEvents.Shadings) != 0 && (!_context.IsHidden || (_events & ContentEvents.HiddenContent) != 0))
        {
            var shading = new ShadingEvent
            {
                Shading = document.Resolve(value),
                Model = model,
                ResourceName = name,
                Ctm = State.Ctm,
                IsHidden = _context.IsHidden,
            };
            _processor.PaintShading(shading, _context);
        }
    }

    /// <summary>Returns the pattern a colour selected, or <see langword="null"/>.</summary>
    internal PdfPattern? GetPattern(in PdfColor color) =>
        color.Pattern is { } pattern ? _context.Document.Shadings.GetPattern(pattern, CurrentStream ?? _fallbackReference) : null;

    /// <summary>
    /// Runs the cell of the tiling pattern a colour selected (§8.7.3.1 steps a to d): q; the graphics state at the beginning of the
    /// stream that selected the pattern, with the CTM mapping pattern space; the cell, clipped to its bounding box; Q.
    /// </summary>
    internal bool RunPatternCell(in PdfColor selected, ContentProcessor processor)
    {
        // A copy: the caller's colour may live in the state stack, which the nested run grows.
        PdfColor color = selected;
        if (GetPattern(color) is not PdfTilingPattern { IsValid: true } pattern)
        {
            return false;
        }

        PdfColorSpace? underlying = (color.ColorSpace as PdfPatternColorSpace)?.Underlying;
        bool uncolored = pattern.IsPaintTypeKnown ? pattern.PaintType == PdfTilingPaintType.Uncolored : color.ComponentCount > 0;
        GraphicsState initial = StartStateFor(color.PatternMatrix);
        initial.Ctm = pattern.GetPatternSpace(color);
        if (uncolored && underlying is not null)
        {
            // §8.7.3.3: the cell is a stencil painted in the colour given with the pattern (PDFBox sets both colours to it).
            var stencil = new PdfColor(underlying, color.Components);
            initial.SetColor(stroke: false, stencil);
            initial.SetColor(stroke: true, stencil);
        }

        var run = new NestedRun
        {
            Kind = ContentRunKind.Pattern,
            Identity = pattern.Stream,
            Reference = pattern.Reference,
            Content = pattern.GetContent(),
            Resources = pattern.Resources ?? _context.Resources,
            InitialState = initial,
            ClipBox = pattern.BoundingBox,
            Processor = processor,
            IgnoresColorOperators = uncolored,
        };

        switch (RunNested(run))
        {
            case NestedRunResult.Recursive:
                Report(ContentIssue.PatternRecursion, -1, "A tiling pattern's cell paints with the same pattern (directly or through other patterns or forms); the inner cell is empty.");
                return false;
            case NestedRunResult.TooDeep:
                Report(ContentIssue.NestingTooDeep, -1, "Patterns, forms and glyphs are nested deeper than the limit; the innermost is not run.");
                return false;
            default:
                return true;
        }
    }
}
