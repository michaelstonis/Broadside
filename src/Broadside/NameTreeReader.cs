using System.Diagnostics.CodeAnalysis;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>Reads one name tree: keys are strings compared byte by byte.</summary>
/// <remarks>
/// ISO 32000-2 §7.9.6, Table 36, and Annex J.3.3 (string comparison is a binary comparison of the bytes). Shorter keys sort before
/// longer keys with the same leading bytes, which is what <see cref="MemoryExtensions.SequenceCompareTo{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
/// gives. A key written as a name is used by its bytes, with a diagnostic; any other key is skipped. One reader per tree root:
/// <see cref="PdfDocument"/> keeps them, so the flattened index of a damaged tree is built once. See <see cref="TreeReader{TKey}"/>.
/// </remarks>
internal sealed class NameTreeReader : TreeReader<CosString>
{
    private static readonly TreeCodes NameTreeCodes = new(
        DiagnosticCodes.NameTreeNodeInvalid,
        DiagnosticCodes.NameTreeLimitsInvalid,
        DiagnosticCodes.NameTreeKeysUnsorted,
        DiagnosticCodes.NameTreeDuplicateKey,
        DiagnosticCodes.NameTreeKeyInvalid,
        DiagnosticCodes.NameTreeCycle,
        DiagnosticCodes.NameTreeTooDeep);

    /// <summary>Initializes a new instance of the <see cref="NameTreeReader"/> class.</summary>
    /// <param name="root">The root node dictionary.</param>
    /// <param name="rootReference">The root's reference, for diagnostics; <see langword="null"/> when the root is direct.</param>
    /// <param name="resolve">Resolves references through the document.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <param name="maxDepth">The deepest level the walk descends to.</param>
    internal NameTreeReader(CosDictionary root, CosReference? rootReference, Func<CosObject?, CosObject> resolve, DiagnosticSink diagnostics, int maxDepth = DefaultMaxDepth)
        : base(root, rootReference, resolve, diagnostics, maxDepth)
    {
    }

    /// <inheritdoc/>
    private protected override CosName EntriesKey => KnownNames.Names;

    /// <inheritdoc/>
    private protected override TreeCodes Codes => NameTreeCodes;

    /// <inheritdoc/>
    private protected override string Kind => "name tree";

    /// <inheritdoc/>
    private protected override IComparer<CosString> KeyComparer => ByteOrder.Instance;

    /// <inheritdoc/>
    private protected override IEqualityComparer<CosString> KeyEquality => EqualityComparer<CosString>.Default;

    /// <summary>Looks up the key with exactly these bytes.</summary>
    /// <param name="key">The key's bytes.</param>
    /// <param name="value">The value, resolved one level, when found.</param>
    /// <returns><see langword="true"/> when the tree holds the key.</returns>
    internal bool TryGetValue(ReadOnlySpan<byte> key, [MaybeNullWhen(false)] out CosObject value) => TryGetValue(new CosString(key), out value);

    /// <inheritdoc/>
    private protected override KeyState ReadKey(CosObject key, [MaybeNullWhen(false)] out CosString value, out string? message)
    {
        switch (key)
        {
            case CosString text:
                value = text;
                message = null;
                return KeyState.Valid;
            case CosName name:
                value = new CosString(name.Bytes);
                message = "A name tree key shall be a string; it is a name, whose bytes are used as the key.";
                return KeyState.Repaired;
            default:
                value = null!;
                message = "A name tree key is not a string; the pair is skipped.";
                return KeyState.Invalid;
        }
    }

    /// <summary>Orders strings by their bytes (Annex J.3.3).</summary>
    private sealed class ByteOrder : IComparer<CosString>
    {
        public static readonly ByteOrder Instance = new();

        public int Compare(CosString? x, CosString? y) => x!.Bytes.SequenceCompareTo(y!.Bytes);
    }
}
