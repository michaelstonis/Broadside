#!/usr/bin/env bash
# Measures the code coverage of each fuzz target's seed corpus (issue #274; docs/fuzz-coverage.md).
#
#   tests/Broadside.Fuzz/Coverage/coverage.sh [target...]        # every target from --list when none is given
#
# For each target: writes its seeds with --seeds (what the scheduled libFuzzer run starts from), then runs the target once over
# those seeds, its regression seeds (tests/Broadside.Fuzz/Seeds/<target>) and tests/Corpus with --corpus under coverlet (the
# local tool coverlet.console in .config/dotnet-tools.json; run `dotnet tool restore` first), which instruments Broadside.dll and
# Broadside.Rendering.dll in a copy of the build (artifacts/fuzz/coverage/bin), so the build itself is never instrumented. Then report.py writes the table: line coverage over the types tests/Broadside.Fuzz/parsers.txt maps to
# the target, against the threshold, and line coverage of the two assemblies.
#
# Before: dotnet build tests/Broadside.Fuzz -c Release. Output: artifacts/fuzz/coverage/<target>/coverage.xml and
# artifacts/fuzz/coverage/report.md. A target that fails on a seed fails the script (that is a finding). Extra PDF directories
# for the seeds (the CI seed corpora) may be given in SEED_DIRECTORIES, separated by spaces.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
harness="${root}/artifacts/bin/Broadside.Fuzz/release/Broadside.Fuzz.dll"
out="${root}/artifacts/fuzz/coverage"
cd "${root}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# coverlet instruments in place and restores afterwards; it works on a copy so a smoke run of the build is never instrumented.
copy="${out}/bin"
rm -rf "${copy}"
mkdir -p "${copy}"
cp -R "$(dirname "${harness}")/." "${copy}/"

if [ "$#" -gt 0 ]; then
  targets=("$@")
else
  targets=()
  while IFS= read -r name; do targets+=("${name}"); done < <(dotnet "${harness}" --list)
fi

# shellcheck disable=SC2206
extra=(${SEED_DIRECTORIES:-})
for target in "${targets[@]}"; do
  work="${out}/${target}"
  rm -rf "${work}"
  mkdir -p "${work}"
  dotnet "${harness}" --seeds "${target}" "${work}/seeds" ${extra[@]+"${extra[@]}"} > /dev/null
  inputs=("${work}/seeds" "tests/Corpus")
  if [ -d "tests/Broadside.Fuzz/Seeds/${target}" ]; then
    inputs+=("tests/Broadside.Fuzz/Seeds/${target}")
  fi

  started=${SECONDS}
  dotnet tool run coverlet "${copy}/Broadside.Fuzz.dll" --target dotnet \
    --targetargs "${copy}/Broadside.Fuzz.dll --corpus ${target} ${inputs[*]}" \
    --include "[Broadside]*" --include "[Broadside.Rendering]*" --format cobertura --output "${work}/coverage.xml" \
    > "${work}/log.txt" 2>&1 || { cat "${work}/log.txt"; exit 1; }
  echo "${target}: $((SECONDS - started)) s"
done

python3 -I "${root}/tests/Broadside.Fuzz/Coverage/report.py" "${root}" "${out}" > "${out}/report.md"
echo "Wrote ${out}/report.md"
