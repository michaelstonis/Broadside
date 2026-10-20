using Android.Content;
using Android.Views;
using Microsoft.Maui.Platform;

namespace Broadside.Viewer.Maui;

/// <summary>Android: hardware-keyboard keys (and a focusable view with no focus highlight).</summary>
internal sealed partial class PdfViewHandler
{
    protected override ContentViewGroup CreatePlatformView() => new KeyboardContentViewGroup(Context, this);

    internal static string? KeyName(Keycode code, KeyEvent? e) => code switch
    {
        Keycode.DpadUp => "ArrowUp",
        Keycode.DpadDown => "ArrowDown",
        Keycode.DpadLeft => "ArrowLeft",
        Keycode.DpadRight => "ArrowRight",
        Keycode.PageUp => "PageUp",
        Keycode.PageDown => "PageDown",
        Keycode.MoveHome => "Home",
        Keycode.MoveEnd => "End",
        Keycode.Tab => "Tab",
        Keycode.Enter or Keycode.NumpadEnter => "Enter",
        Keycode.Escape => "Escape",
        Keycode.Space => " ",
        Keycode.Plus or Keycode.Equals or Keycode.NumpadAdd => "+",
        Keycode.Minus or Keycode.NumpadSubtract => "-",
        _ => e?.GetUnicodeChar((MetaKeyStates)0) is int c and > 32 ? char.ToString((char)c) : null,
    };

    internal static InputModifiers Modifiers(KeyEvent? e) =>
        e is null ? InputModifiers.None
            : (e.IsShiftPressed ? InputModifiers.Shift : InputModifiers.None)
            | (e.IsCtrlPressed ? InputModifiers.Control : InputModifiers.None)
            | (e.IsAltPressed ? InputModifiers.Alt : InputModifiers.None)
            | (e.IsMetaPressed ? InputModifiers.Meta : InputModifiers.None);

    private sealed class KeyboardContentViewGroup : ContentViewGroup
    {
        private readonly WeakReference<PdfViewHandler> _handler;

        public KeyboardContentViewGroup(Context context, PdfViewHandler handler)
            : base(context)
        {
            _handler = new WeakReference<PdfViewHandler>(handler);
            Focusable = true;
            FocusableInTouchMode = true;
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                DefaultFocusHighlightEnabled = false;
            }
        }

        public override bool DispatchTouchEvent(MotionEvent? e)
        {
            if (e?.ActionMasked == MotionEventActions.Down && !HasFocus)
            {
                RequestFocus();
            }

            return base.DispatchTouchEvent(e);
        }

        public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
        {
            if (_handler.TryGetTarget(out PdfViewHandler? handler) && KeyName(keyCode, e) is { } name && handler.Key(name, Modifiers(e)))
            {
                return true;
            }

            return base.OnKeyDown(keyCode, e);
        }
    }
}
