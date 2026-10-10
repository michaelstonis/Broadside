using Broadside.Fonts;
using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>Type 3 glyph descriptions (§9.6.4): each shown glyph's <c>CharProcs</c> stream runs as a nested content stream.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §9.6.4, Table 111. The glyph runs on the nested-run core with the graphics state of the text at the show (everything
/// is inherited but the CTM, §9.6.4 p.334), CTM = FontMatrix × T<sub>rm</sub>, so the description draws in glyph space with its origin
/// at the glyph origin; names resolve in the font's <c>Resources</c>, else in those of the page or form that shows the glyph (Table
/// 110). The first operator declares the glyph: <c>d0</c> a glyph that sets its own colour; <c>d1</c> a shape whose colour is the
/// text's, inside which (and inside every stream it invokes, §8.6.8) colour operators, colour-related <c>gs</c> entries and images
/// other than image masks are ignored. The advance comes from <c>Widths</c>, never from the <c>wx</c> operands.
/// </para>
/// <para>
/// Repairs: a description whose first operator is neither runs as if it began with <c>d0</c>, as pdf.js and PDFBox do
/// (<c>ContentType3GlyphMetricsMissing</c>); a <c>d0</c> or <c>d1</c> anywhere else is ignored (<c>ContentType3GlyphMetricsMisplaced</c>);
/// a glyph that would run inside itself (a description that shows text in the inherited current font without <c>Tf</c>) is not run
/// again but still advances (<c>ContentType3GlyphRecursion</c>).
/// </para>
/// </remarks>
internal sealed partial class ContentInterpreter
{
    /// <summary>Whether the next operator is the first of a Type 3 glyph description.</summary>
    private bool _glyphHeadPending;

    /// <summary>Runs a Type 3 glyph's description (§9.6.4) for a processor that entered it: glyph space is the font matrix times the glyph's text matrix.</summary>
    private void RunType3Glyph(in GlyphEvent glyph, PdfType3Font font, byte code, ContentProcessor processor)
    {
        if (processor.BeginType3Glyph(glyph, _context) == ContentVisit.Skip)
        {
            return;
        }

        if (font.FindCharProc(code, out CosReference? reference) is { } procedure)
        {
            GraphicsState initial = State;
            initial.Ctm = font.FontMatrix * glyph.TextMatrix * glyph.Ctm;
            var run = new NestedRun
            {
                Kind = ContentRunKind.Type3Glyph,
                Identity = procedure,
                Reference = reference,
                Content = _context.Document.ContentResources.GetContent(procedure),
                Resources = font.Resources ?? _context.Resources,
                InitialState = initial,
                Processor = processor,
            };
            _glyphHeadPending = true;
            NestedRunResult result = RunNested(run);
            _glyphHeadPending = false;
            if (result == NestedRunResult.Recursive)
            {
                Report(ContentIssue.Type3GlyphRecursion, -1, "A Type 3 glyph description would run inside itself (text shown in the glyph's own font); it is not run again.");
            }
            else
            {
                ReportNesting(result, -1);
            }
        }

        processor.EndType3Glyph(glyph, _context);
    }

    /// <summary>
    /// Called for every operator before it runs: returns whether it is the first operator of a glyph description, and reports a
    /// description that does not begin with <c>d0</c> or <c>d1</c> (Table 111).
    /// </summary>
    private bool TakeGlyphHead(ContentOperatorCode code, int offset)
    {
        if (!_glyphHeadPending)
        {
            return false;
        }

        _glyphHeadPending = false;
        if (code is not (ContentOperatorCode.SetGlyphWidth or ContentOperatorCode.SetGlyphWidthAndBoundingBox))
        {
            Report(ContentIssue.Type3GlyphMetricsMissing, offset, "A Type 3 glyph description does not begin with d0 or d1; it is run as if it began with d0.");
        }

        return true;
    }

    /// <summary><c>d0</c> and <c>d1</c> (Table 111): only as the first operator of a glyph description; <c>d1</c> locks the colour.</summary>
    private void ExecuteGlyphMetrics(ContentOperatorCode code, int offset, bool glyphHead)
    {
        if (!glyphHead)
        {
            Report(ContentIssue.Type3GlyphMetricsMisplaced, offset, "d0 or d1 that is not the first operator of a Type 3 glyph description; ignored.");
            return;
        }

        if (code == ContentOperatorCode.SetGlyphWidthAndBoundingBox)
        {
            // §8.6.8: restored with the rest of the run's state when the glyph's nested run ends.
            IgnoresColorOperators = true;
        }
    }
}
