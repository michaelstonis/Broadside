#!/usr/bin/env bash
# Installs the reference renderers RenderDiff compares Broadside with (issue #137). Nothing it installs is committed: everything
# lands in the reference directory, by default artifacts/reference-renderers (gitignored), or $BROADSIDE_REFERENCE_DIR.
#
#   tools/RenderDiff/acquire-references.sh            # PDFium and pdf.js
#   tools/RenderDiff/acquire-references.sh pdfium     # one of them
#
# PDFium:  pypdfium2 (a PDFium build in a Python wheel), pinned with hashes in reference/pdfium/requirements.txt, into a venv.
#          Needs python3 (3.9 or later) with the venv module.
# pdf.js:  pdfjs-dist and @napi-rs/canvas, pinned by reference/pdfjs/package-lock.json, installed with npm ci --ignore-scripts.
#          Needs node 20 or later and npm.
# poppler: not installed here; RenderDiff uses pdftoppm from PATH when present (apt install poppler-utils, brew install poppler).
#
# Re-running reinstalls. Runs on Linux and macOS.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
reference_dir="${BROADSIDE_REFERENCE_DIR:-$repo_root/artifacts/reference-renderers}"

engines=("$@")
if [ ${#engines[@]} -eq 0 ]; then
  engines=(pdfium pdfjs)
fi

install_pdfium() {
  local target="$reference_dir/pdfium"
  command -v python3 >/dev/null || { echo "pdfium: python3 is not on PATH" >&2; return 1; }
  rm -rf "$target"
  python3 -m venv "$target"
  "$target/bin/python" -m pip install --quiet --disable-pip-version-check --no-deps --only-binary :all: \
    --require-hashes -r "$script_dir/reference/pdfium/requirements.txt"
  echo "pdfium: $("$target/bin/python" -c 'import pypdfium2; print(pypdfium2.PYPDFIUM_INFO, "with PDFium", pypdfium2.PDFIUM_INFO)') in $target"
}

install_pdfjs() {
  local target="$reference_dir/pdfjs"
  command -v node >/dev/null || { echo "pdfjs: node is not on PATH" >&2; return 1; }
  command -v npm >/dev/null || { echo "pdfjs: npm is not on PATH" >&2; return 1; }
  rm -rf "$target"
  mkdir -p "$target"
  cp "$script_dir/reference/pdfjs/package.json" "$script_dir/reference/pdfjs/package-lock.json" "$target/"
  (cd "$target" && npm ci --ignore-scripts --no-audit --no-fund --loglevel=error)
  echo "pdfjs: pdfjs-dist $(node -p "require('$target/node_modules/pdfjs-dist/package.json').version") in $target"
}

mkdir -p "$reference_dir"
for engine in "${engines[@]}"; do
  case "$engine" in
    pdfium) install_pdfium ;;
    pdfjs) install_pdfjs ;;
    *) echo "Unknown engine '$engine'; expected pdfium or pdfjs." >&2; exit 2 ;;
  esac
done
