using Broadside.Objects;

namespace Broadside;

/// <content>Writing outlines, destinations and page labels (issue #164): ISO 32000-2 §12.3.2, §12.3.3 and §12.4.2.</content>
public sealed partial class PdfDocument
{
    private static readonly CosName PageLabelStyleKey = new("S");
    private static readonly CosName PageLabelPrefixKey = new("P");
    private static readonly CosName PageLabelStartKey = new("St");

    /// <summary>Creates a new, empty outline and makes it the document's, replacing any outline the catalog has.</summary>
    /// <returns>The outline; add items with <see cref="PdfOutline.Add"/>.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, Table 29 (<c>Outlines</c>), and §12.3.3, Table 150: an indirect <c>&lt;&lt; /Type /Outlines &gt;&gt;</c>. An
    /// outline it replaces stays in the file until a save that removes unreachable objects. A mutation: no read may run concurrently
    /// with it.
    /// </remarks>
    public PdfOutline CreateOutline()
    {
        var dictionary = new CosDictionary { [KnownNames.Type] = NavigationNames.Outlines };
        CosReference reference = AddObject(dictionary);
        Catalog[NavigationNames.Outlines] = reference;
        return new PdfOutline(this, dictionary, reference);
    }

    /// <summary>Creates an explicit destination showing <paramref name="page"/> as <paramref name="view"/> says.</summary>
    /// <param name="page">A page of this document.</param>
    /// <param name="view">How the page is shown.</param>
    /// <param name="left">The left edge, for <c>XYZ</c>, <c>FitV</c>, <c>FitR</c> and <c>FitBV</c>; <see langword="null"/> keeps the current one (not for <c>FitR</c>).</param>
    /// <param name="top">The top edge, for <c>XYZ</c>, <c>FitH</c>, <c>FitR</c> and <c>FitBH</c>; <see langword="null"/> keeps the current one (not for <c>FitR</c>).</param>
    /// <param name="right">The right edge, for <c>FitR</c>.</param>
    /// <param name="bottom">The bottom edge, for <c>FitR</c>.</param>
    /// <param name="zoom">The zoom factor, for <c>XYZ</c>; <see langword="null"/> keeps the current one.</param>
    /// <returns>A destination over a new destination array, not yet stored anywhere: give it to <see cref="PdfOutlineItem.Destination"/> or <see cref="SetNamedDestination(string, PdfExplicitDestination)"/>.</returns>
    /// <exception cref="ArgumentException">
    /// The page is not an indirect page object of this document, the view is <see cref="PdfDestinationView.Unknown"/>, a parameter the
    /// view does not take is given, a <c>FitR</c> edge is missing, or a value is not finite.
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.2.2, Table 149: <c>[page /XYZ left top zoom]</c>, <c>[page /Fit]</c>, <c>[page /FitH top]</c>,
    /// <c>[page /FitV left]</c>, <c>[page /FitR left bottom right top]</c>, <c>[page /FitB]</c> (PDF 1.1), <c>[page /FitBH top]</c>
    /// (PDF 1.1), <c>[page /FitBV left]</c> (PDF 1.1), in default user space, null for a value kept.
    /// </remarks>
    public PdfExplicitDestination CreateDestination(
        PdfPage page,
        PdfDestinationView view,
        double? left = null,
        double? top = null,
        double? right = null,
        double? bottom = null,
        double? zoom = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Reference is null || Pages.IndexOf(page.Dictionary) < 0 || !ReferenceEquals(Resolve(page.Reference), page.Dictionary))
        {
            throw new ArgumentException("The page shall be an indirect page object of this document.", nameof(page));
        }

        (string name, bool hasLeft, bool hasTop, bool hasRightBottom, bool hasZoom) = view switch
        {
            PdfDestinationView.Xyz => ("XYZ", true, true, false, true),
            PdfDestinationView.Fit => ("Fit", false, false, false, false),
            PdfDestinationView.FitH => ("FitH", false, true, false, false),
            PdfDestinationView.FitV => ("FitV", true, false, false, false),
            PdfDestinationView.FitR => ("FitR", true, true, true, false),
            PdfDestinationView.FitB => ("FitB", false, false, false, false),
            PdfDestinationView.FitBH => ("FitBH", false, true, false, false),
            PdfDestinationView.FitBV => ("FitBV", true, false, false, false),
            _ => throw new ArgumentException("The view shall be one of Table 149's.", nameof(view)),
        };
        Check(left, hasLeft, nameof(left));
        Check(top, hasTop, nameof(top));
        Check(right, hasRightBottom, nameof(right));
        Check(bottom, hasRightBottom, nameof(bottom));
        Check(zoom, hasZoom, nameof(zoom));
        var array = new CosArray([page.Reference, new CosName(name)]);
        if (view == PdfDestinationView.FitR)
        {
            if (left is null || top is null || right is null || bottom is null)
            {
                throw new ArgumentException("A FitR destination needs all four edges.", nameof(view));
            }

            array.Add(Number(left));
            array.Add(Number(bottom));
            array.Add(Number(right));
            array.Add(Number(top));
        }
        else
        {
            if (hasLeft)
            {
                array.Add(Number(left));
            }

            if (hasTop)
            {
                array.Add(Number(top));
            }

            if (hasZoom)
            {
                array.Add(Number(zoom));
            }
        }

        return new PdfExplicitDestination(this, array, isRemote: false, owner: null);

        static void Check(double? value, bool allowed, string parameter)
        {
            if (value is { } number && (!allowed || !double.IsFinite(number)))
            {
                throw new ArgumentException(allowed ? "The value shall be a finite number." : "The view does not take this value.", parameter);
            }
        }

        static CosObject Number(double? value) => value switch
        {
            null => CosNull.Instance,
            { } number when double.IsInteger(number) && Math.Abs(number) <= int.MaxValue => new CosInteger((long)number),
            { } number => new CosReal(number),
        };
    }

    /// <summary>Gives <paramref name="destination"/> the name <paramref name="name"/> in the name dictionary's <c>Dests</c> tree.</summary>
    /// <param name="name">The name, a text string: an existing key that reads as it is replaced, else it is written in PDFDocEncoding when it can be, else UTF-16BE.</param>
    /// <param name="destination">An explicit destination of this document.</param>
    /// <returns>A named destination for <paramref name="name"/>, to give to <see cref="PdfOutlineItem.Destination"/>.</returns>
    /// <exception cref="ArgumentException">The destination is remote or belongs to another document.</exception>
    /// <remarks>
    /// ISO 32000-2 §12.3.2.4 (PDF 1.2) and §7.7.4, Table 32. The name dictionary and its <c>Dests</c> tree are created when absent; the
    /// key is found as <see cref="GetNamedDestination(string)"/> finds it (PDFDocEncoding, then UTF-16BE, then every key decoded). A
    /// PDF 1.1 entry of the same name in the catalog's <c>Dests</c> dictionary is removed, so both lookups agree. A mutation.
    /// </remarks>
    public PdfNamedDestination SetNamedDestination(string name, PdfExplicitDestination destination)
    {
        ArgumentNullException.ThrowIfNull(name);
        PdfNameTree? tree = Names?.Dests;
        CosString key = tree is not null && FindDestinationKey(tree, name) is { } existing ? existing : new CosString(TextStringEncoder.Encode(name));
        return SetNamedDestination(key, destination);
    }

    /// <summary>Gives <paramref name="destination"/> the byte-string name <paramref name="name"/> in the name dictionary's <c>Dests</c> tree.</summary>
    /// <param name="name">The name, compared byte by byte (Annex J.3.3).</param>
    /// <param name="destination">An explicit destination of this document.</param>
    /// <returns>A named destination for <paramref name="name"/>, to give to <see cref="PdfOutlineItem.Destination"/>.</returns>
    /// <exception cref="ArgumentException">The destination is remote or belongs to another document.</exception>
    /// <remarks>ISO 32000-2 §12.3.2.4 (PDF 1.2) and §7.7.4, Table 32. See <see cref="SetNamedDestination(string, PdfExplicitDestination)"/>.</remarks>
    public PdfNamedDestination SetNamedDestination(CosString name, PdfExplicitDestination destination)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.IsRemote || !ReferenceEquals(destination.OwningDocument, this))
        {
            throw new ArgumentException("A named destination shall be a destination in this document, not a remote one or one of another document.", nameof(destination));
        }

        CosDictionary names = NameDictionaryForWriting();
        PdfNameTree tree = GetNameTree(names.TryGetValue(NavigationNames.Dests, out CosObject? root) ? root : null) ?? CreateDestsTree(names);
        tree.Set(name, destination.Array);
        RemoveLegacyDestination(name.Bytes);
        return new PdfNamedDestination(this, name, isRemote: false, owner: null);
    }

    /// <summary>Removes the named destination <paramref name="name"/>.</summary>
    /// <param name="name">The name, a text string, found as <see cref="GetNamedDestination(string)"/> finds it.</param>
    /// <returns><see langword="true"/> when the document had a destination of that name.</returns>
    /// <remarks>
    /// ISO 32000-2 §12.3.2.4: from the name dictionary's <c>Dests</c> tree and the catalog's <c>Dests</c> dictionary. References to the
    /// name (outline items, links, go-to actions) are left, and no longer resolve. A mutation.
    /// </remarks>
    public bool RemoveNamedDestination(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        bool removed = Names?.Dests is { } tree && FindDestinationKey(tree, name) is { } key && tree.Remove(key);
        if (!name.Contains('\0', StringComparison.Ordinal))
        {
            removed |= RemoveLegacyDestination(new CosName(name).Bytes);
        }

        return removed;
    }

    /// <summary>Removes the named destination <paramref name="name"/>, compared byte by byte.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when the document had a destination of that name.</returns>
    /// <remarks>ISO 32000-2 §12.3.2.4 and Annex J.3.3. See <see cref="RemoveNamedDestination(string)"/>.</remarks>
    public bool RemoveNamedDestination(CosString name)
    {
        ArgumentNullException.ThrowIfNull(name);
        bool removed = Names?.Dests is { } tree && tree.Remove(name);
        return RemoveLegacyDestination(name.Bytes) | removed;
    }

    /// <summary>Creates new page labels that number every page in decimal from 1, replacing any the catalog has.</summary>
    /// <returns>The page labels; give ranges their style with <see cref="PdfPageLabels.SetRange"/>.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.7.2, Table 29 (<c>PageLabels</c>, PDF 1.3), and §12.4.2: a new number tree holding the range for page index 0
    /// the clause requires, <c>&lt;&lt; /S /D &gt;&gt;</c>. A tree it replaces stays in the file until a save that removes unreachable
    /// objects. A mutation.
    /// </remarks>
    public PdfPageLabels CreatePageLabels()
    {
        PdfNumberTree tree = CreateNumberTree([KeyValuePair.Create(0, (CosObject)PageLabelDictionary(PdfPageLabelStyle.Arabic, null, 1))]);
        Catalog[PageLabelsKey] = tree.RootReference!;
        return new PdfPageLabels(this, GetNumberTreeReader(tree.RootReference)!);
    }

    /// <summary>Builds a page label dictionary (Table 161) with only the entries that differ from the defaults.</summary>
    internal static CosDictionary PageLabelDictionary(PdfPageLabelStyle style, string? prefix, int firstNumber)
    {
        var dictionary = new CosDictionary();
        string? name = style switch
        {
            PdfPageLabelStyle.None => null,
            PdfPageLabelStyle.Arabic => "D",
            PdfPageLabelStyle.UppercaseRoman => "R",
            PdfPageLabelStyle.LowercaseRoman => "r",
            PdfPageLabelStyle.UppercaseLetters => "A",
            PdfPageLabelStyle.LowercaseLetters => "a",
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, "The style shall be one of Table 161's."),
        };
        if (name is not null)
        {
            dictionary[PageLabelStyleKey] = new CosName(name);
        }

        if (!string.IsNullOrEmpty(prefix))
        {
            dictionary[PageLabelPrefixKey] = new CosString(TextStringEncoder.Encode(prefix));
        }

        if (firstNumber != 1)
        {
            dictionary[PageLabelStartKey] = new CosInteger(firstNumber);
        }

        return dictionary;
    }

    /// <summary>The key of the <c>Dests</c> tree that reads as <paramref name="name"/>, as <see cref="GetNamedDestination(string)"/> finds it.</summary>
    private static CosString? FindDestinationKey(PdfNameTree tree, string name)
    {
        if (TextStringEncoder.TryEncodePdfDoc(name, out byte[]? pdfDoc) && tree.ContainsKey(new CosString(pdfDoc)))
        {
            return new CosString(pdfDoc);
        }

        var utf16 = new CosString(TextStringEncoder.EncodeUtf16(name));
        if (tree.ContainsKey(utf16))
        {
            return utf16;
        }

        foreach ((CosString key, _) in tree.Reader.EnumerateRaw())
        {
            if (string.Equals(key.DecodeText(), name, StringComparison.Ordinal))
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>The catalog's name dictionary, created (indirect) when absent or not a dictionary.</summary>
    /// <remarks>ISO 32000-2 §7.7.4, Table 32. Shared by every writer of a name tree it holds (<c>Dests</c>, <c>EmbeddedFiles</c>).</remarks>
    internal CosDictionary NameDictionaryForWriting()
    {
        if (Catalog.TryGetValue(NavigationNames.Names, out CosObject? entry) && Resolve(entry) is CosDictionary names)
        {
            return names;
        }

        names = new CosDictionary();
        Catalog[NavigationNames.Names] = AddObject(names);
        return names;
    }

    private PdfNameTree CreateDestsTree(CosDictionary names)
    {
        PdfNameTree tree = CreateNameTree([]);
        names[NavigationNames.Dests] = tree.RootReference!;
        return tree;
    }

    /// <summary>Removes the catalog's PDF 1.1 <c>Dests</c> entry whose name has <paramref name="bytes"/>.</summary>
    private bool RemoveLegacyDestination(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 0
            && !bytes.Contains((byte)0)
            && Catalog.TryGetValue(NavigationNames.Dests, out CosObject? entry)
            && Resolve(entry) is CosDictionary dests
            && dests.Remove(new CosName(bytes));
}
