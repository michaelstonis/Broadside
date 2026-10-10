# Workflows

## `ci.yml`: every pull request and every push to `main`

| Job | Runs on | What it does |
|---|---|---|
| `build-test` | ubuntu, windows, macos | `dotnet build Broadside.slnx -c Release -warnaserror`, then `dotnet test Broadside.Core.slnf` with TRX reports uploaded as `test-results-<os>`. On Linux and Windows the CoreGraphics and Android backends drop out through the `BroadsideBuildPlatformBackends` gate; macOS installs the `macos ios maccatalyst android` workloads first and builds them. Linux also packs the core packages and uploads them as `packages`. |
| `api-surface` | ubuntu (PRs only) | Writes the diff of every `src/**/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` against the base branch to the job summary. Never fails; the PublicApiAnalyzers already fail the build on an undeclared API change. |
| `conformance-map` | ubuntu | `dotnet run --project tools/ConformanceCheck -- --root . --summary` into the job summary; fails on a `done` row without a test. Its steps are skipped, with a note in the summary, until `tools/ConformanceCheck` lands (issue #7). |
| `fuzz-smoke` | ubuntu | `--list`s the fuzz targets and runs `--smoke <target> 60` for each; uploads `artifacts/fuzz/**` as `fuzz-findings` on failure. |
| `bench-dry` | ubuntu | `dotnet run --project bench/Broadside.Benchmarks -- --filter '*' --job dry`: one iteration of every benchmark, no measurement. |

A newer push to the same PR or branch cancels the older run. NuGet packages are cached per OS, keyed on `Directory.Packages.props` and every `.csproj`. `DiffEngine_Disabled=true` keeps Verify from launching a diff tool.

## `corpus.yml`: weekly (Monday 04:17 UTC) and the "Run workflow" button

| Job | Runs on | What it does |
|---|---|---|
| `corpus-gates` | ubuntu | Fetches every non-manual corpus with `tools/CorpusFetcher`, builds in Release, and runs `tests/Broadside.Tests` with `--filter-trait "Category=Corpus"` and `BROADSIDE_CORPUS_DIR` set: the open-and-walk gate (#47), the veraPDF agreement, the real-world font, image and colour sweeps, and the content interpreter gate (#80, one class per corpus, with its allowlist and snapshots). Uploads `artifacts/corpus-gate/*.json` (per-file counts, diagnostics, time and allocation) as `corpus-gate`. |

The corpus tests skip when `corpus/` is absent, which is why `ci.yml` cannot catch a regression in them; this workflow is where they run.

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
2. `build-macos`: installs the workloads, builds and tests the full solution, packs it, and uploads only `Broadside.Rendering.CoreGraphics` and `Broadside.Rendering.Android`, so that every package comes from exactly one build.
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
