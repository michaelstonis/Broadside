using Broadside.Objects;

namespace Broadside;

/// <content>Writing document metadata and dating modifications on save (issue #162): ISO 32000-2 §14.3.</content>
public sealed partial class PdfDocument
{
    /// <summary>Whether the document was made by <see cref="Create()"/> and has not been re-pointed at a written file since.</summary>
    private bool _isNew;

    /// <summary>Whether a modification date was set through <see cref="PdfDocumentProperties.ModificationDate"/> since the last save.</summary>
    private bool _modificationDateSet;

    /// <summary>The Info dates written since the last save, in the form of the document's version then (issue #291).</summary>
    private readonly HashSet<MetadataEntry> _writtenDates = [];

    /// <summary>Writes one metadata entry to the Info dictionary and the XMP packet (<see cref="DocumentMetadataWriter"/>).</summary>
    internal void WriteMetadata(MetadataEntry entry, object? value)
    {
        new DocumentMetadataWriter(this).Set(entry, value).Commit(Now(), createPacket: true);
        if (entry is MetadataEntry.CreationDate or MetadataEntry.ModificationDate)
        {
            _writtenDates.Add(entry);
        }

        if (entry == MetadataEntry.ModificationDate)
        {
            _modificationDateSet = true;
        }
    }

    /// <summary>
    /// Before a save plans what to write: when <paramref name="options"/> asks for it and the document changed (or is new), dates the
    /// modification (and, for a new document without one, the creation) in the Info dictionary and the XMP packet (§14.3.4).
    /// </summary>
    private void DateModification(PdfSaveOptions options)
    {
        if (_isNew && !options.IncrementalUpdate)
        {
            // §14.4: "When a PDF file is first written, both identifiers shall be set to the same value." The identifiers of the
            // in-memory file Create() built are placeholders; without them the writer derives both from what it writes.
            _ = Trailer.Remove(KnownNames.ID);
        }

        if (!options.UpdateModificationDate || !(_isNew || _loader.HasChanges))
        {
            _modificationDateSet = false;
            return;
        }

        MetadataDate now = Now();
        var writer = new DocumentMetadataWriter(this);
        if (!_modificationDateSet)
        {
            writer.Set(MetadataEntry.ModificationDate, now);
            _writtenDates.Add(MetadataEntry.ModificationDate);
        }

        if (_isNew && !writer.Has(MetadataEntry.CreationDate))
        {
            writer.Set(MetadataEntry.CreationDate, now);
            _writtenDates.Add(MetadataEntry.CreationDate);
        }

        writer.Commit(now, createPacket: false);
        _modificationDateSet = false;
    }

    /// <summary>
    /// Once the version plan has decided the version <paramref name="written"/> the save writes, and every refusal check has passed:
    /// lays the metadata out for that version. A save that raises the document from <paramref name="before"/> to PDF 2.0 applies the
    /// layout a PDF 2.0 document's metadata edits get (<see cref="DocumentMetadataWriter.Commit"/>): the entries Table 349 deprecates
    /// move from the Info dictionary to the XMP packet, which is created when the document has none, and Info keeps only
    /// <c>CreationDate</c>, <c>ModDate</c> and keys Table 349 does not define (ISO 32000-2 §14.3.3; issue #301). Then the dates written
    /// since the last save take the form of <paramref name="written"/> (<see cref="FormatDatesFor"/>).
    /// </summary>
    /// <remarks>
    /// The layout is written when there is something for it to do, as a PDF 2.0 document's would be: when Info holds a deprecated
    /// entry, or a date was written since the last save (the dating, or a date property set). A raise of a document whose metadata
    /// nobody touched and that has nothing to move adds no packet.
    /// </remarks>
    /// <returns>Whether objects the plan had already planned changed or were added, so the save must be planned again.</returns>
    private bool LayOutMetadataFor(PdfVersion before, PdfVersion written)
    {
        bool laidOut = false;
        if (before < Pdf20 && written >= Pdf20)
        {
            var writer = new DocumentMetadataWriter(this);
            if (_writtenDates.Count > 0 || writer.HasDeprecatedInfoEntry())
            {
                writer.Commit(Now(), createPacket: true, layout: written);
                laidOut = true;
            }
        }

        FormatDatesFor(written);
        return laidOut;
    }

    /// <summary>
    /// Once the version plan has decided the version <paramref name="written"/> the save writes: when it is PDF 2.0 or later, writes the
    /// dates written since the last save (by <see cref="DateModification"/> or through <see cref="PdfDocumentProperties"/>, in the form
    /// of the version the document had then) again in the 2.0 form, without the terminating apostrophe (ISO 32000-2 §7.9.4 NOTE 2). The
    /// Info dictionary they are in changed, so the plan already writes it; the writer serializes it as it is when it writes.
    /// </summary>
    private void FormatDatesFor(PdfVersion written)
    {
        if (written >= Pdf20 && _writtenDates.Count > 0)
        {
            new DocumentMetadataWriter(this).FormatDatesForPdf20(_writtenDates);
        }

        _writtenDates.Clear();
    }

    private MetadataDate Now() => MetadataDate.FromInstant(TimeProvider.GetLocalNow());
}
