namespace Broadside.Viewer;

/// <summary>How many steps an <see cref="EditHistory"/> keeps, how close in time edits of one field merge, and its clock.</summary>
/// <remarks>
/// Not a spec feature. Mutable and fluent like the other options objects: <c>Use*</c> selects an implementation, <c>With*</c> sets a
/// value. A history copies the values when it is created.
/// </remarks>
public sealed class EditHistoryOptions
{
    /// <summary>Gets the most steps the history keeps (100 by default); recording one more drops the oldest.</summary>
    public int Limit { get; private set; } = 100;

    /// <summary>
    /// Gets how close in time two value edits of the same form field must be to merge into one step (one second by default; zero never
    /// merges): a shell that commits a field on each keystroke still undoes the typing in one step.
    /// </summary>
    public TimeSpan MergeWindow { get; private set; } = TimeSpan.FromSeconds(1);

    /// <summary>Gets the clock the merge window is measured with (<see cref="TimeProvider.System"/> by default).</summary>
    public TimeProvider TimeProvider { get; private set; } = TimeProvider.System;

    /// <summary>Sets <see cref="Limit"/>.</summary>
    /// <param name="steps">The most steps kept, at least 1.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="steps"/> is less than 1.</exception>
    public EditHistoryOptions WithLimit(int steps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(steps, 1);
        Limit = steps;
        return this;
    }

    /// <summary>Sets <see cref="MergeWindow"/>.</summary>
    /// <param name="window">The window; <see cref="TimeSpan.Zero"/> never merges.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="window"/> is negative.</exception>
    public EditHistoryOptions WithMergeWindow(TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero);
        MergeWindow = window;
        return this;
    }

    /// <summary>Selects the clock the merge window is measured with (a manual one in tests).</summary>
    /// <param name="timeProvider">The clock.</param>
    /// <returns>These options.</returns>
    public EditHistoryOptions UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProvider = timeProvider;
        return this;
    }
}
