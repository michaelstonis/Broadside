namespace Broadside.Viewer.Uno;

/// <summary>The viewer's colors as 0xAARRGGBB, shared by the view and the tests that check composed frames.</summary>
internal static class ViewerColors
{
    /// <summary>Around the pages.</summary>
    public const uint Background = 0xFFE8E8EC;

    /// <summary>Around the pages under the dark-mode color filter.</summary>
    public const uint DarkBackground = 0xFF2B2B2B;

    /// <summary>The paper before a page's tiles arrive.</summary>
    public const uint Paper = 0xFFFFFFFF;

    /// <summary>The paper under the dark-mode color filter (white paper inverts to black).</summary>
    public const uint DarkPaper = 0xFF000000;

    public const uint Shadow = 0x28000000;

    public const uint Selection = 0x55337AFF;

    public const uint SearchMatch = 0x66FFD400;

    public const uint CurrentMatch = 0x99FF8C00;

    public const uint Chrome = 0xC0337AFF;

    public const uint Handle = 0xFF337AFF;
}
