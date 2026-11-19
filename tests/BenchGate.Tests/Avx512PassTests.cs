namespace Broadside.Tools.BenchGate.Tests;

/// <summary>
/// Issue #539: every job records its processor and AVX-512 state, and the AVX-512-off pass is compared on its own, so a
/// register-pressure regression that only a processor without AVX-512 shows fails the gate whatever runner the default pass got.
/// </summary>
public sealed class Avx512PassTests
{
    private static readonly MachineInfo Xeon = new("INTEL XEON PLATINUM 8573C", true, true, false, "Ubuntu 24.04", "X64");

    private static readonly MachineInfo XeonOff = Xeon with { Avx512Runtime = false, Avx512Disabled = true };

    private static readonly MachineInfo Epyc = new("AMD EPYC 7763 64-Core Processor", false, false, false, "Ubuntu 24.04", "X64");

    [Fact]
    public void A_slowdown_only_without_AVX512_fails_and_names_the_pass()
    {
        // #335: ResolvePieces spilled to the stack only with 16 vector registers; with AVX-512 nothing changed.
        using ResultsTree tree = SameOnDefault();
        tree.AddMachine("off-0", XeonOff)
            .Add("off-0/round-1/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("off-0/round-1/head", ResultsTree.Lexer, 0, ResultsTree.Around(1500))
            .Add("off-0/confirm/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("off-0/confirm/head", ResultsTree.Lexer, 0, ResultsTree.Around(1480))
            .Add("off-0/confirm-2/head", ResultsTree.Lexer, 0, ResultsTree.Around(1520))
            .Add("off-0/confirm-2/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        GateFinding finding = Assert.Single(report.Failures);
        Assert.Equal(FindingKind.Slower, finding.Kind);
        Assert.Equal(TimePass.Avx512Off, finding.Pass);
        Assert.Equal("slower with AVX-512 disabled", finding.Label);
        Assert.Equal(
            "1.50x: 1.000 us on base, 1.500 us here (rounds 1.50; reruns 1.48, 1.52) on INTEL XEON PLATINUM 8573C, AVX-512 disabled",
            finding.Message);
        Assert.Equal(2, report.Times.Count);
        Assert.Equal([TimePass.Default, TimePass.Avx512Off], report.Times.Select(static time => time.Pass));
        Assert.Equal([ResultsTree.Lexer], BenchmarkGate.Suspects(tree.Root));
    }

    [Fact]
    public void A_single_round_slowdown_that_one_rerun_does_not_reproduce_is_a_notice()
    {
        using ResultsTree tree = SameOnDefault();
        tree.AddMachine("off-0", XeonOff)
            .Add("off-0/round-1/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("off-0/round-1/head", ResultsTree.Lexer, 0, ResultsTree.Around(1500))
            .Add("off-0/confirm/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("off-0/confirm/head", ResultsTree.Lexer, 0, ResultsTree.Around(1400))
            .Add("off-0/confirm-2/head", ResultsTree.Lexer, 0, ResultsTree.Around(1020))
            .Add("off-0/confirm-2/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.True(report.Passed);
        GateFinding finding = Assert.Single(report.Findings);
        Assert.Equal("slower, within tolerance with AVX-512 disabled", finding.Label);
    }

    [Fact]
    public void A_slowdown_in_the_default_pass_names_the_runner_it_was_measured_on()
    {
        using var tree = new ResultsTree();
        tree.AddMachine("shard-0", Epyc)
            .AddRounds("base", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddRounds("head", 2, ResultsTree.Lexer, 0, ResultsTree.Around(1000));
        foreach (string folder in (string[])["round-1/base", "round-2/base"])
        {
            tree.Add("shard-0/" + folder, ResultsTree.Flate, 0, ResultsTree.Around(500));
        }

        foreach (string folder in (string[])["round-1/head", "round-2/head"])
        {
            tree.Add("shard-0/" + folder, ResultsTree.Flate, 0, ResultsTree.Around(700));
        }

        tree.Add("shard-0/confirm/base", ResultsTree.Flate, 0, ResultsTree.Around(500))
            .Add("shard-0/confirm/head", ResultsTree.Flate, 0, ResultsTree.Around(700));
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0), (ResultsTree.Flate, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        GateFinding finding = Assert.Single(report.Failures);
        Assert.Equal("slower", finding.Label);
        Assert.Equal(TimePass.Default, finding.Pass);
        Assert.EndsWith("on AMD EPYC 7763 64-Core Processor, no AVX-512", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_summary_lists_every_runner_and_both_passes()
    {
        using ResultsTree tree = SameOnDefault();
        tree.AddMachine("off-0", XeonOff)
            .Add("off-0/round-1/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("off-0/round-1/head", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .AddMachine("bench-gate-allocations-0/alloc-0", Epyc);
        string baseline = tree.WriteBaseline((ResultsTree.Lexer, 0));

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);
        string summary = report.ToMarkdown("abc1234");

        Assert.True(report.Passed);
        Assert.Equal(1, report.RoundCounts[TimePass.Avx512Off]);
        Assert.Contains("and again with AVX-512 disabled in 1 round(s)", summary, StringComparison.Ordinal);
        Assert.Contains("Runners: 3 job(s), 2 with AVX-512", summary, StringComparison.Ordinal);
        Assert.Contains("| `shard-0` | INTEL XEON PLATINUM 8573C | yes | AVX-512 on |", summary, StringComparison.Ordinal);
        Assert.Contains("| `off-0` | INTEL XEON PLATINUM 8573C | yes | AVX-512 disabled |", summary, StringComparison.Ordinal);
        Assert.Contains("| `bench-gate-allocations-0/alloc-0` | AMD EPYC 7763 64-Core Processor | no | no AVX-512 |", summary, StringComparison.Ordinal);
        Assert.Contains("| AVX-512 off |", summary, StringComparison.Ordinal);
        Assert.Contains("| AVX-512 on |", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_processor_model_and_AVX512_flag_are_read_from_proc_cpuinfo()
    {
        const string cpuinfo = """
            processor	: 0
            vendor_id	: GenuineIntel
            model name	: INTEL(R) XEON(R) PLATINUM 8573C
            flags		: fpu vme de pse avx2 avx512f avx512dq avx512cd avx512bw avx512vl

            processor	: 1
            model name	: INTEL(R) XEON(R) PLATINUM 8573C
            """;

        Assert.Equal(("INTEL(R) XEON(R) PLATINUM 8573C", true), MachineInfo.ParseCpuInfo(cpuinfo));
        Assert.Equal(("AMD EPYC 7763 64-Core Processor", false), MachineInfo.ParseCpuInfo("model name\t: AMD EPYC 7763 64-Core Processor\nflags\t\t: fpu avx2 avx512_bf16_x\n"));
        Assert.Equal((null, null), MachineInfo.ParseCpuInfo("processor : 0\n"));
    }

    [Fact]
    public void The_AVX512_off_pass_asks_for_no_allocation_figures()
    {
        // Its jobs measure times only; the allocations jobs have the figures of every case.
        using var tree = new ResultsTree();
        tree.AddMachine("off-0", XeonOff)
            .Add("off-0/round-1/base", ResultsTree.Flate, 0, ResultsTree.Around(500))
            .Add("off-0/round-1/head", ResultsTree.Flate, 0, ResultsTree.Around(500));
        Directory.Delete(Path.Combine(tree.Root, "off-0", "allocations"), recursive: true);
        string baseline = tree.WriteBaseline();

        GateReport report = BenchmarkGate.Run(baseline, tree.Root);

        Assert.True(report.Passed);
        Assert.Equal(0, report.CaseCount);
        Assert.Single(report.Times);
    }

    [Fact]
    public void A_machine_file_round_trips()
    {
        using var tree = new ResultsTree();
        tree.AddMachine("x", XeonOff);

        MachineInfo read = MachineInfo.Load(Path.Combine(tree.Root, "x", MachineInfo.FileName));

        Assert.Equal(XeonOff, read);
        Assert.Equal(TimePass.Avx512Off, read.Pass);
        Assert.Equal("INTEL XEON PLATINUM 8573C, AVX-512 disabled", read.Describe());
    }

    [Theory]
    [InlineData(true, true, false, "AVX-512 on")]
    [InlineData(false, false, false, "no AVX-512")]
    [InlineData(true, false, false, "AVX-512 not used by .NET")]
    [InlineData(true, false, true, "AVX-512 disabled")]
    [InlineData(false, false, true, "no AVX-512 (nothing to disable)")]
    [InlineData(true, true, true, "AVX-512 still on (disabling it failed)")]
    public void The_AVX512_state_is_described(bool hardware, bool runtime, bool disabled, string expected) =>
        Assert.Equal(expected, new MachineInfo("cpu", hardware, runtime, disabled, "os", "X64").Avx512State());

    /// <summary>The lexer measured equal on a Xeon with AVX-512 in two rounds of the default pass.</summary>
    private static ResultsTree SameOnDefault()
    {
        var tree = new ResultsTree();
        tree.AddMachine("shard-0", Xeon)
            .Add("shard-0/round-1/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("shard-0/round-1/head", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("shard-0/round-2/head", ResultsTree.Lexer, 0, ResultsTree.Around(1000))
            .Add("shard-0/round-2/base", ResultsTree.Lexer, 0, ResultsTree.Around(1000));
        return tree;
    }
}
