#!/usr/bin/env bash
# Writes an SPDX 2.2 SBOM for a folder of packed Broadside packages (issue #265), with Microsoft's sbom-tool (a local dotnet tool,
# .config/dotnet-tools.json; it is never a package dependency).
#
#   tools/PackageCheck/generate-sbom.sh <packages dir> <version> <output .spdx.json>
#
# The files section lists every .nupkg and .snupkg with its hashes. The packages section lists what the shipping projects under src/
# resolve from NuGet, read from a restore of only those projects into a separate folder (so test, benchmark and sample dependencies
# stay out), including build-time tools such as MinVer and SourceLink. Platform targets that need a workload are not restored on a
# host without it (BroadsideBuildPlatformBackends=false), so their workload-only dependencies are not listed.
set -euo pipefail

if [ "$#" -ne 3 ]; then
  echo "usage: $0 <packages dir> <version> <output .spdx.json>" >&2
  exit 2
fi

root="$(cd "$(dirname "$0")/../.." && pwd)"
packages="$(cd "$1" && pwd)"
version="$2"
output="$3"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

mkdir -p "${work}/drop"
cp "${packages}"/*.nupkg "${work}/drop/"
cp "${packages}"/*.snupkg "${work}/drop/" 2>/dev/null || true

for project in "${root}"/src/*/*.csproj; do
  dotnet restore "${project}" -p:Configuration=Release -p:BroadsideBuildPlatformBackends=false -p:ArtifactsPath="${work}/components" --verbosity quiet
done

cd "${root}"
dotnet tool restore >/dev/null
dotnet sbom-tool generate \
  -b "${work}/drop" \
  -bc "${work}/components" \
  -pn Broadside \
  -pv "${version}" \
  -ps "Michael Stonis and contributors" \
  -nsb https://github.com/michaelstonis/Broadside \
  -V Warning </dev/null

mkdir -p "$(dirname "${output}")"
cp "${work}/drop/_manifest/spdx_2.2/manifest.spdx.json" "${output}"
echo "SBOM written to ${output}"
