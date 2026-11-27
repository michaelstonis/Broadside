using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Broadside.Viewer.Blazor;

/// <summary>
/// The default markup of one <see cref="ViewerCommand"/> in a <see cref="PdfToolbar"/>: a button, a pressed-state button, a select, the
/// page box or the find box, with <c>data-command</c> set to the command's identifier. An <see cref="PdfToolbar.ItemTemplate"/> renders
/// it for the commands it does not restyle.
/// </summary>
/// <remarks>Not a spec feature, except the page box (ISO 32000-2 §12.4.2, page labels).</remarks>
public sealed class PdfToolbarItem : ComponentBase, IDisposable
{
    private ViewerCommand? _followed;
    private string _findText = string.Empty;
    private int _pageResets;

    /// <summary>Gets or sets the command to render.</summary>
    [Parameter]
    [EditorRequired]
    public ViewerCommand? Command { get; set; }

    /// <summary>Stops following the command.</summary>
    public void Dispose() => Follow(null);

    /// <inheritdoc/>
    protected override void OnParametersSet() => Follow(Command);

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        switch (Command)
        {
            case ViewerButtonCommand button:
                builder.OpenElement(0, "button");
                Common(builder, button);
                builder.AddAttribute(10, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, button.Execute));
                builder.AddContent(11, button.Glyph ?? button.Label);
                builder.CloseElement();
                break;
            case ViewerToggleCommand toggle:
                builder.OpenElement(20, "button");
                Common(builder, toggle);
                builder.AddAttribute(30, "aria-pressed", toggle.IsChecked ? "true" : "false");
                builder.AddAttribute(31, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => toggle.IsChecked = !toggle.IsChecked));
                builder.AddContent(32, toggle.Glyph ?? toggle.Label);
                builder.CloseElement();
                break;
            case ViewerChoiceCommand choice:
                RenderChoice(builder, choice);
                break;
            case ViewerPageCommand page:
                RenderPageBox(builder, page);
                break;
            case ViewerSearchCommand search:
                RenderFindBox(builder, search);
                break;
        }
    }

    private static void Common(RenderTreeBuilder builder, ViewerCommand command)
    {
        builder.AddAttribute(1, "type", "button");
        builder.AddAttribute(2, "class", "bs-toolbar-button");
        builder.AddAttribute(3, "data-command", command.Id);
        builder.AddAttribute(4, "aria-label", command.Label);
        builder.AddAttribute(5, "title", Title(command));
        builder.AddAttribute(6, "disabled", !command.IsEnabled);
    }

    private static string Title(ViewerCommand command) =>
        command.ToolTip is { } tip ? command.Shortcut is { } shortcut ? $"{tip} ({shortcut})" : tip : command.Label;

    private void RenderChoice(RenderTreeBuilder builder, ViewerChoiceCommand choice)
    {
        builder.OpenElement(40, "select");
        builder.AddAttribute(41, "class", "bs-toolbar-select");
        builder.AddAttribute(42, "data-command", choice.Id);
        builder.AddAttribute(43, "aria-label", choice.Label);
        builder.AddAttribute(44, "title", Title(choice));
        builder.AddAttribute(45, "disabled", !choice.IsEnabled);
        builder.AddAttribute(46, "value", choice.Selected.ToString(CultureInfo.InvariantCulture));
        builder.AddAttribute(47, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            if (int.TryParse(e.Value as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index >= 0 && index < choice.Choices.Count)
            {
                choice.Selected = index;
            }
        }));
        if (choice.Selected < 0)
        {
            // What the view shows between the choices (a zoom between the presets).
            builder.OpenElement(48, "option");
            builder.AddAttribute(49, "value", "-1");
            builder.AddAttribute(50, "hidden", true);
            builder.AddContent(51, choice.Text.Length > 0 ? choice.Text : choice.Label);
            builder.CloseElement();
        }

        for (int i = 0; i < choice.Choices.Count; i++)
        {
            builder.OpenElement(52, "option");
            builder.AddAttribute(53, "value", i.ToString(CultureInfo.InvariantCulture));
            builder.AddContent(54, choice.Choices[i]);
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private void RenderPageBox(RenderTreeBuilder builder, ViewerPageCommand page)
    {
        builder.OpenElement(60, "span");
        builder.AddAttribute(61, "class", "bs-toolbar-page");
        builder.AddAttribute(62, "data-command", page.Id);
        builder.OpenElement(63, "input");
        builder.SetKey(_pageResets); // a refused entry is replaced by a fresh box showing the current page
        builder.AddAttribute(64, "type", "text");
        builder.AddAttribute(65, "aria-label", page.Label);
        builder.AddAttribute(66, "title", page.Error ?? Title(page));
        builder.AddAttribute(67, "aria-invalid", page.Error is null ? "false" : "true");
        builder.AddAttribute(68, "disabled", !page.IsEnabled);
        builder.AddAttribute(69, "value", page.Text);
        builder.AddAttribute(70, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            if (!page.TrySubmit(e.Value as string ?? string.Empty))
            {
                _pageResets++;
            }
        }));
        builder.CloseElement();
        builder.OpenElement(71, "span");
        builder.AddAttribute(72, "class", "bs-toolbar-count");
        builder.AddContent(73, page.PageCount > 0 ? "/ " + page.PageCount.ToString(CultureInfo.InvariantCulture) : string.Empty);
        builder.CloseElement();
        builder.CloseElement();
    }

    private void RenderFindBox(RenderTreeBuilder builder, ViewerSearchCommand search)
    {
        builder.OpenElement(80, "span");
        builder.AddAttribute(81, "class", "bs-toolbar-find");
        builder.AddAttribute(82, "data-command", search.Id);
        builder.OpenElement(83, "input");
        builder.AddAttribute(84, "type", "search");
        builder.AddAttribute(85, "placeholder", search.Label);
        builder.AddAttribute(86, "aria-label", search.Label);
        builder.AddAttribute(87, "title", Title(search));
        builder.AddAttribute(88, "disabled", !search.IsEnabled);
        builder.AddAttribute(89, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e => _findText = e.Value as string ?? string.Empty));
        builder.AddAttribute(90, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, e =>
        {
            if (e.Key != "Enter")
            {
                return;
            }

            if (_findText.Length > 0 && string.Equals(_findText, search.Query, StringComparison.Ordinal))
            {
                if (e.ShiftKey)
                {
                    search.Previous();
                }
                else
                {
                    search.Next();
                }
            }
            else
            {
                search.Submit(_findText);
            }
        }));
        builder.CloseElement();
        FindButton(builder, 91, search, "Previous match", "‹", search.Previous);
        FindButton(builder, 92, search, "Next match", "›", search.Next);
        builder.OpenElement(93, "span");
        builder.AddAttribute(94, "class", "bs-toolbar-status");
        builder.AddAttribute(95, "role", "status");
        builder.AddContent(96, search.Status);
        builder.CloseElement();
        builder.CloseElement();
    }

    private void FindButton(RenderTreeBuilder builder, int sequence, ViewerSearchCommand search, string label, string glyph, Action step)
    {
        builder.OpenRegion(sequence);
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "type", "button");
        builder.AddAttribute(2, "class", "bs-toolbar-button");
        builder.AddAttribute(3, "aria-label", label);
        builder.AddAttribute(4, "title", label);
        builder.AddAttribute(5, "disabled", !search.IsEnabled);
        builder.AddAttribute(6, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, step));
        builder.AddContent(7, glyph);
        builder.CloseElement();
        builder.CloseRegion();
    }

    private void Follow(ViewerCommand? command)
    {
        if (ReferenceEquals(command, _followed))
        {
            return;
        }

        _followed?.Changed -= OnCommandChanged;
        _followed = command;
        _followed?.Changed += OnCommandChanged;
    }

    private void OnCommandChanged(object? sender, EventArgs e) => _ = InvokeAsync(StateHasChanged);
}
