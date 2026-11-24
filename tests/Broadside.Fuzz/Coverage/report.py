"""Writes the fuzz coverage table (docs/fuzz-coverage.md) from the Cobertura files coverage.sh collected.

Usage: python3 -I report.py <repository root> <artifacts/fuzz/coverage>

For each target: line coverage over the types tests/Broadside.Fuzz/parsers.txt maps to it (the parser scope, judged against
THRESHOLD), and line coverage of Broadside.dll plus Broadside.Rendering.dll as a whole. A target with no mapped type has no
parser scope; its row shows the assembly coverage only.
"""

import os
import re
import sys
import xml.etree.ElementTree as ElementTree

THRESHOLD = 60.0


def read_map(root):
    by_target = {}
    with open(os.path.join(root, "tests", "Broadside.Fuzz", "parsers.txt"), encoding="utf-8") as lines:
        for line in lines:
            fields = line.split()
            if len(fields) < 2 or fields[0].startswith("#"):
                continue
            for target in fields[1:]:
                by_target.setdefault(target, []).append(fields[0])
    return by_target


def normalize(name):
    """A Cobertura class name or a reflection type name as 'Namespace.Outer.Inner', generic arity and closures removed."""
    name = name.replace("/", ".").replace("+", ".")
    name = re.sub(r"`\d+", "", name)
    name = re.sub(r"<.*$", "", name)
    return name.rstrip(".")


def read_lines(path):
    """{(file, line): hit} for every coverable line, and {(file, line): class} to attribute lines to types."""
    hits = {}
    owner = {}
    for _, element in ElementTree.iterparse(path):
        if element.tag != "class":
            continue
        name = normalize(element.get("name", ""))
        filename = element.get("filename", "")
        for line in element.iter("line"):
            key = (filename, int(line.get("number")))
            hit = int(line.get("hits", "0")) > 0
            hits[key] = hits.get(key, False) or hit
            # A line belongs to the innermost type naming it; closures share their declaring type's name after normalize.
            if key not in owner or len(name) > len(owner[key]):
                owner[key] = name
        element.clear()
    return hits, owner


def belongs(type_name, class_name):
    return class_name == type_name or class_name.startswith(type_name + ".")


def percent(covered, total):
    return 100.0 * covered / total if total else 0.0


def main():
    root, out = sys.argv[1], sys.argv[2]
    mapped = read_map(root)
    targets = sorted(name for name in os.listdir(out) if os.path.isfile(os.path.join(out, name, "coverage.xml")))
    rows = []
    for target in targets:
        hits, owner = read_lines(os.path.join(out, target, "coverage.xml"))
        types = [normalize(name) for name in mapped.get(target, [])]
        scope = [key for key, name in owner.items() if any(belongs(t, name) for t in types)]
        scope_total = len(scope)
        scope_covered = sum(1 for key in scope if hits[key])
        all_covered = sum(1 for hit in hits.values() if hit)
        seeds = len(os.listdir(os.path.join(out, target, "seeds"))) if os.path.isdir(os.path.join(out, target, "seeds")) else 0
        rows.append((target, types, scope_covered, scope_total, all_covered, len(hits), seeds))

    print("| Target | Seeds | Parser scope (types) | Scope lines | Scope coverage | Verdict | Library coverage |")
    print("|---|---:|---|---:|---:|---|---:|")
    below = []
    for target, types, covered, total, all_covered, all_total, seeds in rows:
        if types:
            value = percent(covered, total)
            verdict = "meets" if value >= THRESHOLD else "below"
            if value < THRESHOLD:
                below.append(target)
            short = ", ".join(sorted({t.rsplit(".", 1)[-1] for t in types}))
            print(f"| `{target}` | {seeds} | {short} | {total} | {value:.1f}% | {verdict} | {percent(all_covered, all_total):.1f}% |")
        else:
            print(f"| `{target}` | {seeds} | (no parser mapped) | | | n/a | {percent(all_covered, all_total):.1f}% |")
    print()
    print(f"Threshold: {THRESHOLD:.0f}% line coverage of the parser scope. Below it: {', '.join(below) if below else 'none'}.")


if __name__ == "__main__":
    main()
