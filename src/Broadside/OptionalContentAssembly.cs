using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <summary>
/// Carries the optional content configuration of appended pages into the target's <c>OCProperties</c> (issue #307), as part of a
/// <see cref="DocumentAssembly"/>: the source groups the copy session reached (every listed group when every page is copied) are
/// listed in the target with their default states, presentation order, radio-button collections, locks, auto-states and alternate
/// configurations, restricted to those groups.
/// </summary>
/// <remarks>
/// ISO 32000-2 §8.11.4 (Tables 98, 99 and 101) and §7.7.2, Table 29. Without this the copied content names groups the target's
/// <c>OCProperties</c> does not list, so they stop being optional content and a layer hidden by default shows (§8.11.4.2).
/// <list type="bullet">
/// <item><description><c>OCGs</c>: the copies after the target's groups.</description></item>
/// <item><description>
/// <c>D</c>: each copy's default state as the source's <c>D</c> computes it, written in the target's <c>OFF</c> (or <c>ON</c> when
/// the target's <c>BaseState</c> is not <c>ON</c>); <c>Order</c>, <c>RBGroups</c>, <c>Locked</c> and <c>AS</c> appended after the
/// target's, keeping only the carried groups (a group left out of <c>Order</c> gives its sublayers its place; empty collections are
/// dropped). A target without <c>OCProperties</c> also takes the source's <c>Name</c>, <c>Creator</c> and <c>ListMode</c>.
/// </description></item>
/// <item><description>
/// <c>Configs</c>: each alternate configuration of the source appended, restricted the same way. Its <c>BaseState</c> applies to the
/// target's groups as well when the configuration is chosen.
/// </description></item>
/// </list>
/// The target's own entries are never changed. Groups the source does not list in <c>OCGs</c> are not optional content in the source
/// (§8.11.4.2) and are not listed in the target either.
/// </remarks>
internal sealed class OptionalContentAssembly
{
    private readonly PdfDocument _source;
    private readonly PdfDocument _target;
    private readonly PdfObjectCopier _copier;
    private readonly bool _whole;
    private readonly Dictionary<PdfOptionalContentGroup, CosReference> _carried = new(ReferenceEqualityComparer.Instance);

    public OptionalContentAssembly(PdfDocument source, PdfDocument target, PdfObjectCopier copier, bool whole)
    {
        _source = source;
        _target = target;
        _copier = copier;
        _whole = whole;
    }

    /// <summary>Lists the carried groups and their configuration in the target; call once everything else is copied.</summary>
    public void Run()
    {
        if (_source.OptionalContent is not { } source)
        {
            return;
        }

        var groups = new List<(PdfOptionalContentGroup Group, CosReference Copy)>();
        foreach (PdfOptionalContentGroup group in source.Groups)
        {
            if (group.Reference is not { } reference)
            {
                continue;
            }

            CosReference? copy = _whole ? _copier.Copy(reference) as CosReference : _copier.TryGetCopy(reference, out CosReference? copied) ? copied : null;
            if (copy is not null && _carried.TryAdd(group, copy))
            {
                groups.Add((group, copy));
            }
        }

        if (groups.Count == 0)
        {
            return;
        }

        _target.RequireFeature(VersionedFeatures.OptionalContent);
        bool created = _target.Resolve(_target.Catalog.GetValueOrDefault(FileAndLayerNames.OCProperties)) is not CosDictionary;
        CosDictionary properties = created ? NewProperties() : (CosDictionary)_target.Resolve(_target.Catalog[FileAndLayerNames.OCProperties]);
        CosArray ocgs = Array(properties, FileAndLayerNames.OCGs);
        if (_target.Resolve(properties.GetValueOrDefault(FileAndLayerNames.D)) is not CosDictionary defaults)
        {
            defaults = new CosDictionary();
            properties[FileAndLayerNames.D] = defaults;
        }

        // Table 99: the default configuration's BaseState shall be ON; a damaged target's other base state needs the ON groups listed.
        bool baseOn = _target.Resolve(defaults.GetValueOrDefault(FileAndLayerNames.BaseState)) is not CosName baseState || baseState.Equals(FileAndLayerNames.ON);
        PdfOptionalContentConfiguration sourceDefaults = source.DefaultConfiguration;
        PdfOptionalContentState states = source.GetDefaultStates();
        foreach ((PdfOptionalContentGroup group, CosReference copy) in groups)
        {
            ocgs.Add(copy);
            if (!states.IsOn(group))
            {
                Array(defaults, FileAndLayerNames.OFF).Add(copy);
            }
            else if (!baseOn)
            {
                Array(defaults, FileAndLayerNames.ON).Add(copy);
            }
        }

        if (created)
        {
            CopyEntries(sourceDefaults.Dictionary, defaults, FileAndLayerNames.Name, FileAndLayerNames.Creator, FileAndLayerNames.ListMode);
        }

        AppendCollections(sourceDefaults, defaults, ownOnly: false);
        foreach (PdfOptionalContentConfiguration configuration in source.Configurations)
        {
            var copy = new CosDictionary();
            CopyEntries(
                configuration.Dictionary,
                copy,
                FileAndLayerNames.Name,
                FileAndLayerNames.Creator,
                FileAndLayerNames.BaseState,
                FileAndLayerNames.Intent,
                FileAndLayerNames.ListMode);
            foreach ((CosName key, IReadOnlyList<PdfOptionalContentGroup> list) in (ReadOnlySpan<(CosName, IReadOnlyList<PdfOptionalContentGroup>)>)
                [(FileAndLayerNames.ON, configuration.On), (FileAndLayerNames.OFF, configuration.Off)])
            {
                if (configuration.Dictionary.ContainsKey(key))
                {
                    copy[key] = Carried(list);
                }
            }

            AppendCollections(configuration, copy, ownOnly: true);
            Array(properties, FileAndLayerNames.Configs).Add(copy);
        }

        if (created)
        {
            _target.Catalog[FileAndLayerNames.OCProperties] = properties;
        }
    }

    private static CosDictionary NewProperties() => new() { [FileAndLayerNames.OCGs] = new CosArray(), [FileAndLayerNames.D] = new CosDictionary() };

    /// <summary>
    /// Appends a configuration's <c>Order</c>, <c>RBGroups</c>, <c>Locked</c> and <c>AS</c>, restricted to the carried groups, to
    /// <paramref name="target"/>'s; with <paramref name="ownOnly"/>, only those the configuration has itself (an alternate
    /// configuration without <c>Order</c> or <c>RBGroups</c> takes the default configuration's, Table 99).
    /// </summary>
    private void AppendCollections(PdfOptionalContentConfiguration configuration, CosDictionary target, bool ownOnly)
    {
        CosDictionary source = configuration.Dictionary;
        if (!ownOnly || source.ContainsKey(FileAndLayerNames.Order))
        {
            var order = new CosArray();
            AppendOrder(configuration.Order, order);
            if (order.Count > 0 || ownOnly)
            {
                // Only the first element meets the target's entries; the rest are already written as they read.
                CosArray targetOrder = Array(target, FileAndLayerNames.Order);
                for (int index = 0; index < order.Count; index++)
                {
                    if (index == 0)
                    {
                        AppendOrderEntry(targetOrder, order[index]);
                    }
                    else
                    {
                        targetOrder.Add(order[index]);
                    }
                }
            }
        }

        if (!ownOnly || source.ContainsKey(FileAndLayerNames.RBGroups))
        {
            foreach (IReadOnlyList<PdfOptionalContentGroup> collection in configuration.RadioButtonGroups)
            {
                CosArray carried = Carried(collection);
                if (carried.Count > 0)
                {
                    Array(target, FileAndLayerNames.RBGroups).Add(carried);
                }
            }

            if (ownOnly)
            {
                _ = Array(target, FileAndLayerNames.RBGroups);
            }
        }

        CosArray locked = Carried(configuration.Locked);
        if (locked.Count > 0 || (ownOnly && source.ContainsKey(FileAndLayerNames.Locked)))
        {
            CosArray targetLocked = Array(target, FileAndLayerNames.Locked);
            foreach (CosObject group in locked)
            {
                targetLocked.Add(group);
            }
        }

        // Table 101: a usage application dictionary keeps its event and categories, with the groups it names that are carried.
        foreach (PdfOptionalContentUsageApplication application in configuration.AutoStates)
        {
            CosArray carried = Carried(application.Groups);
            if (carried.Count == 0)
            {
                continue;
            }

            var copy = new CosDictionary();
            foreach ((CosName key, CosObject value) in application.Dictionary)
            {
                copy[key] = key.Equals(FileAndLayerNames.OCGs) ? carried : _copier.Copy(value);
            }

            Array(target, FileAndLayerNames.AS).Add(copy);
        }
    }

    /// <summary>
    /// Writes an <c>Order</c> tree (Table 99) with only the carried groups: a group left out gives its place to its sublayers, and a
    /// labelled or unlabelled collection left empty is dropped.
    /// </summary>
    private void AppendOrder(IReadOnlyList<PdfOptionalContentOrderItem> items, CosArray output)
    {
        foreach (PdfOptionalContentOrderItem item in items)
        {
            if (item.Group is { } group)
            {
                if (!_carried.TryGetValue(group, out CosReference? copy))
                {
                    AppendOrder(item.Children, output);
                    continue;
                }

                output.Add(copy);
                var sublayers = new CosArray();
                AppendOrder(item.Children, sublayers);
                if (sublayers.Count > 0)
                {
                    output.Add(sublayers);
                }

                continue;
            }

            var nested = new CosArray();
            if (item.Label is { } label)
            {
                nested.Add(new CosString(TextStringEncoder.Encode(label)));
            }

            int start = nested.Count;
            AppendOrder(item.Children, nested);
            if (nested.Count > start)
            {
                AppendOrderEntry(output, nested);
            }
        }
    }

    /// <summary>
    /// Adds an <c>Order</c> element; an unlabelled array that would follow a group without sublayers is added element by element, as
    /// it would otherwise read as that group's sublayers (Table 99).
    /// </summary>
    private void AppendOrderEntry(CosArray output, CosObject entry)
    {
        if (entry is CosArray nested
            && (nested.Count == 0 || _target.Resolve(nested[0]) is not CosString)
            && output.Count > 0
            && _target.Resolve(output[output.Count - 1]) is CosDictionary)
        {
            foreach (CosObject element in nested)
            {
                output.Add(element);
            }

            return;
        }

        output.Add(entry);
    }

    /// <summary>The copies of the carried groups among <paramref name="groups"/>, in order, each once.</summary>
    private CosArray Carried(IReadOnlyList<PdfOptionalContentGroup> groups)
    {
        var array = new CosArray();
        var seen = new HashSet<CosReference>();
        foreach (PdfOptionalContentGroup group in groups)
        {
            if (_carried.TryGetValue(group, out CosReference? copy) && seen.Add(copy))
            {
                array.Add(copy);
            }
        }

        return array;
    }

    private void CopyEntries(CosDictionary source, CosDictionary target, params ReadOnlySpan<CosName> keys)
    {
        foreach (CosName key in keys)
        {
            if (source.TryGetValue(key, out CosObject? value))
            {
                target[key] = _copier.Copy(value);
            }
        }
    }

    /// <summary>The array <paramref name="owner"/>'s <paramref name="key"/> holds in the target, created (direct) when absent.</summary>
    private CosArray Array(CosDictionary owner, CosName key)
    {
        if (_target.Resolve(owner.GetValueOrDefault(key)) is CosArray array)
        {
            return array;
        }

        array = new CosArray();
        owner[key] = array;
        return array;
    }
}
