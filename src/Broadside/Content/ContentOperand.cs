using Broadside.Objects;

namespace Broadside.Content;

/// <summary>
/// One operand of a content stream operator, read in place: numbers as values, names and strings as decoded bytes, arrays and
/// dictionaries as views of their elements. Valid only during the callback that received it; copy what you keep, or call
/// <see cref="ToCosObject"/>.
/// </summary>
/// <remarks>ISO 32000-2 §7.8.2: operands are direct objects of any basic type except a stream.</remarks>
public readonly ref struct ContentOperand
{
    /// <summary>What a default operand reads as: the null object.</summary>
    private static readonly OperandEntry NullEntry = new() { Kind = ContentOperandKind.Null, End = 1 };

    private readonly ReadOnlySpan<OperandEntry> _entries;
    private readonly ReadOnlySpan<byte> _bytes;
    private readonly int _index;

    internal ContentOperand(ReadOnlySpan<OperandEntry> entries, ReadOnlySpan<byte> bytes, int index)
    {
        _entries = entries;
        _bytes = bytes;
        _index = index;
    }

    /// <summary>Gets the object type.</summary>
    public ContentOperandKind Kind => Entry.Kind;

    /// <summary>Gets a value indicating whether this is an integer or a real.</summary>
    public bool IsNumber => Entry.Kind is ContentOperandKind.Integer or ContentOperandKind.Real;

    /// <summary>Gets the value of a number; 0 for any other kind.</summary>
    /// <remarks>ISO 32000-2 §7.3.3. Integers beyond the range of a double's exact integers keep the nearest double.</remarks>
    public double Number => IsNumber ? Entry.Number : 0;

    /// <summary>Gets the value of a boolean; false for any other kind.</summary>
    public bool Boolean => Entry.Kind == ContentOperandKind.Boolean && Entry.Number != 0;

    /// <summary>Gets the decoded bytes of a name (without the solidus, <c>#xx</c> escapes resolved) or a string (escapes resolved); empty for any other kind.</summary>
    /// <remarks>ISO 32000-2 §7.3.4 and §7.3.5.</remarks>
    public ReadOnlySpan<byte> Bytes => _bytes.Slice(Entry.BytesStart, Entry.BytesLength);

    /// <summary>Gets a value indicating whether a string was written in hexadecimal (§7.3.4.3).</summary>
    public bool IsHexadecimal => Entry.IsHexadecimal;

    /// <summary>Gets the elements of an array, or the alternating keys and values of a dictionary; empty for any other kind.</summary>
    public ContentOperands Items => new(_entries, _bytes, _index + 1, Entry.Count);

    private ref readonly OperandEntry Entry => ref _entries.IsEmpty ? ref NullEntry : ref _entries[_index];

    /// <summary>Returns whether this is a name with the bytes <paramref name="name"/>.</summary>
    /// <param name="name">The name's bytes, without the solidus.</param>
    /// <returns><see langword="true"/> for a name equal to <paramref name="name"/>.</returns>
    public bool IsName(ReadOnlySpan<byte> name) => Kind == ContentOperandKind.Name && Bytes.SequenceEqual(name);

    /// <summary>Returns the operand as a new COS object, copying its bytes and elements.</summary>
    /// <returns>A <see cref="CosObject"/> of the matching type.</returns>
    /// <remarks>Allocates; for processors that keep an operand, such as a marked-content property list, beyond the callback.</remarks>
    public CosObject ToCosObject()
    {
        switch (Kind)
        {
            case ContentOperandKind.Boolean:
                return Boolean ? CosBoolean.True : CosBoolean.False;
            case ContentOperandKind.Integer:
                double value = Number;
                return value is >= long.MinValue and <= long.MaxValue ? new CosInteger((long)value) : new CosReal(value);
            case ContentOperandKind.Real:
                return new CosReal(Number);
            case ContentOperandKind.Name:
                return new CosName(Bytes);
            case ContentOperandKind.String:
                return new CosString(Bytes, IsHexadecimal);
            case ContentOperandKind.Array:
                var array = new CosArray();
                foreach (ContentOperand item in Items)
                {
                    array.Add(item.ToCosObject());
                }

                return array;
            case ContentOperandKind.Dictionary:
                var dictionary = new CosDictionary();
                ContentOperands items = Items;
                for (int index = 0; index + 1 < items.Count; index += 2)
                {
                    if (items[index] is { Kind: ContentOperandKind.Name } key && items[index + 1].ToCosObject() is not CosNull and var item)
                    {
                        dictionary[new CosName(key.Bytes)] = item;
                    }
                }

                return dictionary;
            default:
                return CosNull.Instance;
        }
    }
}
