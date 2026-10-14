using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Text;

/// <summary>
/// Puts a page's extracted glyphs in logical content order: the depth-first order of the structure tree, with the element-level
/// replacements (<c>ActualText</c>, and on request <c>Alt</c> and <c>E</c>) and languages applied, and artifacts left out or
/// flagged.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.8.2.5.1: logical content order is a depth-first traversal of the structure hierarchy; content within one
/// marked-content sequence is already in logical order, so each content item's glyphs keep the order the content shows them in.
/// Each glyph belongs to the innermost content item it is shown in (§14.7.5): a marked-content sequence with an MCID in the page's
/// content or in a form XObject's (MCIDs are scoped to their stream, §14.7.5.2), or a form XObject that is an object content item
/// (§14.7.5.3). The tree's <c>K</c> hierarchy decides the order and the owning element.
/// </para>
/// <para>
/// §14.8.2.2: content under an <c>Artifact</c> structure element is an artifact, and so is content of a tagged page that is not in
/// the tree. A page is tagged when the tree holds a content item of its content or of a form it shows; other pages keep content
/// order. Included artifacts outside the tree follow the tree's content, in content order.
/// </para>
/// <para>
/// §14.9.4: an element's <c>ActualText</c> replaces the text of everything it encloses; the outermost element with a replacement
/// wins, its first glyph on the page carries the replacement and the others carry none. <c>Alt</c> (§14.9.3) and <c>E</c> (§14.9.5)
/// are applied the same way when the options ask, flagged so words break around them. An element whose content shows no glyph on
/// the page adds nothing, as no position stands for its replacement.
/// </para>
/// <para>
/// An element whose content spans pages gives its replacement once, so a document read page by page holds it once: on the page
/// where its content starts, the page of its first content item in logical order (§14.8.2.5.1). On a later page its glyphs stay,
/// in order, owned by the element and flagged with the replacement, but with empty text ranges: the continuation adds no text.
/// </para>
/// </remarks>
internal static class StructureOrder
{
    private const TextGlyphFlags Replacements = TextGlyphFlags.ActualText | TextGlyphFlags.AlternateDescription | TextGlyphFlags.Expansion;

    /// <summary>Orders what <paramref name="extractor"/> extracted from <paramref name="page"/>, or returns <see langword="null"/> when the page is not tagged.</summary>
    public static TextPage? Build(PdfDocument document, PdfPage page, TextExtractor extractor, TextExtractionOptions options)
    {
        if (document.StructureTree is not { } tree)
        {
            return null;
        }

        StructureWalk walk = tree.Walk;
        ReadOnlySpan<ContentItemKey> items = extractor.ContentItems;
        var owners = new (PdfStructureElement? Element, int Order)[items.Length];
        bool tagged = walk.MarkedContent(page.Dictionary) is { Count: > 0 };
        for (int index = 0; index < items.Length; index++)
        {
            ContentItemKey key = items[index];
            bool found = key.Mcid >= 0
                ? walk.TryGetMarkedContent(key.Owner, key.Mcid, out PdfStructureElement? element, out int order)
                : walk.TryGetObject(key.Owner, out element, out order);
            if (found)
            {
                owners[index] = (element, order);
                tagged = true;
            }
        }

        if (!tagged)
        {
            return null;
        }

        ReadOnlySpan<TextGlyph> glyphs = extractor.Glyphs;
        ReadOnlySpan<int> glyphItems = extractor.GlyphItems;
        var elements = new ElementCache(document, options);
        var structural = new List<(int Order, int Glyph)>(glyphs.Length);
        var outside = new List<int>();
        for (int index = 0; index < glyphs.Length; index++)
        {
            int item = glyphItems[index];
            if (item >= 0 && owners[item].Element is { } element)
            {
                if (options.IncludeArtifacts || !elements.Get(element).IsArtifact)
                {
                    structural.Add((owners[item].Order, index));
                }
            }
            else if (options.IncludeArtifacts)
            {
                outside.Add(index);
            }
        }

        // Stable: within one content item the glyphs keep content order (§14.8.2.5.1).
        structural.Sort(static (x, y) => x.Order != y.Order ? x.Order.CompareTo(y.Order) : x.Glyph.CompareTo(y.Glyph));

        ReadOnlySpan<char> source = extractor.Text;
        var ordered = new TextGlyph[structural.Count + outside.Count];
        var text = new System.Text.StringBuilder(source.Length);
        int count = 0;
        PdfStructureElement? replacing = null;
        foreach ((int _, int index) in structural)
        {
            ref readonly TextGlyph glyph = ref glyphs[index];
            PdfStructureElement element = owners[glyphItems[index]].Element!;
            ElementInfo info = elements.Get(element);
            TextGlyphFlags flags = info.IsArtifact ? glyph.Flags | TextGlyphFlags.Artifact : glyph.Flags;
            int start = text.Length;
            if (info.Replacer is { } replacer)
            {
                flags = (flags & ~Replacements) | info.ReplacementFlag;
                if (!ReferenceEquals(replacer, replacing) && StartsOn(walk, replacer, page))
                {
                    text.Append(info.Replacement);
                }
            }
            else
            {
                text.Append(source.Slice(glyph.TextStart, glyph.TextLength));
            }

            replacing = info.Replacer;
            ordered[count++] = glyph with
            {
                TextStart = start,
                TextLength = text.Length - start,
                Flags = flags,
                StructureElement = element,
                Language = info.Language,
            };
        }

        foreach (int index in outside)
        {
            // §14.8.2.2.2: content of a tagged page that is not in the structure tree is an artifact.
            ref readonly TextGlyph glyph = ref glyphs[index];
            int start = text.Length;
            text.Append(source.Slice(glyph.TextStart, glyph.TextLength));
            ordered[count++] = glyph with { TextStart = start, TextLength = text.Length - start, Flags = glyph.Flags | TextGlyphFlags.Artifact };
        }

        return new TextPage(ordered, text.ToString());
    }

    /// <summary>
    /// Whether <paramref name="replacer"/>'s content starts on <paramref name="page"/>, so the page carries its replacement: the page
    /// of its first content item with a known page, or any page showing it when no item names one.
    /// </summary>
    private static bool StartsOn(StructureWalk walk, PdfStructureElement replacer, PdfPage page) =>
        !walk.TryGetFirstPage(replacer, out CosDictionary? first) || ReferenceEquals(first, page.Dictionary);

    /// <summary>What an element contributes to the glyphs it encloses, inherited down the hierarchy.</summary>
    private sealed record ElementInfo(bool IsArtifact, PdfStructureElement? Replacer, string? Replacement, TextGlyphFlags ReplacementFlag, string? Language);

    /// <summary>The <see cref="ElementInfo"/> of each element met on the page, computed once from its parent's.</summary>
    private sealed class ElementCache(PdfDocument document, TextExtractionOptions options)
    {
        private readonly Dictionary<PdfStructureElement, ElementInfo> _infos = [];
        private ElementInfo? _root;

        public ElementInfo Get(PdfStructureElement element)
        {
            if (_infos.TryGetValue(element, out ElementInfo? info))
            {
                return info;
            }

            // The chain up to the nearest element already known (or the root), then down again; iterative, as hierarchies can be deep.
            var chain = new Stack<PdfStructureElement>();
            ElementInfo inherited = Root;
            for (PdfStructureElement? current = element; current is not null; current = current.Parent)
            {
                if (_infos.TryGetValue(current, out ElementInfo? known))
                {
                    inherited = known;
                    break;
                }

                if (!chain.Contains(current))
                {
                    chain.Push(current);
                }
                else
                {
                    break;
                }
            }

            while (chain.TryPop(out PdfStructureElement? next))
            {
                inherited = Derive(next, inherited);
                _infos[next] = inherited;
            }

            return inherited;
        }

        private ElementInfo Root => _root ??= new ElementInfo(false, null, null, TextGlyphFlags.None, ViewReading.Text(document, document.Catalog, StructureNames.Lang));

        private ElementInfo Derive(PdfStructureElement element, ElementInfo parent)
        {
            bool artifact = parent.IsArtifact || element.StandardType is { Name: "Artifact", IsStandard: true };
            string? language = element.Language ?? parent.Language;
            if (parent.Replacer is not null)
            {
                // The outermost replacement stands for everything below it (§14.9.4).
                return parent with { IsArtifact = artifact, Language = language };
            }

            if (element.ActualText is { } actual)
            {
                return new ElementInfo(artifact, element, actual, TextGlyphFlags.ActualText, language);
            }

            if (options.ApplyAlternateDescriptions && element.AlternateDescription is { Length: > 0 } alternate)
            {
                return new ElementInfo(artifact, element, alternate, TextGlyphFlags.AlternateDescription, language);
            }

            if (options.ApplyExpansions && element.Expansion is { Length: > 0 } expansion)
            {
                return new ElementInfo(artifact, element, expansion, TextGlyphFlags.Expansion, language);
            }

            return new ElementInfo(artifact, null, null, TextGlyphFlags.None, language);
        }
    }
}
