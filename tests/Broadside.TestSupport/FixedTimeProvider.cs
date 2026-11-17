namespace Broadside.TestSupport;

/// <summary>A clock that always reads the same instant, so documents saved at different times are byte-identical.</summary>
public sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    /// <summary>Creates a clock that reads <paramref name="now"/>.</summary>
    /// <param name="now">The instant the clock reads.</param>
    public FixedTimeProvider(DateTimeOffset now)
    {
        _now = now;
        LocalTimeZone = TimeZoneInfo.CreateCustomTimeZone("Fixed", now.Offset, "Fixed", "Fixed");
    }

    /// <summary>Gets a clock fixed at 1 January 2026, 00:00 UTC.</summary>
    public static FixedTimeProvider Instance { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    /// <inheritdoc/>
    public override TimeZoneInfo LocalTimeZone { get; }

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
}
