using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Broadside.Objects;

namespace Broadside.Parsing;

/// <summary>Where an object being loaded comes from.</summary>
internal enum ObjectOrigin : byte
{
    /// <summary>An indirect object in the file body, located by a cross-reference entry (§7.3.10).</summary>
    FileBody,

    /// <summary>An object inside an object stream (§7.5.7; issue #39). Never decrypted on its own (§7.6.2).</summary>
    ObjectStream,
}

/// <summary>What the loader knows about the object it is loading, passed to every hook.</summary>
/// <param name="Reference">The object's number and generation.</param>
/// <param name="Offset">The absolute byte offset of the object's <c>N G obj</c> header; for an object-stream member, of the object stream.</param>
/// <param name="Origin">Whether the object is in the file body or in an object stream.</param>
/// <param name="Depth">How many loads are in progress beneath this one (an indirect <c>Length</c> loads while its stream loads).</param>
internal readonly record struct ObjectLoadContext(CosReference Reference, long Offset, ObjectOrigin Origin, int Depth);

/// <summary>
/// Hook 1 of the object loader: resolves a stream's <c>Length</c> so the parser can find the end of the data (§7.3.8.2). Runs during
/// parsing. Issue #41 extends recovery of wrong lengths behind it.
/// </summary>
internal interface IStreamExtentResolver
{
    /// <summary>Returns the byte count a stream's <c>Length</c> entry stands for, or <see langword="null"/> when unknown.</summary>
    /// <param name="lengthEntry">The <c>Length</c> entry as stored, a direct integer or an indirect reference.</param>
    /// <param name="loader">The loader, to resolve a reference with.</param>
    /// <param name="context">The stream object being loaded.</param>
    /// <returns>The length, or <see langword="null"/>.</returns>
    long? ResolveLength(CosObject lengthEntry, ObjectLoader loader, in ObjectLoadContext context);
}

/// <summary>
/// Hook 2 of the object loader: decrypts the strings and stream data of a freshly parsed object (§7.6). Runs after parsing and before
/// the object is published to the cache. Issue #42 supplies the security-handler implementation; until a file's <c>Encrypt</c>
/// dictionary has been read the loader uses <see cref="NullObjectDecryptor"/>.
/// </summary>
/// <remarks>
/// Must not decrypt cross-reference streams, objects inside object streams (<see cref="ObjectOrigin.ObjectStream"/>, already decrypted
/// with their container), or the <c>Encrypt</c> dictionary itself (§7.6.2). Must not mark anything dirty (ADR 0004).
/// </remarks>
internal interface IObjectDecryptor
{
    /// <summary>Returns <paramref name="value"/> with its strings and stream data decrypted.</summary>
    /// <param name="value">The object as parsed.</param>
    /// <param name="context">The object being loaded.</param>
    /// <returns>The decrypted object; may be <paramref name="value"/> itself.</returns>
    CosObject Decrypt(CosObject value, in ObjectLoadContext context);
}

/// <summary>
/// Hook 3 of the object loader: the cache loaded objects are published to, so each indirect object is parsed once and every
/// reference to it yields the same instance (a live view needs one instance to view). Issue #45 replaces the default with a
/// lazy-once, memory-bounded cache under the concurrency contract.
/// </summary>
internal interface IObjectCache
{
    /// <summary>Looks up a loaded object.</summary>
    /// <param name="reference">The object's number and generation.</param>
    /// <param name="value">The object, when cached.</param>
    /// <returns><see langword="true"/> when cached.</returns>
    bool TryGet(CosReference reference, [NotNullWhen(true)] out CosObject? value);

    /// <summary>Publishes a loaded object, or returns the one another thread published first.</summary>
    /// <param name="reference">The object's number and generation.</param>
    /// <param name="value">The loaded object.</param>
    /// <returns>The cached instance, which every caller must use.</returns>
    CosObject Publish(CosReference reference, CosObject value);
}

/// <summary>The default <see cref="IStreamExtentResolver"/>: a direct integer, or an indirect reference loaded through the loader.</summary>
internal sealed class DefaultStreamExtentResolver : IStreamExtentResolver
{
    /// <summary>Gets the shared instance; it holds no state.</summary>
    public static DefaultStreamExtentResolver Instance { get; } = new();

    /// <inheritdoc/>
    public long? ResolveLength(CosObject lengthEntry, ObjectLoader loader, in ObjectLoadContext context)
    {
        CosObject value = lengthEntry is CosReference reference && !reference.Equals(context.Reference)
            ? loader.Load(reference, context.Depth + 1)
            : lengthEntry;
        return value is CosInteger { Value: >= 0 } length ? length.Value : null;
    }
}

/// <summary>The <see cref="IObjectDecryptor"/> of an unencrypted file: returns every object unchanged.</summary>
internal sealed class NullObjectDecryptor : IObjectDecryptor
{
    /// <summary>Gets the shared instance; it holds no state.</summary>
    public static NullObjectDecryptor Instance { get; } = new();

    /// <inheritdoc/>
    public CosObject Decrypt(CosObject value, in ObjectLoadContext context) => value;
}

/// <summary>The default <see cref="IObjectCache"/>: an unbounded concurrent dictionary, first publisher wins.</summary>
internal sealed class ConcurrentObjectCache : IObjectCache
{
    private readonly ConcurrentDictionary<CosReference, CosObject> _objects = new();

    /// <inheritdoc/>
    public bool TryGet(CosReference reference, [NotNullWhen(true)] out CosObject? value) => _objects.TryGetValue(reference, out value);

    /// <inheritdoc/>
    public CosObject Publish(CosReference reference, CosObject value) => _objects.GetOrAdd(reference, value);
}

/// <summary>
/// The hooks of one document's <see cref="ObjectLoader"/>, which runs them in a fixed order for every object: stream extent (during
/// parsing), then decryption, then cache publication. Later issues implement a hook; they do not restructure the loader.
/// </summary>
internal sealed class ObjectLoaderHooks
{
    private volatile IObjectDecryptor _decryptor = NullObjectDecryptor.Instance;

    /// <summary>Gets hook 1, stream extent resolution (issue #41 extends it).</summary>
    public IStreamExtentResolver StreamExtent { get; init; } = DefaultStreamExtentResolver.Instance;

    /// <summary>
    /// Gets or sets hook 2, decryption (issue #42). Set once during open, after the <c>Encrypt</c> dictionary is read and before the
    /// catalog is loaded; objects loaded before then (the <c>Encrypt</c> dictionary) are never decrypted.
    /// </summary>
    public IObjectDecryptor Decryptor
    {
        get => _decryptor;
        set => _decryptor = value;
    }

    /// <summary>Gets hook 3, cache publication (issue #45 replaces it).</summary>
    public IObjectCache Cache { get; init; } = new ConcurrentObjectCache();
}
