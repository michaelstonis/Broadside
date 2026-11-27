using Broadside.Objects;

namespace Broadside.Writing;

/// <summary>
/// Finds the objects a full save writes that are identical to another one it writes, so that every reference to them is written as
/// a reference to the other, and they are not written (<see cref="PdfSaveOptions.MergeDuplicateObjects"/>, issue #634).
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.3.10: an indirect object may be referred to from any number of places, so two identical objects can share one
/// definition without changing what any page shows. §7.5.3: the file body holds each written object once.
/// </para>
/// <para>
/// The candidates are the indirect streams (images, form XObjects, font programs, ICC profiles, embedded files, content streams), the
/// indirect arrays (color spaces, widths) and the indirect dictionaries whose identity does not matter: fonts, font descriptors,
/// encodings, graphics state parameter dictionaries, halftones, shading patterns, shadings and functions. Pages, page tree nodes, the
/// catalog, annotations, fields, outline items, structure elements, optional content groups and every other dictionary are never
/// merged: something refers back to them (<c>P</c>, <c>Parent</c>) or tells them apart by identity.
/// </para>
/// <para>
/// Objects are grouped by a hash of their content (a stream's dictionary without <c>Length</c> and its encoded data, read once), then
/// confirmed equal under the rules of <see cref="CosObject.DeepEquals"/>, with each reference compared through the merges already
/// found. The object with the lowest number in a group is kept. Merging repeats until nothing changes, at most
/// <see cref="MaxRounds"/> times: two font dictionaries become equal only once their font programs merge. Streams are compared as
/// encoded, so two streams merge only when they hold the same filters, the same <c>DecodeParms</c> and the same bytes; data read
/// from an encrypted file is compared decrypted, since the save encrypts every object again under its written number.
/// </para>
/// </remarks>
internal static class DuplicateMerge
{
    /// <summary>The most rounds of merging; each round can only make objects that refer to merged objects equal.</summary>
    public const int MaxRounds = 8;

    private static readonly CosName Font = new("Font");
    private static readonly CosName FontDescriptor = new("FontDescriptor");
    private static readonly CosName Encoding = new("Encoding");
    private static readonly CosName ExtGState = new("ExtGState");
    private static readonly CosName Halftone = new("Halftone");
    private static readonly CosName Pattern = new("Pattern");
    private static readonly CosName ShadingType = new("ShadingType");
    private static readonly CosName FunctionType = new("FunctionType");
    private static readonly CosName HalftoneType = new("HalftoneType");

    /// <summary>
    /// Finds the duplicates among <paramref name="kept"/>, which is in ascending object number, and removes them from it.
    /// </summary>
    /// <param name="kept">The objects the save writes, in ascending object number; the duplicates are removed.</param>
    /// <param name="excluded">Whether an object must keep its own definition: a stream whose data could not be decrypted.</param>
    /// <returns>What was merged: each removed object's reference mapped to the reference of the object kept in its place.</returns>
    public static DuplicateMergeResult Apply(List<(CosReference Reference, CosObject Value)> kept, Func<CosObject, bool> excluded)
    {
        var candidates = new List<Candidate>();
        foreach ((CosReference reference, CosObject value) in kept)
        {
            if (IsCandidate(value) && !excluded(value))
            {
                candidates.Add(new Candidate(reference, value, value is CosStream stream ? DataHash(stream) : 0));
            }
        }

        var aliases = new Dictionary<CosReference, CosReference>();
        long bytes = 0;
        int rounds = 0;
        while (rounds < MaxRounds)
        {
            int before = aliases.Count;
            var groups = new Dictionary<int, List<Candidate>>();
            foreach (Candidate candidate in candidates)
            {
                if (aliases.ContainsKey(candidate.Reference))
                {
                    continue;
                }

                int hash = HashCode.Combine(candidate.DataHash, Hash(candidate.Value, aliases));
                if (!groups.TryGetValue(hash, out List<Candidate>? group))
                {
                    groups[hash] = [candidate];
                    continue;
                }

                int match = group.FindIndex(other => Equal(other.Value, candidate.Value, aliases));
                if (match >= 0)
                {
                    aliases[candidate.Reference] = group[match].Reference;
                    bytes += candidate.Value is CosStream stream ? stream.EncodedLength : 0;
                }
                else
                {
                    group.Add(candidate);
                }
            }

            if (aliases.Count == before)
            {
                break;
            }

            rounds++;
        }

        if (aliases.Count == 0)
        {
            return new DuplicateMergeResult(aliases, 0, rounds);
        }

        // A keeper of an early round may merge into a lower-numbered object later: every alias names the object finally kept.
        foreach (CosReference reference in aliases.Keys.ToList())
        {
            aliases[reference] = Canonical(reference, aliases);
        }

        kept.RemoveAll(item => aliases.ContainsKey(item.Reference));
        return new DuplicateMergeResult(aliases, bytes, rounds);
    }

    /// <summary>The object kept for <paramref name="reference"/>: itself, or the object its merges lead to.</summary>
    private static CosReference Canonical(CosReference reference, Dictionary<CosReference, CosReference> aliases)
    {
        while (aliases.TryGetValue(reference, out CosReference? target))
        {
            reference = target;
        }

        return reference;
    }

    /// <summary>Whether an indirect object may be merged with another: see the type's remarks.</summary>
    private static bool IsCandidate(CosObject value) => value switch
    {
        CosStream => true,
        CosArray => true,
        CosDictionary dictionary => IsMergeableDictionary(dictionary),
        _ => false,
    };

    /// <summary>
    /// Whether a dictionary has no identity: a font (§9.6, §9.7), font descriptor (§9.8), encoding (§9.6.5), graphics state parameter
    /// dictionary (§8.4.5), halftone (§10.6), shading pattern (§8.7.3), shading (§8.7.4.3) or function (§7.10).
    /// </summary>
    private static bool IsMergeableDictionary(CosDictionary dictionary)
    {
        if (dictionary.TryGetValue(KnownNames.Type, out CosObject? type) && type is CosName name)
        {
            return name.Equals(Font) || name.Equals(FontDescriptor) || name.Equals(Encoding) || name.Equals(ExtGState)
                || name.Equals(Halftone) || name.Equals(Pattern);
        }

        return !dictionary.ContainsKey(KnownNames.Type)
            && (dictionary.ContainsKey(ShadingType) || dictionary.ContainsKey(FunctionType) || dictionary.ContainsKey(HalftoneType));
    }

    /// <summary>A hash of a stream's encoded data, read once.</summary>
    private static int DataHash(CosStream stream)
    {
        var hash = default(HashCode);
        hash.AddBytes(stream.EncodedData.Span);
        return hash.ToHashCode();
    }

    /// <summary>
    /// A hash of an object's structure that equal objects (<see cref="Equal"/>) share: dictionary entries in any order, a stream's
    /// <c>Length</c> ignored and its data left to <see cref="DataHash"/>, references through the merges found so far.
    /// </summary>
    private static int Hash(CosObject value, Dictionary<CosReference, CosReference> aliases) => value switch
    {
        CosReference reference => Canonical(reference, aliases).GetHashCode(),
        CosArray array => HashArray(array, aliases),
        CosDictionary dictionary => HashDictionary(dictionary, aliases, ignoreLength: false),
        CosStream stream => HashCode.Combine(1, HashDictionary(stream.Dictionary, aliases, ignoreLength: true)),
        _ => value.GetHashCode(),
    };

    private static int HashArray(CosArray array, Dictionary<CosReference, CosReference> aliases)
    {
        var hash = default(HashCode);
        hash.Add(array.Count);
        foreach (CosObject item in array)
        {
            hash.Add(Hash(item, aliases));
        }

        return hash.ToHashCode();
    }

    private static int HashDictionary(CosDictionary dictionary, Dictionary<CosReference, CosReference> aliases, bool ignoreLength)
    {
        // Entries in any order (§7.3.7): their hashes are summed.
        int sum = 0;
        int count = 0;
        foreach (KeyValuePair<CosName, CosObject> entry in dictionary)
        {
            if (ignoreLength && entry.Key.Equals(KnownNames.Length))
            {
                continue;
            }

            sum = unchecked(sum + HashCode.Combine(entry.Key, Hash(entry.Value, aliases)));
            count++;
        }

        return HashCode.Combine(count, sum);
    }

    /// <summary>
    /// <see cref="CosObject.DeepEquals"/>, with each reference compared through the merges found so far: two objects that refer to
    /// merged objects are equal when they are otherwise equal.
    /// </summary>
    private static bool Equal(CosObject left, CosObject right, Dictionary<CosReference, CosReference> aliases)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return (left, right) switch
        {
            (CosReference a, CosReference b) => Canonical(a, aliases).Equals(Canonical(b, aliases)),
            (CosArray a, CosArray b) => ArraysEqual(a, b, aliases),
            (CosDictionary a, CosDictionary b) => DictionariesEqual(a, b, aliases, ignoreLength: false),
            (CosStream a, CosStream b) => a.EncodedLength == b.EncodedLength
                && DictionariesEqual(a.Dictionary, b.Dictionary, aliases, ignoreLength: true)
                && a.EncodedData.Span.SequenceEqual(b.EncodedData.Span),
            _ => CosObject.DeepEquals(left, right),
        };
    }

    private static bool ArraysEqual(CosArray left, CosArray right, Dictionary<CosReference, CosReference> aliases)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!Equal(left[index], right[index], aliases))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionariesEqual(CosDictionary left, CosDictionary right, Dictionary<CosReference, CosReference> aliases, bool ignoreLength)
    {
        int leftCount = left.Count - (ignoreLength && left.ContainsKey(KnownNames.Length) ? 1 : 0);
        int rightCount = right.Count - (ignoreLength && right.ContainsKey(KnownNames.Length) ? 1 : 0);
        if (leftCount != rightCount)
        {
            return false;
        }

        foreach (KeyValuePair<CosName, CosObject> entry in left)
        {
            if (ignoreLength && entry.Key.Equals(KnownNames.Length))
            {
                continue;
            }

            if (!right.TryGetValue(entry.Key, out CosObject? other) || !Equal(entry.Value, other, aliases))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct Candidate(CosReference Reference, CosObject Value, int DataHash);
}

/// <summary>What <see cref="DuplicateMerge.Apply"/> merged.</summary>
/// <param name="Aliases">Each object left out, mapped to the object written in its place.</param>
/// <param name="StreamBytes">The encoded stream data the objects left out held.</param>
/// <param name="Rounds">The rounds that merged something, at most <see cref="DuplicateMerge.MaxRounds"/>.</param>
internal sealed record DuplicateMergeResult(IReadOnlyDictionary<CosReference, CosReference> Aliases, long StreamBytes, int Rounds);
