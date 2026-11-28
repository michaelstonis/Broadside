using System.Text;
using Broadside.Annotations;
using Broadside.Content;
using Broadside.Diagnostics;
using Broadside.Filters;
using Broadside.Fonts;
using Broadside.Fonts.Resolution;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Structure;

namespace Broadside.Text.Editing;

/// <summary>One match to replace, with what the edit learns about it as the content runs.</summary>
/// <param name="Match">The match, from <see cref="Text"/>.</param>
/// <param name="Replacement">The text that replaces it; empty to delete it.</param>
/// <param name="Text">The extracted text the match indexes.</param>
internal sealed record TextEditMatch(TextMatch Match, string Replacement, TextPage Text)
{
    /// <summary>A located match of one glyph, by its code and box corner in page space, replaced by "y": for benchmarks and tests.</summary>
    internal static TextEditMatch OfGlyph(uint code, PathPoint corner, TextPage text) =>
        new(new TextMatch(0, "x", 0, 0, 1, [], new PdfRectangle(corner.X - 1, corner.Y - 1, corner.X + 1, corner.Y + 1)), "y", text)
        {
            Keys = [GlyphMatching.Key(code, corner)],
            Located = true,
        };

    /// <summary>Gets or sets the text the match's glyphs show instead: the replacement, or a whole <c>ActualText</c> sequence's new text.</summary>
    public string Shown { get; set; } = Replacement;

    /// <summary>Gets or sets the glyph keys of the match, in its glyph order.</summary>
    public List<GlyphMatching.GlyphKey> Keys { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether every glyph of the match was found in the content.</summary>
    public bool Located { get; set; }

    /// <summary>Gets or sets a value indicating whether the content run reached the match's first glyph.</summary>
    public bool Anchored { get; set; }

    /// <summary>Gets or sets a value indicating whether the replacement was planned (successfully or not).</summary>
    public bool Planned { get; set; }

    /// <summary>Gets or sets the replacement's runs, or <see langword="null"/> when it cannot be shown (the match is then kept).</summary>
    public TextEditPlan? Plan { get; set; }

    /// <summary>Gets or sets a value indicating whether the match was replaced.</summary>
    public bool Replaced { get; set; }

    /// <summary>Gets a value indicating whether the match's glyphs are changed: planned, with a replacement that can be shown.</summary>
    public bool Effective => Plan is not null;
}

/// <summary>A replacement's runs, each in one font, and its advance in the original font's <c>TJ</c> units.</summary>
/// <param name="Runs">The runs.</param>
/// <param name="Units">The advance of all runs, in thousandths of the original text space unit (<c>TJ</c> units).</param>
/// <param name="UsedFallback">Whether a run is in a fallback font.</param>
internal sealed record TextEditPlan(List<TextEditRun> Runs, double Units, bool UsedFallback);

/// <summary>Consecutive characters of a replacement shown in one font.</summary>
/// <param name="Fallback">The fallback font, or <see langword="null"/> for the original font.</param>
/// <param name="Codes">The codes, high byte first.</param>
internal sealed record TextEditRun(PdfCanvasFont? Fallback, List<byte> Codes);

/// <summary>The text state a text-showing operator ran with, captured at its first glyph.</summary>
/// <param name="Font">The font.</param>
/// <param name="FontDictionary">The font dictionary.</param>
/// <param name="Size">The font size <c>T_fs</c>.</param>
/// <param name="Scaling">The horizontal scaling <c>T_h</c>, as a fraction.</param>
/// <param name="CharacterSpacing">The character spacing <c>T_c</c>.</param>
/// <param name="WordSpacing">The word spacing <c>T_w</c>.</param>
/// <param name="Vertical">Whether the font writes vertically.</param>
internal readonly record struct TextEditState(PdfFont? Font, CosDictionary? FontDictionary, double Size, double Scaling, double CharacterSpacing, double WordSpacing, bool Vertical);

/// <summary>
/// One application of text replacement to a document (issue #641): per page, finds each match's glyphs again by identity, rewrites the
/// text-showing operators that hold them through <see cref="TextEditEditor"/>, then updates replacement text in the structure tree and
/// extends ToUnicode CMaps. Every change is made under one change log, so a failure, or a diagnostic in strict mode, leaves the document
/// as it was.
/// </summary>
/// <remarks>
/// ISO 32000-2 §9.4.3 (Table 107), §9.4.4, §9.3.2 to §9.3.4, §9.6.5, §9.7.6.2, §9.9.2, §9.10.2, §9.10.3 and §14.9.4. A glyph is
/// identified by its code and the bottom-left corner of its box in page space (<see cref="GlyphMatching"/>), as redaction does.
/// </remarks>
internal sealed class TextEditSession
{
    private static readonly CosName ActualText = new("ActualText");
    private static readonly CosName Alt = new("Alt");
    private static readonly CosName Expansion = new("E");
    private static readonly CosName[] EncodingEntries = [FilterNames.Filter, FilterNames.DecodeParms, FilterNames.F, new("FFilter"), new("FDecodeParms"), FilterNames.DL, KnownNames.Length];
    private static readonly CosName[] ReplacementKeys = [ActualText, Alt, Expansion];

    private readonly List<Diagnostic> _diagnostics = [];
    private readonly List<(PdfPage Page, TextPage Text, List<TextEditMatch> Matches)> _pages = [];
    private readonly Dictionary<GlyphMatching.GlyphKey, Queue<int>> _targets = [];
    private readonly Dictionary<PdfFont, TextFontEncoder> _encoders = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PdfFont, PdfCanvasFont?> _resolved = new(ReferenceEqualityComparer.Instance);
    private readonly ToUnicodeExtension _toUnicode = new();
    private List<TextEditMatch> _current = [];
    private TextPage? _currentText;
    private TextEditEditor? _pageEditor;
    private PdfCanvasFont? _helvetica;
    private int _replaced;
    private int _usedFallback;

    public TextEditSession(PdfDocument document, PdfTextReplaceOptions options)
    {
        Document = document;
        Options = options;
    }

    public PdfDocument Document { get; }

    public PdfTextReplaceOptions Options { get; }

    public ContentOptions ContentOptions { get; } = new();

    /// <summary>Gets the depth of form copies being rewritten, bounded by <see cref="ContentOptions.MaxNestingDepth"/>.</summary>
    public int FormDepth { get; private set; }

    /// <summary>Gets a value indicating whether glyphs remain to be found in the current page's run.</summary>
    public bool HasTargets => _targets.Count > 0;

    /// <summary>Gets the union of the current page's located matches' bounds, in page space.</summary>
    public PdfRectangle TargetBounds { get; private set; }

    /// <summary>Adds a page's matches; a match overlapping an earlier one of the page is left out.</summary>
    public void AddPage(PdfPage page, TextPage text, IEnumerable<(TextMatch Match, string Replacement)> matches)
    {
        List<TextEditMatch> list = [];
        var used = new HashSet<int>();
        foreach ((TextMatch match, string replacement) in matches.OrderBy(static entry => entry.Match.GlyphStart))
        {
            var entry = new TextEditMatch(match, replacement, text);
            (int first, int count, entry.Shown) = Shown(text, match, replacement);
            bool overlaps = false;
            for (int glyph = first; glyph < first + count && glyph < text.Glyphs.Count; glyph++)
            {
                overlaps |= !used.Add(glyph);
                TextGlyph value = text.Glyphs[glyph];
                entry.Keys.Add(GlyphMatching.Key(value.CharacterCode, value.Quad.BottomLeft));
            }

            if (!overlaps && entry.Keys.Count > 0)
            {
                list.Add(entry);
            }
        }

        if (list.Count > 0)
        {
            _pages.Add((page, text, list));
        }
    }

    /// <summary>
    /// The glyphs a match changes and the text they show instead. A match inside an <c>ActualText</c> sequence covers every glyph of the
    /// sequence (§14.9.4), whose text is the replacement's, not the glyphs': when the glyphs read, through their font, as text that holds
    /// the match, only the glyphs of that part change; otherwise every glyph of the sequence shows the sequence's new replacement text,
    /// so what is drawn and what is read agree.
    /// </summary>
    private static (int First, int Count, string Shown) Shown(TextPage text, TextMatch match, string replacement)
    {
        IReadOnlyList<TextGlyph> glyphs = text.Glyphs;
        int end = Math.Min(match.GlyphStart + match.GlyphCount, glyphs.Count);
        if (match.GlyphStart < 0 || match.GlyphStart >= end)
        {
            return (match.GlyphStart, match.GlyphCount, replacement);
        }

        for (int glyph = match.GlyphStart; glyph < end; glyph++)
        {
            if ((glyphs[glyph].Flags & TextGlyphFlags.ActualText) == 0)
            {
                return (match.GlyphStart, match.GlyphCount, replacement);
            }
        }

        var shown = new StringBuilder();
        var starts = new List<int>();
        Span<char> buffer = stackalloc char[FontUnicode.MaxLength];
        for (int glyph = match.GlyphStart; glyph < end; glyph++)
        {
            TextGlyph value = glyphs[glyph];
            starts.Add(shown.Length);
            int written = value.Font.GetUnicode(new CharacterCode(value.CharacterCode, value.CodeLength, true), buffer, out _);
            shown.Append(buffer[..written]);
        }

        starts.Add(shown.Length);
        int at = shown.ToString().IndexOf(match.Text, StringComparison.Ordinal);
        if (at >= 0)
        {
            int first = starts.IndexOf(at), last = starts.IndexOf(at + match.Text.Length);
            if (first >= 0 && last > first)
            {
                return (match.GlyphStart + first, last - first, replacement);
            }
        }

        string actual = text.GetGlyphText(glyphs[match.GlyphStart]).ToString();
        int inside = actual.IndexOf(match.Text, StringComparison.Ordinal);
        string whole = inside < 0 ? replacement : string.Concat(actual.AsSpan(0, inside), replacement, actual.AsSpan(inside + match.Text.Length));
        return (match.GlyphStart, end - match.GlyphStart, whole);
    }

    /// <summary>Applies every page's matches under one change log.</summary>
    public PdfTextEditResult Run()
    {
        if (_pages.Count > 0)
        {
            AnnotationEditing.Atomically(() =>
            {
                foreach ((PdfPage page, TextPage text, List<TextEditMatch> matches) in _pages)
                {
                    EditPage(page, text, matches);
                }

                foreach (PdfFont font in _toUnicode.Apply(Document))
                {
                    Report(
                        TextEditDiagnostics.TextEditToUnicodeExtended,
                        "The ToUnicode CMap of a font gains entries for the codes a replacement uses (ISO 32000-2 §9.10.3); other pages that use the font are unaffected.",
                        font.Reference);
                }

                return true;
            });
        }

        return new PdfTextEditResult(_replaced, _usedFallback, [.. _diagnostics]);
    }

    /// <summary>Records a diagnostic on the document and in the result; in strict mode it throws (and the run is undone).</summary>
    public void Report(DiagnosticCode code, string message, CosReference? reference)
    {
        var diagnostic = new Diagnostic(code, code.DefaultSeverity, message, objectReference: reference);
        _diagnostics.Add(diagnostic);
        Document.ReportDiagnostic(diagnostic);
    }

    /// <summary>Takes the match a glyph shown in the content belongs to, if it is one of the current page's located matches' glyphs.</summary>
    public bool TryTake(GlyphMatching.GlyphKey key, out int match)
    {
        if (_targets.TryGetValue(key, out Queue<int>? queue) && queue.TryDequeue(out match))
        {
            if (queue.Count == 0)
            {
                _targets.Remove(key);
            }

            return true;
        }

        match = -1;
        return false;
    }

    /// <summary>Gets a match of the current page.</summary>
    public TextEditMatch Match(int index) => _current[index];

    /// <summary>
    /// Plans a match's replacement in the font and state of the operator that holds its first glyph: each character in the original
    /// font when it has a usable code (<see cref="TextFontEncoder"/>), otherwise in the first fallback font that has it. Returns
    /// <see langword="null"/>, after recording why, when a character has no font, the size is 0, or a fallback would be needed in
    /// vertical writing.
    /// </summary>
    public TextEditPlan? Plan(TextEditMatch match, in TextEditState state)
    {
        if (state.Font is not { } font || state.Size == 0 || !double.IsFinite(state.Size))
        {
            NotEncodable(match);
            return null;
        }

        TextFontEncoder encoder = Encoder(font);
        List<TextEditRun> runs = [];
        List<(EncodedCode Code, string Text)> toUnicode = [];
        List<(PdfCanvasFont Font, CanvasGlyph Glyph, string Text)> shown = [];
        double units = 0;
        bool fallback = false;
        Span<byte> code = stackalloc byte[4];
        foreach (Rune rune in match.Shown.EnumerateRunes())
        {
            string text = rune.ToString();
            if (encoder.TryEncode(rune.Value, out EncodedCode encoded))
            {
                TextEditRun run = Run(runs, null);
                encoded.Write(run.Codes);
                for (int index = 0; index < encoded.Length; index++)
                {
                    code[index] = (byte)(encoded.Value >> (8 * (encoded.Length - 1 - index)));
                }

                ShownGlyph glyph = font.ReadShownGlyph(code[..encoded.Length]);
                double spacing = state.CharacterSpacing + (glyph.AppliesWordSpacing ? state.WordSpacing : 0);
                units += ((state.Vertical ? glyph.VerticalDisplacement : glyph.HorizontalDisplacement) * 1000) + (spacing * 1000 / state.Size);
                if (encoded.NeedsToUnicode)
                {
                    toUnicode.Add((encoded, text));
                }

                continue;
            }

            if (state.Vertical || FindFallback(font, rune) is not { } substitute || !substitute.TryGetGlyph(rune.Value, out CanvasGlyph canvas))
            {
                NotEncodable(match);
                return null;
            }

            fallback = true;
            TextEditRun fallbackRun = Run(runs, substitute);
            int length = substitute.AppliesWordSpacing ? 1 : 2;
            for (int shift = length - 1; shift >= 0; shift--)
            {
                fallbackRun.Codes.Add((byte)(canvas.Code >> (8 * shift)));
            }

            double fallbackSpacing = state.CharacterSpacing + (length == 1 && canvas.Code == 32 ? state.WordSpacing : 0);
            units += canvas.Width + (fallbackSpacing * 1000 / state.Size);
            shown.Add((substitute, canvas, text));
        }

        foreach ((PdfCanvasFont substitute, CanvasGlyph glyph, string text) in shown)
        {
            substitute.OnShown(glyph, text);
        }

        foreach ((EncodedCode encoded, string text) in toUnicode)
        {
            _toUnicode.Add(font, encoded, text);
        }

        if (fallback)
        {
            Report(
                TextEditDiagnostics.TextEditFallbackFont,
                "Characters of a replacement have no usable glyph in the font the replaced text is shown in; they are shown in a fallback font (ISO 32000-2 §9.9.2).",
                font.Reference);
        }

        return new TextEditPlan(runs, units, fallback);
    }

    /// <summary>Counts a match as replaced.</summary>
    public void Replaced(TextEditMatch match)
    {
        if (match.Replaced)
        {
            return;
        }

        match.Replaced = true;
        _replaced++;
        if (match.Plan is { UsedFallback: true })
        {
            _usedFallback++;
        }
    }

    /// <summary>
    /// Updates the replacement text entries (<c>ActualText</c>, <c>Alt</c>, <c>E</c>) of a dictionary whose content held replaced
    /// matches: each match's old text is replaced by its new text where the entry holds it; an entry that holds none of them is removed
    /// and recorded. Returns whether the dictionary changed.
    /// </summary>
    public bool UpdateReplacementText(CosDictionary dictionary, IEnumerable<TextEditMatch> matches, CosReference? reference)
    {
        bool changed = false;
        foreach (CosName key in ReplacementKeys)
        {
            if (!dictionary.TryGetValue(key, out CosObject? value) || Document.Resolve(value) is not CosString text)
            {
                continue;
            }

            string current = text.DecodeText();
            bool found = true;
            foreach (TextEditMatch match in matches)
            {
                int at = current.IndexOf(match.Match.Text, StringComparison.Ordinal);
                if (at < 0)
                {
                    found = false;
                    break;
                }

                current = string.Concat(current.AsSpan(0, at), match.Replacement, current.AsSpan(at + match.Match.Text.Length));
            }

            if (found)
            {
                dictionary[key] = new CosString(TextStringEncoder.Encode(current));
            }
            else
            {
                dictionary.Remove(key);
                Report(
                    TextEditDiagnostics.TextEditReplacementTextRemoved,
                    $"The /{key.Value} entry of content whose text was replaced does not hold the old text; it is removed (ISO 32000-2 §14.9.4).",
                    reference);
            }

            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Copies a form XObject painted where located matches lie and rewrites the copy; returns the copy's new reference, or
    /// <see langword="null"/> when none of its glyphs changed (the copy is then dropped). Other pages and forms that paint the original keep
    /// it (copy on write).
    /// </summary>
    public CosReference? RewriteFormCopy(PdfFormXObject form, Matrix toPage)
    {
        if (FormDepth >= ContentOptions.MaxNestingDepth)
        {
            return null;
        }

        CosStream original = form.Stream;
        var dictionary = new CosDictionary();
        foreach (KeyValuePair<CosName, CosObject> pair in original.Dictionary)
        {
            if (Array.IndexOf(EncodingEntries, pair.Key) < 0)
            {
                dictionary[pair.Key] = pair.Value;
            }
        }

        var copy = new CosStream(dictionary, Document.DecodeStream(original).ToArray());
        var view = new PdfFormXObject(Document, copy, reference: null);
        var editor = new TextEditEditor(this);
        editor.Configure(dictionary, view.Resources, toPage);
        bool changed;
        FormDepth++;
        try
        {
            changed = ContentRewriting.RewriteForm(view, Document, editor, ContentOptions) && editor.ChangedGlyphs;
        }
        finally
        {
            FormDepth--;
        }

        return changed ? Document.AddObject(copy) : null;
    }

    private static TextEditRun Run(List<TextEditRun> runs, PdfCanvasFont? font)
    {
        if (runs.Count > 0 && ReferenceEquals(runs[^1].Fallback, font))
        {
            return runs[^1];
        }

        var run = new TextEditRun(font, []);
        runs.Add(run);
        return run;
    }

    private void NotEncodable(TextEditMatch match) =>
        Report(
            TextEditDiagnostics.TextEditNotEncodable,
            $"A replacement on page {Document.Pages.IndexOf(PageOf(match)) + 1} has a character no font tried can show, or would need a fallback font in vertical writing; the match is left as it was.",
            PageOf(match).Reference);

    private PdfPage PageOf(TextEditMatch match) => _pages.First(page => ReferenceEquals(page.Text, match.Text)).Page;

    private TextFontEncoder Encoder(PdfFont font)
    {
        if (!_encoders.TryGetValue(font, out TextFontEncoder? encoder))
        {
            encoder = new TextFontEncoder(font, _currentText);
            _encoders[font] = encoder;
        }

        return encoder;
    }

    /// <summary>The first font that has the character: the caller's, the font resolver's match for the original font, Helvetica.</summary>
    private PdfCanvasFont? FindFallback(PdfFont original, Rune rune)
    {
        if (Options.FallbackFont is { } given && ReferenceEquals(given.Document, Document) && given.TryGetGlyph(rune.Value, out _))
        {
            return given;
        }

        if (!_resolved.TryGetValue(original, out PdfCanvasFont? resolved))
        {
            resolved = Query(original) is { } query ? Document.EmbedFont(query) : null;
            _resolved[original] = resolved;
        }

        if (resolved is not null && resolved.TryGetGlyph(rune.Value, out _))
        {
            return resolved;
        }

        _helvetica ??= Document.GetStandard14Font(Standard14Font.Helvetica);
        return _helvetica.TryGetGlyph(rune.Value, out _) ? _helvetica : null;
    }

    /// <summary>The font resolver query for a font's family, weight and style, from its name and descriptor; none for a Type 3 font.</summary>
    private static FontQuery? Query(PdfFont font) => font switch
    {
        PdfSimpleFont simple when font.FontType != PdfFontType.Type3 => FontSubstitution.CreateQuery(simple, simple.Metrics),
        PdfType0Font { DescendantFont: { } descendant } => FontSubstitution.CreateQuery(descendant),
        _ => null,
    };

    /// <summary>
    /// Makes <paramref name="matches"/> the current page's: the glyphs of the located ones become the targets the editor looks for, and
    /// their bounds the area forms are rewritten in. Returns whether there is anything to look for.
    /// </summary>
    internal bool Prepare(List<TextEditMatch> matches)
    {
        _current = matches;
        _targets.Clear();
        double left = double.PositiveInfinity, bottom = double.PositiveInfinity, right = double.NegativeInfinity, top = double.NegativeInfinity;
        for (int index = 0; index < matches.Count; index++)
        {
            TextEditMatch match = matches[index];
            if (!match.Located)
            {
                continue;
            }

            foreach (GlyphMatching.GlyphKey key in match.Keys)
            {
                if (!_targets.TryGetValue(key, out Queue<int>? queue))
                {
                    queue = new Queue<int>();
                    _targets[key] = queue;
                }

                queue.Enqueue(index);
            }

            PdfRectangle bounds = match.Match.Bounds;
            left = Math.Min(left, bounds.Left);
            bottom = Math.Min(bottom, bounds.Bottom);
            right = Math.Max(right, bounds.Right);
            top = Math.Max(top, bounds.Top);
        }

        TargetBounds = _targets.Count == 0 ? default : new PdfRectangle(left - 1, bottom - 1, right + 1, top + 1);
        return _targets.Count > 0;
    }

    private void EditPage(PdfPage page, TextPage text, List<TextEditMatch> matches)
    {
        _currentText = text;
        _encoders.Clear();
        Locate(page, matches);
        foreach (TextEditMatch match in matches.Where(static match => !match.Located))
        {
            Report(
                TextEditDiagnostics.TextEditMatchNotLocated,
                $"A match on page {Document.Pages.IndexOf(page) + 1} could not be found again glyph by glyph in the content; it is left as it was.",
                page.Reference);
        }

        if (!Prepare(matches))
        {
            return;
        }

        TextEditEditor editor = _pageEditor ??= new TextEditEditor(this);
        editor.Configure(page.Dictionary, page.Resources);
        page.RewriteContent(editor, ContentOptions);
        _targets.Clear();

        foreach (TextEditMatch match in matches)
        {
            // Found by the locating run but not changed by the editor: inside a form it does not rewrite (nested too deeply, or outside
            // the matches' bounds), or in an operator it could not rebuild. A match whose replacement cannot be shown was recorded already.
            if (match.Located && !match.Replaced && (!match.Planned || match.Plan is not null))
            {
                Report(
                    TextEditDiagnostics.TextEditMatchNotLocated,
                    $"A match on page {Document.Pages.IndexOf(page) + 1} lies in content the edit cannot rewrite; it is left as it was.",
                    page.Reference);
            }
        }

        if (Options.UpdateReplacementText)
        {
            UpdateStructure(text, matches);
        }

        page.Dictionary.Remove(FileAndLayerNames.Thumb);
    }

    /// <summary>
    /// Runs the page's content once, forms entered, to find which matches have every glyph in it, counting each glyph identity as often
    /// as the content shows it; a match some of whose glyphs are missing is left alone rather than half replaced.
    /// </summary>
    private void Locate(PdfPage page, List<TextEditMatch> matches)
    {
        var wanted = new Dictionary<GlyphMatching.GlyphKey, int>();
        foreach (TextEditMatch match in matches)
        {
            foreach (GlyphMatching.GlyphKey key in match.Keys)
            {
                wanted[key] = 0;
            }
        }

        var locator = new TextLocator(wanted);
        ContentInterpreter.RunPage(page, Document, locator, ContentOptions);
        var used = new Dictionary<GlyphMatching.GlyphKey, int>();
        foreach (TextEditMatch match in matches)
        {
            var taken = new Dictionary<GlyphMatching.GlyphKey, int>();
            bool all = true;
            foreach (GlyphMatching.GlyphKey key in match.Keys)
            {
                int count = taken.GetValueOrDefault(key) + 1;
                if (wanted[key] - used.GetValueOrDefault(key) < count)
                {
                    all = false;
                    break;
                }

                taken[key] = count;
            }

            match.Located = all;
            if (all)
            {
                foreach ((GlyphMatching.GlyphKey key, int count) in taken)
                {
                    used[key] = used.GetValueOrDefault(key) + count;
                }
            }
        }
    }

    /// <summary>Updates the replacement text of the structure elements (and their ancestors) that hold replaced matches' glyphs (§14.9.4).</summary>
    private void UpdateStructure(TextPage text, List<TextEditMatch> matches)
    {
        var byElement = new Dictionary<CosDictionary, (PdfStructureElement Element, List<TextEditMatch> Matches)>(ReferenceEqualityComparer.Instance);
        foreach (TextEditMatch match in matches)
        {
            if (!match.Replaced)
            {
                continue;
            }

            var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
            for (int glyph = match.Match.GlyphStart; glyph < match.Match.GlyphStart + match.Match.GlyphCount && glyph < text.Glyphs.Count; glyph++)
            {
                PdfStructureElement? element = text.Glyphs[glyph].StructureElement;
                for (int depth = 0; element is not null && depth < 256 && seen.Add(element.Dictionary); depth++)
                {
                    if (ReplacementKeys.Any(element.Dictionary.ContainsKey))
                    {
                        if (!byElement.TryGetValue(element.Dictionary, out (PdfStructureElement Element, List<TextEditMatch> Matches) entry))
                        {
                            entry = (element, []);
                            byElement[element.Dictionary] = entry;
                        }

                        entry.Matches.Add(match);
                    }

                    element = element.Parent;
                }
            }
        }

        foreach ((PdfStructureElement element, List<TextEditMatch> held) in byElement.Values)
        {
            UpdateReplacementText(element.Dictionary, held, element.Reference);
        }
    }

    /// <summary>Counts, for each wanted glyph identity, how often the content shows it (forms entered).</summary>
    private sealed class TextLocator(Dictionary<GlyphMatching.GlyphKey, int> wanted) : ContentProcessor
    {
        private readonly Dictionary<PdfFont, (double Descent, double Ascent)> _extents = new(ReferenceEqualityComparer.Instance);

        public override ContentEvents Events => ContentEvents.Glyphs | ContentEvents.Forms | ContentEvents.HiddenContent;

        public override ContentVisit BeginForm(in FormEvent form, ContentContext context) => ContentVisit.Enter;

        public override void ShowGlyph(in GlyphEvent glyph, ContentContext context)
        {
            if (glyph.Font is not { } font)
            {
                return;
            }

            if (!_extents.TryGetValue(font, out (double Descent, double Ascent) extent))
            {
                extent = TextExtractor.ReadExtent(font);
                _extents[font] = extent;
            }

            GlyphMatching.GlyphKey key = GlyphMatching.Key(glyph.CharacterCode, (glyph.TextMatrix * glyph.Ctm).Transform(0, extent.Descent));
            if (wanted.TryGetValue(key, out int count))
            {
                wanted[key] = count + 1;
            }
        }
    }
}
