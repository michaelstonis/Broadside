namespace Broadside;

/// <summary>The external factors auto-state entries consult: magnification, language and user.</summary>
/// <remarks>ISO 32000-2 §8.11.4.4. A factor left <see langword="null"/> makes its category recommend nothing.</remarks>
public sealed class PdfOptionalContentUsageContext
{
    /// <summary>Gets the current magnification factor (1.0 is 100 %), for the <c>Zoom</c> category.</summary>
    public double? Zoom { get; init; }

    /// <summary>Gets the application's language and locale (such as <c>en-US</c>), for the <c>Language</c> category.</summary>
    /// <remarks>ISO 32000-2 §14.9.2.</remarks>
    public string? Language { get; init; }

    /// <summary>Gets the user's names, titles and organisations, for the <c>User</c> category (exact match).</summary>
    public IReadOnlyCollection<string>? UserNames { get; init; }
}
