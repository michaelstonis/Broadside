#if IOS
using Foundation;
using Metal;
using Microsoft.Maui.Handlers;
using SkiaSharp;
using SkiaSharp.Views.iOS;
using SkiaSharp.Views.Maui;
using UIKit;

namespace Broadside.Viewer.Maui;

/// <summary>iOS: <see cref="MetalCanvas"/> as SkiaSharp's Metal view, painted on demand, with touches forwarded in canvas pixels.</summary>
#pragma warning disable CA1001 // the touch recognizer is released in DisconnectHandler, the handler's end of life
internal sealed class MetalCanvasHandler : ViewHandler<MetalCanvas, SKMetalView>
{
    private static readonly IPropertyMapper<MetalCanvas, MetalCanvasHandler> PropertyMapper =
        new PropertyMapper<MetalCanvas, MetalCanvasHandler>(ViewMapper);

    private static readonly CommandMapper<MetalCanvas, MetalCanvasHandler> CommandMapper =
        new(ViewCommandMapper) { [nameof(MetalCanvas.InvalidateSurface)] = (handler, _, _) => handler.PlatformView?.SetNeedsDisplay() };

    private static readonly Lazy<bool> Device = new(() =>
    {
        using IMTLDevice? device = MTLDevice.SystemDefault;
        return device is not null;
    });

    private TouchRecognizer? _touches;

    public MetalCanvasHandler()
        : base(PropertyMapper, CommandMapper)
    {
    }

    /// <summary>Gets whether the device has a Metal GPU (every iOS device and simulator the package supports; checked anyway).</summary>
    public static bool HasDevice => Device.Value;

    protected override SKMetalView CreatePlatformView() => new()
    {
        // On demand: drawn after SetNeedsDisplay only, never by a free-running loop.
        Paused = true,
        EnableSetNeedsDisplay = true,
        MultipleTouchEnabled = true,
        Opaque = true,
    };

    protected override void ConnectHandler(SKMetalView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.PaintSurface += OnPaintSurface;
        _touches = new TouchRecognizer(this);
        platformView.AddGestureRecognizer(_touches);
    }

    protected override void DisconnectHandler(SKMetalView platformView)
    {
        platformView.PaintSurface -= OnPaintSurface;
        if (_touches is not null)
        {
            platformView.RemoveGestureRecognizer(_touches);
            _touches.Dispose();
            _touches = null;
        }

        base.DisconnectHandler(platformView);
    }

    private void OnPaintSurface(object? sender, SKPaintMetalSurfaceEventArgs e) => VirtualView?.OnPaint(e.Surface, e.BackendRenderTarget.Width);

    private void Forward(NSSet touches, SKTouchAction action, bool inContact)
    {
        if (VirtualView is not { } canvas || PlatformView is not { } view)
        {
            return;
        }

        nfloat scale = view.ContentScaleFactor;
        foreach (UITouch touch in touches.Cast<UITouch>())
        {
            CoreGraphics.CGPoint at = touch.LocationInView(view);
            SKTouchDeviceType device = touch.Type == UITouchType.Stylus ? SKTouchDeviceType.Pen : SKTouchDeviceType.Touch;
            canvas.OnTouch(new SKTouchEventArgs(
                touch.Handle.Handle.ToInt64(),
                action,
                SKMouseButton.Left,
                device,
                new SKPoint((float)(at.X * scale), (float)(at.Y * scale)),
                inContact));
        }
    }

    /// <summary>Observes every touch on the canvas without recognizing a gesture, so nothing else is held back.</summary>
    private sealed class TouchRecognizer : UIGestureRecognizer
    {
        private readonly WeakReference<MetalCanvasHandler> _handler;

        public TouchRecognizer(MetalCanvasHandler handler)
        {
            _handler = new WeakReference<MetalCanvasHandler>(handler);
            CancelsTouchesInView = false;
            DelaysTouchesBegan = false;
            DelaysTouchesEnded = false;
        }

        public override void TouchesBegan(NSSet touches, UIEvent evt)
        {
            base.TouchesBegan(touches, evt);
            Send(touches, SKTouchAction.Pressed, inContact: true);
        }

        public override void TouchesMoved(NSSet touches, UIEvent evt)
        {
            base.TouchesMoved(touches, evt);
            Send(touches, SKTouchAction.Moved, inContact: true);
        }

        public override void TouchesEnded(NSSet touches, UIEvent evt)
        {
            base.TouchesEnded(touches, evt);
            Send(touches, SKTouchAction.Released, inContact: false);
        }

        public override void TouchesCancelled(NSSet touches, UIEvent evt)
        {
            base.TouchesCancelled(touches, evt);
            Send(touches, SKTouchAction.Cancelled, inContact: false);
        }

        private void Send(NSSet touches, SKTouchAction action, bool inContact)
        {
            if (_handler.TryGetTarget(out MetalCanvasHandler? handler))
            {
                handler.Forward(touches, action, inContact);
            }
        }
    }
}
#pragma warning restore CA1001
#endif
