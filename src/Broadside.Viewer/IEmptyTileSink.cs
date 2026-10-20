namespace Broadside.Viewer;

/// <summary>A surface that keeps an overlay tile with nothing on it without pixels (the tile scheduler's own relay, #357).</summary>
internal interface IEmptyTileSink
{
    /// <summary>Takes a finished overlay tile whose display list draws nothing.</summary>
    /// <param name="tile">The tile.</param>
    void PresentEmpty(Tile tile);
}
