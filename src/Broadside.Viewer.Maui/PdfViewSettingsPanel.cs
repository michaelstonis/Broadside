using System.Globalization;
using Broadside.Rendering;
using Microsoft.Maui.Controls;

namespace Broadside.Viewer.Maui;

/// <summary>
/// How a <see cref="PdfView"/> shows its document: the page layout (continuous, single page, two-up, two-up continuous, horizontal,
/// with or without a cover page alone), smart dark mode, GPU or CPU composition, a view rotation added to every page's own, and zoom.
/// None of these changes the file.
/// </summary>
/// <remarks>Spec #33 viewer capabilities 2 (layout modes), 3 (zoom) and 9 (dark mode); ISO 32000-2 §7.7.3.3 (the page's Rotate entry the rotation adds to).</remarks>
public sealed class PdfViewSettingsPanel : ContentView
{
    /// <summary>The <see cref="Viewer"/> property.</summary>
    public static readonly BindableProperty ViewerProperty =
        BindableProperty.Create(nameof(Viewer), typeof(PdfView), typeof(PdfViewSettingsPanel), null, propertyChanged: (b, _, n) => ((PdfViewSettingsPanel)b).Attach((PdfView?)n));

    private static readonly (PageLayoutMode Mode, string Title, string Id)[] Modes =
    [
        (PageLayoutMode.Continuous, "Continuous", "layout-continuous"),
        (PageLayoutMode.SinglePage, "Single page", "layout-single"),
        (PageLayoutMode.TwoUp, "Two-up", "layout-two-up"),
        (PageLayoutMode.TwoUpContinuous, "Two-up continuous", "layout-two-up-continuous"),
        (PageLayoutMode.Horizontal, "Horizontal", "layout-horizontal"),
    ];

    private readonly Button[] _modes;
    private readonly Switch _cover;
    private readonly Switch _dark;
    private readonly Switch _gpu;
    private readonly Label _rotation = new() { AutomationId = "rotation-value", VerticalTextAlignment = TextAlignment.Center, MinimumWidthRequest = 48, HorizontalTextAlignment = TextAlignment.Center };
    private readonly Label _zoom = PanelKit.Heading("Zoom");
    private PdfView? _view;
    private bool _updating;

    /// <summary>Creates the panel; set <see cref="Viewer"/> to the view it sets up.</summary>
    public PdfViewSettingsPanel()
    {
        _modes = [.. Modes.Select(m => PanelKit.Button(m.Title, m.Id, () => SetLayout(LayoutChanges.Copy(_view?.LayoutOptions).WithMode(m.Mode))))];
        (View coverRow, _cover) = PanelKit.Toggle("Cover page alone (two-up)", "layout-cover", false, value => SetLayout(LayoutChanges.Copy(_view?.LayoutOptions).WithCover(value)));
        (View darkRow, _dark) = PanelKit.Toggle("Dark mode", "view-dark-mode", false, value =>
        {
            if (_view is not null && !_updating)
            {
                _view.ColorFilter = value ? ColorFilter.DarkMode : ColorFilter.None;
            }
        });
        (View gpuRow, _gpu) = PanelKit.Toggle("Compose on the GPU", "view-gpu", true, value =>
        {
            if (_view is not null && !_updating)
            {
                _view.UseHardwareAcceleration = value;
            }
        });

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    PanelKit.Heading("Page layout"),
                    PanelKit.Wrap(_modes),
                    coverRow,
                    PanelKit.Heading("Appearance"),
                    darkRow,
                    PanelKit.Hint("Smart dark mode inverts the lightness of text and drawings and keeps their hue; images keep their colors. The tiles are redrawn from the cached display lists."),
                    PanelKit.Heading("Renderer"),
                    gpuRow,
                    PanelKit.Hint("Tiles render off the UI thread with SkiaSharp; they are composed on the GPU (Metal, OpenGL ES or Direct3D) or on the CPU."),
                    PanelKit.Heading("Rotation"),
                    new HorizontalStackLayout
                    {
                        Spacing = 6,
                        Children =
                        {
                            PanelKit.Button("⟲ Left", "rotate-left", () => SetLayout(LayoutChanges.Rotate(_view?.LayoutOptions, -1)), "Rotate left"),
                            _rotation,
                            PanelKit.Button("Right ⟳", "rotate-right", () => SetLayout(LayoutChanges.Rotate(_view?.LayoutOptions, 1)), "Rotate right"),
                        },
                    },
                    PanelKit.Hint("A view rotation added to every page's own; the file is not changed."),
                    _zoom,
                    PanelKit.Wrap(
                        PanelKit.Button("−", "zoom-out", () => _view?.ZoomBy(1 / 1.25), "Zoom out"),
                        PanelKit.Button("+", "zoom-in", () => _view?.ZoomBy(1.25), "Zoom in"),
                        PanelKit.Button("Fit width", "zoom-fit-width", () => _view?.ZoomTo(ZoomMode.FitWidth)),
                        PanelKit.Button("Fit page", "zoom-fit-page", () => _view?.ZoomTo(ZoomMode.FitPage)),
                        PanelKit.Button("Actual size", "zoom-actual", () => _view?.ZoomTo(ZoomMode.ActualSize))),
                    PanelKit.Hint("Pinch, double-tap, Ctrl or ⌘ with the wheel or trackpad, and + / − on a keyboard zoom too."),
                },
            },
        };
        Update();
    }

    /// <summary>Gets or sets the view this panel sets up.</summary>
    public PdfView? Viewer
    {
        get => (PdfView?)GetValue(ViewerProperty);
        set => SetValue(ViewerProperty, value);
    }

    private void Attach(PdfView? view)
    {
        _view?.StateChanged -= OnStateChanged;
        _view = view;
        _view?.StateChanged += OnStateChanged;
        Update();
    }

    private void OnStateChanged(object? sender, PdfViewStateChangedEventArgs e) =>
        _zoom.Text = e.State.PageCount > 0 ? string.Create(CultureInfo.InvariantCulture, $"Zoom {e.State.Zoom * 100:0}%") : "Zoom";

    private void SetLayout(PageLayoutOptions options)
    {
        if (_view is not null && !_updating)
        {
            _view.LayoutOptions = options;
            Update();
        }
    }

    private void Update()
    {
        _updating = true;
        PageLayoutOptions layout = _view?.LayoutOptions ?? new PageLayoutOptions();
        PanelKit.Select(_modes, _modes[Array.FindIndex(Modes, m => m.Mode == layout.Mode) is int i and >= 0 ? i : 0]);
        _cover.IsToggled = layout.ShowCover;
        _dark.IsToggled = _view?.ColorFilter == ColorFilter.DarkMode;
        _gpu.IsToggled = _view?.UseHardwareAcceleration ?? true;
        _rotation.Text = layout.Rotation.ToString(CultureInfo.InvariantCulture) + "°";
        _updating = false;
    }
}
