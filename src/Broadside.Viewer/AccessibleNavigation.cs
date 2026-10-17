namespace Broadside.Viewer;

/// <summary>What <see cref="AccessibilityBridge.MoveFocus"/> and <see cref="AccessibilityBridge.FindNext"/> look for.</summary>
/// <remarks>ISO 32000-2 §14.8.2.5 (reading order), Spec #33 viewer capability 8.</remarks>
public enum AccessibleNavigation
{
    /// <summary>The next node in reading order, whatever its role.</summary>
    Element,

    /// <summary>The next heading, optionally of one level.</summary>
    Heading,

    /// <summary>The next landmark: a section or a note.</summary>
    Landmark,

    /// <summary>The next table.</summary>
    Table,

    /// <summary>The next list.</summary>
    List,

    /// <summary>The next figure.</summary>
    Figure,

    /// <summary>The next link.</summary>
    Link,

    /// <summary>The next element of an <see cref="IAccessibleElementProvider"/>, in its focus order.</summary>
    Control,

    /// <summary>The first node of the next page (of the previous page, backwards).</summary>
    Page,
}
