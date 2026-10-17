#!/usr/bin/env bash
# Publishes the Blazor WebAssembly viewer sample (trimmed, AOT-compiled) as a self-contained static site into a folder, then prints
# its file count and total size. The site works from any sub-path (relative base href) on any static host that serves the standard
# web media types (.html, .js, .css, .wasm, .pdf); no server-side code, no precompressed copies.
#
#   samples/Broadside.Viewer.Blazor.Sample/publish-static.sh <output-folder>
#
# Needs the wasm-tools workload (`dotnet workload install wasm-tools`). DOTNET overrides the dotnet executable.
set -euo pipefail

if [ $# -ne 1 ]; then
  echo "usage: $0 <output-folder>" >&2
  exit 2
fi

here="$(cd "$(dirname "$0")" && pwd)"
dotnet="${DOTNET:-dotnet}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

"$dotnet" publish "$here/Broadside.Viewer.Blazor.Sample.csproj" -c Release -o "$work" -nologo

mkdir -p "$1"
out="$(cd "$1" && pwd)"
cp -R "$work/wwwroot/." "$out/"
# Referenced component libraries bring precompressed copies of their own assets; a plain static host does not need them.
find "$out" -type f \( -name "*.br" -o -name "*.gz" \) -delete

count=$(find "$out" -type f | wc -l | tr -d ' ')
bytes=$(find "$out" -type f -exec cat {} + | wc -c | tr -d ' ')

echo "Published to $out: $count files, $bytes bytes ($(awk -v b="$bytes" 'BEGIN {printf "%.1f MB", b / 1048576}'))"
echo "Types:"
find "$out" -type f | sed -E 's/.*\.([^./]+)$/.\1/' | sort | uniq -c
