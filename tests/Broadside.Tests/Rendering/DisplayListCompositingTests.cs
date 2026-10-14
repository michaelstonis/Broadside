using System.Text;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Constant alpha and blend mode in the display list (ISO 32000-2 §11.6.3, §11.6.4.4; ADR 0006): the stroking and nonstroking alpha
/// constants and the blend mode a <c>gs</c> sets reach the backend through <see cref="IRenderingBackend.SetCompositing"/> before the
/// first paint they apply to, only when they differ from what the backend holds, and are saved and restored with the state.
/// </summary>
public sealed class DisplayListCompositingTests
{
    private const string Resources = "/ExtGState << /Half << /CA 0.5 /ca 0.25 /BM /Multiply >> /Opaque << /CA 1 /ca 1 /BM /Normal >> >>";

    [Fact]
    public void Alpha_constants_and_blend_mode_reach_the_backend_before_the_paint_they_apply_to()
    {
        List<string> operations = Record("/Half gs 0 0 10 10 re f");

        Assert.Equal(
            [
                "compositing 0.5 0.25 Multiply",
                "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | DeviceGray 0",
            ],
            operations);
    }

    [Fact]
    public void A_restore_brings_back_the_compositing_of_its_save_without_another_record()
    {
        List<string> operations = Record("q /Half gs 0 0 1 1 re f Q 0 0 2 2 re f");

        Assert.Equal(
            [
                "save",
                "compositing 0.5 0.25 Multiply",
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "restore",
                "fill nonzero M 0 0 L 2 0 L 2 2 L 0 2 Z | DeviceGray 0",
            ],
            operations);
    }

    [Fact]
    public void Compositing_equal_to_what_the_backend_holds_or_followed_by_no_paint_records_nothing()
    {
        List<string> operations = Record("/Opaque gs 0 0 1 1 re f q /Half gs Q /Half gs 0 0 1 1 re f /Half gs 0 0 2 2 re f /Half gs");

        Assert.Equal(
            [
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "compositing 0.5 0.25 Multiply",
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "fill nonzero M 0 0 L 2 0 L 2 2 L 0 2 Z | DeviceGray 0",
            ],
            operations);
    }

    [Fact]
    public void Images_shadings_and_stencil_masks_are_painted_with_the_compositing_in_force()
    {
        List<string> operations = Record(
            "q /Half gs 10 0 0 10 0 0 cm BI /W 1 /H 1 /CS /G /BPC 8 ID \u0080 EI BI /W 1 /H 1 /IM true ID \u0000 EI Q /Half gs /Sh0 sh",
            "/Shading << /Sh0 5 0 R >>",
            "<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 1 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >>");

        // The restore brings back the compositing saved before the gs, so sh needs its own record.
        Assert.Equal(
            [
                "save",
                "compositing 0.5 0.25 Multiply",
                "transform [10 0 0 10 0 0]",
                "image 1x1 DeviceGray bpc 8",
                "imagemask 1x1 | DeviceGray 0",
                "restore",
                "compositing 0.5 0.25 Multiply",
                "shading Axial [1 0 0 1 0 0]",
            ],
            operations);
    }

    [Fact]
    public void A_glyph_run_ends_where_the_compositing_changes()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Encoding.ASCII.GetBytes("BT /F1 10 Tf (AB) Tj /Half gs (CD) Tj ET"), Resources));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(2, recorder.Operations.Count(line => line.StartsWith("glyphs", StringComparison.Ordinal)));
        Assert.Equal("compositing 0.5 0.25 Multiply", recorder.Operations[^2]);
    }

    [Fact]
    public void A_shading_pattern_s_nonstroking_alpha_and_blend_mode_join_those_of_the_paint()
    {
        // §11.6.7: the pattern's ExtGState augments the state its shading is painted with; only what affects sh counts, so its CA
        // (a stroking parameter) is ignored, and its ca scales the paint's alpha, nonstroking for a fill, stroking for a stroke.
        List<string> operations = Record(
            "/Half gs /Pattern cs /P0 scn /Pattern CS /P0 SCN 0 0 10 10 re f 0 0 5 5 re S 0 0 1 1 re f",
            "/Pattern << /P0 5 0 R >>",
            "<< /Type /Pattern /PatternType 2 /ExtGState << /ca 0.5 /CA 0.1 /BM /Screen >> /Shading << /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 1 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >> >>");

        Assert.Equal(
            [
                "compositing 0.5 0.125 Screen",
                "fill nonzero M 0 0 L 10 0 L 10 10 L 0 10 Z | shading Axial [1 0 0 1 0 0] background",
                "compositing 0.25 0.25 Screen",
                "stroke 1 Butt Miter 10 [] 0 M 0 0 L 5 0 L 5 5 L 0 5 Z | shading Axial [1 0 0 1 0 0] background",
                "compositing 0.5 0.125 Screen",
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | shading Axial [1 0 0 1 0 0] background",
            ],
            operations);
    }

    private static List<string> Record(string content, string resources = "", params string[] objects)
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), Resources + " " + resources, objects));
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        return recorder.Operations;
    }
}
