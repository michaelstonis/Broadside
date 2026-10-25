#!/usr/bin/env bash
# Builds the documentation site with Lunet and checks the output: every expected route exists,
# search is wired in, every package under src/ has an API reference, and every internal link and asset
# reference resolves to a generated file.
# First runs ./check-adr-refs.sh: no doc, README, site page, template or XML doc comment references an ADR.
# Modelled on wieslawsoltes/TreeDataGrid check-docs.sh; see docs/research/doc-site-tooling.md.
#
#   ./check-docs.sh                  build into site/.lunet/build/www and check it
#   ./check-docs.sh --demo <folder>  also copy a published Blazor demo (samples/Broadside.Viewer.Blazor.Sample/publish-static.sh)
#                                    into www/demo/ and check it with the rest; the site links to the live demo at demo/
#
# Without --demo, links into demo/ are not checked (the folder is only there when the docs job publishes the demo).
# Runs in CI (.github/workflows/docs.yml) on every pull request that touches the site or its inputs.
# Works with the bash 3.2 that ships with macOS (no associative arrays).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SITE="${ROOT}/site"
DOC_ROOT="${SITE}/.lunet/build/www"
# Must match site_project_basepath in site/config.scriban for the production build.
BASEPATH="/Broadside"

DEMO=""
while [ $# -gt 0 ]; do
    case "$1" in
        --demo)
            [ $# -ge 2 ] || { echo "usage: $0 [--demo <published demo folder>]" >&2; exit 2; }
            DEMO="$(cd "$2" && pwd)"
            shift 2
            ;;
        *) echo "usage: $0 [--demo <published demo folder>]" >&2; exit 2 ;;
    esac
done
if [ -n "${DEMO}" ] && [ ! -f "${DEMO}/index.html" ]; then
    echo "check-docs: ${DEMO} has no index.html: is it a published demo?" >&2
    exit 2
fi

failures=0
fail() {
    echo "check-docs: $*" >&2
    failures=$((failures + 1))
}

cd "${ROOT}"

# 0. No ADR references in user-facing text (fast, before the build).
if ! "${ROOT}/check-adr-refs.sh"; then
    fail "ADR references in user-facing text (lines above)"
fi

dotnet tool restore

# Start from a clean output so a page removed from the sources cannot survive in www/.
rm -rf "${SITE}/.lunet/build/www" "${SITE}/.lunet/build/cache/api/dotnet"

LUNET_LOG="$(mktemp)"
trap 'rm -f "${LUNET_LOG}"' EXIT

(cd "${SITE}" && dotnet tool run lunet --stacktrace build) 2>&1 | tee "${LUNET_LOG}"

# The live demo goes in demo/ after Lunet has built the site, so Lunet never processes it (and its search index leaves it out).
if [ -n "${DEMO}" ]; then
    mkdir -p "${DOC_ROOT}/demo"
    cp -R "${DEMO}/." "${DOC_ROOT}/demo/"
fi

# Lunet logs build errors (failed API extraction, template errors) and unresolved xrefs and still exits 0.
# On CI runners the level is ANSI-colored ("\e[31mERR\e[39m"), so ERR may sit between "m" and ESC.
if grep -E '(^|[[:space:]m])ERR([[:space:][:cntrl:]]|$)|Error while building api dotnet|Unable to select the api dotnet output|Unable to build api dotnet' "${LUNET_LOG}" >/dev/null; then
    fail "Lunet reported errors (lines above marked ERR)"
fi
if grep -E 'Unable to find xref' "${LUNET_LOG}" >/dev/null; then
    fail "unresolved xref links (lines above: Unable to find xref)"
fi

# 1. Expected routes.
for route in \
    index.html \
    404.html \
    articles/index.html \
    articles/getting-started/index.html \
    articles/architecture/index.html \
    articles/roadmap/index.html \
    articles/conformance/index.html \
    articles/contributing/index.html \
    articles/api-coverage/index.html \
    api/index.html \
    css/site.css \
    js/site.js \
    js/lunet-search.sqlite
do
    if [ ! -f "${DOC_ROOT}/${route}" ]; then
        fail "missing route: ${route}"
    fi
done

# 1b. The live demo: a static Blazor WebAssembly app with a relative base href, so it works under /Broadside/demo/.
if [ -n "${DEMO}" ]; then
    for route in demo/index.html demo/css/app.css demo/js/start.js; do
        [ -f "${DOC_ROOT}/${route}" ] || fail "missing demo route: ${route}"
    done
    if ! ls "${DOC_ROOT}"/demo/*/blazor.webassembly.js >/dev/null 2>&1; then
        fail "the demo has no blazor.webassembly.js in its runtime folder"
    fi
    if ! grep -q '<base href="./" */>' "${DOC_ROOT}/demo/index.html"; then
        fail "demo/index.html has no relative base href, so it would not work under ${BASEPATH}/demo/"
    fi
fi

# 2. Search: the index is generated and the theme renders the search box.
if ! grep -q 'id="search-input"' "${DOC_ROOT}/index.html"; then
    fail "index.html has no search box (is 'with search' enabled in site/config.scriban?)"
fi

# 3. Footer carries the project's MIT license, not the theme's Creative Commons default.
if ! grep -q 'MIT license' "${DOC_ROOT}/index.html"; then
    fail "index.html footer does not name the MIT license"
fi
if grep -q -E 'Creative Commons|CC BY 2\.5' "${DOC_ROOT}/index.html"; then
    fail "index.html footer still carries the theme's Creative Commons default"
fi

# 4. No source artefacts leaked into the output: raw .md links or files, /readme routes.
md_links="$(grep -r -o -h -E 'href="[^"]*\.md([?#][^"]*)?"' --include='*.html' "${DOC_ROOT}" | grep -v -E 'href="https?://' || true)"
if [ -n "${md_links}" ]; then
    fail "raw .md links in the generated HTML:"
    echo "${md_links}" | sort -u >&2
fi
if grep -r -q -E 'href="[^"]*/readme([?#"])' --include='*.html' "${DOC_ROOT}"; then
    fail "/readme routes in the generated HTML instead of directory routes"
fi
md_files="$(find "${DOC_ROOT}" -name '*.md' -print)"
if [ -n "${md_files}" ]; then
    fail "raw .md files in the output:"
    echo "${md_files}" >&2
fi

# 5. Every package under src/ has an API reference. Its public types are the type lines of its PublicAPI
#    files (a bare dotted name: members carry a signature or "->"). Each must have a generated page under
#    api/, except in a package the site build cannot compile (listed below), whose types the API coverage
#    article must document instead. The article's table must also name every package.
NOT_GENERATED="Broadside.Rendering.CoreGraphics"
COVERAGE="${DOC_ROOT}/articles/api-coverage/index.html"
packages_checked=0
types_checked=0
for project in "${ROOT}"/src/*/*.csproj; do
    package_dir="$(dirname "${project}")"
    package="$(basename "${project}" .csproj)"
    packages_checked=$((packages_checked + 1))
    if [ -f "${COVERAGE}" ] && ! grep -q "<code>${package}</code>" "${COVERAGE}"; then
        fail "package ${package} has no row in site/articles/api-coverage.md"
    fi
    types="$(cat "${package_dir}"/PublicAPI.*.txt 2>/dev/null | tr -d '\r' | grep -E '^[A-Za-z_][A-Za-z0-9_.]*$' || true)"
    if [ -z "${types}" ]; then
        fail "package ${package} has no public types in its PublicAPI files, so its API reference cannot be checked"
        continue
    fi
    generated=true
    for skipped in ${NOT_GENERATED}; do
        [ "${package}" = "${skipped}" ] && generated=false
    done
    for type in ${types}; do
        types_checked=$((types_checked + 1))
        if [ "${generated}" = true ]; then
            if [ ! -f "${DOC_ROOT}/api/${type}/index.html" ]; then
                fail "no API reference page for ${type} (package ${package}): is it listed in site/config.scriban?"
            fi
        elif [ ! -f "${COVERAGE}" ] || ! grep -q "${type##*.}" "${COVERAGE}"; then
            fail "${type} (package ${package}, not generated) is not documented in site/articles/api-coverage.md"
        fi
    done
done

# 6. Every internal href/src resolves to a file in the output. Absolute links must carry the
#    production base path (GitHub Pages serves the site under /Broadside/); relative links are
#    resolved against the directory of the page that contains them.
resolve() {
    # $1: path on disk without query or fragment. Directory routes resolve to their index.html.
    local target="$1"
    if [ -d "${target}" ]; then
        [ -f "${target%/}/index.html" ]
    else
        [ -f "${target}" ]
    fi
}

links_checked=0
demo_links_skipped=0
pairs="$(mktemp)"
# "page directory<TAB>reference", one per distinct pair. The theme's menus quote attributes with single quotes.
{
    grep -r -o -E '(href|src)="[^"]*"' --include='*.html' "${DOC_ROOT}" \
        | sed -E 's#^(.*)/[^/]*\.html:(href|src)="([^"]*)"$#\1	\3#'
    grep -r -o -E "(href|src)='[^']*'" --include='*.html' "${DOC_ROOT}" \
        | sed -E "s#^(.*)/[^/]*\\.html:(href|src)='([^']*)'\$#\\1	\\3#"
} | sort -u > "${pairs}"

while IFS="$(printf '\t')" read -r page_dir ref; do
    case "${ref}" in
        ''|'#'*|http://*|https://*|mailto:*|data:*|javascript:*|//*) continue ;;
    esac
    path="${ref%%#*}"
    path="${path%%\?*}"
    [ -z "${path}" ] && continue
    if [ -z "${DEMO}" ]; then
        case "${path}" in
            "${BASEPATH}/demo"|"${BASEPATH}/demo/"*|demo|demo/*|../demo|../demo/*|../../demo|../../demo/*)
                demo_links_skipped=$((demo_links_skipped + 1))
                continue
                ;;
        esac
    fi
    links_checked=$((links_checked + 1))
    case "${path}" in
        "${BASEPATH}"|"${BASEPATH}/"*)
            target="${DOC_ROOT}${path#"${BASEPATH}"}"
            ;;
        /*)
            fail "absolute link without the ${BASEPATH} base path: ${ref} (in ${page_dir#"${DOC_ROOT}"}/)"
            continue
            ;;
        *)
            target="${page_dir}/${path}"
            ;;
    esac
    if ! resolve "${target}"; then
        fail "broken internal link: ${ref} (in ${page_dir#"${DOC_ROOT}"}/)"
    fi
done < "${pairs}"
rm -f "${pairs}"

pages="$(find "${DOC_ROOT}" -name '*.html' | wc -l | tr -d ' ')"
if [ "${failures}" -ne 0 ]; then
    echo "check-docs: FAILED with ${failures} problem(s) in ${pages} pages" >&2
    exit 1
fi
demo_note="demo in demo/ checked"
[ -n "${DEMO}" ] || demo_note="no demo (${demo_links_skipped} links into demo/ not checked; pass --demo <folder>)"
echo "check-docs: OK, ${pages} HTML pages, ${packages_checked} packages (${types_checked} public types) covered, ${links_checked} internal references checked, ${demo_note}, output in ${DOC_ROOT#"${ROOT}/"}"
