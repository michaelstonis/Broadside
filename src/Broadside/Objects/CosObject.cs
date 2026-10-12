using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Broadside.Objects;

/// <summary>
/// One COS object: a value exactly as a PDF file stores it. The hierarchy is closed; every object is one of
/// <see cref="CosBoolean"/>, <see cref="CosInteger"/>, <see cref="CosReal"/>, <see cref="CosString"/>, <see cref="CosName"/>,
/// <see cref="CosArray"/>, <see cref="CosDictionary"/>, <see cref="CosStream"/>, <see cref="CosNull"/> or <see cref="CosReference"/>.
/// </summary>
/// <remarks>
/// <para>ISO 32000-2 §7.3.</para>
/// <para>
/// <b>Mutability.</b> Booleans, numbers, strings, names, null and references are immutable values. Arrays, dictionaries and
/// streams are mutable containers.
/// </para>
/// <para>
/// <b>Dirty tracking.</b> <see cref="IsDirty"/> is <see langword="false"/> for every object a parser produces and for every newly
/// constructed object, and becomes <see langword="true"/> once the object, or any direct object it contains, is changed through the
/// public API. Immutable objects are never dirty; replacing one inside a container makes the container dirty. Indirect references
/// are not followed: a change to the object a <see cref="CosReference"/> points to does not make the referring container dirty.
/// </para>
/// <para>
/// <b>Equality.</b> <see cref="object.Equals(object)"/> is value equality for the immutable objects and reference identity for the
/// mutable containers, so a container is safe as a hash key while it changes. <see cref="DeepEquals"/> compares structure, and is
/// the equality a serialize-then-parse round trip preserves.
/// </para>
/// </remarks>
public abstract class CosObject
{
    private protected CosObject()
    {
    }

    /// <summary>
    /// Gets a value indicating whether this object, or a direct object it contains, has been changed through the public API since it
    /// was parsed or constructed. Always <see langword="false"/> for immutable objects.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.6: the basis for writing an incremental update.</remarks>
    public virtual bool IsDirty => false;

    /// <summary>Parses exactly one COS object from <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The object's syntax, optionally surrounded by white-space and comments.</param>
    /// <returns>The parsed object. Parsed objects are not dirty.</returns>
    /// <exception cref="FormatException">
    /// The bytes are empty, hold more than one object, or deviate from the syntax of ISO 32000-2 §7.2 and §7.3 in any way that a
    /// reader would have to repair. Standalone parsing is strict; lenient reading with diagnostics happens when a document is read.
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §7.2 and §7.3. A stream (a dictionary followed by <c>stream</c> … <c>endstream</c>) takes its data extent from a
    /// direct <c>Length</c> entry; when <c>Length</c> is an indirect reference, the data ends at the <c>endstream</c> keyword.
    /// The <c>N G obj</c> … <c>endobj</c> wrapper of an indirect object definition is file structure (§7.3.10) and is not accepted here.
    /// </remarks>
    public static CosObject Parse(ReadOnlySpan<byte> bytes)
    {
        if (TryParseCore(bytes, out CosObject? result, out CosRepair? firstRepair))
        {
            return result;
        }

        throw new FormatException(firstRepair!.Value.ToString());
    }

    /// <summary>Tries to parse exactly one COS object from <paramref name="bytes"/>, under the same rules as <see cref="Parse"/>.</summary>
    /// <param name="bytes">The object's syntax, optionally surrounded by white-space and comments.</param>
    /// <param name="result">The parsed object, or <see langword="null"/> when the bytes are not exactly one well-formed object.</param>
    /// <returns><see langword="true"/> when the bytes hold exactly one well-formed object.</returns>
    /// <remarks>ISO 32000-2 §7.2 and §7.3.</remarks>
    public static bool TryParse(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out CosObject? result) =>
        TryParseCore(bytes, out result, out _);

    /// <summary>
    /// Compares two objects by structure: the same kind of object with the same value, recursively for containers.
    /// </summary>
    /// <param name="left">The first object, or <see langword="null"/>.</param>
    /// <param name="right">The second object, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the two objects are structurally equal.</returns>
    /// <remarks>
    /// <para>ISO 32000-2 §7.3. The rules:</para>
    /// <list type="bullet">
    /// <item>The kinds must match: the integer <c>1</c> is not equal to the real <c>1.0</c>.</item>
    /// <item>Strings compare their bytes; whether a string was written in literal or hexadecimal form does not matter (§7.3.4).</item>
    /// <item>Names compare their bytes after <c>#xx</c> escapes are expanded (§7.3.5).</item>
    /// <item>Arrays compare element by element, in order.</item>
    /// <item>Dictionaries compare their entries regardless of order (§7.3.7). A dictionary never holds a null value (see <see cref="CosDictionary"/>).</item>
    /// <item>Streams compare their dictionaries, ignoring the <c>Length</c> entry, and their encoded data byte for byte; <c>Length</c> is a property of the serialization, not of the stream (§7.3.8).</item>
    /// <item>References compare object and generation numbers; the objects they point to are not compared (§7.3.10).</item>
    /// <item>Two <see langword="null"/> arguments are equal; <see langword="null"/> never equals an object, not even <see cref="CosNull.Instance"/>.</item>
    /// </list>
    /// </remarks>
    public static bool DeepEquals(CosObject? left, CosObject? right) => CosEquality.DeepEquals(left, right);

    /// <summary>Writes this object's PDF syntax to <paramref name="writer"/>.</summary>
    /// <param name="writer">The destination.</param>
    /// <remarks>
    /// ISO 32000-2 §7.2 and §7.3. The output parses back, through <see cref="Parse"/>, to an object that <see cref="DeepEquals"/> this
    /// one. A stream is written with a direct <c>Length</c> equal to its encoded data length.
    /// </remarks>
    public void WriteTo(IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        CosWriter.Write(this, writer);
    }

    /// <summary>Returns this object's PDF syntax, as <see cref="WriteTo"/> writes it, with each byte shown as its Latin-1 character.</summary>
    /// <returns>The PDF syntax of this object.</returns>
    public override string ToString()
    {
        var buffer = new ArrayBufferWriter<byte>();
        CosWriter.Write(this, buffer);
        return Encoding.Latin1.GetString(buffer.WrittenSpan);
    }

    private static bool TryParseCore(ReadOnlySpan<byte> bytes, [NotNullWhen(true)] out CosObject? result, out CosRepair? firstRepair)
    {
        var repairs = new CosRepairLog();
        var parser = new CosParser(bytes, repairs);
        CosObject parsed = parser.ParseObject();
        parser.ExpectEndOfInput();

        firstRepair = repairs.First;
        if (firstRepair is not null)
        {
            result = null;
            return false;
        }

        result = parsed;
        return true;
    }
}
