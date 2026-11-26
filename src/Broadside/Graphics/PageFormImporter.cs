using System.Buffers;
using System.Runtime.CompilerServices;
using Broadside.Annotations;
using Broadside.Content;
using Broadside.Filters;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside.Graphics;

/// <summary>
/// Makes a page into a form XObject of a target document (issue #628): the page's content joined into one stream, its resources
/// shared (same document) or copied (another document), its box, rotation and user unit in the form's bounding box and matrix.
/// </summary>
/// <remarks>ISO 32000-2 §8.10.1, Table 93; §8.10.3; §7.7.3.3 and §7.7.3.4; §14.11.2; §8.11.2.</remarks>
internal static class PageFormImporter
{
    private static readonly CosName XObjectType = new("XObject");
    private static readonly CosName FormSubtype = new("Form");
    private static readonly CosName Mcid = new("MCID");
    private static readonly CosName Transparency = new("Transparency");
    private static readonly CosName S = new("S");

    /// <summary>Returns the form for <paramref name="page"/> in <paramref name="target"/>, the one made before for the same page and options when there is one.</summary>
    public static PdfFormXObject Import(PdfDocument target, PdfPage page, PdfPageFormOptions options)
    {
        PdfDocument source = page.Document;
        Session session = target.Drawing.PageForms(source);
        var key = new FormKey(page.Dictionary, options.Key);
        if (session.Forms.TryGetValue(key, out PdfFormXObject? known)
            && known.Reference is { } knownReference
            && ReferenceEquals(target.Resolve(knownReference), known.Stream))
        {
            return known;
        }

        PdfFormXObject form = Create(target, page, options, session.Copier);
        session.Forms[key] = form;
        return form;
    }

    private static PdfFormXObject Create(PdfDocument target, PdfPage page, PdfPageFormOptions options, PdfObjectCopier? copier)
    {
        PdfDocument source = page.Document;

        // Read everything from the source first: an exception leaves the target unchanged.
        byte[] content = ReadContent(source, page, options.KeepStructure);
        PdfRectangle box = Box(page, options.Boundary);
        int rotation = options.ApplyRotation ? page.Rotation : 0;
        Matrix matrix = FormMatrix(box, rotation, page.UserUnit);

        CosObject? resourcesEntry = page.Dictionary.TryGetValue(KnownNames.Resources, out CosObject? own) && source.Resolve(own) is CosDictionary
            ? own
            : page.FindInheritedEntry(KnownNames.Resources);
        page.Dictionary.TryGetValue(AnnotationNames.Group, out CosObject? groupEntry);

        Func<CosObject, CosObject>? map = copier is null ? null : copier.Copy;
        CosObject resources = resourcesEntry is null ? new CosDictionary() : map is null ? resourcesEntry : map(resourcesEntry);
        if (target.Resolve(resources) is not CosDictionary)
        {
            resources = new CosDictionary();
        }

        if (options.IncludeAnnotations && page.Annotations.Count > 0)
        {
            // The appearances are named in a resource dictionary of the form's own, so the page's (shared or copied) is not changed.
            var drawn = new CosDictionary();
            foreach (KeyValuePair<CosName, CosObject> entry in (CosDictionary)target.Resolve(resources))
            {
                drawn[entry.Key] = entry.Value;
            }

            var writer = new ContentWriter();
            AnnotationFlattening.DrawPrinted(target, page, writer, drawn, map);
            if (writer.Length > 0)
            {
                content = content.Length == 0 ? writer.ToArray() : [.. content, (byte)'\n', .. writer.Written];
                resources = drawn;
            }
        }

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = XObjectType,
            [AnnotationNames.Subtype] = FormSubtype,
            [AnnotationNames.BBox] = DrawingCache.Rectangle(box),
        };
        if (matrix != Matrix.Identity)
        {
            dictionary[AnnotationNames.Matrix] = DrawingCache.Numbers([matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F]);
        }

        dictionary[KnownNames.Resources] = resources;
        if (groupEntry is not null && (map is null ? groupEntry : map(groupEntry)) is { } group and not CosNull)
        {
            dictionary[AnnotationNames.Group] = group;
            if (target.Resolve(group) is CosDictionary groupDictionary && Transparency.Equals(target.Resolve(groupDictionary.GetValueOrDefault(S))))
            {
                target.RequireFeature(VersionedFeatures.TransparencyGroups);
            }
        }

        if (options.KeepStructure && source.Resolve(page.Dictionary.GetValueOrDefault(AnnotationNames.StructParents)) is CosInteger structParents)
        {
            dictionary[AnnotationNames.StructParents] = structParents;
        }

        var stream = new CosStream(dictionary, ReadOnlyMemory<byte>.Empty);
        if (content.Length > 0)
        {
            dictionary[FilterNames.Filter] = FilterNames.FlateDecode;
            target.EncodeStream(stream, content);
        }

        if (copier is not null)
        {
            RegisterCopiedGroups(source, target, copier);
        }

        return new PdfFormXObject(target, stream, target.AddObject(stream));
    }

    /// <summary>The chosen boundary, which the page's getters already intersect with the media box (§14.11.2.1).</summary>
    private static PdfRectangle Box(PdfPage page, PdfPageBoundary boundary) => boundary switch
    {
        PdfPageBoundary.MediaBox => page.MediaBox,
        PdfPageBoundary.BleedBox => page.BleedBox,
        PdfPageBoundary.TrimBox => page.TrimBox,
        PdfPageBoundary.ArtBox => page.ArtBox,
        _ => page.CropBox,
    };

    /// <summary>
    /// The form matrix: the page's <c>Rotate</c> (clockwise as displayed, Table 31), then the translation that puts the rotated box's
    /// lower-left corner at the origin, then the scale of <c>UserUnit</c>.
    /// </summary>
    internal static Matrix FormMatrix(PdfRectangle box, int rotation, double userUnit)
    {
        (double a, double b, double c, double d) = rotation switch
        {
            90 => (0d, -1d, 1d, 0d),
            180 => (-1d, 0d, 0d, -1d),
            270 => (0d, 1d, -1d, 0d),
            _ => (1d, 0d, 0d, 1d),
        };
        var rotate = new Matrix(a, b, c, d, 0, 0);
        PathPoint p0 = rotate.Transform(box.Left, box.Bottom);
        PathPoint p1 = rotate.Transform(box.Right, box.Top);
        double left = Math.Min(p0.X, p1.X);
        double bottom = Math.Min(p0.Y, p1.Y);
        return new Matrix(a * userUnit, b * userUnit, c * userUnit, d * userUnit, -left * userUnit, -bottom * userUnit);
    }

    /// <summary>
    /// The page's content, its streams decoded and joined with a line feed (Table 31), balanced: a <c>q</c> before it for each
    /// <c>Q</c> it restores beyond what it saved, and an unclosed text object, marked-content sequence or saved state closed after it.
    /// Without <paramref name="keepStructure"/>, <c>MCID</c> entries leave the marked-content property lists written in the content.
    /// </summary>
    private static byte[] ReadContent(PdfDocument source, PdfPage page, bool keepStructure)
    {
        List<CosStream> streams = [];
        switch (source.Resolve(page.Dictionary.GetValueOrDefault(KnownNames.Contents)))
        {
            case CosStream stream:
                streams.Add(stream);
                break;
            case CosArray array:
                foreach (CosObject item in array)
                {
                    if (source.Resolve(item) is CosStream part)
                    {
                        streams.Add(part);
                    }
                }

                break;
        }

        if (streams.Count == 0)
        {
            return [];
        }

        var joined = new ArrayBufferWriter<byte>();
        foreach (CosStream stream in streams)
        {
            if (source.Streams.CryptFilter.IsLocked(stream.Dictionary))
            {
                throw new NotSupportedException(
                    "A content stream is encrypted with a crypt filter the source document was opened without the key to; the page cannot be made into a form.");
            }

            if (!source.Streams.CanDecode(stream.Dictionary))
            {
                throw new NotSupportedException("A content stream of the page uses a filter this engine does not decode; the page cannot be made into a form.");
            }

            if (joined.WrittenCount > 0)
            {
                joined.Write("\n"u8);
            }

            joined.Write(source.DecodeStream(stream).Span);
        }

        return Balance(joined.WrittenSpan, keepStructure);
    }

    private static byte[] Balance(ReadOnlySpan<byte> content, bool keepStructure)
    {
        var body = new ArrayBufferWriter<byte>(content.Length + 16);
        var arena = new OperandArena();
        var reader = new ContentReader(content, arena);
        int copied = 0;
        int depth = 0;
        int lowest = 0;
        int marked = 0;
        bool inText = false;
        bool any = false;
        while (reader.Next(out ReadOperator op))
        {
            any = true;
            switch (op.Code)
            {
                case ContentOperatorCode.SaveState:
                    depth++;
                    break;
                case ContentOperatorCode.RestoreState:
                    lowest = Math.Min(lowest, --depth);
                    break;
                case ContentOperatorCode.BeginText:
                    inText = true;
                    break;
                case ContentOperatorCode.EndText:
                    inText = false;
                    break;
                case ContentOperatorCode.BeginMarkedContent:
                    marked++;
                    break;
                case ContentOperatorCode.BeginMarkedContentWithProperties:
                    marked++;
                    if (!keepStructure && WithoutMcid(arena.Operands) is { } rewritten)
                    {
                        body.Write(content[copied..op.Start]);
                        body.Write(rewritten);
                        copied = op.End;
                    }

                    break;
                case ContentOperatorCode.EndMarkedContent:
                    marked = Math.Max(0, marked - 1);
                    break;
            }

            arena.Clear();
        }

        arena.Trim();
        if (!any)
        {
            return content.ToArray();
        }

        body.Write(content[copied..]);
        var result = new ArrayBufferWriter<byte>(body.WrittenCount + 32);
        for (int index = 0; index < -lowest; index++)
        {
            result.Write("q\n"u8);
        }

        result.Write(body.WrittenSpan);
        result.Write("\n"u8);
        if (inText)
        {
            result.Write("ET\n"u8);
        }

        for (int index = 0; index < marked; index++)
        {
            result.Write("EMC\n"u8);
        }

        for (int index = 0; index < depth - lowest; index++)
        {
            result.Write("Q\n"u8);
        }

        return result.WrittenSpan.ToArray();
    }

    /// <summary>The <c>BDC</c> operator rewritten without the <c>MCID</c> of an inline property list, or null when it has none.</summary>
    private static byte[]? WithoutMcid(ContentOperands operands)
    {
        if (operands.Count != 2 || operands[0].Kind != ContentOperandKind.Name || operands[1].Kind != ContentOperandKind.Dictionary
            || operands[1].ToCosObject() is not CosDictionary properties || !properties.ContainsKey(Mcid))
        {
            return null;
        }

        properties.Remove(Mcid);
        var output = new ArrayBufferWriter<byte>();
        CosWriter.Write(operands[0].ToCosObject(), output);
        output.Write(" "u8);
        CosWriter.Write(properties, output);
        output.Write(" BDC"u8);
        return output.WrittenSpan.ToArray();
    }

    /// <summary>Lists in the target's <c>OCProperties</c> the optional content groups the session copied (§8.11.4.2), ON or OFF as the source's default configuration has them.</summary>
    private static void RegisterCopiedGroups(PdfDocument source, PdfDocument target, PdfObjectCopier copier)
    {
        CosArray? off = null;
        bool offRead = false;
        foreach ((CosReference original, CosReference copy) in copier.CopiedObjects)
        {
            if (target.Resolve(copy) is not CosDictionary group || !FileAndLayerNames.OCG.Equals(target.Resolve(group.GetValueOrDefault(KnownNames.Type))))
            {
                continue;
            }

            if (!offRead)
            {
                offRead = true;
                off = source.Resolve(source.Catalog.GetValueOrDefault(FileAndLayerNames.OCProperties)) is CosDictionary properties
                    && source.Resolve(properties.GetValueOrDefault(FileAndLayerNames.D)) is CosDictionary configuration
                    && source.Resolve(configuration.GetValueOrDefault(FileAndLayerNames.OFF)) is CosArray array
                        ? array
                        : null;
            }

            OptionalContentWriter.RegisterGroup(target, copy, visible: off is null || !off.Contains(original));
        }
    }

    /// <summary>The page and option values a form was made for.</summary>
    internal readonly record struct FormKey(CosDictionary Page, int Options) : IEquatable<FormKey>
    {
        public bool Equals(FormKey other) => ReferenceEquals(Page, other.Page) && Options == other.Options;

        public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Page), Options);
    }

    /// <summary>
    /// What a target keeps per source document (issue #628): one copy session, so objects that several imported pages share are
    /// copied once, and the forms made, for memoization.
    /// </summary>
    internal sealed class Session(PdfObjectCopier? copier)
    {
        /// <summary>Gets the copy session; <see langword="null"/> when the source is the target.</summary>
        public PdfObjectCopier? Copier { get; } = copier;

        /// <summary>Gets the forms made, by page and options.</summary>
        public Dictionary<FormKey, PdfFormXObject> Forms { get; } = [];
    }
}
