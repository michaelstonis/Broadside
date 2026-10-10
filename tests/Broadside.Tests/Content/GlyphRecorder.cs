using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Fonts;
using Broadside.Graphics;

namespace Broadside.Tests.Content;

/// <summary>One glyph event, copied out of its callback.</summary>
internal sealed record RecordedGlyph(
    uint Code,
    int CodeLength,
    PdfFont? Font,
    Matrix TextMatrix,
    Matrix Ctm,
    double Width,
    double AdvanceX,
    double AdvanceY,
    double Adjustment,
    bool WordSpacingApplied,
    TextRenderingMode Mode,
    bool IsHidden)
{
    /// <summary>Gets the glyph origin in default user space: the text matrix's translation mapped by the CTM.</summary>
    public PathPoint Origin => Ctm.Transform(TextMatrix.E, TextMatrix.F);
}

/// <summary>Records glyph events and the text clips set at ET.</summary>
internal sealed class GlyphRecorder(ContentEvents events = ContentEvents.Glyphs | ContentEvents.Text | ContentEvents.Clips) : ContentProcessor
{
    public List<RecordedGlyph> Glyphs { get; } = [];

    public List<(ClipKind Kind, FillRule Rule, int GlyphCount, uint[] Codes)> Clips { get; } = [];

    public List<string> Order { get; } = [];

    public override ContentEvents Events => events;

    public override void BeginText(ContentContext context) => Order.Add("BT");

    public override void EndText(ContentContext context) => Order.Add("ET");

    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
    {
        Order.Add("Glyph");
        Glyphs.Add(new RecordedGlyph(
            glyph.CharacterCode,
            glyph.CodeLength,
            glyph.Font,
            glyph.TextMatrix,
            glyph.Ctm,
            glyph.HorizontalDisplacement,
            glyph.AdvanceX,
            glyph.AdvanceY,
            glyph.Adjustment,
            glyph.WordSpacingApplied,
            glyph.RenderingMode,
            glyph.IsHidden));
    }

    public override void IntersectClip(in ClipEvent clip, ContentContext context)
    {
        Order.Add("Clip " + clip.Kind);
        uint[] codes = [.. clip.Glyphs.ToArray().Select(glyph => glyph.CharacterCode)];
        Clips.Add((clip.Kind, clip.Rule, clip.Glyphs.Length, codes));
    }

    /// <summary>Runs <paramref name="content"/> on a page whose /F1 is Helvetica and records its glyphs.</summary>
    public static (GlyphRecorder Recorder, IReadOnlyList<Diagnostic> Diagnostics) Run(string content, ContentEvents events = ContentEvents.Glyphs | ContentEvents.Text | ContentEvents.Clips)
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.BuildWith(ContentPdf.HelveticaResources, content, ContentPdf.Helvetica));
        var recorder = new GlyphRecorder(events);
        document.Pages[0].ProcessContent(recorder);
        return (recorder, document.Diagnostics);
    }
}
