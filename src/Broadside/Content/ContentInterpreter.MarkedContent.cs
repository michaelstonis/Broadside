using Broadside.Objects;

namespace Broadside.Content;

/// <summary>The marked-content operators of Table 352: <c>MP DP BMC BDC EMC</c>.</summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §14.6. A property list is an inline dictionary or a name in the current resources' <c>Properties</c> subdictionary
/// (§14.6.2); the event carries the inline operand or the resolved dictionary, and the <c>MCID</c> of either. Sequences nest; they
/// are scoped to one content stream (a page's <c>Contents</c> array is one stream), so a form's unclosed sequences are closed at the
/// form's end. Tags are reported as written, not role-mapped (§14.6.1, note 3).
/// </para>
/// <para>
/// Optional content (§8.11.3.2): a sequence tagged <c>OC</c> whose property list is an optional content group or membership
/// dictionary hides what it encloses when that is off; <see cref="ContentContext.IsHidden"/> follows the nesting through one
/// <see cref="PdfOptionalContentTracker"/> per run, so a form invoked inside a hidden section is hidden too.
/// </para>
/// <para>
/// Repairs: a named property list missing from the resources gives the sequence no properties (an <c>OC</c> section stays visible,
/// as in pdf.js) with <c>ContentPropertiesMissing</c>; <c>EMC</c> without an open sequence in the stream is ignored and sequences still
/// open at the end of a stream are closed there with implicit end events, both with <c>ContentMarkedContentUnbalanced</c>.
/// Sequences that interleave with <c>BT</c>/<c>ET</c> (Figure 9 forbids it, real files do it) are kept as written.
/// </para>
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private static readonly CosName OptionalContentTag = new("OC");
    private static readonly CosName OtherTag = new("MarkedContent");
    private static readonly CosName PropertiesName = new("Properties");

    private MarkedSequence[] _marked = new MarkedSequence[8];
    private byte[] _tagBytes = new byte[128];
    private int _markedCount;
    private int _markedFloor;
    private int _tagByteCount;
    private PdfOptionalContentTracker? _tracker;
    private PdfOptionalContentProperties? _trackerProperties;
    private PdfOptionalContentState? _trackerState;

    /// <summary>Gets the number of open marked-content sequences.</summary>
    public int MarkedContentDepth => _markedCount;

    private void ExecuteMarkedContent(ContentOperatorCode code, ContentOperands operands, int offset)
    {
        switch (code)
        {
            case ContentOperatorCode.BeginMarkedContent:
                BeginSequence(operands[0].Bytes, default, hasProperties: false, offset);
                break;
            case ContentOperatorCode.BeginMarkedContentWithProperties:
                BeginSequence(operands[0].Bytes, operands[1], hasProperties: true, offset);
                break;
            case ContentOperatorCode.EndMarkedContent:
                if (_markedCount <= _markedFloor)
                {
                    Report(ContentIssue.MarkedContentUnbalanced, offset, "EMC without a marked-content sequence open in this content stream; ignored.");
                    break;
                }

                EndSequence();
                break;
            case ContentOperatorCode.MarkPoint:
                MarkPoint(operands[0].Bytes, default, hasProperties: false, offset);
                break;
            case ContentOperatorCode.MarkPointWithProperties:
                MarkPoint(operands[0].Bytes, operands[1], hasProperties: true, offset);
                break;
        }
    }

    /// <summary>Starts optional-content tracking for a top-level run, reusing the tracker of the previous run when it fits.</summary>
    private void BeginMarkedContentTracking(PdfDocument document, ContentOptions options)
    {
        _markedCount = 0;
        _markedFloor = 0;
        _tagByteCount = 0;
        PdfOptionalContentProperties? properties = document.OptionalContent;
        if (properties is null)
        {
            _tracker = null;
        }
        else if (_tracker is not null && ReferenceEquals(_trackerProperties, properties) && ReferenceEquals(_trackerState, options.OptionalContentState))
        {
            _tracker.Reset();
        }
        else
        {
            _tracker = new PdfOptionalContentTracker(properties, options.OptionalContentState);
        }

        _trackerProperties = properties;
        _trackerState = options.OptionalContentState;
    }

    private void EndMarkedContentTracking()
    {
        _marked.AsSpan(0, _markedCount).Clear();
        _markedCount = 0;
        _tracker?.Reset();
    }

    /// <summary>Closes the sequences the running stream left open (§14.6: sequences shall not cross a content stream's end).</summary>
    private void CloseMarkedContent()
    {
        if (_markedCount > _markedFloor)
        {
            Report(ContentIssue.MarkedContentUnbalanced, -1, "The content stream ends inside marked-content sequences (BMC or BDC without EMC); they are ended there.");
            while (_markedCount > _markedFloor)
            {
                EndSequence();
            }
        }
    }

    private void BeginSequence(ReadOnlySpan<byte> tag, ContentOperand properties, bool hasProperties, int offset)
    {
        CosObject? resource = null;
        CosDictionary? named = hasProperties ? ResolveProperties(properties, offset, out resource) : null;
        int? mcid = hasProperties ? Mcid(properties, named) : null;
        bool optional = tag.SequenceEqual("OC"u8);
        if (_tracker is { } tracker)
        {
            if (optional && hasProperties)
            {
                bool inline = properties.Kind == ContentOperandKind.Dictionary;
                tracker.BeginMarkedContent(OptionalContentTag, inline ? properties.ToCosObject() : named, inline);
            }
            else
            {
                tracker.BeginMarkedContent(OtherTag, null);
            }

            _context.IsHidden = !tracker.IsVisible;
        }

        if (_markedCount == _marked.Length)
        {
            Array.Resize(ref _marked, _marked.Length * 2);
        }

        if (_tagByteCount + tag.Length > _tagBytes.Length)
        {
            Array.Resize(ref _tagBytes, Math.Max(_tagBytes.Length * 2, _tagByteCount + tag.Length));
        }

        tag.CopyTo(_tagBytes.AsSpan(_tagByteCount));
        _marked[_markedCount++] = new MarkedSequence(_tagByteCount, tag.Length, named, resource, mcid, optional, _context.IsHidden);
        _tagByteCount += tag.Length;
        if ((_events & ContentEvents.MarkedContent) != 0)
        {
            var mc = new MarkedContentEvent
            {
                Tag = tag,
                InlineProperties = hasProperties && properties.Kind == ContentOperandKind.Dictionary ? properties : default,
                Properties = named,
                PropertiesObject = resource,
                Mcid = mcid,
                Depth = _markedCount,
                IsHidden = _context.IsHidden,
                IsOptionalContent = optional,
                ContentStream = _context.ContentStream,
                StructParents = _context.StructParents,
            };
            _processor.BeginMarkedContent(mc, _context);
        }
    }

    private void EndSequence()
    {
        MarkedSequence sequence = _marked[_markedCount - 1];
        if ((_events & ContentEvents.MarkedContent) != 0)
        {
            var mc = new MarkedContentEvent
            {
                Tag = _tagBytes.AsSpan(sequence.TagStart, sequence.TagLength),
                Properties = sequence.Properties,
                PropertiesObject = sequence.PropertiesObject,
                Mcid = sequence.Mcid,
                Depth = _markedCount,
                IsHidden = sequence.IsHidden,
                IsOptionalContent = sequence.IsOptionalContent,
                ContentStream = _context.ContentStream,
                StructParents = _context.StructParents,
            };
            _processor.EndMarkedContent(mc, _context);
        }

        _marked[--_markedCount] = default;
        _tagByteCount = sequence.TagStart;
        if (_tracker is { } tracker)
        {
            tracker.EndMarkedContent();
            _context.IsHidden = !tracker.IsVisible;
        }
    }

    private void MarkPoint(ReadOnlySpan<byte> tag, ContentOperand properties, bool hasProperties, int offset)
    {
        CosObject? resource = null;
        CosDictionary? named = hasProperties ? ResolveProperties(properties, offset, out resource) : null;
        if ((_events & ContentEvents.MarkedContent) != 0)
        {
            var mc = new MarkedContentEvent
            {
                Tag = tag,
                InlineProperties = hasProperties && properties.Kind == ContentOperandKind.Dictionary ? properties : default,
                Properties = named,
                PropertiesObject = hasProperties ? resource : null,
                Mcid = hasProperties ? Mcid(properties, named) : null,
                Depth = _markedCount,
                IsHidden = _context.IsHidden,
                IsOptionalContent = false,
                ContentStream = _context.ContentStream,
                StructParents = _context.StructParents,
            };
            _processor.MarkedContentPoint(mc, _context);
        }
    }

    /// <summary>A named property list: the entry of the current resources' <c>Properties</c> subdictionary (§14.6.2).</summary>
    private CosDictionary? ResolveProperties(ContentOperand properties, int offset, out CosObject? resolved)
    {
        resolved = null;
        if (properties.Kind != ContentOperandKind.Name)
        {
            return null;
        }

        PdfDocument document = _context.Document;
        CosObject? value = document.ColorSpaces.FindResource(_context.Resources, PropertiesName, properties.Bytes, out _);
        if (value is null)
        {
            Report(ContentIssue.PropertiesMissing, offset, "A marked-content property list name is not in the resources' Properties dictionary; the sequence has no properties.");
            return null;
        }

        // Usually a dictionary; for /AF also a bare array of file specifications (§14.13.5, Example 2).
        resolved = document.Resolve(value);
        return resolved as CosDictionary;
    }

    /// <summary>The <c>MCID</c> of an inline or named property list (§14.7.5.2), or <see langword="null"/>.</summary>
    private int? Mcid(ContentOperand properties, CosDictionary? named)
    {
        if (named is not null)
        {
            return named.TryGetValue(Structure.StructureNames.MCID, out CosObject? value) && _context.Document.Resolve(value) is CosInteger { Value: >= 0 and <= int.MaxValue } integer
                ? (int)integer.Value
                : null;
        }

        if (properties.Kind != ContentOperandKind.Dictionary)
        {
            return null;
        }

        ContentOperands items = properties.Items;
        for (int index = 0; index + 1 < items.Count; index += 2)
        {
            if (items[index].IsName("MCID"u8) && items[index + 1] is { Kind: ContentOperandKind.Integer, Number: >= 0 and <= int.MaxValue } mcid)
            {
                return (int)mcid.Number;
            }
        }

        return null;
    }

    /// <summary>One open marked-content sequence: its tag (in the tag buffer), resolved properties and MCID.</summary>
    private readonly record struct MarkedSequence(int TagStart, int TagLength, CosDictionary? Properties, CosObject? PropertiesObject, int? Mcid, bool IsOptionalContent, bool IsHidden);
}
