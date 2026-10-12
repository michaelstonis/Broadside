using System.Globalization;
using System.Text;
using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Tests.Content;

/// <summary>
/// A processor that writes every event it receives as one line of text, so a test can assert the event sequence a content
/// stream produces. Geometry is printed with the invariant culture and the shortest round-trip form of each double.
/// </summary>
internal sealed class RecordingProcessor(ContentEvents events = ContentEvents.All) : ContentProcessor
{
    public List<string> Lines { get; } = [];

    public Dictionary<string, int> Counts { get; } = [];

    public override ContentEvents Events => events;

    public override void BeginRun(ContentContext context) => Add("BeginRun", $"BeginRun {context.RunKind}");

    public override void EndRun(ContentContext context) => Add("EndRun", "EndRun");

    public override void VisitOperator(in ContentOperator op, ContentContext context) =>
        Add("Operator", $"Op{Operands(op.Operands)} {Encoding.Latin1.GetString(op.Keyword)}");

    public override void SaveState(ContentContext context) => Add("SaveState", $"q {context.StateDepth}");

    public override void RestoreState(ContentContext context) => Add("RestoreState", $"Q {context.StateDepth}");

    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        string closed = path.ClosedBeforePaint ? " closed" : string.Empty;
        string clip = path.PendingClip is { } rule ? $" clip={rule}" : string.Empty;
        Add("PaintPath", $"Paint {path.Paint} {path.FillRule}{closed}{clip} {Path(path.Path)} ctm={Matrix(context.State.Ctm)}");
    }

    public override void IntersectClip(in ClipEvent clip, ContentContext context) =>
        Add("IntersectClip", $"Clip {clip.Kind} {clip.Rule} #{clip.Handle}<#{clip.ParentHandle} {Path(clip.Path)} ctm={Matrix(clip.Ctm)}");

    public override void BeginText(ContentContext context) => Add("BeginText", "BT");

    public override void EndText(ContentContext context) => Add("EndText", "ET");

    public override void ShowGlyph(in GlyphEvent glyph, ContentContext context) => Add("ShowGlyph", "Glyph");

    public override void PaintImage(in ImageEvent image, ContentContext context) => Add("PaintImage", "Image");

    public override void PaintShading(in ShadingEvent shading, ContentContext context) => Add("PaintShading", "Shading");

    public override ContentVisit BeginForm(in FormEvent form, ContentContext context)
    {
        Add("BeginForm", "BeginForm");
        return ContentVisit.Enter;
    }

    public override void EndForm(in FormEvent form, ContentContext context) => Add("EndForm", "EndForm");

    public override void BeginMarkedContent(in MarkedContentEvent mc, ContentContext context) => Add("BeginMarkedContent", "BMC");

    public override void EndMarkedContent(in MarkedContentEvent mc, ContentContext context) => Add("EndMarkedContent", "EMC");

    public override void MarkedContentPoint(in MarkedContentEvent mc, ContentContext context) => Add("MarkedContentPoint", "MP");

    public int Count(string name) => Counts.GetValueOrDefault(name);

    /// <summary>The lines between the run's begin and end lines.</summary>
    public List<string> Body => [.. Lines.Where(line => line is not ("EndRun" or "BeginRun Page"))];

    /// <summary>The lines that are not operator lines, for tests that look at semantic events only.</summary>
    public List<string> Semantic => [.. Body.Where(line => !line.StartsWith("Op", StringComparison.Ordinal))];

    internal static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    internal static string Matrix(Matrix m) => $"[{Number(m.A)} {Number(m.B)} {Number(m.C)} {Number(m.D)} {Number(m.E)} {Number(m.F)}]";

    internal static string Path(PathView path)
    {
        var text = new StringBuilder();
        int point = 0;
        foreach (PathVerb verb in path.Verbs)
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            int count = verb switch
            {
                PathVerb.MoveTo or PathVerb.LineTo => 1,
                PathVerb.QuadTo => 2,
                PathVerb.CubicTo => 3,
                _ => 0,
            };
            text.Append(verb switch
            {
                PathVerb.MoveTo => 'M',
                PathVerb.LineTo => 'L',
                PathVerb.QuadTo => 'Q',
                PathVerb.CubicTo => 'C',
                _ => 'Z',
            });
            for (int index = 0; index < count; index++, point++)
            {
                text.Append(' ').Append(Number(path.Points[point].X)).Append(',').Append(Number(path.Points[point].Y));
            }
        }

        return text.ToString();
    }

    private static string Operands(ContentOperands operands)
    {
        var text = new StringBuilder();
        for (int index = 0; index < operands.Count; index++)
        {
            text.Append(' ').Append(Operand(operands[index]));
        }

        return text.ToString();
    }

    private static string Operand(ContentOperand operand) => operand.Kind switch
    {
        ContentOperandKind.Integer or ContentOperandKind.Real => Number(operand.Number),
        ContentOperandKind.Boolean => operand.Boolean ? "true" : "false",
        ContentOperandKind.Null => "null",
        ContentOperandKind.Name => "/" + Encoding.Latin1.GetString(operand.Bytes),
        ContentOperandKind.String => "(" + Encoding.Latin1.GetString(operand.Bytes) + ")",
        ContentOperandKind.Array => "[" + Operands(operand.Items).TrimStart() + "]",
        _ => "<<" + Operands(operand.Items).TrimStart() + ">>",
    };

    private void Add(string name, string line)
    {
        Lines.Add(line);
        Counts[name] = Count(name) + 1;
    }
}
