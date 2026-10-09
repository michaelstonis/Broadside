using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broadside.Tools.CorpusFetcher;

/// <summary>The contents of <c>corpora.json</c>: every corpus the fetcher knows about.</summary>
internal sealed class CorpusManifest
{
    /// <summary>Corpora in priority order.</summary>
    public required List<CorpusEntry> Corpora { get; init; }
}

/// <summary>How a corpus is obtained.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CorpusKind>))]
internal enum CorpusKind
{
    /// <summary>A git-hosted repository fetched as an archive pinned to a commit, from which only <see cref="CorpusEntry.Subpath"/> is kept.</summary>
    [JsonStringEnumMemberName("git-sparse")]
    GitSparse,

    /// <summary>One or more archives (zip or tar.gz) extracted as-is.</summary>
    [JsonStringEnumMemberName("zip")]
    Zip,

    /// <summary>Individual files downloaded one by one.</summary>
    [JsonStringEnumMemberName("files")]
    Files,
}

/// <summary>One corpus in the manifest.</summary>
internal sealed class CorpusEntry
{
    /// <summary>Short identifier; also the folder name under <c>corpus/</c>.</summary>
    public required string Id { get; init; }

    /// <summary>What the corpus is and why it is useful.</summary>
    public required string Description { get; init; }

    /// <summary>License or terms-of-use note. Read it before redistributing anything.</summary>
    public required string License { get; init; }

    /// <summary>How the corpus is fetched.</summary>
    public required CorpusKind Kind { get; init; }

    /// <summary>Download locations. For <see cref="CorpusKind.GitSparse"/> this is the archive of the pinned commit.</summary>
    public required List<Uri> Urls { get; init; }

    /// <summary>Human-readable home page of the corpus.</summary>
    public Uri? Homepage { get; init; }

    /// <summary>The commit SHA (or other immutable ref) the archive is pinned to.</summary>
    public string? Ref { get; init; }

    /// <summary>Number of leading path components to drop from every archive entry (GitHub archives wrap everything in <c>repo-sha/</c>).</summary>
    public int StripPrefix { get; init; }

    /// <summary>Only entries under this path (after prefix stripping) are extracted. <see langword="null"/> keeps everything.</summary>
    public string? Subpath { get; init; }

    /// <summary>Only files with one of these extensions (case-insensitive, with the dot) are kept. <see langword="null"/> keeps every extension.</summary>
    public List<string>? Extensions { get; init; }

    /// <summary>Approximate download and on-disk size, for humans.</summary>
    public required string ApproximateSize { get; init; }

    /// <summary>When <see langword="true"/> the fetcher never downloads this corpus; it prints the URL and the note instead.</summary>
    public bool Manual { get; init; }

    /// <summary>Anything else a user should know.</summary>
    public string? Notes { get; init; }
}

/// <summary>The contents of <c>corpus/manifest.lock.json</c>: what was actually downloaded.</summary>
internal sealed class LockFile
{
    /// <summary>Format version of the lock file.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Per-corpus state keyed by <see cref="CorpusEntry.Id"/>.</summary>
    public Dictionary<string, LockedCorpus> Corpora { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Lock state of one corpus.</summary>
internal sealed class LockedCorpus
{
    /// <summary>The pinned ref the files came from, if any.</summary>
    public string? Ref { get; set; }

    /// <summary>When the corpus was last fetched or verified.</summary>
    public DateTimeOffset FetchedAt { get; set; }

    /// <summary>Every file on disk, with its path relative to <c>corpus/</c> using forward slashes.</summary>
    public List<LockedFile> Files { get; init; } = [];

    /// <summary>Targets of <c>.link</c> files found in the archive, fetched only with <c>--include-linked</c>.</summary>
    public List<LinkedFile> Links { get; init; } = [];
}

/// <summary>One file on disk.</summary>
internal sealed class LockedFile
{
    /// <summary>Path relative to <c>corpus/</c>, forward slashes.</summary>
    public required string Path { get; init; }

    /// <summary>Size in bytes.</summary>
    public required long Size { get; init; }

    /// <summary>Lower-case hex SHA-256 of the contents.</summary>
    public required string Sha256 { get; init; }

    /// <summary>For files fetched from a <c>.link</c> target, the URL they came from.</summary>
    public Uri? Source { get; init; }
}

/// <summary>A <c>.link</c> file discovered in an archive: the path it stands for and the URL it points at.</summary>
internal sealed class LinkedFile
{
    /// <summary>Path relative to <c>corpus/</c> the download should land at.</summary>
    public required string Path { get; init; }

    /// <summary>Where the file lives.</summary>
    public required Uri Url { get; init; }
}

/// <summary>Source-generated JSON metadata for the manifest and lock file.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(CorpusManifest))]
[JsonSerializable(typeof(LockFile))]
internal sealed partial class CorpusJsonContext : JsonSerializerContext;
