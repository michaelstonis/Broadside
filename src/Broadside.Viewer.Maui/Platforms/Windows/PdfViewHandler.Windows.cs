using Microsoft.Maui.Platform;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Broadside.Viewer.Maui;

/// <summary>Windows: keys (the panel is a tab stop without the system focus visual).</summary>
internal sealed partial class PdfViewHandler
{
    protected override void ConnectHandler(ContentPanel platformView)
    {
        base.ConnectHandler(platformView);
        platformView.IsTabStop = true;
        platformView.UseSystemFocusVisuals = false;
        platformView.KeyDown += OnKeyDown;
        platformView.PointerPressed += OnPointerPressed;
    }

    protected override void DisconnectHandler(ContentPanel platformView)
    {
        platformView.KeyDown -= OnKeyDown;
        platformView.PointerPressed -= OnPointerPressed;
        base.DisconnectHandler(platformView);
    }

    private static void OnPointerPressed(object sender, PointerRoutedEventArgs e) => ((UIElement)sender).Focus(FocusState.Pointer);

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (KeyName(e.Key) is { } name && Key(name, Modifiers()))
        {
            e.Handled = true;
        }
    }

    private static InputModifiers Modifiers() =>
        (Down(VirtualKey.Shift) ? InputModifiers.Shift : InputModifiers.None)
        | (Down(VirtualKey.Control) ? InputModifiers.Control : InputModifiers.None)
        | (Down(VirtualKey.Menu) ? InputModifiers.Alt : InputModifiers.None);

    private static bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    internal static string? KeyName(VirtualKey key) => key switch
    {
        VirtualKey.Up => "ArrowUp",
        VirtualKey.Down => "ArrowDown",
        VirtualKey.Left => "ArrowLeft",
        VirtualKey.Right => "ArrowRight",
        VirtualKey.PageUp => "PageUp",
        VirtualKey.PageDown => "PageDown",
        VirtualKey.Home => "Home",
        VirtualKey.End => "End",
        VirtualKey.Tab => "Tab",
        VirtualKey.Enter => "Enter",
        VirtualKey.Escape => "Escape",
        VirtualKey.Space => " ",
        VirtualKey.Add or (VirtualKey)187 => "+",
        VirtualKey.Subtract or (VirtualKey)189 => "-",
        >= VirtualKey.Number0 and <= VirtualKey.Number9 => char.ToString((char)('0' + (key - VirtualKey.Number0))),
        >= VirtualKey.A and <= VirtualKey.Z => char.ToString((char)('a' + (key - VirtualKey.A))),
        _ => null,
    };
}
