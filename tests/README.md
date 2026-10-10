# Tests

## Layout

| Project | What it is |
|---|---|
| `Broadside.Tests` | Tests for the core package (COS layer, document model, filters, security, content, text, layout, forms, signatures). |
| `Broadside.Rendering.Tests` | Tests for the managed rasterizer, color engine and PNG encoder. Rendering snapshots live here. |
| `Broadside.Rendering.Skia.Tests` | Tests for the SkiaSharp backend against the same display lists. |
| `Broadside.Fonts.Standard14.Tests` | Tests for the standard 14 font metrics and programs. |
| `Broadside.Fonts.Cmaps.Tests` | Tests for the predefined CMaps. |
| `Broadside.TestSupport` | A plain library shared by the test projects: the `Corpus` helper and the Verify configuration. Not a test project. |
| `Broadside.Fuzz` | The SharpFuzz harness. A console application, not a test project; see its [README](Broadside.Fuzz/README.md). |
| `Corpus/` | Hand-written minimal PDFs, one feature each; see its [README](Corpus/README.md). |

One test project per core package, named `<Package>.Tests`; `src/Directory.Build.props` grants each one `InternalsVisibleTo` on its package (and grants it to `Broadside.Benchmarks` and `Broadside.Fuzz`). Unit tests sit next to the feature they cover, in the same namespace as the code plus `.Tests`. The Direct2D, CoreGraphics and Android backends have no test projects yet: each runs only on its own platform, so they get projects, and a CI leg, when those backends exist.

Every test project is xUnit v3 on the Microsoft Testing Platform runner (`tests/Directory.Build.props` sets `OutputType=Exe`, the runner, and a global `using Xunit`), with `AnalysisLevel=latest-all` and warnings as errors like the rest of the repository; the folder props relax the few rules that only matter for shipping code.

## Running

```sh
dotnet test Broadside.Core.slnf                        # every test project, no workloads needed
dotnet test tests/Broadside.Tests                      # one project
dotnet test tests/Broadside.Tests -- --filter-method '*.Package_assembly_loads'   # one method (fully qualified, '*' at either end)
dotnet test Broadside.Core.slnf -- --help              # the runner's own options
```

`Broadside.Core.slnf` lists the core packages and their test projects; the benchmark and fuzz projects are in `Broadside.slnx` only, so `dotnet test` on the filter never tries to run them.

## The corpus helper

`Broadside.TestSupport.Corpus` finds `tests/Corpus/` at run time by walking up from the test assembly to the directory that holds `Broadside.slnx`, so tests work from any output path and any working directory as long as they run from a checkout.

```csharp
using Broadside.TestSupport;

byte[] bytes = Corpus.Bytes("empty-page.pdf");
using FileStream stream = Corpus.Open("xref-stream.pdf");
string path = Corpus.Path("outline.pdf");

[Theory]
[MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
public void Opens(string fileName) { ... }
```

`Corpus.WellFormedFiles`, `Corpus.MalformedFiles` and `Corpus.AllFiles` are `TheoryData<string>` over the well-formed and broken tables in the corpus README (`Corpus.PasswordProtectedFiles` is `TheoryData<string, string>` of file name and user password over its password-protected table); `WellFormedFileNames`, `MalformedFileNames` and `AllFileNames` are the same lists as `IReadOnlyList<string>`. The lists are hard-coded and `CorpusSmokeTests.File_lists_match_the_corpus_directory` fails if they drift from the directory, so adding a corpus file means adding it to `Corpus.cs` and to the README table.

## The real-world corpus gate

The real-world corpora in `corpus/` (gitignored, fetched by `tools/CorpusFetcher`) are reached through `RealWorldCorpus` in `Broadside.TestSupport`. It finds `corpus/` in this checkout or the nearest enclosing one (so a worktree under `.claude/worktrees/` reuses the main checkout's corpora), or the directory named by `BROADSIDE_CORPUS_DIR`. CI does not fetch the corpora; every corpus test then skips.

```sh
dotnet run --project tools/CorpusFetcher -- --only pdfjs,pdfbox,qpdf,pdfium-tests,verapdf-corpus,pdf20examples
dotnet test tests/Broadside.Tests -- --filter-trait "Category=Corpus"
```

- `RealWorldCorpusTests` (issue #47): one theory row per PDF of pdf.js, PDFBox, qpdf, PDFium and the PDF 2.0 examples, opened leniently and walked completely (`DocumentWalker`: pages, every object, every stream decoded) within 30 s. Password, certificate and unsupported-encryption outcomes are counted, not failures; a file lenient mode gives up on must be listed, with the reason qpdf and poppler fail too, in `RealWorldCorpusTests.Unreadable`. The per-corpus counts and diagnostic histograms are a Verify snapshot, so a change in what the reader reports shows as a diff.
- `VeraPdfAgreementTests`: strict mode against the pass/fail verdict in each veraPDF corpus file name, on the syntactic "6.1 File structure" tests of PDF/A-1b, PDF/A-2b and PDF/A-4. Every disagreement is triaged per rule in `VeraPdfAgreementTests.Triage` and listed in the snapshot.
- `DocumentWalker` is meant to be shared with the open-and-walk fuzz target and benchmark: link it as source, as `CorpusLocator` is.

## Snapshots (Verify)

Rendering output, extracted text, serialized object graphs and the like are checked with [Verify](https://github.com/VerifyTests/Verify). `Broadside.TestSupport.VerifyConfiguration` runs as a module initializer in every test project and puts snapshots in a `Snapshots/` folder next to the test source file, named `<TestClass>.<TestMethod>.verified.<ext>`.

- `*.verified.*` is the accepted snapshot. It is committed.
- `*.received.*` is what the test produced this run. It is gitignored. A test fails when the two differ, or when there is no verified file yet.

To accept a change, replace the verified file with the received one and commit it:

```sh
mv tests/Broadside.Tests/Snapshots/Foo.Bar.received.txt tests/Broadside.Tests/Snapshots/Foo.Bar.verified.txt
```

or install the [Verify CLI](https://github.com/VerifyTests/Verify.Terminal) (`dotnet tool install --global verify.tool`) and run `dotnet verify review` to step through pending snapshots, or let a diff tool handle it: Verify launches one automatically when it finds it installed and `DiffEngine_Disabled` is not set. CI sets `DiffEngine_Disabled=true` (Verify also detects the `CI` variable). Review every snapshot change in the PR as carefully as the code: an accepted wrong snapshot is a wrong test.

Verify 33 and later require the consumer to state its license terms at build time; `tests/Directory.Build.props` claims the open-source exemption with a twelve-month expiry that has to be bumped before it lapses. The comment there says what to do.

## Benchmarks

`bench/Broadside.Benchmarks` is a BenchmarkDotNet console application. Every hot path gets a benchmark class with `[MemoryDiagnoser]` (CLAUDE.md, "Code conventions"); `CorpusBenchmarks` is the placeholder that proves the harness runs.

```sh
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*'              # full run, Job.Default
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*' --job short  # quick local signal (3 iterations)
dotnet run -c Release --project bench/Broadside.Benchmarks -- --filter '*' --job dry    # CI smoke: one in-process iteration, no measurement
dotnet run -c Release --project bench/Broadside.Benchmarks -- --list flat               # the benchmark names
```

`--job short` and `--job dry` are handled by `BenchmarkConfig`; any other BenchmarkDotNet option, including other `--job` values, passes through. Results land in `BenchmarkDotNet.Artifacts/` (gitignored).

## Fuzzing

`tests/Broadside.Fuzz` holds one SharpFuzz target per parser and codec. Without any fuzzer installed, `--smoke <target> [seconds]` runs the target over the corpus and random mutations of it; CI runs that for 60 s per target. Real fuzzing with libFuzzer or AFL is described in [Broadside.Fuzz/README.md](Broadside.Fuzz/README.md).

```sh
dotnet run -c Release --project tests/Broadside.Fuzz -- --smoke object-parser 10
```
