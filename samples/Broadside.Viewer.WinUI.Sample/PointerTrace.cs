using System.Globalization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Broadside.Viewer.WinUI.Sample;

/// <summary>
/// An opt-in trace of the pointer input the view receives (set <c>BROADSIDE_WINUI_TRACE=1</c>; the UI tests do): each press,
/// move with a button down, release, cancel and lost capture, where it landed in the view and what the selection is after the view
/// handled it, written to <see cref="StartupLog"/>. A UI test that times out prints the log, so a failed drag shows which events
/// arrived.
/// </summary>
internal static class PointerTrace
{
    private static int s_moves;

    public static bool IsOn { get; } = Environment.GetEnvironmentVariable("BROADSIDE_WINUI_TRACE") == "1";

    /// <summary>Listens on the view, handled events too (the view marks the pointer events it uses as handled).</summary>
    public static void Attach(PdfView view)
    {
        view.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) => Write(view, "pressed", e)), handledEventsToo: true);
        view.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) => Moved(view, e)), handledEventsToo: true);
        view.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) => Write(view, "released", e)), handledEventsToo: true);
        view.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, e) => Write(view, "canceled", e)), handledEventsToo: true);
        view.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, e) => Write(view, "capture lost", e)), handledEventsToo: true);
    }

    /// <summary>The selection as the status line shows it: "Selected 1:4-1:38" (one-based page, offset) or "No selection".</summary>
    public static string Describe(TextSelection? selection) =>
        selection is null || selection.IsEmpty
            ? "No selection"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Selected {selection.Start.PageIndex + 1}:{selection.Start.Offset}-{selection.End.PageIndex + 1}:{selection.End.Offset}");

    // Moves with no button down (hover) are many and say nothing about a drag: only moves in contact are written.
    private static void Moved(PdfView view, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(view).IsInContact)
        {
            s_moves++;
            Write(view, "moved", e);
        }
    }

    private static void Write(PdfView view, string what, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(view);
        StartupLog.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"Pointer {what}: id {e.Pointer.PointerId} {point.PointerDeviceType} at ({point.Position.X:0.0}, {point.Position.Y:0.0}) contact {point.IsInContact} left {point.Properties.IsLeftButtonPressed} handled {e.Handled} moves {s_moves}; {Describe(view.Selection)}"));
    }
}
