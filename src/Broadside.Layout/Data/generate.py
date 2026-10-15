#!/usr/bin/env python3
"""Generates the Unicode line-breaking data of Broadside.Layout from the Unicode Character Database.

Run with ``python3 -I src/Broadside.Layout/Data/generate.py <ucd-dir>`` (stdlib only), where ``<ucd-dir>`` holds the
Unicode 16.0.0 files below, downloaded unmodified. The script checks their SHA-256 before reading them. The output is
deterministic: re-running it on the same inputs reproduces both outputs byte for byte, so a reviewer can regenerate and
``git diff`` them.

Unicode 16.0.0 is the version the .NET 10 base class library implements (``CharUnicodeInfo``), so line breaking and the
BCL agree on which code points are assigned. Bump the version and the hashes together, then regenerate.

Inputs (https://www.unicode.org/Public/16.0.0/, licence: Unicode License v3, https://www.unicode.org/terms_of_use.html):

* ``ucd/LineBreak.txt``: the Line_Break property (UAX #14).
* ``ucd/EastAsianWidth.txt``: East_Asian_Width, for ``$EastAsian`` (F, W, H) in rules LB19a, LB21a and LB30.
* ``ucd/emoji/emoji-data.txt``: Extended_Pictographic, for rule LB30b.
* ``ucd/extracted/DerivedGeneralCategory.txt``: General_Category, for LB1 (SA resolves to CM when Mn or Mc), LB15a/LB15b
  (QU split by Pi and Pf) and LB30b (Cn).
* ``ucd/auxiliary/LineBreakTest.txt``: the conformance test of the default algorithm.

Outputs:

* ``src/Broadside.Layout/LineBreakData.g.cs``: a two-stage table, one byte per code point. The low six bits are the line
  break class after LB1 (AI, SG and XX resolve to AL; SA to CM or AL by General_Category; CJ to NS) with QU split into
  QU, QU_Pi and QU_Pf; bit 6 is East_Asian_Width F, W or H; bit 7 is Extended_Pictographic and General_Category Cn.
* ``tests/Broadside.Layout.Tests/Data/LineBreakTest-16.0.0.txt.gz``: the test file with its header kept and the
  per-line comments removed (a modification of the file: the comments only name the characters and rules), gzipped.
"""

from __future__ import annotations

import gzip
import hashlib
import io
import os
import sys

VERSION = "16.0.0"
HASHES = {
    "LineBreak.txt": "e97e4259d0d20fab150b9c7b4b28abfae5cd78ca97e7f4ac6ed20d685d5f4a7c",
    "EastAsianWidth.txt": "43adc76c0686a42cb370764eb8cfe2b2a45b10b855e5572a2db4a0eecce15d5b",
    "emoji-data.txt": "f1365a5173eee18e1f98b240cdc492e84a25f1ce7e0c9d1094eb29c41a22696a",
    "DerivedGeneralCategory.txt": "7676ab755a41ef82108460238569e60ad65c191ddafe61b36c6765ec1353f293",
    "LineBreakTest.txt": "910759a611a479f37df4f535d2e64d7589be3c5ac5f7491cf1fb4fa4cdb211e9",
}

# The resolved classes, in the order of the C# enum LineBreakClass (values 0..).
CLASSES = [
    "BK", "CR", "LF", "NL", "SP", "ZW", "ZWJ", "CM", "WJ", "GL", "BA", "BB", "B2", "HY", "CB", "CL", "CP", "EX", "IN",
    "NS", "OP", "QU", "QU_Pi", "QU_Pf", "IS", "NU", "PO", "PR", "SY", "AK", "AL", "AP", "AS", "EB", "EM", "H2", "H3",
    "HL", "ID", "JL", "JV", "JT", "RI", "VF", "VI",
]
EAST_ASIAN = 0x40
PICTOGRAPHIC_UNASSIGNED = 0x80
LIMIT = 0x110000
SHIFT = 7

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..", ".."))


def read(directory: str, name: str) -> bytes:
    data = open(os.path.join(directory, name), "rb").read()
    digest = hashlib.sha256(data).hexdigest()
    if digest != HASHES[name]:
        sys.exit(f"{name}: SHA-256 {digest} is not the pinned Unicode {VERSION} file")
    return data


def ranges(data: bytes):
    for raw in data.decode("utf-8").splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        fields = [field.strip() for field in line.split(";")]
        first, _, last = fields[0].partition("..")
        yield int(first, 16), int(last or first, 16), fields[1]


def missing(data: bytes) -> str:
    for line in data.decode("utf-8").splitlines():
        if line.startswith("# @missing: 0000..10FFFF;"):
            return line.split(";")[1].strip()
    sys.exit("no @missing default")


def main() -> None:
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    directory = sys.argv[1]
    line_break_data = read(directory, "LineBreak.txt")
    width_data = read(directory, "EastAsianWidth.txt")
    emoji_data = read(directory, "emoji-data.txt")
    category_data = read(directory, "DerivedGeneralCategory.txt")
    test_data = read(directory, "LineBreakTest.txt")

    line_break = [missing(line_break_data)] * LIMIT
    for first, last, value in ranges(line_break_data):
        line_break[first:last + 1] = [value] * (last - first + 1)

    category = ["Cn"] * LIMIT
    for first, last, value in ranges(category_data):
        category[first:last + 1] = [value] * (last - first + 1)

    wide = bytearray(LIMIT)
    for first, last, value in ranges(width_data):
        if value in ("F", "W", "H"):
            wide[first:last + 1] = b"\x01" * (last - first + 1)

    pictographic = bytearray(LIMIT)
    for first, last, value in ranges(emoji_data):
        if value == "Extended_Pictographic":
            pictographic[first:last + 1] = b"\x01" * (last - first + 1)

    index = {name: number for number, name in enumerate(CLASSES)}
    values = bytearray(LIMIT)
    for code in range(LIMIT):
        value = line_break[code]
        general = category[code]
        if value in ("AI", "SG", "XX"):
            value = "AL"
        elif value == "SA":
            value = "CM" if general in ("Mn", "Mc") else "AL"
        elif value == "CJ":
            value = "NS"
        elif value == "QU" and general in ("Pi", "Pf"):
            value = "QU_" + general
        byte = index[value]
        if wide[code]:
            byte |= EAST_ASIAN
        if pictographic[code] and general == "Cn":
            byte |= PICTOGRAPHIC_UNASSIGNED
        values[code] = byte

    block = 1 << SHIFT
    blocks: dict[bytes, int] = {}
    stage1: list[int] = []
    stage2 = bytearray()
    for start in range(0, LIMIT, block):
        chunk = bytes(values[start:start + block])
        if chunk not in blocks:
            blocks[chunk] = len(blocks)
            stage2 += chunk
        stage1.append(blocks[chunk])
    if len(blocks) > 0xFF:
        sys.exit("stage 2 has more blocks than a byte indexes: raise SHIFT")

    write_table(stage1, stage2)
    write_test(test_data)


def numbers(values, per_line: int) -> str:
    lines = []
    for start in range(0, len(values), per_line):
        lines.append("        " + ",".join(str(value) for value in values[start:start + per_line]) + ",")
    return "\n".join(lines)


def write_table(stage1: list[int], stage2: bytearray) -> None:
    enum = "\n".join(f"    {name.replace('_', '')} = {number}," for number, name in enumerate(CLASSES))
    text = f"""// <auto-generated>
// Generated by src/Broadside.Layout/Data/generate.py from the Unicode Character Database {VERSION} (LineBreak.txt,
// EastAsianWidth.txt, emoji-data.txt, DerivedGeneralCategory.txt). Do not edit; regenerate.
// Unicode data: Copyright (c) 1991-2024 Unicode, Inc. Unicode License v3, https://www.unicode.org/terms_of_use.html
// </auto-generated>

namespace Broadside.Layout.Flow;

/// <summary>Line_Break classes after rule LB1 of UAX #14 (Unicode {VERSION}), with QU split by General_Category Pi and Pf.</summary>
internal enum LineBreakClass : byte
{{
{enum}
}}

/// <summary>The line-breaking properties of every code point, Unicode {VERSION}.</summary>
internal static partial class LineBreakData
{{
    /// <summary>The Unicode version of the data.</summary>
    public const string UnicodeVersion = "{VERSION}";

    /// <summary>Set when East_Asian_Width is F, W or H (<c>$EastAsian</c> in UAX #14).</summary>
    public const byte EastAsian = {EAST_ASIAN};

    /// <summary>Set when the code point is Extended_Pictographic and unassigned (rule LB30b).</summary>
    public const byte PictographicUnassigned = {PICTOGRAPHIC_UNASSIGNED};

    /// <summary>The mask of the class in a table value.</summary>
    public const byte ClassMask = 0x3F;

    private const int Shift = {SHIFT};

    /// <summary>The class and flags of <paramref name="codePoint"/> (0 to U+10FFFF).</summary>
    public static byte Lookup(int codePoint) =>
        Stage2[(Stage1[codePoint >> Shift] << Shift) | (codePoint & ((1 << Shift) - 1))];

    private static ReadOnlySpan<byte> Stage1 =>
    [
{numbers(stage1, 64)}
    ];

    private static ReadOnlySpan<byte> Stage2 =>
    [
{numbers(stage2, 64)}
    ];
}}
"""
    path = os.path.join(ROOT, "src", "Broadside.Layout", "LineBreakData.g.cs")
    with open(path, "w", encoding="utf-8", newline="\n") as file:
        file.write(text)
    print(f"{path}: stage 1 {len(stage1)} entries, stage 2 {len(stage2)} bytes")


def write_test(data: bytes) -> None:
    out = io.StringIO()
    for line in data.decode("utf-8").splitlines():
        if line.startswith("#"):
            out.write(line + "\n")
            continue
        body = line.split("#", 1)[0].strip()
        if body:
            out.write(body + "\n")
    path = os.path.join(ROOT, "tests", "Broadside.Layout.Tests", "Data", f"LineBreakTest-{VERSION}.txt.gz")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    buffer = io.BytesIO()
    with gzip.GzipFile(filename="", mode="wb", fileobj=buffer, compresslevel=9, mtime=0) as file:
        file.write(out.getvalue().encode("utf-8"))
    with open(path, "wb") as file:
        file.write(buffer.getvalue())
    print(f"{path}: {len(buffer.getvalue())} bytes")


if __name__ == "__main__":
    main()
