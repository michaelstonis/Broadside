#!/usr/bin/env bash
# Times PDFium and pdf.js over the rendering gate's pinned pages (issue #140), with the reference renderers
# acquire-references.sh installs. Writes artifacts/reference-bench/<engine>.jsonl (one case per line; see reference/*/bench.*).
#
# Usage: tools/RenderDiff/bench-references.sh [iterations=10] [dpis=72,150] [engines...]
# Needs BROADSIDE_CORPUS_DIR (tools/CorpusFetcher output). The Broadside side is bench/Broadside.Benchmarks'
# RealWorldRenderBenchmarks and RealWorldPageBenchmarks; docs/benchmarks/phase-3-render-baselines.md compares the two.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
reference_dir="${BROADSIDE_REFERENCE_DIR:-$repo_root/artifacts/reference-renderers}"
corpus="${BROADSIDE_CORPUS_DIR:?set BROADSIDE_CORPUS_DIR to the real-world corpus}"
pages="$repo_root/tests/Broadside.Rendering.Tests/CorpusGate/pinned-pages.txt"
out="$repo_root/artifacts/reference-bench"

iterations="${1:-10}"
dpis="${2:-72,150}"
shift $(($# > 2 ? 2 : $#))
engines=("$@")
if [ ${#engines[@]} -eq 0 ]; then
  engines=(pdfium pdfjs)
fi

mkdir -p "$out"
for engine in "${engines[@]}"; do
  case "$engine" in
    pdfium) "$reference_dir/pdfium/bin/python" -I "$script_dir/reference/pdfium/bench.py" "$pages" "$corpus" "$dpis" "$iterations" > "$out/pdfium.jsonl" ;;
    pdfjs) RENDERDIFF_PDFJS_DIR="$reference_dir/pdfjs" node "$script_dir/reference/pdfjs/bench.mjs" "$pages" "$corpus" "$dpis" "$iterations" > "$out/pdfjs.jsonl" ;;
    *) echo "Unknown engine '$engine'; expected pdfium or pdfjs." >&2; exit 2 ;;
  esac
  echo "$engine: $(wc -l < "$out/$engine.jsonl" | tr -d ' ') cases in $out/$engine.jsonl"
done
