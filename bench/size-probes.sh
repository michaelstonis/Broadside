#!/usr/bin/env bash
# Publishes the Native AOT size probes (bench/SizeProbes, issue #361) for the current runtime, runs each over its corpus file, and
# writes their sizes as JSON ({"Probes": {"<rid>": {"<probe>": bytes, ...}}}) for `BenchGate sizes`. See bench/baselines/gate/README.md,
# "Size probes".
#
#   bench/size-probes.sh [--out <file>] [--probe <name>]... [extra dotnet publish arguments]
#
# A probe's size is the size of its published executable. A probe that fails to publish (a trimmer or AOT warning is an error) or
# whose run fails makes the script exit non-zero.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/artifacts/size-probes/sizes.json"
selected=()
extra=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --out) out="$2"; shift 2 ;;
    --probe) selected+=("$2"); shift 2 ;;
    *) extra+=("$1"); shift ;;
  esac
done

# name, project folder, arguments (relative to the repository root).
probes=(
  "empty|Empty|"
  "open-metadata|OpenMetadata|tests/Corpus/info-dictionary.pdf"
  "extract-text|ExtractText|tests/Corpus/text-standard14.pdf"
  "render|Render|tests/Corpus/paths-fill-rules.pdf"
  "fill-form|FillForm|tests/Corpus/acroform-fields.pdf name"
  "save|Save|tests/Corpus/info-dictionary.pdf"
  "sign|Sign|tests/Corpus/empty-page.pdf"
)

rid="$(dotnet --info | awk -F': *' '/^ *RID:/ { print $2; exit }')"
mkdir -p "$(dirname "$out")"
json="{\n  \"Probes\": {\n    \"$rid\": {"
separator=""
for entry in "${probes[@]}"; do
  IFS='|' read -r name folder arguments <<<"$entry"
  if [[ ${#selected[@]} -gt 0 ]] && [[ ! " ${selected[*]} " == *" $name "* ]]; then
    continue
  fi

  publish="$root/artifacts/size-probes/$name"
  rm -rf "$publish"
  dotnet publish "$root/bench/SizeProbes/$folder" -c Release --use-current-runtime -o "$publish" -nologo -v quiet ${extra[@]+"${extra[@]}"}
  executable="$publish/SizeProbe.$folder"
  [[ -f "$executable.exe" ]] && executable="$executable.exe"
  # shellcheck disable=SC2086 # the arguments are a word list.
  (cd "$root" && "$executable" $arguments)
  bytes=$(wc -c <"$executable" | tr -d ' ')
  echo "$name: $bytes bytes"
  json+="$separator\n      \"$name\": $bytes"
  separator=","
done

json+="\n    }\n  }\n}"
printf '%b\n' "$json" >"$out"
echo "wrote $out"
