namespace Broadside.Viewer;

/// <summary>A snapshot of what a <see cref="DocumentSession"/> is doing and holding.</summary>
/// <param name="PendingOperations">Page loads, display-list builds and tile renders started and not finished.</param>
/// <param name="PixelBytes">Bytes of pooled tile pixels the session holds right now (0 once every render has finished).</param>
/// <param name="TilesPresented">Tiles handed to a surface since the session opened.</param>
/// <param name="TilesCancelled">Tile renders cancelled (by their token or by closing the session) since the session opened.</param>
/// <remarks>Spec #33 story 54; the tile scheduler's memory budget (viewer capability 13) reads <paramref name="PixelBytes"/>.</remarks>
public readonly record struct SessionStatistics(int PendingOperations, long PixelBytes, long TilesPresented, long TilesCancelled);
