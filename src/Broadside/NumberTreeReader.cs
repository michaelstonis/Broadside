using System.Diagnostics.CodeAnalysis;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>Reads one number tree: keys are integers in ascending numerical order.</summary>
/// <remarks>
/// ISO 32000-2 §7.9.7, Table 37. Keys are 32-bit: a key outside that range is skipped with a diagnostic, and a real holding an
/// integral value is used as that integer, with a diagnostic. One reader per tree root: <see cref="PdfDocument"/> keeps them. See
/// <see cref="TreeReader{TKey}"/>.
/// </remarks>
internal sealed class NumberTreeReader : TreeReader<int>
{
    private static readonly TreeCodes NumberTreeCodes = new(
        DiagnosticCodes.NumberTreeNodeInvalid,
        DiagnosticCodes.NumberTreeLimitsInvalid,
        DiagnosticCodes.NumberTreeKeysUnsorted,
        DiagnosticCodes.NumberTreeDuplicateKey,
        DiagnosticCodes.NumberTreeKeyInvalid,
        DiagnosticCodes.NumberTreeCycle,
        DiagnosticCodes.NumberTreeTooDeep);

    /// <summary>Initializes a new instance of the <see cref="NumberTreeReader"/> class.</summary>
    /// <param name="root">The root node dictionary.</param>
    /// <param name="rootReference">The root's reference, for diagnostics; <see langword="null"/> when the root is direct.</param>
    /// <param name="resolve">Resolves references through the document.</param>
    /// <param name="diagnostics">Where to report deviations.</param>
    /// <param name="maxDepth">The deepest level the walk descends to.</param>
    internal NumberTreeReader(CosDictionary root, CosReference? rootReference, Func<CosObject?, CosObject> resolve, DiagnosticSink diagnostics, int maxDepth = DefaultMaxDepth)
        : base(root, rootReference, resolve, diagnostics, maxDepth)
    {
    }

    /// <inheritdoc/>
    private protected override CosName EntriesKey => NavigationNames.Nums;

    /// <inheritdoc/>
    private protected override TreeCodes Codes => NumberTreeCodes;

    /// <inheritdoc/>
    private protected override string Kind => "number tree";

    /// <inheritdoc/>
    private protected override IComparer<int> KeyComparer => Comparer<int>.Default;

    /// <inheritdoc/>
    private protected override IEqualityComparer<int> KeyEquality => EqualityComparer<int>.Default;

    /// <inheritdoc/>
    private protected override KeyState ReadKey(CosObject key, [MaybeNullWhen(false)] out int value, out string? message)
    {
        switch (key)
        {
            case CosInteger integer when integer.Value is >= int.MinValue and <= int.MaxValue:
                value = (int)integer.Value;
                message = null;
                return KeyState.Valid;
            case CosReal real when double.IsInteger(real.Value) && real.Value is >= int.MinValue and <= int.MaxValue:
                value = (int)real.Value;
                message = "A number tree key shall be an integer; it is a real holding an integral value, used as that integer.";
                return KeyState.Repaired;
            default:
                value = 0;
                message = "A number tree key is not an integer in the range a reader supports; the pair is skipped.";
                return KeyState.Invalid;
        }
    }
}
