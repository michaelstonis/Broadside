# Workflows

## `ci.yml`: every pull request and every push to `main`

| Job | Runs on | What it does |
|---|---|---|
| `build-test` | ubuntu, windows, macos | `dotnet build Broadside.slnx -c Release -warnaserror`, then `dotnet test Broadside.Core.slnf` with TRX reports uploaded as `test-results-<os>`. On Linux and Windows the CoreGraphics and Android backends drop out through the `BroadsideBuildPlatformBackends` gate; macOS installs the `macos ios maccatalyst android` workloads first and builds them (the CoreGraphics package for macOS, iOS and Mac Catalyst), then also runs `tests/Broadside.Rendering.CoreGraphics.Tests`, the CoreGraphics backend's golden-image and allocation tests (issue #125). Windows also runs `tests/Broadside.Rendering.Direct2D.Tests` (Direct2D on a WARP device against the golden images, outside the solution filter because it needs Windows) and dry-runs `bench/Broadside.Rendering.Direct2D.Benchmarks`. Linux also packs the core packages and uploads them as `packages`. |
| `aot-smoke` | ubuntu, windows, macos | `dotnet publish tests/Broadside.Rendering.AotSmoke -c Release --use-current-runtime` (Native AOT, trimmed, trim and AOT warnings are errors), then runs the executable with `--require-aot`: it renders the rendering corpus files with the software rasterizer, compares every pixel with the reviewed snapshots of `Broadside.Rendering.Tests` and round-trips the PNG output (issue #95). |
| `verapdf` | ubuntu | Installs veraPDF 1.28.2 (pinned, SHA-256 checked, headless IzPack install, Temurin 21) and runs the `Category=VeraPdf` tests of `tests/Broadside.Tests` and `tests/Broadside.Fonts.Standard14.Tests` with `BROADSIDE_REQUIRE_VERAPDF=1`: documents saved as PDF/A-2b must pass veraPDF's PDF/A-2B profile with no failed rule (#201). Locally those tests skip when `verapdf` is not on the PATH (or at `BROADSIDE_VERAPDF`). |
| `api-surface` | ubuntu (PRs only) | Writes the diff of every `src/**/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` against the base branch to the job summary. Never fails; the PublicApiAnalyzers already fail the build on an undeclared API change. |
| `conformance-map` | ubuntu | `dotnet run --project tools/ConformanceCheck -- --root . --summary` into the job summary; fails on a `done` row without a test. Its steps are skipped, with a note in the summary, until `tools/ConformanceCheck` lands (issue #7). |
| `format` | ubuntu | `dotnet format --verify-no-changes` on `Broadside.Core.slnf`, `tests/Broadside.Fuzz`, `bench/Broadside.Benchmarks`, `tests/Broadside.Rendering.Direct2D.Tests` and `bench/Broadside.Rendering.Direct2D.Benchmarks` (the last four are outside the solution filter). |
| `fuzz-smoke (<shard>/6)` | ubuntu | Six parallel shards: each `--list`s the fuzz targets and runs `--smoke <target> 60` for every target whose index modulo 6 is its shard; uploads `artifacts/fuzz/**` as `fuzz-findings-<shard>` on failure. |
| `bench-dry` | ubuntu | `dotnet run --project bench/Broadside.Benchmarks -- --filter '*' --job dry`: one iteration of every benchmark, no measurement; then the same for the realistic tier, `bench/Broadside.Benchmarks.Realistic` (the sample documents, not gated, #363). |
| `blazor-wasm / blazor-wasm (chrome <browsers>)` | ubuntu | Calls [`blazor-wasm.yml`](#blazor-wasmyml-the-blazor-webassembly-sample-in-browsers) with `browsers: firefox` on pull requests into the integration branch, and `firefox webkit` on pull requests into `main` and pushes to `main` (#389). |
| `maui` | macOS | Installs the `maui` workload and builds `samples/Broadside.Viewer.Maui.Sample` (#226) for Android in Debug and in Release (ILLink and AOT on, arm64 and x86_64), and compiles its Mac Catalyst and iOS-simulator heads (`-t:Compile`: bundling needs Xcode 27, which the image lacks), all with `-warnaserror`. Then `tools/ApkAotCheck` checks that every AOT image in the Release APK holds the MVIDs of the assemblies the APK ships, the mismatch that crashed the Release app at startup in #366 (#368). The shell library itself builds in `build-test`; its tests are in `Broadside.Core.slnf`. |

A newer push to the same PR or branch cancels the older run. NuGet packages are cached per OS, keyed on `Directory.Packages.props` and every `.csproj`. `DiffEngine_Disabled=true` keeps Verify from launching a diff tool.

## `blazor-wasm.yml`: the Blazor WebAssembly sample in browsers

Called by `ci.yml` (input `browsers`), nightly at 03:47 UTC on the integration branch (`NIGHTLY_BRANCH`, `spec/33-broadside-v1`; GitHub runs schedules from `main`'s copy, so the nightly starts once this file is on `main`) and on the "Run workflow" button, both with `firefox webkit`.

| Job | Runs on | What it does |
|---|---|---|
| `blazor-wasm (chrome <browsers>)` | ubuntu | Installs `wasm-tools`, publishes `samples/Broadside.Viewer.Blazor.Sample` twice with `publish-static.sh` (trimmed and AOT-compiled; then `--no-aot --runtime-folder framework`, in its own obj/bin folders), and installs Playwright's `browsers`: `~/.cache/ms-playwright` is cached per Playwright version (read from `Directory.Packages.props`) and browser set, each install attempt is killed after 4 minutes and retried up to three times, and WebKit's system packages are installed with apt timeouts and retries (Firefox needs none: it runs on the libraries of the image's own Firefox). Then runs `tests/Broadside.Viewer.Blazor.BrowserTests` in the image's Chrome and those browsers, all required (`BROADSIDE_BROWSERS_REQUIRED`): a remote linearized file shows page 1 before the download completes, keyboard and screen-reader reading order, a phone profile with pinch-zoom, every demo feature at 390 px with touch in each browser (#338; the WebKit cases skip where WebKit is not installed), the non-AOT build booting from a host that refuses paths starting with `_`, no native binaries and the size budgets (issues #221, #338). Screenshots are uploaded as `blazor-demo-screenshots`. |

WebKit is not on every pull request because its system packages (about 120 MB of GStreamer and friends from the Ubuntu mirror) stalled the install past its 10-minute limit twice on #388, so the browser tests never ran (#389).

## `bench-gate.yml`: allocations on every pull request that touches library, benchmark or corpus code; times on hot paths, `perf`, `main` and nightly

The benchmark regression gate (issues #139, #389; [`bench/baselines/gate/README.md`](../../bench/baselines/gate/README.md) has the rules, the thresholds, when times are compared and how to update the baseline). Runs when a pull request's diff touches `src/**`, `bench/**`, `tests/Broadside.TestSupport/**`, `tests/Corpus/**`, `tools/BenchGate/**`, `Directory.Build.props`, `Directory.Packages.props`, `global.json` or the workflow; nightly at 02:17 UTC on the integration branch (from `main`'s copy of the file); and on the "Run workflow" button, which takes a `base` (branch, tag or commit, default `main`) and `timing` (default true).

| Job | Runs on | What it does |
|---|---|---|
| `plan` | ubuntu-24.04 | Picks the head and base commits and decides whether times are compared: on a pull request into `main`, one labelled `perf` (read from the API, so a re-run sees a label added since), or one whose diff matches `HOT_PATHS` (lexer and parsing, content, filters and codecs, rendering and colour, fonts and text, SDK and package versions); nightly, against the branch's tip when the previous nightly ran (nothing merged since: nothing runs); by hand, per `timing`. Writes the decision and its reason to the job summary. The decision is `bench/gate-plan.sh`, tested by `bench/test-gate-plan.sh` (0 to 10 hot files, with and without `perf`, into `main` and the integration branch). |
| `allocations (<shard>)` | ubuntu-24.04, four shards | Picks this shard's methods with `tools/BenchGate shard` (`OpenAndWalk` split over all shards through `BROADSIDE_BENCH_PARTITION`) and runs `bench/run-gate.sh --measure allocations`: the head's allocation figures only (`Broadside.Benchmarks --gate-allocations`), no base build, no timing. About 10 minutes. Uploads `bench-gate-allocations-<shard>`. |
| `measure (<shard>)` | ubuntu-24.04, four shards, only when times are compared | Checks out the base commit in a second worktree and runs `bench/run-gate.sh --measure times`: the base commit's allocation figures (the stale-baseline notice), two interleaved BenchmarkDotNet rounds of both commits, and a rerun of any case the rounds find slower. 45 to 70 minutes. Uploads `bench-gate-shard-<shard>`. |
| `gate` | ubuntu-24.04 | Runs always, and fails first if `plan` failed or left an output empty (which would otherwise skip every measuring job silently). Downloads every shard's figures and reports and runs `tools/BenchGate compare` against `bench/baselines/gate/allocations.json`: one row per regression in the job summary and an `::error` annotation each, then fails if there is any, or if a shard failed. Uploads the baseline re-recorded from the run as `bench-gate-baseline`. Nightly, a failure opens the issue "Nightly benchmark gate failed on spec/33-broadside-v1" (label `area:ci`), or comments on it while it is open. |

A newer push to the same pull request cancels the older run. A run without times takes about 12 minutes; with times, about 70. The gate is not a required check: an intended slowdown is merged by the owner with the reason in the pull request.

## `bench-gate-perf.yml`: the `perf` label

On `pull_request_target` `labeled` with `perf` only (every other label skips its one job): finds the benchmark gate run of the pull request's head commit, cancels it if it is still running, and re-runs it (`gh run rerun`), so its `plan` job sees the label and times are compared. The gate itself does not trigger on labels, so that labelling a pull request for any other reason never starts, cancels or replaces a gate run. A pull request that changes nothing the gate watches has no run to re-run; use the gate's "Run workflow" button.

It is `pull_request_target` so that it also works for a fork's pull request, whose `pull_request` token is read-only. That is safe because the job is metadata-only: it checks out and runs nothing from the pull request, takes only the label name (in `if:`) and the head SHA (through an environment variable) from the event, and its token has `actions: write` and `pull-requests: read` only (the file's header comment has the reasoning; keep it that way). `pull_request_target` runs the base branch's copy of the file, so it works for pull requests into a branch once the file is on that branch.

## `corpus.yml`: weekly (Monday 04:17 UTC) and the "Run workflow" button

| Job | Runs on | What it does |
|---|---|---|
| `corpus-gates` | ubuntu | Fetches every non-manual corpus with `tools/CorpusFetcher`, builds in Release, and runs `tests/Broadside.Tests` with `--filter-trait "Category=Corpus"` and `BROADSIDE_CORPUS_DIR` set: the open-and-walk gate (#47), the veraPDF agreement, the real-world font, image and colour sweeps, and the content interpreter gate (#80, one class per corpus, with its allowlist and snapshots). Uploads `artifacts/corpus-gate/*.json` (per-file counts, diagnostics, time and allocation) as `corpus-gate`. Then runs `tests/Broadside.Rendering.Tests` with the same trait: the rendering gate (#138: every page of every file through the software rasterizer with every layer, no exception, no page over its processor-time budget, render diagnostics on well-formed files triaged in `render-diagnostics.allowlist.txt`, per-corpus snapshots) and the pinned subset (reviewed fingerprints, parallel renders equal to sequential ones). Uploads `artifacts/render-gate/` (per-file JSON and the pinned pages' PNGs; real-world renders are not committed, open decision 8) as `render-gate`. |

The corpus tests skip when `corpus/` is absent, which is why `ci.yml` cannot catch a regression in them; this workflow is where they run.

## `android.yml`: the Android backend on an emulator, weekly, on Android pull requests and the "Run workflow" button

Runs Mondays 05:17 UTC, on pull requests that touch `src/Broadside.Rendering.Android/**`, the display list (`src/Broadside/Rendering/**`, `src/Broadside/Graphics/**`), the Android test projects, the golden-image harness, `tests/Golden/**`, `tests/Corpus/**` or the workflow itself, and on the "Run workflow" button (spec #33 open decision 9: emulators are too slow and flaky for every PR).

| Job | Runs on | What it does |
|---|---|---|
| `emulator (API 35)` | ubuntu | Installs the `android` workload and the SDK packages it needs (`-t:InstallAndroidDependencies`), builds `tests/Broadside.Rendering.Android.Device` in Release for `android-x64` and the host project `tests/Broadside.Rendering.Android.Tests`, enables KVM, boots an x86_64 `google_apis` emulator with `reactivecircus/android-emulator-runner` (3 cores, 4 GB RAM, 576 MB heap; `-no-snapshot -no-audio -no-boot-anim -gpu swiftshader_indirect`, no cameras) and runs `.github/scripts/android-emulator-tests.sh`, which logs the runner's memory, CPUs and KVM, waits for the device to be attached and booted, and runs the host tests with `BROADSIDE_ANDROID_REQUIRED=1` (a missing device fails instead of skipping). The tests install the APK, then render the corpus files in batches of 8, one instrumentation run each (render through `AndroidCanvasBackend`, measure warm replays), pull each batch's gzip-compressed pixels over adb, 16 files per `adb pull`, and compare them with the golden images (issue #133). Robustness against the emulator's adb transport dropping to `device offline` (issue #344): the harness checks that the device is online and booted before every phase, retries install, clear and pull after `adb reconnect offline` (restarting the adb server on later attempts) with back-off, and reruns an instrumentation batch only when the transport cut it off before it reported a result code; an error the device reports (a failed install, a crashed or failing instrumentation) fails at once. When the transport stays down the tests fail with `adb transport lost`, and only then does the script reconnect and run the test step once more (the first attempt's TRX and adb log move to `attempt-1/`). Uploads the TRX, the harness's `android-adb.log` (every adb command with its exit code and time), the test console logs and, on failure, logcat as `android-test-results`, and on failure `tests/Golden/*.android.received.png` and `.diff.png` as `golden-images-android`. |

## `maui-android.yml`: the MAUI sample on an emulator, weekly, on MAUI sample pull requests and the "Run workflow" button

Runs Mondays 05:47 UTC, on pull requests that touch `samples/Broadside.Viewer.Maui.Sample/**`, the viewer shells it is built from (`src/Broadside.Viewer/**`, `src/Broadside.Viewer.Maui/**`, `src/Broadside.Rendering.Skia/**`), `tools/ApkAotCheck/**`, `Directory.Build.props`, `Directory.Packages.props`, `global.json` or the workflow itself, and on the "Run workflow" button (spec #33 open decision 9, as for `android.yml`).

| Job | Runs on | What it does |
|---|---|---|
| `smoke (API 35)` | ubuntu | Installs the `maui-android` workload and the SDK packages it needs, builds the sample in Release for `android-x64` with `-warnaserror`, runs `tools/ApkAotCheck` on the APK, enables KVM, boots an x86_64 `google_apis` emulator (SwiftShader GPU) with `reactivecircus/android-emulator-runner` and runs `samples/Broadside.Viewer.Maui.Sample/smoke-android.sh`: install, start the smoke intent, and require `BROADSIDE_SMOKE_OK` in logcat (page 1 of the sample textbook chapter captured from the GPU surface) before a `BROADSIDE_SMOKE_FAILED`, a native abort or 300 s (#368). |

## `fuzz.yml`: real fuzzing, weekly and on the "Run workflow" button

Every fuzz target under libFuzzer through SharpFuzz, one parallel job per target (issue #48; `tests/Broadside.Fuzz/README.md`, "Scheduled runs in CI").

| Job | Runs on | What it does |
|---|---|---|
| `targets` | ubuntu | Builds the harness and turns `--list` (or the `targets` input) into the matrix; checks the `minutes` input (default 80, at most 300 so every job stays under GitHub's 6-hour limit). |
| `fuzz (<target>)` | ubuntu | Builds the libfuzzer-dotnet driver from pinned, hash-checked source with clang; installs SharpFuzz.CommandLine 2.3.0; builds the harness in Release; fetches `pdf20examples` and `qpdf` with `tools/CorpusFetcher` (cached) as extra seeds; restores the target's libFuzzer corpus from the previous run (`actions/cache`, key `fuzz-corpus-<target>-<run id>`); runs `tests/Broadside.Fuzz/run-libfuzzer.sh` (which instruments a copy of `Broadside.dll`) for the given minutes on every core; saves the minimized corpus; uploads `stats.json` as `fuzz-stats-<target>`, and on a finding fails and uploads `fuzz-findings-<target>` (inputs, replays, worker logs). |
| `summary` | ubuntu | One table in the job summary: minutes, workers, CPU-hours, executions, corpus size and findings per target, and the cumulative fuzzing time of the run. |

Schedule: Sundays 03:17 UTC. A scheduled run fuzzes every registered target for 80 minutes: the 19 Phase 1 targets alone make 25.3 hours of wall-clock time (about 100 CPU-hours on the 4-core runners), and every target added later adds 80 minutes. Runs on one branch never overlap (they would race to save the same cache). From a terminal: `gh workflow run fuzz.yml --ref main -f minutes=30 -f targets="document save"`. A finding is fixed in a PR with a regression test next to the feature that rebuilds the input's mechanism (issue #48's are in `PredictorTests`/`FilterAllocationTests`, `CrossReferenceStreamTests`, `SaveTests` and `PageLabelTests`); replay the uploaded input with `--run` first.

## `docs.yml`: the documentation site

Runs on pull requests and pushes to `main` that touch `site/**`, `src/**`, `docs/**`, `.config/**`, `check-docs.sh`, `check-adr-refs.sh`, any top-level `*.md`, any `README*`, the issue and pull request templates, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, the Blazor demo (`samples/Broadside.Viewer.Blazor.Sample/**`, and `tests/Corpus/*.pdf` for its gallery) or the workflow itself, and on the "Run workflow" button.

| Job | Runs on | What it does |
|---|---|---|
| `build` | ubuntu | Installs the `android` workload and the Android SDK packages it needs. Publishes the live demo (`samples/Broadside.Viewer.Blazor.Sample`) with `publish-static.sh` (the gallery reads the TraceMonkey paper from pdf.js's copy, which the repository does not carry; see the sample's README, "Documents and licences"): trimmed and compiled ahead of time outside pull requests, after installing the `wasm-tools` workload, and with `--no-aot` and no workload on pull requests, which takes about 6 minutes instead of about 10 (`ci.yml`'s `blazor-wasm` job publishes and tests the AOT build on every pull request). Then runs `./check-docs.sh --demo <folder>`: first runs `./check-adr-refs.sh`, which fails when a top-level Markdown file (except `CLAUDE.md` and `CONTEXT.md`), anything under `site/` or `docs/` (except the decision records themselves), any README, an issue or pull request template, or a `///` comment in `src/` cites or links an architecture decision record (see the script's header for the exact match); then restores the local tools (`.config/dotnet-tools.json`, Lunet), builds `site/` with `lunet build` into `site/.lunet/build/www`, and fails on a Lunet error, a missing route (home, each article, `/api/`, the search index), a raw `.md` link, a package under `src/` without an API reference (a public type in its `PublicAPI` files with no page under `/api/`, or, for a package the job cannot build, not documented in `site/articles/api-coverage.md`), or an internal link or asset that does not resolve. `--demo` copies the published demo into `demo/` after the Lunet build and checks its routes, its runtime and its relative base href (which makes it work under `/Broadside/demo/`) with the rest. Lunet's `api.dotnet` module builds every package in Release, one target framework each (`net10.0`, `net10.0-windows10.0.19041.0` for Direct2D, `net10.0-android` for the Android backend), and turns their XML docs into `/api/`; `Broadside.Rendering.CoreGraphics` needs the Apple workloads, which do not install on Linux, so its API is written out in `site/articles/api-coverage.md`. On `main` it also uploads the output, demo included, as the Pages artifact. |
| `deploy` | ubuntu (`main` only) | `actions/deploy-pages` publishes the artifact to <https://michaelstonis.github.io/Broadside/> through the `github-pages` environment; the demo is at <https://michaelstonis.github.io/Broadside/demo/>. |

Pull requests only build and check. GitHub Pages is configured with the "GitHub Actions" source (`build_type: workflow`), so nothing is pushed to a `gh-pages` branch, and Jekyll does not run, so the demo's `_framework/` and `_content/` folders are served as they are. The demo uses no WebAssembly threads, so it needs no cross-origin isolation headers (which Pages cannot send).

Locally, `./check-docs.sh` runs the same build and checks, without the demo (links into `demo/` are then left unchecked); `samples/Broadside.Viewer.Blazor.Sample/publish-static.sh --no-aot out/demo && ./check-docs.sh --demo out/demo` checks it too. To preview with live reload, `cd site && dotnet tool run lunet serve` (the dev environment drops the `/Broadside` base path).

## `release.yml`: tags `v*` and the "Run workflow" button

1. `build-linux`: builds, tests and packs the core packages (`Broadside.Core.slnf`), including `Broadside.Rendering.Direct2D` (a single `net10.0-windows10.0.19041.0` target that builds anywhere through `EnableWindowsTargeting`).
2. `build-windows`: builds the Windows target of `Broadside.Viewer.Maui` (`net10.0-windows10.0.19041.0`, the `Platforms/Windows` handler) and uploads the assembly, PDB and XML docs as `viewer-maui-windows`. WinAppSDK's build runs `MakePri.exe`, so this target builds only on Windows, and the Apple targets only on macOS; neither runner can build the whole package (#381).
3. `build-macos` (after `build-windows`): installs the workloads, builds the full solution, tests `Broadside.Core.slnf` and the CoreGraphics backend, and packs it. It then downloads `viewer-maui-windows` into the Windows target's output folder and repacks `Broadside.Viewer.Maui` with `-p:BroadsideBuildWindowsShell=true` (restore, then `dotnet pack --no-build`), so the one package holds the `net10.0`, Android, iOS, Mac Catalyst and Windows assemblies, and fails if any of the five is missing (the listing goes to the job summary). It uploads only `Broadside.Rendering.CoreGraphics`, `Broadside.Rendering.Android` and `Broadside.Viewer.Maui`, so that every package comes from exactly one build.
4. `publish`: merges the two sets into the `packages` artifact. On a `v*` tag that is not a dry run it logs in to NuGet.org with trusted publishing, runs `dotnet nuget push --skip-duplicate`, and creates a GitHub release with generated notes and the packages attached (marked pre-release when the tag has a `-` suffix, e.g. `v0.1.0-preview.1`).

MinVer derives the package version from the tag (`v1.2.3` → `1.2.3`); a dispatch run on a branch produces `0.1.0-preview.0.<height>`.

### Release dry run

Actions tab → **Release** → **Run workflow** → pick the branch or tag → leave **dry_run** checked → **Run workflow**. Or from a terminal:

```sh
gh workflow run release.yml --ref main -f dry_run=true
gh run watch
```

The dry run builds, tests and packs on all three runners and uploads the merged `packages` artifact, but pushes nothing and creates no release. It needs no secrets. Setting `dry_run` to false on a tag ref performs a real release; on a branch ref it still publishes nothing, because the push and release steps also require a `v*` tag.

### Trusted publishing (one-time setup by the owner)

The repository holds no NuGet API key. `NuGet/login` exchanges the job's GitHub OIDC token for a short-lived API key, which only works once nuget.org knows the workflow. Before the first real release:

1. Sign in to nuget.org as `stonis` (the nuget.org account; the workflow passes it as `user:`) → profile → **Trusted Publishing** → **Add**.
2. Repository owner `michaelstonis`, repository `Broadside`, workflow file `release.yml`, environment empty.
3. Push a tag: `git tag v0.1.0-preview.1 && git push origin v0.1.0-preview.1`.

Until that registration exists, only dry runs succeed; the login step of a real release fails with an authorization error and nothing is pushed.
