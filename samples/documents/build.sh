#!/usr/bin/env bash
# Regenerates the representative sample documents (#363) into samples/documents/pdf/. See MANIFEST.md for what each one is, what
# made it and where every asset comes from.
#
#   samples/documents/build.sh [document...]     (no argument: all of them)
#
# Producers are development tools only; nothing here is a package dependency. Override their paths with WEASYPRINT, CHROME, QPDF,
# GS and DOTNET. The versions the committed files were made with are listed in MANIFEST.md.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
out="$here/pdf"
weasyprint="${WEASYPRINT:-weasyprint}"
qpdf="${QPDF:-qpdf}"
dotnet="${DOTNET:-dotnet}"
mkdir -p "$out"

only=("$@")
want() {
  [ ${#only[@]} -eq 0 ] && return 0
  local name
  for name in "${only[@]}"; do [ "$name" = "$1" ] && return 0; done
  return 1
}

# WeasyPrint, run from the document's folder so relative URLs (images, ../fonts) resolve.
weasy() {
  local folder="$1" source="$2" target="$3"
  shift 3
  echo "weasyprint  $folder/$source -> pdf/$target"
  (cd "$here/$folder" && "$weasyprint" --quiet "$@" "$source" "$out/$target")
}

# Broadside's own writer and flow layout (samples/documents/generator).
generator_built=false
broadside() {
  if [ "$generator_built" = false ]; then
    "$dotnet" build -c Release --nologo -v quiet "$here/generator" >/dev/null
    generator_built=true
  fi
  echo "broadside   $1 -> pdf/"
  "$dotnet" run -c Release --no-build --project "$here/generator" -- "$here" "$1" "$out"
}

want catalogue && weasy catalogue catalogue.html catalogue.pdf
want invoice-colourful && weasy invoice-colourful invoice.html invoice-colourful.pdf
want invoice-conservative && broadside invoice-conservative
if want textbook; then
  weasy textbook textbook.html textbook.pdf --pdf-tags
  echo "qpdf        pdf/textbook.pdf -> pdf/textbook-linearized.pdf"
  "$qpdf" --linearize --object-streams=generate --deterministic-id "$out/textbook.pdf" "$out/textbook-linearized.pdf"
fi

echo
echo "Sizes:"
( cd "$out" && ls -l *.pdf | awk '{ printf "  %-40s %10s bytes\n", $9, $5; total += $5 } END { printf "  %-40s %10s bytes (%.1f MB)\n", "total", total, total / 1048576 }' )
