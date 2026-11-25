namespace Broadside.Samples;

/// <summary>What every guide sample shares: it runs in its output folder and fails loudly when a result is not what the guide says.</summary>
internal static class Sample
{
    /// <summary>Makes the output folder, where the sample's input files are copied, the current directory.</summary>
    public static void Start(string name)
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
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
}
