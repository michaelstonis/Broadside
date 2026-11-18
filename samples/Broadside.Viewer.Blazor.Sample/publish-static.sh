#!/usr/bin/env bash
# Publishes the Broadside Blazor WebAssembly demo as a self-contained static site into a folder, then prints its file count, total
# size and largest file. The site works from any sub-path (relative base href) on any static host that serves the standard web media
# types (.html, .js, .css, .wasm, .pdf); no server-side code, no precompressed copies.
#
#   samples/Broadside.Viewer.Blazor.Sample/publish-static.sh [--no-aot | --hot-aot | --aot-profiler] [--runtime-folder <name>] <output-folder>
#
#   (default)                 trimmed and compiled ahead of time to WebAssembly: fastest, but the runtime module is ~21 MB.
#   --no-aot                  trimmed, no ahead-of-time compilation (the IL runs in the interpreter): ~14 MB besides the gallery documents,
#                             and no file over ~4.5 MB besides them (the largest sample PDF is 8.1 MB), for hosts with a
#                             per-file size limit. Builds in its own obj/bin folders (obj/noaot, bin/noaot):
#                             a non-AOT build over AOT intermediates produces a runtime that fails at startup.
#   --hot-aot                 compiles ahead of time only the methods a recorded viewing session ran (hot-aot*.aotprofile),
#                             optimized for size, and interprets the rest: pages draw at AOT speed with an 11.1 MiB runtime module,
#                             under a 16 MB per-file limit (issues #365, #547). Builds in obj/hotaot, bin/hotaot.
#   --aot-profiler            no ahead-of-time compilation, with Mono's AOT profiler linked in: the build that records those
#                             profiles (tests/Broadside.Viewer.Blazor.BrowserTests/AotProfileTests.cs). Builds in obj/aotprofiler,
#                             bin/aotprofiler.
#   --runtime-folder <name>   puts the runtime in <name>/ instead of _framework/ and component assets in <name>-content/ instead of
#                             _content/, for hosts that reserve paths starting with "_" (see README.md).
#
# AOT needs the wasm-tools workload (`dotnet workload install wasm-tools`). DOTNET overrides the dotnet executable.
set -euo pipefail

usage() {
  echo "usage: $0 [--no-aot | --hot-aot | --aot-profiler] [--runtime-folder <name>] <output-folder>" >&2
  exit 2
}

aot=true
folder=""
target=""
while [ $# -gt 0 ]; do
  case "$1" in
    --no-aot) aot=false; shift ;;
    --hot-aot) aot=hot; shift ;;
    --aot-profiler) aot=profiler; shift ;;
    --runtime-folder) [ $# -ge 2 ] || usage; folder="$2"; shift 2 ;;
    -*) usage ;;
    *) [ -z "$target" ] || usage; target="$1"; shift ;;
  esac
done
[ -n "$target" ] || usage

here="$(cd "$(dirname "$0")" && pwd)"
dotnet="${DOTNET:-dotnet}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

args=(-c Release -o "$work" -nologo -p:BroadsideAot="$aot")
if [ "$aot" = false ]; then
  args+=(-p:IntermediateOutputPath=obj/noaot/ -p:OutputPath=bin/noaot/)
elif [ "$aot" = hot ]; then
  args+=(-p:IntermediateOutputPath=obj/hotaot/ -p:OutputPath=bin/hotaot/)
elif [ "$aot" = profiler ]; then
  args+=(-p:IntermediateOutputPath=obj/aotprofiler/ -p:OutputPath=bin/aotprofiler/)
fi
if [ -n "$folder" ]; then
  args+=(-p:BroadsideRuntimeFolder="$folder")
fi

"$dotnet" publish "$here/Broadside.Viewer.Blazor.Sample.csproj" "${args[@]}"

# A previous publish of this site is replaced (fingerprinted file names change with every build); any other folder is only added to.
if [ -f "$target/index.html" ] && [ -f "$target/js/start.js" ]; then
  rm -rf "$target"
fi
mkdir -p "$target"
out="$(cd "$target" && pwd)"
cp -R "$work/wwwroot/." "$out/"
# Referenced component libraries bring precompressed copies of their own assets; a plain static host does not need them.
find "$out" -type f \( -name "*.br" -o -name "*.gz" \) -delete

count=$(find "$out" -type f | wc -l | tr -d ' ')
bytes=$(find "$out" -type f -exec cat {} + | wc -c | tr -d ' ')
largest=$(find "$out" -type f -exec wc -c {} + | grep -v ' total$' | sort -n | tail -1 | awk '{printf "%s (%.1f MB)", $2, $1 / 1048576}')

case "$aot" in true) kind="AOT" ;; hot) kind="AOT of the profiled methods only" ;; profiler) kind="no AOT, AOT profiler" ;; *) kind="no AOT" ;; esac
echo "Published to $out ($kind): $count files, $bytes bytes ($(awk -v b="$bytes" 'BEGIN {printf "%.1f MB", b / 1048576}'))"
echo "Largest file: ${largest#"$out"/}"
echo "Types:"
find "$out" -type f | sed -E 's/.*\.([^./]+)$/.\1/' | sort | uniq -c
