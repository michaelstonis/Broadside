using System.Text;
using Broadside.Objects;
using Broadside.Security.Cryptography;
using Broadside.Writing;

namespace Broadside;

/// <content>Writing attachments and associated files (issue #163): ISO 32000-2 §7.11 and §14.13.</content>
public sealed partial class PdfDocument
{
    /// <summary>
    /// Embeds <paramref name="contents"/> as a new embedded file stream and returns a new file specification for it, attached to
    /// nothing yet.
    /// </summary>
    /// <param name="fileName">The file's name, written to the specification's <c>UF</c> (as a text string) and <c>F</c>.</param>
    /// <param name="contents">The file's bytes; copied.</param>
    /// <param name="options">Media type, description, dates and relationship; <see langword="null"/> for the defaults.</param>
    /// <returns>A live view of the new file specification. Attach it with <see cref="AddEmbeddedFile(string, PdfFileSpecification)"/>
    /// or <see cref="AddAssociatedFile"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// ISO 32000-2 §7.11.3, Table 43, and §7.11.4, Tables 44 and 45. Two new indirect objects: the stream (<c>Type</c>
    /// <c>EmbeddedFile</c>, <c>Subtype</c>, and <c>Params</c> with <c>Size</c>, the MD5 <c>CheckSum</c> and the dates given) and the
    /// specification (<c>Type</c> <c>Filespec</c>, <c>F</c>, <c>UF</c>, <c>EF</c> with <c>F</c> and <c>UF</c>, and <c>Desc</c> and
    /// <c>AFRelationship</c> when given). The stream is written unfiltered and compressed by the save
    /// (<see cref="PdfSaveOptions.StreamCompression"/>).
    /// </para>
    /// <para>
    /// A solidus in <paramref name="fileName"/> is written escaped (<c>\/</c>, §7.11.2.1) so the name stays one component.
    /// <c>F</c> holds the name in PDFDocEncoding, with <c>_</c> for each character it cannot encode; <c>UF</c> holds it exactly.
    /// Minimum versions: embedded file streams PDF 1.3, <c>Desc</c> 1.6, <c>UF</c> 1.7, <c>AFRelationship</c> 2.0; a later save raises
    /// the document's version to the highest one used, or refuses in strict mode (ADR 0003). A mutation: no
    /// read may run concurrently with it.
    /// </para>
    /// </remarks>
    public PdfFileSpecification CreateEmbeddedFile(string fileName, ReadOnlySpan<byte> contents, PdfEmbeddedFileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        options ??= new PdfEmbeddedFileOptions();

        var parameters = new CosDictionary { [FileAndLayerNames.Size] = new CosInteger(contents.Length) };
        if (options.CreationDate is { } created)
        {
            parameters[FileAndLayerNames.CreationDate] = DateString(created);
        }

        if (options.ModificationDate is { } modified)
        {
            parameters[FileAndLayerNames.ModDate] = DateString(modified);
        }

        byte[] checkSum = new byte[Md5.HashSize];
        Md5.HashData(contents, checkSum);
        parameters[FileAndLayerNames.CheckSum] = new CosString(checkSum, hexadecimal: true);

        var streamDictionary = new CosDictionary
        {
            [FileAndLayerNames.Type] = FileAndLayerNames.EmbeddedFile,
            [FileAndLayerNames.Subtype] = new CosName(options.MediaType ?? "application/octet-stream"),
            [FileAndLayerNames.Params] = parameters,
        };
        CosReference stream = AddObject(new CosStream(streamDictionary, contents.ToArray()));

        string escaped = fileName.Replace("/", "\\/", StringComparison.Ordinal);
        var specification = new CosDictionary
        {
            [FileAndLayerNames.Type] = FileAndLayerNames.Filespec,
            [FileAndLayerNames.F] = new CosString(PdfDocFileName(escaped)),
            [FileAndLayerNames.UF] = new CosString(TextStringEncoder.Encode(escaped)),
            [FileAndLayerNames.EF] = new CosDictionary { [FileAndLayerNames.F] = stream, [FileAndLayerNames.UF] = stream },
        };
        if (options.Description is { } description)
        {
            specification[FileAndLayerNames.Desc] = new CosString(TextStringEncoder.Encode(description));
        }

        if (options.Relationship is { } relationship)
        {
            specification[FileAndLayerNames.AFRelationship] = new CosName(relationship.ToString());
        }

        CosReference reference = AddObject(specification);

        // The minimum versions a save raises the document to (issue #141).
        RequireFeature(VersionedFeatures.EmbeddedFileStream);
        RequireFeature(VersionedFeatures.UnicodeFileName);
        if (options.Description is not null)
        {
            RequireFeature(VersionedFeatures.FileDescription);
        }

        if (options.Relationship is not null)
        {
            RequireFeature(VersionedFeatures.AssociatedFiles);
        }

        return new PdfFileSpecification(this, specification, reference);
    }

    /// <summary>
    /// Embeds <paramref name="contents"/> (see <see cref="CreateEmbeddedFile"/>) and lists the new file in the document's
    /// <c>EmbeddedFiles</c> name tree under <paramref name="fileName"/>.
    /// </summary>
    /// <param name="fileName">The file's name and its key in the tree.</param>
    /// <param name="contents">The file's bytes; copied.</param>
    /// <param name="options">Media type, description, dates and relationship; <see langword="null"/> for the defaults.</param>
    /// <returns>The new entry.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileName"/> is empty.</exception>
    /// <remarks>ISO 32000-2 §7.7.4, Table 32 (PDF 1.4), and §7.11.4. As <see cref="AddEmbeddedFile(string, PdfFileSpecification)"/>.</remarks>
    public PdfEmbeddedFileEntry AddEmbeddedFile(string fileName, ReadOnlySpan<byte> contents, PdfEmbeddedFileOptions? options = null) =>
        AddEmbeddedFile(fileName, CreateEmbeddedFile(fileName, contents, options));

    /// <summary>Lists <paramref name="file"/> in the document's <c>EmbeddedFiles</c> name tree under <paramref name="name"/>.</summary>
    /// <param name="name">The key, as text. An existing entry whose key decodes to the same text is replaced, keeping its key.</param>
    /// <param name="file">An indirect file specification of this document with an embedded file (<c>EF</c>).</param>
    /// <returns>The new entry.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, or <paramref name="file"/> belongs to another document, is direct, or embeds no file.
    /// </exception>
    /// <remarks>
    /// ISO 32000-2 §7.7.4, Table 32 (<c>EmbeddedFiles</c>, PDF 1.4), §7.9.6 and §7.11.4.1: the tree maps name strings to file
    /// specifications that refer to embedded file streams through <c>EF</c>, which makes the specification indirect (Table 43). The
    /// catalog's <c>Names</c> dictionary and the tree are created when absent. A new key is written as a text string (§7.9.2.2). A
    /// mutation: no read may run concurrently with it.
    /// </remarks>
    public PdfEmbeddedFileEntry AddEmbeddedFile(string name, PdfFileSpecification file)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(file);
        CheckOwnFile(file);
        if (file.Reference is not { } reference)
        {
            throw new ArgumentException("A file specification with an embedded file shall be indirect (ISO 32000-2 §7.11.3, Table 43).", nameof(file));
        }

        if (file.EmbeddedFile is null)
        {
            throw new ArgumentException("An EmbeddedFiles entry shall refer to an embedded file stream through EF (ISO 32000-2 §7.11.4.1).", nameof(file));
        }

        PdfNameTree tree = EmbeddedFilesTreeForWriting();
        RequireFeature(VersionedFeatures.EmbeddedFilesTree);
        CosString key = FindEmbeddedFileKeys(tree, name).FirstOrDefault() ?? new CosString(TextStringEncoder.Encode(name));
        tree.Set(key, reference);
        return new PdfEmbeddedFileEntry(key, file);
    }

    /// <summary>Removes the entries of the document's <c>EmbeddedFiles</c> name tree whose key decodes to <paramref name="name"/>.</summary>
    /// <param name="name">The key, as text.</param>
    /// <returns><see langword="true"/> when an entry was removed.</returns>
    /// <remarks>
    /// ISO 32000-2 §7.7.4, Table 32, and §7.9.6. Only the tree changes: the file specification and its stream stay objects of the
    /// document (an annotation or an <c>AF</c> array may still refer to them) until a save drops unreachable objects
    /// (<see cref="PdfSaveOptions.RemoveUnreachableObjects"/>). A mutation: no read may run concurrently with it.
    /// </remarks>
    public bool RemoveEmbeddedFile(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Names?.EmbeddedFiles is not { } tree)
        {
            return false;
        }

        bool removed = false;
        foreach (CosString key in FindEmbeddedFileKeys(tree, name))
        {
            removed |= tree.Remove(key);
        }

        return removed;
    }

    /// <summary>Associates <paramref name="file"/> with <paramref name="owner"/> by adding it to the owner's <c>AF</c> array.</summary>
    /// <param name="owner">
    /// The dictionary the file is associated with: the <see cref="Catalog"/>, a page's <see cref="PdfPage.Dictionary"/>, an
    /// annotation, a structure element, an XObject's stream dictionary, a document part or a marked-content property list.
    /// </param>
    /// <param name="file">A file specification dictionary of this document; its <c>AFRelationship</c> says how the file relates to the owner.</param>
    /// <exception cref="ArgumentException"><paramref name="file"/> belongs to another document or is a file specification string.</exception>
    /// <remarks>
    /// ISO 32000-2 §14.13.1 to §14.13.9 (PDF 2.0): <c>AF</c> is an array of file specification dictionaries; the specification is
    /// added by its indirect reference when it has one. A file already in the array is not added twice. An <c>AF</c> that is a single
    /// dictionary (read leniently as an array of one) becomes an array holding it and the new file; an <c>AF</c> that is neither is
    /// replaced. An embedded associated file should have a <c>Subtype</c> and <c>Params</c> with <c>ModDate</c> (§14.13.2), which
    /// <see cref="CreateEmbeddedFile"/> writes when <see cref="PdfEmbeddedFileOptions.ModificationDate"/> is set. Minimum version PDF
    /// 2.0, which a later save raises the document to (or refuses in strict mode; ADR 0003). A mutation: no read may run concurrently with it.
    /// </remarks>
    public void AddAssociatedFile(CosDictionary owner, PdfFileSpecification file)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(file);
        CheckOwnFile(file);
        if (file.Dictionary is null)
        {
            throw new ArgumentException("An associated file is a file specification dictionary (ISO 32000-2 §14.13.1).", nameof(file));
        }

        CosObject element = file.Reference ?? file.Value;
        RequireFeature(VersionedFeatures.AssociatedFiles);
        owner.TryGetValue(FileAndLayerNames.AF, out CosObject? entry);
        switch (Resolve(entry))
        {
            case CosArray files:
                if (!files.Any(existing => ReferenceEquals(Resolve(existing), file.Value)))
                {
                    files.Add(element);
                }

                break;
            case CosDictionary single when ReferenceEquals(single, file.Value):
                owner[FileAndLayerNames.AF] = new CosArray([entry!]);
                break;
            case CosDictionary:
                owner[FileAndLayerNames.AF] = new CosArray([entry!, element]);
                break;
            default:
                owner[FileAndLayerNames.AF] = new CosArray([element]);
                break;
        }
    }

    /// <summary>Removes <paramref name="file"/> from <paramref name="owner"/>'s <c>AF</c> array.</summary>
    /// <param name="owner">The dictionary the file is associated with.</param>
    /// <param name="file">The associated file, as read from <see cref="ReadAssociatedFiles"/> or created by <see cref="CreateEmbeddedFile"/>.</param>
    /// <returns><see langword="true"/> when the file was associated with the owner and is no longer.</returns>
    /// <remarks>
    /// ISO 32000-2 §14.13. An <c>AF</c> left empty is removed. The file specification stays an object of the document (see
    /// <see cref="RemoveEmbeddedFile"/>). A mutation: no read may run concurrently with it.
    /// </remarks>
    public bool RemoveAssociatedFile(CosDictionary owner, PdfFileSpecification file)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(file);
        if (!owner.TryGetValue(FileAndLayerNames.AF, out CosObject? entry))
        {
            return false;
        }

        switch (Resolve(entry))
        {
            case CosArray files:
                bool removed = false;
                for (int index = files.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(Resolve(files[index]), file.Value))
                    {
                        files.RemoveAt(index);
                        removed = true;
                    }
                }

                if (files.Count == 0)
                {
                    owner.Remove(FileAndLayerNames.AF);
                }

                return removed;
            case CosDictionary single when ReferenceEquals(single, file.Value):
                owner.Remove(FileAndLayerNames.AF);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Formats a date as a PDF date string, to the second (ISO 32000-2 §7.9.4).</summary>
    private static CosString DateString(DateTimeOffset date) =>
        new(Encoding.ASCII.GetBytes(MetadataDate.FromInstant(date).ToPdfDate(terminatingApostrophe: false)));

    /// <summary>The name in PDFDocEncoding, each character without a code replaced by <c>_</c> (for <c>F</c>, a byte string).</summary>
    private static byte[] PdfDocFileName(string name)
    {
        if (TextStringEncoder.TryEncodePdfDoc(name, out byte[]? bytes))
        {
            return bytes;
        }

        var builder = new StringBuilder(name.Length);
        foreach (Rune rune in name.EnumerateRunes())
        {
            builder.Append(rune.IsAscii && !Rune.IsControl(rune) ? (char)rune.Value : '_');
        }

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static List<CosString> FindEmbeddedFileKeys(PdfNameTree tree, string name) =>
        [.. tree.Where(entry => string.Equals(entry.Key.DecodeText(), name, StringComparison.Ordinal)).Select(entry => entry.Key)];

    /// <summary>The <c>EmbeddedFiles</c> tree, with the catalog's <c>Names</c> dictionary and the tree created when absent.</summary>
    private PdfNameTree EmbeddedFilesTreeForWriting()
    {
        CosDictionary names;
        if (Catalog.TryGetValue(NavigationNames.Names, out CosObject? entry) && Resolve(entry) is CosDictionary existing)
        {
            names = existing;
        }
        else
        {
            names = new CosDictionary();
            Catalog[NavigationNames.Names] = names;
        }

        if (names.TryGetValue(FileAndLayerNames.EmbeddedFiles, out CosObject? root) && GetNameTree(root) is { } tree)
        {
            return tree;
        }

        PdfNameTree created = CreateNameTree([]);
        names[FileAndLayerNames.EmbeddedFiles] = created.RootReference!;
        return created;
    }

    private void CheckOwnFile(PdfFileSpecification file)
    {
        if (!ReferenceEquals(file.Document, this))
        {
            throw new ArgumentException("The file specification belongs to another document.", nameof(file));
        }
    }
}
