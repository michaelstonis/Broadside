# PackageCheck

Supply-chain checks on packed packages (issue #265). CI runs them on every package it packs (`ci.yml` on Linux, `release.yml` on
Linux and macOS); everything here also runs locally.

```sh
# Pack as CI does (ContinuousIntegrationBuild maps source paths to /_/, which the check requires), then check every package.
dotnet pack Broadside.Core.slnf -c Release -p:ContinuousIntegrationBuild=true -p:BroadsideBuildPlatformBackends=false
dotnet run --project tools/PackageCheck -- verify artifacts/package/release            # offline
dotnet run --project tools/PackageCheck -- verify artifacts/package/release --online   # also fetch every SourceLink URL (needs the commit pushed)

# Reproducible build: clone HEAD twice into different folders, pack each, compare.
tools/PackageCheck/check-reproducible.sh [work dir]

# SPDX SBOM of a folder of packages (Microsoft's sbom-tool, a local dotnet tool).
tools/PackageCheck/generate-sbom.sh artifacts/package/release <version> out/broadside.spdx.json
```

Exit code 0 when everything passes, 1 with one `error: <package>: <message>` line per finding, 2 for a bad command line or policy.

## What `verify` checks

For every `.nupkg` in the folder, against [`package-policy.json`](package-policy.json):

| Check | Fails when |
|---|---|
| Dependencies | A dependency (any target framework group) is not on the package's allowlist. Another Broadside package is allowed only at the package's own version (every package shares one version). |
| Licence | The nuspec's licence is not the one the policy names (an SPDX expression, or `file:<path>` for the core's licence file), or a licence file it names is missing. |
| Files | A file the policy lists (third-party notices and licences) is not in the package. |
| README and icon | The nuspec has no `readme` or `icon`, or names a file that is not in the package. |
| Repository | The nuspec's `repository` URL is not the policy's, or has no full commit. |
| Symbols | A `lib/**.dll` has no `.snupkg` beside the package, the `.snupkg` has no PDB at the same path, the PDB is not portable, or its ID differs from the assembly's CodeView entry. |
| Source paths | A PDB document is not under `/_/`, i.e. the package was not built with `ContinuousIntegrationBuild=true` (which CI sets). |
| SourceLink | The PDB has no SourceLink map, a document is neither embedded nor mapped, or a mapped URL is not `https://raw.githubusercontent.com/michaelstonis/Broadside/<the nuspec's commit>/...`. With `--online`, any URL that does not answer `HEAD` with success. |

Package validation itself (`EnablePackageValidation` with `PackageValidationBaselineVersion`, set in `src/Directory.Build.props`) is not
this tool: it runs inside `dotnet pack` and fails the pack on a breaking change against the last published version.

## The policy

A package without an entry under `packages` gets `default`: Microsoft packages with no native component only (the `microsoft-managed`
set) and an MIT licence. That is the rule for the core, `Broadside.Rendering`, `Broadside.Layout` and both font packages, and for any
package added later, which therefore needs no policy change unless it is a backend or carries third-party files. A backend may add its
platform library (`Broadside.Rendering.Skia`: SkiaSharp; the other backends reach theirs through the SDK or a workload, not a package).
The viewer shells are previews outside v1.0 and may add their UI framework. Adding a package to an allowlist is a review decision: the
core's list changes only with the rule that it depends on nothing but Microsoft-shipped packages without native code.

`Every_package_the_policy_names_exists_under_src` in `tests/PackageCheck.Tests` fails when an entry names a package that no longer exists.

## `compare` and the reproducible build

`check-reproducible.sh` clones the committed HEAD into two folders with different names, sets each clone's `origin` to the real one
(SourceLink and the nuspec read it), packs `Broadside.Core.slnf` in Release with `ContinuousIntegrationBuild=true` in each, and runs
`compare`: both folders must hold the same `.nupkg`/`.snupkg` files with byte-identical entries. NuGet's OPC bookkeeping
(`_rels/.rels`, `package/services/metadata/**`, which get a fresh random ID on every pack) and zip timestamps are not compared.
In the static web asset manifests the Razor SDK packs into the WebGL backend and the Blazor viewer
(`build/Microsoft.AspNetCore.StaticWebAsset*.props`), each asset's `LastWriteTime` and `Last-Modified` header are ignored too: they
record when git checked the asset out. The assets, their sizes and their content hashes (ETags) are still compared.

`compare --except <package id>` reports a package's differences as warnings instead of failures. The script names one package:
`Broadside.Viewer.WinUI`, because CsWinRT's WinRT AOT source generator (which the WinUI viewer needs for its AOT-safe WinRT
projections) writes its generated files in a different order from one build to the next. The viewer is a preview outside v1.0; every
other package must be identical. Remove the exception when a CsWinRT update makes the generator's output order stable.

What the check has caught: the logging source generator wrote the core's two generated logging classes in a different order from one
build to the next, so the core assembly and PDB differed between builds of the same commit. The core now has one generated logging
class (`DiagnosticLog`); `ObjectLog` uses `LoggerMessage.Define` directly. Keep it that way: a second `[LoggerMessage]` class in one
assembly brings the difference back, and this check fails.

## The SBOM

`generate-sbom.sh` copies the packages into a scratch folder, restores only the projects under `src/` into a separate artifacts folder
(so test, benchmark and sample dependencies stay out) and runs `sbom-tool generate` over both: the files section lists every package
with its hashes, the packages section what those projects resolve from NuGet, build-time tools included. `release.yml`'s `publish` job
writes `broadside-<version>.spdx.json` next to the packages, so the `packages` artifact of a dry run carries it and a release attaches it.
`sbom-tool` is a local tool in `.config/dotnet-tools.json` (it targets .NET 8 and rolls forward), never a package dependency.

## Layout

- `PackagePolicy.cs`: the policy file and its rules.
- `PackageVerifier.cs`: nuspec reading and the package-level checks.
- `SymbolVerifier.cs`: assembly/PDB matching, source paths and SourceLink.
- `SourceLinkResolver.cs`: the `--online` fetch.
- `PackageComparer.cs`: `compare`.
- `Program.cs`: the command line only.
