using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Broadside.Viewer.Maui;

/// <summary>Registers the Broadside viewer with a MAUI app.</summary>
public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registers SkiaSharp's views and the <see cref="PdfView"/> handler, which adds keyboard input on desktops and hardware keyboards,
    /// trackpad scrolling and pinch on Mac Catalyst, and keeps the platform's focus ring off the canvas.
    /// </summary>
    /// <param name="builder">The app builder.</param>
    /// <returns>The same builder.</returns>
    public static MauiAppBuilder UseBroadsideViewer(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSkiaSharp();
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<PdfView, PdfViewHandler>();
#if IOS
            handlers.AddHandler<MetalCanvas, MetalCanvasHandler>();
#endif
        });
        return builder;
    }
}

/// <summary>The <see cref="PdfView"/> handler: a content view whose platform view takes keys (see the files under Platforms/).</summary>
/// <remarks>Gesture recognizers it adds (Mac Catalyst) are released in DisconnectHandler, as MAUI handlers release platform objects.</remarks>
#pragma warning disable CA1001 // platform objects are released in DisconnectHandler, the handler's end of life
internal sealed partial class PdfViewHandler : ContentViewHandler
{
    /// <summary>Maps a key from the platform to the view; returns whether the viewer used it.</summary>
    internal bool Key(string key, InputModifiers modifiers) => VirtualView is PdfView view && view.HandleKey(key, modifiers);
}
#pragma warning restore CA1001
