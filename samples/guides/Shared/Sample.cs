namespace Broadside.Samples;

/// <summary>What every guide sample shares: it runs in its output folder and fails loudly when a result is not what the guide says.</summary>
internal static class Sample
{
    /// <summary>The variable naming the folder that <see cref="Finish"/> copies the PDF files a sample wrote into (run-samples.sh sets it).</summary>
    private const string OutputVariable = "BROADSIDE_SAMPLES_OUTPUT";

    private static DateTime _started;

    /// <summary>Makes the output folder, where the sample's input files are copied, the current directory.</summary>
    public static void Start(string name)
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        _started = DateTime.UtcNow.AddSeconds(-1);
        Console.WriteLine($"== {name}");
    }

    /// <summary>Throws when <paramref name="condition"/> is false, so the sample exits with an error.</summary>
    public static void Expect(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Sample check failed: {what}");
        }
    }

    /// <summary>
    /// Reports success and, when <c>BROADSIDE_SAMPLES_OUTPUT</c> names a folder, copies every PDF file the sample wrote into a subfolder
    /// named after it, where run-samples.sh checks them with qpdf (and pdfsig or veraPDF where it can).
    /// </summary>
    public static void Finish()
    {
        string? output = Environment.GetEnvironmentVariable(OutputVariable);
        if (!string.IsNullOrEmpty(output))
        {
            string folder = Path.Combine(output, AppDomain.CurrentDomain.FriendlyName);
            Directory.CreateDirectory(folder);
            foreach (string file in Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.pdf"))
            {
                if (File.GetLastWriteTimeUtc(file) >= _started)
                {
                    File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
                }
            }
        }

        Console.WriteLine("OK");
    }
}
