using Broadside.Objects;

namespace Broadside;

/// <summary>
/// The ON/OFF states of every optional content group, plus the intents that decide which groups count: an immutable snapshot.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.11.2.1: states are not part of the file; a processor holds them. They start from a configuration
/// (<see cref="PdfOptionalContentProperties.GetDefaultStates"/>, §8.11.4.5) and change through a user interface or set-OCG-state
/// actions (§12.6.4.13, <see cref="Apply"/>). Every change returns a new snapshot, so concurrent renders with different layer
/// settings over one document are safe.
/// </para>
/// <para>
/// §8.11.2.3: a group counts only when one of its intents is among <see cref="Intents"/> (<c>All</c> matches every intent); a group
/// that does not count has no effect on visibility, and an empty intent list makes all content visible.
/// </para>
/// </remarks>
public sealed class PdfOptionalContentState
{
    private readonly bool[] _on;
    private readonly bool[] _effective;
    private readonly CosName[] _intents;

    internal PdfOptionalContentState(PdfOptionalContentProperties properties, PdfOptionalContentConfiguration configuration, bool[] on, CosName[] intents)
    {
        Properties = properties;
        Configuration = configuration;
        _on = on;
        _intents = intents;
        _effective = new bool[on.Length];
        bool all = Array.Exists(intents, intent => intent.Equals(FileAndLayerNames.All));
        for (int ordinal = 0; ordinal < on.Length; ordinal++)
        {
            CosName[] groupIntents = properties.GroupIntents[ordinal];
            _effective[ordinal] = all || Array.Exists(groupIntents, intent => intent.Equals(FileAndLayerNames.All) ? intents.Length > 0 : Array.IndexOf(intents, intent) >= 0);
        }
    }

    /// <summary>Gets the configuration the snapshot was computed from; its radio-button groups and auto-state entries apply to it.</summary>
    /// <remarks>ISO 32000-2 §8.11.4.3.</remarks>
    public PdfOptionalContentConfiguration Configuration { get; }

    /// <summary>Gets the intents whose groups count when computing visibility, from the configuration's <c>Intent</c> unless replaced.</summary>
    /// <remarks>ISO 32000-2 §8.11.2.3 and §8.11.4.3, Table 99.</remarks>
    public IReadOnlyList<CosName> Intents => _intents;

    /// <summary>Gets the properties the groups belong to.</summary>
    internal PdfOptionalContentProperties Properties { get; }

    /// <summary>Gets the states by group ordinal.</summary>
    internal ReadOnlySpan<bool> On => _on;

    /// <summary>Gets, by group ordinal, whether the group counts under <see cref="Intents"/>.</summary>
    internal ReadOnlySpan<bool> Effective => _effective;

    /// <summary>Returns whether <paramref name="group"/> is ON.</summary>
    /// <param name="group">A group of the same optional content properties.</param>
    /// <returns><see langword="true"/> when ON.</returns>
    /// <exception cref="ArgumentException">The group belongs to another document or properties view.</exception>
    /// <remarks>ISO 32000-2 §8.11.2.1.</remarks>
    public bool IsOn(PdfOptionalContentGroup group) => _on[Check(group)];

    /// <summary>Returns a snapshot with <paramref name="group"/> set to <paramref name="on"/>.</summary>
    /// <param name="group">A group of the same optional content properties.</param>
    /// <param name="on">The new state.</param>
    /// <returns>The new snapshot; this one is unchanged.</returns>
    /// <exception cref="ArgumentException">The group belongs to another document or properties view.</exception>
    /// <remarks>ISO 32000-2 §8.11.2.1. Radio-button groups are not applied; use <see cref="Apply"/> for that.</remarks>
    public PdfOptionalContentState With(PdfOptionalContentGroup group, bool on)
    {
        int ordinal = Check(group);
        bool[] states = (bool[])_on.Clone();
        states[ordinal] = on;
        return new PdfOptionalContentState(Properties, Configuration, states, _intents);
    }

    /// <summary>Returns a snapshot that uses <paramref name="intents"/> to decide which groups count.</summary>
    /// <param name="intents">The intents; <c>All</c> for every intent; empty to make all content visible.</param>
    /// <returns>The new snapshot.</returns>
    /// <remarks>ISO 32000-2 §8.11.2.3.</remarks>
    public PdfOptionalContentState WithIntents(IEnumerable<CosName> intents)
    {
        ArgumentNullException.ThrowIfNull(intents);
        return new PdfOptionalContentState(Properties, Configuration, _on, [.. intents]);
    }

    /// <summary>
    /// Applies a set-OCG-state array (<c>[/ON g1 /OFF g2 /Toggle g3 ...]</c>): each name sets how the groups after it change.
    /// </summary>
    /// <param name="state">The <c>State</c> array of a set-OCG-state action.</param>
    /// <param name="preserveRadioButtons">
    /// The action's <c>PreserveRB</c> (default <see langword="true"/>): turning a group ON turns OFF the other groups of every
    /// radio-button collection of <see cref="Configuration"/> that holds it.
    /// </param>
    /// <returns>The new snapshot. Entries that are neither a state name nor a listed group are ignored.</returns>
    /// <remarks>ISO 32000-2 §12.6.4.13, Table 216. The action itself is typed elsewhere; this applies its data.</remarks>
    public PdfOptionalContentState Apply(CosArray state, bool preserveRadioButtons = true)
    {
        ArgumentNullException.ThrowIfNull(state);
        bool[] states = (bool[])_on.Clone();
        IReadOnlyList<IReadOnlyList<PdfOptionalContentGroup>> radio = preserveRadioButtons ? Configuration.RadioButtonGroups : [];
        string mode = "ON";
        foreach (CosObject item in state)
        {
            switch (Properties.Document.Resolve(item))
            {
                case CosName name when name.Value is "ON" or "OFF" or "Toggle":
                    mode = name.Value;
                    break;
                case CosDictionary dictionary when Properties.FindGroup(dictionary) is { } group:
                    bool value = mode switch
                    {
                        "ON" => true,
                        "OFF" => false,
                        _ => !states[group.Ordinal],
                    };
                    states[group.Ordinal] = value;
                    if (value)
                    {
                        foreach (IReadOnlyList<PdfOptionalContentGroup> collection in radio)
                        {
                            if (collection.Contains(group))
                            {
                                foreach (PdfOptionalContentGroup other in collection)
                                {
                                    states[other.Ordinal] = other == group;
                                }
                            }
                        }
                    }

                    break;
            }
        }

        return new PdfOptionalContentState(Properties, Configuration, states, _intents);
    }

    /// <summary>Returns a copy of the state array, for the properties view's state computations.</summary>
    internal bool[] CopyStates() => (bool[])_on.Clone();

    private int Check(PdfOptionalContentGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (!ReferenceEquals(Properties.FindGroup(group.Dictionary), group))
        {
            throw new ArgumentException("The group does not belong to the optional content properties this state was computed for.", nameof(group));
        }

        return group.Ordinal;
    }
}
