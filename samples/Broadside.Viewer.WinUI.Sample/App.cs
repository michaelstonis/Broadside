using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Broadside.Viewer.WinUI.Sample;

/// <summary>The application: WinUI's control styles, then one window showing the document named on the command line.</summary>
public sealed partial class App : Application
{
    private readonly string[] _args;
    private MainWindow? _window;

    public App(string[] args)
    {
        _args = args;
        Resources.MergedDictionaries.Add(new XamlControlsResources());
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string path = _args.Length > 0 ? Path.GetFullPath(_args[0]) : Path.Combine(AppContext.BaseDirectory, "documents", "textbook.pdf");
        _window = new MainWindow(path);
        _window.Activate();
    }
}
