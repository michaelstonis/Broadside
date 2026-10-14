namespace Broadside.Rendering;

/// <summary>Transparency groups (ISO 32000-2 §11.4).</summary>
public sealed partial class SoftwareRasterizer
{
    /// <summary>The backend's group half.</summary>
    private sealed partial class Backend
    {
        public void DrawGroup(in DisplayGroup group) => group.Replay(this);
    }
}
