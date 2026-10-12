using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Forms;

/// <summary>The XFA resource of an interactive form: XML Data Package packets, kept as data (parse-and-preserve).</summary>
/// <remarks>
/// ISO 32000-2 Annex K and §12.7.3, Table 224 (<c>XFA</c>, PDF 1.5, deprecated in PDF 2.0). The packets are neither parsed nor
/// rendered; the interactive form's fields remain what Broadside reads. Opening a document with a non-empty XFA resource records an
/// <c>XfaFormPresent</c> Information diagnostic, and <c>XfaFormDynamic</c> too when the catalog's <c>NeedsRendering</c> is true.
/// </remarks>
public sealed class PdfXfaForm
{
    private readonly PdfAcroForm _form;

    internal PdfXfaForm(PdfAcroForm form, CosObject value)
    {
        _form = form;
        Value = value;
    }

    /// <summary>Gets the XFA entry, resolved: one stream holding the whole XDP document, or an array of packet names and streams.</summary>
    /// <remarks>ISO 32000-2 Annex K.</remarks>
    public CosObject Value { get; }

    /// <summary>
    /// Gets a value indicating whether the form is dynamic: the catalog's <c>NeedsRendering</c> is true, so the pages are placeholders
    /// and the content comes from the XFA template.
    /// </summary>
    /// <remarks>ISO 32000-2 §7.7.2, Table 29 (<c>NeedsRendering</c>, PDF 1.7, deprecated in PDF 2.0), and Annex K.</remarks>
    public bool IsDynamic => FieldTree.Get(_form.Document, _form.Document.Catalog, FormNames.NeedsRendering) is CosBoolean { Value: true };

    /// <summary>Gets the packets in order: one unnamed packet when the entry is a stream, else one per name and stream pair.</summary>
    /// <remarks>
    /// ISO 32000-2 Annex K: the array form is <c>[name1 stream1 name2 stream2 ...]</c>, names being text strings such as
    /// <c>template</c> and <c>datasets</c>. A pair whose name is not a string or whose value is not a stream, and an odd element
    /// left over, are skipped with an <c>XfaPacketsInvalid</c> diagnostic.
    /// </remarks>
    public IReadOnlyList<PdfXfaPacket> Packets
    {
        get
        {
            PdfDocument document = _form.Document;
            switch (Value)
            {
                case CosStream stream:
                    return [new PdfXfaPacket(null, stream, XfaReference())];
                case CosArray array:
                    var packets = new List<PdfXfaPacket>(array.Count / 2);
                    bool invalid = array.Count % 2 != 0;
                    for (int index = 0; index + 1 < array.Count; index += 2)
                    {
                        if (document.Resolve(array[index]) is CosString name && document.Resolve(array[index + 1]) is CosStream packet)
                        {
                            packets.Add(new PdfXfaPacket(name.DecodeText(), packet, array[index + 1] as CosReference));
                        }
                        else
                        {
                            invalid = true;
                        }
                    }

                    if (invalid)
                    {
                        _form.Report(DiagnosticCodes.XfaPacketsInvalid, "The XFA array shall alternate packet names (text strings) and streams (Annex K); the elements that do not are skipped.");
                    }

                    return packets;
                default:
                    return [];
            }
        }
    }

    private CosReference? XfaReference() => _form.Dictionary.TryGetValue(FormNames.XFA, out CosObject? entry) ? entry as CosReference : null;
}

/// <summary>One packet of an XFA resource: a named part of the XML Data Package, such as <c>template</c> or <c>datasets</c>.</summary>
/// <remarks>ISO 32000-2 Annex K. Decode <see cref="Stream"/> with <see cref="PdfDocument.DecodeStream(CosStream)"/> to read its XML.</remarks>
public sealed class PdfXfaPacket
{
    internal PdfXfaPacket(string? name, CosStream stream, CosReference? reference)
    {
        Name = name;
        Stream = stream;
        Reference = reference;
    }

    /// <summary>Gets the packet name, or <see langword="null"/> when the XFA entry is a single stream.</summary>
    public string? Name { get; }

    /// <summary>Gets the stream holding the packet's XML.</summary>
    public CosStream Stream { get; }

    /// <summary>Gets the indirect reference to <see cref="Stream"/>, or <see langword="null"/>.</summary>
    public CosReference? Reference { get; }
}
