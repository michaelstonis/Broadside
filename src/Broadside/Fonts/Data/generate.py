#!/usr/bin/env python3
"""Generates the font data tables of the core from the files in this directory.

Run with ``python3 -I src/Broadside/Fonts/Data/generate.py`` (stdlib only). The output is deterministic:
re-running it on unchanged inputs reproduces the three files byte for byte, so a reviewer can regenerate and
``git diff`` them.

Inputs (all checked in next to this script):

* ``Afm/*.afm`` and ``Afm/MustRead.html``: the Adobe Core 14 AFM files, unmodified, from Apache PDFBox
  (https://github.com/apache/pdfbox, commit 8712d47b075511a4c5a2829c3713ba2d538999bb,
  pdfbox/src/main/resources/org/apache/pdfbox/resources/afm/). Licence: MustRead.html (SPDX APAFML).
* ``GlyphList/glyphlist.txt``, ``GlyphList/zapfdingbats.txt`` and ``GlyphList/LICENSE.md``: the Adobe Glyph
  List 2.0 and the ITC Zapf Dingbats Glyph List, unmodified, from https://github.com/adobe-type-tools/agl-aglfn
  (commit 4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6). Licence: BSD-3-Clause (LICENSE.md).
* ``encodings.txt``: StandardEncoding, WinAnsiEncoding, MacRomanEncoding and MacExpertEncoding as ISO 32000-2
  Annex D (Tables D.2 and D.4, with the notes to Table D.2 applied) and the Mac OS Roman additions of Table 113.

Outputs (in ``src/Broadside/Fonts/``):

* ``GlyphNameTable.g.cs``: every glyph name the built-in encodings and the AFM files use, and the encodings as
  code -> name index tables. The Symbol and ZapfDingbats built-in encodings come from the AFM ``C`` codes.
* ``Standard14Data.g.cs``: per-font metrics and per-glyph widths of the 14 fonts (a modification of the AFM files:
  converted to C# tables, kerning, ligature and bounding-box data removed), and, per character set, the glyphs by
  Unicode value (through the glyph lists) for resolving glyph names the AFM files do not use.
* ``AdobeGlyphList.g.cs``: the two glyph lists as name -> Unicode tables.
"""

from __future__ import annotations

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.dirname(HERE)

# Order is the numeric value of the public Standard14Font enum.
FONTS = [
    ("Courier", "Courier"),
    ("CourierBold", "Courier-Bold"),
    ("CourierOblique", "Courier-Oblique"),
    ("CourierBoldOblique", "Courier-BoldOblique"),
    ("Helvetica", "Helvetica"),
    ("HelveticaBold", "Helvetica-Bold"),
    ("HelveticaOblique", "Helvetica-Oblique"),
    ("HelveticaBoldOblique", "Helvetica-BoldOblique"),
    ("TimesRoman", "Times-Roman"),
    ("TimesBold", "Times-Bold"),
    ("TimesItalic", "Times-Italic"),
    ("TimesBoldItalic", "Times-BoldItalic"),
    ("Symbol", "Symbol"),
    ("ZapfDingbats", "ZapfDingbats"),
]

# ISO 32000-2 §9.8.2 Table 121 bit values.
FIXED_PITCH, SERIF, SYMBOLIC, NONSYMBOLIC, ITALIC = 1, 2, 4, 32, 64


class Afm:
    """The parts of an AFM file (Adobe TN 5004) the tables need: global keys and C/WX/N of each glyph."""

    def __init__(self, path: str) -> None:
        self.keys: dict[str, str] = {}
        self.comments: list[str] = []
        self.glyphs: list[tuple[int, int, str]] = []  # (code or -1, width, name)
        in_metrics = False
        with open(path, encoding="ascii") as fh:
            for raw in fh:
                line = raw.strip()
                if not line:
                    continue
                key, _, value = line.partition(" ")
                if key == "StartCharMetrics":
                    in_metrics = True
                elif key == "EndCharMetrics":
                    in_metrics = False
                elif in_metrics:
                    fields = {}
                    for part in line.split(";"):
                        part = part.strip()
                        if part:
                            k, _, v = part.partition(" ")
                            fields.setdefault(k, v.strip())
                    self.glyphs.append((int(fields["C"]), int(fields["WX"]), fields["N"]))
                elif key == "Comment":
                    self.comments.append(value.strip())
                elif key not in self.keys:
                    self.keys[key] = value.strip()

    def number(self, key: str) -> float | None:
        return float(self.keys[key]) if key in self.keys else None


def read_glyph_list(path: str) -> list[tuple[str, str]]:
    entries = []
    with open(path, encoding="ascii") as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            name, value = line.split(";")
            entries.append((name, "".join(chr(int(v, 16)) for v in value.split())))
    entries.sort(key=lambda e: e[0].encode("ascii"))
    names = [n for n, _ in entries]
    assert len(set(names)) == len(names), "duplicate glyph list names"
    return entries


def read_encodings(path: str) -> dict[str, list[str | None]]:
    tables: dict[str, list[str | None]] = {}
    current: list[str | None] | None = None
    with open(path, encoding="ascii") as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            if line.startswith("["):
                current = [None] * 256
                tables[line[1:-1]] = current
                continue
            code, name = line.split()
            assert current is not None and current[int(code, 8)] is None
            current[int(code, 8)] = name
    return tables


def cs_string(value: str) -> str:
    out = []
    for ch in value:
        if ch in "\"\\" or not (0x20 <= ord(ch) < 0x7F):
            out.append("\\u%04X" % ord(ch))
        else:
            out.append(ch)
    return '"' + "".join(out) + '"'


def wrap(items: list[str], indent: str, width: int = 120) -> list[str]:
    lines, current = [], indent
    for item in items:
        piece = item + ","
        if len(current) + len(piece) + 1 > width and current.strip():
            lines.append(current.rstrip())
            current = indent
        current += piece + " "
    if current.strip():
        lines.append(current.rstrip())
    return lines


def span(name: str, kind: str, values: list[int], doc: str) -> list[str]:
    # A span over a static array, not a "static ReadOnlySpan<T> X => [...]" property: for element types wider than a byte that
    # compiles to RuntimeHelpers.CreateSpan, which allocates on every access in unoptimized (Debug, tier-0) code.
    return ([f"    /// <summary>{doc}</summary>", f"    internal static ReadOnlySpan<{kind}> {name} => {name}Data;", "",
             f"    private static readonly {kind}[] {name}Data =", "    ["]
            + wrap([str(v) for v in values], "        ") + ["    ];", ""])


def string_data(name: str, value: str, doc: str) -> list[str]:
    chunks, i = [], 0
    while i < len(value):
        chunks.append(value[i:i + 80])
        i += 80
    lines = [f"    /// <summary>{doc}</summary>", f"    private const string {name} ="]
    for index, chunk in enumerate(chunks):
        lines.append("        " + cs_string(chunk) + (";" if index == len(chunks) - 1 else " +"))
    lines.append("")
    return lines


def header(notices: list[str]) -> list[str]:
    lines = ["// <auto-generated>", "// Generated by src/Broadside/Fonts/Data/generate.py. Do not edit; edit the generator or its inputs and re-run it."]
    for notice in notices:
        lines.append("//")
        lines.extend("// " + n if n else "//" for n in notice.split("\n"))
    lines += ["// </auto-generated>", "", "namespace Broadside.Fonts;", ""]
    return lines


AFM_NOTICE = (
    "Derived from the Adobe Core 14 AFM files (Apache PDFBox commit 8712d47b075511a4c5a2829c3713ba2d538999bb,\n"
    "pdfbox/src/main/resources/org/apache/pdfbox/resources/afm/), which carry this notice (MustRead.html):\n"
    "\n"
    "This file and the 14 PostScript(R) AFM files it accompanies may be used, copied, and distributed for any\n"
    "purpose and without charge, with or without modification, provided that all copyright notices are retained;\n"
    "that the AFM files are not distributed without this file; that all modifications to this file or any of the\n"
    "AFM files are prominently noted in the modified file(s); and that this paragraph is not modified. Adobe\n"
    "Systems has no responsibility or obligation to support the use of the AFM files.\n"
    "\n"
    "MODIFIED: the AFM files were converted to C# tables; kerning, ligature and glyph bounding-box data were\n"
    "removed. The unmodified AFM files and MustRead.html are in src/Broadside/Fonts/Data/Afm/ and ship in the\n"
    "package under licenses/adobe-core14-afm/.")

AGL_NOTICE = (
    "Derived from the Adobe Glyph List and the ITC Zapf Dingbats Glyph List (https://github.com/adobe-type-tools/agl-aglfn,\n"
    "commit 4036a9ca80a62f64f9de4f7321a9a045ad0ecfd6), converted to C# tables.\n"
    "\n"
    "Copyright 2002-2019 Adobe (http://www.adobe.com/).\n"
    "\n"
    "Redistribution and use in source and binary forms, with or without modification, are permitted provided that\n"
    "the following conditions are met:\n"
    "\n"
    "Redistributions of source code must retain the above copyright notice, this list of conditions and the\n"
    "following disclaimer.\n"
    "\n"
    "Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the\n"
    "following disclaimer in the documentation and/or other materials provided with the distribution.\n"
    "\n"
    "Neither the name of Adobe nor the names of its contributors may be used to endorse or promote products derived\n"
    "from this software without specific prior written permission.\n"
    "\n"
    "THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS \"AS IS\" AND ANY EXPRESS OR IMPLIED\n"
    "WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A\n"
    "PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY\n"
    "DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,\n"
    "PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER\n"
    "CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR\n"
    "OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.")


def write(name: str, lines: list[str]) -> None:
    with open(os.path.join(OUT, name), "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines).rstrip("\n") + "\n")
    print("wrote", name)


def main() -> int:
    afms = [Afm(os.path.join(HERE, "Afm", file + ".afm")) for _, file in FONTS]
    for (_, file), afm in zip(FONTS, afms):
        assert afm.keys["FontName"] == file, file
    agl = read_glyph_list(os.path.join(HERE, "GlyphList", "glyphlist.txt"))
    zapf_list = read_glyph_list(os.path.join(HERE, "GlyphList", "zapfdingbats.txt"))
    encodings = read_encodings(os.path.join(HERE, "encodings.txt"))

    # The built-in encodings of the two symbolic fonts are their AFM C codes (Annex D.5, D.6).
    for label, afm in (("SymbolEncoding", afms[12]), ("ZapfDingbatsEncoding", afms[13])):
        table: list[str | None] = [None] * 256
        for code, _, name in afm.glyphs:
            if code >= 0:
                table[code] = name
        encodings[label] = table
    # StandardEncoding is the C codes of every Latin AFM (Annex D.2 STD column).
    for afm in afms[:12]:
        latin = [None] * 256
        for code, _, name in afm.glyphs:
            if code >= 0:
                latin[code] = name
        assert latin == encodings["StandardEncoding"], afm.keys["FontName"]
    # Mac OS Roman (§9.6.5.4 Table 113) = MacRomanEncoding plus the additions, Euro replacing currency.
    mac_os = list(encodings["MacRomanEncoding"])
    for code, name in enumerate(encodings.pop("MacOSRomanAdditions")):
        if name:
            mac_os[code] = name
    encodings["MacOSRomanEncoding"] = mac_os

    names = set()
    for afm in afms:
        names.update(name for _, _, name in afm.glyphs)
    for table in encodings.values():
        names.update(n for n in table if n)
    names = sorted(names, key=lambda n: n.encode("ascii"))
    index = {n: i for i, n in enumerate(names)}

    # GlyphNameTable.g.cs
    lines = header([AFM_NOTICE])
    lines += [
        "/// <summary>Glyph names the built-in encodings and the Standard 14 metrics use, and the built-in encodings as name indexes.</summary>",
        "/// <remarks>ISO 32000-2 Annex D (Tables D.2, D.4, D.5, D.6) and §9.6.5.4 Table 113; PostScript Language Reference Appendix E.7 (ISOLatin1Encoding).</remarks>",
        "internal static partial class GlyphNameTable",
        "{",
        "    /// <summary>Every glyph name, in ordinal order.</summary>",
        "    internal static readonly string[] Names =",
        "    [",
    ]
    lines += wrap([cs_string(n) for n in names], "        ")
    lines += ["    ];", ""]
    order = ["StandardEncoding", "WinAnsiEncoding", "MacRomanEncoding", "MacExpertEncoding", "SymbolEncoding",
             "ZapfDingbatsEncoding", "MacOSRomanEncoding", "ISOLatin1Encoding"]
    for label in order:
        table = encodings[label]
        lines += span(label, "short", [index[n] if n else -1 for n in table],
                      f"{label}: for each code, the index of its glyph name in <see cref=\"Names\"/>, or -1.")
    lines[-1:] = ["}"]
    write("GlyphNameTable.g.cs", lines)

    # Standard14Data.g.cs
    widths = []
    for afm in afms:
        row = [-1] * len(names)
        for _, wx, name in afm.glyphs:
            row[index[name]] = wx
        widths.extend(row)
    metrics = []
    for (_, file), afm in zip(FONTS, afms):
        bbox = [int(v) for v in afm.keys["FontBBox"].split()]
        italic = afm.number("ItalicAngle") or 0.0
        fixed = afm.keys["IsFixedPitch"] == "true"
        symbolic = afm.keys["EncodingScheme"] == "FontSpecific"
        serif = file.startswith("Times")
        flags = (FIXED_PITCH if fixed else 0) | (SERIF if serif else 0) | (SYMBOLIC if symbolic else NONSYMBOLIC) \
            | (ITALIC if italic != 0 else 0)
        # Symbol and ZapfDingbats have no CapHeight, XHeight, Ascender or Descender: the font box stands in for
        # Ascent, Descent and CapHeight, and XHeight is 0 (the §9.8.1 default).
        cap = afm.number("CapHeight") if "CapHeight" in afm.keys else bbox[3]
        xh = afm.number("XHeight") or 0
        asc = afm.number("Ascender") if "Ascender" in afm.keys else bbox[3]
        desc = afm.number("Descender") if "Descender" in afm.keys else bbox[1]
        metrics.extend(bbox + [int(round(italic * 10)), int(cap), int(xh), int(asc), int(desc),
                               int(afm.number("StdVW")), int(afm.number("StdHW")), flags])

    notices = [AFM_NOTICE]
    for (_, file), afm in zip(FONTS, afms):
        copyright_lines = [c for c in afm.comments if c.startswith("Copyright")]
        notices.append(f"{file}.afm:\n" + "\n".join(copyright_lines + ["Notice " + afm.keys["Notice"]]))
    notices.append(AGL_NOTICE)
    lines = header(notices)
    lines += [
        "/// <summary>Metrics of the Standard 14 fonts, from the Adobe Core 14 AFM files.</summary>",
        "/// <remarks>ISO 32000-2 §9.6.2.2; Adobe Technical Note #5004.</remarks>",
        "internal static partial class Standard14Data",
        "{",
        f"    /// <summary>The number of glyph names in <see cref=\"GlyphNameTable.Names\"/>; the stride of <see cref=\"Widths\"/>.</summary>",
        f"    internal const int NameCount = {len(names)};",
        "",
        "    /// <summary>The number of values per font in <see cref=\"FontMetrics\"/>.</summary>",
        "    internal const int MetricsStride = 12;",
        "",
    ]
    lines += span("Widths", "short", widths,
                  "For each font (in <see cref=\"Standard14Font\"/> order) and each glyph name index, the AFM WX width, or -1 when the font has no such glyph.")
    lines += span("FontMetrics", "short", metrics,
                  "For each font: FontBBox (4 values), ItalicAngle x 10, CapHeight, XHeight, Ascent, Descent, StdVW, StdHW and the ISO 32000-2 Table 121 flags.")

    def by_unicode(glyph_names: set[str], glyph_list: list[tuple[str, str]]) -> tuple[list[int], list[int]]:
        lookup = dict(glyph_list)
        pairs = {}
        for name in sorted(glyph_names, key=lambda n: n.encode("ascii")):
            value = lookup.get(name)
            if value and len(value) == 1 and ord(value) not in pairs:
                pairs[ord(value)] = index[name]
        keys = sorted(pairs)
        return keys, [pairs[k] for k in keys]

    for label, glyphs, glyph_list in (
            ("Latin", {n for _, _, n in afms[4].glyphs}, agl),
            ("Symbol", {n for _, _, n in afms[12].glyphs}, agl),
            ("ZapfDingbats", {n for _, _, n in afms[13].glyphs}, zapf_list)):
        keys, values = by_unicode(glyphs, glyph_list)
        lines += span(label + "Unicodes", "ushort", keys,
                      f"Unicode values of the {label} character set's glyphs, ascending; parallel to <see cref=\"{label}UnicodeNames\"/>.")
        lines += span(label + "UnicodeNames", "short", values,
                      f"For each value of <see cref=\"{label}Unicodes\"/>, the index of the glyph's name in <see cref=\"GlyphNameTable.Names\"/>.")
    lines[-1:] = ["}"]
    write("Standard14Data.g.cs", lines)

    # AdobeGlyphList.g.cs
    lines = header([AGL_NOTICE])
    lines += [
        "/// <summary>The Adobe Glyph List and the ITC Zapf Dingbats Glyph List: glyph name to Unicode.</summary>",
        "/// <remarks>ISO 32000-2 §9.10.2 refers to the Adobe Glyph List for mapping glyph names to Unicode.</remarks>",
        "internal static partial class AdobeGlyphList",
        "{",
    ]
    for label, entries in (("Agl", agl), ("Zapf", zapf_list)):
        name_data, name_offsets, value_data, value_offsets = "", [0], "", [0]
        for name, value in entries:
            name_data += name
            name_offsets.append(len(name_data))
            value_data += value
            value_offsets.append(len(value_data))
        assert len(name_data) < 65536 and len(value_data) < 65536
        lines += [f"    private const int {label}Count = {len(entries)};", ""]
        lines += string_data(label + "Names", name_data, "The glyph names, ordinal order, concatenated.")
        lines += span(label + "NameOffsets", "ushort", name_offsets, "Where each name starts in the names; one more entry than there are names.")
        lines += string_data(label + "Values", value_data, "The Unicode values of each name, concatenated.")
        lines += span(label + "ValueOffsets", "ushort", value_offsets, "Where each value starts in the values; one more entry than there are names.")
    lines[-1:] = ["}"]
    write("AdobeGlyphList.g.cs", lines)
    print(len(names), "glyph names;", len(agl), "AGL entries;", len(zapf_list), "Zapf Dingbats entries")
    return 0


if __name__ == "__main__":
    sys.exit(main())
