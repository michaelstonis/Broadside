using System.Diagnostics;
using System.Xml.Linq;

namespace Broadside.Tests.Writing;

/// <summary>
/// Validates a written file with veraPDF, the PDF Association's reference PDF/A validator, as an independent check of PDF/A output
/// (issue #201). veraPDF is a Java command-line tool, used by the tests only, never by the library (ADR 0001).
/// </summary>
/// <remarks>
/// The tool is found as <c>verapdf</c> on the PATH, or at the path in <c>BROADSIDE_VERAPDF</c>. Locally a test is skipped when it is
/// missing; CI installs a pinned, checksum-verified release and sets <c>BROADSIDE_REQUIRE_VERAPDF=1</c>, which turns the skip into a
/// failure so the check can never silently stop running there (<c>.github/workflows/ci.yml</c>, job <c>verapdf</c>).
/// </remarks>
internal static class VeraPdf
{
    /// <summary>Validates <paramref name="file"/> against a PDF/A flavour and returns the rules it fails.</summary>
    /// <param name="file">The file's bytes.</param>
    /// <param name="flavour">The veraPDF flavour: <c>2b</c>, <c>2u</c>, <c>3b</c>, <c>4</c>, ...</param>
    /// <returns>Each failed rule as <c>clause-test: description (count)</c>; empty when the file is compliant.</returns>
    public static IReadOnlyList<string> FailedRules(byte[] file, string flavour)
    {
        string executable = Find();
        string path = Path.Combine(Path.GetTempPath(), $"broadside-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, file);
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in new[] { "--flavour", flavour, "--format", "xml", path })
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start)!;
            Task<string> error = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            int xml = output.IndexOf('<', StringComparison.Ordinal);
            Assert.True(xml >= 0, $"veraPDF wrote no report (exit code {process.ExitCode}): {error.Result}");
            XDocument report = XDocument.Parse(output[xml..]);
            XElement? details = report.Descendants("details").FirstOrDefault();
            Assert.True(details is not null, $"veraPDF did not validate the file (exit code {process.ExitCode}): {output}{error.Result}");
            return [.. details.Elements("rule")
                .Where(rule => (string?)rule.Attribute("status") == "failed")
                .Select(rule => $"{(string?)rule.Attribute("clause")}-{(string?)rule.Attribute("testNumber")}: {((string?)rule.Element("description"))?.Trim()} ({(string?)rule.Attribute("failedChecks")})")];
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Find()
    {
        string? configured = Environment.GetEnvironmentVariable("BROADSIDE_VERAPDF");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
        {
            return configured;
        }

        string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        string[] names = OperatingSystem.IsWindows() ? ["verapdf.bat", "verapdf"] : ["verapdf"];
        string? found = directories.SelectMany(directory => names.Select(name => Path.Combine(directory, name))).FirstOrDefault(File.Exists);
        if (found is not null)
        {
            return found;
        }

        const string missing = "veraPDF is not on the PATH (or at BROADSIDE_VERAPDF); install it to run the PDF/A checks (CI installs a pinned release).";
        if (Environment.GetEnvironmentVariable("BROADSIDE_REQUIRE_VERAPDF") == "1")
        {
            Assert.Fail(missing);
        }

        Assert.Skip(missing);
        return null!;
    }
}
