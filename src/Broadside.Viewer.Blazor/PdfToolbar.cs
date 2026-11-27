using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// A toolbar for a <see cref="PdfViewer"/> (or any <see cref="IDocumentViewer"/>): the shared <see cref="ViewerToolbar"/> model rendered
/// as HTML buttons, selects and inputs in a <c>role="toolbar"</c> element; overflow commands go to a <c>&lt;details&gt;</c> "⋯" menu.
/// </summary>
/// <remarks>
/// <para>
/// Not a spec feature, except the page box (ISO 32000-2 §12.4.2, page labels) and the default visibility (§12.2, Table 147,
/// <c>HideToolbar</c>). The viewer itself stays toolbar-free: an app that wants none pays nothing.
/// </para>
/// <para>
/// Bind it with <c>&lt;PdfViewer @ref="viewer" /&gt; &lt;PdfToolbar Viewer="viewer" /&gt;</c>; <see cref="Toolbar"/> is the model, where
/// an app adds, removes, reorders or hides commands. <see cref="ItemTemplate"/> renders every item (its context is the command); a
/// template falls back to the default markup with <see cref="PdfToolbarItem"/>. Every item carries <c>data-command</c> with the
/// command's identifier. The layout rules are in a <c>:where(.bs-toolbar)</c> style of no specificity, so <see cref="Class"/> wins.
/// </para>
/// </remarks>
public sealed class PdfToolbar : ComponentBase, IDisposable
{
    private const string Style =
        ":where(.bs-toolbar){display:flex;flex-wrap:wrap;align-items:center;gap:4px}"
        + ":where(.bs-toolbar-more){position:relative}"
        + ":where(.bs-toolbar-more>summary){list-style:none;cursor:pointer;padding:2px 8px}"
        + ":where(.bs-toolbar-overflow){position:absolute;right:0;z-index:10;display:flex;flex-direction:column;gap:4px;padding:6px;background:Canvas;border:1px solid GrayText;border-radius:4px}"
        + ":where(.bs-toolbar-page,.bs-toolbar-find){display:inline-flex;align-items:center;gap:4px}"
        + ":where(.bs-toolbar-page input){width:4em;text-align:center}";

    private IDocumentViewer? _boundViewer;
    private ViewerToolbarOptions? _boundOptions;

    /// <summary>Gets or sets the viewer the toolbar drives (a <see cref="PdfViewer"/>); a new model is created for each.</summary>
    [Parameter]
    public IDocumentViewer? Viewer { get; set; }

    /// <summary>Gets or sets the options of the model (the commands, their order, the zoom presets, compact); a change creates the model again.</summary>
    [Parameter]
    public ViewerToolbarOptions? Options { get; set; }

    /// <summary>Gets or sets a template for every item (its context is the command); <see cref="PdfToolbarItem"/> renders the default.</summary>
    [Parameter]
    public RenderFragment<ViewerCommand>? ItemTemplate { get; set; }

    /// <summary>Gets or sets CSS classes added to the toolbar element (<c>.bs-toolbar</c>).</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Gets or sets the toolbar's accessible name ("Document toolbar" by default).</summary>
    [Parameter]
    public string? Label { get; set; }

    /// <summary>Gets the model bound to <see cref="Viewer"/>, or <see langword="null"/> without a viewer.</summary>
    public ViewerToolbar? Toolbar { get; private set; }

    /// <summary>Unbinds the toolbar from its viewer.</summary>
    public void Dispose() => Bind(null, null);

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(Viewer, _boundViewer) || !ReferenceEquals(Options, _boundOptions))
        {
            Bind(Viewer, Options);
        }
    }

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.OpenElement(0, "style");
        builder.AddContent(1, Style);
        builder.CloseElement();

        builder.OpenElement(2, "div");
        builder.AddAttribute(3, "class", Class is null ? "bs-toolbar" : "bs-toolbar " + Class);
        builder.AddAttribute(4, "role", "toolbar");
        builder.AddAttribute(5, "aria-label", Label ?? "Document toolbar");
        builder.AddAttribute(6, "hidden", Toolbar is { IsVisible: false });
        if (Toolbar is { } toolbar)
        {
            List<ViewerCommand>? overflow = null;
            foreach (ViewerCommand command in toolbar.Items)
            {
                if (command.Placement == ViewerCommandPlacement.Overflow)
                {
                    (overflow ??= []).Add(command);
                }
                else if (command.Placement == ViewerCommandPlacement.Primary)
                {
                    RenderItem(builder, command);
                }
            }

            if (overflow is not null)
            {
                builder.OpenElement(7, "details");
                builder.AddAttribute(8, "class", "bs-toolbar-more");
                builder.OpenElement(9, "summary");
                builder.AddAttribute(10, "aria-label", "More");
                builder.AddAttribute(11, "title", "More");
                builder.AddAttribute(12, "data-command", "toolbar.more");
                builder.AddContent(13, "⋯");
                builder.CloseElement();
                builder.OpenElement(14, "div");
                builder.AddAttribute(15, "class", "bs-toolbar-overflow");
                foreach (ViewerCommand command in overflow)
                {
                    RenderItem(builder, command);
                }

                builder.CloseElement();
                builder.CloseElement();
            }
        }

        builder.CloseElement();
    }

    private void RenderItem(RenderTreeBuilder builder, ViewerCommand command)
    {
        builder.OpenRegion(20);
        if (ItemTemplate is { } template)
        {
            builder.AddContent(0, template(command));
        }
        else
        {
            builder.OpenComponent<PdfToolbarItem>(1);
            builder.SetKey(command);
            builder.AddComponentParameter(2, nameof(PdfToolbarItem.Command), command);
            builder.CloseComponent();
        }

        builder.CloseRegion();
    }

    private void Bind(IDocumentViewer? viewer, ViewerToolbarOptions? options)
    {
        _boundViewer = viewer;
        _boundOptions = options;
        if (Toolbar is { } old)
        {
            old.Changed -= OnToolbarChanged;
            old.Dispose();
        }

        Toolbar = viewer is null ? null : new ViewerToolbar(viewer, options);
        Toolbar?.Changed += OnToolbarChanged;
    }

    private void OnToolbarChanged(object? sender, EventArgs e) => _ = InvokeAsync(StateHasChanged);
}
