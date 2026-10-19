namespace Broadside.Viewer;

/// <summary>
/// Renders tiles of a session's document outside the thread that runs the viewer, where the host has no thread pool for it: in the
/// browser, a second .NET runtime in a Web Worker with its own copy of the document. The <see cref="TileScheduler"/> hands it the
/// tiles it accepts (<see cref="CanRender"/>) and keeps building and drawing the others itself, so the UI thread only plans frames,
/// presents finished tiles and draws what the remote copy cannot know about (edits, which stay with the session's own document).
/// </summary>
/// <remarks>
/// Spec #33 viewer capability 1 (#357), ADR 0013. Selected with <see cref="ViewerOptions.UseRemoteRenderer"/>; the session disposes it
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
    /// <returns>A task that completes once the tile is presented, or fails with <see cref="OperationCanceledException"/>.</returns>
    Task RenderAsync(Tile tile, IRenderSurface surface, CancellationToken cancellationToken);
}
