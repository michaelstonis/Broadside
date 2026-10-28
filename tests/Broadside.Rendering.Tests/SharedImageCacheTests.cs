using System.Text;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests;

/// <summary>
/// Issue #432: the cache of converted images a viewer's backends share. Leased images are never freed; every image is freed exactly
/// once, when it has left the cache (evicted, replaced, or the last backend left) and its last lease is released; the bytes kept stay
/// within the budget; the backends that miss a key at once convert it once.
/// </summary>
public sealed class SharedImageCacheTests
{
    [Fact]
    public void An_evicted_image_is_freed_when_its_last_lease_is_released_not_before()
    {
        using PdfDocument document = OneImage();
        SharedImageCache<Probe> cache = SharedImageCache<Probe>.Join(document, budget: 100);
        var a = new Probe();
        var b = new Probe();
        SharedImageCache<Probe>.Slot slotA = cache.SlotOf(new object());
        SharedImageCache<Probe>.Slot slotB = cache.SlotOf(new object());

        cache.Publish(slotA, a, 60);
        Probe? again = cache.Lease(slotA);
        Assert.Same(a, again);
        cache.Release(again);

        // B does not fit beside A: A leaves the cache, but its converter still draws it.
        cache.Publish(slotB, b, 60);
        Assert.Null(cache.Lease(slotA));
        Assert.Equal(60, cache.Bytes);
        Assert.Equal(0, a.Frees);

        cache.Release(a);
        Assert.Equal(1, a.Frees);
        cache.Release(b);
        Assert.Equal(0, b.Frees);

        cache.Leave();
        Assert.Equal(1, b.Frees);
        Assert.Equal(0, cache.Bytes);
        Assert.All([a, b], p => Assert.False(p.FreedWhileLeased));
    }

    [Fact]
    public void A_replaced_conversion_is_freed_once_its_draws_end_and_the_new_one_serves_the_key()
    {
        using PdfDocument document = OneImage();
        SharedImageCache<Probe> cache = SharedImageCache<Probe>.Join(document, budget: 1000);
        SharedImageCache<Probe>.Slot slot = cache.SlotOf(new object());
        var coarse = new Probe();
        var fine = new Probe();
        cache.Publish(slot, coarse, 10);
        Probe drawing = cache.Lease(slot)!;
        cache.Release(coarse);

        cache.Publish(slot, fine, 40);
        Assert.Equal(0, coarse.Frees);
        Assert.Equal(40, cache.Bytes);
        Assert.Same(fine, cache.Lease(slot));

        cache.Release(drawing);
        Assert.Equal(1, coarse.Frees);
        cache.Release(fine);
        cache.Release(fine);
        cache.Leave();
        Assert.Equal(1, fine.Frees);
    }

    [Fact]
    public void Images_larger_than_the_budget_are_kept_two_at_a_time_outside_it_and_a_zero_budget_keeps_none()
    {
        using PdfDocument document = OneImage();
        SharedImageCache<Probe> cache = SharedImageCache<Probe>.Join(document, budget: 10);
        Probe[] large = [new(), new(), new()];
        SharedImageCache<Probe>.Slot[] slots = [.. large.Select(_ => cache.SlotOf(new object()))];
        for (int i = 0; i < large.Length; i++)
        {
            cache.Publish(slots[i], large[i], 50);
            cache.Release(large[i]);
        }

        Assert.Equal([1, 0, 0], large.Select(p => p.Frees));
        Assert.Equal(0, cache.Bytes);
        Assert.True(cache.Grow(large[2], 1000));
        cache.Leave();
        Assert.All(large, p => Assert.Equal(1, p.Frees));

        using PdfDocument other = OneImage();
        SharedImageCache<Probe> none = SharedImageCache<Probe>.Join(other, budget: 0);
        var probe = new Probe();
        SharedImageCache<Probe>.Slot slot = none.SlotOf(new object());
        none.Publish(slot, probe, 1);
        Assert.Null(none.Lease(slot));
        Assert.False(none.Grow(probe, 1));
        none.Release(probe);
        Assert.Equal(1, probe.Frees);
        none.Leave();
    }

    [Fact]
    public void Leases_conversions_and_evictions_from_many_threads_free_every_image_once_never_while_leased_within_the_budget()
    {
        using PdfDocument document = OneImage();
        const long budget = 500;
        const int threads = 8;
        SharedImageCache<Probe> cache = SharedImageCache<Probe>.Join(document, budget);
        object[] keys = [.. Enumerable.Range(0, 24).Select(_ => new object())];
        var all = new System.Collections.Concurrent.ConcurrentBag<Probe>();
        int overBudget = 0;
        int drawnFreed = 0;
        int[] converting = new int[keys.Length];
        int doubleConversions = 0;
#pragma warning disable CA5394 // Reproducible test data, not security.
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, t =>
        {
            var random = new Random(t);
            for (int i = 0; i < 4000; i++)
            {
                int k = random.Next(keys.Length);
                SharedImageCache<Probe>.Slot slot = cache.SlotOf(keys[k]);
                Probe? image = cache.Lease(slot);
                if (image is null)
                {
                    lock (slot.Converting)
                    {
                        image = cache.Lease(slot);
                        if (image is null)
                        {
                            if (Interlocked.Increment(ref converting[k]) != 1)
                            {
                                Interlocked.Increment(ref doubleConversions);
                            }

                            image = new Probe();
                            all.Add(image);
                            cache.Publish(slot, image, random.Next(1, 120) == 1 ? 900 : random.Next(10, 120));
                            Interlocked.Decrement(ref converting[k]);
                        }
                    }
                }

                if (image.IsFreed)
                {
                    Interlocked.Increment(ref drawnFreed);
                }

                if (random.Next(4) == 0)
                {
                    cache.Grow(image, random.Next(1, 30));
                }

                if (cache.Bytes > budget)
                {
                    Interlocked.Increment(ref overBudget);
                }

                if (image.IsFreed)
                {
                    Interlocked.Increment(ref drawnFreed);
                }

                cache.Release(image);
            }
        });
#pragma warning restore CA5394

        cache.Leave();
        Assert.Equal(0, drawnFreed);
        Assert.Equal(0, overBudget);
        Assert.Equal(0, doubleConversions);
        Assert.Equal(0, cache.Bytes);
        Assert.All(all, p => Assert.Equal(1, p.Frees));
        Assert.All(all, p => Assert.False(p.FreedWhileLeased));
        Assert.Equal(all.Count, cache.Converted);
    }

    [Fact]
    public void The_backends_of_one_document_share_one_cache_until_the_last_leaves()
    {
        using PdfDocument document = OneImage();
        using PdfDocument other = OneImage();
        SharedImageCache<Probe> first = SharedImageCache<Probe>.Join(document, 100);
        SharedImageCache<Probe> second = SharedImageCache<Probe>.Join(document, 100);
        SharedImageCache<Probe> elsewhere = SharedImageCache<Probe>.Join(other, 100);
        Assert.Same(first, second);
        Assert.NotSame(first, elsewhere);
        Assert.Equal(2, first.Users);

        var probe = new Probe();
        first.Publish(first.SlotOf(document), probe, 10);
        first.Release(probe);
        first.Leave();
        Assert.Equal(0, probe.Frees);
        second.Leave();
        Assert.Equal(1, probe.Frees);
        elsewhere.Leave();
    }

    private static PdfDocument OneImage() => PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes("0 0 m")));

    private sealed class Probe : SharedImage
    {
        private int _frees;

        public int Frees => Volatile.Read(ref _frees);

        public bool FreedWhileLeased { get; private set; }

        protected override void Free()
        {
            FreedWhileLeased |= Leases != 0;
            Interlocked.Increment(ref _frees);
        }
    }
}
