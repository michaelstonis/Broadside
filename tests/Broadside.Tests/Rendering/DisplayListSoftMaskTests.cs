using System.Text;
using Broadside.Graphics;
using Broadside.Rendering;
using Broadside.TestSupport;

namespace Broadside.Tests.Rendering;

/// <summary>
/// Soft masks in the display list (ISO 32000-2 §11.5, §11.6.4.3, §11.6.5.1; ADR 0006): the graphics state's mask reaches the backend
/// through <see cref="IRenderingBackend.SetSoftMask"/> before the paints it applies to, its group a list of its own built once per
/// mask; a group XObject painted under a mask carries it as <see cref="DisplayGroup.SoftMask"/>.
/// </summary>
public sealed class DisplayListSoftMaskTests
{
    // Object 5: the luminosity mask's group (gray, BC 1); 6: an alpha mask's group; 7: a transfer function; 8: a transparency group form.
    private const string States =
        "/ExtGState << /L << /SMask << /Type /Mask /S /Luminosity /G 5 0 R /BC [1] >> >>"
        + " /A << /SMask << /Type /Mask /S /Alpha /G 6 0 R /TR 7 0 R >> /AIS true >>"
        + " /N << /SMask /None >> >>";

    private const string Inversion = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>";

    private static readonly string LuminosityGroup = Form("/Group << /S /Transparency /CS /DeviceGray >> /BBox [0 0 10 10]", "0.5 g 0 0 10 10 re f");
    private static readonly string AlphaGroup = Form("/Group << /S /Transparency >> /BBox [0 0 20 20]", "0 0 20 20 re f");
    private static readonly string GroupForm = Form("/Group << /S /Transparency >> /BBox [0 0 10 10]", "0 0 10 10 re f");

    // Object 9: a 1 x 1 gray image with a soft mask of its own (object 10).
    private static readonly string MaskedImage = ContentSamples.Stream(
        "\u0080",
        "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /SMask 10 0 R");

    private static readonly string ImageSoftMask = ContentSamples.Stream(
        "\u0040",
        "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8");

    [Fact]
    public void The_mask_in_the_graphics_state_reaches_the_backend_before_the_paints_it_applies_to()
    {
        List<string> operations = Record("/L gs 0 0 1 1 re f /A gs 0 0 2 2 re f /N gs 0 0 3 3 re f");

        // Luminosity: the group's space and BC; alpha: TR and AIS (§11.6.5.1, Table 142); None turns masking off (§11.6.4.3).
        Assert.Equal(
            [
                "softmask Luminosity [1 0 0 1 0 0] DeviceGray 1 bbox [0 0 10 10]",
                "fill nonzero M 0 0 L 1 0 L 1 1 L 0 1 Z | DeviceGray 0",
                "softmask Alpha [1 0 0 1 0 0] tr Exponential shape bbox [0 0 20 20]",
                "fill nonzero M 0 0 L 2 0 L 2 2 L 0 2 Z | DeviceGray 0",
                "softmask None",
                "fill nonzero M 0 0 L 3 0 L 3 3 L 0 3 Z | DeviceGray 0",
            ],
            operations);
    }

    [Fact]
    public void A_mask_is_built_once_for_every_paint_under_it()
    {
        // 100 paints in their own save each: one mask record per paint (the restore drops it), one group list for all.
        string content = string.Concat(Enumerable.Range(0, 100).Select(i => $"q /L gs {i} 0 1 1 re f Q "));

        Record(content, out RecordingBackend recorder);

        Assert.Equal(100, recorder.SoftMasks.Count);
        Assert.Single(recorder.SoftMasks.Select(mask => mask.Content).Distinct());
        Assert.All(recorder.SoftMasks, mask => Assert.Equal(PdfSoftMaskType.Luminosity, mask.Subtype));
    }

    [Fact]
    public void The_mask_matrix_is_the_transform_when_the_state_was_set()
    {
        // §11.6.5.1: the mask's coordinate system is the CTM at gs, not at the paint.
        List<string> operations = Record("q 1 0 0 1 3 0 cm 2 0 0 2 5 5 cm /L gs 1 0 0 1 4 4 cm 0 0 1 1 re f Q");

        Assert.Contains("softmask Luminosity [2 0 0 2 8 5] DeviceGray 1 bbox [0 0 10 10]", operations);
    }

    [Fact]
    public void An_image_with_a_mask_of_its_own_paints_without_the_graphics_states_mask()
    {
        List<string> operations = Record("/L gs 0 0 1 1 re f q 10 0 0 10 0 0 cm /Im0 Do Q 0 0 2 2 re f");

        int image = operations.FindIndex(line => line.StartsWith("image", StringComparison.Ordinal));
        Assert.True(image > 0);
        Assert.Equal("softmask None", operations.Take(image).Last(line => line.StartsWith("softmask", StringComparison.Ordinal)));
        Assert.Equal(2, operations.Count(line => line.StartsWith("softmask", StringComparison.Ordinal)));
        Assert.Equal("fill nonzero M 0 0 L 2 0 L 2 2 L 0 2 Z | DeviceGray 0", operations[^1]);
    }

    [Fact]
    public void A_group_painted_under_a_mask_carries_it_and_its_content_paints_unmasked()
    {
        List<string> operations = Record("/L gs 0 0 1 1 re f q 2 0 0 2 0 0 cm /Fm0 Do Q");

        Assert.Contains(
            "group Transparency bbox [0 0 10 10] [2 0 0 2 0 0] softmask Luminosity [1 0 0 1 0 0] DeviceGray 1 bbox [0 0 10 10]",
            operations);

        // §11.6.6: the group's content starts with no soft mask; the backend held the page's, so the content turns it off.
        int group = operations.FindIndex(line => line.StartsWith("group", StringComparison.Ordinal));
        int end = operations.IndexOf("end group");
        Assert.Contains("softmask None", operations.GetRange(group, end - group));
    }

    [Fact]
    public void A_mask_set_in_a_type3_glyph_moves_with_every_show_of_the_glyph()
    {
        using PdfDocument document = PdfDocument.Open(ContentSamples.OnePage(
            Encoding.Latin1.GetBytes("BT /T3 10 Tf 100 100 Td (aa) Tj ET"),
            "/Font << /T3 5 0 R >>",
            "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /a 6 0 R >> "
            + "/Encoding << /Type /Encoding /Differences [97 /a] >> /FirstChar 97 /LastChar 97 /Widths [1000] "
            + "/Resources << /ExtGState << /L << /SMask << /S /Luminosity /G 7 0 R >> >> >> >> >>",
            ContentSamples.Stream("1000 0 d0 /L gs 0 0 1000 1000 re f"),
            LuminosityGroup));
        var recorder = new RecordingBackend();

        document.Pages[0].CreateDisplayList().Replay(recorder);

        string[] masks = [.. recorder.Operations.Where(line => line.StartsWith("softmask Luminosity", StringComparison.Ordinal))];
        Assert.Equal(2, masks.Length);
        Assert.Contains(" 100 100]", masks[0], StringComparison.Ordinal);
        Assert.Contains(" 110 100]", masks[1], StringComparison.Ordinal);
        Assert.Single(recorder.SoftMasks.Where(mask => !mask.IsNone).Select(mask => mask.Content).Distinct());
    }

    private static string Form(string entries, string content, string resources = "") =>
        ContentSamples.Stream(content, $"/Type /XObject /Subtype /Form {entries} /Resources << {resources} >>");

    private static List<string> Record(string content) => Record(content, out _);

    private static List<string> Record(string content, out RecordingBackend recorder)
    {
        using PdfDocument document = Open(content);
        recorder = new RecordingBackend();
        document.Pages[0].CreateDisplayList().Replay(recorder);
        return recorder.Operations;
    }

    private static PdfDocument Open(string content) => PdfDocument.Open(ContentSamples.OnePage(
        Encoding.Latin1.GetBytes(content),
        States + " /XObject << /Fm0 8 0 R /Im0 9 0 R >>",
        LuminosityGroup,
        AlphaGroup,
        Inversion,
        GroupForm,
        MaskedImage,
        ImageSoftMask));
}
