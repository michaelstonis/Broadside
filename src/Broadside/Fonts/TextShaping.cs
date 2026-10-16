using System.Collections.ObjectModel;

namespace Broadside.Fonts;

/// <summary>
/// How text is shaped into glyphs before it is shown: the OpenType script and language system whose features apply, the run's
/// direction, and which features are on. Immutable; each <c>With*</c> method returns a changed copy. Two instances with the same
/// settings are equal.
/// </summary>
/// <remarks>
/// <para>
/// OpenType specification, "GSUB — Glyph Substitution Table", "GPOS — Glyph Positioning Table", "kern" and "OpenType Layout Common Table Formats" (ScriptList, LangSys,
/// FeatureList), and the Feature Tags registry. Shaping applies the font's <c>GSUB</c> lookups for the default features
/// <c>ccmp</c>, <c>locl</c>, <c>rlig</c>, <c>rclt</c>, <c>calt</c>, <c>liga</c> and <c>clig</c>, and, for Arabic, the joining
/// forms <c>isol</c>, <c>fina</c>, <c>medi</c> and <c>init</c> chosen by the Unicode cursive joining rules; <c>kern</c> (also on
/// by default) adds the font's pair kerning (its <c>GPOS</c> pair adjustments, else its <c>kern</c> table), and the <c>GPOS</c>
/// features <c>mark</c>, <c>mkmk</c>, <c>curs</c>, <c>dist</c>, <c>abvm</c> and <c>blwm</c> (on by default) place marks on their
/// bases and join cursive glyphs. <see cref="WithFeature"/> turns any of these off or another feature (such as
/// <c>smcp</c>, <c>dlig</c> or <c>salt</c>, taking its first alternate) on.
/// </para>
/// <para>
/// The text of a run is one script in one direction: a right-to-left run (Arabic, Hebrew) is shaped in logical order and its glyphs
/// are shown in visual order, right to left. Splitting mixed text into runs is bidirectional layout's job (issue #173).
/// Indic, Southeast Asian and the other complex scripts are not shaped beyond the default features (issue #230).
/// </para>
/// </remarks>
public sealed class TextShaping : IEquatable<TextShaping>
{
    private static readonly string[] DefaultFeatures = ["ccmp", "locl", "rlig", "rclt", "calt", "liga", "clig", "kern", "isol", "fina", "medi", "init", "mark", "mkmk", "curs", "dist", "abvm", "blwm"];

    private readonly SortedDictionary<string, bool> _features;
    private readonly int _hash;

    private TextShaping(string? script, string? language, bool? rightToLeft, SortedDictionary<string, bool> features)
    {
        Script = script;
        Language = language;
        RightToLeft = rightToLeft;
        _features = features;
        Features = new ReadOnlyDictionary<string, bool>(features);
        var hash = new HashCode();
        hash.Add(script, StringComparer.Ordinal);
        hash.Add(language, StringComparer.Ordinal);
        hash.Add(rightToLeft);
        foreach ((string tag, bool enabled) in features)
        {
            hash.Add(tag, StringComparer.Ordinal);
            hash.Add(enabled);
        }

        _hash = hash.ToHashCode();
    }

    /// <summary>Gets the default shaping: the script found from the text, the default language system, the default features.</summary>
    public static TextShaping Default { get; } = new(null, null, null, new SortedDictionary<string, bool>(StringComparer.Ordinal));

    /// <summary>
    /// Gets the OpenType script tag whose features apply (such as <c>latn</c>, <c>arab</c> or <c>hebr</c>), or
    /// <see langword="null"/> to find it from the first letter of the text (Latin, Greek, Cyrillic, Arabic and Hebrew are recognised;
    /// other text uses the font's default script).
    /// </summary>
    public string? Script { get; }

    /// <summary>Gets the OpenType language system tag (such as <c>TRK</c> or <c>URD</c>), or <see langword="null"/> for the script's default language system.</summary>
    public string? Language { get; }

    /// <summary>
    /// Gets whether the run is right to left (glyphs in reverse order, mirrored characters shown mirrored, UAX #9 rules L2 and L4),
    /// or <see langword="null"/> for the script's direction (Arabic and Hebrew are right to left).
    /// </summary>
    public bool? RightToLeft { get; }

    /// <summary>Gets the features turned on or off explicitly, by tag; the others keep their default.</summary>
    public IReadOnlyDictionary<string, bool> Features { get; }

    /// <summary>Returns a copy that applies the features of another script.</summary>
    /// <param name="script">An OpenType script tag of one to four printable ASCII characters, or <see langword="null"/> to find the script from the text.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="script"/> is not a valid tag.</exception>
    public TextShaping WithScript(string? script) => new(script is null ? null : Validate(script, nameof(script)), Language, RightToLeft, _features);

    /// <summary>Returns a copy that applies the features of a language system of the script.</summary>
    /// <param name="language">An OpenType language system tag of one to four printable ASCII characters, or <see langword="null"/> for the default.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="language"/> is not a valid tag.</exception>
    public TextShaping WithLanguage(string? language) => new(Script, language is null ? null : Validate(language, nameof(language)), RightToLeft, _features);

    /// <summary>Returns a copy with an explicit direction.</summary>
    /// <param name="rightToLeft">Whether the run is right to left, or <see langword="null"/> for the script's direction.</param>
    /// <returns>The changed copy.</returns>
    public TextShaping WithRightToLeft(bool? rightToLeft) => new(Script, Language, rightToLeft, _features);

    /// <summary>Returns a copy with a feature turned on or off.</summary>
    /// <param name="feature">An OpenType feature tag of one to four printable ASCII characters (such as <c>liga</c> or <c>smcp</c>).</param>
    /// <param name="enabled">Whether the feature applies.</param>
    /// <returns>The changed copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="feature"/> is not a valid tag.</exception>
    public TextShaping WithFeature(string feature, bool enabled)
    {
        string tag = Validate(feature, nameof(feature));
        var features = new SortedDictionary<string, bool>(_features, StringComparer.Ordinal) { [tag] = enabled };
        return new TextShaping(Script, Language, RightToLeft, features);
    }

    /// <summary>Gets a value indicating whether a feature applies: its explicit setting, else whether it is on by default.</summary>
    /// <param name="feature">The feature tag.</param>
    /// <returns><see langword="true"/> when shaping applies the feature.</returns>
    public bool IsEnabled(string feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return _features.TryGetValue(feature.PadRight(4), out bool enabled) ? enabled : Array.IndexOf(DefaultFeatures, feature) >= 0;
    }

    /// <inheritdoc/>
    public bool Equals(TextShaping? other) =>
        ReferenceEquals(this, other) || (other is not null && _hash == other._hash && Script == other.Script && Language == other.Language && RightToLeft == other.RightToLeft
        && _features.Count == other._features.Count && _features.SequenceEqual(other._features));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TextShaping);

    /// <inheritdoc/>
    public override int GetHashCode() => _hash;

    /// <summary>The tags of the features that apply when they are on by default or explicitly.</summary>
    internal IEnumerable<string> ExplicitlyEnabled() => _features.Where(pair => pair.Value).Select(pair => pair.Key);

    private static string Validate(string tag, string parameter)
    {
        ArgumentNullException.ThrowIfNull(tag, parameter);
        if (tag.Length is 0 or > 4 || tag.Any(character => character is < ' ' or > '~'))
        {
            throw new ArgumentException($"'{tag}' is not an OpenType tag: one to four printable ASCII characters (OpenType specification, \"Tag\" data type).", parameter);
        }

        return tag.PadRight(4);
    }
}
