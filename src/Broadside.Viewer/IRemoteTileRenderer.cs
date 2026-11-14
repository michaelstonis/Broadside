using Broadside.Rendering;

namespace Broadside.Viewer;

/// <summary>
/// Renders tiles of a session's document outside the thread that runs the viewer, where the host has no thread pool for it: in the
/// browser, a second .NET runtime in a Web Worker with its own copy of the document. The <see cref="TileScheduler"/> hands it the
/// tiles it accepts (<see cref="CanRender(in Tile)"/>) and keeps building and drawing the others itself, so the UI thread only plans frames,
/// presents finished tiles and draws what the remote copy cannot know about (edits, which stay with the session's own document).
/// </summary>
/// <remarks>
/// Spec #33 viewer capability 1 (#357). Selected with <see cref="ViewerOptions.UseRemoteRenderer"/>; the session disposes it
/// when it closes. A tile it renders never reaches the surface after its token is cancelled.
/// </remarks>
public interface IRemoteTileRenderer : IDisposable
{
    /// <summary>Gets how many tiles it renders at once; the scheduler keeps at most this many waiting on it.</summary>
    int MaxConcurrency { get; }

    /// <summary>Returns whether it renders <paramref name="tile"/> (for example only content layers, which edits never change).</summary>
    /// <param name="tile">The tile.</param>
    /// <returns><see langword="true"/> when the tile is rendered remotely.</returns>
    bool CanRender(in Tile tile);

    /// <summary>Renders a tile and presents it to <paramref name="surface"/>, unless <paramref name="cancellationToken"/> is cancelled first.</summary>
    /// <param name="tile">The tile, as <see cref="DocumentSession.RenderTileAsync"/> takes it.</param>
    /// <param name="surface">Where the finished tile goes, on the caller's context.</param>
    /// <param name="cancellationToken">Stops the render; the tile is then never presented.</param>
    /// <returns>
    /// A task that completes once the tile is presented, or fails with <see cref="OperationCanceledException"/>. Completing without
    /// presenting gives the tile back: the scheduler starts it again, and draws it itself when <see cref="CanRender(in Tile)"/> then declines it
    /// (a renderer that stopped working).
    /// </returns>
    Task RenderAsync(Tile tile, IRenderSurface surface, CancellationToken cancellationToken);

    /// <summary>Returns whether it renders <paramref name="tile"/> with <paramref name="colorFilter"/> run over it.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="colorFilter">The session's color filter (<see cref="DocumentSession.ColorFilter"/>).</param>
    /// <returns><see langword="true"/> when the tile is rendered remotely.</returns>
    /// <remarks>
    /// Spec #33 viewer capability 9. The default renders only unfiltered tiles (<see cref="CanRender(in Tile)"/>), so the session draws
    /// the filtered ones itself.
    /// </remarks>
    bool CanRender(in Tile tile, ColorFilter colorFilter) => colorFilter == ColorFilter.None && CanRender(tile);

    /// <summary>
    /// Renders a tile with <paramref name="colorFilter"/> run over it and presents it to <paramref name="surface"/>, unless
    /// <paramref name="cancellationToken"/> is cancelled first.
    /// </summary>
    /// <param name="tile">The tile, as <see cref="DocumentSession.RenderTileAsync"/> takes it.</param>
    /// <param name="colorFilter">The color filter, such as <see cref="ColorFilter.DarkMode"/>.</param>
    /// <param name="surface">Where the finished tile goes, on the caller's context.</param>
    /// <param name="cancellationToken">Stops the render; the tile is then never presented.</param>
    /// <returns>As <see cref="RenderAsync(Tile, IRenderSurface, CancellationToken)"/>.</returns>
    /// <remarks>
    /// Spec #33 viewer capability 9. The default renders an unfiltered tile through
    /// <see cref="RenderAsync(Tile, IRenderSurface, CancellationToken)"/> and gives a filtered one back without presenting it, for the
    /// scheduler to draw.
    /// </remarks>
    Task RenderAsync(Tile tile, ColorFilter colorFilter, IRenderSurface surface, CancellationToken cancellationToken) =>
        colorFilter == ColorFilter.None ? RenderAsync(tile, surface, cancellationToken) : Task.CompletedTask;
}
