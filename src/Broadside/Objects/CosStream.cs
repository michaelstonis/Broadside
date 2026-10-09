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

    /// <summary>Gets the stream dictionary.</summary>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets or sets the stream's data as stored in the file, before any filter is decoded. Setting marks the stream dirty.</summary>
    /// <remarks>ISO 32000-2 §7.3.8.1. Setting the data does not change the dictionary's <c>Filter</c> entry; keep the two consistent.</remarks>
    public ReadOnlyMemory<byte> EncodedData
    {
        get => _encodedData;
        set
        {
            _encodedData = value;
            _changed = true;
        }
    }

    /// <inheritdoc/>
    public override bool IsDirty => _changed || Dictionary.IsDirty;
}
