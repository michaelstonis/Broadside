using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Broadside.Viewer.WinUI;

/// <summary>WinUI keys and modifiers as the controller names them (DOM <c>KeyboardEvent.key</c> names, as every shell does).</summary>
internal static class KeyNames
{
    public static string? Name(VirtualKey key) => key switch
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
        VirtualKey.Add or (VirtualKey)187 => "+", // the numeric keypad's +, and VK_OEM_PLUS (=/+)
        VirtualKey.Subtract or (VirtualKey)189 => "-", // the keypad's -, and VK_OEM_MINUS
        >= VirtualKey.Number0 and <= VirtualKey.Number9 => char.ToString((char)('0' + (key - VirtualKey.Number0))),
        >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9 => char.ToString((char)('0' + (key - VirtualKey.NumberPad0))),
        >= VirtualKey.A and <= VirtualKey.Z => char.ToString((char)('a' + (key - VirtualKey.A))),
        _ => null,
    };

    public static InputModifiers Modifiers(VirtualKeyModifiers modifiers) =>
        ((modifiers & VirtualKeyModifiers.Shift) != 0 ? InputModifiers.Shift : InputModifiers.None)
        | ((modifiers & VirtualKeyModifiers.Control) != 0 ? InputModifiers.Control : InputModifiers.None)
        | ((modifiers & VirtualKeyModifiers.Menu) != 0 ? InputModifiers.Alt : InputModifiers.None)
        | ((modifiers & VirtualKeyModifiers.Windows) != 0 ? InputModifiers.Meta : InputModifiers.None);

    /// <summary>The modifier keys held now (a key event carries none of its own in WinUI).</summary>
    public static InputModifiers Current() =>
        (Down(VirtualKey.Shift) ? InputModifiers.Shift : InputModifiers.None)
        | (Down(VirtualKey.Control) ? InputModifiers.Control : InputModifiers.None)
        | (Down(VirtualKey.Menu) ? InputModifiers.Alt : InputModifiers.None);

    private static bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
}
