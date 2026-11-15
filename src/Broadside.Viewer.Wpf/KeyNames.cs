using System.Windows.Input;

namespace Broadside.Viewer.Wpf;

/// <summary>Maps WPF keys onto the DOM <c>KeyboardEvent.key</c> names the controller takes (the Blazor and MAUI shells use the same names).</summary>
internal static class KeyNames
{
    public static string? Name(Key key) => key switch
    {
        Key.Up => "ArrowUp",
        Key.Down => "ArrowDown",
        Key.Left => "ArrowLeft",
        Key.Right => "ArrowRight",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Home => "Home",
        Key.End => "End",
        Key.Tab => "Tab",
        Key.Enter => "Enter",
        Key.Escape => "Escape",
        Key.Space => " ",
        Key.F3 => "F3",
        Key.Add or Key.OemPlus => "+",
        Key.Subtract or Key.OemMinus => "-",
        >= Key.D0 and <= Key.D9 => char.ToString((char)('0' + (key - Key.D0))),
        >= Key.NumPad0 and <= Key.NumPad9 => char.ToString((char)('0' + (key - Key.NumPad0))),
        >= Key.A and <= Key.Z => char.ToString((char)('a' + (key - Key.A))),
        _ => null,
    };

    public static InputModifiers Modifiers(ModifierKeys keys) =>
        ((keys & ModifierKeys.Shift) != 0 ? InputModifiers.Shift : InputModifiers.None)
        | ((keys & ModifierKeys.Control) != 0 ? InputModifiers.Control : InputModifiers.None)
        | ((keys & ModifierKeys.Alt) != 0 ? InputModifiers.Alt : InputModifiers.None)
        | ((keys & ModifierKeys.Windows) != 0 ? InputModifiers.Meta : InputModifiers.None);
}
