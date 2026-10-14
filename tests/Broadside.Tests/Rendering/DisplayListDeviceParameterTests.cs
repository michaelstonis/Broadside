using System.Text;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Overprint and halftones in the display list (ISO 32000-2 §8.6.7, §10.6; ADR 0006): the overprint flags, the overprint mode, the
/// halftone and its origin reach the backend through <see cref="IRenderingBackend.SetDeviceParameters"/> before the first paint they
/// apply to, only when they differ from what the backend holds, and are saved and restored with the state, so a backend for a
/// device that overprints or halftones can honour them.
/// </summary>
public sealed class DisplayListDeviceParameterTests
{
    private const string Resources =
        "/ExtGState << /Over << /OP true /OPM 1 >> /FillOnly << /OP false /op true >> /Off << /OP false /op false /OPM 0 >> "
        + "/Screen << /HT << /Type /Halftone /HalftoneType 1 /Frequency 60 /Angle 45 /SpotFunction /Round >> /HTO [10 20] >> "
        + "/DefaultScreen << /HT /Default >> >>";

    [Fact]
    public void Overprint_flags_and_mode_reach_the_backend_before_the_paint_they_apply_to()
    {
        // §8.4.5, Table 57: OP sets both flags when op is absent.
        List<string> operations = Record("/Over gs 0 0 1 1 re f /FillOnly gs 0 0 2 2 re f");

        Assert.Equal(
            [
                "device overprint true true 1",
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "device overprint false true 1",
                "fill nonzero M 0 0 L 2 0 L 2 2 L 0 2 Z | DeviceGray 0",
            ],
            operations);
    }

    [Fact]
    public void A_restore_brings_back_the_saved_parameters_and_unchanged_or_unused_ones_record_nothing()
    {
        List<string> operations = Record("/Off gs 0 0 1 1 re f q /Over gs Q q /Over gs 0 0 2 2 re f Q 0 0 3 3 re f /Over gs");

        Assert.Equal(
            [
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "save",
                "device overprint true true 1",
                "fill nonzero M 0 0 L 2 0 L 2 2 L 0 2 Z | DeviceGray 0",
                "restore",
                "fill nonzero M 0 0 L 3 0 L 3 3 L 0 3 Z | DeviceGray 0",
            ],
            operations);
    }

    [Fact]
    public void A_halftone_dictionary_and_its_origin_are_handed_over_as_written()
    {
        using PdfDocument document = Open("/Screen gs 0 0 1 1 re f /DefaultScreen gs 0 0 2 2 re f");
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(
            [
                "device overprint false false 0 halftone 1 origin 10 20",
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "device overprint false false 0 origin 10 20",
                "fill nonzero M 0 0 L 2 0 L 2 2 L 0 2 Z | DeviceGray 0",
            ],
            recorder.Operations);
        DisplayDeviceParameters screen = recorder.DeviceParameters[0];
        var halftone = Assert.IsType<CosDictionary>(screen.Halftone);
        Assert.Equal(new CosName("Round"), halftone[new CosName("SpotFunction")]);
        Assert.Equal(new PathPoint(10, 20), screen.HalftoneOrigin);
        Assert.Null(recorder.DeviceParameters[1].Halftone);
    }

    [Fact]
    public void Every_kind_of_paint_is_preceded_by_the_parameters_in_force()
    {
        List<string> operations = Record(
            "q /Over gs 10 0 0 10 0 0 cm BI /W 1 /H 1 /CS /G /BPC 8 ID \u0080 EI Q 0 0 1 1 re S q /Over gs /Sh0 sh Q",
            "/Shading << /Sh0 5 0 R >>",
            "<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 1 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >>");

        Assert.Equal(
            [
                "save",
                "device overprint true true 1",
                "transform [10 0 0 10 0 0]",
                "image 1x1 DeviceGray bpc 8",
                "restore",
                "stroke 1 Butt Miter 10 [] 0 M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "save",
                "device overprint true true 1",
                "shading Axial [1 0 0 1 0 0]",
                "restore",
            ],
            operations);
    }

    [Fact]
    public void A_glyph_run_ends_where_the_overprint_changes()
    {
        using PdfDocument document = PdfDocument.Open(TextSamples.Page(Encoding.ASCII.GetBytes("BT /F1 10 Tf (AB) Tj /Over gs (CD) Tj ET"), Resources));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        Assert.Equal(2, recorder.Operations.Count(line => line.StartsWith("glyphs", StringComparison.Ordinal)));
        Assert.Equal("device overprint true true 1", recorder.Operations[^2]);
    }

    private static PdfDocument Open(string content, string resources = "", params string[] objects) =>
        PdfDocument.Open(ContentSamples.OnePage(Encoding.Latin1.GetBytes(content), Resources + " " + resources, objects));

    private static List<string> Record(string content, string resources = "", params string[] objects)
    {
        using PdfDocument document = Open(content, resources, objects);
        var recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        return recorder.Operations;
    }
}
