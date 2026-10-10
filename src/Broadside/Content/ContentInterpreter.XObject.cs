using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Content;

/// <summary>XObjects (Table 86: <c>Do</c>) and inline images (Table 90: <c>BI ID EI</c>).</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.8 to §8.10. <c>Do</c> looks its name up in the current resources' <c>XObject</c> subdictionary: an image is painted
/// into the unit square of user space (one <see cref="ImageEvent"/>), a form's content runs as a nested stream (§8.10.1, see the
/// <c>Nesting</c> part), and a PostScript XObject (deprecated, §8.8.2) is ignored. An image or form whose <c>OC</c> is off is not
/// drawn (§8.11.3.3).
/// </para>
/// <para>
/// The reader already delivers an inline image whole: <c>BI</c> with its dictionary as written (abbreviated keys included) and the
/// data range up to <c>EI</c>, never tokenized (issue #60's <c>InlineImageEnd</c>); <c>ID</c> or <c>EI</c> reaching here stand outside
/// any inline image. The image event carries issue #60's <see cref="Images.PdfImage"/> (abbreviations expanded, colour space resolved
/// in the current resources) besides the dictionary and data as written.
/// </para>
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private static readonly CosName XObjectName = new("XObject");
    private static readonly CosName SubtypeName = new("Subtype");
    private static readonly CosName ImageMaskName = new("ImageMask");
    private static readonly CosName StructParentName = new("StructParent");
    private static readonly PdfVersion Pdf20 = new(2, 0);

    private void ExecuteXObject(ContentOperatorCode code, ContentOperands operands, ReadOperator op, ReadOnlySpan<byte> content, int offset)
    {
        switch (code)
        {
            case ContentOperatorCode.PaintXObject:
                PaintXObject(operands[0].Bytes, offset);
                break;
            case ContentOperatorCode.BeginInlineImage:
                PaintInlineImage(operands[0], content.Slice(op.DataStart, op.DataLength), op, content, offset);
                break;
            default:
                Report(ContentIssue.InlineImageInvalid, offset, "ID or EI outside an inline image; ignored.");
                break;
        }
    }

    private void PaintXObject(ReadOnlySpan<byte> name, int offset)
    {
        PdfDocument document = _context.Document;
        CosObject? value = document.ColorSpaces.FindResource(_context.Resources, XObjectName, name, out _);
        if (value is null)
        {
            Report(ContentIssue.XObjectMissing, offset, "Do names an XObject that is not in the resources' XObject dictionary; ignored.");
            return;
        }

        if (document.Resolve(value) is not CosStream stream)
        {
            Report(ContentIssue.XObjectInvalid, offset, "Do names an XObject that is not a stream; ignored.");
            return;
        }

        CosObject? subtype = stream.Dictionary.TryGetValue(SubtypeName, out CosObject? entry) ? document.Resolve(entry) : null;
        switch (subtype)
        {
            case CosName { Value: "Form" }:
                PaintForm(document.ContentResources.GetForm(stream, value as CosReference), name, offset, _processor);
                break;
            case CosName { Value: "Image" }:
                PaintImageXObject(stream, value as CosReference, name);
                break;
            case CosName { Value: "PS" }:
                Report(ContentIssue.PostScriptXObject, offset, "Do names a PostScript XObject (deprecated, §8.8.2); it is not interpreted.");
                break;
            default:
                Report(ContentIssue.XObjectInvalid, offset, "Do names a stream whose Subtype is not Form, Image or PS; ignored.");
                break;
        }
    }

    private void PaintImageXObject(CosStream stream, CosReference? reference, ReadOnlySpan<byte> name)
    {
        CosDictionary dictionary = stream.Dictionary;
        bool stencil = dictionary.TryGetValue(ImageMaskName, out CosObject? mask) && _context.Document.Resolve(mask) is CosBoolean { Value: true };
        if (IgnoresColorOperators && !stencil)
        {
            Report(ContentIssue.ColorOperatorIgnored, -1, "An image that is not an image mask is painted inside a d1 glyph or an uncoloured pattern, where only image masks are allowed; it is ignored.");
            return;
        }

        if ((_events & ContentEvents.Images) == 0)
        {
            return;
        }

        bool hidden = _context.IsHidden || (_tracker is { } tracker && !tracker.IsObjectVisible(dictionary));
        if (hidden && (_events & ContentEvents.HiddenContent) == 0)
        {
            return;
        }

        PdfDocument document = _context.Document;
        Images.PdfImage? model = document.GetImage((CosObject?)reference ?? stream);
        var image = new ImageEvent
        {
            Stream = stream,
            Reference = reference,
            ResourceName = name,
            Image = model,
            IsStencil = model?.IsStencil ?? stencil,
            Ctm = State.Ctm,
            StructParent = Annotations.AnnotationValues.ReadInteger(document, dictionary.TryGetValue(StructParentName, out CosObject? key) ? key : null),
            IsHidden = hidden,
        };
        _processor.PaintImage(image, _context);
    }

    /// <summary>An inline image (§8.9.7): the dictionary as written and the encoded data, painted into the unit square.</summary>
    private void PaintInlineImage(ContentOperand dictionary, ReadOnlySpan<byte> data, ReadOperator op, ReadOnlySpan<byte> content, int offset)
    {
        ContentOperands entries = dictionary.Items;
        bool stencil = false;
        bool hasLength = false;
        for (int index = 0; index + 1 < entries.Count; index += 2)
        {
            ContentOperand key = entries[index];
            if (key.IsName("IM"u8) || key.IsName("ImageMask"u8))
            {
                stencil = entries[index + 1].Boolean;
            }
            else if (key.IsName("L"u8) || key.IsName("Length"u8))
            {
                hasLength = true;
            }
        }

        if (!hasLength && _context.Document.Version >= Pdf20)
        {
            Report(ContentIssue.InlineImageLengthMissing, offset, "An inline image in a PDF 2.0 file has no L (Length), which §8.9.7 requires; its end was found by scanning.");
        }

        if (IgnoresColorOperators && !stencil)
        {
            Report(ContentIssue.ColorOperatorIgnored, offset, "An inline image that is not an image mask is painted inside a d1 glyph or an uncoloured pattern, where only image masks are allowed; it is ignored.");
            return;
        }

        if ((_events & ContentEvents.Images) == 0 || (_context.IsHidden && (_events & ContentEvents.HiddenContent) == 0))
        {
            return;
        }

        int partStart = _parts.Count > 0 ? _parts[_part].Start : 0;
        var written = new ContentOperator
        {
            Code = op.Code,
            Keyword = content.Slice(op.KeywordStart, op.KeywordLength),
            Operands = _arena.Operands,
            Data = data,
            Stream = CurrentStream,
            PartIndex = _part,
            Offset = op.Start - partStart,
            Length = op.End - op.Start,
            KeywordOffset = op.KeywordStart - partStart,
        };
        var image = new ImageEvent
        {
            IsInline = true,
            Image = _context.GetInlineImage(written),
            InlineDictionary = dictionary,
            InlineData = data,
            IsStencil = stencil,
            Ctm = State.Ctm,
            IsHidden = _context.IsHidden,
        };
        _processor.PaintImage(image, _context);
    }
}
