#!/usr/bin/env python3
"""Generates src/Broadside/Text/GlyphlessFontData.g.cs: the glyphless TrueType program of OCR text layers (issue #649).

The program has two glyphs, both empty: .notdef (glyph 0) and one blank glyph (glyph 1) that every character code of the
layer's font maps to through the CIDToGIDMap. Glyph 1 is not .notdef, so PDF/A-2 and later accept text shown with it
(ISO 19005-2 6.2.11.8). Every glyph advances 500 units of a 1000-unit em; ascent 800, descent -200. The bytes are
deterministic (fixed dates), so regenerating writes the same file.

Usage: python3 src/Broadside/Text/Data/glyphless.py
"""

import os
import struct

UNITS_PER_EM = 1000
ADVANCE = 500
ASCENT = 800
DESCENT = -200
NAME = "GlyphLessFont"


def head():
    return struct.pack(
        ">IIIIHHqqhhhhHHhhh",
        0x00010000,  # version
        0x00010000,  # fontRevision
        0,  # checkSumAdjustment, patched below
        0x5F0F3CF5,  # magicNumber
        0x000B,  # flags: baseline at y=0, lsb at x=0, integer scaling
        UNITS_PER_EM,
        0,  # created (fixed for determinism)
        0,  # modified
        0, DESCENT, ADVANCE, ASCENT,  # xMin yMin xMax yMax
        0,  # macStyle
        3,  # lowestRecPPEM
        2,  # fontDirectionHint
        0,  # indexToLocFormat: short
        0,  # glyphDataFormat
    )


def hhea():
    return struct.pack(
        ">IhhhHhhhhhh8xhH",
        0x00010000, ASCENT, DESCENT, 0, ADVANCE, 0, 0, 0, 1, 0, 0, 0, 2)


def hmtx():
    return struct.pack(">HhHh", ADVANCE, 0, ADVANCE, 0)


def maxp():
    return struct.pack(">IHHHHHHHHHHHHHH", 0x00010000, 2, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0)


def loca():
    return struct.pack(">HHH", 0, 0, 0)


def glyf():
    # Both glyphs are empty (equal loca offsets); the table still holds four zero bytes, because readers treat an empty glyf
    # table as a missing one.
    return bytes(4)


def cmap():
    # One (3, 1) format 4 subtable mapping U+0020 to glyph 1; the PDF font maps codes through its CIDToGIDMap, not this table.
    seg_count = 2
    sub = struct.pack(
        ">HHHHHHH", 4, 16 + 8 * seg_count, 0, seg_count * 2, 4, 1, 0)
    sub += struct.pack(">HH", 0x0020, 0xFFFF)  # endCode
    sub += struct.pack(">H", 0)  # reservedPad
    sub += struct.pack(">HH", 0x0020, 0xFFFF)  # startCode
    sub += struct.pack(">HH", (1 - 0x20) & 0xFFFF, 1)  # idDelta
    sub += struct.pack(">HH", 0, 0)  # idRangeOffset
    return struct.pack(">HHHHI", 0, 1, 3, 1, 12) + sub


def name():
    records = [(1, NAME), (2, "Regular"), (3, NAME), (4, NAME), (5, "Version 1.0"), (6, NAME)]
    strings = b""
    entries = b""
    for name_id, text in records:
        data = text.encode("utf-16-be")
        entries += struct.pack(">HHHHHH", 3, 1, 0x0409, name_id, len(data), len(strings))
        strings += data
    return struct.pack(">HHH", 0, len(records), 6 + 12 * len(records)) + entries + strings


def os2():
    return struct.pack(
        ">HhHHHhhhhhhhhhhh10sIIII4sHHHhhhHHIIhhHHH",
        4,  # version
        ADVANCE,  # xAvgCharWidth
        400,  # usWeightClass
        5,  # usWidthClass
        0,  # fsType: installable embedding
        650, 600, 0, 75,  # ySubscript x/y size, x/y offset
        650, 600, 0, 350,  # ySuperscript
        50, 250,  # yStrikeout size, position
        0,  # sFamilyClass
        bytes(10),  # panose
        0, 0, 0, 0,  # ulUnicodeRange1-4
        b"NONE",  # achVendID
        0x0040,  # fsSelection: REGULAR
        0x0020, 0x0020,  # usFirstCharIndex, usLastCharIndex
        ASCENT, DESCENT, 0,  # sTypoAscender, sTypoDescender, sTypoLineGap
        ASCENT, -DESCENT,  # usWinAscent, usWinDescent
        1, 0,  # ulCodePageRange1-2
        500, ASCENT,  # sxHeight, sCapHeight
        0, 0x0020, 0,  # usDefaultChar, usBreakChar, usMaxContext
    )


def post():
    return struct.pack(">IIhhIIIII", 0x00030000, 0, -100, 50, 1, 0, 0, 0, 0)


def checksum(data):
    data += b"\0" * ((4 - len(data) % 4) % 4)
    return sum(struct.unpack(">%dI" % (len(data) // 4), data)) & 0xFFFFFFFF


def build():
    tables = {
        b"OS/2": os2(),
        b"cmap": cmap(),
        b"glyf": glyf(),
        b"head": head(),
        b"hhea": hhea(),
        b"hmtx": hmtx(),
        b"loca": loca(),
        b"maxp": maxp(),
        b"name": name(),
        b"post": post(),
    }
    tags = sorted(tables)
    count = len(tags)
    power = 1
    selector = 0
    while power * 2 <= count:
        power *= 2
        selector += 1
    directory = struct.pack(">IHHHH", 0x00010000, count, power * 16, selector, count * 16 - power * 16)
    offset = 12 + 16 * count
    body = b""
    for tag in tags:
        data = tables[tag]
        directory += struct.pack(">4sIII", tag, checksum(data), offset + len(body), len(data))
        body += data + b"\0" * ((4 - len(data) % 4) % 4)
    font = bytearray(directory + body)
    adjustment = (0xB1B0AFBA - checksum(bytes(font))) & 0xFFFFFFFF
    head_offset = struct.unpack(">I", font[12 + 16 * tags.index(b"head") + 8:12 + 16 * tags.index(b"head") + 12])[0]
    font[head_offset + 8:head_offset + 12] = struct.pack(">I", adjustment)
    return bytes(font)


def main():
    font = build()
    lines = []
    for start in range(0, len(font), 16):
        lines.append("        " + " ".join("0x%02X," % b for b in font[start:start + 16]))
    source = (
        "// <auto-generated>\n"
        "// Generated by src/Broadside/Text/Data/glyphless.py. Do not edit; regenerate.\n"
        "// </auto-generated>\n"
        "\n"
        "namespace Broadside.Text;\n"
        "\n"
        "/// <summary>The glyphless TrueType program of OCR text layers: .notdef and one blank glyph, each 500 units wide.</summary>\n"
        "internal static class GlyphlessFontData\n"
        "{\n"
        "    /// <summary>The program's %d bytes.</summary>\n"
        "    public static ReadOnlySpan<byte> Program =>\n"
        "    [\n"
        "%s\n"
        "    ];\n"
        "}\n"
    ) % (len(font), "\n".join(line.rstrip() for line in lines))
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "GlyphlessFontData.g.cs")
    with open(path, "w", newline="\n") as handle:
        handle.write(source)
    print("wrote %d bytes of font program to %s" % (len(font), os.path.normpath(path)))


if __name__ == "__main__":
    main()
