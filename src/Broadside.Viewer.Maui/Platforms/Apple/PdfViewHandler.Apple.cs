using Foundation;
using UIKit;
using PlatformContentView = Microsoft.Maui.Platform.ContentView;

namespace Broadside.Viewer.Maui;

/// <summary>iOS and Mac Catalyst: hardware-keyboard keys, and on Mac Catalyst trackpad and wheel scrolling and trackpad pinch.</summary>
internal sealed partial class PdfViewHandler
{
    private UIPanGestureRecognizer? _scroll;
    private UIPinchGestureRecognizer? _pinch;
    private CoreGraphics.CGPoint _lastTranslation;
    private nfloat _lastScale = 1;

    protected override PlatformContentView CreatePlatformView() => new KeyboardContentView(this);

    protected override void ConnectHandler(PlatformContentView platformView)
    {
        base.ConnectHandler(platformView);
        if (!OperatingSystem.IsMacCatalyst())
        {
            return;
        }

        // Scrolling only (no touch types): clicks and drags stay with the canvas's touch events.
        _scroll = new UIPanGestureRecognizer(OnScroll)
        {
            AllowedScrollTypesMask = UIScrollTypeMask.All,
            AllowedTouchTypes = [],
            ShouldRecognizeSimultaneously = (_, _) => true,
        };
        _pinch = new UIPinchGestureRecognizer(OnPinch) { ShouldRecognizeSimultaneously = (_, _) => true };
        platformView.AddGestureRecognizer(_scroll);
        platformView.AddGestureRecognizer(_pinch);
    }

    protected override void DisconnectHandler(PlatformContentView platformView)
    {
        if (_scroll is not null)
        {
            platformView.RemoveGestureRecognizer(_scroll);
            _scroll.Dispose();
            _scroll = null;
        }

        if (_pinch is not null)
        {
            platformView.RemoveGestureRecognizer(_pinch);
            _pinch.Dispose();
            _pinch = null;
        }

        base.DisconnectHandler(platformView);
    }

    private void OnScroll(UIPanGestureRecognizer recognizer)
    {
        if (VirtualView is not PdfView view || PlatformView is not { } platformView)
        {
            return;
        }

        CoreGraphics.CGPoint translation = recognizer.TranslationInView(platformView);
        if (recognizer.State == UIGestureRecognizerState.Began)
        {
            _lastTranslation = default;
        }

        if (recognizer.State is UIGestureRecognizerState.Began or UIGestureRecognizerState.Changed)
        {
            CoreGraphics.CGPoint at = recognizer.LocationInView(platformView);
            double dx = translation.X - _lastTranslation.X;
            double dy = translation.Y - _lastTranslation.Y;
            _lastTranslation = translation;
            view.HandleScroll(-dx, -dy, Modifiers(recognizer.ModifierFlags), at.X, at.Y);
        }
    }

    private void OnPinch(UIPinchGestureRecognizer recognizer)
    {
        if (VirtualView is not PdfView view || PlatformView is not { } platformView)
        {
            return;
        }

        if (recognizer.State == UIGestureRecognizerState.Began)
        {
            _lastScale = 1;
        }

        if (recognizer.State is UIGestureRecognizerState.Began or UIGestureRecognizerState.Changed && _lastScale > 0)
        {
            CoreGraphics.CGPoint at = recognizer.LocationInView(platformView);
            view.HandleMagnify(recognizer.Scale / _lastScale, at.X, at.Y);
            _lastScale = recognizer.Scale;
        }
    }

    internal static InputModifiers Modifiers(UIKeyModifierFlags flags) =>
        ((flags & UIKeyModifierFlags.Shift) != 0 ? InputModifiers.Shift : InputModifiers.None)
        | ((flags & UIKeyModifierFlags.Control) != 0 ? InputModifiers.Control : InputModifiers.None)
        | ((flags & UIKeyModifierFlags.Alternate) != 0 ? InputModifiers.Alt : InputModifiers.None)
        | ((flags & UIKeyModifierFlags.Command) != 0 ? InputModifiers.Meta : InputModifiers.None);

    internal static string? KeyName(UIKey key) => key.KeyCode switch
    {
        UIKeyboardHidUsage.KeyboardUpArrow => "ArrowUp",
        UIKeyboardHidUsage.KeyboardDownArrow => "ArrowDown",
        UIKeyboardHidUsage.KeyboardLeftArrow => "ArrowLeft",
        UIKeyboardHidUsage.KeyboardRightArrow => "ArrowRight",
        UIKeyboardHidUsage.KeyboardPageUp => "PageUp",
        UIKeyboardHidUsage.KeyboardPageDown => "PageDown",
        UIKeyboardHidUsage.KeyboardHome => "Home",
        UIKeyboardHidUsage.KeyboardEnd => "End",
        UIKeyboardHidUsage.KeyboardTab => "Tab",
        UIKeyboardHidUsage.KeyboardReturnOrEnter or UIKeyboardHidUsage.KeypadEnter => "Enter",
        UIKeyboardHidUsage.KeyboardEscape => "Escape",
        UIKeyboardHidUsage.KeyboardSpacebar => " ",
        UIKeyboardHidUsage.KeyboardEqualSign or UIKeyboardHidUsage.KeypadPlus => "+",
        UIKeyboardHidUsage.KeyboardHyphen or UIKeyboardHidUsage.KeypadHyphen => "-",
        _ => key.CharactersIgnoringModifiers is { Length: 1 } text ? text : null,
    };

    /// <summary>The content view of a <see cref="PdfView"/>: first responder for keys, with no focus ring.</summary>
    private sealed class KeyboardContentView : PlatformContentView
    {
        private readonly WeakReference<PdfViewHandler> _handler;

        public KeyboardContentView(PdfViewHandler handler)
        {
            _handler = new WeakReference<PdfViewHandler>(handler);
            if (OperatingSystem.IsIOSVersionAtLeast(15) || OperatingSystem.IsMacCatalystVersionAtLeast(15))
            {
                FocusEffect = null;
            }
        }

        public override bool CanBecomeFirstResponder => true;

        public override void MovedToWindow()
        {
            base.MovedToWindow();
            if (Window is not null)
            {
                BecomeFirstResponder();
            }
        }

        public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent evt)
        {
            bool handled = false;
            if (_handler.TryGetTarget(out PdfViewHandler? handler))
            {
                foreach (UIPress press in presses)
                {
                    if (press.Key is { } key && KeyName(key) is { } name && handler.Key(name, Modifiers(key.ModifierFlags)))
                    {
                        handled = true;
                    }
                }
            }

            if (!handled)
            {
                base.PressesBegan(presses, evt);
            }
        }

        public override void TouchesBegan(NSSet touches, UIEvent? evt)
        {
            if (!IsFirstResponder)
            {
                BecomeFirstResponder();
            }

            base.TouchesBegan(touches, evt);
        }
    }
}
