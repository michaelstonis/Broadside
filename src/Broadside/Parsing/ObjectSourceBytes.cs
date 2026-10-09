namespace Broadside.Parsing;

/// <summary>
/// Where the bytes of an indirect object's value are in the source, as the loader read them: from the first token after
/// <c>N G obj</c> to the end of the value's last token (the <c>endstream</c> keyword for a stream), or, for an object-stream
/// member, the member's slice of the decoded object stream without surrounding white-space.
/// </summary>
/// <remarks>
/// ISO 32000-2 §7.3.10 and §7.5.7. Recorded only for objects read without any repair, so a writer may copy them unchanged instead of
/// re-serializing (issue #44): a literal string such as <c>(a\053b)</c> keeps its form, which signatures and byte-level diffs need.
/// </remarks>
internal readonly struct ObjectSourceBytes
{
    private ObjectSourceBytes(long fileOffset, int length, ReadOnlyMemory<byte> member)
    {
        FileOffset = fileOffset;
        Length = length;
        Member = member;
    }

    /// <summary>Gets the absolute offset of the first byte in the file, or -1 for an object-stream member.</summary>
    public long FileOffset { get; }

    /// <summary>Gets the number of bytes.</summary>
    public int Length { get; }

    /// <summary>Gets the bytes of an object-stream member, from its container's decoded data; empty for an object in the file body.</summary>
    public ReadOnlyMemory<byte> Member { get; }

    /// <summary>Gets a value indicating whether the bytes are in the file body rather than in a decoded object stream.</summary>
    public bool IsInFile => FileOffset >= 0;

    /// <summary>Describes bytes in the file body.</summary>
    /// <param name="offset">The absolute offset of the first byte.</param>
    /// <param name="length">The number of bytes.</param>
    /// <returns>The description.</returns>
    public static ObjectSourceBytes InFile(long offset, int length) => new(offset, length, ReadOnlyMemory<byte>.Empty);

    /// <summary>Describes the bytes of an object-stream member.</summary>
    /// <param name="bytes">The member's bytes in its container's decoded data.</param>
    /// <returns>The description.</returns>
    public static ObjectSourceBytes InObjectStream(ReadOnlyMemory<byte> bytes) => new(-1, bytes.Length, bytes);
}
