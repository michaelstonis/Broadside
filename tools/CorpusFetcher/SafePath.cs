namespace Broadside.Tools.CorpusFetcher;

/// <summary>
/// Path hygiene for untrusted archive entries. Archives are downloaded from the internet, so every entry name is
/// treated as hostile until it has been normalized and proven to stay inside the target directory.
/// </summary>
internal static class SafePath
{
    private static readonly char[] InvalidNameChars = BuildInvalidNameChars();

    /// <summary>
    /// Normalizes an archive entry name to forward-slash relative form, or returns <see langword="null"/> when the entry
    /// is a directory, is absolute, contains <c>.</c> or <c>..</c> components, a drive or stream separator, or characters
    /// that are not valid in file names.
    /// </summary>
    public static string? NormalizeEntryName(string rawName)
    {
        ArgumentNullException.ThrowIfNull(rawName);

        string name = rawName.Replace('\\', '/');
        if (name.Length == 0 || name.EndsWith('/') || name.StartsWith('/') || name.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        string[] parts = name.Split('/');
        foreach (string part in parts)
        {
            if (part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(InvalidNameChars) >= 0)
            {
                return null;
            }
        }

        return string.Join('/', parts);
    }

    /// <summary>Drops <paramref name="count"/> leading components, or returns <see langword="null"/> when there are not enough.</summary>
    public static string? StripLeadingComponents(string relativePath, int count)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        string remaining = relativePath;
        for (int i = 0; i < count; i++)
        {
            int slash = remaining.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
            {
                return null;
            }

            remaining = remaining[(slash + 1)..];
        }

        return remaining.Length == 0 ? null : remaining;
    }

    /// <summary>Returns the part of <paramref name="relativePath"/> below <paramref name="subpath"/>, or <see langword="null"/> when it is not under it.</summary>
    public static string? RelativeToSubpath(string relativePath, string? subpath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);

        if (string.IsNullOrEmpty(subpath))
        {
            return relativePath;
        }

        string prefix = subpath.TrimEnd('/') + "/";
        return relativePath.StartsWith(prefix, StringComparison.Ordinal) && relativePath.Length > prefix.Length
            ? relativePath[prefix.Length..]
            : null;
    }

    /// <summary>
    /// Resolves <paramref name="relativePath"/> (forward slashes, already normalized) under <paramref name="targetDirectory"/>
    /// and proves the result stays inside it. Returns <see langword="null"/> when it escapes.
    /// </summary>
    public static string? ResolveInside(string targetDirectory, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(targetDirectory);
        ArgumentNullException.ThrowIfNull(relativePath);

        string root = Path.GetFullPath(targetDirectory);
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? candidate : null;
    }

    /// <summary>Takes the last path segment of a URL, percent-decoded, as a safe file name; <see langword="null"/> when it is unusable.</summary>
    public static string? FileNameFromUrl(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);

        string lastSegment = url.Segments.Length > 0 ? url.Segments[^1] : string.Empty;
        string decoded = Uri.UnescapeDataString(lastSegment);
        string? normalized = NormalizeEntryName(decoded);
        return normalized is null || normalized.Contains('/', StringComparison.Ordinal) ? null : normalized;
    }

    private static char[] BuildInvalidNameChars()
    {
        // Union of the platform's invalid chars with the ones Windows rejects, so a corpus fetched on macOS or Linux
        // still has names that work when the folder is copied to Windows.
        HashSet<char> chars = [.. Path.GetInvalidFileNameChars(), '<', '>', '"', '|', '?', '*'];
        for (int i = 0; i < 32; i++)
        {
            chars.Add((char)i);
        }

        return [.. chars];
    }
}
