namespace Broadside.Tools.ReleaseNotes;

/// <summary>Adds a release's notes to CHANGELOG.md: newest first; a release (or the Unreleased section) written again is replaced.</summary>
public static class Changelog
{
    public const string DefaultHeader =
        "# Changelog\n\n" +
        "Every release of the Broadside packages, newest first. Each section is written by `tools/ReleaseNotes` from the Conventional Commit " +
        "titles merged since the previous tag (see the [versioning policy](https://michaelstonis.github.io/Broadside/articles/versioning/)). " +
        "The GitHub release of each tag carries the same notes and the packages.\n\n";

    /// <summary>Returns <paramref name="existing"/> with <paramref name="section"/> (which starts with a <c>## version</c> heading) added.</summary>
    public static string Update(string existing, string section)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(section);
        string text = existing.Replace("\r\n", "\n", StringComparison.Ordinal);
        string newSection = section.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() + "\n";
        string version = HeadingVersion(newSection.Split('\n', 2)[0]) ?? throw new ArgumentException("The section must start with a '## <version>' heading.", nameof(section));

        if (text.Trim().Length == 0)
        {
            text = DefaultHeader;
        }

        int first = FindHeading(text, 0);
        string header = first < 0 ? text : text[..first];
        var sections = new List<string>();
        for (int start = first; start >= 0;)
        {
            int next = FindHeading(text, start + 1);
            sections.Add(text[start..(next < 0 ? text.Length : next)].Trim() + "\n");
            start = next;
        }

        sections.RemoveAll(s =>
        {
            string? existingVersion = HeadingVersion(s.Split('\n', 2)[0]);
            return existingVersion == version || existingVersion == "Unreleased";
        });
        sections.Insert(0, newSection);
        return header.TrimEnd() + "\n\n" + string.Join("\n", sections);
    }

    private static int FindHeading(string text, int from)
    {
        if (from == 0 && text.StartsWith("## ", StringComparison.Ordinal))
        {
            return 0;
        }

        int index = text.IndexOf("\n## ", Math.Max(0, from - 1), StringComparison.Ordinal);
        return index < 0 ? -1 : index + 1;
    }

    private static string? HeadingVersion(string line) =>
        line.StartsWith("## ", StringComparison.Ordinal) ? line[3..].Split(' ', 2)[0] : null;
}
