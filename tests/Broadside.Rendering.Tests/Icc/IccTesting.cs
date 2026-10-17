using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>Helpers shared by the ICC engine tests.</summary>
internal static class IccTesting
{
    /// <summary>
    /// RGB, media-relative colorimetric, black point compensation OFF (ISO 32000-2 §8.6.5.9): how the Little CMS and profile-maths
    /// references of #117 and #118 were computed.
    /// </summary>
    public static ColorConversion WithoutCompensation { get; } = new() { BlackPointCompensation = BlackPointCompensation.Off };

    /// <summary>The colour space a resource name of the first page's ColorSpace subdictionary describes.</summary>
    public static PdfColorSpace Space(PdfDocument document, string resource)
    {
        var resources = (CosDictionary)document.Pages[0].Resources!;
        var spaces = (CosDictionary)document.Resolve(resources[new CosName("ColorSpace")]);
        return document.GetColorSpace(spaces[new CosName(resource)]);
    }
}
