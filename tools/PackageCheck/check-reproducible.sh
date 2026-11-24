#!/usr/bin/env bash
# Reproducible-build check (issue #265): clones the current commit into two different folders, packs Broadside.Core.slnf in each
# as CI does (Release, ContinuousIntegrationBuild=true, so source paths map to /_/ and the output cannot depend on the folder),
# and fails unless every package file is byte-identical between the two (PackageCheck compare).
#
#   tools/PackageCheck/check-reproducible.sh [work dir]
#
# It builds the committed HEAD, not uncommitted changes. The work dir (default: a new temporary folder) keeps both clones and both
# package folders for inspection when the check fails.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
work="${1:-$(mktemp -d)}"
commit="$(git -C "${root}" rev-parse HEAD)"
origin="$(git -C "${root}" remote get-url origin)"
mkdir -p "${work}"
echo "Building ${commit} twice under ${work}"

for side in first second-build; do
  clone="${work}/${side}"
  rm -rf "${clone}"
  git clone --quiet --no-checkout "${root}" "${clone}"
  git -C "${clone}" checkout --quiet --detach "${commit}"
  # SourceLink and the nuspec's repository element read the origin remote: point it where the real checkout's points.
  git -C "${clone}" remote set-url origin "${origin}"
  dotnet pack "${clone}/Broadside.Core.slnf" -c Release \
    -p:ContinuousIntegrationBuild=true -p:BroadsideBuildPlatformBackends=false \
    --verbosity quiet -o "${work}/${side}-packages"
done

dotnet run --project "${root}/tools/PackageCheck" -c Release -- compare "${work}/first-packages" "${work}/second-build-packages"
