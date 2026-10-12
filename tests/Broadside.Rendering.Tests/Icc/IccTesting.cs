using Broadside.Graphics;
using Broadside.Objects;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>Helpers shared by the ICC engine tests.</summary>
internal static class IccTesting
{
    /// <summary>The colour space a resource name of the first page's ColorSpace subdictionary describes.</summary>
    public static PdfColorSpace Space(PdfDocument document, string resource)
    {
        var resources = (CosDictionary)document.Pages[0].Resources!;
        var spaces = (CosDictionary)document.Resolve(resources[new CosName("ColorSpace")]);
        return document.GetColorSpace(spaces[new CosName(resource)]);
    }
}
