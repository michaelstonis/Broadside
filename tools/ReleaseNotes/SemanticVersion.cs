using System.Globalization;

namespace Broadside.Tools.ReleaseNotes;

/// <summary>A SemVer 2.0 version, ordered by precedence (build metadata ignored), so that <c>0.1.0-preview.10</c> follows <c>0.1.0-preview.9</c>.</summary>
internal sealed class SemanticVersion : IComparable<SemanticVersion>
{
    private SemanticVersion(int major, int minor, int patch, string[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> PreRelease { get; }

    public static SemanticVersion? TryParse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string core = text.Split('+', 2)[0];
        string[] parts = core.Split('-', 2);
        string[] numbers = parts[0].Split('.');
        if (numbers.Length != 3
            || !int.TryParse(numbers[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(numbers[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
            || !int.TryParse(numbers[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
        {
            return null;
        }

        string[] preRelease = parts.Length > 1 ? parts[1].Split('.') : [];
        return preRelease.Any(p => p.Length == 0) ? null : new SemanticVersion(major, minor, patch, preRelease);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);
        if (result == 0)
        {
            result = Minor.CompareTo(other.Minor);
        }

        if (result == 0)
        {
            result = Patch.CompareTo(other.Patch);
        }

        if (result != 0)
        {
            return result;
        }

        // A release follows its pre-releases.
        if (PreRelease.Count == 0 || other.PreRelease.Count == 0)
        {
            return other.PreRelease.Count.CompareTo(PreRelease.Count) switch { < 0 => -1, > 0 => 1, _ => 0 };
        }

        for (int i = 0; i < Math.Min(PreRelease.Count, other.PreRelease.Count); i++)
        {
            bool leftNumeric = long.TryParse(PreRelease[i], NumberStyles.None, CultureInfo.InvariantCulture, out long left);
            bool rightNumeric = long.TryParse(other.PreRelease[i], NumberStyles.None, CultureInfo.InvariantCulture, out long right);
            result = (leftNumeric, rightNumeric) switch
            {
                (true, true) => left.CompareTo(right),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(PreRelease[i], other.PreRelease[i]),
            };
            if (result != 0)
            {
                return result;
            }
        }

        return PreRelease.Count.CompareTo(other.PreRelease.Count);
    }
}
