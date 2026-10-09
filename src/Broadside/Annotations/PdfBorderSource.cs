namespace Broadside.Annotations;

/// <summary>Where an annotation's border was read from.</summary>
/// <remarks>ISO 32000-2 §12.5.4 and §12.5.2, Table 166: <c>BS</c> wins over <c>Border</c>; with neither, the default applies.</remarks>
public enum PdfBorderSource
{
    /// <summary>Neither entry is present: solid, 1 point, square corners (§12.5.4).</summary>
    Default,

    /// <summary>The <c>Border</c> array (Table 166).</summary>
    BorderArray,

    /// <summary>The border style dictionary <c>BS</c> (Table 168).</summary>
    BorderStyle,
}
