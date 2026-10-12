using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Parameters;
using BenchmarkDotNet.Running;

namespace Broadside.Benchmarks;

/// <summary>
/// The regression gate's allocation figures (<c>bench/baselines/gate/README.md</c>):
/// <c>--gate-allocations &lt;output.json&gt; --filter &lt;glob&gt;...</c> runs every matching benchmark case in this process, the way
/// BenchmarkDotNet would set it up (parameters, <c>[GlobalSetup]</c>, <c>[GlobalCleanup]</c>), warms it up, and records the fewest
/// bytes the current thread allocated in one call of the benchmark method over several calls. That is the steady-state cost of a
/// call, exactly and repeatably: BenchmarkDotNet's own <c>Allocated</c> divides one iteration's allocations, all threads included,
/// by an operation count its pilot chooses from timing, so a one-off cost per iteration (a cache rebuilt after its forced
/// collection, a background thread) shifts it by a varying amount, and the gate needs a figure that does not move.
/// This is the same measurement as <c>Broadside.TestSupport.Allocations.Measure</c> (issue #48), per benchmark case.
/// </summary>
internal static class GateAllocations
{
    /// <summary>The command-line switch that selects this mode.</summary>
    public const string Option = "--gate-allocations";

    private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const int WarmUpCalls = 30;
    private const int MeasuredCalls = 10;
    private static readonly TimeSpan WarmUpBudget = TimeSpan.FromMilliseconds(500);

    /// <summary>Runs the mode; <paramref name="args"/> follow <see cref="Option"/>. Returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine($"usage: {Option} <output.json> [--filter <glob>...]");
            return 2;
        }

        string output = args[0];
        string[] patterns = args.Length > 2 && args[1] == "--filter" ? args[2..] : ["*"];
        var filter = new GlobFilter(patterns);

        var results = new SortedDictionary<string, long>(StringComparer.Ordinal);
        int failures = 0;
        foreach (Type type in typeof(GateAllocations).Assembly.GetTypes()
            .Where(static type => type.IsPublic && !type.IsAbstract && type.GetMethods().Any(static method => method.GetCustomAttribute<BenchmarkAttribute>() is not null))
            .OrderBy(static type => type.FullName, StringComparer.Ordinal))
        {
            foreach (BenchmarkCase benchmark in BenchmarkConverter.TypeToBenchmarks(type).BenchmarksCases.Where(filter.Predicate))
            {
                string name = FullNameProvider.GetBenchmarkName(benchmark);
                try
                {
                    results[name] = Measure(benchmark);
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{results[name],12} B  {name}"));
                }
                catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or NotSupportedException)
                {
                    failures++;
                    Console.Error.WriteLine($"{name}: {(ex as TargetInvocationException)?.InnerException ?? ex}");
                }
            }
        }

        var benchmarks = new JsonObject();
        foreach ((string name, long bytes) in results)
        {
            benchmarks[name] = bytes;
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        File.WriteAllText(output, new JsonObject { ["Unit"] = "bytes per call", ["Benchmarks"] = benchmarks }.ToJsonString(options));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{results.Count} case(s) measured, {failures} failed; written to {output}."));
        return failures == 0 ? 0 : 1;
    }

    private static long Measure(BenchmarkCase benchmark)
    {
        Descriptor descriptor = benchmark.Descriptor;
        MethodInfo workload = descriptor.WorkloadMethod;
        if (workload.GetParameters().Length > 0)
        {
            throw new NotSupportedException("benchmarks with [Arguments] are not supported by the allocation gate yet");
        }

        object instance = Activator.CreateInstance(descriptor.Type)
            ?? throw new InvalidOperationException($"cannot create {descriptor.Type}");
        foreach (ParameterInstance parameter in benchmark.Parameters.Items)
        {
            Set(descriptor.Type, parameter.IsStatic ? null : instance, parameter.Name, parameter.Value);
        }

        descriptor.GlobalSetupMethod?.Invoke(instance, null);
        try
        {
            Type returns = workload.ReturnType;
            MethodInfo measure = returns == typeof(void)
                ? typeof(GateAllocations).GetMethod(nameof(MeasureAction), BindingFlags.NonPublic | BindingFlags.Static)!
                : typeof(GateAllocations).GetMethod(nameof(MeasureFunc), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(returns);
            Type delegateType = returns == typeof(void) ? typeof(Action) : typeof(Func<>).MakeGenericType(returns);
            Delegate call = workload.CreateDelegate(delegateType, instance);
            return (long)measure.Invoke(null, [call])!;
        }
        finally
        {
            descriptor.GlobalCleanupMethod?.Invoke(instance, null);
        }
    }

    private static long MeasureAction(Action call) => MeasureCore(call, static action => action());

    private static long MeasureFunc<T>(Func<T> call) => MeasureCore(call, static func => Sink<T>.Value = func());

    /// <summary>
    /// Warm-up (at least 30 calls, then until half a second has passed), then the fewest bytes over 10 single calls during which
    /// no garbage collection ran. A collection inside a call can clear weakly held caches (<c>ConditionalWeakTable</c>) halfway
    /// through it, so such a call is not counted; the gate runs this mode with a large generation-0 budget
    /// (<c>DOTNET_GCgen0size</c>) so that most calls see none. When every attempt saw one, the fewest bytes of all attempts count.
    /// </summary>
    private static long MeasureCore<TCall>(TCall call, Action<TCall> invoke)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < WarmUpCalls || clock.Elapsed < WarmUpBudget; i++)
        {
            invoke(call);
            if (i > 100_000)
            {
                break;
            }
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long fewest = long.MaxValue;
        long fewestWithCollection = long.MaxValue;
        int clean = 0;
        for (int attempt = 0; attempt < MeasuredCalls * 5 && clean < MeasuredCalls && fewest > 0; attempt++)
        {
            int collections = GC.CollectionCount(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            invoke(call);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (GC.CollectionCount(0) == collections)
            {
                clean++;
                fewest = Math.Min(fewest, bytes);
            }
            else
            {
                fewestWithCollection = Math.Min(fewestWithCollection, bytes);
            }
        }

        return clean > 0 ? fewest : fewestWithCollection;
    }

    private static void Set(Type type, object? target, string name, object? value)
    {
        if (type.GetProperty(name, MemberFlags) is { } property)
        {
            property.SetValue(target, value);
        }
        else if (type.GetField(name, MemberFlags) is { } field)
        {
            field.SetValue(target, value);
        }
        else
        {
            throw new InvalidOperationException($"{type.Name} has no parameter member {name}");
        }
    }

    /// <summary>Keeps a benchmark's result alive without boxing it, as BenchmarkDotNet's consumer does.</summary>
    private static class Sink<T>
    {
        public static T? Value;
    }
}
