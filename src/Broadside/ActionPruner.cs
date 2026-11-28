using Broadside.Objects;

namespace Broadside;

/// <summary>
/// Removes actions from an owner's action entry and from every <c>Next</c> sequence below it (ISO 32000-2 §12.6.2): the walk both
/// <see cref="Writing.PdfAConverter"/> and <see cref="Sanitizing.DocumentSanitizer"/> use. Each action is judged once; a cycle of
/// <c>Next</c> entries ends the walk where it closes.
/// </summary>
/// <remarks>
/// A removed action is unlinked from its owner, or from its predecessor's <c>Next</c>. With <c>splice</c>, the actions that followed
/// it take its place: removed from an owner, the first of them becomes the owner's action and the others follow it, after its own
/// <c>Next</c> sequence, so they run in the order they did (§12.6.2: a <c>Next</c> array runs in order, each item's own sequence
/// first). Without it, they go with it. A value that is not a dictionary where an action is expected is removed too, reported as
/// <see langword="null"/>. Untouched entries are not written to, so an unchanged action keeps its bytes.
/// </remarks>
internal sealed class ActionPruner
{
    private static readonly CosName NextKey = new("Next");

    private readonly PdfDocument _document;
    private readonly Func<CosDictionary, bool> _keep;
    private readonly Action<CosDictionary?> _removed;
    private readonly bool _splice;
    private readonly Dictionary<CosDictionary, bool> _decided = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a pruner.</summary>
    /// <param name="document">The document the actions are in.</param>
    /// <param name="keep">Whether an action dictionary stays; called once per action.</param>
    /// <param name="removed">Called for each removed action, with <see langword="null"/> for a value that is not a dictionary.</param>
    /// <param name="splice">Whether the actions after a removed one take its place.</param>
    public ActionPruner(PdfDocument document, Func<CosDictionary, bool> keep, Action<CosDictionary?> removed, bool splice)
    {
        _document = document;
        _keep = keep;
        _removed = removed;
        _splice = splice;
    }

    /// <summary>Prunes the action <paramref name="owner"/>'s <paramref name="key"/> holds; the entry is removed when nothing stays.</summary>
    /// <param name="owner">The dictionary holding the action (an annotation, an outline item, the catalog).</param>
    /// <param name="key">The entry: <c>A</c>, <c>OpenAction</c>, <c>PA</c>, or an additional-actions trigger.</param>
    /// <returns><see langword="true"/> when the entry changed.</returns>
    public bool Prune(CosDictionary owner, CosName key)
    {
        if (!owner.TryGetValue(key, out CosObject? value))
        {
            return false;
        }

        (List<CosObject> kept, bool changed) = Filter(value);
        if (!changed)
        {
            return false;
        }

        if (kept.Count == 0)
        {
            owner.Remove(key);
            return true;
        }

        owner[key] = kept[0];
        if (kept.Count > 1 && _document.Resolve(kept[0]) is CosDictionary first)
        {
            List<CosObject> next = first.TryGetValue(NextKey, out CosObject? own) ? Items(own) : [];
            next.AddRange(kept.Skip(1));
            first[NextKey] = new CosArray(next);
        }

        return true;
    }

    /// <summary>
    /// Prunes each trigger of <paramref name="owner"/>'s additional actions (<c>AA</c>, §12.6.3); an <c>AA</c> left empty, or one that
    /// is not a dictionary, is removed.
    /// </summary>
    /// <param name="owner">The catalog, a page, an annotation or a form field.</param>
    /// <param name="key">The <c>AA</c> name.</param>
    /// <returns><see langword="true"/> when anything changed.</returns>
    public bool PruneAdditionalActions(CosDictionary owner, CosName key)
    {
        if (!owner.TryGetValue(key, out CosObject? value))
        {
            return false;
        }

        if (_document.Resolve(value) is not CosDictionary triggers)
        {
            _removed(null);
            owner.Remove(key);
            return true;
        }

        bool changed = false;
        foreach (CosName trigger in triggers.Keys.ToArray())
        {
            changed |= Prune(triggers, trigger);
        }

        if (triggers.Count == 0)
        {
            owner.Remove(key);
            changed = true;
        }

        return changed;
    }

    private List<CosObject> Items(CosObject value) => _document.Resolve(value) switch
    {
        CosArray array => [.. array],
        CosNull => [],
        _ => [value],
    };

    /// <summary>The values that stay of <paramref name="value"/>, an action or an array of them, and whether any was removed.</summary>
    private (List<CosObject> Kept, bool Changed) Filter(CosObject value)
    {
        switch (_document.Resolve(value))
        {
            case CosDictionary action:
                return FilterAction(value, action);
            case CosArray array:
                var kept = new List<CosObject>(array.Count);
                bool changed = false;
                foreach (CosObject item in array)
                {
                    (List<CosObject> items, bool itemChanged) = Filter(item);
                    kept.AddRange(items);
                    changed |= itemChanged;
                }

                return (kept, changed);
            case CosNull:
                // An absent action (a null, or a reference to nothing) is no action: the entry goes, nothing is reported.
                return ([], true);
            default:
                _removed(null);
                return ([], true);
        }
    }

    private (List<CosObject> Kept, bool Changed) FilterAction(CosObject value, CosDictionary action)
    {
        if (_decided.TryGetValue(action, out bool kept))
        {
            // Judged already, on another path or earlier in a cycle: its Next sequence was pruned then.
            return kept ? ([value], false) : ([], true);
        }

        kept = _keep(action);
        _decided[action] = kept;
        if (kept)
        {
            if (action.TryGetValue(NextKey, out CosObject? next))
            {
                (List<CosObject> following, bool changed) = Filter(next);
                if (changed)
                {
                    switch (following.Count)
                    {
                        case 0:
                            action.Remove(NextKey);
                            break;
                        case 1 when _document.Resolve(following[0]) is CosDictionary:
                            action[NextKey] = following[0];
                            break;
                        default:
                            action[NextKey] = new CosArray(following);
                            break;
                    }
                }
            }

            return ([value], false);
        }

        _removed(action);
        if (_splice && action.TryGetValue(NextKey, out CosObject? successors))
        {
            return (Filter(successors).Kept, true);
        }

        return ([], true);
    }
}
