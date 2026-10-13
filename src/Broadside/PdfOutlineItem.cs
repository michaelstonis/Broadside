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
/// The setters and <see cref="Add"/>, <see cref="Insert"/>, <see cref="MoveTo"/> and <see cref="Remove"/> edit the outline in place
/// (§12.3.3, Tables 150 and 151): they work on the links as they are now, not on an earlier walk, keep <c>First</c>, <c>Last</c>,
/// <c>Prev</c>, <c>Next</c> and <c>Parent</c> consistent around the change, and correct the <c>Count</c> of every ancestor whose
/// visible descendants change. They are mutations: no read may run concurrently with them.
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
    /// <remarks>As the walk that produced this view found it; <see cref="MoveTo"/> updates it for this view.</remarks>
    public PdfOutlineItem? Parent { get; private set; }

    /// <summary>Gets the depth of the item: 0 for a top-level item.</summary>
    /// <remarks>As the walk that produced this view found it; <see cref="MoveTo"/> updates it for this view.</remarks>
    public int Level { get; private set; }

    /// <summary>Gets the item's children, in the order of their <c>First</c>/<c>Next</c> chain.</summary>
    /// <remarks>ISO 32000-2 §12.3.3: "the items at a given level shall appear in the order in which they occur in the linked list".</remarks>
    public IReadOnlyList<PdfOutlineItem> Children => _children;

    /// <summary>Gets or sets the text displayed for the item; the empty string when <c>Title</c> is missing or not a string.</summary>
    /// <exception cref="ArgumentNullException">The value set is <see langword="null"/>.</exception>
    /// <remarks>ISO 32000-2 §12.3.3, Table 151; a text string (§7.9.2.2), written in PDFDocEncoding when it can be, else UTF-16BE.</remarks>
    public string Title
    {
        get => _document.Resolve(Dictionary.TryGetValue(NavigationNames.Title, out CosObject? title) ? title : null) is CosString text
            ? text.DecodeText()
            : string.Empty;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Dictionary[NavigationNames.Title] = new CosString(TextStringEncoder.Encode(value));
        }
    }

    /// <summary>Gets or sets the destination shown when the item is activated (<c>Dest</c>), or <see langword="null"/>.</summary>
    /// <exception cref="ArgumentException">The destination set is remote, or belongs to another document.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.3, Table 151, and §12.3.2: an explicit destination, or a named one (a name or a string). Setting a destination
    /// writes its array or name as <c>Dest</c> and removes any <c>A</c> ("shall not be present if a Dest entry is present"); setting
    /// <see langword="null"/> removes <c>Dest</c>. Make destinations with <see cref="PdfDocument.CreateDestination"/> or
    /// <see cref="PdfDocument.SetNamedDestination(string, PdfExplicitDestination)"/>.
    /// </remarks>
    public PdfDestination? Destination
    {
        get => Dictionary.TryGetValue(NavigationNames.Dest, out CosObject? dest) && !IsActionDictionary(_document.Resolve(dest))
            ? PdfDestination.Create(_document, dest, isRemote: false, Reference)
            : null;
        set
        {
            if (value is null)
            {
                Dictionary.Remove(NavigationNames.Dest);
                return;
            }

            if (value.IsRemote || !ReferenceEquals(value.OwningDocument, _document))
            {
                throw new ArgumentException("An outline item's destination shall be a destination in this document, not a remote one or one of another document.", nameof(value));
            }

            Dictionary[NavigationNames.Dest] = value.Value;
            Dictionary.Remove(NavigationNames.A);
        }
    }

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

    /// <summary>Gets or sets the colour of the item's text (<c>C</c>, PDF 1.4); black when absent or not three numbers, components clamped to 0 to 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A component of the value set is not a number from 0 to 1.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.3, Table 151: DeviceRGB, default <c>[0.0 0.0 0.0]</c>. Setting black removes <c>C</c>, the default; any other
    /// colour is written as three numbers (PDF 1.4).
    /// </remarks>
    public PdfRgbColor Color
    {
        get => ReadColor(out _);
        set
        {
            ReadOnlySpan<double> components = [value.Red, value.Green, value.Blue];
            foreach (double component in components)
            {
                if (!(component is >= 0 and <= 1))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Each colour component shall be a number from 0.0 to 1.0.");
                }
            }

            if (value == PdfRgbColor.Black)
            {
                Dictionary.Remove(NavigationNames.C);
                return;
            }

            Dictionary[NavigationNames.C] = new CosArray([new CosReal(value.Red), new CosReal(value.Green), new CosReal(value.Blue)]);
        }
    }

    /// <summary>Gets or sets the style flags (<c>F</c>, PDF 1.4); as stored when read, 0 when absent or not an integer.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value set has a bit Table 152 does not define.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.3, Tables 151 and 152: "all other bits of the integer shall be 0". Setting
    /// <see cref="PdfOutlineItemFlags.None"/> removes <c>F</c>, the default.
    /// </remarks>
    public PdfOutlineItemFlags Flags
    {
        get => (PdfOutlineItemFlags)ReadFlags(out _);
        set
        {
            if ((value & ~(PdfOutlineItemFlags.Italic | PdfOutlineItemFlags.Bold)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Only the italic and bold flags are defined.");
            }

            if (value == PdfOutlineItemFlags.None)
            {
                Dictionary.Remove(NavigationNames.F);
            }
            else
            {
                Dictionary[NavigationNames.F] = new CosInteger((int)value);
            }
        }
    }

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

    /// <summary>Gets or sets a value indicating whether the item is open: its children are shown. An item without a positive <c>Count</c> is closed.</summary>
    /// <remarks>
    /// ISO 32000-2 §12.3.3, Table 151. Setting it rewrites the item's <c>Count</c> with the matching sign (its magnitude recomputed
    /// from the children) and the <c>Count</c> of every ancestor and of the outline that shows the item's descendants. An item without
    /// children has no <c>Count</c> to hold the state, so setting it then does nothing; an item opens when it gets its first child.
    /// </remarks>
    public bool IsOpen
    {
        get => Count > 0;
        set
        {
            if (Dictionary.ContainsKey(NavigationNames.First))
            {
                OutlineEditor.UpdateCounts(_document, Dictionary, value);
            }
        }
    }

    internal List<PdfOutlineItem> ChildList => _children;

    /// <summary>Adds a child item titled <paramref name="title"/> after the item's last child.</summary>
    /// <param name="title">The text displayed for the child.</param>
    /// <returns>The new item, an indirect object with no destination yet.</returns>
    /// <exception cref="InvalidOperationException">The item is direct, or is no longer in the document.</exception>
    /// <remarks>ISO 32000-2 §12.3.3, Table 151. See <see cref="Insert"/>.</remarks>
    public PdfOutlineItem Add(string title) => InsertAt(null, title);

    /// <summary>Inserts a child item titled <paramref name="title"/> at <paramref name="index"/> among the item's children.</summary>
    /// <param name="index">The position among the children, from 0 to their number.</param>
    /// <param name="title">The text displayed for the child.</param>
    /// <returns>The new item, an indirect object with no destination yet.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or past the end of the children.</exception>
    /// <exception cref="InvalidOperationException">The item is direct, or is no longer in the document.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.3, Table 151. The child is linked through <c>Parent</c>, <c>Prev</c>, <c>Next</c>, <c>First</c> and
    /// <c>Last</c>; the item's <c>Count</c> grows (an item that had no children becomes open), and so does each ancestor's while the
    /// new child is visible.
    /// </remarks>
    public PdfOutlineItem Insert(int index, string title)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return InsertAt(index, title);
    }

    /// <summary>Moves the item, with its descendants, to <paramref name="index"/> among the children of <paramref name="parent"/>.</summary>
    /// <param name="parent">The new parent item, or <see langword="null"/> for the top level of the outline.</param>
    /// <param name="index">The position among the new parent's children once the item has been taken out, from 0 to their number.</param>
    /// <exception cref="ArgumentException"><paramref name="parent"/> is the item, one of its descendants, or an item of another document.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or past the end of the new parent's children.</exception>
    /// <exception cref="InvalidOperationException">The item or the new parent is direct, or is not in the document's outline.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.3, Tables 150 and 151. The item's old neighbours are linked to each other and its new ones to it, its
    /// <c>Parent</c> names the new parent, and the <c>Count</c> entries of both ancestries are corrected. This view's
    /// <see cref="Parent"/> and <see cref="Level"/> follow the move; other views keep what their walk saw.
    /// </remarks>
    public void MoveTo(PdfOutlineItem? parent, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        (CosDictionary root, CosReference rootReference) = OutlineRoot();
        OutlineEditor.Link self = Self();
        (CosDictionary oldParent, List<OutlineEditor.Link> oldSiblings, int oldIndex) = Locate(root, self);
        CosDictionary target = root;
        CosReference targetReference = rootReference;
        if (parent is not null)
        {
            if (!ReferenceEquals(parent._document, _document))
            {
                throw new ArgumentException("The new parent is an item of another document.", nameof(parent));
            }

            OutlineEditor.Link parentLink = parent.Self();
            _ = parent.Locate(root, parentLink);
            if (IsSelfOrAncestor(self.Item, parentLink.Item, root))
            {
                throw new ArgumentException("An item cannot be moved under itself or one of its descendants.", nameof(parent));
            }

            (target, targetReference) = (parentLink.Item, parentLink.Reference);
        }

        int available = ReferenceEquals(target, oldParent) ? oldSiblings.Count - 1 : OutlineEditor.Children(_document, target).Count;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, available);
        OutlineEditor.UnlinkAt(_document, oldParent, oldSiblings, oldIndex);
        List<OutlineEditor.Link> siblings = OutlineEditor.Children(_document, target);
        OutlineEditor.LinkAt(_document, target, targetReference, siblings, index, self);
        Parent = parent;
        Level = parent is null ? 0 : parent.Level + 1;
    }

    /// <summary>Removes the item and its descendants from the outline and from the document.</summary>
    /// <exception cref="InvalidOperationException">The item is direct, or is not in the document's outline.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.3, Tables 150 and 151. The item's neighbours are linked to each other, its parent's <c>First</c>,
    /// <c>Last</c> and <c>Count</c> and its ancestors' <c>Count</c> entries are corrected, and the item and its descendants are removed
    /// with <see cref="PdfDocument.RemoveObject"/> (a save writes their numbers as free entries); an item the rest of the outline
    /// still reaches through damaged links is kept. The view must not be edited afterwards.
    /// </remarks>
    public void Remove()
    {
        (CosDictionary root, _) = OutlineRoot();
        OutlineEditor.Link self = Self();
        (CosDictionary parent, List<OutlineEditor.Link> siblings, int index) = Locate(root, self);
        OutlineEditor.UnlinkAt(_document, parent, siblings, index);
        List<OutlineEditor.Link> removed = OutlineEditor.Subtree(_document, self);
        HashSet<CosDictionary> kept = OutlineEditor.Reachable(_document, root);
        foreach (OutlineEditor.Link item in removed)
        {
            if (!kept.Contains(item.Item))
            {
                _document.RemoveObject(item.Reference);
            }
        }
    }


    private PdfOutlineItem InsertAt(int? index, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        OutlineEditor.Link self = Self();
        OutlineEditor.Link link = OutlineEditor.Insert(_document, Dictionary, self.Reference, index, title);
        return new PdfOutlineItem(_document, link.Item, link.Reference, this, Level + 1);
    }

    /// <summary>The item's own link: its dictionary and reference, checked to be still the document's object.</summary>
    private OutlineEditor.Link Self()
    {
        if (Reference is null)
        {
            throw new InvalidOperationException("A direct outline item cannot be edited: the clause requires items to be indirect objects.");
        }

        if (!ReferenceEquals(_document.Resolve(Reference), Dictionary))
        {
            throw new InvalidOperationException("The outline item is no longer an object of the document.");
        }

        return new OutlineEditor.Link(Dictionary, Reference);
    }

    /// <summary>The outline dictionary and its reference; the outline is made indirect when the catalog holds it directly.</summary>
    private (CosDictionary Root, CosReference Reference) OutlineRoot()
    {
        PdfOutline outline = _document.Outline ?? throw new InvalidOperationException("The document has no outline.");
        return (outline.Dictionary, outline.EnsureIndirect());
    }

    /// <summary>Finds the level <paramref name="self"/> is linked in: its <c>Parent</c>, else the parent this view was walked under.</summary>
    private (CosDictionary Parent, List<OutlineEditor.Link> Siblings, int Index) Locate(CosDictionary root, OutlineEditor.Link self)
    {
        CosDictionary?[] candidates =
        [
            Dictionary.TryGetValue(KnownNames.Parent, out CosObject? parent) ? _document.Resolve(parent) as CosDictionary : null,
            Parent?.Dictionary ?? root,
        ];
        HashSet<CosDictionary>? reachable = null;
        foreach (CosDictionary? candidate in candidates)
        {
            if (candidate is null)
            {
                continue;
            }

            if (!ReferenceEquals(candidate, root))
            {
                reachable ??= OutlineEditor.Reachable(_document, root);
                if (!reachable.Contains(candidate))
                {
                    continue;
                }
            }

            List<OutlineEditor.Link> siblings = OutlineEditor.Children(_document, candidate);
            int index = OutlineEditor.IndexOf(siblings, self.Item);
            if (index >= 0)
            {
                return (candidate, siblings, index);
            }
        }

        throw new InvalidOperationException("The outline item is not in the document's outline.");
    }

    /// <summary>Whether <paramref name="item"/> is <paramref name="node"/> or one of its ancestors, by <c>Parent</c> links.</summary>
    private bool IsSelfOrAncestor(CosDictionary item, CosDictionary node, CosDictionary root)
    {
        var seen = new HashSet<CosDictionary>(ReferenceEqualityComparer.Instance);
        for (CosDictionary? current = node; current is not null && !ReferenceEquals(current, root) && seen.Add(current);)
        {
            if (ReferenceEquals(current, item))
            {
                return true;
            }

            current = current.TryGetValue(KnownNames.Parent, out CosObject? parent) ? _document.Resolve(parent) as CosDictionary : null;
        }

        return false;
    }

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
