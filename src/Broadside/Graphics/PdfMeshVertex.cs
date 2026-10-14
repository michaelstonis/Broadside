namespace Broadside.Graphics;

/// <summary>A vertex of a triangle mesh shading: a point in the shading's target space and its colour, or its parametric value t.</summary>
/// <remarks>
/// ISO 32000-2 §8.7.4.5.5 and §8.7.4.5.6: each vertex has coordinates and either n colour components (in the shading's colour
/// space) or, when the shading has a function, a single parametric value t that the function maps to a colour.
/// </remarks>
public readonly struct PdfMeshVertex
{
    /// <summary>Initializes a new instance of the <see cref="PdfMeshVertex"/> struct with a colour, for a mesh without a function.</summary>
    /// <param name="x">The horizontal coordinate, in the shading's target space.</param>
    /// <param name="y">The vertical coordinate, in the shading's target space.</param>
    /// <param name="color">The colour, in the mesh's colour space.</param>
    public PdfMeshVertex(double x, double y, PdfColor color)
    {
        Point = new PathPoint(x, y);
        Color = color;
    }

    /// <summary>Initializes a new instance of the <see cref="PdfMeshVertex"/> struct with a parametric value, for a mesh with a function.</summary>
    /// <param name="x">The horizontal coordinate, in the shading's target space.</param>
    /// <param name="y">The vertical coordinate, in the shading's target space.</param>
    /// <param name="t">The parametric value, within the function's domain.</param>
    public PdfMeshVertex(double x, double y, double t)
    {
        Point = new PathPoint(x, y);
        Parameter = t;
        IsParametric = true;
    }

    /// <summary>Gets the vertex's coordinates.</summary>
    public PathPoint Point { get; }

    /// <summary>Gets the vertex's colour; meaningless when <see cref="IsParametric"/>.</summary>
    public PdfColor Color { get; }

    /// <summary>Gets the vertex's parametric value t; meaningful only when <see cref="IsParametric"/>.</summary>
    public double Parameter { get; }

    /// <summary>Gets a value indicating whether the vertex has a parametric value rather than a colour.</summary>
    public bool IsParametric { get; }
}
