using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Broadside.Rendering.Tests.Hosting;

/// <summary>
/// Issue #239 (spec #33 user stories 43 to 45): the engine a container hands out renders, saves and logs exactly as the static entry
/// point does. Every hand-written corpus file is opened through both and compared page by page and byte by byte. ISO 32000-2 §7.5
/// (saving) and §8 (rendering).
/// </summary>
[Collection(HeavyTestCollection.Name)]
public sealed class HostedEngineParityTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

#pragma warning disable CA2000 // The documents are disposed by the using declarations that receive them.
    [Fact]
    public void Every_corpus_page_renders_to_identical_bitmaps_and_every_unchanged_save_gives_identical_bytes_through_both_engines()
    {
        using ServiceProvider services = new ServiceCollection().AddBroadside(static options => options.UseTimeProvider(new FixedClock(Now))).BuildServiceProvider();
        PdfEngine hosted = services.GetRequiredService<PdfEngine>();
        PdfOptions staticOptions = new PdfOptions().UseTimeProvider(new FixedClock(Now));
        var failures = new List<string>();
        int rendered = 0;
        int saved = 0;
        using var rasterizer = new SoftwareRasterizer();
        RenderOptions render = new RenderOptions().WithScale(0.25);

        foreach (string path in Directory.EnumerateFiles(Corpus.Directory, "*.pdf").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            byte[] file = File.ReadAllBytes(path);
            using PdfDocument? fromStatic = TryOpen(() => PdfDocument.Open(file, staticOptions), out string? staticOpen);
            using PdfDocument? fromHosted = TryOpen(() => hosted.Open(file), out string? hostedOpen);
            if (staticOpen != hostedOpen)
            {
                failures.Add($"{name}: open ended {staticOpen ?? "ok"} through the static entry point and {hostedOpen ?? "ok"} through the container");
                continue;
            }

            if (fromStatic is null || fromHosted is null)
            {
                continue;
            }

            string? pages = Outcome(() => fromStatic.Pages.Count, out int staticPages);
            string? hostedPages = Outcome(() => fromHosted.Pages.Count, out int hostedPageCount);
            if (pages != hostedPages || staticPages != hostedPageCount)
            {
                failures.Add($"{name}: {staticPages} pages ({pages ?? "ok"}) against {hostedPageCount} ({hostedPages ?? "ok"})");
                continue;
            }

            for (int index = 0; index < staticPages; index++)
            {
                string? left = RenderPage(rasterizer, fromStatic, index, render, out byte[]? leftPixels, out int leftWidth);
                string? right = RenderPage(rasterizer, fromHosted, index, render, out byte[]? rightPixels, out int rightWidth);
                if (left != right || leftWidth != rightWidth || !(leftPixels ?? []).AsSpan().SequenceEqual(rightPixels ?? []))
                {
                    failures.Add($"{name} page {index + 1}: the bitmaps differ ({left ?? "ok"} against {right ?? "ok"})");
                }
                else if (leftPixels is not null)
                {
                    rendered++;
                }
            }

            string? savedStatic = Save(fromStatic, out byte[]? staticBytes);
            string? savedHosted = Save(fromHosted, out byte[]? hostedBytes);

            // Encryption draws fresh random salts and vectors on every save, so an encrypted file is compared by outcome only.
            bool identical = fromStatic.IsEncrypted || (staticBytes ?? []).AsSpan().SequenceEqual(hostedBytes ?? []);
            if (savedStatic != savedHosted || !identical)
            {
                failures.Add($"{name}: the saved bytes differ ({savedStatic ?? "ok"} against {savedHosted ?? "ok"})");
            }
            else if (staticBytes is not null)
            {
                saved++;
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(rendered > 100, $"Only {rendered} pages rendered: the corpus was not found.");
        Assert.True(saved > 100, $"Only {saved} files saved: the corpus was not found.");
    }

#pragma warning restore CA2000

    [Fact]
    public void Per_call_save_and_render_options_apply_alike_to_a_document_from_either_engine()
    {
        using ServiceProvider services = new ServiceCollection().AddBroadside(static options => options.UseTimeProvider(new FixedClock(Now))).BuildServiceProvider();
        byte[] file = Corpus.Bytes("text-standard14.pdf");
        using PdfDocument fromStatic = PdfDocument.Open(file, new PdfOptions().UseTimeProvider(new FixedClock(Now)));
        using PdfDocument fromHosted = services.GetRequiredService<PdfEngine>().Open(file);
        PdfSaveOptions save = new PdfSaveOptions().WithStreamCompression(PdfStreamCompression.None).WithObjectNumbering(PdfObjectNumbering.Automatic).WithRemoveUnreachableObjects(true);
        RenderOptions render = new RenderOptions().WithDpi(48).WithTransparentBackground(true).WithAntiAliasing(AntiAliasing.None);
        using var rasterizer = new SoftwareRasterizer();

        using var staticStream = new MemoryStream();
        using var hostedStream = new MemoryStream();
        fromStatic.Save(staticStream, save);
        fromHosted.Save(hostedStream, save);
        using Bitmap staticBitmap = rasterizer.Render(fromStatic.Pages[0], render);
        using Bitmap hostedBitmap = rasterizer.Render(fromHosted.Pages[0], render);

        Assert.Equal(staticStream.ToArray(), hostedStream.ToArray());
        Assert.Equal(staticBitmap.Pixels.ToArray(), hostedBitmap.Pixels.ToArray());
    }

    [Fact]
    public void A_diagnostic_raised_while_rendering_reaches_the_hosts_logger_with_its_code_severity_and_object_reference()
    {
        using var logs = new CapturingLoggerProvider();
        using ServiceProvider services = new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(logs))
            .AddBroadside()
            .BuildServiceProvider();
        using PdfDocument document = services.GetRequiredService<PdfEngine>().Open(Corpus.Bytes("acroform-fields.pdf"));
        PdfPage page = document.Pages[0];
        int recordedBeforeRender = document.Diagnostics.Count;
        int loggedBeforeRender = logs.Entries.Count;

        using var rasterizer = new SoftwareRasterizer();
        using Bitmap bitmap = rasterizer.Render(page, new RenderOptions().WithScale(0.25));

        Diagnostic[] raised = [.. document.Diagnostics.Skip(recordedBeforeRender)];
        LogEntry[] logged = [.. logs.Entries.Skip(loggedBeforeRender)];
        Diagnostic missing = Assert.Single(raised, static candidate => candidate.Code == "AppearanceResourcesMissing");
        Assert.Equal(raised.Length, logged.Length);
        LogEntry entry = Assert.Single(logged, static candidate => (string?)candidate.State["Code"] == "AppearanceResourcesMissing");
        Assert.Equal("Broadside.PdfDocument", entry.Category);
        Assert.Equal(new EventId(1, "Diagnostic"), entry.EventId);
        Assert.Equal(DiagnosticSeverity.Warning, missing.Severity);
        Assert.Equal(DiagnosticSeverity.Warning, entry.State["Severity"]);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.NotNull(missing.ObjectReference);
        Assert.Equal(missing.ObjectReference, Assert.IsType<CosReference>(entry.State["ObjectReference"]));
        Assert.Equal(missing.Message, entry.State["Message"]);
    }

    private static PdfDocument? TryOpen(Func<PdfDocument> open, out string? failure)
    {
        try
        {
            failure = null;
            return open();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failure = exception.GetType().Name;
            return null;
        }
    }

    private static string? Outcome(Func<int> read, out int value)
    {
        try
        {
            value = read();
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            value = -1;
            return exception.GetType().Name;
        }
    }

    private static string? RenderPage(SoftwareRasterizer rasterizer, PdfDocument document, int index, RenderOptions options, out byte[]? pixels, out int width)
    {
        try
        {
            using Bitmap bitmap = rasterizer.Render(document.Pages[index], options);
            pixels = bitmap.Pixels.ToArray();
            width = bitmap.Width;
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            pixels = null;
            width = -1;
            return exception.GetType().Name;
        }
    }

    private static string? Save(PdfDocument document, out byte[]? bytes)
    {
        try
        {
            using var stream = new MemoryStream();
            document.Save(stream);
            bytes = stream.ToArray();
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            bytes = null;
            return exception.GetType().Name;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
