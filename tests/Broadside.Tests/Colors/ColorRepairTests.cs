using Broadside.Diagnostics;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Colors;

/// <summary>
/// Every real-world deviation of colour spaces and colour operators (ISO 32000-2 §8.6) as a lenient/strict pair: lenient reading
/// repairs it, paints the page and records exactly one kind of diagnostic; strict reading throws that diagnostic (ADR 0005).
/// </summary>
public class ColorRepairTests
{
    private const string Paint = " 0 0 10 10 re f";

    private const string Gray = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>";

    public static TheoryData<string, string, string, string> Deviations => new()
    {
        { "missing named space", "<< >>", "/Nowhere cs" + Paint, "ContentColorSpaceMissing" },
        { "abbreviation outside an inline image", "<< >>", "/CMYK cs 0 0 0 1 sc" + Paint, "ContentColorSpaceAbbreviated" },
        { "unknown family", "<< /ColorSpace << /X [/Hexachrome 1] >> >>", "/X cs" + Paint, "ColorSpaceInvalid" },
        { "device space as an array", "<< /ColorSpace << /X [/DeviceRGB] >> >>", "/X cs" + Paint, "ColorSpaceEntryInvalid" },
        { "CalRGB without a white point", "<< /ColorSpace << /X [/CalRGB << >>] >> >>", "/X cs" + Paint, "ColorSpaceEntryInvalid" },
        { "negative CalGray gamma", "<< /ColorSpace << /X [/CalGray << /WhitePoint [0.95 1 1.09] /Gamma -2 >>] >> >>", "/X cs" + Paint, "ColorSpaceEntryInvalid" },
        { "negative black point", "<< /ColorSpace << /X [/CalGray << /WhitePoint [0.95 1 1.09] /BlackPoint [-1 0 0] >>] >> >>", "/X cs" + Paint, "ColorSpaceEntryInvalid" },
        { "Lab range minimum above maximum", "<< /ColorSpace << /X [/Lab << /WhitePoint [0.96 1 0.82] /Range [10 -10 0 1] >>] >> >>", "/X cs" + Paint, "ColorSpaceEntryInvalid" },
        { "ICCBased stream that is not a profile", "<< /ColorSpace << /X [/ICCBased << /N 3 /Length 1 >>] >> >>", "/X cs" + Paint, "ColorSpaceInvalid" },
        { "Indexed hival above 255", "<< /ColorSpace << /X [/Indexed /DeviceGray 256 <" + new string('0', 512) + ">] >> >>", "/X cs" + Paint, "ColorSpaceEntryInvalid" },
        { "Indexed lookup short", "<< /ColorSpace << /X [/Indexed /DeviceRGB 1 <FF0000>] >> >>", "/X cs 1 sc" + Paint, "IndexedLookupInvalid" },
        { "Indexed lookup long", "<< /ColorSpace << /X [/Indexed /DeviceGray 0 <0000>] >> >>", "/X cs" + Paint, "IndexedLookupInvalid" },
        { "Indexed lookup missing", "<< /ColorSpace << /X [/Indexed /DeviceGray 1 7] >> >>", "/X cs" + Paint, "IndexedLookupInvalid" },
        { "Indexed base Indexed", "<< /ColorSpace << /X [/Indexed [/Indexed /DeviceGray 0 <00>] 0 <00>] >> >>", "/X cs" + Paint, "ColorSpaceInvalid" },
        { "tint transform not a function", "<< /ColorSpace << /X [/Separation /S /DeviceGray 3] >> >>", "/X cs" + Paint, "TintTransformInvalid" },
        { "tint transform with too few outputs", "<< /ColorSpace << /X [/Separation /S /DeviceCMYK " + Gray + "] >> >>", "/X cs" + Paint, "TintTransformInvalid" },
        { "alternate that cannot be an alternate", "<< /ColorSpace << /X [/Separation /S /Pattern " + Gray + "] >> >>", "/X cs" + Paint, "ColorSpaceEntryInvalid" },
        { "DeviceN repeated names", "<< /ColorSpace << /X [/DeviceN [/A /A] /DeviceGray 6 0 R] >> >>", "/X cs" + Paint, "DeviceNColorantsInvalid" },
        { "default of the wrong size", "<< /ColorSpace << /DefaultRGB /DeviceCMYK >> >>", "1 0 0 rg" + Paint, "DefaultColorSpaceInvalid" },
        { "SC in a Separation space", "<< /ColorSpace << /X [/Separation /S /DeviceGray " + Gray + "] >> >>", "/X cs 0.5 sc" + Paint, "ContentColorOperatorMismatch" },
        { "sc in a Pattern space", "<< >>", "/Pattern cs 0.5 sc" + Paint, "ContentColorOperatorInvalid" },
        { "scn with a name in DeviceRGB", "<< >>", "/DeviceRGB cs 1 0 0 /P scn" + Paint, "ContentColorOperatorInvalid" },
        { "too few components", "<< >>", "/DeviceCMYK cs 1 sc" + Paint, "ContentColorOperandCount" },
        { "too many components", "<< >>", "/DeviceGray cs 1 0.5 sc" + Paint, "ContentColorOperandCount" },
        { "pattern not in the resources", "<< >>", "/Pattern cs /P scn" + Paint, "ContentPatternMissing" },
        { "space that contains itself", "<< /ColorSpace << /X 5 0 R >> >>", "/X cs" + Paint, "ColorSpaceCycle" },
    };

    [Theory]
    [MemberData(nameof(Deviations))]
    public void A_deviation_is_repaired_with_one_kind_of_diagnostic_and_strict_mode_throws_it(string deviation, string resources, string content, string code)
    {
        byte[] file = Page(resources, content, "[/Indexed 5 0 R 0 <00>]", "<< /FunctionType 4 /Domain [0 1 0 1] /Range [0 1] /Length 5 >>\nstream\n{pop}\nendstream");

        (List<Paint> paints, string[] codes) = Run(file);
        Assert.Single(paints);
        Assert.True(codes.Distinct().SequenceEqual([code]), $"{deviation}: {string.Join(", ", codes)}");

        using PdfDocument strict = PdfDocument.Open(file, new PdfOptions().UseStrict());
        var exception = Assert.Throws<DiagnosticException>(() => strict.Pages[0].ProcessContent(new ColorRecorder()));
        Assert.Equal(code, exception.Diagnostic.Code);
    }
}
