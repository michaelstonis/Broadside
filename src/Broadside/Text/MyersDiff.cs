using System.Buffers;

namespace Broadside.Text;

/// <summary>
/// The shortest edit script between two sequences of integers (word identities): Myers' O(ND) difference algorithm in its
/// linear-space variant, which finds the middle snake of the edit graph from both ends at once and divides the problem there.
/// </summary>
/// <remarks>
/// E. W. Myers, "An O(ND) Difference Algorithm and Its Variations", Algorithmica 1 (1986), section 4b. Memory is linear in the
/// sequences' length (the search vectors are as long as the distance bound); work runs on an explicit stack, so deep divisions do not
/// recurse.
/// </remarks>
internal static class MyersDiff
{
    /// <summary>Marks the elements of <paramref name="a"/> to delete and of <paramref name="b"/> to insert to turn one into the other.</summary>
    /// <param name="a">The old sequence.</param>
    /// <param name="b">The new sequence.</param>
    /// <param name="deleted">Set where an element of <paramref name="a"/> is deleted; as long as <paramref name="a"/>, all false on entry.</param>
    /// <param name="inserted">Set where an element of <paramref name="b"/> is inserted; as long as <paramref name="b"/>, all false on entry.</param>
    /// <param name="maxDistance">The largest number of deletions plus insertions to look for.</param>
    /// <param name="cancellationToken">Checked once per step of the search.</param>
    /// <returns><see langword="false"/> when the edit distance passes <paramref name="maxDistance"/>; the marks are then incomplete.</returns>
    public static bool TryDiff(ReadOnlySpan<int> a, ReadOnlySpan<int> b, Span<bool> deleted, Span<bool> inserted, int maxDistance, CancellationToken cancellationToken)
    {
        // Each step of the search goes one edit deeper from each end, so half the bound (and a margin for the odd edit) finds every
        // script within it; the exact count is checked at the end.
        int halfBound = (int)Math.Min((maxDistance / 2L) + 2, ((long)a.Length + b.Length + 1) / 2 + 1);
        int length = (2 * halfBound) + 2;
        int[] forward = ArrayPool<int>.Shared.Rent(length);
        int[] backward = ArrayPool<int>.Shared.Rent(length);
        var work = new Stack<(int ALow, int AHigh, int BLow, int BHigh)>();
        work.Push((0, a.Length, 0, b.Length));
        int distance = 0;
        try
        {
            while (work.TryPop(out (int ALow, int AHigh, int BLow, int BHigh) range))
            {
                (int aLow, int aHigh, int bLow, int bHigh) = range;

                // The common prefix and suffix are kept.
                while (aLow < aHigh && bLow < bHigh && a[aLow] == b[bLow])
                {
                    aLow++;
                    bLow++;
                }

                while (aLow < aHigh && bLow < bHigh && a[aHigh - 1] == b[bHigh - 1])
                {
                    aHigh--;
                    bHigh--;
                }

                if (aLow == aHigh || bLow == bHigh)
                {
                    deleted[aLow..aHigh].Fill(true);
                    inserted[bLow..bHigh].Fill(true);
                    distance += aHigh - aLow + bHigh - bLow;
                    if (distance > maxDistance)
                    {
                        return false;
                    }

                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                switch (MiddleSnake(a[aLow..aHigh], b[bLow..bHigh], forward.AsSpan(0, length), backward.AsSpan(0, length), halfBound, cancellationToken, out int x, out int y))
                {
                    case Split.Found:
                        work.Push((aLow + x, aHigh, bLow + y, bHigh));
                        work.Push((aLow, aLow + x, bLow, bLow + y));
                        break;
                    case Split.Disjoint:
                        deleted[aLow..aHigh].Fill(true);
                        inserted[bLow..bHigh].Fill(true);
                        distance += aHigh - aLow + bHigh - bLow;
                        if (distance > maxDistance)
                        {
                            return false;
                        }

                        break;
                    default:
                        return false;
                }
            }

            return true;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(forward);
            ArrayPool<int>.Shared.Return(backward);
        }
    }

    /// <summary>
    /// Searches the edit graph of <paramref name="a"/> and <paramref name="b"/> from both corners until the paths meet, and returns
    /// the point where they do: the problem splits into the parts before and after it.
    /// </summary>
    private static Split MiddleSnake(ReadOnlySpan<int> a, ReadOnlySpan<int> b, Span<int> forward, Span<int> backward, int halfBound, CancellationToken cancellationToken, out int splitX, out int splitY)
    {
        int n = a.Length, m = b.Length;
        int maxD = Math.Min((n + m + 1) / 2, halfBound);
        int offset = maxD;
        int vLength = (2 * maxD) + 2;
        Span<int> v1 = forward[..vLength];
        Span<int> v2 = backward[..vLength];
        v1.Fill(-1);
        v2.Fill(-1);
        v1[offset + 1] = 0;
        v2[offset + 1] = 0;
        int delta = n - m;
        bool front = (delta & 1) != 0;
        int k1Start = 0, k1End = 0, k2Start = 0, k2End = 0;
        for (int d = 0; d < maxD; d++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The forward paths: furthest reaching on each diagonal k1 after d edits.
            for (int k1 = -d + k1Start; k1 <= d - k1End; k1 += 2)
            {
                int k1Offset = offset + k1;
                int x1 = k1 == -d || (k1 != d && v1[k1Offset - 1] < v1[k1Offset + 1]) ? v1[k1Offset + 1] : v1[k1Offset - 1] + 1;
                int y1 = x1 - k1;
                while (x1 < n && y1 < m && a[x1] == b[y1])
                {
                    x1++;
                    y1++;
                }

                v1[k1Offset] = x1;
                if (x1 > n)
                {
                    k1End += 2; // off the right of the graph
                }
                else if (y1 > m)
                {
                    k1Start += 2; // off the bottom of the graph
                }
                else if (front)
                {
                    int k2Offset = offset + delta - k1;
                    if (k2Offset >= 0 && k2Offset < vLength && v2[k2Offset] != -1 && x1 >= n - v2[k2Offset])
                    {
                        splitX = x1;
                        splitY = y1;
                        return Split.Found;
                    }
                }
            }

            // The reverse paths, from the far corner.
            for (int k2 = -d + k2Start; k2 <= d - k2End; k2 += 2)
            {
                int k2Offset = offset + k2;
                int x2 = k2 == -d || (k2 != d && v2[k2Offset - 1] < v2[k2Offset + 1]) ? v2[k2Offset + 1] : v2[k2Offset - 1] + 1;
                int y2 = x2 - k2;
                while (x2 < n && y2 < m && a[n - x2 - 1] == b[m - y2 - 1])
                {
                    x2++;
                    y2++;
                }

                v2[k2Offset] = x2;
                if (x2 > n)
                {
                    k2End += 2;
                }
                else if (y2 > m)
                {
                    k2Start += 2;
                }
                else if (!front)
                {
                    int k1Offset = offset + delta - k2;
                    if (k1Offset >= 0 && k1Offset < vLength && v1[k1Offset] != -1)
                    {
                        int x1 = v1[k1Offset];
                        if (x1 >= n - x2)
                        {
                            splitX = x1;
                            splitY = offset + x1 - k1Offset;
                            return Split.Found;
                        }
                    }
                }
            }
        }

        splitX = 0;
        splitY = 0;

        // The paths meet within (n + m + 1) / 2 steps unless the bound cut the search short; with nothing in common they never do.
        return maxD == (n + m + 1) / 2 ? Split.Disjoint : Split.TooFar;
    }

    private enum Split
    {
        Found,
        Disjoint,
        TooFar,
    }
}
