using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace Broadside.Viewer.WinUI.Sample;

/// <summary>
/// The application: WinUI's control styles, then one window showing the document named on the command line.
/// </summary>
/// <remarks>
/// With no App.xaml the XAML compiler generates no metadata provider for the app, and WinUI's XAML parser has no type information
/// when it loads the control templates (<see cref="XamlControlsResources"/>): the app fails before its first window. So the app is its
/// own <see cref="IXamlMetadataProvider"/>, answering from WinUI's controls provider, as the generated App.g.i.cs would.
/// </remarks>
public sealed partial class App : Application, IXamlMetadataProvider
{
    private readonly XamlControlsXamlMetaDataProvider _controls = new();
    private readonly string[] _args;
    private MainWindow? _window;

    public App(string[] args)
    {
        _args = args;
        UnhandledException += (_, e) => StartupLog.Write($"Application.UnhandledException: {e.Message}{Environment.NewLine}{e.Exception}");
    }

    public IXamlType GetXamlType(Type type) => _controls.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _controls.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _controls.GetXmlnsDefinitions();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Resources.MergedDictionaries.Add(new XamlControlsResources());
            StartupLog.Write("Control resources loaded");
            string path = _args.Length > 0 ? Path.GetFullPath(_args[0]) : Path.Combine(AppContext.BaseDirectory, "documents", "textbook.pdf");
            _window = new MainWindow(path);
            _window.Activate();
            StartupLog.Write("Window activated: " + path);
        }
        catch (Exception e)
        {
            StartupLog.Write("Launching failed: " + e);
            throw;
        }
    }
}
