using System.Globalization;
using System.Runtime.CompilerServices;

namespace Broadside.Benchmarks;

/// <summary>
/// Moves where a measuring process's heap data and compiled code land, by a random amount per process, when
/// <c>BROADSIDE_BENCH_LAYOUT=random</c> (the regression gate's timing runs, issue #554). Some kernels run up to 1.37x slower or
/// faster with the placement of their arrays relative to their compiled code (on Intel processors, GrayToRgb at 101 or 138 ns),
/// and that placement is fixed by everything a build compiles and allocates before the benchmark, so two builds of identical
/// code could differ by 1.3x on every run. With the placement drawn per process, that difference becomes noise across
/// processes, which the gate's rerun of several processes per commit absorbs.
/// <c>BROADSIDE_BENCH_LAYOUT=heap:&lt;n&gt;,code:&lt;n&gt;</c> reproduces one placement.
/// </summary>
internal static class LayoutJitter
{
    /// <summary>The environment variable that turns it on.</summary>
    public const string Variable = "BROADSIDE_BENCH_LAYOUT";

    private const int MaximumHeapWords = 512; // 8-byte words: 0 to 4 KB, a page
    private const int MaximumCodeMethods = 64;

    private static byte[]? _heapPad;

    /// <summary>Applies the placement the environment asks for; returns a description of it, or null when there is none.</summary>
    public static string? Apply()
    {
        string? setting = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrEmpty(setting))
        {
            return null;
        }

        int heap;
        int code;
        if (setting == "random")
        {
#pragma warning disable CA5394 // a measurement placement, not security
            heap = Random.Shared.Next(MaximumHeapWords);
            code = Random.Shared.Next(MaximumCodeMethods);
#pragma warning restore CA5394
        }
        else if (!TryParse(setting, out heap, out code))
        {
            throw new InvalidOperationException($"{Variable} takes 'random' or 'heap:<n>,code:<n>', not '{setting}'.");
        }

        // A live array shifts every later allocation of this thread by its size; methods compiled now shift every later method.
        _heapPad = new byte[heap * 8];
        int compiled = Grow<Level>(code);
        return string.Create(CultureInfo.InvariantCulture, $"heap:{heap},code:{compiled} ({_heapPad.Length} B, {compiled} methods)");
    }

    private static bool TryParse(string setting, out int heap, out int code)
    {
        heap = code = -1;
        foreach (string part in setting.Split(','))
        {
            string[] pair = part.Split(':');
            if (pair.Length != 2 || !int.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                return false;
            }

            switch (pair[0])
            {
                case "heap":
                    heap = value;
                    break;
                case "code":
                    code = value;
                    break;
                default:
                    return false;
            }
        }

        return heap >= 0 && code >= 0;
    }

    // Each call compiles a new instantiation (Level, Level<Level>, ...): value-type instantiations never share code.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Grow<T>(int remaining)
        where T : struct => remaining == 0 ? 0 : 1 + Grow<Level<T>>(remaining - 1);

    private struct Level;

    private struct Level<T>;
}
