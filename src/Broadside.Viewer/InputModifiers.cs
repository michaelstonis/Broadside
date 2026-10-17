namespace Broadside.Viewer;

/// <summary>The modifier keys held during a wheel or key event of the <see cref="InputModel"/>.</summary>
/// <remarks>Spec #33 viewer capability 14. <see cref="Control"/> and <see cref="Meta"/> (Cmd on macOS) both turn the wheel into a zoom.</remarks>
[Flags]
public enum InputModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Shift: the wheel scrolls across instead of along, Space pages back.</summary>
    Shift = 1,

    /// <summary>Control.</summary>
    Control = 2,

    /// <summary>Alt (Option on macOS).</summary>
    Alt = 4,

    /// <summary>Meta: Cmd on macOS, the Windows key elsewhere.</summary>
    Meta = 8,
}
