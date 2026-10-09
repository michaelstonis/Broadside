using System.Diagnostics;
using System.Globalization;
using Broadside.TestSupport;
using SharpFuzz;

namespace Broadside.Fuzz;

/// <summary>
/// Runs a target without any external fuzzer: first over every file in <c>tests/Corpus/</c>, then for the requested time
/// over random mutations of those files (bit flips, byte overwrites, truncation, insertion, deletion, chunk copies).
/// Any exception that escapes the target ends the run; the input is saved under <c>artifacts/fuzz/</c> so it can be
/// replayed with <c>--run</c>. The mutator is seeded, and the seed is printed, so a failing run is reproducible.
/// </summary>
internal static class SmokeRunner
{
    private const int MaxMutationsPerInput = 4;
    private const int MaxInsertLength = 16;

    public static int Run(string targetName, ReadOnlySpanAction target, TimeSpan duration, int seed)
    {
        string[] files = CorpusLocator.CorpusFiles();
        byte[][] seeds = new byte[files.Length][];
        for (int i = 0; i < files.Length; i++)
        {
            seeds[i] = File.ReadAllBytes(files[i]);
        }

        Log($"smoke: target '{targetName}', {seeds.Length} corpus files, {duration.TotalSeconds:0.#} s, seed {seed}");

        for (int i = 0; i < seeds.Length; i++)
        {
            if (!TryExecute(target, seeds[i], out Exception? failure))
            {
                return ReportFailure(targetName, seeds[i], failure, $"corpus file {Path.GetFileName(files[i])}", seed, iteration: 0);
            }
        }

        Log($"smoke: all {seeds.Length} corpus files passed; mutating");

        var random = new Random(seed);
        var stopwatch = Stopwatch.StartNew();
        long iterations = 0;
        while (stopwatch.Elapsed < duration)
        {
            int seedIndex = random.Next(seeds.Length);
            byte[] input = Mutate(seeds[seedIndex], random);
            iterations++;

            if (!TryExecute(target, input, out Exception? failure))
            {
                return ReportFailure(targetName, input, failure, $"mutation of {Path.GetFileName(files[seedIndex])}", seed, iterations);
            }
        }

        double perSecond = iterations / Math.Max(stopwatch.Elapsed.TotalSeconds, double.Epsilon);
        Log($"smoke: {iterations} mutated inputs in {stopwatch.Elapsed.TotalSeconds:0.#} s ({perSecond:0} / s), no failures");
        return 0;
    }

    private static bool TryExecute(ReadOnlySpanAction target, byte[] input, out Exception? failure)
    {
        try
        {
            target(input);
            failure = null;
            return true;
        }
        catch (Exception exception)
        {
            failure = exception;
            return false;
        }
    }

    private static int ReportFailure(string targetName, byte[] input, Exception? failure, string origin, int seed, long iteration)
    {
        string directory = Path.Combine(CorpusLocator.RepositoryRoot, "artifacts", "fuzz", targetName);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"smoke-seed{seed}-iter{iteration}.bin"));
        File.WriteAllBytes(path, input);

        Log($"smoke: FAILED on {origin} (seed {seed}, iteration {iteration}); input saved to {path}");
        Log($"smoke: replay with: dotnet run -c Release --project tests/Broadside.Fuzz -- --run {targetName} \"{path}\"");
        Console.WriteLine(failure);
        return 1;
    }

    private static byte[] Mutate(byte[] source, Random random)
    {
        byte[] input = source;
        int count = random.Next(1, MaxMutationsPerInput + 1);
        for (int i = 0; i < count; i++)
        {
            input = MutateOnce(input, random);
        }

        return input;
    }

    private static byte[] MutateOnce(byte[] input, Random random)
    {
        if (input.Length == 0)
        {
            return Insert(input, random);
        }

        return random.Next(6) switch
        {
            0 => FlipBit(input, random),
            1 => OverwriteByte(input, random),
            2 => Truncate(input, random),
            3 => Insert(input, random),
            4 => Delete(input, random),
            _ => CopyChunk(input, random),
        };
    }

    private static byte[] FlipBit(byte[] input, Random random)
    {
        byte[] result = (byte[])input.Clone();
        int index = random.Next(result.Length);
        result[index] ^= (byte)(1 << random.Next(8));
        return result;
    }

    private static byte[] OverwriteByte(byte[] input, Random random)
    {
        byte[] result = (byte[])input.Clone();
        result[random.Next(result.Length)] = (byte)random.Next(256);
        return result;
    }

    private static byte[] Truncate(byte[] input, Random random) => input[..random.Next(input.Length)];

    private static byte[] Insert(byte[] input, Random random)
    {
        int position = random.Next(input.Length + 1);
        byte[] insertion = new byte[random.Next(1, MaxInsertLength + 1)];
        random.NextBytes(insertion);
        return [.. input[..position], .. insertion, .. input[position..]];
    }

    private static byte[] Delete(byte[] input, Random random)
    {
        int start = random.Next(input.Length);
        int length = random.Next(1, Math.Min(MaxInsertLength, input.Length - start) + 1);
        return [.. input[..start], .. input[(start + length)..]];
    }

    private static byte[] CopyChunk(byte[] input, Random random)
    {
        byte[] result = (byte[])input.Clone();
        int length = random.Next(1, Math.Min(MaxInsertLength, result.Length) + 1);
        int from = random.Next(result.Length - length + 1);
        int to = random.Next(result.Length - length + 1);
        Array.Copy(input, from, result, to, length);
        return result;
    }

    private static void Log(FormattableString message) => Console.WriteLine(message.ToString(CultureInfo.InvariantCulture));
}
