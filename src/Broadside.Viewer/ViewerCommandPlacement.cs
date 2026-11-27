namespace Broadside.Viewer;

/// <summary>Where a shell shows a <see cref="ViewerCommand"/>.</summary>
/// <remarks>Not a spec feature.</remarks>
public enum ViewerCommandPlacement
{
    /// <summary>In the bar itself.</summary>
    Primary,

    /// <summary>In the bar's overflow ("more") menu.</summary>
    Overflow,

    /// <summary>Not shown (the command stays in <see cref="ViewerToolbar.Items"/>, so it can be shown again).</summary>
    Hidden,
}
