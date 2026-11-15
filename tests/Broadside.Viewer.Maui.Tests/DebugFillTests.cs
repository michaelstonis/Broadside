using Broadside.Annotations;
using Broadside.Forms;

namespace Broadside.Viewer.Maui.Tests;

public sealed class DebugFillTests
{
    [Fact]
    public void Debug_fill() => UiThread.Run(async () =>
    {
        using Shell shell = await Shell.OpenAsync(Shell.CorpusFile("acroform-fields.pdf"));
        AnnotationLayer layer = shell.Controller.Annotations!;
        AnnotationTarget field = default;
        for (int i = 0; i < layer.GetCount(0); i++)
        {
            var t = new AnnotationTarget(0, i);
            if (layer.GetAnnotation(t) is PdfWidgetAnnotation { Field: PdfTextField { IsReadOnly: false } })
            {
                field = t;
                break;
            }
        }

        LayoutRectangle box = layer.GetScreenRectangle(field, shell.Layout, shell.Viewport);
        shell.Tap(1, (box.X + (box.Width / 2)) / shell.Ratio, (box.Y + (box.Height / 2)) / shell.Ratio);
        FormFilling f = shell.Controller.Forms!;
        string s1 = $"focused={layer.Focused} target={f.Target} kind={f.Kind} text='{f.Text}' ro={f.IsReadOnly}";
        bool set = f.SetText("Ada");
        string s2 = $"set={set} dirty={f.IsDirty} text='{f.Text}'";
        shell.Events.Clear();
        bool commit = f.Commit();
        shell.Controller.Frame();
        string s3 = $"commit={commit} events={shell.Events.Count} value={((PdfTextField)((PdfWidgetAnnotation)layer.GetAnnotation(field)).Field!).Value}";
        Assert.Fail(s1 + " | " + s2 + " | " + s3);
    });
}
