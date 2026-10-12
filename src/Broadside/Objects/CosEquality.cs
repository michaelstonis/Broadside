namespace Broadside.Objects;

/// <summary>Structural equality of COS objects; the rules are documented on <see cref="CosObject.DeepEquals"/>.</summary>
/// <remarks>ISO 32000-2 §7.3.</remarks>
internal static class CosEquality
{
    public static bool DeepEquals(CosObject? left, CosObject? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return (left, right) switch
        {
            (null, _) or (_, null) => false,
            (CosArray a, CosArray b) => ArraysEqual(a, b),
            (CosDictionary a, CosDictionary b) => DictionariesEqual(a, b, ignoreLength: false),
            (CosStream a, CosStream b) => DictionariesEqual(a.Dictionary, b.Dictionary, ignoreLength: true)
                && a.EncodedData.Span.SequenceEqual(b.EncodedData.Span),
            _ => left.Equals(right),
        };
    }

    private static bool ArraysEqual(CosArray left, CosArray right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!DeepEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictionariesEqual(CosDictionary left, CosDictionary right, bool ignoreLength)
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

            if (!right.TryGetValue(entry.Key, out CosObject? other) || !DeepEquals(entry.Value, other))
            {
                return false;
            }
        }

        return true;
    }
}
