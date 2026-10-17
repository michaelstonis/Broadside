using Broadside.Diagnostics;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Rendering.Icc;
using Broadside.Security;
using Broadside.TestSupport;

namespace Broadside.Rendering.Tests.Icc;

/// <summary>
/// Every ICC profile embedded in the fetched pdf.js and PDFBox corpora (tools/CorpusFetcher) is read by the engine or recorded as
/// unusable, and none makes reading or converting throw (ISO 32000-2 §8.6.5.5; ADR 0005). Skipped when the corpus is not fetched.
/// </summary>
public sealed class RealWorldIccTests(ITestOutputHelper output)
{
    private static readonly CosName DestOutputProfile = new("DestOutputProfile");
    private static readonly CosName Size = new("Size");
    private static readonly CosName IccBased = new("ICCBased");

    public static TheoryData<string> Corpora => new() { "pdfjs", "pdfbox" };

    [Theory]
    [MemberData(nameof(Corpora))]
    public void Every_embedded_profile_converts_or_records_a_diagnostic(string corpus)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpus);
        if (files.Count == 0)
        {
            Assert.Skip($"{corpus} not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var models = new Dictionary<string, int>(StringComparer.Ordinal);
        var refused = new List<string>();
        foreach (string file in files)
        {
            Sweep(file, models, refused);
        }

        foreach ((string model, int count) in models.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"{model}: {count}");
        }

        refused.ForEach(output.WriteLine);
        Assert.NotEmpty(models);
    }

    /// <summary>The corpora whose output intents carry CMYK printing profiles: the GWG suite (PDF/X-4) and the PDF/A test files.</summary>
    public static TheoryData<string> OutputIntentCorpora => new() { "gwg-output-suite", "verapdf-corpus" };

    /// <summary>
    /// Every CMYK output intent profile (ISO 32000-2 §14.11.5, <c>DestOutputProfile</c>) of a corpus is a lookup-table profile the
    /// engine converts without recording a diagnostic: in practice lut16Type or lutAToBType press characterisations such as FOGRA39
    /// or GRACoL (ICC.1:2022 §10.10, §10.12).
    /// </summary>
    [Theory]
    [MemberData(nameof(OutputIntentCorpora))]
    public void Every_CMYK_output_intent_profile_converts_without_diagnostics(string corpus)
    {
        IReadOnlyList<string> files = RealWorldCorpus.Files(corpus);
        if (files.Count == 0)
        {
            Assert.Skip($"{corpus} not fetched; run tools/CorpusFetcher (or set {RealWorldCorpus.EnvironmentVariable}).");
        }

        var converted = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            SweepOutputIntents(file, converted);
        }

        converted.ToList().ForEach(output.WriteLine);
        Assert.NotEmpty(converted);
    }

    /// <summary>Converts a colour through every CMYK output intent profile of one file.</summary>
    private static void SweepOutputIntents(string file, SortedSet<string> converted)
    {
        using PdfDocument? document = TryOpen(file);
        if (document is null)
        {
            return;
        }

        foreach (int number in OutputIntentProfiles(document))
        {
            if (document.GetColorSpace(new CosArray { IccBased, new CosReference(number, 0) }) is not PdfIccBasedColorSpace { ComponentCount: 4 } space
                || !IccProfile.TryParse(space.GetProfileData().Span, out IccProfile? profile, out _)
                || profile.Header.DataColorSpace != "CMYK")
            {
                continue;
            }

            int before = document.Diagnostics.Count;
            float[] rgb = new float[3];
            document.GetColorConverter(space).Convert([0.2f, 0.4f, 0.6f, 0.1f], rgb);

            Assert.Equal(IccProfileModel.LookupTable, profile.Model);
            Assert.All(rgb, value => Assert.InRange(value, 0f, 1f));
            Assert.True(document.Diagnostics.Count == before, $"{file} object {number}: {string.Join("; ", document.Diagnostics.Skip(before).Select(static d => d.Message))}");
            converted.Add(Description(profile, space));
        }
    }

    /// <summary>A profile's description for the test output: the AToB0 type and the profile size, which tell the presses apart.</summary>
    private static string Description(IccProfile profile, PdfIccBasedColorSpace space) =>
        $"v{profile.Header.Version} {profile.Header.DeviceClass} {profile.Header.ConnectionSpace.Trim()} {space.GetProfileData().Length} bytes";

    /// <summary>The object numbers of the catalog's output intents' <c>DestOutputProfile</c> streams (ISO 32000-2 §14.11.5, Table 401).</summary>
    private static SortedSet<int> OutputIntentProfiles(PdfDocument document)
    {
        var found = new SortedSet<int>();
        if (document.Trailer.TryGetValue(new CosName("Root"), out CosObject? root)
            && document.Resolve(root) is CosDictionary catalog
            && catalog.TryGetValue(new CosName("OutputIntents"), out CosObject? intents)
            && document.Resolve(intents) is CosArray array)
        {
            foreach (CosObject intent in array)
            {
                if (document.Resolve(intent) is CosDictionary dictionary
                    && dictionary.TryGetValue(DestOutputProfile, out CosObject? profile)
                    && profile is CosReference reference)
                {
                    found.Add(reference.ObjectNumber);
                }
            }
        }

        return found;
    }

    /// <summary>Reads and converts every profile stream of one file.</summary>
    private static void Sweep(string file, Dictionary<string, int> models, List<string> refused)
    {
        using PdfDocument? document = TryOpen(file);
        if (document is null)
        {
            return;
        }

        foreach (int number in ProfileStreams(document))
        {
            if (document.GetColorSpace(new CosArray { IccBased, new CosReference(number, 0) }) is not PdfIccBasedColorSpace space)
            {
                continue;
            }

            bool read = IccProfile.TryParse(space.GetProfileData().Span, out IccProfile? profile, out string? problem);
            float[] color = [.. Enumerable.Repeat(0.5f, space.ComponentCount)];
            float[] rgb = new float[3];
            document.GetColorConverter(space).Convert(color, rgb);
            Assert.All(rgb, value => Assert.InRange(value, 0f, 1f));

            string key = read ? $"{profile!.Header.DataColorSpace.Trim()} {profile.Model}" : "unreadable";
            models[key] = models.GetValueOrDefault(key) + 1;
            if (!read)
            {
                refused.Add($"{file} {number}: {problem}");
                Assert.True(RecordsUnusableProfile(file, number), $"{file} object {number} ({problem}) converted without IccProfileInvalid.");
            }
        }
    }

    /// <summary>Whether converting the profile alone, in a fresh document, records <c>IccProfileInvalid</c>.</summary>
    private static bool RecordsUnusableProfile(string file, int number)
    {
        using PdfDocument document = TryOpen(file)!;
        PdfColorSpace space = document.GetColorSpace(new CosArray { IccBased, new CosReference(number, 0) });
        _ = document.GetColorConverter(space);
        return document.Diagnostics.Any(static diagnostic => diagnostic.Code == "IccProfileInvalid");
    }

    /// <summary>
    /// The object numbers of the profile streams: the second element of every <c>[/ICCBased stream]</c> array and every output
    /// intent's <c>DestOutputProfile</c>, found in the direct objects of every indirect object.
    /// </summary>
    private static SortedSet<int> ProfileStreams(PdfDocument document)
    {
        var found = new SortedSet<int>();
        long size = document.Trailer.TryGetValue(Size, out CosObject? entry) && entry is CosInteger integer ? integer.Value : 0;
        for (int number = 1; number < Math.Min(size, DocumentWalker.MaxObjectNumber + 1L); number++)
        {
            Collect(document.Resolve(new CosReference(number, 0)), found, 0);
        }

        return found;
    }

    private static void Collect(CosObject value, SortedSet<int> found, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        switch (value)
        {
            case CosArray { Count: 2 } array when array[0] is CosName name && name.Equals(IccBased) && array[1] is CosReference profile:
                found.Add(profile.ObjectNumber);
                break;
            case CosArray array:
                foreach (CosObject item in array)
                {
                    Collect(item, found, depth + 1);
                }

                break;
            case CosStream stream:
                Collect(stream.Dictionary, found, depth + 1);
                break;
            case CosDictionary dictionary:
                foreach ((CosName key, CosObject item) in dictionary)
                {
                    if (key.Equals(DestOutputProfile) && item is CosReference reference)
                    {
                        found.Add(reference.ObjectNumber);
                    }

                    Collect(item, found, depth + 1);
                }

                break;
        }
    }

    private static PdfDocument? TryOpen(string file)
    {
        try
        {
            return PdfDocument.Open(RealWorldCorpus.PathOf(file), new PdfOptions().UseIccColorManagement());
        }
        catch (Exception exception) when (exception is DiagnosticException or PdfPasswordException or PdfCertificateException or PdfEncryptionNotSupportedException)
        {
            return null;
        }
    }
}
