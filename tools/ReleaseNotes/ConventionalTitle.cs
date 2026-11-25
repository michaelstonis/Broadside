using System.Text.RegularExpressions;

namespace Broadside.Tools.ReleaseNotes;

/// <summary>A Conventional Commits title, <c>type(scope)!: description</c>, and the breaking-change footer of its body.</summary>
public sealed partial record ConventionalTitle(string Type, string? Scope, bool Bang, string Description)
{
    /// <summary>Parses a commit or pull request title; <see langword="null"/> when it does not follow Conventional Commits.</summary>
    public static ConventionalTitle? TryParse(string title)
    {
        Match match = TitlePattern().Match(title.Trim());
        if (!match.Success)
        {
            return null;
        }

        string? scope = match.Groups["scope"].Success ? match.Groups["scope"].Value.Trim() : null;
        return new ConventionalTitle(match.Groups["type"].Value, string.IsNullOrEmpty(scope) ? null : scope, match.Groups["bang"].Success, match.Groups["description"].Value.Trim());
    }

    /// <summary>The text of a <c>BREAKING CHANGE:</c> (or <c>BREAKING-CHANGE:</c>) footer in a commit body, or <see langword="null"/>.</summary>
    public static string? BreakingFooter(string body)
    {
        Match match = BreakingPattern().Match(body);
        return match.Success ? match.Groups["text"].Value.Trim() : null;
    }

    [GeneratedRegex(@"^(?<type>[a-z]+)(\((?<scope>[^()]*)\))?(?<bang>!)?: (?<description>\S.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex TitlePattern();

    [GeneratedRegex(@"^BREAKING[ -]CHANGE: *(?<text>.+)$", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex BreakingPattern();
}
