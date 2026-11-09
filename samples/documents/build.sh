#!/usr/bin/env bash
# Regenerates the representative sample documents (#363) into samples/documents/pdf/. See MANIFEST.md for what each one is, what
# made it and where every asset comes from.
#
#   samples/documents/build.sh [document...]     (no argument: all of them)
#
# Producers are development tools only; nothing here is a package dependency. Override their paths with WEASYPRINT, CHROME, QPDF,
# GS, TESSERACT, JBIG2, PYTHON (with fontTools and Pillow) and DOTNET. The versions the committed files were made with are listed in MANIFEST.md.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
out="$here/pdf"
weasyprint="${WEASYPRINT:-weasyprint}"
qpdf="${QPDF:-qpdf}"
gs="${GS:-gs}"
tesseract="${TESSERACT:-tesseract}"
jbig2="${JBIG2:-jbig2}"
if [ -z "${CHROME:-}" ]; then
  for candidate in "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" google-chrome google-chrome-stable chromium chromium-browser; do
    if command -v "$candidate" >/dev/null 2>&1 || [ -x "$candidate" ]; then CHROME="$candidate"; break; fi
  done
fi
chrome="${CHROME:-google-chrome}"
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

# Headless Chrome's print to PDF (Skia's PDF backend), with an outline from the headings and a tagged structure.
chrome_pdf() {
  local folder="$1" source="$2" target="$3"
  echo "chrome      $folder/$source -> pdf/$target"
  "$chrome" --headless --disable-gpu --no-pdf-header-footer --generate-pdf-document-outline --no-first-run \
    --user-data-dir="$(mktemp -d)" --print-to-pdf="$out/$target" "file://$here/$folder/$source" 2>/dev/null
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

want newsletter && chrome_pdf newsletter newsletter.html newsletter.pdf
if want paper; then
  chrome_pdf paper paper.html paper.pdf
  echo "gs          pdf/paper.pdf -> pdf/paper-ghostscript.pdf"
  "$gs" -q -dNOPAUSE -dBATCH -dSAFER -sDEVICE=pdfwrite -dPDFSETTINGS=/prepress -dCompatibilityLevel=1.7 \
    -sOutputFile="$out/paper-ghostscript.pdf" "$out/paper.pdf"
fi
want dictionary && weasy dictionary dictionary.html dictionary.pdf
want brochure && broadside brochure

# The novel: page labels (roman front matter, then the chapters from 1) checked against the first chapter's page.
label_novel() {
  local file="$1" front="$2"
  pdftotext -f $((front + 1)) -l $((front + 1)) "$out/$file" - | head -1 | grep -q "CHAPTER I" \
    || { echo "$file: the front matter is no longer $front pages; update build.sh" >&2; exit 1; }
  "$qpdf" --replace-input --set-page-labels 1:r $((front + 1)):D -- --object-streams=generate --deterministic-id "$out/$file"
}
if want novel; then
  weasy novel willows.html novel-pocket.pdf -s pocket.css
  label_novel novel-pocket.pdf 3
  weasy novel willows.html novel-hardback.pdf -s hardback.css --pdf-tags
  label_novel novel-hardback.pdf 6
  broadside novel-large-print
fi

if want showcase; then
  weasy showcase showcase.html .showcase-weasyprint.pdf
  broadside showcase
  rm -f "$out/.showcase-weasyprint.pdf"
fi

# Broadside: the signed agreement (its test PKI is in contract/test-pki/), then the encrypted copies of the documents above.
want contract && broadside contract
want encrypted && broadside encrypted

# Broadside: forms (and an XFA hybrid in an update), and the paper with a referee's annotations in an update.
want application-form && broadside application-form
want paper-reviewed && broadside paper-reviewed

# International text: the subset Noto fonts in fonts/ come from tools/subset_fonts.py (see MANIFEST.md), not from this script.
want leaflet && weasy leaflet leaflet.html leaflet-multilingual.pdf
want notice && broadside notice
want rashomon && broadside rashomon

# A PDF 1.2-era technical memo written byte by byte (Type 1 and Type 3 fonts, non-embedded standard 14 fonts, LZW, ASCII85,
# RunLength and ASCIIHex), and Broadside's color reference card.
if want report-1996; then
  echo "python      tools/report_1996.py -> pdf/report-1996.pdf"
  "${PYTHON:-python3}" "$here/tools/report_1996.py" "$here" "$out/report-1996.pdf" >/dev/null
fi
want color-guide && broadside color-guide

# Scans with an invisible OCR text layer: tesseract keeps the page images as they are (CCITT G4 TIFF pages become CCITTFaxDecode,
# JPEG 2000 pages JPXDecode); the JBIG2 copy swaps jbig2enc's symbol-mode pages into the G4 file. scans/ holds the prepared page
# images (tools/prepare_scans.py).
if want scans; then
  echo "tesseract   scans/memo-*.tif -> pdf/scan-memo-g4.pdf"
  ls "$here"/scans/memo-*.tif > "$out/.memo.txt"
  "$tesseract" "$out/.memo.txt" "$out/scan-memo-g4" -l eng --dpi 300 -c document_title="Memorandum on the Phillips report" pdf 2>/dev/null
  echo "jbig2       scans/memo-*.tif -> pdf/scan-memo-jbig2.pdf"
  rm -rf "$out/.jbig2" && mkdir "$out/.jbig2"
  (cd "$out/.jbig2" && "$jbig2" -s -p -b output "$here"/scans/memo-*.tif 2>/dev/null)
  broadside scan-jbig2
  echo "tesseract   scans/moon-*.jp2 -> pdf/scan-moon-memo-jpx.pdf"
  ls "$here"/scans/moon-*.jp2 > "$out/.moon.txt"
  "$tesseract" "$out/.moon.txt" "$out/scan-moon-memo-jpx" -l eng --dpi 300 -c document_title="In event of moon disaster" pdf 2>/dev/null
  rm -rf "$out/.jbig2" "$out/.memo.txt" "$out/.moon.txt"
fi

# Structure and scale (Broadside): mixed page geometry, a portfolio and PDF/A copies of other samples, the complete works of
# Shakespeare (the text is downloaded from Project Gutenberg for this step only), and an A1 contour map.
want board-pack && broadside board-pack
want archival && broadside archival
if want shakespeare; then
  echo "curl        gutenberg.org eBook #100 -> pdf/.pg100.txt"
  curl -sSfL -o "$out/.pg100.txt" https://www.gutenberg.org/cache/epub/100/pg100.txt
  broadside shakespeare
  rm -f "$out/.pg100.txt"
fi
want contour-map && broadside contour-map

# Damaged copies of showcase.pdf and novel-large-print.pdf (after both are built).
if want damaged; then
  echo "python      tools/damage.py -> pdf/showcase-broken-xref.pdf, pdf/novel-large-print-truncated.pdf"
  "${PYTHON:-python3}" "$here/tools/damage.py" "$out" >/dev/null
fi

echo
echo "Sizes:"
( cd "$out" && ls -l *.pdf | awk '{ printf "  %-40s %10s bytes\n", $9, $5; total += $5 } END { printf "  %-40s %10s bytes (%.1f MB)\n", "total", total, total / 1048576 }' )
