# Workflows

## `ci.yml`: every pull request and every push to `main`

| Job | Runs on | What it does |
|---|---|---|
| `build-test` | ubuntu, windows, macos | `dotnet build Broadside.slnx -c Release -warnaserror`, then `dotnet test Broadside.Core.slnf` with TRX reports uploaded as `test-results-<os>`. On Linux and Windows the CoreGraphics and Android backends drop out through the `BroadsideBuildPlatformBackends` gate; macOS installs the `macos ios maccatalyst android` workloads first and builds them (the CoreGraphics package for macOS, iOS and Mac Catalyst), then also runs `tests/Broadside.Rendering.CoreGraphics.Tests`, the CoreGraphics backend's golden-image and allocation tests (issue #125). Windows also runs `tests/Broadside.Rendering.Direct2D.Tests` (Direct2D on a WARP device against the golden images, outside the solution filter because it needs Windows) and dry-runs `bench/Broadside.Rendering.Direct2D.Benchmarks`. Linux also packs the core packages and uploads them as `packages`. |
| `aot-smoke` | ubuntu, windows, macos | `dotnet publish tests/Broadside.Rendering.AotSmoke -c Release --use-current-runtime` (Native AOT, trimmed, trim and AOT warnings are errors), then runs the executable with `--require-aot`: it renders the rendering corpus files with the software rasterizer, compares every pixel with the reviewed snapshots of `Broadside.Rendering.Tests` and round-trips the PNG output (issue #95). |
| `api-surface` | ubuntu (PRs only) | Writes the diff of every `src/**/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` against the base branch to the job summary. Never fails; the PublicApiAnalyzers already fail the build on an undeclared API change. |
| `conformance-map` | ubuntu | `dotnet run --project tools/ConformanceCheck -- --root . --summary` into the job summary; fails on a `done` row without a test. Its steps are skipped, with a note in the summary, until `tools/ConformanceCheck` lands (issue #7). |
| `format` | ubuntu | `dotnet format --verify-no-changes` on `Broadside.Core.slnf`, `tests/Broadside.Fuzz`, `bench/Broadside.Benchmarks`, `tests/Broadside.Rendering.Direct2D.Tests` and `bench/Broadside.Rendering.Direct2D.Benchmarks` (the last four are outside the solution filter). |
| `fuzz-smoke (<shard>/6)` | ubuntu | Six parallel shards: each `--list`s the fuzz targets and runs `--smoke <target> 60` for every target whose index modulo 6 is its shard; uploads `artifacts/fuzz/**` as `fuzz-findings-<shard>` on failure. |
| `bench-dry` | ubuntu | `dotnet run --project bench/Broadside.Benchmarks -- --filter '*' --job dry`: one iteration of every benchmark, no measurement. |

A newer push to the same PR or branch cancels the older run. NuGet packages are cached per OS, keyed on `Directory.Packages.props` and every `.csproj`. `DiffEngine_Disabled=true` keeps Verify from launching a diff tool.

## `corpus.yml`: weekly (Monday 04:17 UTC) and the "Run workflow" button

| Job | Runs on | What it does |
|---|---|---|
| `corpus-gates` | ubuntu | Fetches every non-manual corpus with `tools/CorpusFetcher`, builds in Release, and runs `tests/Broadside.Tests` with `--filter-trait "Category=Corpus"` and `BROADSIDE_CORPUS_DIR` set: the open-and-walk gate (#47), the veraPDF agreement, the real-world font, image and colour sweeps, and the content interpreter gate (#80, one class per corpus, with its allowlist and snapshots). Uploads `artifacts/corpus-gate/*.json` (per-file counts, diagnostics, time and allocation) as `corpus-gate`. Then runs `tests/Broadside.Rendering.Tests` with the same trait: the rendering gate (#138: every page of every file through the software rasterizer with every layer, no exception, no page over its processor-time budget, render diagnostics on well-formed files triaged in `render-diagnostics.allowlist.txt`, per-corpus snapshots) and the pinned subset (reviewed fingerprints, parallel renders equal to sequential ones). Uploads `artifacts/render-gate/` (per-file JSON and the pinned pages' PNGs; real-world renders are not committed, open decision 8) as `render-gate`. |

The corpus tests skip when `corpus/` is absent, which is why `ci.yml` cannot catch a regression in them; this workflow is where they run.

## `android.yml`: the Android backend on an emulator, weekly, on Android pull requests and the "Run workflow" button

Runs Mondays 05:17 UTC, on pull requests that touch `src/Broadside.Rendering.Android/**`, the display list (`src/Broadside/Rendering/**`, `src/Broadside/Graphics/**`), the Android test projects, the golden-image harness, `tests/Golden/**`, `tests/Corpus/**` or the workflow itself, and on the "Run workflow" button (spec #33 open decision 9: emulators are too slow and flaky for every PR).

| Job | Runs on | What it does |
|---|---|---|
| `emulator (API 35)` | ubuntu | Installs the `android` workload and the SDK packages it needs (`-t:InstallAndroidDependencies`), builds `tests/Broadside.Rendering.Android.Device` in Release for `android-x64` and the host project `tests/Broadside.Rendering.Android.Tests`, enables KVM, boots an x86_64 `google_apis` emulator with `reactivecircus/android-emulator-runner` and runs the host tests with `BROADSIDE_ANDROID_REQUIRED=1` (a missing device fails instead of skipping). The tests install the APK, run its instrumentation (render the path corpus files through `AndroidCanvasBackend`, measure warm replays), pull the pixels over adb and compare them with the golden images (issue #133). Uploads TRX as `android-test-results`, and on failure `tests/Golden/*.android.received.png` and `.diff.png` as `golden-images-android`. |

## `fuzz.yml`: real fuzzing, weekly and on the "Run workflow" button

Every fuzz target under libFuzzer through SharpFuzz, one parallel job per target (issue #48; `tests/Broadside.Fuzz/README.md`, "Scheduled runs in CI").

| Job | Runs on | What it does |
|---|---|---|
| `targets` | ubuntu | Builds the harness and turns `--list` (or the `targets` input) into the matrix; checks the `minutes` input (default 80, at most 300 so every job stays under GitHub's 6-hour limit). |
| `fuzz (<target>)` | ubuntu | Builds the libfuzzer-dotnet driver from pinned, hash-checked source with clang; installs SharpFuzz.CommandLine 2.3.0; builds the harness in Release; fetches `pdf20examples` and `qpdf` with `tools/CorpusFetcher` (cached) as extra seeds; restores the target's libFuzzer corpus from the previous run (`actions/cache`, key `fuzz-corpus-<target>-<run id>`); runs `tests/Broadside.Fuzz/run-libfuzzer.sh` (which instruments a copy of `Broadside.dll`) for the given minutes on every core; saves the minimized corpus; uploads `stats.json` as `fuzz-stats-<target>`, and on a finding fails and uploads `fuzz-findings-<target>` (inputs, replays, worker logs). |
| `summary` | ubuntu | One table in the job summary: minutes, workers, CPU-hours, executions, corpus size and findings per target, and the cumulative fuzzing time of the run. |

Schedule: Sundays 03:17 UTC. A scheduled run fuzzes every registered target for 80 minutes: the 19 Phase 1 targets alone make 25.3 hours of wall-clock time (about 100 CPU-hours on the 4-core runners), and every target added later adds 80 minutes. Runs on one branch never overlap (they would race to save the same cache). From a terminal: `gh workflow run fuzz.yml --ref main -f minutes=30 -f targets="document save"`. A finding is fixed in a PR with a regression test next to the feature that rebuilds the input's mechanism (issue #48's are in `PredictorTests`/`FilterAllocationTests`, `CrossReferenceStreamTests`, `SaveTests` and `PageLabelTests`); replay the uploaded input with `--run` first.

## `docs.yml`: the documentation site

Runs on pull requests and pushes to `main` that touch `site/**`, `src/**`, `docs/**`, `.config/**`, `check-docs.sh`, `Directory.Build.props`, `Directory.Packages.props`, `global.json` or the workflow itself, and on the "Run workflow" button.

| Job | Runs on | What it does |
|---|---|---|
| `build` | ubuntu | `./check-docs.sh`: restores the local tools (`.config/dotnet-tools.json`, Lunet), builds `site/` with `lunet build` into `site/.lunet/build/www`, and fails on a Lunet error, a missing route (home, each article, `/api/`, the search index), a raw `.md` link, or an internal link or asset that does not resolve. Lunet's `api.dotnet` module builds `Broadside`, `Broadside.Rendering`, `Broadside.Rendering.Skia`, `Broadside.Fonts.Standard14` and `Broadside.Fonts.Cmaps` in Release and turns their XML docs into `/api/`. On `main` it also uploads the output as the Pages artifact. |
| `deploy` | ubuntu (`main` only) | `actions/deploy-pages` publishes the artifact to <https://michaelstonis.github.io/Broadside/> through the `github-pages` environment. |

Pull requests only build and check. GitHub Pages is configured with the "GitHub Actions" source (`build_type: workflow`), so nothing is pushed to a `gh-pages` branch.

Locally, `./check-docs.sh` runs the same build and checks. To preview with live reload, `cd site && dotnet tool run lunet serve` (the dev environment drops the `/Broadside` base path).

## `release.yml`: tags `v*` and the "Run workflow" button

1. `build-linux`: builds, tests and packs the core packages (`Broadside.Core.slnf`).
2. `build-macos`: installs the workloads, builds the full solution, tests `Broadside.Core.slnf` and the CoreGraphics backend, packs it, and uploads only `Broadside.Rendering.CoreGraphics` and `Broadside.Rendering.Android`, so that every package comes from exactly one build.
3. `publish`: merges the two sets into the `packages` artifact. On a `v*` tag that is not a dry run it logs in to NuGet.org with trusted publishing, runs `dotnet nuget push --skip-duplicate`, and creates a GitHub release with generated notes and the packages attached (marked pre-release when the tag has a `-` suffix, e.g. `v0.1.0-preview.1`).

MinVer derives the package version from the tag (`v1.2.3` → `1.2.3`); a dispatch run on a branch produces `0.1.0-preview.0.<height>`.

### Release dry run

Actions tab → **Release** → **Run workflow** → pick the branch or tag → leave **dry_run** checked → **Run workflow**. Or from a terminal:

```sh
gh workflow run release.yml --ref main -f dry_run=true
gh run watch
```

The dry run builds, tests and packs on both runners and uploads the merged `packages` artifact, but pushes nothing and creates no release. It needs no secrets. Setting `dry_run` to false on a tag ref performs a real release; on a branch ref it still publishes nothing, because the push and release steps also require a `v*` tag.

### Trusted publishing (one-time setup by the owner)

The repository holds no NuGet API key. `NuGet/login` exchanges the job's GitHub OIDC token for a short-lived API key, which only works once nuget.org knows the workflow. Before the first real release:

1. Sign in to nuget.org as `michaelstonis` → profile → **Trusted Publishing** → **Add**.
2. Repository owner `michaelstonis`, repository `Broadside`, workflow file `release.yml`, environment empty.
3. Push a tag: `git tag v0.1.0-preview.1 && git push origin v0.1.0-preview.1`.

Until that registration exists, only dry runs succeed; the login step of a real release fails with an authorization error and nothing is pushed.
