using System.Text;
using Broadside.Filters;
using Broadside.Objects;

namespace Broadside;

/// <summary>The document metadata entries Table 349 defines, each with the XMP property it corresponds to.</summary>
internal enum MetadataEntry
{
    /// <summary><c>Title</c>, <c>dc:title</c> (its default alternative).</summary>
    Title,

    /// <summary><c>Author</c>, <c>dc:creator</c>.</summary>
    Author,

    /// <summary><c>Subject</c>, <c>dc:description</c> (its default alternative).</summary>
    Subject,

    /// <summary><c>Keywords</c>, <c>pdf:Keywords</c>.</summary>
    Keywords,

    /// <summary><c>Creator</c>, <c>xmp:CreatorTool</c>.</summary>
    Creator,

    /// <summary><c>Producer</c>, <c>pdf:Producer</c>.</summary>
    Producer,

    /// <summary><c>Trapped</c>, <c>pdf:Trapped</c>.</summary>
    Trapped,

    /// <summary><c>CreationDate</c>, <c>xmp:CreateDate</c>.</summary>
    CreationDate,

    /// <summary><c>ModDate</c>, <c>xmp:ModifyDate</c>.</summary>
    ModificationDate,
}

/// <summary>
/// Writes document metadata to the Info dictionary and the XMP packet of the catalog's metadata stream and keeps the two in sync
/// (ISO 32000-2 §14.3; ISO 16684-1). One instance is one edit: <see cref="Set"/> the entries that change, then <see cref="Commit"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where each entry goes.</b> The XMP packet is written when the document has a metadata stream, when it is PDF 2.0 ("Metadata
/// streams are the preferred method in PDF 2.0", §14.3.1), or when the edit asks for one and the document is PDF 1.4 or later (the
/// version metadata streams arrived in). The Info dictionary is written in a document before PDF 2.0, created when it has none; in a
/// PDF 2.0 document it keeps only <c>CreationDate</c>, <c>ModDate</c> and keys Table 349 does not define, is never created, and
/// loses the deprecated entries (§14.3.3), which move to the packet first when it does not have them.
/// </para>
/// <para>
/// <b>Sync.</b> An entry the edit sets is written to both places (or removed from both). An entry it does not set is copied to the
/// place that lacks it, as §14.3.4 allows for dates "as long as both are fully equivalent"; two values that disagree are left as they
/// are (§14.3.4: "a PDF processor should leave the inconsistent values unchanged"). Dates are written to the second, in the same
/// offset in both places. <c>xmp:MetadataDate</c> is set to the edit's time whenever the packet is written.
/// </para>
/// <para>
/// <b>The packet.</b> Every property of the existing packet is kept, in order, and written back in canonical form
/// (<see cref="XmpPacketWriter"/>); only the properties of the entries above and <c>xmp:MetadataDate</c> change. A packet that cannot
/// be read is replaced by a new one. The stream's data is written unfiltered (ISO 32000-2 §14.3.2 lets non-PDF tools find a packet
/// only in unencoded data), with <c>Type</c> <c>Metadata</c> and <c>Subtype</c> <c>XML</c>; whether it is encrypted on save is the
/// security handler's decision (<c>EncryptMetadata</c>, §7.6.3.2).
/// </para>
/// </remarks>
internal sealed class DocumentMetadataWriter
{
    private static readonly PdfVersion Pdf14 = new(1, 4);
    private static readonly PdfVersion Pdf20 = new(2, 0);
    private static readonly CosName InfoKey = new("Info");
    private static readonly CosName MetadataKey = new("Metadata");
    private static readonly CosName MetadataType = new("Metadata");
    private static readonly CosName SubtypeKey = new("Subtype");
    private static readonly CosName XmlSubtype = new("XML");
    private static readonly CosName[] EncodingEntries = [FilterNames.Filter, FilterNames.DecodeParms, FilterNames.F, new("FFilter"), new("FDecodeParms"), FilterNames.DL];
    private static readonly MetadataEntry[] Entries = Enum.GetValues<MetadataEntry>();

    private readonly PdfDocument _document;
    private readonly Dictionary<MetadataEntry, object?> _changes = [];

    public DocumentMetadataWriter(PdfDocument document) => _document = document;

    /// <summary>Sets an entry: a <see cref="string"/> for text and <see cref="MetadataEntry.Trapped"/>, a <see cref="MetadataDate"/> for a date, <see langword="null"/> to remove it.</summary>
    public DocumentMetadataWriter Set(MetadataEntry entry, object? value)
    {
        _changes[entry] = value;
        return this;
    }

    /// <summary>Gets whether either place has a value for <paramref name="entry"/>.</summary>
    public bool Has(MetadataEntry entry)
    {
        (CosDictionary? info, CosReference? reference) = ReadInfo();
        XmpPacket? packet = _document.Metadata?.Packet;
        return ReadInfoValue(info, reference, entry) is not null || ReadXmpValue(packet, entry) is not null;
    }

    /// <summary>Gets whether the Info dictionary holds an entry PDF 2.0 deprecates there (every Table 349 entry but the two dates, §14.3.3).</summary>
    public bool HasDeprecatedInfoEntry()
    {
        (CosDictionary? info, CosReference? reference) = ReadInfo();
        return info is not null && Entries.Any(entry =>
            entry is not (MetadataEntry.CreationDate or MetadataEntry.ModificationDate) && ReadInfoValue(info, reference, entry) is not null);
    }

    /// <summary>Writes the edit.</summary>
    /// <param name="now">The edit's time: <c>xmp:MetadataDate</c>.</param>
    /// <param name="createPacket">Whether a document before PDF 2.0 without a metadata stream gets one.</param>
    /// <param name="layout">
    /// The version whose layout to write when it is not the document's own: the version a save raises the document to, once the
    /// version plan has decided it (§14.3.3). <see langword="null"/> is <see cref="PdfDocument.Version"/>.
    /// </param>
    public void Commit(MetadataDate now, bool createPacket, PdfVersion? layout = null)
    {
        PdfVersion version = layout ?? _document.Version;
        bool isPdf20 = version >= Pdf20;
        (CosDictionary? info, CosReference? infoReference) = ReadInfo();
        (CosStream? stream, XmpPacket? packet) = ReadMetadata();
        bool writePacket = stream is not null || isPdf20 || (createPacket && version >= Pdf14);

        List<XmpProperty> properties = packet is null ? [] : [.. packet.Properties];
        var infoValues = new Dictionary<MetadataEntry, object?>();
        foreach (MetadataEntry entry in Entries)
        {
            object? inInfo = ReadInfoValue(info, infoReference, entry);
            object? inPacket = ReadXmpValue(packet, entry);
            bool keptInInfo = !isPdf20 || entry is MetadataEntry.CreationDate or MetadataEntry.ModificationDate;
            if (_changes.TryGetValue(entry, out object? value))
            {
                if (writePacket)
                {
                    SetXmp(properties, entry, value);
                }

                infoValues[entry] = keptInInfo ? value : null;
                continue;
            }

            if (writePacket && inInfo is not null && inPacket is null)
            {
                SetXmp(properties, entry, inInfo);
            }

            if (!keptInInfo)
            {
                if (inInfo is not null)
                {
                    infoValues[entry] = null;
                }
            }
            else if (inPacket is not null && inInfo is null && (info is not null || !isPdf20))
            {
                infoValues[entry] = inPacket;
            }
        }

        if (writePacket)
        {
            SetXmp(properties, null, now);
            if (packet is null)
            {
                properties.Insert(0, XmpProperty.Simple(XmpNamespaces.DublinCore, "format", "application/pdf"));
            }

            WritePacket(stream, properties, packet?.About ?? string.Empty);
        }

        WriteInfo(info, infoValues, isPdf20);
    }

    /// <summary>
    /// Writes the Info dictionary's <paramref name="entries"/> dates again in the PDF 2.0 form, without the terminating apostrophe of
    /// PDF 1.7 and earlier (ISO 32000-2 §7.9.4 NOTE 2): for a save that raises the document to 2.0 after they were written in the
    /// form of its version then. A date already in that form, or one the Info dictionary does not hold, is left as it is.
    /// </summary>
    public void FormatDatesForPdf20(IEnumerable<MetadataEntry> entries)
    {
        (CosDictionary? info, CosReference? reference) = ReadInfo();
        foreach (MetadataEntry entry in entries)
        {
            if (info is null || ReadInfoValue(info, reference, entry) is not MetadataDate date)
            {
                continue;
            }

            var text = new CosString(Encoding.ASCII.GetBytes(date.ToPdfDate(terminatingApostrophe: false)));
            CosName key = InfoKeyOf(entry);
            if (!text.Equals(_document.Resolve(info.GetValueOrDefault(key))))
            {
                info[key] = text;
            }
        }
    }

    private (CosDictionary? Info, CosReference? Reference) ReadInfo()
    {
        _document.Trailer.TryGetValue(InfoKey, out CosObject? entry);
        return (_document.Resolve(entry) as CosDictionary, entry as CosReference);
    }

    private (CosStream? Stream, XmpPacket? Packet) ReadMetadata()
    {
        PdfMetadata? metadata = _document.Metadata;
        return (metadata?.Stream, metadata?.Packet);
    }

    private object? ReadInfoValue(CosDictionary? dictionary, CosReference? reference, MetadataEntry entry)
    {
        if (dictionary is null)
        {
            return null;
        }

        PdfDocumentInformation info = new(_document, dictionary, reference);
        return entry switch
        {
            MetadataEntry.Title => info.Title,
            MetadataEntry.Author => info.Author,
            MetadataEntry.Subject => info.Subject,
            MetadataEntry.Keywords => info.Keywords,
            MetadataEntry.Creator => info.Creator,
            MetadataEntry.Producer => info.Producer,
            MetadataEntry.Trapped => dictionary.ContainsKey(InfoKeyOf(entry)) ? info.Trapped.ToString() : null,
            MetadataEntry.CreationDate => info.CreationDate is { } date ? MetadataDate.From(date) : null,
            _ => info.ModificationDate is { } date ? MetadataDate.From(date) : null,
        };
    }

    private static object? ReadXmpValue(XmpPacket? packet, MetadataEntry entry)
    {
        if (packet is null)
        {
            return null;
        }

        return entry switch
        {
            MetadataEntry.Title => packet.Title,
            MetadataEntry.Author => packet.Creators is { Count: > 0 } creators ? string.Join("; ", creators) : null,
            MetadataEntry.Subject => packet.Description,
            MetadataEntry.Keywords => packet.Keywords,
            MetadataEntry.Creator => packet.CreatorTool,
            MetadataEntry.Producer => packet.Producer,
            MetadataEntry.Trapped => packet.Trapped is "True" or "False" or "Unknown" ? packet.Trapped : null,
            MetadataEntry.CreationDate => packet.CreateDate is { } date ? MetadataDate.From(date) : null,
            _ => packet.ModifyDate is { } date ? MetadataDate.From(date) : null,
        };
    }

    private static CosName InfoKeyOf(MetadataEntry entry) => new(entry switch
    {
        MetadataEntry.CreationDate => "CreationDate",
        MetadataEntry.ModificationDate => "ModDate",
        _ => entry.ToString(),
    });

    private static (string NamespaceName, string Name) XmpNameOf(MetadataEntry? entry) => entry switch
    {
        MetadataEntry.Title => (XmpNamespaces.DublinCore, "title"),
        MetadataEntry.Author => (XmpNamespaces.DublinCore, "creator"),
        MetadataEntry.Subject => (XmpNamespaces.DublinCore, "description"),
        MetadataEntry.Keywords => (XmpNamespaces.Pdf, "Keywords"),
        MetadataEntry.Creator => (XmpNamespaces.Xmp, "CreatorTool"),
        MetadataEntry.Producer => (XmpNamespaces.Pdf, "Producer"),
        MetadataEntry.Trapped => (XmpNamespaces.Pdf, "Trapped"),
        MetadataEntry.CreationDate => (XmpNamespaces.Xmp, "CreateDate"),
        MetadataEntry.ModificationDate => (XmpNamespaces.Xmp, "ModifyDate"),
        _ => (XmpNamespaces.Xmp, "MetadataDate"),
    };

    /// <summary>Sets (or, for <see langword="null"/>, removes) one entry's property in place, keeping its position; <paramref name="entry"/> <see langword="null"/> is <c>xmp:MetadataDate</c>.</summary>
    private static void SetXmp(List<XmpProperty> properties, MetadataEntry? entry, object? value)
    {
        (string namespaceName, string name) = XmpNameOf(entry);
        int index = properties.FindIndex(property => property.Name == name && property.NamespaceName == namespaceName);
        XmpProperty? existing = index >= 0 ? properties[index] : null;
        XmpProperty? replacement = value switch
        {
            null => null,
            MetadataDate date => XmpProperty.Simple(namespaceName, name, date.ToXmpDate()),
            string text when entry is MetadataEntry.Title or MetadataEntry.Subject => LanguageAlternative(namespaceName, name, text, existing),
            string text when entry is MetadataEntry.Author =>
                XmpProperty.Array(namespaceName, name, XmpPropertyKind.OrderedArray, [XmpProperty.Simple(XmpNamespaces.Rdf, "li", text)]),
            string text => XmpProperty.Simple(namespaceName, name, text),
            _ => throw new ArgumentException("A metadata value is text or a date.", nameof(value)),
        };
        if (replacement is null)
        {
            if (index >= 0)
            {
                properties.RemoveAt(index);
            }
        }
        else if (index >= 0)
        {
            properties[index] = replacement;
        }
        else
        {
            properties.Add(replacement);
        }
    }

    /// <summary>A language alternative whose default (<c>x-default</c>) item is <paramref name="text"/>, keeping the other languages of <paramref name="existing"/>.</summary>
    private static XmpProperty LanguageAlternative(string namespaceName, string name, string text, XmpProperty? existing)
    {
        var items = new List<XmpProperty> { XmpProperty.Simple(XmpNamespaces.Rdf, "li", text, "x-default") };
        if (existing is { Kind: XmpPropertyKind.Alternative })
        {
            items.AddRange(existing.Items.Where(item => !string.Equals(item.Language, "x-default", StringComparison.OrdinalIgnoreCase)));
        }

        return XmpProperty.Array(namespaceName, name, XmpPropertyKind.Alternative, items);
    }

    private void WritePacket(CosStream? stream, List<XmpProperty> properties, string about)
    {
        byte[] data = XmpPacketWriter.Write(properties, about);
        if (stream is null)
        {
            var dictionary = new CosDictionary { [KnownNames.Type] = MetadataType, [SubtypeKey] = XmlSubtype };
            _document.Catalog[MetadataKey] = _document.AddObject(new CosStream(dictionary, data));
            return;
        }

        foreach (CosName key in EncodingEntries)
        {
            _ = stream.Dictionary.Remove(key);
        }

        if (!MetadataType.Equals(_document.Resolve(stream.Dictionary.GetValueOrDefault(KnownNames.Type))))
        {
            stream.Dictionary[KnownNames.Type] = MetadataType;
        }

        if (!XmlSubtype.Equals(_document.Resolve(stream.Dictionary.GetValueOrDefault(SubtypeKey))))
        {
            stream.Dictionary[SubtypeKey] = XmlSubtype;
        }

        stream.EncodedData = data;
    }

    private void WriteInfo(CosDictionary? info, Dictionary<MetadataEntry, object?> values, bool isPdf20)
    {
        if (values.Count == 0 || (info is null && (isPdf20 || values.Values.All(value => value is null))))
        {
            return;
        }

        if (info is null)
        {
            info = [];
            _document.Trailer[InfoKey] = _document.AddObject(info);
        }

        foreach ((MetadataEntry entry, object? value) in values)
        {
            CosName key = InfoKeyOf(entry);
            switch (value)
            {
                case null:
                    _ = info.Remove(key);
                    break;
                case MetadataDate date:
                    info[key] = new CosString(Encoding.ASCII.GetBytes(date.ToPdfDate(terminatingApostrophe: !isPdf20)));
                    break;
                case string text when entry == MetadataEntry.Trapped:
                    info[key] = new CosName(text);
                    break;
                case string text:
                    info[key] = new CosString(TextStringEncoder.Encode(text));
                    break;
            }
        }
    }
}
