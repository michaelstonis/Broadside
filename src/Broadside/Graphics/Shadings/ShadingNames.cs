using Broadside.Objects;

namespace Broadside.Graphics.Shadings;

/// <summary>The names shadings and patterns use (ISO 32000-2 Tables 74 to 85), created once.</summary>
/// <remarks>Kept out of <see cref="KnownNames"/> so that the content tickets editing it in parallel do not collide.</remarks>
internal static class ShadingNames
{
    public static readonly CosName ShadingType = new("ShadingType");
    public static readonly CosName ColorSpace = new("ColorSpace");
    public static readonly CosName CS = new("CS");
    public static readonly CosName Background = new("Background");
    public static readonly CosName BBox = new("BBox");
    public static readonly CosName AntiAlias = new("AntiAlias");
    public static readonly CosName Function = new("Function");
    public static readonly CosName Domain = new("Domain");
    public static readonly CosName Matrix = new("Matrix");
    public static readonly CosName Coords = new("Coords");
    public static readonly CosName Extend = new("Extend");
    public static readonly CosName BitsPerCoordinate = new("BitsPerCoordinate");
    public static readonly CosName BitsPerComponent = new("BitsPerComponent");
    public static readonly CosName BitsPerFlag = new("BitsPerFlag");
    public static readonly CosName Decode = new("Decode");
    public static readonly CosName VerticesPerRow = new("VerticesPerRow");
    public static readonly CosName PatternType = new("PatternType");
    public static readonly CosName PaintType = new("PaintType");
    public static readonly CosName TilingType = new("TilingType");
    public static readonly CosName XStep = new("XStep");
    public static readonly CosName YStep = new("YStep");
    public static readonly CosName Resources = new("Resources");
    public static readonly CosName Shading = new("Shading");
    public static readonly CosName ExtGState = new("ExtGState");
}
