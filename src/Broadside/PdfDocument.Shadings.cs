using Broadside.Graphics;
using Broadside.Graphics.Shadings;
using Broadside.Objects;

namespace Broadside;

/// <summary>Shadings and patterns (issue #79): resolved models cached per document.</summary>
public sealed partial class PdfDocument
{
    private ShadingCache? _shadings;

    /// <summary>Gets the document's shadings and patterns, created on first use.</summary>
    internal ShadingCache Shadings
    {
        get
        {
            ShadingCache? cache = Volatile.Read(ref _shadings);
            return cache ?? Interlocked.CompareExchange(ref _shadings, new ShadingCache(this), null) ?? _shadings!;
        }
    }

    /// <summary>Gets or sets the most vertices a mesh shading decodes (a patch counts as 16); the rest is dropped with a diagnostic.</summary>
    /// <remarks>Guards against decompression bombs feeding meshes (ISO 32000-2 §8.7.4.5.5 to §8.7.4.5.8). Settable for tests.</remarks>
    internal int MaxMeshVertices { get; set; } = MeshLayout.DefaultMaxVertices;

    /// <summary>Returns the resolved model of a shading of this document.</summary>
    /// <param name="shading">A shading dictionary or stream, or an indirect reference to one, such as a value of a resource dictionary's <c>Shading</c> subdictionary.</param>
    /// <returns>
    /// The model, shared by every caller asking for the same object until it changes; <see langword="null"/>, with a diagnostic,
    /// when the object is not a shading of Type 1 to 7. A shading that cannot be painted is returned with
    /// <see cref="PdfShading.IsValid"/> <see langword="false"/>.
    /// </returns>
    /// <exception cref="Broadside.Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the shading.</exception>
    /// <remarks>ISO 32000-2 §8.7.4. Deviations are recorded on <see cref="Diagnostics"/> once, when the shading is first read.</remarks>
    public PdfShading? GetShading(CosObject shading)
    {
        ArgumentNullException.ThrowIfNull(shading);
        return Shadings.GetShading(shading, owner: null);
    }

    /// <summary>Returns the resolved model of a pattern of this document.</summary>
    /// <param name="pattern">A pattern dictionary or stream, or an indirect reference to one, such as a value of a resource dictionary's <c>Pattern</c> subdictionary.</param>
    /// <returns>
    /// The model, shared by every caller asking for the same object until it changes; <see langword="null"/>, with a diagnostic,
    /// when the object is not a pattern of Type 1 or 2. A pattern that cannot be painted is returned with
    /// <see cref="PdfPattern.IsValid"/> <see langword="false"/>.
    /// </returns>
    /// <exception cref="Broadside.Diagnostics.DiagnosticException">In strict mode, for the first deviation found in the pattern.</exception>
    /// <remarks>ISO 32000-2 §8.7.1. Deviations are recorded on <see cref="Diagnostics"/> once, when the pattern is first read.</remarks>
    public PdfPattern? GetPattern(CosObject pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return Shadings.GetPattern(pattern, owner: null);
    }
}
