using System.IO.Compression;

namespace Broadside;

/// <summary>Options for one save: how the written file is laid out, how its objects are numbered, which objects it holds and which
/// streams are compressed.</summary>
/// <remarks>
/// ISO 32000-2 §7.5. Separate from <see cref="PdfOptions"/>, which configures reading, because a document can be saved several times
/// in different layouts. The defaults write a classic cross-reference table, which every reader supports, and compress new and
/// changed streams.
/// </remarks>
public sealed class PdfSaveOptions
{
    private PdfCrossReferenceLayout _crossReferenceLayout = PdfCrossReferenceLayout.Table;
    private PdfObjectNumbering _objectNumbering = PdfObjectNumbering.Automatic;
    private PdfStreamCompression _streamCompression = PdfStreamCompression.ChangedStreams;
    private CompressionLevel _compressionLevel = CompressionLevel.Optimal;

    /// <summary>
    /// Gets or sets how the file stores its cross-reference information and objects. The default is
    /// <see cref="PdfCrossReferenceLayout.Table"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.5.4, §7.5.7, §7.5.8. See <see cref="PdfCrossReferenceLayout"/> for the combinations and their versions.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined layout.</exception>
    public PdfCrossReferenceLayout CrossReferenceLayout
    {
        get => _crossReferenceLayout;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a cross-reference layout.");
            }

            _crossReferenceLayout = value;
        }
    }

    /// <summary>
    /// Gets or sets how the written objects are numbered. The default, <see cref="PdfObjectNumbering.Automatic"/>, keeps the
    /// document's numbers unless they are too large or too sparse to write compactly.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.3.10, §7.5.4. See <see cref="PdfObjectNumbering"/>.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined numbering.</exception>
    public PdfObjectNumbering ObjectNumbering
    {
        get => _objectNumbering;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not an object numbering.");
            }

            _objectNumbering = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether objects the trailer does not reach are left out of the written file (garbage collection).
    /// The default is <see langword="false"/>: every object the cross-reference information lists is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.5 and §7.3.10. An object is reachable when a chain of indirect references leads to it from the trailer: from the
    /// catalog (<c>Root</c>), the document information dictionary (<c>Info</c>) or any other trailer entry. A page removed from the page
    /// tree, an annotation removed from its page's <c>Annots</c>, and everything only they referred to, are not written. An object
    /// reached only as a stream's indirect <c>Length</c> is not written either; the stream gets a direct length (§7.3.8.2).
    /// </para>
    /// <para>
    /// Objects the library does not interpret (JavaScript, XFA, 3D, rich media, multimedia) are kept like any other object when they
    /// are reachable, with their data unchanged. With <see cref="PdfObjectNumbering.Automatic"/>, a dropped object's number becomes a
    /// free entry with its generation raised by one (§7.5.4).
    /// </para>
    /// </remarks>
    public bool RemoveUnreachableObjects { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the save appends an incremental update to the document's file instead of writing a
    /// complete new one. The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.5.6. The written file is every byte of the file the document was read from, unchanged, followed by the objects
    /// that were changed or added since it was read (or since the last incremental save), a cross-reference section listing only them
    /// and the objects removed, and a trailer whose <c>Prev</c> entry points at the previous section. The section has the form of the
    /// file's newest one: a cross-reference stream after a stream (§7.5.8), a classic table otherwise, including after a hybrid file's
    /// table (§7.5.8.4). Objects keep their numbers; a removed object's entry is free with its generation raised by one.
    /// </para>
    /// <para>
    /// <see cref="CrossReferenceLayout"/>, <see cref="ObjectNumbering"/> and <see cref="RemoveUnreachableObjects"/> do not apply to
    /// an update. <see cref="StreamCompression"/> applies to the streams the update writes, which are all new or changed, so
    /// <see cref="PdfStreamCompression.ChangedStreams"/> and <see cref="PdfStreamCompression.AllStreams"/> compress the same ones.
    /// Once the last byte is written, the document reads from the written file and its objects are no longer dirty, so the next
    /// incremental save appends only what changes after this one.
    /// </para>
    /// </remarks>
    public bool IncrementalUpdate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file is written linearized, so that a viewer reading it over a network can show the
    /// first page before the rest has arrived. The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 Annex F. The file starts with a linearization parameter dictionary (F.3.3) and a first-page cross-reference section
    /// (F.3.4); then come the catalog and the objects needed at open (F.3.5), the primary hint stream with the page offset, shared
    /// object and (when the document has an outline) outline hint tables (F.3.6, F.4), the first page's objects (F.3.7), each other
    /// page with the objects only it uses (F.3.8), the objects several of those pages share (F.3.9), everything else (F.3.10), and the
    /// main cross-reference section (F.3.11). Objects are renumbered as F.3.1 requires, so <see cref="ObjectNumbering"/> does not
    /// apply, and the attributes pages inherit from the page tree are written on each page instead (F.3.10). The parts follow qpdf's
    /// partition, which is validated against Acrobat; the first page is page 0.
    /// </para>
    /// <para>
    /// <see cref="PdfCrossReferenceLayout.Table"/> writes two classic tables, <see cref="PdfCrossReferenceLayout.Stream"/> two
    /// cross-reference streams, and <see cref="PdfCrossReferenceLayout.StreamWithObjectStreams"/> also stores the objects that no page
    /// and nothing needed at open uses in object streams at the end of the file (F.3.1). The hint stream is compressed unless
    /// <see cref="StreamCompression"/> is <see cref="PdfStreamCompression.None"/>. A linearized file cannot be an incremental update
    /// (F.1, G.7), and a document without pages, or whose page tree lists a page twice, cannot be linearized.
    /// </para>
    /// </remarks>
    public bool Linearize { get; set; }

    /// <summary>Sets <see cref="Linearize"/>.</summary>
    /// <param name="linearize">Whether the file is written linearized.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 Annex F.</remarks>
    public PdfSaveOptions WithLinearize(bool linearize)
    {
        Linearize = linearize;
        return this;
    }

    /// <summary>
    /// Gets or sets how the saved file is encrypted with the standard security handler; <see langword="null"/> (the default) keeps
    /// the document's own encryption: an encrypted document is saved encrypted with its own key, an unencrypted one unencrypted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.6. A document saved with encryption has a new encryption dictionary and file encryption key, and every string
    /// and stream is encrypted with it (§7.6.2), whatever encryption the document was read with; the old encryption dictionary is not
    /// written. Encrypted output is not deterministic: keys, salts and initialization vectors are random (§7.6.3, §7.6.4.4.7).
    /// </para>
    /// <para>
    /// Keeping the document's own encryption re-encrypts every string and stream with the key it was opened with, under the
    /// encryption dictionary it was read with; ciphertext is never copied from the source file. A document that has an integrity MAC
    /// (ISO/TS 32004), or whose permissions require one, gets a new one computed over the whole saved file with its own key and
    /// <c>KDFSalt</c>; one opened without its file encryption key cannot, and is refused.
    /// </para>
    /// <para>
    /// An incremental update (<see cref="IncrementalUpdate"/>) always keeps the document's encryption (§7.5.6: earlier revisions stay
    /// as written), so it refuses this option and <see cref="RemoveEncryption"/>.
    /// </para>
    /// </remarks>
    public PdfEncryptionOptions? Encryption { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the document is saved unencrypted even though it was read encrypted. The default is
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.6.2: the trailer has no <c>Encrypt</c> entry, and every string and stream is written as plaintext. A stream that
    /// selected a crypt filter (§7.4.10) is written without its <c>Crypt</c> filter. Data the document's credentials could not
    /// decrypt (a crypt filter they do not unlock) cannot be written unencrypted, and such a save is refused. Cannot be combined with
    /// <see cref="Encryption"/>. An incremental update cannot remove encryption from an encrypted document (its earlier revisions
    /// stay encrypted); for a document read as not encrypted because its <c>Encrypt</c> entry is damaged
    /// (<see cref="PdfDocument.IsEncrypted"/> is <see langword="false"/>) it drops the entry from the update's trailer.
    /// </remarks>
    public bool RemoveEncryption { get; set; }

    /// <summary>Sets <see cref="Encryption"/>.</summary>
    /// <param name="encryption">How to encrypt the saved file; <see langword="null"/> to keep the document's own encryption.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.6.</remarks>
    public PdfSaveOptions WithEncryption(PdfEncryptionOptions? encryption)
    {
        Encryption = encryption;
        return this;
    }

    /// <summary>Sets <see cref="RemoveEncryption"/>.</summary>
    /// <param name="remove">Whether the document is saved unencrypted.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.6.2.</remarks>
    public PdfSaveOptions WithRemoveEncryption(bool remove)
    {
        RemoveEncryption = remove;
        return this;
    }

    /// <summary>
    /// Gets or sets how the saved file is encrypted for certificate recipients with the public-key security handler;
    /// <see langword="null"/> (the default) keeps the document's own encryption.
    /// </summary>
    /// <remarks>
    /// ISO 32000-2 §7.6.5. Like <see cref="Encryption"/>, the saved file has a new encryption dictionary and file encryption key,
    /// whatever encryption the document was read with, and its output is not deterministic (random seed, CMS keys and IVs). Cannot be
    /// combined with <see cref="Encryption"/> or <see cref="RemoveEncryption"/>, and an incremental update (<see cref="IncrementalUpdate"/>)
    /// refuses it. A save without any recipient is refused.
    /// </remarks>
    public PdfPublicKeyEncryptionOptions? PublicKeyEncryption { get; set; }

    /// <summary>Sets <see cref="PublicKeyEncryption"/>.</summary>
    /// <param name="encryption">How to encrypt the saved file for certificate recipients; <see langword="null"/> to keep the document's own encryption.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.6.5.</remarks>
    public PdfSaveOptions WithPublicKeyEncryption(PdfPublicKeyEncryptionOptions? encryption)
    {
        PublicKeyEncryption = encryption;
        return this;
    }

    /// <summary>
    /// Gets or sets whether a save of a changed document records the save as the document's most recent modification. The default is
    /// <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §14.3.3 and §14.3.4: "When writing the time and date of the most recent modification ... a PDF processor shall
    /// ensure that the data in the document information dictionary and the document level metadata stream – if both are written –
    /// are fully equivalent." When the document has changed since it was read (or since its last incremental save), the save first
    /// sets its modification date to the engine's clock (<see cref="PdfOptions.UseTimeProvider"/>), to the second, the way
    /// <see cref="PdfDocumentProperties.ModificationDate"/> does: the Info dictionary's <c>ModDate</c> and the XMP packet's
    /// <c>xmp:ModifyDate</c> and <c>xmp:MetadataDate</c>. A new document (<see cref="PdfDocument.Create()"/>) also gets its creation
    /// date the first time it is saved. A date set through <see cref="PdfDocumentProperties.ModificationDate"/> since the last save
    /// is kept.
    /// </para>
    /// <para>
    /// The document is changed by this, as by any other edit. A document with no change is written exactly as before, byte for byte
    /// where its objects allow.
    /// </para>
    /// </remarks>
    public bool UpdateModificationDate { get; set; } = true;

    /// <summary>Sets <see cref="UpdateModificationDate"/>.</summary>
    /// <param name="update">Whether a save of a changed document dates the modification.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §14.3.4.</remarks>
    public PdfSaveOptions WithUpdateModificationDate(bool update)
    {
        UpdateModificationDate = update;
        return this;
    }

    /// <summary>
    /// Gets or sets the PDF/A conformance level the saved file claims and is checked against, or <see cref="PdfAConformance.None"/>
    /// (the default) for none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 19005-2 (PDF/A-2), ISO 19005-3 (PDF/A-3) and ISO 19005-4 (PDF/A-4), cited by the rule identifiers of veraPDF's validation
    /// profiles, the reference the output is validated with: see <see cref="PdfAConformance"/> for the levels (2b, 2u, 3b, 3u, 4, 4f). Level U also checks that every
    /// glyph shown maps to Unicode; PDF/A-3 allows embedded files of any kind when each is an associated file with a relationship. The save first checks the document, changing nothing: each rule it cannot meet (a font
    /// program not embedded, encryption, an action PDF/A forbids, an annotation without an appearance, a device colour space the output
    /// intent does not cover, a feature later than PDF 1.7, ...) is reported as a <c>PdfANonConformant</c> warning naming the rule. In
    /// strict mode the first one throws and nothing is written or changed. In lenient mode the file is saved without the PDF/A changes
    /// below and without claiming conformance (a <c>PdfANotIdentified</c> information diagnostic says so): it never claims a
    /// conformance it does not have.
    /// </para>
    /// <para>
    /// When every check passes, the save adds what PDF/A requires: an output intent (<c>GTS_PDFA1</c>) with Broadside's own sRGB
    /// profile unless the document has one with a destination profile, the XMP metadata stream with the PDF/A identification
    /// (<c>pdfaid:part</c>, <c>pdfaid:conformance</c>) mirroring the document information, and the file identifiers; it writes the
    /// header as PDF 1.7 (PDF/A-2 and PDF/A-3 are based on ISO 32000-1), lowering a new document's 2.0; PDF/A-3's associated files
    /// (AF, AFRelationship) do not raise it. PDF/A-4 is based on PDF 2.0: the header is 2.0 (a read 1.x document is raised), the
    /// identification adds <c>pdfaid:rev</c>, and the trailer keeps no document information dictionary.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined level.</exception>
    public PdfAConformance PdfAConformance
    {
        get;
        set => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Not a defined PDF/A conformance level.");
    }

    /// <summary>Sets <see cref="PdfAConformance"/>.</summary>
    /// <param name="conformance">The PDF/A conformance level, or <see cref="PdfAConformance.None"/>.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conformance"/> is not a defined level.</exception>
    /// <remarks>ISO 19005-2, ISO 19005-3 and ISO 19005-4 (veraPDF PDF/A-2B, -2U, -3B, -3U, -4 and -4F validation profiles).</remarks>
    public PdfSaveOptions WithPdfAConformance(PdfAConformance conformance)
    {
        PdfAConformance = conformance;
        return this;
    }

    /// <summary>
    /// Gets or sets the PDF/UA conformance the saved file claims and is checked against, or <see cref="PdfUAConformance.None"/> (the
    /// default) for none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ISO 14289-1 (PDF/UA-1), cited by the rule identifiers of veraPDF's PDF/UA-1 validation profile. The save first checks the document,
    /// changing nothing: each failure condition it finds (no structure tree, content neither tagged nor an artifact, a non-standard type
    /// without a role mapping, no title, no or a malformed language, a figure without alternate text, a header cell without a scope, a
    /// skipped heading level, a note without an ID, an untagged or undescribed annotation, a font not embedded or not mapped to Unicode,
    /// a feature later than PDF 1.7, ...) is reported as a <c>PdfUANonConformant</c> warning naming the rule. In strict mode the first
    /// one throws and nothing is written or changed. In lenient mode the file is saved without the changes below and without claiming
    /// conformance (a <c>PdfUANotIdentified</c> information diagnostic says so).
    /// </para>
    /// <para>
    /// When every check passes, the save writes what PDF/UA-1 requires of the file: the <c>pdfuaid:part</c> identification in the XMP
    /// metadata stream (with its extension schema when the file is also PDF/A), <c>DisplayDocTitle</c>, <c>MarkInfo</c> <c>Marked</c>
    /// and, on every page with annotations, <c>Tabs</c> <c>/S</c>; it writes the header as PDF 1.7. It combines with
    /// <see cref="PdfAConformance"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined conformance.</exception>
    public PdfUAConformance PdfUAConformance
    {
        get;
        set => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Not a defined PDF/UA conformance.");
    }

    /// <summary>Sets <see cref="PdfUAConformance"/>.</summary>
    /// <param name="conformance">The PDF/UA conformance, or <see cref="PdfUAConformance.None"/>.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="conformance"/> is not a defined conformance.</exception>
    /// <remarks>ISO 14289-1 (veraPDF PDF/UA-1 validation profile).</remarks>
    public PdfSaveOptions WithPdfUAConformance(PdfUAConformance conformance)
    {
        PdfUAConformance = conformance;
        return this;
    }

    /// <summary>Sets <see cref="IncrementalUpdate"/>.</summary>
    /// <param name="incremental">Whether the save appends an incremental update.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.5.6.</remarks>
    public PdfSaveOptions WithIncrementalUpdate(bool incremental)
    {
        IncrementalUpdate = incremental;
        return this;
    }

    /// <summary>Sets <see cref="ObjectNumbering"/>.</summary>
    /// <param name="numbering">The numbering.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="numbering"/> is not a defined numbering.</exception>
    /// <remarks>ISO 32000-2 §7.3.10, §7.5.4.</remarks>
    public PdfSaveOptions WithObjectNumbering(PdfObjectNumbering numbering)
    {
        ObjectNumbering = numbering;
        return this;
    }

    /// <summary>Sets <see cref="RemoveUnreachableObjects"/>.</summary>
    /// <param name="remove">Whether objects the trailer does not reach are left out.</param>
    /// <returns>These options.</returns>
    /// <remarks>ISO 32000-2 §7.5, §7.3.10.</remarks>
    public PdfSaveOptions WithRemoveUnreachableObjects(bool remove)
    {
        RemoveUnreachableObjects = remove;
        return this;
    }

    /// <summary>
    /// Gets or sets which unfiltered streams are compressed with the <c>FlateDecode</c> encoder. The default is
    /// <see cref="PdfStreamCompression.ChangedStreams"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.4. See <see cref="PdfStreamCompression"/> for which streams qualify.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined setting.</exception>
    public PdfStreamCompression StreamCompression
    {
        get => _streamCompression;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a stream compression setting.");
            }

            _streamCompression = value;
        }
    }

    /// <summary>
    /// Gets or sets how hard the <c>FlateDecode</c> encoder compresses the streams it writes, the object streams and cross-reference
    /// stream included. The default is <see cref="CompressionLevel.Optimal"/>.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.4.4.1: RFC 1951 leaves the effort to the encoder; every level decodes the same way.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined level.</exception>
    public CompressionLevel CompressionLevel
    {
        get => _compressionLevel;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a compression level.");
            }

            _compressionLevel = value;
        }
    }

    /// <summary>Sets <see cref="CrossReferenceLayout"/>.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="layout"/> is not a defined layout.</exception>
    /// <remarks>ISO 32000-2 §7.5.4, §7.5.7, §7.5.8.</remarks>
    public PdfSaveOptions WithCrossReferenceLayout(PdfCrossReferenceLayout layout)
    {
        CrossReferenceLayout = layout;
        return this;
    }

    /// <summary>Sets <see cref="StreamCompression"/>.</summary>
    /// <param name="compression">Which unfiltered streams to compress.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="compression"/> is not a defined setting.</exception>
    /// <remarks>ISO 32000-2 §7.4.4.</remarks>
    public PdfSaveOptions WithStreamCompression(PdfStreamCompression compression)
    {
        StreamCompression = compression;
        return this;
    }

    /// <summary>Sets <see cref="CompressionLevel"/>.</summary>
    /// <param name="level">How hard to compress.</param>
    /// <returns>These options.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not a defined level.</exception>
    /// <remarks>ISO 32000-2 §7.4.4.1.</remarks>
    public PdfSaveOptions WithCompressionLevel(CompressionLevel level)
    {
        CompressionLevel = level;
        return this;
    }
}
