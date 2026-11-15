using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Broadside.Viewer.Uno.Sample;

/// <summary>The sample application: one window with the viewer page.</summary>
public sealed partial class App : Application
{
    public App() => Resources.MergedDictionaries.Add(new XamlControlsResources());

    /// <summary>Gets the main window.</summary>
    public static Window? MainWindow { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window { Title = "Broadside Viewer", Content = new MainPage() };
        MainWindow.Activate();
    }
}
