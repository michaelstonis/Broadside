using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>One button of a new radio button field: where its widget goes and the on state that selects it.</summary>
/// <remarks>
/// ISO 32000-2 §12.7.5.2.4: each widget of a radio button field has an appearance for its on state and for <c>Off</c>; the field's
/// value (<c>V</c>) is the on state of the selected button. Buttons with the same on state turn on together when the field's
/// RadiosInUnison flag is set.
/// </remarks>
public sealed class PdfRadioButton
{
    /// <summary>Initializes a new instance of the <see cref="PdfRadioButton"/> class.</summary>
    /// <param name="page">The page the widget is on: a page of the document the field is created in.</param>
    /// <param name="rectangle">The widget's rectangle, in default user space.</param>
    /// <param name="state">The button's on state, written as a name (its export value); not <c>Off</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> or <paramref name="state"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="state"/> is empty, <c>Off</c>, or holds U+0000.</exception>
    /// <remarks>ISO 32000-2 §12.7.5.2.4 and §12.5.5 (appearance states); §7.3.5 (a name holds no null byte).</remarks>
    public PdfRadioButton(PdfPage page, PdfRectangle rectangle, string state)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Length == 0 || state == "Off" || state.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("An on state is a non-empty name other than Off, without U+0000 (ISO 32000-2 §12.7.5.2.4, §7.3.5).", nameof(state));
        }

        Page = page;
        Rectangle = rectangle;
        State = state;
    }

    /// <summary>Gets the page the widget is on.</summary>
    public PdfPage Page { get; }

    /// <summary>Gets the widget's rectangle, in default user space.</summary>
    /// <remarks>ISO 32000-2 §12.5.2, Table 166 (<c>Rect</c>).</remarks>
    public PdfRectangle Rectangle { get; }

    /// <summary>Gets the button's on state, the name its appearance subdictionary and the field's value use.</summary>
    /// <remarks>ISO 32000-2 §12.7.5.2.4.</remarks>
    public string State { get; }

    /// <summary>Gets the on state as a name.</summary>
    internal CosName StateName => new(State);
}
