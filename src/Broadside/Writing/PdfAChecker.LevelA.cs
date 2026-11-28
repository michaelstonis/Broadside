using System.Globalization;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Writing;

/// <content>
/// The level A rules of PDF/A-2a and PDF/A-3a (issue #650): the logical structure ISO 19005-2 and -3 clause 6.7 requires, as veraPDF's
/// PDF/A-2A and PDF/A-3A profiles check it, and the Private Use Area rule 6.2.11.7.3-1.
/// </content>
/// <remarks>
/// <para>
/// veraPDF rules: 6.7.2.2-1 (<c>MarkInfo</c> <c>Marked</c> true; a save sets it when the structure tree is there, so it is reported only
/// for a document without one), 6.7.3.3-1 (a <c>StructTreeRoot</c>), 6.7.3.4-1 (every non-standard structure type role-mapped to a
/// standard one), 6.7.3.4-2 (no circular mapping), 6.7.3.4-3 (no standard type remapped), 6.7.4-1 (every <c>Lang</c> of the catalog, a
/// structure element or a property list a language identifier, or empty) and 6.2.11.7.3-1 (a glyph mapped to the Unicode Private Use
/// Area shown inside replacement text: a marked-content sequence's <c>ActualText</c>, or its structure element's or an ancestor's).
/// The structure types are those of the PDF 1.7 set, which PDF/A-2 and -3 are based on.
/// </para>
/// <para>
/// veraPDF checks no more of level A. The issue's further rules are checked too and reported under Broadside's own identifiers, which
/// name the ISO 32000-2 clause, never a veraPDF rule: <see cref="UntaggedContentRule"/> (content neither tagged nor an artifact,
/// §14.8.2.2), <see cref="LanguageRule"/> (no catalog <c>Lang</c>, and content in a structure element with no language in effect,
/// §14.9.2) and <see cref="AlternateDescriptionRule"/> (a <c>Figure</c>, <c>Formula</c> or <c>Form</c> without <c>Alt</c> or
/// <c>ActualText</c>, or with auto-tagging's placeholder; a <c>Form</c> whose widget's field has a <c>TU</c> is described, §14.9.3).
/// Word breaks are not checked: veraPDF has no rule for them. The content walk is the PDF/UA check's (<see cref="TaggedContentWalker"/>).
/// </para>
/// <para>ISO 19005-2 §6.2.11.7.3 and §6.7; ISO 19005-3 §6.2.11.7.3 and §6.7; ISO 32000-2 §14.7.3, §14.8, §14.9.2, §14.9.3 and §14.9.4.</para>
/// </remarks>
internal sealed partial class PdfAChecker
{
    /// <summary>Broadside's identifier for content neither tagged nor marked as an artifact in a level A save (ISO 32000-2 §14.8.2.2).</summary>
    public const string UntaggedContentRule = "ISO 32000-2 14.8.2.2-1";

    /// <summary>Broadside's identifier for content with no natural language in effect in a level A save (ISO 32000-2 §14.9.2).</summary>
    public const string LanguageRule = "ISO 32000-2 14.9.2-1";

    /// <summary>Broadside's identifier for a figure, formula or form without alternate text in a level A save (ISO 32000-2 §14.9.3).</summary>
    public const string AlternateDescriptionRule = "ISO 32000-2 14.9.3-1";

    private static readonly CosName Lang = new("Lang");

    /// <summary>Whether <paramref name="rule"/> is a veraPDF rule identifier (<c>6.7.4-1</c>), not one of Broadside's own (<c>ISO 32000-2 14.9.2-1</c>).</summary>
    public static bool IsVeraPdfRule(string rule) => rule.Length > 0 && char.IsAsciiDigit(rule[0]);

    /// <summary>The level A rules (PDF/A-2a, PDF/A-3a): the structure tree, its types and languages, and the content's tagging.</summary>
    private void CheckLogicalStructure()
    {
        if (!_conformance.NeedsLogicalStructure())
        {
            return;
        }

        if (_document.Language is { } catalogLanguage && catalogLanguage.Length > 0 && !TaggedPdfChecks.IsLanguageTag(catalogLanguage))
        {
            Report("6.7.4-1", $"the catalog's Lang '{catalogLanguage}' is not a language identifier");
        }

        PdfStructureTreeRoot? root = _document.StructureTree;
        if (root is null)
        {
            Report("6.7.3.3-1", "the document has no structure tree (no StructTreeRoot in the catalog): it is not tagged");
            if (!_document.MarkInfo.Marked)
            {
                Report("6.7.2.2-1", "the catalog's MarkInfo has no Marked entry true (a save sets it only for a document with a structure tree)");
            }

            return;
        }

        foreach (string type in TaggedPdfChecks.RemappedStandardTypes(root))
        {
            Report("6.7.3.4-3", $"the role map remaps the standard structure type {type}", root.Reference);
        }

        bool languageKnown = !string.IsNullOrEmpty(_document.Language);
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<PdfStructureElement>(root.Children.Reverse());
        while (pending.TryPop(out PdfStructureElement? element))
        {
            if (!seen.Add(element.Dictionary))
            {
                continue;
            }

            CheckStructureElement(element, languageKnown);
            foreach (PdfStructureElement kid in element.Children.OfType<PdfStructureElement>().Reverse())
            {
                pending.Push(kid);
            }
        }

        var walker = new TaggedContentWalker(_document, root, new LevelAContent(this));
        foreach (PdfPage page in _document.Pages)
        {
            walker.WalkPage(page);
        }
    }

    private void CheckStructureElement(PdfStructureElement element, bool languageKnown)
    {
        CosReference? reference = element.Reference;
        string type = element.StructureType?.Value ?? "(none)";
        if (!TaggedPdfChecks.HasPdf17StandardType(element))
        {
            if (!TaggedPdfChecks.MapsToPdf17StandardType(element))
            {
                Report("6.7.3.4-1", $"the structure type {type} is not role-mapped to a standard structure type", reference);
            }

            if (TaggedPdfChecks.HasCircularMapping(element))
            {
                Report("6.7.3.4-2", $"the role mapping of the structure type {type} is circular", reference);
            }
        }

        if (element.Language is { Length: > 0 } language && !TaggedPdfChecks.IsLanguageTag(language))
        {
            Report("6.7.4-1", $"a structure element's Lang '{language}' is not a language identifier", reference);
        }

        if (!languageKnown && TaggedPdfChecks.EnclosesContent(element) && string.IsNullOrEmpty(element.EffectiveLanguage))
        {
            Report(LanguageRule, "the natural language of the content is not specified: the catalog has no Lang, nor do the structure elements that enclose it");
        }

        string? standard = element.ResolvedStandardType?.Name;
        if (standard is "Figure" or "Formula" or "Form")
        {
            switch (TaggedPdfChecks.Describe(_document, element))
            {
                case TaggedPdfChecks.Description.Missing when standard != "Form" || !DescribesWidget(element):
                    Report(AlternateDescriptionRule, $"a {standard} has no alternate text (Alt) or replacement text (ActualText)", reference);
                    break;
                case TaggedPdfChecks.Description.Placeholder:
                    Report(AlternateDescriptionRule, $"a {standard} still has auto-tagging's placeholder alternate text '{element.AlternateDescription}', not a description", reference);
                    break;
            }
        }
    }

    /// <summary>Whether a <c>Form</c> element's widget annotation has a field whose <c>TU</c> describes it (ISO 32000-2 §12.7.4.1, §14.8.4.4.3).</summary>
    private bool DescribesWidget(PdfStructureElement form) => form.Children
        .OfType<PdfObjectReference>()
        .Any(kid => Resolve(kid.ReferencedObject) is CosDictionary widget && TaggedPdfChecks.HasToolTip(_document, widget));

    /// <summary>Whether a shown character maps into the Unicode Private Use Area: U+E000 to U+F8FF, or planes 15 and 16.</summary>
    private static bool IsPrivateUse(ReadOnlySpan<char> text)
    {
        foreach (char character in text)
        {
            // A high surrogate from U+DB80 to U+DBFF starts a code point of plane 15 or 16.
            if (character is (>= '' and <= '') or (>= '\uDB80' and <= '\uDBFF'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What the level A rules check of the content walk.</summary>
    private sealed class LevelAContent(PdfAChecker checker) : ITaggedContentVisitor
    {
        public void MarkedContentBegun(TaggedContentState state, CosDictionary? properties, bool artifact, bool tagged)
        {
            if (properties is not null && checker.Resolve(properties.GetValueOrDefault(Lang)) is CosString text
                && text.DecodeText() is { Length: > 0 } language && !TaggedPdfChecks.IsLanguageTag(language))
            {
                checker.Report("6.7.4-1", $"a property list's Lang '{language}' is not a language identifier", state.Page);
            }
        }

        public void Painted(TaggedContentState state)
        {
            if (!state.Tagged && !state.Artifact)
            {
                checker.Report(UntaggedContentRule, "content on a page is neither tagged (in the structure tree) nor marked as an Artifact", state.Page);
            }
        }

        public void FontSet(CosObject font)
        {
        }

        public void TextShown(PdfFont font, string resourceName, ReadOnlySpan<byte> text, TaggedContentState state)
        {
            if (state.HasActualText)
            {
                return;
            }

            Span<char> buffer = stackalloc char[FontUnicode.MaxLength];
            while (!text.IsEmpty)
            {
                ShownGlyph glyph = font.ReadShownGlyph(text);
                int written = font.ReadUnicode(text, buffer, out UnicodeSource _);
                if (IsPrivateUse(buffer[..written]))
                {
                    checker.ReportUnicode(font, resourceName, "6.2.11.7.3-1", string.Create(CultureInfo.InvariantCulture, $"maps the character code {glyph.Code} into the Unicode Private Use Area, and no ActualText encloses it"));
                    return;
                }

                text = text[Math.Clamp(glyph.Length, 1, text.Length)..];
            }
        }
    }
}
