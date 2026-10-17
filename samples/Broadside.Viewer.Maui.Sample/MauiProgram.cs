using Broadside.Viewer.Maui;

namespace Broadside.Viewer.Maui.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().UseBroadsideViewer();
        return builder.Build();
    }
}

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new NavigationPage(new MainPage())) { Title = "Broadside Viewer", Width = 1100, Height = 900 };
}
