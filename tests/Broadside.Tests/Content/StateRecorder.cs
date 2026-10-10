using Broadside.Content;
using Broadside.Graphics;

namespace Broadside.Tests.Content;

/// <summary>Copies the graphics state at every path paint, so a test can assert the parameters a path was painted with.</summary>
internal sealed class StateRecorder : ContentProcessor
{
    public List<GraphicsState> States { get; } = [];

    public List<double[]> DashArrays { get; } = [];

    public override ContentEvents Events => ContentEvents.Paths;

    public override void PaintPath(in PathEvent path, ContentContext context)
    {
        States.Add(context.State);
        DashArrays.Add(context.State.DashArray.ToArray());
    }

    /// <summary>Opens a one-page document with <paramref name="content"/> and records the state of each paint.</summary>
    public static (StateRecorder Recorder, IReadOnlyList<Broadside.Diagnostics.Diagnostic> Diagnostics) Run(string content)
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build(content));
        var recorder = new StateRecorder();
        document.Pages[0].ProcessContent(recorder);
        return (recorder, document.Diagnostics);
    }
}
