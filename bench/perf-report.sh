#!/usr/bin/env bash
# The v1 performance comparison (issue #275): times Broadside, PDFium and pdf.js on the rendering gate's pinned real-world pages
# with one protocol, measures each engine's peak memory per file, times Broadside's WebAssembly build against pdf.js in the same
# Chromium, and writes the report. See bench/baselines/v1/README.md.
#
#   bench/perf-report.sh [--label <name>] [--out <dir>] [--iterations 10] [--dpis 72,150] [--document-dpi 150] [--no-browser]
#   bench/perf-report.sh --publish <results-dir>...     # copy result folders into bench/baselines/v1 and regenerate the site page
#
# A run writes <out> (default artifacts/perf-report/<label>): environment.json, broadside.jsonl, pdfium.jsonl, pdfjs.jsonl,
# memory.jsonl, browser.jsonl and report.md (that run alone). --publish takes one or more such folders (from this machine or the
# perf-report workflow's artifacts), copies each to bench/baselines/v1/<label>/, and writes site/articles/performance.md from every
# folder there.
#
# Needs BROADSIDE_CORPUS_DIR or the gitignored corpus/ (dotnet run --project tools/CorpusFetcher -- --only pdfjs,pdf20examples,gwg-output-suite),
# python3 and node for the references (tools/RenderDiff/acquire-references.sh runs when they are missing), and for the browser
# comparison the wasm-tools workload and Playwright's Chromium. DOTNET overrides the dotnet executable.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${DOTNET:-dotnet}"
label=""
out=""
iterations=10
dpis="72,150"
document_dpi=150
browser=true
publish=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    --label) label="$2"; shift 2 ;;
    --out) out="$2"; shift 2 ;;
    --iterations) iterations="$2"; shift 2 ;;
    --dpis) dpis="$2"; shift 2 ;;
    --document-dpi) document_dpi="$2"; shift 2 ;;
    --no-browser) browser=false; shift ;;
    --publish) shift; while [[ $# -gt 0 && "$1" != --* ]]; do publish+=("$1"); shift; done ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

perfcompare() { "$dotnet" run --project "$root/tools/PerfCompare" -c Release --no-build -- "$@"; }

if [[ ${#publish[@]} -gt 0 ]]; then
  "$dotnet" build "$root/tools/PerfCompare" -c Release -nologo -v quiet
  args=()
  for folder in "${publish[@]}"; do
    name="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["slug"])' "$folder/environment.json")"
    target="$root/bench/baselines/v1/$name"
    rm -rf "$target" && mkdir -p "$target"
    cp "$folder"/environment.json "$folder"/*.jsonl "$target/"
  done
  for folder in "$root"/bench/baselines/v1/*/; do
    [[ -f "$folder/environment.json" ]] && args+=(--results "$folder")
  done
  perfcompare report "${args[@]}" --out "$root/site/articles/performance.md"
  exit 0
fi

corpus="${BROADSIDE_CORPUS_DIR:-$root/corpus}"
[[ -d "$corpus" ]] || { echo "No real-world corpus at $corpus: set BROADSIDE_CORPUS_DIR or run tools/CorpusFetcher." >&2; exit 1; }
corpus="$(cd "$corpus" && pwd)"
pages="$root/tests/Broadside.Rendering.Tests/CorpusGate/pinned-pages.txt"
references="${BROADSIDE_REFERENCE_DIR:-$root/artifacts/reference-renderers}"

# shellcheck disable=SC1091 # the running system's release file
case "$(uname -s)" in
  Darwin) os="macOS $(sw_vers -productVersion)"; cpu="$(sysctl -n machdep.cpu.brand_string)"; cores="$(sysctl -n hw.logicalcpu)"
          memory="$(( $(sysctl -n hw.memsize) / 1073741824 )) GB"; slug_os=macos ;;
  Linux)  os="$(. /etc/os-release && echo "$PRETTY_NAME")"; cpu="$(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2 | sed 's/^ //')"
          cores="$(nproc)"; memory="$(awk '/MemTotal/ { printf "%d GB", $2 / 1048576 }' /proc/meminfo)"; slug_os=linux ;;
  *) echo "Runs on macOS and Linux." >&2; exit 1 ;;
esac
arch="$(uname -m)"
label="${label:-$os, $arch}"
slug="${slug_os}-${arch}${PERF_SLUG_SUFFIX:+-$PERF_SLUG_SUFFIX}"
out="${out:-$root/artifacts/perf-report/$slug}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
load() { uptime | sed -E 's/.*load averages?: //'; }

[[ -x "$references/pdfium/bin/python" && -d "$references/pdfjs/node_modules/pdfjs-dist" ]] || "$root/tools/RenderDiff/acquire-references.sh"
"$dotnet" build "$root/tools/PerfCompare" -c Release -nologo -v quiet
load_start="$(load)"

echo "== Broadside"
perfcompare bench --pages "$pages" --corpus "$corpus" --dpis "$dpis" --document-dpi "$document_dpi" --iterations "$iterations" --out "$out/broadside.jsonl"
echo "== PDFium"
"$references/pdfium/bin/python" -I "$root/tools/RenderDiff/reference/pdfium/bench.py" "$pages" "$corpus" "$dpis" "$iterations" "$document_dpi" > "$out/pdfium.jsonl"
echo "== pdf.js"
RENDERDIFF_PDFJS_DIR="$references/pdfjs" node "$root/tools/RenderDiff/reference/pdfjs/bench.mjs" "$pages" "$corpus" "$dpis" "$iterations" "$document_dpi" > "$out/pdfjs.jsonl"

echo "== Peak memory"
: > "$out/memory.jsonl"
memory_line() { # engine, file (empty for idle), json from the harness
  python3 -c 'import json,sys; d=json.loads(sys.argv[3]); d["engine"]=sys.argv[1]; d["file"]=sys.argv[2] or None; print(json.dumps(d))' "$1" "$2" "$3" >> "$out/memory.jsonl"
}
measure_memory() { # file relative to the corpus, or empty
  local file="$1" full=()
  [[ -n "$file" ]] && full=("$corpus/$file")
  memory_line broadside "$file" "$(perfcompare memory ${full[@]+--file "${full[0]}"} --dpi "$document_dpi")"
  memory_line pdfium "$file" "$("$references/pdfium/bin/python" -I "$root/tools/RenderDiff/reference/pdfium/bench.py" --memory ${full[@]+"${full[0]}"} ${full[@]+"$document_dpi"})"
  memory_line pdfjs "$file" "$(RENDERDIFF_PDFJS_DIR="$references/pdfjs" node "$root/tools/RenderDiff/reference/pdfjs/bench.mjs" --memory ${full[@]+"${full[0]}"} ${full[@]+"$document_dpi"})"
}
measure_memory ""
while IFS=$'\t' read -r file _; do
  [[ -z "$file" || "$file" == \#* || ! -f "$corpus/$file" ]] && continue
  measure_memory "$file"
done < "$pages"

chromium=""
if [[ "$browser" == true ]]; then
  echo "== Browser"
  app="${BROADSIDE_BLAZOR_APP:-$root/artifacts/blazor-sample}"
  [[ -f "$app/index.html" ]] || "$root/samples/Broadside.Viewer.Blazor.Sample/publish-static.sh" "$app"
  "$dotnet" build "$root/tests/Broadside.Viewer.Blazor.BrowserTests" -c Release -nologo -v quiet
  BROADSIDE_BLAZOR_APP="$app" BROADSIDE_BLAZOR_REQUIRED=1 BROADSIDE_CORPUS_DIR="$corpus" \
  BROADSIDE_PDFJS_DIST="$references/pdfjs/node_modules/pdfjs-dist" BROADSIDE_PERF_BROWSER_OUTPUT="$out/browser.jsonl" \
  BROADSIDE_PERF_DPIS="$dpis" BROADSIDE_PERF_DOCUMENT_DPI="$document_dpi" BROADSIDE_PERF_ITERATIONS="$iterations" \
  BROADSIDE_BROWSER_LOG="$out/browser.log" BROADSIDE_CROSS_ORIGIN_ISOLATED=1 \
    "$dotnet" test --project "$root/tests/Broadside.Viewer.Blazor.BrowserTests" -c Release --no-build -- --filter-class '*PerfComparisonTests'
  chromium="$(grep -m1 -o 'chromium [0-9.]*' "$out/browser.log" 2>/dev/null || true)"
fi

pypdfium="$("$references/pdfium/bin/python" -c 'import pypdfium2; print(f"pypdfium2 {pypdfium2.PYPDFIUM_INFO}, PDFium {pypdfium2.PDFIUM_INFO}")')"
pdfjs_version="$(node -p "require('$references/pdfjs/node_modules/pdfjs-dist/package.json').version")"
canvas_version="$(node -p "require('$references/pdfjs/node_modules/@napi-rs/canvas/package.json').version")"
runtime="$("$dotnet" --list-runtimes | awk '/Microsoft.NETCore.App/ { v = $2 } END { print v }')"
python3 - "$out/environment.json" <<PY
import json, sys
json.dump({
  "label": """$label""",
  "slug": "$slug",
  "Date": "$(date -u +%Y-%m-%d)",
  "Commit": "$(git -C "$root" rev-parse --short=10 HEAD)",
  "Machine": """$cpu, $cores logical cores, $memory""",
  "OS": """$os ($arch)""",
  ".NET": """$("$dotnet" --version) SDK, runtime $runtime""",
  "PDFium": """$pypdfium, Python $(python3 -c 'import platform; print(platform.python_version())')""",
  "pdf.js": "pdfjs-dist $pdfjs_version, @napi-rs/canvas $canvas_version, Node $(node --version)",
  "Browser": """${chromium:-not run}""",
  "Settings": "DPI $dpis; whole document at $document_dpi DPI; $iterations timed runs per case; one thread per engine",
  "Load average": """$load_start at the start, $(load) at the end""",
}, open(sys.argv[1], "w"), indent=2)
PY

perfcompare report --results "$out" --out "$out/report.md"
echo "Results in $out. To publish: bench/perf-report.sh --publish $out [more result folders]"
