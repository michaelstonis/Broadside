namespace Broadside.Objects;

/// <summary>Creates the stream object for data the parser has located, so a file reader can keep the data in its source.</summary>
/// <remarks>
/// ISO 32000-2 §7.3.8.1 and §7.5.4: a stream's data is located in the file, not part of the object's syntax tree. Without a factory
/// the parser copies the data into the stream; a file reader instead refers to the bytes where they are (a slice of the caller's
/// memory, or a range read from the file on demand), so a cached stream object never holds its data (issue #45).
/// </remarks>
internal interface IStreamDataFactory
{
    /// <summary>Creates a stream over the data at <paramref name="start"/> in the parser's input.</summary>
    /// <param name="dictionary">The stream dictionary.</param>
    /// <param name="start">The offset of the first data byte in the parser's input.</param>
    /// <param name="length">The number of data bytes.</param>
    /// <returns>The stream.</returns>
    CosStream CreateStream(CosDictionary dictionary, int start, int length);
}

/// <summary>Stream data that stays in its source and is read on each access.</summary>
/// <remarks>
/// Lets a cached <see cref="CosStream"/> hold only its dictionary and the extent of its data, so a large file's streams cost no memory
/// until they are read, and none after. Reading is safe from several threads at once.
/// </remarks>
internal abstract class DeferredStreamData
{
    /// <summary>Gets the number of bytes.</summary>
    public abstract int Length { get; }

    /// <summary>Reads the bytes.</summary>
    /// <returns>The data; each call may return a new copy.</returns>
    public abstract ReadOnlyMemory<byte> Read();
}
