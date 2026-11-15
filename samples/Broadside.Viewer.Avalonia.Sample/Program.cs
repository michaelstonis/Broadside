using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace Broadside.Viewer.Avalonia.Sample;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string? source = desktop.Args is { Length: > 0 } args ? args[0] : null;
            desktop.MainWindow = new MainWindow(source ?? Path.Combine(AppContext.BaseDirectory, "documents", "textbook.pdf"));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
