using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Rendering;

namespace Broadside.Caching;

/// <summary>The byte budgets of a document's render caches, copied from <see cref="PdfOptions"/> (issue #103).</summary>
/// <param name="Content">Decoded content streams.</param>
/// <param name="Images">Decoded images and masks.</param>
/// <param name="Glyphs">Glyph outlines.</param>
/// <param name="DisplayLists">Display lists.</param>
internal sealed record CacheBudgets(long Content, long Images, long Glyphs, long DisplayLists)
{
    /// <summary>Gets the defaults of <see cref="PdfOptions"/>.</summary>
    public static CacheBudgets Default { get; } = new(
        PdfOptions.DefaultContentCacheBudget,
        PdfOptions.DefaultImageCacheBudget,
        PdfOptions.DefaultGlyphCacheBudget,
        PdfOptions.DefaultDisplayListCacheBudget);
}

/// <summary>
/// A document's render caches, each bounded by its byte budget and evicted least recently used (spec #33 viewer capability 13, issue
/// #103): decoded content (§7.8.2), decoded images and masks (§8.9), glyph outlines (§9.2.4) and display lists (ADR 0006).
/// </summary>
/// <remarks>
/// Safe for concurrent renders of an unchanged document (CLAUDE.md thread-safety contract): each cache has its own lock, and two
/// threads missing the same entry both build it, the later addition replacing the earlier (both are equal, so either is right).
/// </remarks>
internal sealed class DocumentCaches(CacheBudgets budgets)
{
    /// <summary>Gets the decoded content streams and joined <c>Contents</c> arrays, keyed by the stream or array.</summary>
    public ByteBudgetCache<object, IBudgetedEntry> Content { get; } = new(budgets.Content, ReferenceEqualityComparer.Instance);

    /// <summary>Gets the decoded images and stencil masks, keyed by the image XObject's stream and whether it is painted as a stencil.</summary>
    public ByteBudgetCache<(CosStream Stream, bool Stencil), DisplayImageEntry> Images { get; } = new(budgets.Images);

    /// <summary>Gets the glyph outlines, keyed by the font program and glyph id.</summary>
    public ByteBudgetCache<(FontProgram Program, int GlyphId), GlyphOutlineEntry> Glyphs { get; } = new(budgets.Glyphs);

    /// <summary>Gets the display lists, keyed by the page object, the layers and intent, and the content limits they were built with.</summary>
    public ByteBudgetCache<DisplayListKey, DisplayListEntry> DisplayLists { get; } = new(budgets.DisplayLists);

    /// <summary>Returns every cache's counters.</summary>
    /// <returns>The counters.</returns>
    public PdfDocumentCacheStatistics GetStatistics() =>
        new(Content.GetStatistics(), Images.GetStatistics(), Glyphs.GetStatistics(), DisplayLists.GetStatistics());

    /// <summary>Gets the bytes all four caches hold.</summary>
    public long Bytes => Content.Bytes + Images.Bytes + Glyphs.Bytes + DisplayLists.Bytes;

    /// <summary>Drops least recently used entries, content first and display lists last, until the caches hold at most <paramref name="maxBytes"/>.</summary>
    /// <param name="maxBytes">The most bytes kept.</param>
    /// <returns>The bytes held afterwards.</returns>
    public long Trim(long maxBytes)
    {
        long excess = Bytes - maxBytes;
        excess -= Content.Trim(excess);
        excess -= Images.Trim(excess);
        excess -= Glyphs.Trim(excess);
        DisplayLists.Trim(excess);
        return Bytes;
    }

    /// <summary>Drops every entry of every cache.</summary>
    public void Clear()
    {
        Content.Clear();
        Images.Clear();
        Glyphs.Clear();
        DisplayLists.Clear();
    }
}

/// <summary>
/// An entry derived from many objects of a document: current while no COS object has changed through the public API and no object
/// was added or removed since the derivation started (<see cref="CosMutation.Epoch"/>, read before it reads anything).
/// </summary>
/// <param name="epoch">The epoch read before the derivation.</param>
internal abstract class EpochEntry(long epoch) : IBudgetedEntry
{
    /// <inheritdoc/>
    public abstract long Bytes { get; }

    /// <inheritdoc/>
    public bool IsCurrent => CosMutation.Epoch == epoch;
}
