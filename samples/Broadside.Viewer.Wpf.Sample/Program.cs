using System.Windows;

namespace Broadside.Viewer.Wpf.Sample;

/// <summary>Starts the sample: <c>Broadside.Viewer.Wpf.Sample.exe [file.pdf]</c> opens the file, or the first sample document.</summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        return app.Run(new MainWindow(args.Length > 0 ? args[0] : null));
    }
}
