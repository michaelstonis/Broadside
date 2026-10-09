using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using static Broadside.Tests.Colors.ColorTesting;

namespace Broadside.Tests.Colors;

/// <summary>
/// Every colour space family of ISO 32000-2 §8.6.3 (Table 61) read through <see cref="PdfDocument.GetColorSpace"/>: the typed
/// views, their initial colours (Table 73), default decode arrays (Table 88) and the repairs lenient reading applies.
/// </summary>
public class ColorSpaceFamilyTests
{
    private static readonly CosReference Five = new(5, 0);

    [Fact]
    public void Device_family_names_are_the_device_singletons()
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Same(PdfDeviceGrayColorSpace.Instance, document.GetColorSpace(new CosName("DeviceGray")));
        Assert.Same(PdfDeviceRgbColorSpace.Instance, document.GetColorSpace(new CosName("DeviceRGB")));
        Assert.Same(PdfDeviceCmykColorSpace.Instance, document.GetColorSpace(new CosName("DeviceCMYK")));
        Assert.Equal([0f, 0f, 0f, 1f], PdfDeviceCmykColorSpace.Instance.GetInitialColor().Components.ToArray());
        Assert.Equal([0.0, 1, 0, 1, 0, 1], PdfDeviceRgbColorSpace.Instance.GetDefaultDecode(8));
        Assert.Equal(new PdfVersion(1, 1), PdfDeviceGrayColorSpace.Instance.MinimumVersion);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void CalGray_and_CalRGB_expose_their_dictionaries_with_defaults()
    {
        using PdfDocument document = PdfDocument.Create();
        var gray = (PdfCalGrayColorSpace)document.GetColorSpace(Cos("[/CalGray << /WhitePoint [0.9505 1 1.089] /Gamma 2.2 >>]"));
        var rgb = (PdfCalRgbColorSpace)document.GetColorSpace(Cos("[/CalRGB << /WhitePoint [0.9505 1 1.089] /BlackPoint [0.01 0.01 0.01] /Gamma [1.8 1.8 1.8] >>]"));

        Assert.Equal(new CieXyz(0.9505, 1, 1.089), gray.WhitePoint);
        Assert.Equal(default, gray.BlackPoint);
        Assert.Equal(2.2, gray.Gamma);
        Assert.Equal(1, gray.ComponentCount);
        Assert.Equal(new CieXyz(0.01, 0.01, 0.01), rgb.BlackPoint);
        Assert.Equal([1.8, 1.8, 1.8], rgb.Gamma);
        Assert.Equal([1.0, 0, 0, 0, 1, 0, 0, 0, 1], rgb.Matrix);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_missing_white_point_reads_as_D65_with_a_diagnostic_and_strict_mode_throws()
    {
        using PdfDocument document = PdfDocument.Create();
        var space = (PdfCalRgbColorSpace)document.GetColorSpace(Cos("[/CalRGB << /Gamma [0 1 1] >>]"));

        Assert.Equal(CieXyz.D65, space.WhitePoint);
        Assert.Equal([1.0, 1, 1], space.Gamma);
        Assert.Equal(["ColorSpaceEntryInvalid"], document.Diagnostics.Select(d => d.Code).Distinct());

        using PdfDocument strict = PdfDocument.Open(Page("<< >>", string.Empty), new PdfOptions().UseStrict());
        Assert.Throws<DiagnosticException>(() => strict.GetColorSpace(Cos("[/CalRGB << /Gamma [0 1 1] >>]")));
    }

    [Fact]
    public void Lab_ranges_bound_its_components_and_its_initial_colour()
    {
        using PdfDocument document = PdfDocument.Create();
        var lab = (PdfLabColorSpace)document.GetColorSpace(Cos("[/Lab << /WhitePoint [0.9642 1 0.8249] /Range [-50 50 10 60] >>]"));
        var bad = (PdfLabColorSpace)document.GetColorSpace(Cos("[/Lab << /WhitePoint [0.9642 1 0.8249] /Range [50 -50 0 1] >>]"));

        Assert.Equal(new ComponentRange(0, 100), lab.GetComponentRange(0));
        Assert.Equal(new ComponentRange(10, 60), lab.GetComponentRange(2));
        Assert.Equal([0f, 0f, 10f], lab.GetInitialColor().Components.ToArray());
        Assert.Equal([0.0, 100, -50, 50, 10, 60], lab.GetDefaultDecode(8));
        Assert.Equal([-100.0, 100, -100, 100], bad.Range);
        Assert.Equal(["ColorSpaceEntryInvalid"], document.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void ICCBased_reads_the_profile_header_and_falls_back_to_the_device_space_by_N()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty, IccProfiles.Stream(IccProfiles.Header("mntr", "RGB "), "/N 3 /Range [0 1 0 1 0 0.5]")));
        var space = (PdfIccBasedColorSpace)document.GetColorSpace(Cos("[/ICCBased 5 0 R]"));

        IccProfileHeader header = Assert.IsType<IccProfileHeader>(space.ProfileHeader);
        Assert.Equal(new Version(2, 1, 0), header.Version);
        Assert.Equal("mntr", header.DeviceClass);
        Assert.Equal("RGB ", header.DataColorSpace);
        Assert.Equal(3, header.ComponentCount);
        Assert.True(header.IsSupportedForPdf);
        Assert.Equal(0.9642, header.Illuminant.X, 4);
        Assert.Equal(3, space.ComponentCount);
        Assert.Equal(3, space.DeclaredComponentCount);
        Assert.Same(PdfDeviceRgbColorSpace.Instance, space.Alternate);
        Assert.Equal(new ComponentRange(0, 0.5), space.GetComponentRange(2));
        Assert.Equal(128, space.GetProfileData().Length);
        Assert.Equal(new PdfVersion(1, 3), space.MinimumVersion);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_valid_profile_header_wins_over_a_wrong_N_and_an_unusable_alternate_gives_way()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty, IccProfiles.Stream(IccProfiles.Header("scnr", "GRAY"), "/N 3 /Alternate /DeviceRGB")));
        var space = (PdfIccBasedColorSpace)document.GetColorSpace(Cos("[/ICCBased 5 0 R]"));

        Assert.Equal(1, space.ComponentCount);
        Assert.Same(PdfDeviceGrayColorSpace.Instance, space.Alternate);
        Assert.Equal(["IccComponentCountMismatch", "ColorSpaceEntryInvalid"], document.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void A_stream_that_is_not_a_profile_uses_the_alternate_with_a_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty, "<< /N 4 /Alternate /DeviceCMYK /Length 3 >>\nstream\nabc\nendstream"));
        var space = (PdfIccBasedColorSpace)document.GetColorSpace(Cos("[/ICCBased 5 0 R]"));

        Assert.Null(space.ProfileHeader);
        Assert.Equal(4, space.ComponentCount);
        Assert.Same(PdfDeviceCmykColorSpace.Instance, space.Alternate);
        Assert.Equal(["IccProfileInvalid"], document.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void A_Lab_profile_takes_the_Lab_ranges_of_Table_68()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty, IccProfiles.Stream(IccProfiles.Header("spac", "Lab ", major: 4, minorAndFix: 0x30), "/N 3")));
        var space = (PdfIccBasedColorSpace)document.GetColorSpace(Cos("[/ICCBased 5 0 R]"));

        Assert.Equal([0.0, 100, -128, 127, -128, 127], space.Range);
        Assert.Equal(new Version(4, 3, 0), space.ProfileHeader!.Version);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_version_5_profile_is_not_supported_and_needs_no_diagnostic()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty, IccProfiles.Stream(IccProfiles.Header("mntr", "RGB ", major: 5, minorAndFix: 0), "/N 3")));
        var space = (PdfIccBasedColorSpace)document.GetColorSpace(Cos("[/ICCBased 5 0 R]"));

        Assert.False(space.ProfileHeader!.IsSupportedForPdf);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Indexed_exposes_its_base_high_value_and_lookup_table()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty, "<< /Length 6 >>\nstream\n\xFF\x00\x00\x00\x00\xFF\nendstream"));
        var space = (PdfIndexedColorSpace)document.GetColorSpace(Cos("[/Indexed /DeviceRGB 1 5 0 R]"));
        var text = (PdfIndexedColorSpace)document.GetColorSpace(Cos("[/Indexed /DeviceGray 2 (\x00\x80\xFF)]"));

        Assert.Same(PdfDeviceRgbColorSpace.Instance, space.Base);
        Assert.Equal(1, space.HighValue);
        Assert.Equal(new byte[] { 255, 0, 0, 0, 0, 255 }, space.GetLookup().ToArray());
        Assert.Equal(new ComponentRange(0, 1), space.GetComponentRange(0));
        Assert.Equal([0.0, 15], space.GetDefaultDecode(4));
        Assert.Equal(new PdfVersion(1, 1), space.MinimumVersion);
        Assert.Equal(new PdfVersion(1, 2), text.MinimumVersion);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Indexed_repairs_its_high_value_and_lookup_length()
    {
        using PdfDocument document = PdfDocument.Create();
        var high = (PdfIndexedColorSpace)document.GetColorSpace(Cos("[/Indexed /DeviceGray 300 <00>]"));

        Assert.Equal(255, high.HighValue);
        Assert.Equal(["ColorSpaceEntryInvalid", "IndexedLookupInvalid"], document.Diagnostics.Select(d => d.Code));
    }

    [Theory]
    [InlineData("[/Indexed /DeviceRGB 1 3]", "IndexedLookupInvalid")]
    [InlineData("[/Indexed /Pattern 0 <00>]", "ColorSpaceInvalid")]
    [InlineData("[/Indexed /DeviceRGB /x <00>]", "ColorSpaceInvalid")]
    [InlineData("[/Unknown 1 2]", "ColorSpaceInvalid")]
    [InlineData("[]", "ColorSpaceInvalid")]
    [InlineData("<< /N 3 >>", "ColorSpaceInvalid")]
    [InlineData("[/DeviceN [] /DeviceGray 0]", "ColorSpaceInvalid")]
    [InlineData("[/Separation (x) /DeviceGray 0]", "ColorSpaceInvalid")]
    public void A_space_that_cannot_be_used_reads_as_device_gray(string syntax, string code)
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Same(PdfDeviceGrayColorSpace.Instance, document.GetColorSpace(Cos(syntax)));
        Assert.Equal(code, document.Diagnostics[0].Code);
    }

    [Fact]
    public void A_space_that_contains_itself_is_a_cycle_and_reads_as_device_gray()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty, "[/Indexed 5 0 R 1 <0000>]"));

        Assert.Same(PdfDeviceGrayColorSpace.Instance, document.GetColorSpace(Five));
        Assert.Equal(["ColorSpaceCycle"], document.Diagnostics.Select(d => d.Code));
        Assert.All(document.Diagnostics, d => Assert.Equal(Five, d.ObjectReference));
    }

    [Fact]
    public void A_device_space_written_as_an_array_is_read_with_a_diagnostic_and_CalCMYK_silently_as_DeviceCMYK()
    {
        using PdfDocument document = PdfDocument.Create();

        Assert.Same(PdfDeviceRgbColorSpace.Instance, document.GetColorSpace(Cos("[/DeviceRGB]")));
        Assert.Same(PdfDeviceCmykColorSpace.Instance, document.GetColorSpace(Cos("[/CalCMYK << /WhitePoint [0.9505 1 1.089] >>]")));
        Assert.Equal(["ColorSpaceEntryInvalid"], document.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Separation_exposes_its_colourant_alternate_and_tint_transform()
    {
        using PdfDocument document = PdfDocument.Create();
        var spot = (PdfSeparationColorSpace)document.GetColorSpace(Cos("[/Separation /Gold /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0.2 1 0] /N 1 >>]"));
        var all = (PdfSeparationColorSpace)document.GetColorSpace(Cos("[/Separation /All /DeviceGray << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>]"));
        var none = (PdfSeparationColorSpace)document.GetColorSpace(Cos("[/Separation /None /DeviceGray << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>]"));

        Assert.Equal("Gold", spot.ColorantName.Value);
        Assert.Same(PdfDeviceCmykColorSpace.Instance, spot.Alternate);
        Assert.Equal(4, spot.TintTransform!.OutputCount);
        Assert.Equal([1f], spot.GetInitialColor().Components.ToArray());
        Assert.False(spot.IsAll || spot.IsNone);
        Assert.True(all.IsAll);
        Assert.True(none.IsNone);
        Assert.Equal(new PdfVersion(1, 2), spot.MinimumVersion);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_separation_whose_tint_transform_is_not_a_function_is_recorded()
    {
        using PdfDocument document = PdfDocument.Create();
        var space = (PdfSeparationColorSpace)document.GetColorSpace(Cos("[/Separation /Spot /DeviceRGB 0]"));

        Assert.Null(space.TintTransform);
        Assert.Equal(["TintTransformInvalid"], document.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void DeviceN_exposes_its_colourants_and_NChannel_attributes()
    {
        using PdfDocument document = PdfDocument.Create();
        var space = (PdfDeviceNColorSpace)document.GetColorSpace(Cos(
            "[/DeviceN [/Cyan /Orange /None] /DeviceCMYK << /FunctionType 4 /Domain [0 1 0 1 0 1] /Range [0 1 0 1 0 1 0 1] >> "
            + "<< /Subtype /NChannel /Colorants << /Orange [/Separation /Orange /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0.6 1 0] /N 1 >>] >> "
            + "/Process << /ColorSpace /DeviceCMYK /Components [/Cyan /Magenta /Yellow /Black] >> "
            + "/MixingHints << /Solidities << /Orange 0.8 /Default 2 >> /PrintingOrder [/Cyan /Orange] /DotGain << /Orange << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >> >> >> >>]"));

        Assert.Equal(["Cyan", "Orange", "None"], space.ColorantNames.Select(n => n.Value));
        Assert.Equal(3, space.ComponentCount);
        Assert.False(space.AreAllNone);
        Assert.True(space.IsNChannel);
        PdfDeviceNAttributes attributes = space.Attributes!;
        Assert.IsType<PdfSeparationColorSpace>(Assert.Single(attributes.Colorants).Value);
        Assert.Same(PdfDeviceCmykColorSpace.Instance, attributes.Process!.ColorSpace);
        Assert.Equal(4, attributes.Process.Components.Count);
        Assert.Equal(1.0, attributes.MixingHints!.Solidities[new CosName("Default")]);
        Assert.Equal(["Cyan", "Orange"], attributes.MixingHints.PrintingOrder.Select(n => n.Value));
        Assert.Single(attributes.MixingHints.DotGain);
        Assert.Equal([1f, 1f, 1f], space.GetInitialColor().Components.ToArray());
        Assert.Equal(new PdfVersion(1, 6), space.MinimumVersion);
        Assert.DoesNotContain(document.Diagnostics, d => d.Code.StartsWith("Color", StringComparison.Ordinal) || d.Code.StartsWith("DeviceN", StringComparison.Ordinal));
    }

    [Fact]
    public void DeviceN_names_that_repeat_or_name_All_are_recorded()
    {
        using PdfDocument document = PdfDocument.Create();
        var space = (PdfDeviceNColorSpace)document.GetColorSpace(Cos("[/DeviceN [/A /A /None /None] /DeviceGray << /FunctionType 4 /Domain [0 1 0 1 0 1 0 1] /Range [0 1] >>]"));

        Assert.Equal(4, space.ComponentCount);
        Assert.Contains("DeviceNColorantsInvalid", document.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Pattern_spaces_with_and_without_an_underlying_space()
    {
        using PdfDocument document = PdfDocument.Create();
        var colored = (PdfPatternColorSpace)document.GetColorSpace(new CosName("Pattern"));
        var uncolored = (PdfPatternColorSpace)document.GetColorSpace(Cos("[/Pattern /DeviceCMYK]"));
        var nested = (PdfPatternColorSpace)document.GetColorSpace(Cos("[/Pattern [/Pattern /DeviceRGB]]"));

        Assert.Null(colored.Underlying);
        Assert.Equal(0, colored.ComponentCount);
        Assert.Empty(colored.GetDefaultDecode(8));
        Assert.Same(PdfDeviceCmykColorSpace.Instance, uncolored.Underlying);
        Assert.Equal(4, uncolored.ComponentCount);
        Assert.Null(nested.Underlying);
        Assert.Equal(["ColorSpaceEntryInvalid"], document.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void One_object_reads_as_one_shared_view_until_it_changes()
    {
        using PdfDocument document = PdfDocument.Open(Page("<< >>", string.Empty, "[/CalGray << /WhitePoint [0.9505 1 1.089] /Gamma 1.5 >>]"));
        PdfColorSpace first = document.GetColorSpace(Five);

        Assert.Same(first, document.GetColorSpace(Five));
        var array = (CosArray)document.Resolve(Five);
        array[0] = new CosName("DeviceRGB");
        array.RemoveAt(1);
        Assert.Same(PdfDeviceRgbColorSpace.Instance, document.GetColorSpace(Five));
    }

    [Fact]
    public void Views_read_their_entries_live()
    {
        using PdfDocument document = PdfDocument.Create();
        var array = (CosArray)Cos("[/CalGray << /WhitePoint [0.9505 1 1.089] /Gamma 1.5 >>]");
        var space = (PdfCalGrayColorSpace)document.GetColorSpace(array);

        ((CosDictionary)array[1])[new CosName("Gamma")] = new CosReal(2.5);
        Assert.Equal(2.5, space.Gamma);
    }
}
