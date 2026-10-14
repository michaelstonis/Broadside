using Broadside.Objects;

namespace Broadside;

/// <content>Writing document metadata and dating modifications on save (issue #162): ISO 32000-2 §14.3.</content>
public sealed partial class PdfDocument
{
    /// <summary>Whether the document was made by <see cref="Create()"/> and has not been re-pointed at a written file since.</summary>
    private bool _isNew;

    /// <summary>Whether a modification date was set through <see cref="PdfDocumentProperties.ModificationDate"/> since the last save.</summary>
    private bool _modificationDateSet;

    /// <summary>Writes one metadata entry to the Info dictionary and the XMP packet (<see cref="DocumentMetadataWriter"/>).</summary>
    internal void WriteMetadata(MetadataEntry entry, object? value)
    {
        new DocumentMetadataWriter(this).Set(entry, value).Commit(Now(), createPacket: true);
        if (entry == MetadataEntry.ModificationDate)
        {
            _modificationDateSet = true;
        }
    }

    /// <summary>
    /// Once a save has passed every check that can refuse it (issue #300), before it plans the file it writes: when
    /// <paramref name="options"/> asks for it and the document changed (or is new), dates the modification (and, for a new document
    /// without one, the creation) in the Info dictionary and the XMP packet (§14.3.4).
    /// </summary>
    /// <returns>Whether the document changed: dated, or a new document's placeholder identifiers dropped.</returns>
    private bool DateModification(PdfSaveOptions options)
    {
        bool changed = false;
        if (_isNew && !options.IncrementalUpdate)
        {
            // §14.4: "When a PDF file is first written, both identifiers shall be set to the same value." The identifiers of the
            // in-memory file Create() built are placeholders; without them the writer derives both from what it writes.
            changed = Trailer.Remove(KnownNames.ID);
        }

        if (!options.UpdateModificationDate || !(_isNew || _loader.HasChanges))
        {
            _modificationDateSet = false;
            return changed;
        }

        MetadataDate now = Now();
        var writer = new DocumentMetadataWriter(this);
        if (!_modificationDateSet)
        {
            writer.Set(MetadataEntry.ModificationDate, now);
        }

        if (_isNew && !writer.Has(MetadataEntry.CreationDate))
        {
            writer.Set(MetadataEntry.CreationDate, now);
        }

        writer.Commit(now, createPacket: false);
        _modificationDateSet = false;
        return true;
    }

    private MetadataDate Now() => MetadataDate.FromInstant(TimeProvider.GetLocalNow());
}
