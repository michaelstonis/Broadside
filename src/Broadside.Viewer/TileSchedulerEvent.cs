using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>Why a <see cref="TileScheduler"/> wants a tile, in the order it renders them.</summary>
/// <remarks>Spec #33 viewer capability 3: visible first, then near the viewport, then ahead in the scroll direction.</remarks>
public enum TilePriority
{
    /// <summary>The viewport shows it: a visible page's placeholder, then its sharp tiles.</summary>
    Visible,

    /// <summary>It lies within <see cref="TileSchedulerOptions.NearMargin"/> of the viewport.</summary>
    Near,

    /// <summary>It lies ahead in the scroll direction, within <see cref="TileSchedulerOptions.RenderAhead"/> viewports.</summary>
    Ahead,
}

/// <summary>What happened to a tile in a <see cref="TileScheduler"/>'s trace.</summary>
/// <remarks>Spec #33 viewer capabilities 1, 3 and 13.</remarks>
public enum TileEventKind
{
    /// <summary>Its render started; its pixels now count against the budget.</summary>
    Started,

    /// <summary>It reached the surface.</summary>
    Presented,

    /// <summary>Its render stopped because the viewport moved away (or the scheduler closed); it never reaches the surface.</summary>
    Cancelled,

    /// <summary>
    /// The surface dropped it: to stay within the memory budget, because its layer was hidden (<see cref="TileScheduler.SetLayers"/>) or
    /// because an edit changed what it shows (<see cref="TileScheduler.Invalidate(int, PdfRectangle, DisplayLayers)"/>).
    /// </summary>
    Evicted,

    /// <summary>Its render failed; the scheduler does not retry it while it stays wanted.</summary>
    Failed,
}

/// <summary>One entry of a <see cref="TileScheduler"/>'s trace (<see cref="TileSchedulerOptions.Observer"/>).</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Tile">The tile.</param>
/// <param name="Priority">Why the scheduler wanted it when this happened.</param>
/// <param name="IsPlaceholder">Whether it is a page's low-resolution placeholder.</param>
/// <param name="Bytes">The bytes accounted against the budget right after this happened.</param>
/// <remarks>Spec #33 viewer capabilities 1, 3 and 13.</remarks>
public readonly record struct TileSchedulerEvent(TileEventKind Kind, Tile Tile, TilePriority Priority, bool IsPlaceholder, long Bytes);

/// <summary>What a <see cref="TileScheduler"/> holds and has done.</summary>
/// <param name="Budget">The memory budget in bytes.</param>
/// <param name="Bytes">The bytes accounted now: tiles on the surface, tiles being drawn, and the document's caches.</param>
/// <param name="PeakBytes">The most bytes accounted at any point the scheduler observed.</param>
/// <param name="TileBytes">The bytes of the tiles on the surface.</param>
/// <param name="ResidentTiles">The tiles on the surface.</param>
/// <param name="PendingTiles">The wanted tiles not yet on the surface.</param>
/// <param name="TilesStarted">Renders started.</param>
/// <param name="TilesPresented">Tiles presented.</param>
/// <param name="TilesCancelled">Renders cancelled.</param>
/// <param name="TilesEvicted">Tiles evicted from the surface.</param>
/// <param name="LongestFrame">The longest <see cref="TileScheduler.RunFrame"/> call; it exceeds its budget by at most one display-list build.</param>
/// <remarks>Spec #33 viewer capability 13.</remarks>
public readonly record struct TileSchedulerStatistics(
    long Budget,
    long Bytes,
    long PeakBytes,
    long TileBytes,
    int ResidentTiles,
    int PendingTiles,
    long TilesStarted,
    long TilesPresented,
    long TilesCancelled,
    long TilesEvicted,
    TimeSpan LongestFrame);
