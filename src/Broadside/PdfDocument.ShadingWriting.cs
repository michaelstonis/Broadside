using System.Globalization;
using Broadside.Graphics;
using Broadside.Graphics.Functions;
using Broadside.Graphics.Shadings;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside;

/// <summary>Writing shadings and patterns (issue #155): the shadings, gradient functions and patterns a <see cref="PdfCanvas"/> paints with.</summary>
public sealed partial class PdfDocument
{
    private static readonly CosName PatternTypeName = new("Pattern");

    /// <summary>Creates a function of one input over 0 to 1 that blends between gradient stops.</summary>
    /// <param name="stops">The stops: at least two, offsets from 0 to 1 in non-decreasing order, colours all in one colour space (not Pattern or Indexed) of this document.</param>
    /// <returns>
    /// The function, a new indirect dictionary: an exponential function (Type 2, exponent 1) when the stops make one blend, else a
    /// stitching function (Type 3) of one exponential function per blend, its <c>Bounds</c> the offsets where blends meet.
    /// </returns>
    /// <exception cref="ArgumentException">There are fewer than two stops, an offset is outside 0 to 1, not finite or smaller than the one before, or the colours are not in one usable space or have the wrong number of components.</exception>
    /// <remarks>
    /// ISO 32000-2 §7.10.3 (Table 40) and §7.10.4 (Table 41), PDF 1.3. Before the first stop the first colour applies, after the last
    /// the last; two stops at one offset make a hard edge there. Its outputs are the components of the stops' colour space, so it can
    /// colour an axial, radial or mesh shading in that space. Records PDF 1.3. A mutation.
    /// </remarks>
    public PdfFunction CreateGradientFunction(IReadOnlyList<PdfGradientStop> stops)
    {
        CosDictionary function = GradientFunction(stops, out _);
        return GetFunction(AddObject(function))!;
    }

    /// <summary>Creates an axial shading (Type 2) that blends between gradient stops along the line from <paramref name="start"/> to <paramref name="end"/>.</summary>
    /// <param name="start">The point where offset 0 lies, in the shading's target space.</param>
    /// <param name="end">The point where offset 1 lies; not <paramref name="start"/>.</param>
    /// <param name="stops">The stops, as for <see cref="CreateGradientFunction"/>; their colour space is the shading's.</param>
    /// <returns>The shading, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">A coordinate is not finite, the points are equal, the stops are not usable (see <see cref="CreateGradientFunction"/>), or an option does not fit.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.5.3, Table 79 (PDF 1.3). <c>Coords</c> is the axis, <c>Domain</c> [0 1], and the <c>Function</c> is written
    /// inline as <see cref="CreateGradientFunction"/> builds it. Paint it with <see cref="PdfCanvas.PaintShading"/> or through
    /// <see cref="CreateShadingPattern"/>. Records PDF 1.3. A mutation.
    /// </remarks>
    public PdfAxialShading CreateAxialShading(PathPoint start, PathPoint end, IReadOnlyList<PdfGradientStop> stops) =>
        CreateAxialShading(start, end, stops, null);

    /// <summary>Creates an axial shading (Type 2) that blends between gradient stops along the line from <paramref name="start"/> to <paramref name="end"/>.</summary>
    /// <param name="start">The point where offset 0 lies, in the shading's target space.</param>
    /// <param name="end">The point where offset 1 lies; not <paramref name="start"/>.</param>
    /// <param name="stops">The stops, as for <see cref="CreateGradientFunction"/>; their colour space is the shading's.</param>
    /// <param name="options">The optional entries; <see langword="null"/> for none.</param>
    /// <returns>The shading, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">A coordinate is not finite, the points are equal, the stops are not usable (see <see cref="CreateGradientFunction"/>), or an option does not fit.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.5.3, Table 79 (PDF 1.3). <c>Coords</c> is the axis, <c>Domain</c> [0 1], and the <c>Function</c> is written
    /// inline as <see cref="CreateGradientFunction"/> builds it. Paint it with <see cref="PdfCanvas.PaintShading"/> or through
    /// <see cref="CreateShadingPattern"/>. Records PDF 1.3. A mutation.
    /// </remarks>
    public PdfAxialShading CreateAxialShading(PathPoint start, PathPoint end, IReadOnlyList<PdfGradientStop> stops, PdfShadingOptions? options)
    {
        CosDictionary function = GradientFunction(stops, out PdfColorSpace space);
        return (PdfAxialShading)CreateAxial(space, start, end, function, [0, 1], options);
    }

    /// <summary>Creates an axial shading (Type 2) coloured by a function of the caller's along the line from <paramref name="start"/> to <paramref name="end"/>.</summary>
    /// <param name="colorSpace">The shading's colour space: a space of this document, not Pattern or Indexed.</param>
    /// <param name="start">The point where the function's domain starts (t<sub>0</sub>).</param>
    /// <param name="end">The point where it ends (t<sub>1</sub>); not <paramref name="start"/>.</param>
    /// <param name="function">A function of this document from one input to the colour space's components; its domain is the shading's <c>Domain</c>.</param>
    /// <returns>The shading, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">A coordinate is not finite, the points are equal, the space or the function cannot be used, or an option does not fit.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.3, Table 79 (PDF 1.3). Records PDF 1.3 and the function's version. A mutation.</remarks>
    public PdfAxialShading CreateAxialShading(PdfColorSpace colorSpace, PathPoint start, PathPoint end, PdfFunction function) =>
        CreateAxialShading(colorSpace, start, end, function, null);

    /// <summary>Creates an axial shading (Type 2) coloured by a function of the caller's along the line from <paramref name="start"/> to <paramref name="end"/>.</summary>
    /// <param name="colorSpace">The shading's colour space: a space of this document, not Pattern or Indexed.</param>
    /// <param name="start">The point where the function's domain starts (t<sub>0</sub>).</param>
    /// <param name="end">The point where it ends (t<sub>1</sub>); not <paramref name="start"/>.</param>
    /// <param name="function">A function of this document from one input to the colour space's components; its domain is the shading's <c>Domain</c>.</param>
    /// <param name="options">The optional entries; <see langword="null"/> for none.</param>
    /// <returns>The shading, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">A coordinate is not finite, the points are equal, the space or the function cannot be used, or an option does not fit.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.3, Table 79 (PDF 1.3). Records PDF 1.3 and the function's version. A mutation.</remarks>
    public PdfAxialShading CreateAxialShading(PdfColorSpace colorSpace, PathPoint start, PathPoint end, PdfFunction function, PdfShadingOptions? options)
    {
        (CosObject value, double[] domain) = ShadingFunction(colorSpace, function, 1);
        return (PdfAxialShading)CreateAxial(colorSpace, start, end, value, domain, options);
    }

    /// <summary>Creates a radial shading (Type 3) that blends between gradient stops from one circle to another.</summary>
    /// <param name="startCenter">The centre of the circle where offset 0 lies, in the shading's target space.</param>
    /// <param name="startRadius">Its radius: 0 or more.</param>
    /// <param name="endCenter">The centre of the circle where offset 1 lies.</param>
    /// <param name="endRadius">Its radius: 0 or more.</param>
    /// <param name="stops">The stops, as for <see cref="CreateGradientFunction"/>; their colour space is the shading's.</param>
    /// <returns>The shading, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">A value is not finite, a radius is negative, both circles are the same point, the stops are not usable, or an option does not fit.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (PDF 1.3). <c>Domain</c> is [0 1]. Records PDF 1.3. A mutation.</remarks>
    public PdfRadialShading CreateRadialShading(PathPoint startCenter, double startRadius, PathPoint endCenter, double endRadius, IReadOnlyList<PdfGradientStop> stops) =>
        CreateRadialShading(startCenter, startRadius, endCenter, endRadius, stops, null);

    /// <summary>Creates a radial shading (Type 3) that blends between gradient stops from one circle to another.</summary>
    /// <param name="startCenter">The centre of the circle where offset 0 lies, in the shading's target space.</param>
    /// <param name="startRadius">Its radius: 0 or more.</param>
    /// <param name="endCenter">The centre of the circle where offset 1 lies.</param>
    /// <param name="endRadius">Its radius: 0 or more.</param>
    /// <param name="stops">The stops, as for <see cref="CreateGradientFunction"/>; their colour space is the shading's.</param>
    /// <param name="options">The optional entries; <see langword="null"/> for none.</param>
    /// <returns>The shading, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">A value is not finite, a radius is negative, both circles are the same point, the stops are not usable, or an option does not fit.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (PDF 1.3). <c>Domain</c> is [0 1]. Records PDF 1.3. A mutation.</remarks>
    public PdfRadialShading CreateRadialShading(
        PathPoint startCenter, double startRadius, PathPoint endCenter, double endRadius, IReadOnlyList<PdfGradientStop> stops, PdfShadingOptions? options)
    {
        CosDictionary function = GradientFunction(stops, out PdfColorSpace space);
        return (PdfRadialShading)CreateRadial(space, startCenter, startRadius, endCenter, endRadius, function, [0, 1], options);
    }

    /// <summary>Creates a radial shading (Type 3) coloured by a function of the caller's from one circle to another.</summary>
    /// <param name="colorSpace">The shading's colour space: a space of this document, not Pattern or Indexed.</param>
    /// <param name="startCenter">The centre of the circle where the function's domain starts.</param>
    /// <param name="startRadius">Its radius: 0 or more.</param>
    /// <param name="endCenter">The centre of the circle where it ends.</param>
    /// <param name="endRadius">Its radius: 0 or more.</param>
    /// <param name="function">A function of this document from one input to the colour space's components; its domain is the shading's <c>Domain</c>.</param>
    /// <returns>The shading, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">A value is not finite, a radius is negative, both circles are the same point, the space or the function cannot be used, or an option does not fit.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (PDF 1.3). Records PDF 1.3 and the function's version. A mutation.</remarks>
    public PdfRadialShading CreateRadialShading(PdfColorSpace colorSpace, PathPoint startCenter, double startRadius, PathPoint endCenter, double endRadius, PdfFunction function) =>
        CreateRadialShading(colorSpace, startCenter, startRadius, endCenter, endRadius, function, null);

    /// <summary>Creates a radial shading (Type 3) coloured by a function of the caller's from one circle to another.</summary>
    /// <param name="colorSpace">The shading's colour space: a space of this document, not Pattern or Indexed.</param>
    /// <param name="startCenter">The centre of the circle where the function's domain starts.</param>
    /// <param name="startRadius">Its radius: 0 or more.</param>
    /// <param name="endCenter">The centre of the circle where it ends.</param>
    /// <param name="endRadius">Its radius: 0 or more.</param>
    /// <param name="function">A function of this document from one input to the colour space's components; its domain is the shading's <c>Domain</c>.</param>
    /// <param name="options">The optional entries; <see langword="null"/> for none.</param>
    /// <returns>The shading, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">A value is not finite, a radius is negative, both circles are the same point, the space or the function cannot be used, or an option does not fit.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.4, Table 80 (PDF 1.3). Records PDF 1.3 and the function's version. A mutation.</remarks>
    public PdfRadialShading CreateRadialShading(
        PdfColorSpace colorSpace, PathPoint startCenter, double startRadius, PathPoint endCenter, double endRadius, PdfFunction function, PdfShadingOptions? options)
    {
        (CosObject value, double[] domain) = ShadingFunction(colorSpace, function, 1);
        return (PdfRadialShading)CreateRadial(colorSpace, startCenter, startRadius, endCenter, endRadius, value, domain, options);
    }

    /// <summary>Creates a function-based shading (Type 1): the colour of every point (x, y) of <paramref name="domain"/> is the function's value there.</summary>
    /// <param name="colorSpace">The shading's colour space: a space of this document, not Pattern or Indexed.</param>
    /// <param name="function">A function of this document from two inputs (x, y) to the colour space's components, defined over <paramref name="domain"/>.</param>
    /// <param name="domain">The rectangle of (x, y) values the shading covers (<c>Domain</c> [x<sub>min</sub> x<sub>max</sub> y<sub>min</sub> y<sub>max</sub>]), in the shading's own space.</param>
    /// <param name="matrix">The transformation from the domain's space to the shading's target space (<c>Matrix</c>); the identity is not written.</param>
    /// <returns>The shading, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">A value is not finite, the domain has no area, the space or the function cannot be used, or an option does not fit.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.2, Table 78 (PDF 1.3). Records PDF 1.3 and the function's version. A mutation.</remarks>
    public PdfFunctionShading CreateFunctionShading(PdfColorSpace colorSpace, PdfFunction function, PdfRectangle domain, Matrix matrix) =>
        CreateFunctionShading(colorSpace, function, domain, matrix, null);

    /// <summary>Creates a function-based shading (Type 1): the colour of every point (x, y) of <paramref name="domain"/> is the function's value there.</summary>
    /// <param name="colorSpace">The shading's colour space: a space of this document, not Pattern or Indexed.</param>
    /// <param name="function">A function of this document from two inputs (x, y) to the colour space's components, defined over <paramref name="domain"/>.</param>
    /// <param name="domain">The rectangle of (x, y) values the shading covers (<c>Domain</c> [x<sub>min</sub> x<sub>max</sub> y<sub>min</sub> y<sub>max</sub>]), in the shading's own space.</param>
    /// <param name="matrix">The transformation from the domain's space to the shading's target space (<c>Matrix</c>); the identity is not written.</param>
    /// <param name="options">The optional entries (not <see cref="PdfShadingOptions.ExtendStart"/> or <see cref="PdfShadingOptions.ExtendEnd"/>); <see langword="null"/> for none.</param>
    /// <returns>The shading, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">A value is not finite, the domain has no area, the space or the function cannot be used, or an option does not fit.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.2, Table 78 (PDF 1.3). Records PDF 1.3 and the function's version. A mutation.</remarks>
    public PdfFunctionShading CreateFunctionShading(PdfColorSpace colorSpace, PdfFunction function, PdfRectangle domain, Matrix matrix, PdfShadingOptions? options)
    {
        ThrowIfNotFinite(domain.Left, domain.Bottom, domain.Right, domain.Top, matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F);
        if (!(domain.Right > domain.Left && domain.Top > domain.Bottom))
        {
            throw new ArgumentException("The domain shall have a positive width and height (ISO 32000-2 Table 78).", nameof(domain));
        }

        ThrowIfExtended(options);
        (CosObject value, _) = ShadingFunction(colorSpace, function, 2);
        CosDictionary dictionary = ShadingDictionary(PdfShadingType.FunctionBased, colorSpace, options);
        dictionary[ShadingNames.Domain] = DrawingCache.Numbers([domain.Left, domain.Right, domain.Bottom, domain.Top]);
        if (matrix != Matrix.Identity)
        {
            dictionary[ShadingNames.Matrix] = DrawingCache.Numbers([matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F]);
        }

        dictionary[ShadingNames.Function] = value;
        return (PdfFunctionShading)AddShading(dictionary);
    }

    /// <summary>Creates a free-form triangle mesh shading (Type 4) from a builder's triangles.</summary>
    /// <param name="mesh">The mesh: at least one triangle; its colour space and function belong to this document.</param>
    /// <returns>The shading, a new indirect stream, as read back.</returns>
    /// <exception cref="ArgumentException">The colour space or function belongs to another document, or an option does not fit.</exception>
    /// <exception cref="InvalidOperationException">The mesh has no triangle.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.5, Table 81 (PDF 1.3). See <see cref="PdfMeshBuilder"/> for the data layout. Records PDF 1.3. A mutation.</remarks>
    public PdfTriangleMeshShading CreateMeshShading(PdfFreeFormMeshBuilder mesh) =>
        CreateMeshShading(mesh, null);

    /// <summary>Creates a free-form triangle mesh shading (Type 4) from a builder's triangles.</summary>
    /// <param name="mesh">The mesh: at least one triangle; its colour space and function belong to this document.</param>
    /// <param name="options">The optional entries (not <see cref="PdfShadingOptions.ExtendStart"/> or <see cref="PdfShadingOptions.ExtendEnd"/>); <see langword="null"/> for none.</param>
    /// <returns>The shading, a new indirect stream, as read back.</returns>
    /// <exception cref="ArgumentException">The colour space or function belongs to another document, or an option does not fit.</exception>
    /// <exception cref="InvalidOperationException">The mesh has no triangle.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.5, Table 81 (PDF 1.3). See <see cref="PdfMeshBuilder"/> for the data layout. Records PDF 1.3. A mutation.</remarks>
    public PdfTriangleMeshShading CreateMeshShading(PdfFreeFormMeshBuilder mesh, PdfShadingOptions? options) =>
        (PdfTriangleMeshShading)CreateMesh(mesh, options);

    /// <summary>Creates a lattice-form triangle mesh shading (Type 5) from a builder's rows.</summary>
    /// <param name="mesh">The mesh: at least two rows; its colour space and function belong to this document.</param>
    /// <returns>The shading, a new indirect stream, as read back.</returns>
    /// <exception cref="ArgumentException">The colour space or function belongs to another document, or an option does not fit.</exception>
    /// <exception cref="InvalidOperationException">The mesh has fewer than two rows.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.6, Table 82 (PDF 1.3). Records PDF 1.3. A mutation.</remarks>
    public PdfTriangleMeshShading CreateMeshShading(PdfLatticeMeshBuilder mesh) =>
        CreateMeshShading(mesh, null);

    /// <summary>Creates a lattice-form triangle mesh shading (Type 5) from a builder's rows.</summary>
    /// <param name="mesh">The mesh: at least two rows; its colour space and function belong to this document.</param>
    /// <param name="options">The optional entries (not <see cref="PdfShadingOptions.ExtendStart"/> or <see cref="PdfShadingOptions.ExtendEnd"/>); <see langword="null"/> for none.</param>
    /// <returns>The shading, a new indirect stream, as read back.</returns>
    /// <exception cref="ArgumentException">The colour space or function belongs to another document, or an option does not fit.</exception>
    /// <exception cref="InvalidOperationException">The mesh has fewer than two rows.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.6, Table 82 (PDF 1.3). Records PDF 1.3. A mutation.</remarks>
    public PdfTriangleMeshShading CreateMeshShading(PdfLatticeMeshBuilder mesh, PdfShadingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        return (PdfTriangleMeshShading)CreateMesh(mesh, options, mesh.VerticesPerRow);
    }

    /// <summary>Creates a Coons (Type 6) or tensor-product (Type 7) patch mesh shading from a builder's patches.</summary>
    /// <param name="mesh">The mesh: at least one patch; its colour space and function belong to this document.</param>
    /// <returns>The shading, a new indirect stream, as read back.</returns>
    /// <exception cref="ArgumentException">The colour space or function belongs to another document, or an option does not fit.</exception>
    /// <exception cref="InvalidOperationException">The mesh has no patch.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.7 (Table 83) and §8.7.4.5.8 (Table 85) (PDF 1.3). Records PDF 1.3. A mutation.</remarks>
    public PdfPatchMeshShading CreateMeshShading(PdfPatchMeshBuilder mesh) =>
        CreateMeshShading(mesh, null);

    /// <summary>Creates a Coons (Type 6) or tensor-product (Type 7) patch mesh shading from a builder's patches.</summary>
    /// <param name="mesh">The mesh: at least one patch; its colour space and function belong to this document.</param>
    /// <param name="options">The optional entries (not <see cref="PdfShadingOptions.ExtendStart"/> or <see cref="PdfShadingOptions.ExtendEnd"/>); <see langword="null"/> for none.</param>
    /// <returns>The shading, a new indirect stream, as read back.</returns>
    /// <exception cref="ArgumentException">The colour space or function belongs to another document, or an option does not fit.</exception>
    /// <exception cref="InvalidOperationException">The mesh has no patch.</exception>
    /// <remarks>ISO 32000-2 §8.7.4.5.7 (Table 83) and §8.7.4.5.8 (Table 85) (PDF 1.3). Records PDF 1.3. A mutation.</remarks>
    public PdfPatchMeshShading CreateMeshShading(PdfPatchMeshBuilder mesh, PdfShadingOptions? options) =>
        (PdfPatchMeshShading)CreateMesh(mesh, options);

    /// <summary>Creates a shading pattern (PatternType 2): a shading used as a colour, filling or stroking with it.</summary>
    /// <param name="shading">A shading of this document with an indirect reference (every shading the factories above create has one).</param>
    /// <param name="matrix">The pattern matrix, from pattern space to the default space of the content stream that uses the pattern; <see langword="null"/> for the identity.</param>
    /// <returns>The pattern, a new indirect dictionary, as read back.</returns>
    /// <exception cref="ArgumentException">The shading belongs to another document, has no indirect reference or is not valid, or a matrix component is not finite.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.7.4.2, Table 76 (PDF 1.3), and §8.7.2. The shading's coordinates are in pattern space. Select it with
    /// <see cref="PdfCanvas.SetFillPattern(PdfPattern)"/> or <see cref="PdfCanvas.SetStrokePattern(PdfPattern)"/>. Records PDF 1.3. A mutation.
    /// </remarks>
    public PdfShadingPattern CreateShadingPattern(PdfShading shading, Matrix? matrix = null)
    {
        ArgumentNullException.ThrowIfNull(shading);
        if (!ReferenceEquals(shading.Document, this))
        {
            throw new ArgumentException("The shading belongs to another document.", nameof(shading));
        }

        if (shading.Reference is not { } reference || !shading.IsValid)
        {
            throw new ArgumentException("A shading pattern's shading shall be a valid shading with an indirect reference.", nameof(shading));
        }

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = PatternTypeName,
            [ShadingNames.PatternType] = new CosInteger(2),
            [ShadingNames.Shading] = reference,
        };
        AddPatternMatrix(dictionary, matrix);
        RequireFeature(VersionedFeatures.Shadings);
        return (PdfShadingPattern)GetPattern(AddObject(dictionary))!;
    }

    /// <summary>Creates a tiling pattern (PatternType 1) with an empty cell; draw the cell with <see cref="PdfTilingPattern.CreateCanvas"/>.</summary>
    /// <param name="boundingBox">The cell's bounding box in pattern space (<c>BBox</c>), which clips the cell.</param>
    /// <param name="xStep">The horizontal spacing of cells in pattern space (<c>XStep</c>): not zero.</param>
    /// <param name="yStep">The vertical spacing (<c>YStep</c>): not zero.</param>
    /// <param name="paintType">Whether the cell sets its own colours (coloured) or is a stencil painted in the colour given when the pattern is selected (uncoloured).</param>
    /// <param name="matrix">The pattern matrix, from pattern space to the default space of the content stream that uses the pattern; <see langword="null"/> for the identity.</param>
    /// <param name="tilingType">How the spacing may be adjusted to the device.</param>
    /// <returns>The pattern, a new indirect stream with an empty <c>Resources</c> dictionary and no content, as read back.</returns>
    /// <exception cref="ArgumentException">A value is not finite, or a step is zero.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="paintType"/> or <paramref name="tilingType"/> is not a defined value.</exception>
    /// <remarks>
    /// ISO 32000-2 §8.7.3.1, Table 74 (PDF 1.2), and §8.7.2. Select it with <see cref="PdfCanvas.SetFillPattern(PdfPattern)"/> (coloured)
    /// or <see cref="PdfCanvas.SetFillPattern(PdfTilingPattern, PdfColor)"/> (uncoloured). Records PDF 1.2. A mutation.
    /// </remarks>
    public PdfTilingPattern CreateTilingPattern(
        PdfRectangle boundingBox, double xStep, double yStep, PdfTilingPaintType paintType, Matrix? matrix = null, PdfTilingType tilingType = PdfTilingType.ConstantSpacing)
    {
        ThrowIfNotFinite(boundingBox.Left, boundingBox.Bottom, boundingBox.Right, boundingBox.Top, xStep, yStep);
        if (xStep == 0 || yStep == 0)
        {
            throw new ArgumentException("XStep and YStep shall not be zero (ISO 32000-2 Table 74).", xStep == 0 ? nameof(xStep) : nameof(yStep));
        }

        if (!Enum.IsDefined(paintType))
        {
            throw new ArgumentOutOfRangeException(nameof(paintType), paintType, "Not a defined paint type.");
        }

        if (!Enum.IsDefined(tilingType))
        {
            throw new ArgumentOutOfRangeException(nameof(tilingType), tilingType, "Not a defined tiling type.");
        }

        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = PatternTypeName,
            [ShadingNames.PatternType] = new CosInteger(1),
            [ShadingNames.PaintType] = new CosInteger((int)paintType),
            [ShadingNames.TilingType] = new CosInteger((int)tilingType),
            [ShadingNames.BBox] = DrawingCache.Rectangle(boundingBox),
            [ShadingNames.XStep] = DrawingCache.Number(xStep),
            [ShadingNames.YStep] = DrawingCache.Number(yStep),
            [ShadingNames.Resources] = new CosDictionary(),
        };
        AddPatternMatrix(dictionary, matrix);
        RequireFeature(VersionedFeatures.PatternColorSpace);
        return (PdfTilingPattern)GetPattern(AddObject(new CosStream(dictionary, ReadOnlyMemory<byte>.Empty)))!;
    }

    private static void ThrowIfExtended(PdfShadingOptions? options)
    {
        if (options is { ExtendStart: true } or { ExtendEnd: true })
        {
            throw new ArgumentException("Extend applies to axial and radial shadings only (ISO 32000-2 Tables 79 and 80).", nameof(options));
        }
    }

    private static void AddPatternMatrix(CosDictionary dictionary, Matrix? matrix)
    {
        if (matrix is not { } m)
        {
            return;
        }

        ThrowIfNotFinite(m.A, m.B, m.C, m.D, m.E, m.F);
        if (m != Matrix.Identity)
        {
            dictionary[ShadingNames.Matrix] = DrawingCache.Numbers([m.A, m.B, m.C, m.D, m.E, m.F]);
        }
    }

    /// <summary>A float as the double whose shortest decimal is the float's, so 0.1f is written 0.1.</summary>
    private static double Exact(float value) => double.Parse(value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private static CosArray Components(in PdfColor color)
    {
        var array = new CosArray();
        foreach (float component in color.Components)
        {
            array.Add(DrawingCache.Number(Exact(component)));
        }

        return array;
    }

    private PdfShading CreateAxial(PdfColorSpace space, PathPoint start, PathPoint end, CosObject function, double[] domain, PdfShadingOptions? options)
    {
        ThrowIfNotFinite(start.X, start.Y, end.X, end.Y);
        if (start == end)
        {
            throw new ArgumentException("The axis's start and end shall be different points (ISO 32000-2 §8.7.4.5.3).", nameof(end));
        }

        CosDictionary dictionary = ShadingDictionary(PdfShadingType.Axial, space, options);
        dictionary[ShadingNames.Coords] = DrawingCache.Numbers([start.X, start.Y, end.X, end.Y]);
        return AddGradientShading(dictionary, function, domain, options);
    }

    private PdfShading CreateRadial(
        PdfColorSpace space, PathPoint startCenter, double startRadius, PathPoint endCenter, double endRadius, CosObject function, double[] domain, PdfShadingOptions? options)
    {
        ThrowIfNotFinite(startCenter.X, startCenter.Y, startRadius, endCenter.X, endCenter.Y, endRadius);
        if (startRadius < 0 || endRadius < 0)
        {
            throw new ArgumentException("A radius shall not be negative (ISO 32000-2 Table 80).", startRadius < 0 ? nameof(startRadius) : nameof(endRadius));
        }

        if (startCenter == endCenter && startRadius == endRadius)
        {
            throw new ArgumentException("The two circles shall differ (ISO 32000-2 §8.7.4.5.4).", nameof(endRadius));
        }

        CosDictionary dictionary = ShadingDictionary(PdfShadingType.Radial, space, options);
        dictionary[ShadingNames.Coords] = DrawingCache.Numbers([startCenter.X, startCenter.Y, startRadius, endCenter.X, endCenter.Y, endRadius]);
        return AddGradientShading(dictionary, function, domain, options);
    }

    private PdfShading AddGradientShading(CosDictionary dictionary, CosObject function, double[] domain, PdfShadingOptions? options)
    {
        if (domain is not [0, 1])
        {
            dictionary[ShadingNames.Domain] = DrawingCache.Numbers(domain);
        }

        dictionary[ShadingNames.Function] = function;
        if (options is { ExtendStart: true } or { ExtendEnd: true })
        {
            dictionary[ShadingNames.Extend] = new CosArray { options.ExtendStart ? CosBoolean.True : CosBoolean.False, options.ExtendEnd ? CosBoolean.True : CosBoolean.False };
        }

        return AddShading(dictionary);
    }

    private PdfMeshShading CreateMesh(PdfMeshBuilder mesh, PdfShadingOptions? options, int verticesPerRow = 0)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ThrowIfExtended(options);
        mesh.ThrowIfIncomplete();
        CosDictionary dictionary = ShadingDictionary(mesh.ShadingType, mesh.ColorSpace, options);
        if (verticesPerRow > 0)
        {
            dictionary[ShadingNames.VerticesPerRow] = new CosInteger(verticesPerRow);
        }

        if (mesh.Function is { } function)
        {
            (CosObject value, _) = ShadingFunction(mesh.ColorSpace, function, 1);
            dictionary[ShadingNames.Function] = value;
        }

        CosStream stream = mesh.Encode(dictionary);
        RequireFeature(VersionedFeatures.Shadings);
        return (PdfMeshShading)GetShading(AddObject(stream))!;
    }

    private PdfShading AddShading(CosDictionary dictionary)
    {
        RequireFeature(VersionedFeatures.Shadings);
        return GetShading(AddObject(dictionary))!;
    }

    /// <summary>The entries common to all shadings (Table 77), after checking the space and the options against it.</summary>
    private CosDictionary ShadingDictionary(PdfShadingType type, PdfColorSpace space, PdfShadingOptions? options)
    {
        ThrowIfNotShadingSpace(space, nameof(space), allowIndexed: type >= PdfShadingType.FreeFormTriangleMesh);
        var dictionary = new CosDictionary
        {
            [ShadingNames.ShadingType] = new CosInteger((int)type),
            [ShadingNames.ColorSpace] = DrawingCache.Value(space),
        };
        if (options is null)
        {
            return dictionary;
        }

        if (options.Background is { } background)
        {
            if (!SameSpace(background.ColorSpace, space) || background.ComponentCount != space.ComponentCount)
            {
                throw new ArgumentException("The background shall be a colour of the shading's colour space (ISO 32000-2 Table 77).", nameof(options));
            }

            foreach (float component in background.Components)
            {
                if (!float.IsFinite(component))
                {
                    throw new ArgumentException("The background's components shall be finite numbers.", nameof(options));
                }
            }

            dictionary[ShadingNames.Background] = Components(background);
        }

        if (options.BoundingBox is { } box)
        {
            ThrowIfNotFinite(box.Left, box.Bottom, box.Right, box.Top);
            dictionary[ShadingNames.BBox] = DrawingCache.Rectangle(box);
        }

        if (options.AntiAlias)
        {
            dictionary[ShadingNames.AntiAlias] = CosBoolean.True;
        }

        return dictionary;
    }

    private static bool SameSpace(PdfColorSpace a, PdfColorSpace b) =>
        ReferenceEquals(a, b) || (a.Family == b.Family && a.Family is PdfColorSpaceFamily.DeviceGray or PdfColorSpaceFamily.DeviceRgb or PdfColorSpaceFamily.DeviceCmyk);

    private void ThrowIfNotShadingSpace(PdfColorSpace space, string parameter, bool allowIndexed)
    {
        ArgumentNullException.ThrowIfNull(space, parameter);
        if (space.Cache is { } cache && !ReferenceEquals(cache.Document, this))
        {
            throw new ArgumentException("The colour space belongs to another document.", parameter);
        }

        if (space.Family == PdfColorSpaceFamily.Pattern || (!allowIndexed && space.Family == PdfColorSpaceFamily.Indexed))
        {
            throw new ArgumentException(
                "A shading's colour space shall not be a Pattern space, nor an Indexed space where a function colours it (ISO 32000-2 §8.7.4.3 and §8.7.4.4).", parameter);
        }

        if (VersionedFeatures.ColorSpace(space) is { } feature)
        {
            RequireFeature(feature);
        }
    }

    /// <summary>Checks a function of the caller's for a shading and returns its value and domain.</summary>
    private (CosObject Value, double[] Domain) ShadingFunction(PdfColorSpace space, PdfFunction function, int inputs)
    {
        ArgumentNullException.ThrowIfNull(function);
        ThrowIfNotShadingSpace(space, nameof(space), allowIndexed: false);
        if (!ReferenceEquals(function.Document, this))
        {
            throw new ArgumentException("The function belongs to another document.", nameof(function));
        }

        if (!function.IsValid || function.InputCount != inputs || function.OutputCount != space.ComponentCount || function.Domain.Count != 2 * inputs)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The shading's function shall be a usable function of {inputs} input(s) to {space.ComponentCount} output(s), the components of its colour space (ISO 32000-2 §8.7.4.5)."),
                nameof(function));
        }

        RequireFeature(function.FunctionType == PdfFunctionType.Sampled ? VersionedFeatures.SampledFunction : VersionedFeatures.CalculatedFunction);
        return ((CosObject?)function.Reference ?? function.CosObject, [.. function.Domain]);
    }

    /// <summary>Builds the Type 2 or Type 3 function of gradient stops (direct), after checking them; records PDF 1.3.</summary>
    private CosDictionary GradientFunction(IReadOnlyList<PdfGradientStop> stops, out PdfColorSpace space)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Count < 2)
        {
            throw new ArgumentException("A gradient needs at least two stops.", nameof(stops));
        }

        space = stops[0].Color.ColorSpace;
        ThrowIfNotShadingSpace(space, nameof(stops), allowIndexed: false);
        double previous = 0;
        foreach (PdfGradientStop stop in stops)
        {
            if (!double.IsFinite(stop.Offset) || stop.Offset < previous || stop.Offset > 1)
            {
                throw new ArgumentException("Stop offsets shall be numbers from 0 to 1, each at least the one before.", nameof(stops));
            }

            if (!SameSpace(stop.Color.ColorSpace, space))
            {
                throw new ArgumentException("Every stop of a gradient shall be in one colour space.", nameof(stops));
            }

            if (stop.Color.ComponentCount != space.ComponentCount)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"A stop's colour has {stop.Color.ComponentCount} components; its space has {space.ComponentCount}."),
                    nameof(stops));
            }

            foreach (float component in stop.Color.Components)
            {
                if (!float.IsFinite(component))
                {
                    throw new ArgumentException("Colour components shall be finite numbers.", nameof(stops));
                }
            }

            previous = stop.Offset;
        }

        // The blends: one per pair of neighbouring stops that are apart, from offset 0 (the first colour before the first stop)
        // to 1 (the last colour after the last). Pairs at one offset are hard edges, which the half-open intervals of §7.10.4 give.
        var points = new List<PdfGradientStop>(stops.Count + 2);
        if (stops[0].Offset > 0)
        {
            points.Add(stops[0] with { Offset = 0 });
        }

        points.AddRange(stops);
        if (stops[^1].Offset < 1)
        {
            points.Add(stops[^1] with { Offset = 1 });
        }

        var functions = new CosArray();
        var bounds = new List<double>();
        var encode = new List<double>();
        for (int i = 0; i + 1 < points.Count; i++)
        {
            if (points[i + 1].Offset <= points[i].Offset)
            {
                continue;
            }

            if (functions.Count > 0)
            {
                bounds.Add(points[i].Offset);
            }

            functions.Add(new CosDictionary
            {
                [FunctionNames.FunctionType] = new CosInteger(2),
                [FunctionNames.Domain] = new CosArray { new CosInteger(0), new CosInteger(1) },
                [FunctionNames.C0] = Components(points[i].Color),
                [FunctionNames.C1] = Components(points[i + 1].Color),
                [FunctionNames.N] = new CosInteger(1),
            });
            encode.Add(0);
            encode.Add(1);
        }

        RequireFeature(VersionedFeatures.CalculatedFunction);
        if (functions.Count == 1)
        {
            return (CosDictionary)functions[0];
        }

        return new CosDictionary
        {
            [FunctionNames.FunctionType] = new CosInteger(3),
            [FunctionNames.Domain] = new CosArray { new CosInteger(0), new CosInteger(1) },
            [FunctionNames.Functions] = functions,
            [FunctionNames.Bounds] = DrawingCache.Numbers([.. bounds]),
            [FunctionNames.Encode] = DrawingCache.Numbers([.. encode]),
        };
    }
}
