namespace Broadside.Rendering.Direct2D;

/// <summary>The Direct3D device a <see cref="Direct2DRenderer"/> draws with.</summary>
public enum Direct2DDriver
{
    /// <summary>The graphics hardware; when the machine has no Direct3D 11 hardware device, the renderer falls back to <see cref="Warp"/>.</summary>
    Hardware,

    /// <summary>
    /// WARP, Windows' software rasterizer for Direct3D: available on every Windows machine, including servers and build agents without
    /// a GPU, and the same everywhere, which is why the backend's tests use it.
    /// </summary>
    Warp,
}
