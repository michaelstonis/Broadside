using Broadside.Objects;

namespace Broadside;

/// <summary>One item (bookmark) of the document outline: its title, where it leads, how it looks, and its children.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §12.3.3, Tables 151 and 152. The properties read the item dictionary on every call and return the defaults the clause
/// gives (black, no flags) without writing them. <see cref="Children"/> is the shape of the outline when
/// <see cref="PdfOutline.Items"/> walked it; walk again to see changes to the links.
/// </para>
/// <para>
/// An item may have both <c>Dest</c> and <c>A</c>, which the clause forbids; both are exposed, and a reader that activates the item
/// should prefer <see cref="Action"/> (as viewers do). A <c>Dest</c> holding an action dictionary reads as <see cref="Action"/>.
/// </para>
/// </remarks>
public sealed class PdfOutlineItem
{
    private readonly PdfDocument _document;
    private readonly List<PdfOutlineItem> _children = [];

    internal PdfOutlineItem(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfOutlineItem? parent, int level)
    {
        _document = document;
        Dictionary = dictionary;
        Reference = reference;
        Parent = parent;
        Level = level;
    }

    /// <summary>Gets the outline item dictionary.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 151.</remarks>
    public CosDictionary Dictionary { get; }

    /// <summary>Gets the indirect reference to the item, or <see langword="null"/> when it is a direct dictionary (which the clause forbids).</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the item this one is a child of, or <see langword="null"/> for a top-level item.</summary>
    public PdfOutlineItem? Parent { get; }

    /// <summary>Gets the depth of the item: 0 for a top-level item.</summary>
    public int Level { get; }

    /// <summary>Gets the item's children, in the order of their <c>First</c>/<c>Next</c> chain.</summary>
    /// <remarks>ISO 32000-2 §12.3.3: "the items at a given level shall appear in the order in which they occur in the linked list".</remarks>
    public IReadOnlyList<PdfOutlineItem> Children => _children;

    /// <summary>Gets the text displayed for the item, or the empty string when <c>Title</c> is missing or not a string.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 151; a text string (§7.9.2.2).</remarks>
    public string Title => _document.Resolve(Dictionary.TryGetValue(NavigationNames.Title, out CosObject? title) ? title : null) is CosString text
        ? text.DecodeText()
        : string.Empty;

    /// <summary>Gets the destination shown when the item is activated (<c>Dest</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 151, and §12.3.2: an explicit destination, or a named one (a name or a string).</remarks>
    public PdfDestination? Destination =>
        Dictionary.TryGetValue(NavigationNames.Dest, out CosObject? dest) && !IsActionDictionary(_document.Resolve(dest))
            ? PdfDestination.Create(_document, dest, isRemote: false, Reference)
            : null;

    /// <summary>Gets the action performed when the item is activated (<c>A</c>, PDF 1.1), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 151, and §12.6.</remarks>
    public PdfAction? Action
    {
        get
        {
            if (Dictionary.TryGetValue(NavigationNames.A, out CosObject? action))
            {
                return PdfAction.Create(_document, action, Reference);
            }

            return Dictionary.TryGetValue(NavigationNames.Dest, out CosObject? dest) && IsActionDictionary(_document.Resolve(dest))
                ? PdfAction.Create(_document, dest, Reference)
                : null;
        }
    }

    /// <summary>Gets the structure element the item refers to (<c>SE</c>, PDF 1.3), or <see langword="null"/>. Not meant for navigation.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 151, and §14.7.2.</remarks>
    public CosDictionary? StructureElement =>
        Dictionary.TryGetValue(NavigationNames.SE, out CosObject? element) ? _document.Resolve(element) as CosDictionary : null;

    /// <summary>Gets the colour of the item's text (<c>C</c>, PDF 1.4); black when absent or not three numbers, components clamped to 0 to 1.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 151: DeviceRGB, default <c>[0.0 0.0 0.0]</c>.</remarks>
    public PdfRgbColor Color => ReadColor(out _);

    /// <summary>Gets the style flags (<c>F</c>, PDF 1.4) as stored; 0 when absent or not an integer.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Tables 151 and 152.</remarks>
    public PdfOutlineItemFlags Flags => (PdfOutlineItemFlags)ReadFlags(out _);

    /// <summary>Gets a value indicating whether the item's text is displayed in italic (flag bit 1).</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 152.</remarks>
    public bool IsItalic => (Flags & PdfOutlineItemFlags.Italic) != 0;

    /// <summary>Gets a value indicating whether the item's text is displayed in bold (flag bit 2).</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 152.</remarks>
    public bool IsBold => (Flags & PdfOutlineItemFlags.Bold) != 0;

    /// <summary>Gets the <c>Count</c> entry as stored, or <see langword="null"/> when absent or not an integer.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.3.3, Table 151: positive when the item is open (the number of its visible descendants), negative when closed
    /// (minus the number that would be visible if it were opened). Not corrected when wrong: the outline walk reports it.
    /// </remarks>
    public int? Count => ReadCount(_document, Dictionary);

    /// <summary>Gets a value indicating whether the item is open: its children are shown. An item without a positive <c>Count</c> is closed.</summary>
    /// <remarks>ISO 32000-2 §12.3.3, Table 151.</remarks>
    public bool IsOpen => Count > 0;

    internal List<PdfOutlineItem> ChildList => _children;

    /// <summary>Reads a <c>Count</c> entry: an integer in the 32-bit range, else <see langword="null"/>.</summary>
    internal static int? ReadCount(PdfDocument document, CosDictionary dictionary) =>
        dictionary.TryGetValue(KnownNames.Count, out CosObject? count) && document.Resolve(count) is CosInteger { Value: >= int.MinValue and <= int.MaxValue } integer
            ? (int)integer.Value
            : null;

    /// <summary>Reads <c>C</c>; <paramref name="valid"/> is false when it is present but not three numbers from 0 to 1.</summary>
    internal PdfRgbColor ReadColor(out bool valid)
    {
        valid = true;
        if (!Dictionary.TryGetValue(NavigationNames.C, out CosObject? entry))
        {
            return PdfRgbColor.Black;
        }

        if (_document.Resolve(entry) is not CosArray { Count: 3 } components)
        {
            valid = false;
            return PdfRgbColor.Black;
        }

        Span<double> values = stackalloc double[3];
        for (int index = 0; index < 3; index++)
        {
            if (_document.Resolve(components[index]) is not CosNumber number || !double.IsFinite(number.ToDouble()))
            {
                valid = false;
                return PdfRgbColor.Black;
            }

            values[index] = Math.Clamp(number.ToDouble(), 0, 1);
            valid &= values[index] == number.ToDouble();
        }

        return new PdfRgbColor(values[0], values[1], values[2]);
    }

    /// <summary>Reads <c>F</c>; <paramref name="valid"/> is false when it is present but not an integer.</summary>
    internal int ReadFlags(out bool valid)
    {
        valid = true;
        if (!Dictionary.TryGetValue(NavigationNames.F, out CosObject? entry))
        {
            return 0;
        }

        if (_document.Resolve(entry) is CosInteger { Value: >= int.MinValue and <= int.MaxValue } flags)
        {
            return (int)flags.Value;
        }

        valid = false;
        return 0;
    }

    /// <summary>Whether a <c>Dest</c> value is an action dictionary, a producer error read as <c>A</c>.</summary>
    internal static bool IsActionDictionary(CosObject value) => value is CosDictionary dictionary && dictionary.ContainsKey(NavigationNames.S);
}
