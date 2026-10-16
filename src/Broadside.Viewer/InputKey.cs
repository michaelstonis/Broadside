namespace Broadside.Viewer;

/// <summary>The keys the <see cref="InputModel"/> acts on; a shell maps its platform's key codes to these.</summary>
/// <remarks>Spec #33 viewer capability 14. From the DOM's <c>KeyboardEvent.key</c>: <c>"+"</c> and <c>"="</c> are <see cref="Plus"/>.</remarks>
public enum InputKey
{
    /// <summary>Any other key: not handled.</summary>
    None,

    /// <summary>Left arrow: scrolls left a line; in a paged mode with nothing to scroll left, turns to the previous spread.</summary>
    ArrowLeft,

    /// <summary>Right arrow: scrolls right a line; in a paged mode with nothing to scroll right, turns to the next spread.</summary>
    ArrowRight,

    /// <summary>Up arrow: scrolls up a line; in a paged mode at the spread's top edge, turns to the previous spread.</summary>
    ArrowUp,

    /// <summary>Down arrow: scrolls down a line; in a paged mode at the spread's bottom edge, turns to the next spread.</summary>
    ArrowDown,

    /// <summary>Page Up: scrolls back nine tenths of the viewport along the strip, or turns to the previous spread in a paged mode.</summary>
    PageUp,

    /// <summary>Page Down: scrolls on nine tenths of the viewport along the strip, or turns to the next spread in a paged mode.</summary>
    PageDown,

    /// <summary>Space: as <see cref="PageDown"/>, or <see cref="PageUp"/> with Shift.</summary>
    Space,

    /// <summary>Home: goes to the first page.</summary>
    Home,

    /// <summary>End: goes to the last page.</summary>
    End,

    /// <summary>Plus (or equals): zooms in a step about the viewport's centre.</summary>
    Plus,

    /// <summary>Minus: zooms out a step about the viewport's centre.</summary>
    Minus,

    /// <summary>Zero: zooms to fit the current page's width.</summary>
    Zero,
}
