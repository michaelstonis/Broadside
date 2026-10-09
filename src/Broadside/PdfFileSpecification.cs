using System.Buffers;
using System.Text;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside;

/// <summary>
/// A file specification: a reference from the document to another file, external or embedded, in string or dictionary form. A live
/// view over its COS object.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §7.11 (PDF 1.1). §7.11.2: a file specification string divides into components separated by <c>/</c>; a component's
/// literal solidus is written <c>\/</c> (the PDF string <c>(in\\/out)</c>); a leading <c>/</c> makes it absolute, otherwise it is
/// relative to the containing file, and <c>..</c> moves up a level. §7.11.3, Table 43: the dictionary form adds the file system
/// (<c>FS</c>), per-platform names, the file identifier, the volatile flag, embedded files (<c>EF</c>), related files (<c>RF</c>), a
/// description, a collection item, a thumbnail, an encrypted payload and the associated-file relationship.
/// </para>
/// <para>
/// Name preference (Table 43 says a reader shall use <c>UF</c> instead of <c>F</c>): <c>UF</c>, then <c>F</c>, then the deprecated
/// <c>Unix</c>, <c>Mac</c> and <c>DOS</c>. The embedded file stream is chosen from <c>EF</c> in the same key order.
/// </para>
/// <para>
/// File names are untrusted: they may hold <c>..</c>, absolute paths, backslashes, NUL or reserved device names. The library never
/// combines them with a file-system path; <see cref="SafeFileName"/> gives a sanitised last component for callers that do.
/// </para>
/// </remarks>
public sealed class PdfFileSpecification
{
    /// <summary>The <c>EF</c>/name preference order of Table 43.</summary>
    private static readonly CosName[] NameKeys =
        [FileAndLayerNames.UF, FileAndLayerNames.F, FileAndLayerNames.Unix, FileAndLayerNames.Mac, FileAndLayerNames.DOS];

    /// <summary>The characters no platform allows in a file name.</summary>
    private static readonly SearchValues<char> Invalid = SearchValues.Create(
        "\0\u0001\u0002\u0003\u0004\u0005\u0006\u0007\b\t\n\u000b\f\r\u000e\u000f\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f\u007f:*?\"<>|");

    private readonly PdfDocument _document;

    internal PdfFileSpecification(PdfDocument document, CosObject value, CosReference? reference)
    {
        _document = document;
        Value = value;
        Reference = reference;
    }

    /// <summary>Gets the file specification as stored: a <see cref="CosString"/> or a <see cref="CosDictionary"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.1.</remarks>
    public CosObject Value { get; }

    /// <summary>Gets the file specification dictionary, or <see langword="null"/> for the string form.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43.</remarks>
    public CosDictionary? Dictionary => Value as CosDictionary;

    /// <summary>Gets the indirect reference the specification was reached through, or <see langword="null"/> when it is direct.</summary>
    public CosReference? Reference { get; }

    /// <summary>Gets the file system the specification is interpreted by (<c>FS</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43. <c>URL</c> is the only standard file system (§7.11.5).</remarks>
    public CosName? FileSystem => Dictionary is { } dictionary ? ViewReading.Name(_document, dictionary, FileAndLayerNames.FS) : null;

    /// <summary>Gets a value indicating whether the file system is <c>URL</c>: the file name is a uniform resource locator (RFC 3986).</summary>
    /// <remarks>ISO 32000-2 §7.11.5.</remarks>
    public bool IsUrl => FileSystem is { } fileSystem && fileSystem.Equals(FileAndLayerNames.URL);

    /// <summary>Gets the raw string the file name comes from: the string form itself, else <c>UF</c>, <c>F</c>, <c>Unix</c>, <c>Mac</c> or <c>DOS</c>.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (<c>UF</c> PDF 1.7; <c>Unix</c>, <c>Mac</c>, <c>DOS</c> deprecated in PDF 2.0).</remarks>
    public CosString? FileNameString
    {
        get
        {
            if (Value is CosString value)
            {
                return value;
            }

            foreach (CosName key in NameKeys)
            {
                if (ViewReading.Get(_document, (CosDictionary)Value, key) is CosString name)
                {
                    return name;
                }
            }

            return null;
        }
    }

    /// <summary>Gets the file specification string, decoded as text, or <see langword="null"/> when the dictionary names no file.</summary>
    /// <remarks>ISO 32000-2 §7.11.2 and §7.11.3. <c>UF</c> is a text string (§7.9.2.2); the other keys are decoded the same way.</remarks>
    public string? FileName => FileNameString?.DecodeText();

    /// <summary>Gets a value indicating whether <see cref="FileName"/> is an absolute specification (it begins with <c>/</c>).</summary>
    /// <remarks>ISO 32000-2 §7.11.2.2.</remarks>
    public bool IsAbsolute => FileName is { Length: > 0 } name && name[0] == '/';

    /// <summary>
    /// Gets the components of <see cref="FileName"/>: split at each <c>/</c>, with an escaped solidus (<c>\/</c>) kept inside its
    /// component and the escape removed. A leading <c>/</c> gives no empty first component; <c>..</c> components are kept as written.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.11.2.1. For a URL (<see cref="IsUrl"/>) the components are the URL's path segments as written.</remarks>
    public IReadOnlyList<string> PathComponents => FileName is { } name ? SplitComponents(name) : [];

    /// <summary>
    /// Gets the last component of <see cref="FileName"/>, made safe to use as a file name on any platform, or <see langword="null"/>
    /// when nothing usable is left. Backslashes and drive prefixes count as separators; characters no platform allows
    /// (<c>NUL</c>, control characters, <c>/ \ : * ? " &lt; &gt; |</c>) become <c>_</c>; trailing dots and spaces are removed; a
    /// reserved device name (<c>CON</c>, <c>NUL</c>, <c>COM1</c>, ...) is prefixed with <c>_</c>; <c>.</c> and <c>..</c> give
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>Not a spec concept: file names are untrusted input (CWE-22). The library itself never writes a file under any name.</remarks>
    public string? SafeFileName => FileName is { } name ? Sanitize(name) : null;

    /// <summary>Gets the description of the file (<c>Desc</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 1.6).</remarks>
    public string? Description => Dictionary is { } dictionary ? ViewReading.Text(_document, dictionary, FileAndLayerNames.Desc) : null;

    /// <summary>Gets a value indicating whether the file is volatile and shall not be cached (<c>V</c>). Default <see langword="false"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 1.2).</remarks>
    public bool IsVolatile => Dictionary is { } dictionary && ViewReading.Boolean(_document, dictionary, FileAndLayerNames.V, fallback: false);

    /// <summary>Gets the file identifier of the referenced file (<c>ID</c>, two byte strings), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43, and §14.4.</remarks>
    public CosArray? FileIdentifier => Dictionary is { } dictionary ? ViewReading.Get(_document, dictionary, FileAndLayerNames.ID) as CosArray : null;

    /// <summary>Gets the relationship of the associated file to the object that refers to it (<c>AFRelationship</c>).</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 2.0), and §14.13. Default <see cref="PdfFileRelationship.Unspecified"/>; a second-class name reads as <see cref="PdfFileRelationship.Other"/>.</remarks>
    public PdfFileRelationship Relationship => RelationshipName.Value switch
    {
        "Source" => PdfFileRelationship.Source,
        "Data" => PdfFileRelationship.Data,
        "Alternative" => PdfFileRelationship.Alternative,
        "Supplement" => PdfFileRelationship.Supplement,
        "EncryptedPayload" => PdfFileRelationship.EncryptedPayload,
        "FormData" => PdfFileRelationship.FormData,
        "Schema" => PdfFileRelationship.Schema,
        "Unspecified" => PdfFileRelationship.Unspecified,
        _ => PdfFileRelationship.Other,
    };

    /// <summary>Gets the <c>AFRelationship</c> name as written, or <c>Unspecified</c> when absent.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 2.0).</remarks>
    public CosName RelationshipName =>
        (Dictionary is { } dictionary ? ViewReading.Name(_document, dictionary, FileAndLayerNames.AFRelationship) : null) ?? FileAndLayerNames.Unspecified;

    /// <summary>Gets the embedded file stream (<c>EF</c>, by key preference <c>UF</c>, <c>F</c>, <c>Unix</c>, <c>Mac</c>, <c>DOS</c>), or <see langword="null"/>.</summary>
    /// <exception cref="Diagnostics.DiagnosticException">In strict mode, when <c>EF</c> holds no stream (<c>EmbeddedFileMissing</c>).</exception>
    /// <remarks>
    /// ISO 32000-2 §7.11.3, Table 43 (PDF 1.3), and §7.11.4. When <c>EF</c> is present but none of its entries is a stream, the
    /// specification is still readable; the embedded file reads as absent with an <c>EmbeddedFileMissing</c> diagnostic.
    /// </remarks>
    public PdfEmbeddedFile? EmbeddedFile
    {
        get
        {
            if (Dictionary is not { } dictionary || ViewReading.Get(_document, dictionary, FileAndLayerNames.EF) is not { } entry)
            {
                return null;
            }

            if (entry is CosDictionary files)
            {
                foreach (CosName key in NameKeys)
                {
                    if (ViewReading.Get(_document, files, key) is CosStream stream)
                    {
                        return new PdfEmbeddedFile(_document, stream, ViewReading.ReferenceOf(files, key));
                    }
                }
            }

            ViewReading.Warn(_document, DiagnosticCodes.EmbeddedFileMissing, "The file specification's EF entry holds no embedded file stream; the file reads as not embedded.", Reference);
            return null;
        }
    }

    /// <summary>Gets the related files of the embedded file (<c>RF</c>, the array under the same key <see cref="EmbeddedFile"/> comes from).</summary>
    /// <remarks>
    /// ISO 32000-2 §7.11.3, Table 43 (PDF 1.3), and §7.11.4.2: an array of name strings, each followed by an embedded file stream.
    /// A pair whose name is not a string or whose file is not a stream is skipped with a <c>RelatedFilesInvalid</c> diagnostic.
    /// </remarks>
    public IReadOnlyList<PdfRelatedFile> RelatedFiles
    {
        get
        {
            if (Dictionary is not { } dictionary || ViewReading.Get(_document, dictionary, FileAndLayerNames.RF) is not CosDictionary related)
            {
                return [];
            }

            CosObject? entry = null;
            foreach (CosName key in NameKeys)
            {
                entry = ViewReading.Get(_document, related, key);
                if (entry is not null)
                {
                    break;
                }
            }

            if (entry is not CosArray array)
            {
                if (entry is not null)
                {
                    Report();
                }

                return [];
            }

            var files = new List<PdfRelatedFile>(array.Count / 2);
            bool invalid = array.Count % 2 != 0;
            for (int index = 0; index + 1 < array.Count; index += 2)
            {
                if (_document.Resolve(array[index]) is CosString name && _document.Resolve(array[index + 1]) is CosStream stream)
                {
                    files.Add(new PdfRelatedFile(name, new PdfEmbeddedFile(_document, stream, array[index + 1] as CosReference)));
                }
                else
                {
                    invalid = true;
                }
            }

            if (invalid)
            {
                Report();
            }

            return files;

            void Report() => ViewReading.Warn(_document, DiagnosticCodes.RelatedFilesInvalid, "The related files array is not pairs of a name string and an embedded file stream; malformed pairs are skipped.", Reference);
        }
    }

    /// <summary>Gets the collection item (<c>CI</c>) that supplies this file's values in a portable collection, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 1.7), and §7.11.6.</remarks>
    public PdfCollectionItem? CollectionItem =>
        Dictionary is { } dictionary && ViewReading.Get(_document, dictionary, FileAndLayerNames.CI) is CosDictionary item ? new PdfCollectionItem(_document, item) : null;

    /// <summary>Gets the thumbnail image of the file (<c>Thumb</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 2.0), and §12.3.4.</remarks>
    public CosStream? Thumbnail => Dictionary is { } dictionary ? ViewReading.Get(_document, dictionary, FileAndLayerNames.Thumb) as CosStream : null;

    /// <summary>Gets the encrypted payload dictionary (<c>EP</c>), or <see langword="null"/>. Exposed as is; the payload is not unwrapped.</summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43 (PDF 2.0), and §7.6.7.</remarks>
    public CosDictionary? EncryptedPayload => Dictionary is { } dictionary ? ViewReading.Get(_document, dictionary, FileAndLayerNames.EP) as CosDictionary : null;

    /// <summary>Creates the view over <paramref name="value"/>, resolving it; <see langword="null"/> when it is neither a string nor a dictionary.</summary>
    internal static PdfFileSpecification? Create(PdfDocument document, CosObject? value)
    {
        CosObject resolved = document.Resolve(value);
        return resolved is CosString or CosDictionary ? new PdfFileSpecification(document, resolved, value as CosReference) : null;
    }

    /// <summary>Splits a file specification string at unescaped solidus characters (§7.11.2.1).</summary>
    private static List<string> SplitComponents(string name)
    {
        var components = new List<string>();
        var current = new StringBuilder();
        int start = name.Length > 0 && name[0] == '/' ? 1 : 0;
        for (int index = start; index < name.Length; index++)
        {
            char c = name[index];
            if (c == '\\' && index + 1 < name.Length && name[index + 1] == '/')
            {
                current.Append('/');
                index++;
            }
            else if (c == '/')
            {
                components.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        components.Add(current.ToString());
        return components;
    }

    private static string? Sanitize(string name)
    {
        // Treat every separator any platform uses as a separator: solidus, reverse solidus, and a drive colon.
        int last = name.AsSpan().LastIndexOfAny('/', '\\');
        string component = name[(last + 1)..];
        var builder = new StringBuilder(component.Length);
        foreach (char c in component)
        {
            builder.Append(Invalid.Contains(c) ? '_' : c);
        }

        string result = builder.ToString().TrimEnd('.', ' ');
        if (result.Length == 0 || result is "." or "..")
        {
            return null;
        }

        if (result.Length > 255)
        {
            result = result[..255];
        }

        string stem = result.Split('.')[0];
        bool reserved = stem.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
        return reserved ? "_" + result : result;
    }
}
