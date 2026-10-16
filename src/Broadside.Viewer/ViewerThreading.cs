namespace Broadside.Viewer;

/// <summary>Where a <see cref="DocumentSession"/> does its work.</summary>
/// <remarks>Spec #33 story 54 (display lists built off the UI thread) and viewer capability 2 (time-sliced rendering).</remarks>
public enum ViewerThreading
{
    /// <summary><see cref="Cooperative"/> in the browser and WASI (one thread), otherwise <see cref="Background"/>.</summary>
    Automatic,

    /// <summary>Builds display lists and renders tiles on thread-pool workers; the caller's thread only waits asynchronously.</summary>
    Background,

    /// <summary>
    /// Works on the caller's thread and never blocks it: tiles render in slices of <see cref="ViewerOptions.SliceBudget"/> with a yield
    /// between slices, so a single-threaded host (Blazor WebAssembly) keeps drawing frames. A display-list build is not resumable and
    /// runs in one piece after a yield; it stops at its next cancellation check (every 1,024 operators) when cancelled.
    /// </summary>
    Cooperative,
}
