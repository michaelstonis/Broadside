namespace Broadside;

/// <summary>How a configuration initializes the states of all groups before its <c>ON</c> and <c>OFF</c> arrays apply (<c>BaseState</c>).</summary>
/// <remarks>ISO 32000-2 §8.11.4.3, Table 99. Default <see cref="On"/>; in the default configuration it shall be <see cref="On"/>.</remarks>
public enum PdfOptionalContentBaseState
{
    /// <summary>Every group is turned ON.</summary>
    On,

    /// <summary>Every group is turned OFF.</summary>
    Off,

    /// <summary>Every group keeps its current state.</summary>
    Unchanged,
}

/// <summary>Which groups of the <c>Order</c> array a user interface displays (<c>ListMode</c>).</summary>
/// <remarks>ISO 32000-2 §8.11.4.3, Table 99. Default <see cref="AllPages"/>.</remarks>
public enum PdfOptionalContentListMode
{
    /// <summary>Every group in the <c>Order</c> array.</summary>
    AllPages,

    /// <summary>Only groups referenced by one or more visible pages.</summary>
    VisiblePages,
}

/// <summary>The visibility policy of a membership dictionary without a visibility expression (<c>P</c>).</summary>
/// <remarks>ISO 32000-2 §8.11.2.2, Table 97. Default <see cref="AnyOn"/>.</remarks>
public enum PdfVisibilityPolicy
{
    /// <summary>Visible only if every group in <c>OCGs</c> is ON.</summary>
    AllOn,

    /// <summary>Visible if any group in <c>OCGs</c> is ON.</summary>
    AnyOn,

    /// <summary>Visible if any group in <c>OCGs</c> is OFF.</summary>
    AnyOff,

    /// <summary>Visible only if every group in <c>OCGs</c> is OFF.</summary>
    AllOff,
}

/// <summary>The kind of node in a visibility expression.</summary>
/// <remarks>ISO 32000-2 §8.11.2.2 (PDF 1.6): an array whose first element is <c>And</c>, <c>Or</c> or <c>Not</c>; operands are groups or nested expressions.</remarks>
public enum PdfVisibilityOperator
{
    /// <summary>A leaf: one optional content group, true when it is ON.</summary>
    Group,

    /// <summary>True when every operand is true.</summary>
    And,

    /// <summary>True when any operand is true.</summary>
    Or,

    /// <summary>True when its single operand is false.</summary>
    Not,
}

/// <summary>The situation a usage application dictionary applies to (<c>Event</c>).</summary>
/// <remarks>ISO 32000-2 §8.11.4.4, Table 101.</remarks>
public enum PdfOptionalContentEvent
{
    /// <summary>Interactive viewing.</summary>
    View,

    /// <summary>Printing.</summary>
    Print,

    /// <summary>Exporting to a format without optional content.</summary>
    Export,
}
