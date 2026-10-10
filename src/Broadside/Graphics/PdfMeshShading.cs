using System.Globalization;
using Broadside.Graphics.Shadings;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Graphics;

/// <summary>
/// A shading whose geometry is a mesh given as stream data (Types 4 to 7): triangles (<see cref="PdfTriangleMeshShading"/>) or
/// patches (<see cref="PdfPatchMeshShading"/>) with colours at their vertices or corners.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8, Tables 81 to 85, and §8.9.5.2 (the Decode formula). The data is decoded on first access to
/// the geometry, once however many threads ask; coordinates are kept as doubles (24- and 32-bit coordinates exceed a float's
/// precision). A colour is <see cref="ColorStride"/> floats: the parametric value t when the shading has a function (pass it to
/// <see cref="PdfShading.EvaluateFunction"/> after interpolating), else the components in
/// <see cref="PdfShading.InterpolationColorSpace"/> (indices of an Indexed space are converted to its base when decoded).
/// </para>
/// <para>
/// Repairs: field widths outside the sets the tables allow are read as given (1 to 32 bits) with a diagnostic; truncated data keeps
/// the complete triangles or patches before the cut with one <c>MeshDataTruncated</c> diagnostic; see the subclasses for edge
/// flags. Meshes larger than the limit are cut there with a diagnostic.
/// </para>
/// </remarks>
public abstract class PdfMeshShading : PdfShading
{
    private static readonly int[] CoordinateWidths = [1, 2, 4, 8, 12, 16, 24, 32];
    private static readonly int[] ComponentWidths = [1, 2, 4, 8, 12, 16];
    private static readonly int[] FlagWidths = [2, 4, 8];

    private readonly Lazy<MeshData> _mesh;
    private readonly MeshLayout? _layout;
    private readonly double[] _decode;

    private protected PdfMeshShading(ShadingReader reader, PdfShadingType type)
        : base(reader, type)
    {
        int shadingType = (int)type;
        BitsPerCoordinate = ReadBits(reader, ShadingNames.BitsPerCoordinate, CoordinateWidths);
        BitsPerComponent = ReadBits(reader, ShadingNames.BitsPerComponent, ComponentWidths);
        BitsPerFlag = shadingType == 5 ? 0 : ReadBits(reader, ShadingNames.BitsPerFlag, FlagWidths);
        VerticesPerRowValue = shadingType == 5 ? ReadVerticesPerRow(reader) : 0;
        int values = HasFunction ? 1 : ColorSpace.ComponentCount;
        _decode = reader.Numbers(ShadingNames.Decode) ?? [];
        if (_decode.Length < 4 + (2 * values))
        {
            reader.Invalid(
                DiagnosticCodes.ShadingDecodeInvalid,
                string.Create(CultureInfo.InvariantCulture, $"The mesh's Decode entry is missing or has fewer than {4 + (2 * values)} numbers."));
        }

        if (reader.CosObject is not CosStream stream)
        {
            reader.Invalid(DiagnosticCodes.ShadingNotStream, "A mesh shading (Types 4 to 7) shall be a stream; it is a dictionary.");
            stream = null!;
        }

        IsValid = reader.IsValid;
        if (IsValid)
        {
            _layout = new MeshLayout
            {
                ShadingType = shadingType,
                BitsPerCoordinate = BitsPerCoordinate,
                BitsPerComponent = BitsPerComponent,
                BitsPerFlag = BitsPerFlag,
                Decode = _decode,
                ValueCount = values,
                VerticesPerRow = VerticesPerRowValue,
                Palette = HasFunction ? null : Palette(ColorSpace),
                MaxVertices = reader.Document.MaxMeshVertices,
            };
        }

        _mesh = new Lazy<MeshData>(() => DecodeMesh(stream), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the number of bits per coordinate (1, 2, 4, 8, 12, 16, 24 or 32); 0 when the entry is unusable.</summary>
    /// <remarks>ISO 32000-2 Tables 81 to 83 (<c>BitsPerCoordinate</c>).</remarks>
    public int BitsPerCoordinate { get; }

    /// <summary>Gets the number of bits per colour component or t (1, 2, 4, 8, 12 or 16); 0 when the entry is unusable.</summary>
    /// <remarks>ISO 32000-2 Tables 81 to 83 (<c>BitsPerComponent</c>).</remarks>
    public int BitsPerComponent { get; }

    /// <summary>Gets the number of bits per edge flag (2, 4 or 8); 0 for a lattice (Type 5), which has no flags.</summary>
    /// <remarks>ISO 32000-2 Tables 81 and 83 (<c>BitsPerFlag</c>).</remarks>
    public int BitsPerFlag { get; }

    /// <summary>Gets the Decode array: x<sub>min</sub> x<sub>max</sub> y<sub>min</sub> y<sub>max</sub>, then a pair per colour value.</summary>
    /// <remarks>ISO 32000-2 Tables 81 to 83 (<c>Decode</c>) and §8.9.5.2.</remarks>
    public IReadOnlyList<double> Decode => Array.AsReadOnly(_decode);

    /// <summary>Gets the number of floats per stored colour: 1 (t) with a function, else the components of the interpolation space.</summary>
    public int ColorStride => HasFunction ? 1 : ColorComponentCount;

    /// <summary>Gets the decoded mesh.</summary>
    internal MeshData Mesh => _mesh.Value;

    /// <summary>Gets the lattice width read from the dictionary (Type 5 only).</summary>
    private protected int VerticesPerRowValue { get; }

    private static int ReadBits(ShadingReader reader, CosName key, int[] allowed)
    {
        int? bits = reader.Integer(key);
        if (bits is not (>= 1 and <= 32))
        {
            reader.Invalid(DiagnosticCodes.ShadingBitsInvalid, $"The mesh's {key.Value} entry is missing or not an integer from 1 to 32.");
            return 0;
        }

        if (Array.IndexOf(allowed, bits.Value) < 0)
        {
            reader.Report(DiagnosticCodes.ShadingBitsInvalid, $"The mesh's {key.Value} entry is not one of the values its table allows; it is read as given.");
        }

        return bits.Value;
    }

    private static int ReadVerticesPerRow(ShadingReader reader)
    {
        int? perRow = reader.Integer(ShadingNames.VerticesPerRow);
        if (perRow is not >= 2)
        {
            reader.Invalid(DiagnosticCodes.ShadingEntryInvalid, "A lattice's VerticesPerRow is missing or less than 2.");
            return 0;
        }

        return perRow.Value;
    }

    private static MeshPalette? Palette(PdfColorSpace space)
    {
        if (space is not PdfIndexedColorSpace indexed)
        {
            return null;
        }

        PdfColorSpace baseSpace = indexed.Base;
        var ranges = new ComponentRange[baseSpace.ComponentCount];
        for (int k = 0; k < ranges.Length; k++)
        {
            ranges[k] = baseSpace.GetComponentRange(k);
        }

        return new MeshPalette(indexed.HighValue, indexed.GetLookup().Span, ranges);
    }

    private MeshData DecodeMesh(CosStream stream)
    {
        if (_layout is null)
        {
            return MeshData.Empty;
        }

        MeshData mesh = MeshDecoder.Decode(_layout, Document.DecodeStream(stream).Span);
        ReportIssues(mesh.Issues);
        return mesh;
    }

    private void ReportIssues(MeshIssues issues)
    {
        if ((issues & MeshIssues.Truncated) != 0)
        {
            Diagnostics.Report(DiagnosticCodes.MeshDataTruncated, "The mesh data ends inside a vertex, triangle or patch; the complete ones before it are kept.");
        }

        if ((issues & MeshIssues.FlagInvalid) != 0)
        {
            Diagnostics.Report(DiagnosticCodes.MeshEdgeFlagInvalid, "An edge flag is not allowed or continues a triangle or patch that does not exist; the data it covers is skipped.");
        }

        if ((issues & MeshIssues.LatticeIncomplete) != 0)
        {
            Diagnostics.Report(DiagnosticCodes.MeshLatticeIncomplete, "The lattice's last row is incomplete, or it has fewer than two rows; the incomplete row is dropped.");
        }

        if ((issues & MeshIssues.LimitExceeded) != 0)
        {
            Diagnostics.Report(DiagnosticCodes.MeshSizeExceeded, "The mesh has more vertices than the limit; the rest is dropped.");
        }

        if ((issues & MeshIssues.PatchesUnpadded) != 0)
        {
            // pdf.js and PDFBox write and read patches this way; the reading is recorded, nothing is lost.
            Diagnostics.Report(
                DiagnosticCodes.MeshPatchesUnpadded,
                "The patches are not padded to whole bytes (each set of data shall occupy a whole number of bytes, §8.7.4.5.5); they are read back to back.",
                Broadside.Diagnostics.DiagnosticSeverity.Information);
        }
    }
}
