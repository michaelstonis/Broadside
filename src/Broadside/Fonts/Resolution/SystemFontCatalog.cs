using System.Collections.Concurrent;

namespace Broadside.Fonts.Resolution;

/// <summary>
/// The installed fonts of a set of directories, read lazily: a directory listing up front, then the faces of only the files whose
/// names point at the families asked for, and every file only when a lookup over those finds nothing. Optionally kept in a store
/// so a later process reads no font file it has already read.
/// </summary>
/// <remarks>
/// <para>
/// A lookup is answered by <see cref="ForKeys"/>, an index over the candidate files of its keys
/// (<see cref="FontFileListing.Candidates"/>), and only when that finds nothing by <see cref="Full"/>, the index of every file. The
/// answer depends only on the files on disk, never on what was read before or stored, so it is the same in every process.
/// </para>
/// <para>
/// Safe for concurrent use: each file's faces are read at most once per catalog in the common case (two threads may both read a
/// file the first time; the first result is published and both are equal), published whole and never changed.
/// </para>
/// </remarks>
internal sealed class SystemFontCatalog
{
    /// <summary>The most candidate indexes remembered; further key sets are indexed per lookup.</summary>
    private const int MaxRemembered = 1024;

    private readonly IReadOnlyList<string> _directories;
    private readonly string? _storePath;
    private readonly SystemFontFace[]?[] _faces;
    private readonly Lazy<SystemFontIndex> _full;
    private readonly ConcurrentDictionary<string, SystemFontIndex> _byKeys = new(StringComparer.Ordinal);
    private readonly Lock _storeLock = new();
    private int _storeChanged;

    /// <summary>Initializes a new instance of the <see cref="SystemFontCatalog"/> class: lists the directories and reads the store.</summary>
    /// <param name="directories">The directories, in priority order.</param>
    /// <param name="storePath">The store's full path, or <see langword="null"/> to keep nothing.</param>
    public SystemFontCatalog(IReadOnlyList<string> directories, string? storePath)
    {
        _directories = directories;
        _storePath = storePath;
        Listing = FontFileListing.Create(directories, withStamps: storePath is not null);
        _faces = new SystemFontFace[]?[Listing.Files.Count];
        _full = new Lazy<SystemFontIndex>(() => SystemFontIndex.FromFaces([.. Enumerable.Range(0, _faces.Length).SelectMany(Faces)]), LazyThreadSafetyMode.ExecutionAndPublication);
        if (storePath is not null)
        {
            LoadStore(storePath);
        }
    }

    /// <summary>Gets the font files.</summary>
    public FontFileListing Listing { get; }

    /// <summary>Gets the index of every file, reading the files not read yet on first use.</summary>
    public SystemFontIndex Full => _full.Value;

    /// <summary>Gets the index of the files that may hold faces of the given family keys.</summary>
    /// <param name="keys">The family keys (and PostScript names reduced to keys) a lookup compares.</param>
    /// <returns>The index; its faces in scan order.</returns>
    public SystemFontIndex ForKeys(IReadOnlyCollection<string> keys)
    {
        string group = string.Join('|', keys.Where(key => key.Length > 0).Select(FontFileListing.Group).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        if (_byKeys.TryGetValue(group, out SystemFontIndex? index))
        {
            return index;
        }

        index = SystemFontIndex.FromFaces([.. Listing.Candidates(keys).SelectMany(Faces)]);
        if (_byKeys.Count < MaxRemembered)
        {
            index = _byKeys.GetOrAdd(group, index);
        }

        return index;
    }

    /// <summary>Writes the store when files were read that it does not hold (or it holds files that are gone); never throws.</summary>
    public void SaveIfChanged()
    {
        if (_storePath is null || Volatile.Read(ref _storeChanged) == 0)
        {
            return;
        }

        lock (_storeLock)
        {
            if (Interlocked.Exchange(ref _storeChanged, 0) == 0)
            {
                return;
            }

            // A store that cannot be written is not tried again until more files are read.
            _ = TrySave(_storePath);
        }
    }

    /// <summary>The faces of one listed file, read on first use.</summary>
    private SystemFontFace[] Faces(int index)
    {
        SystemFontFace[]? faces = Volatile.Read(ref _faces[index]);
        if (faces is not null)
        {
            return faces;
        }

        faces = [.. SystemFontIndex.ScanFile(Listing.Files[index].Path)];
        faces = Interlocked.CompareExchange(ref _faces[index], faces, null) ?? faces;
        if (_storePath is not null)
        {
            Volatile.Write(ref _storeChanged, 1);
        }

        return faces;
    }

    private void LoadStore(string path)
    {
        Dictionary<string, StoredFontFile>? stored = null;
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length <= 64 << 20)
            {
                stored = SystemFontIndexFile.Read(File.ReadAllBytes(path), _directories);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            stored = null;
        }

        int used = 0;
        if (stored is not null)
        {
            for (int index = 0; index < _faces.Length; index++)
            {
                FontFileEntry file = Listing.Files[index];
                if (stored.TryGetValue(file.Path, out StoredFontFile? entry) && entry.Length == file.Length && entry.WriteTime == file.WriteTime)
                {
                    _faces[index] = entry.Faces;
                    used++;
                }
            }
        }

        // A missing or unreadable store, or one holding files that are gone or changed, is written again.
        _storeChanged = stored is null || used != stored.Count ? 1 : 0;
    }

    private bool TrySave(string path)
    {
        string? temporary = null;
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var files = new List<(FontFileEntry File, SystemFontFace[] Faces)>();
            for (int index = 0; index < _faces.Length; index++)
            {
                if (Volatile.Read(ref _faces[index]) is { } faces)
                {
                    files.Add((Listing.Files[index], faces));
                }
            }

            // Write beside the store, then replace it in one step, so a reader in another process never sees half a store.
            temporary = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                SystemFontIndexFile.Write(stream, _directories, files);
            }

            File.Move(temporary, path, overwrite: true);
            temporary = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or ArgumentException)
        {
            return false;
        }
        finally
        {
            if (temporary is not null)
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Nothing more to do.
                }
            }
        }
    }
}
