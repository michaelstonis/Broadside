using System.Buffers;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Broadside.Tools.CorpusFetcher;

/// <summary>Per-corpus outcome printed in the summary.</summary>
internal sealed class CorpusResult
{
    /// <summary>Corpus id.</summary>
    public required string Id { get; init; }

    /// <summary>Files written during this run.</summary>
    public int FilesWritten { get; set; }

    /// <summary>Bytes written during this run.</summary>
    public long BytesWritten { get; set; }

    /// <summary>Files already present with a matching hash.</summary>
    public int FilesSkipped { get; set; }

    /// <summary>Archive entries skipped by the subpath or extension filter.</summary>
    public int EntriesFiltered { get; set; }

    /// <summary>Archive entries rejected because their path was unsafe (absolute, escaping, invalid characters) or not a regular file.</summary>
    public int EntriesRejected { get; set; }

    /// <summary><c>.link</c> targets found in the archive.</summary>
    public int LinksFound { get; set; }

    /// <summary><c>.link</c> targets that failed to download.</summary>
    public int LinksFailed { get; set; }

    /// <summary>Set when the corpus is <see cref="CorpusEntry.Manual"/>.</summary>
    public bool Manual { get; init; }

    /// <summary>Set when the corpus could not be fetched.</summary>
    public string? Error { get; set; }
}

/// <summary>Downloads corpora described by a <see cref="CorpusManifest"/> into a corpus directory and maintains the lock file.</summary>
internal sealed class CorpusFetcher
{
    private const string LinkExtension = ".link";
    private const int MaxLinkFileBytes = 4096;
    private const int BufferSize = 1 << 16;

    private readonly string _corpusDirectory;
    private readonly string _tempDirectory;
    private readonly TextWriter _log;
    private readonly HttpClient _http;

    /// <param name="corpusDirectory">Root folder every corpus is written under.</param>
    /// <param name="http">Client used for every download; owned by the caller.</param>
    /// <param name="log">Progress output; owned by the caller.</param>
    public CorpusFetcher(string corpusDirectory, HttpClient http, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(corpusDirectory);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(log);

        _corpusDirectory = Path.GetFullPath(corpusDirectory);
        _tempDirectory = Path.Combine(_corpusDirectory, ".tmp");
        _http = http;
        _log = log;
    }

    /// <summary>Creates the handler the fetcher expects: redirects followed (GitHub archives bounce to codeload), no transparent decompression.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    };

    /// <summary>Creates the client the fetcher expects: no timeout (archives are large) and an identifying user agent. The caller owns both objects.</summary>
    public static HttpClient CreateHttpClient(SocketsHttpHandler handler)
    {
        var http = new HttpClient(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Broadside-CorpusFetcher/1.0 (+https://github.com/michaelstonis/Broadside)");
        return http;
    }

    /// <summary>Path of the lock file inside the corpus directory.</summary>
    public string LockFilePath => Path.Combine(_corpusDirectory, "manifest.lock.json");

    /// <summary>Fetches one corpus, updating <paramref name="lockFile"/> in place.</summary>
    public async Task<CorpusResult> FetchAsync(CorpusEntry entry, LockFile lockFile, bool includeLinked, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(lockFile);

        var result = new CorpusResult { Id = entry.Id, Manual = entry.Manual };
        if (entry.Manual)
        {
            await _log.WriteLineAsync($"[{entry.Id}] manual: not downloaded. {entry.ApproximateSize}");
            foreach (Uri url in entry.Urls)
            {
                await _log.WriteLineAsync($"[{entry.Id}]   {url}");
            }

            if (entry.Notes is not null)
            {
                await _log.WriteLineAsync($"[{entry.Id}]   {entry.Notes}");
            }

            return result;
        }

        try
        {
            Directory.CreateDirectory(_corpusDirectory);
            Directory.CreateDirectory(_tempDirectory);

            lockFile.Corpora.TryGetValue(entry.Id, out LockedCorpus? locked);
            if (locked is not null && locked.Ref == entry.Ref && await IsCompleteAsync(locked, cancellationToken))
            {
                result.FilesSkipped = locked.Files.Count(static f => f.Source is null);
                result.LinksFound = locked.Links.Count;
                await _log.WriteLineAsync($"[{entry.Id}] up to date ({result.FilesSkipped} files verified)");
            }
            else
            {
                locked = await FetchArchivesAsync(entry, locked, result, cancellationToken);
                lockFile.Corpora[entry.Id] = locked;
            }

            if (includeLinked && locked.Links.Count > 0)
            {
                await FetchLinkedAsync(entry, locked, result, cancellationToken);
            }

            locked.FetchedAt = DateTimeOffset.UtcNow;
        }
        catch (HttpRequestException ex)
        {
            result.Error = ex.Message;
        }
        catch (IOException ex)
        {
            result.Error = ex.Message;
        }
        catch (InvalidDataException ex)
        {
            result.Error = ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            result.Error = ex.Message;
        }
        finally
        {
            RemoveIfEmpty(_tempDirectory);
        }

        return result;
    }

    /// <summary>Every non-linked file in the lock exists on disk with the recorded size and hash.</summary>
    private async Task<bool> IsCompleteAsync(LockedCorpus locked, CancellationToken cancellationToken)
    {
        if (locked.Files.Count == 0)
        {
            return false;
        }

        foreach (LockedFile file in locked.Files)
        {
            if (file.Source is not null)
            {
                continue;
            }

            if (!await MatchesAsync(file, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> MatchesAsync(LockedFile file, CancellationToken cancellationToken)
    {
        string? path = SafePath.ResolveInside(_corpusDirectory, file.Path);
        if (path is null || !File.Exists(path))
        {
            return false;
        }

        var info = new FileInfo(path);
        if (info.Length != file.Size)
        {
            return false;
        }

        string hash = await HashFileAsync(path, cancellationToken);
        return string.Equals(hash, file.Sha256, StringComparison.Ordinal);
    }

    private async Task<LockedCorpus> FetchArchivesAsync(CorpusEntry entry, LockedCorpus? previous, CorpusResult result, CancellationToken cancellationToken)
    {
        var known = new Dictionary<string, LockedFile>(StringComparer.Ordinal);
        if (previous is not null)
        {
            foreach (LockedFile file in previous.Files)
            {
                known[file.Path] = file;
            }
        }

        var updated = new LockedCorpus { Ref = entry.Ref };
        string targetDirectory = Path.Combine(_corpusDirectory, entry.Id);
        Directory.CreateDirectory(targetDirectory);

        if (entry.Kind == CorpusKind.Files)
        {
            foreach (Uri url in entry.Urls)
            {
                string? fileName = SafePath.FileNameFromUrl(url);
                if (fileName is null)
                {
                    await _log.WriteLineAsync($"[{entry.Id}] rejected URL with no usable file name: {url}");
                    result.EntriesRejected++;
                    continue;
                }

                string relative = entry.Id + "/" + fileName;
                if (known.TryGetValue(relative, out LockedFile? existing) && await MatchesAsync(existing, cancellationToken))
                {
                    updated.Files.Add(existing);
                    result.FilesSkipped++;
                    continue;
                }

                string? destination = SafePath.ResolveInside(targetDirectory, fileName);
                if (destination is null)
                {
                    result.EntriesRejected++;
                    continue;
                }

                await _log.WriteLineAsync($"[{entry.Id}] downloading {url}");
                (long size, string sha256) = await DownloadToFileAsync(url, destination, cancellationToken);
                updated.Files.Add(new LockedFile { Path = relative, Size = size, Sha256 = sha256 });
                result.FilesWritten++;
                result.BytesWritten += size;
            }
        }
        else
        {
            int archiveIndex = 0;
            foreach (Uri url in entry.Urls)
            {
                string archivePath = Path.Combine(_tempDirectory, $"{entry.Id}-{archiveIndex++.ToString(CultureInfo.InvariantCulture)}{ArchiveSuffix(url)}");
                await _log.WriteLineAsync($"[{entry.Id}] downloading {url}");
                (long archiveBytes, _) = await DownloadToFileAsync(url, archivePath, cancellationToken);
                await _log.WriteLineAsync($"[{entry.Id}] downloaded {FormatBytes(archiveBytes)}, extracting");
                try
                {
                    if (IsTarGz(url))
                    {
                        await ExtractTarGzAsync(entry, archivePath, targetDirectory, known, updated, result, cancellationToken);
                    }
                    else
                    {
                        await ExtractZipAsync(entry, archivePath, targetDirectory, known, updated, result, cancellationToken);
                    }
                }
                finally
                {
                    File.Delete(archivePath);
                }
            }
        }

        // Keep linked files that were fetched earlier and are still intact; they are re-verified lazily.
        if (previous is not null)
        {
            foreach (LockedFile file in previous.Files)
            {
                if (file.Source is not null && updated.Links.Exists(l => string.Equals(l.Path, file.Path, StringComparison.Ordinal)))
                {
                    updated.Files.Add(file);
                }
            }
        }

        updated.Files.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
        updated.Links.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
        return updated;
    }

    private async Task ExtractZipAsync(CorpusEntry entry, string archivePath, string targetDirectory, Dictionary<string, LockedFile> known, LockedCorpus updated, CorpusResult result, CancellationToken cancellationToken)
    {
        using ZipArchive archive = await ZipFile.OpenReadAsync(archivePath, cancellationToken);
        foreach (ZipArchiveEntry zipEntry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EntryVerdict verdict = MapEntry(entry, zipEntry.FullName, out string relative);
            if (verdict != EntryVerdict.Keep)
            {
                Count(verdict, result);
                continue;
            }

            await using Stream source = await zipEntry.OpenAsync(cancellationToken);
            await PlaceEntryAsync(entry, relative, source, targetDirectory, known, updated, result, cancellationToken);
        }
    }

    private async Task ExtractTarGzAsync(CorpusEntry entry, string archivePath, string targetDirectory, Dictionary<string, LockedFile> known, LockedCorpus updated, CorpusResult result, CancellationToken cancellationToken)
    {
        await using FileStream file = OpenRead(archivePath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var reader = new TarReader(gzip, leaveOpen: true);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken) is { } tarEntry)
        {
            if (tarEntry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                // Directories, symlinks, hard links, devices: never materialized.
                result.EntriesRejected += tarEntry.EntryType is TarEntryType.Directory ? 0 : 1;
                continue;
            }

            EntryVerdict verdict = MapEntry(entry, tarEntry.Name, out string relative);
            if (verdict != EntryVerdict.Keep)
            {
                Count(verdict, result);
                continue;
            }

            Stream source = tarEntry.DataStream ?? Stream.Null;
            await PlaceEntryAsync(entry, relative, source, targetDirectory, known, updated, result, cancellationToken);
        }
    }

    /// <summary>Why an archive entry was not mapped to a file.</summary>
    private enum EntryVerdict
    {
        /// <summary>The entry maps to a file under the corpus folder.</summary>
        Keep,

        /// <summary>The entry is a directory marker.</summary>
        Directory,

        /// <summary>The entry is outside the subpath or has an excluded extension.</summary>
        Filtered,

        /// <summary>The entry name is unsafe.</summary>
        Unsafe,
    }

    /// <summary>Normalizes, strips the prefix, applies the subpath and extension filters. <paramref name="relative"/> is the path relative to the corpus folder when the verdict is <see cref="EntryVerdict.Keep"/>.</summary>
    private static EntryVerdict MapEntry(CorpusEntry entry, string rawName, out string relative)
    {
        relative = string.Empty;
        if (rawName.Length == 0 || rawName.EndsWith('/') || rawName.EndsWith('\\'))
        {
            return EntryVerdict.Directory;
        }

        string? normalized = SafePath.NormalizeEntryName(rawName);
        if (normalized is null)
        {
            return EntryVerdict.Unsafe;
        }

        string? stripped = SafePath.StripLeadingComponents(normalized, entry.StripPrefix);
        if (stripped is null)
        {
            return EntryVerdict.Filtered;
        }

        string? underSubpath = SafePath.RelativeToSubpath(stripped, entry.Subpath);
        if (underSubpath is null)
        {
            return EntryVerdict.Filtered;
        }

        if (entry.Extensions is { Count: > 0 } extensions)
        {
            string extension = Path.GetExtension(underSubpath);
            bool allowed = extensions.Exists(e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase));
            if (!allowed)
            {
                return EntryVerdict.Filtered;
            }
        }

        relative = underSubpath;
        return EntryVerdict.Keep;
    }

    private static void Count(EntryVerdict verdict, CorpusResult result)
    {
        switch (verdict)
        {
            case EntryVerdict.Filtered:
                result.EntriesFiltered++;
                break;
            case EntryVerdict.Unsafe:
                result.EntriesRejected++;
                break;
        }
    }

    private async Task PlaceEntryAsync(CorpusEntry entry, string relative, Stream source, string targetDirectory, Dictionary<string, LockedFile> known, LockedCorpus updated, CorpusResult result, CancellationToken cancellationToken)
    {
        if (relative.EndsWith(LinkExtension, StringComparison.OrdinalIgnoreCase))
        {
            await RecordLinkAsync(entry, relative, source, updated, result, cancellationToken);
            return;
        }

        string lockPath = entry.Id + "/" + relative;
        string? destination = SafePath.ResolveInside(targetDirectory, relative);
        if (destination is null)
        {
            await _log.WriteLineAsync($"[{entry.Id}] rejected entry escaping the corpus folder: {relative}");
            result.EntriesRejected++;
            return;
        }

        if (known.TryGetValue(lockPath, out LockedFile? existing) && existing.Source is null && await MatchesAsync(existing, cancellationToken))
        {
            updated.Files.Add(existing);
            result.FilesSkipped++;
            return;
        }

        (long size, string sha256) = await WriteWithHashAsync(source, destination, cancellationToken);
        updated.Files.Add(new LockedFile { Path = lockPath, Size = size, Sha256 = sha256 });
        result.FilesWritten++;
        result.BytesWritten += size;
    }

    private async Task RecordLinkAsync(CorpusEntry entry, string relative, Stream source, LockedCorpus updated, CorpusResult result, CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxLinkFileBytes + 1);
        try
        {
            int total = 0;
            int read;
            while (total <= MaxLinkFileBytes && (read = await source.ReadAsync(buffer.AsMemory(total, (MaxLinkFileBytes + 1) - total), cancellationToken)) > 0)
            {
                total += read;
            }

            if (total > MaxLinkFileBytes)
            {
                await _log.WriteLineAsync($"[{entry.Id}] ignored oversized link file: {relative}");
                result.EntriesRejected++;
                return;
            }

            string text = Encoding.UTF8.GetString(buffer, 0, total);
            string firstLine = text.Split('\n', 2)[0].Trim();
            string targetRelative = relative[..^LinkExtension.Length];
            if (!Uri.TryCreate(firstLine, UriKind.Absolute, out Uri? url)
                || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
                || Path.GetFileName(targetRelative).Length == 0)
            {
                await _log.WriteLineAsync($"[{entry.Id}] ignored link file without an http(s) URL: {relative}");
                result.EntriesRejected++;
                return;
            }

            updated.Links.Add(new LinkedFile { Path = entry.Id + "/" + targetRelative, Url = url });
            result.LinksFound++;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task FetchLinkedAsync(CorpusEntry entry, LockedCorpus locked, CorpusResult result, CancellationToken cancellationToken)
    {
        var byPath = new Dictionary<string, LockedFile>(StringComparer.Ordinal);
        foreach (LockedFile file in locked.Files)
        {
            byPath[file.Path] = file;
        }

        foreach (LinkedFile link in locked.Links)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (byPath.TryGetValue(link.Path, out LockedFile? existing) && existing.Source is not null && await MatchesAsync(existing, cancellationToken))
            {
                result.FilesSkipped++;
                continue;
            }

            string? destination = SafePath.ResolveInside(_corpusDirectory, link.Path);
            if (destination is null)
            {
                result.EntriesRejected++;
                continue;
            }

            await _log.WriteLineAsync($"[{entry.Id}] downloading linked {link.Url}");
            try
            {
                (long size, string sha256) = await DownloadToFileAsync(link.Url, destination, cancellationToken);
                var fetched = new LockedFile { Path = link.Path, Size = size, Sha256 = sha256, Source = link.Url };
                locked.Files.RemoveAll(f => string.Equals(f.Path, link.Path, StringComparison.Ordinal));
                locked.Files.Add(fetched);
                result.FilesWritten++;
                result.BytesWritten += size;
            }
            catch (HttpRequestException ex)
            {
                await _log.WriteLineAsync($"[{entry.Id}] link failed: {link.Url} ({ex.Message})");
                result.LinksFailed++;
            }
            catch (IOException ex)
            {
                await _log.WriteLineAsync($"[{entry.Id}] link failed: {link.Url} ({ex.Message})");
                result.LinksFailed++;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await _log.WriteLineAsync($"[{entry.Id}] link timed out: {link.Url}");
                result.LinksFailed++;
            }
        }

        locked.Files.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));
    }

    private async Task<(long Size, string Sha256)> DownloadToFileAsync(Uri url, string destination, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await WriteWithHashAsync(body, destination, cancellationToken);
    }

    /// <summary>Streams <paramref name="source"/> to a <c>.partial</c> file next to <paramref name="destination"/>, hashing as it goes, then moves it into place.</summary>
    private static async Task<(long Size, string Sha256)> WriteWithHashAsync(Stream source, string destination, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(destination);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        string partial = destination + ".partial";
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await using (FileStream output = new(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    total += read;
                }
            }

            File.Move(partial, destination, overwrite: true);
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return (total, Convert.ToHexStringLower(hasher.GetHashAndReset()));
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static void RemoveIfEmpty(string directory)
    {
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static FileStream OpenRead(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static bool IsTarGz(Uri url)
    {
        string path = url.AbsolutePath;
        return path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
    }

    private static string ArchiveSuffix(Uri url) => IsTarGz(url) ? ".tar.gz" : ".zip";

    /// <summary>Formats a byte count as KB/MB/GB with one decimal.</summary>
    public static string FormatBytes(long bytes)
    {
        const double kilo = 1024;
        return bytes switch
        {
            < 1024 => bytes.ToString(CultureInfo.InvariantCulture) + " B",
            < 1024 * 1024 => (bytes / kilo).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
            < 1024L * 1024 * 1024 => (bytes / kilo / kilo).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
            _ => (bytes / kilo / kilo / kilo).ToString("0.00", CultureInfo.InvariantCulture) + " GB",
        };
    }
}
