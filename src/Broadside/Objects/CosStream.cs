using Broadside.Caching;

namespace Broadside.Objects;

/// <summary>A stream object: a stream dictionary and a sequence of bytes, written <c>&lt;&lt;…&gt;&gt; stream … endstream</c>.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.3.8. The data is held as stored in the file, still encoded by the filters the dictionary's <c>Filter</c> entry
/// names (§7.4); decoding is not this type's job. The dictionary's <c>Length</c> entry is how a reader finds the end of the data
/// (§7.3.8.2); once the stream is read, the data's own length is authoritative, and a writer writes a direct <c>Length</c> equal to it.
/// </para>
/// <para>A stream is dirty when its data has been replaced or its dictionary is dirty.</para>
/// </remarks>
public sealed class CosStream : CosObject
{
    private ReadOnlyMemory<byte> _encodedData;
    private DeferredStreamData? _deferred;
    private bool _changed;

    /// <summary>Initializes a new instance of the <see cref="CosStream"/> class from its dictionary and data.</summary>
    /// <param name="dictionary">The stream dictionary. The stream holds this instance; it is not copied.</param>
    /// <param name="encodedData">The stream's data as stored in the file, encoded by the filters <paramref name="dictionary"/> names. Not copied.</param>
    public CosStream(CosDictionary dictionary, ReadOnlyMemory<byte> encodedData)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        Dictionary = dictionary;
        _encodedData = encodedData;
    }

    /// <summary>Initializes a new instance of the <see cref="CosStream"/> class whose data stays in the file until read.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <param name="encodedData">Where the data is; read on each access to <see cref="EncodedData"/>.</param>
    internal CosStream(CosDictionary dictionary, DeferredStreamData encodedData)
    {
        Dictionary = dictionary;
        _deferred = encodedData;
    }

    /// <summary>
    /// Gets a number that changes every time the data is replaced through the public API, and never by reading or loading. The
    /// dictionary has its own <see cref="CosDictionary.Version"/>.
    /// </summary>
    internal int Version { get; private set; }

    /// <summary>Gets the stream dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets or sets the stream's data as stored in the file, before any filter is decoded. Setting marks the stream dirty.</summary>
    /// <remarks>
    /// ISO 32000-2 §7.3.8.1. Setting the data does not change the dictionary's <c>Filter</c> entry; keep the two consistent. A stream
    /// of a document opened from a file or a stream keeps its data in the file and reads it on each get, so read it once per use.
    /// </remarks>
    public ReadOnlyMemory<byte> EncodedData
    {
        get => _deferred is { } deferred ? deferred.Read() : _encodedData;
        set
        {
            _encodedData = value;
            _deferred = null;
            _changed = true;
            Version++;
        }
    }

    /// <summary>
    /// Replaces the encoded data, as part of loading, with <paramref name="transform"/> of it, computed when the data is first read
    /// and kept (decryption, issue #45: loading a stream does not read or decrypt its data). Does not mark the stream dirty.
    /// </summary>
    /// <param name="transform">Turns the data as loaded into the data the stream holds; runs at most once.</param>
    internal void TransformLoadedData(Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> transform)
    {
        _deferred = new TransformedData(_deferred, _encodedData, transform);
        _encodedData = default;
    }

    /// <summary>
    /// Gets the length of <see cref="EncodedData"/> without reading it; for data transformed on first read and not read yet, the
    /// length before the transform (decryption only shortens the data).
    /// </summary>
    internal int EncodedLength => _deferred is { } deferred ? deferred.Length : _encodedData.Length;

    /// <inheritdoc/>
    public override bool IsDirty => _changed || Dictionary.IsDirty;

    /// <summary>Data computed from the loaded data on first read, once however many threads read it (<see cref="OnceCache{TKey, TValue}"/>).</summary>
    private sealed class TransformedData(DeferredStreamData? source, ReadOnlyMemory<byte> loaded, Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> transform)
        : DeferredStreamData
    {
        private readonly OnceCache<int, ReadOnlyMemory<byte>> _value = new();

        public override int Length => _value.TryGet(0, out ReadOnlyMemory<byte> value) ? value.Length : source?.Length ?? loaded.Length;

        public override ReadOnlyMemory<byte> Read() => _value.GetOrCreate(
            0,
            this,
            static (_, data) => new Created<ReadOnlyMemory<byte>>(data.Compute()),
            static (_, data) => data.Compute());

        private ReadOnlyMemory<byte> Compute() => transform(source?.Read() ?? loaded);
    }
}
