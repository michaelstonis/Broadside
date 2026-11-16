using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Broadside.Viewer.WinUI;

/// <summary>WinUI keys and modifiers as the controller names them (DOM <c>KeyboardEvent.key</c> names, as every shell does).</summary>
internal static partial class KeyNames
{
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyMenu = 0x12;

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

    /// <summary>
    /// The modifier keys held now, as WinUI's keyboard state for the UI thread or Win32's (<c>GetKeyState</c>) says: either may miss
    /// a key the other has (injected and remote input reach them differently), so both count.
    /// </summary>
    public static InputModifiers Current() => Xaml() | Win32();

    /// <summary>The modifier keys WinUI's keyboard state for the UI thread holds (<see cref="InputKeyboardSource.GetKeyStateForCurrentThread"/>).</summary>
    public static InputModifiers Xaml() =>
        (Down(VirtualKey.Shift) ? InputModifiers.Shift : InputModifiers.None)
        | (Down(VirtualKey.Control) ? InputModifiers.Control : InputModifiers.None)
        | (Down(VirtualKey.Menu) ? InputModifiers.Alt : InputModifiers.None);

    /// <summary>The modifier keys Win32's keyboard state for the thread holds (<c>GetKeyState</c>: the state as of the last input message read).</summary>
    public static InputModifiers Win32() =>
        (Win32Down(VirtualKeyShift) ? InputModifiers.Shift : InputModifiers.None)
        | (Win32Down(VirtualKeyControl) ? InputModifiers.Control : InputModifiers.None)
        | (Win32Down(VirtualKeyMenu) ? InputModifiers.Alt : InputModifiers.None);

    /// <summary>The modifier a modifier key stands for (either Shift, Ctrl or Alt key), or none for any other key.</summary>
    public static InputModifiers ModifierOf(VirtualKey key) => key switch
    {
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift => InputModifiers.Shift,
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl => InputModifiers.Control,
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu => InputModifiers.Alt,
        _ => InputModifiers.None,
    };

    private static bool Down(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    // The high-order bit of GetKeyState is set while the key is down.
    private static bool Win32Down(int key) => GetKeyState(key) < 0;

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial short GetKeyState(int virtualKey);
}
