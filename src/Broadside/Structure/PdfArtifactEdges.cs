namespace Broadside.Structure;

/// <summary>The page edges an artifact is logically attached to: the <c>Attached</c> entry of its property list.</summary>
/// <remarks>ISO 32000-2 §14.8.2.2.2, Table 363: pagination and full-page background artifacts only; edges are the crop box's.</remarks>
[Flags]
public enum PdfArtifactEdges
{
    /// <summary>Attached to no edge: no <c>Attached</c> entry.</summary>
    None = 0,

    /// <summary>The top edge.</summary>
    Top = 1,

    /// <summary>The bottom edge.</summary>
    Bottom = 2,

    /// <summary>The left edge.</summary>
    Left = 4,

    /// <summary>The right edge.</summary>
    Right = 8,
}
