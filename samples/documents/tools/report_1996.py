"""Writes report-1996.pdf: a technical memorandum as a mid-1990s PostScript distiller would have written it, byte by byte.

    python3 samples/documents/tools/report_1996.py samples/documents samples/documents/pdf/report-1996.pdf

What makes it a PDF 1.2-era file, and what Broadside has to read in it:

- A classic cross-reference table, one revision, an Info dictionary and no metadata stream, no object or xref streams.
- Content streams compressed with LZW and then ASCII85 (a filter chain), the figure's image RunLength-encoded, and the bitmap
  font's glyphs as inline image masks in ASCIIHex.
- An embedded Type 1 font (FontFile with Length1, Length2 and Length3): EB Garamond (OFL), converted here from its CFF outlines
  to Type 1 charstrings, eexec-encrypted, with WinAnsiEncoding.
- A Type 3 font whose glyphs are 300 dpi bitmaps of IBM Plex Mono (OFL), as dvips wrote TeX's PK fonts, used for a data-logger
  printout. Its glyphs carry standard names and the font has no ToUnicode map.
- The standard 14 fonts, not embedded: Helvetica-Bold (WinAnsiEncoding), Courier (the font's own encoding), Times-Roman
  (MacRomanEncoding with Differences), Symbol and ZapfDingbats.

The text and the numbers are written for the Broadside sample documents; the river, the weir and the authority are invented. The
output is byte-for-byte reproducible. Needs fontTools and Pillow.
"""

import base64
import os
import sys
import zlib

from fontTools.pens.recordingPen import RecordingPen
from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

PAGE_W, PAGE_H = 612, 792

# ---------------------------------------------------------------------------------------------------------------- encoders


def lzw(data: bytes) -> bytes:
    """LZWDecode's encoder: 9 to 12-bit codes, EarlyChange 1, a clear-table code first and when the table fills."""
    clear, eod = 256, 257
    out, buffer, bits = bytearray(), 0, 0
    width = 9

    def emit(code: int) -> None:
        nonlocal buffer, bits
        buffer = (buffer << width) | code
        bits += width
        while bits >= 8:
            bits -= 8
            out.append((buffer >> bits) & 0xFF)

    table = {bytes([i]): i for i in range(256)}
    nxt = 258
    emit(clear)
    w = b""
    for byte in data:
        wc = w + bytes([byte])
        if wc in table:
            w = wc
            continue
        emit(table[w])
        table[wc] = nxt
        nxt += 1
        if nxt + 1 > (1 << width) and width < 12:
            width += 1
        if nxt >= 4094:
            emit(clear)
            table = {bytes([i]): i for i in range(256)}
            nxt = 258
            width = 9
        w = bytes([byte])
    if w:
        emit(table[w])
        nxt += 1
        if nxt + 1 > (1 << width) and width < 12:
            width += 1
    emit(eod)
    if bits:
        out.append((buffer << (8 - bits)) & 0xFF)
    return bytes(out)


def ascii85(data: bytes) -> bytes:
    encoded = base64.a85encode(data, wrapcol=72)
    return encoded + b"~>"


def run_length(data: bytes) -> bytes:
    out, i = bytearray(), 0
    while i < len(data):
        run = 1
        while i + run < len(data) and run < 128 and data[i + run] == data[i]:
            run += 1
        if run > 1:
            out += bytes([257 - run, data[i]])
            i += run
            continue
        start = i
        while i < len(data) and i - start < 128 and not (i + 1 < len(data) and data[i + 1] == data[i]):
            i += 1
        if i == start:
            i += 1
        out.append(i - start - 1)
        out += data[start:i]
    out.append(128)
    return bytes(out)


# ---------------------------------------------------------------------------------------------------------------- Type 1


def t1_number(v: int) -> bytes:
    if -107 <= v <= 107:
        return bytes([v + 139])
    if 108 <= v <= 1131:
        v -= 108
        return bytes([(v >> 8) + 247, v & 0xFF])
    if -1131 <= v <= -108:
        v = -v - 108
        return bytes([(v >> 8) + 251, v & 0xFF])
    return bytes([255]) + v.to_bytes(4, "big", signed=True)


def charstring(sidebearing: int, width: int, recording: RecordingPen) -> bytes:
    out = bytearray(t1_number(sidebearing) + t1_number(width) + bytes([13]))  # hsbw
    x, y = sidebearing, 0
    for op, points in recording.value:
        if op == "moveTo":
            px, py = (round(c) for c in points[0])
            out += t1_number(px - x) + t1_number(py - y) + bytes([21])
            x, y = px, py
        elif op == "lineTo":
            px, py = (round(c) for c in points[0])
            out += t1_number(px - x) + t1_number(py - y) + bytes([5])
            x, y = px, py
        elif op == "curveTo":
            for chunk in range(0, len(points), 3):
                args = bytearray()
                for px, py in points[chunk:chunk + 3]:
                    px, py = round(px), round(py)
                    args += t1_number(px - x) + t1_number(py - y)
                    x, y = px, py
                out += args + bytes([8])
        elif op in ("closePath", "endPath"):
            out.append(9)
        else:
            raise ValueError(op)
    out.append(14)  # endchar
    return bytes(out)


def encrypt(data: bytes, r: int, prefix: bytes) -> bytes:
    out = bytearray()
    for byte in prefix + data:
        cipher = byte ^ (r >> 8)
        r = ((cipher + r) * 52845 + 22719) & 0xFFFF
        out.append(cipher)
    return bytes(out)


def type1_font(path: str, name: str, chars: str) -> tuple[bytes, int, int, int, dict[int, int], tuple]:
    """Converts the CFF outlines of the glyphs for `chars` (cp1252) to an eexec-encrypted Type 1 program."""
    font = TTFont(path)
    glyphs = font.getGlyphSet()
    cmap = font.getBestCmap()
    hmtx = font["hmtx"].metrics
    names, widths = {}, {}
    for ch in sorted(set(chars)):
        code = ch.encode("cp1252")[0]
        gname = cmap.get(ord(ch))
        if gname:
            names[code] = gname
            widths[code] = hmtx[gname][0]
    upm = font["head"].unitsPerEm
    assert upm == 1000, upm
    charstrings = {".notdef": charstring(0, 500, RecordingPen())}
    for gname in sorted(set(names.values())):
        rec = RecordingPen()
        glyphs[gname].draw(rec)
        lsb = hmtx[gname][1]
        charstrings[gname] = charstring(lsb, hmtx[gname][0], rec)
    head = font["head"]
    bbox = (head.xMin, head.yMin, head.xMax, head.yMax)
    clear = (
        "%!PS-AdobeFont-1.0: " + name + " 1.0\n"
        "%%Converted from the OpenType CFF program for the Broadside sample documents (SIL OFL 1.1)\n"
        "11 dict begin\n"
        "/FontInfo 5 dict dup begin /version (1.0) readonly def /Notice (Copyright 2017 The EB Garamond Project Authors. SIL Open Font License 1.1.) readonly def "
        "/FamilyName (EB Garamond) readonly def /ItalicAngle 0 def /isFixedPitch false def end readonly def\n"
        f"/FontName /{name} def\n/PaintType 0 def\n/FontType 1 def\n/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n"
        "/Encoding StandardEncoding def\n"
        f"/FontBBox {{{bbox[0]} {bbox[1]} {bbox[2]} {bbox[3]}}} readonly def\n/UniqueID 4999999 def\n"
        "currentdict end\ncurrentfile eexec\n"
    ).encode("ascii")
    private = bytearray(
        b"dup /Private 8 dict dup begin\n/RD{string currentfile exch readstring pop}executeonly def\n"
        b"/ND{noaccess def}executeonly def\n/NP{noaccess put}executeonly def\n/MinFeature{16 16} def\n/password 5839 def\n"
        b"/BlueValues [-10 0 400 410 650 660] def\n/lenIV 4 def\n"
        b"/Subrs 4 array\n"
    )
    subrs = [bytes([139 + 3, 139 + 0, 12, 16, 12, 17, 12, 17, 12, 33, 11]), bytes([139 + 0, 139 + 1, 12, 16, 11]),
             bytes([139 + 0, 139 + 2, 12, 16, 11]), bytes([11])]
    for i, subr in enumerate(subrs):
        enc = encrypt(subr, 4330, b"\0\0\0\0")
        private += f"dup {i} {len(enc)} RD ".encode() + enc + b" NP\n"
    private += b"ND\n2 index /CharStrings " + str(len(charstrings)).encode() + b" dict dup begin\n"
    for gname, cs in charstrings.items():
        enc = encrypt(cs, 4330, b"\0\0\0\0")
        private += f"/{gname} {len(enc)} RD ".encode() + enc + b" ND\n"
    private += b"end\nend\nreadonly put\nnoaccess put\ndup /FontName get exch definefont pop\nmark currentfile closefile\n"
    binary = encrypt(bytes(private), 55665, b"\x8a\x1f\x2b\x44")
    trailer = (("0" * 64 + "\n") * 8 + "cleartomark\n").encode("ascii")
    return clear + binary + trailer, len(clear), len(binary), len(trailer), widths, bbox


# ---------------------------------------------------------------------------------------------------------------- Type 3


def type3_glyphs(path: str, chars: str):
    """300 dpi bitmaps of a 10 pt font: (code, name, advance, (llx, lly, w, h), hex rows), units of 1/300 inch."""
    px = round(10 * 300 / 72)
    font = ImageFont.truetype(path, px)
    ascent, _ = font.getmetrics()
    tt = TTFont(path)
    cmap = tt.getBestCmap()
    out = []
    for ch in sorted(set(chars)):
        advance = round(font.getlength(ch))
        box = font.getbbox(ch)
        if ch == " " or box[2] <= box[0]:
            out.append((ord(ch), cmap[ord(ch)], advance, None, None))
            continue
        width, height = box[2] - box[0], box[3] - box[1]
        image = Image.new("1", (width, height), 1)
        ImageDraw.Draw(image).text((-box[0], -box[1]), ch, font=font, fill=0)
        rows = image.tobytes()
        llx, lly = box[0], ascent - box[3]
        out.append((ord(ch), cmap[ord(ch)], advance, (llx, lly, width, height), rows.hex().upper()))
    return out, px


# ---------------------------------------------------------------------------------------------------------------- the PDF


class Writer:
    def __init__(self) -> None:
        self.objects: list[bytes | None] = []

    def reserve(self) -> int:
        self.objects.append(None)
        return len(self.objects)

    def set(self, number: int, body: bytes) -> int:
        self.objects[number - 1] = body
        return number

    def add(self, body: bytes) -> int:
        return self.set(self.reserve(), body)

    def stream(self, dictionary: str, data: bytes) -> int:
        return self.add(f"<< {dictionary} /Length {len(data)} >>\nstream\n".encode("latin-1") + data + b"\nendstream")

    def bytes(self, root: int, info: int) -> bytes:
        out = bytearray(b"%PDF-1.2\n%\xe2\xe3\xcf\xd3\n")
        offsets = []
        for i, body in enumerate(self.objects, start=1):
            offsets.append(len(out))
            out += f"{i} 0 obj\n".encode() + body + b"\nendobj\n"
        xref = len(out)
        out += f"xref\n0 {len(self.objects) + 1}\n0000000000 65535 f \n".encode()
        for offset in offsets:
            out += f"{offset:010d} 00000 n \n".encode()
        file_id = zlib.crc32(bytes(out)).to_bytes(4, "big").hex() * 4
        out += (f"trailer\n<< /Size {len(self.objects) + 1} /Root {root} 0 R /Info {info} 0 R /ID [<{file_id}> <{file_id}>] >>\n"
                f"startxref\n{xref}\n%%EOF\n").encode()
        return bytes(out)


def pdf_string(text: str, encoding: str = "cp1252") -> str:
    raw = text.encode(encoding)
    out = []
    for b in raw:
        c = chr(b)
        if c in "()\\":
            out.append("\\" + c)
        elif 32 <= b < 127:
            out.append(c)
        else:
            out.append(f"\\{b:03o}")
    return "(" + "".join(out) + ")"


def wrap(text: str, widths: dict[int, int], size: float, measure: float) -> list[str]:
    lines, line = [], ""
    for word in text.split(" "):
        candidate = (line + " " + word).strip()
        if sum(widths.get(b, 500) for b in candidate.encode("cp1252")) * size / 1000 > measure and line:
            lines.append(line)
            line = word
        else:
            line = candidate
    lines.append(line)
    return lines


TITLE = "Calibration of the Riverside Weir Flow Gauges"
ABSTRACT = (
    "The three ultrasonic level gauges on the Riverside weir were checked against current-meter gaugings between March and "
    "May 1996. Two gauges read within 4 mm of the reference staff; the third drifted by 23 mm after the April flood and was "
    "re-zeroed. Discharge computed from the rated weir equation agrees with the gaugings to within 3.1 percent across the "
    "observed range of 1.2 to 46 cubic meters per second. We recommend quarterly staff checks and a new gauging after every "
    "flow above 40 cubic meters per second."
)
SECTIONS = [
    ("1  Background", [
        "The Riverside weir is a broad-crested concrete weir 18.0 m wide, built in 1958 to hold the town reach at a steady level. "
        "Since 1987 it has also served as the authority's main flow station. Its rating converts the upstream head over the crest into "
        "discharge with the standard broad-crested weir equation given below, whose coefficient was fitted to gaugings made in 1988.",
        "The level gauges were replaced in 1994. Each measures the time of flight of an ultrasonic pulse to the water surface and "
        "back, corrects it for air temperature, and logs the level every fifteen minutes. Their manufacturer quotes an accuracy of "
        "2 mm, but the mountings on the old footbridge move with temperature, and debris in the April flood struck the bridge.",
    ]),
    ("2  Method", [
        "On eight visits the field team read the staff gauge, measured the discharge with a current meter at twenty verticals, and "
        "noted the three logged levels for the same quarter hour. The gaugings span the range of flows usual in spring.",
    ]),
]
TABLE = [
    "Date      Staff(m)  G1(m)   G2(m)   G3(m)   Q gauged  Q rated",
    "--------  --------  ------  ------  ------  --------  -------",
    "03/04/96   0.212    0.214   0.211   0.213     1.24     1.21",
    "03/18/96   0.488    0.490   0.486   0.489     4.31     4.40",
    "04/02/96   0.951    0.953   0.949   0.950    11.72    11.93",
    "04/09/96   1.734    1.737   1.731   1.739    28.65    29.40",
    "04/16/96   2.312    2.315   2.309   2.335    45.90    46.28",
    "04/30/96   1.106    1.108   1.104   1.129    14.80    14.96",
    "05/14/96   0.637    0.639   0.635   0.614     6.48     6.57",
    "05/28/96   0.402    0.404   0.400   0.403     3.25     3.29",
]
FINDINGS = [
    "Gauges 1 and 2 agree with the staff to within 4 mm on every visit.",
    "Gauge 3 read 23 mm high after the flood of April 16 and was re-zeroed on May 14.",
    "The rating agrees with the gaugings to within 3.1 percent; no change to C is needed.",
]
PRINTOUT = [
    "RIVERSIDE WEIR  LOGGER 2  STATION 04117   DUMP 96-05-14 09:12",
    "",
    " TIME   G1MM  G2MM  G3MM  TAIR  BATT  FLAGS",
    " 08:00   639   635   614  11.2  12.6  ......",
    " 08:15   639   636   614  11.4  12.6  ......",
    " 08:30   640   636   615  11.9  12.6  ......",
    " 08:45   640   636   615  12.3  12.6  ......",
    " 09:00   641   637   615  12.8  12.6  ..Z...",
    " 09:15   641   637   638  13.1  12.6  ...R..",
    "",
    " ZERO OFFSET G3 SET +23  OPERATOR JK  OK",
    " NEXT CALIBRATION DUE 96-08-14",
]


def main() -> None:
    root, target = sys.argv[1], sys.argv[2]
    body_text = "".join(chr(c) for c in range(32, 127)) + TITLE + ABSTRACT + "".join(p for _, ps in SECTIONS for p in ps) + "".join(FINDINGS) + \
        "Prepared by the Hydrometry Section. Abstract Table 1 Gauge readings and discharges, 1996. Appendix A. Printout of logger 2 on May 14. Page 0123456789 of Figure 1. The level gauges on the footbridge, seen from downstream. where is the discharge, the crest width, the head and the coefficient (1.62 here)."
    wx = Writer()
    catalog, pages_node, info = wx.reserve(), wx.reserve(), wx.reserve()

    program, l1, l2, l3, widths, bbox = type1_font(os.path.join(root, "fonts/ebgaramond/EBGaramond-Regular.otf"), "EBGaramond-Regular", body_text)
    fontfile = wx.stream(f"/Length1 {l1} /Length2 {l2} /Length3 {l3} /Filter /LZWDecode", lzw(program))
    first, last = min(widths), max(widths)
    descriptor = wx.add((f"<< /Type /FontDescriptor /FontName /EBGaramond-Regular /Flags 34 /FontBBox [{bbox[0]} {bbox[1]} {bbox[2]} {bbox[3]}] "
                         f"/ItalicAngle 0 /Ascent 800 /Descent -270 /CapHeight 650 /XHeight 400 /StemV 80 /FontFile {fontfile} 0 R >>").encode())
    garamond = wx.add((f"<< /Type /Font /Subtype /Type1 /BaseFont /EBGaramond-Regular /FirstChar {first} /LastChar {last} "
                       f"/Widths [{' '.join(str(widths.get(c, 0)) for c in range(first, last + 1))}] /Encoding /WinAnsiEncoding "
                       f"/FontDescriptor {descriptor} 0 R >>").encode())
    helvetica = wx.add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>")
    courier = wx.add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>")
    times = wx.add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding << /Type /Encoding /BaseEncoding /MacRomanEncoding "
                   b"/Differences [128 /Adieresis /Aring 135 /aacute 142 /eacute 150 /ntilde 154 /odieresis 159 /udieresis 165 /bullet] >> >>")
    symbol = wx.add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Symbol >>")
    dingbats = wx.add(b"<< /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats >>")

    printout_chars = "".join(PRINTOUT)
    glyphs, px = type3_glyphs(os.path.join(root, "fonts/ibmplexmono/IBMPlexMono-Regular.ttf"), printout_chars)
    procs, differences = [], []
    for code, name, advance, box, data in glyphs:
        if box is None:
            body = f"{advance} 0 0 0 0 0 d1\n".encode()
        else:
            llx, lly, w, h = box
            body = (f"{advance} 0 {llx} {lly} {llx + w} {lly + h} d1\nq {w} 0 0 {h} {llx} {lly} cm\n"
                    f"BI /W {w} /H {h} /IM true /BPC 1 /F /AHx ID\n").encode() + "\n".join(data[i:i + 64] for i in range(0, len(data), 64)).encode() + b">\nEI Q"
        procs.append((name, wx.stream("", body)))
        differences.append(f"{code} /{name}")
    codes = [g[0] for g in glyphs]
    t3_widths = {g[0]: g[2] for g in glyphs}
    charprocs = wx.add(("<< " + " ".join(f"/{name} {ref} 0 R" for name, ref in procs) + " >>").encode())
    scale = 1 / px
    type3 = wx.add((f"<< /Type /Font /Subtype /Type3 /FontBBox [0 -15 30 45] /FontMatrix [{scale:.6f} 0 0 {scale:.6f} 0 0] "
                    f"/CharProcs {charprocs} 0 R /Encoding << /Type /Encoding /Differences [{' '.join(differences)}] >> "
                    f"/FirstChar {min(codes)} /LastChar {max(codes)} /Widths [{' '.join(str(t3_widths.get(c, 0)) for c in range(min(codes), max(codes) + 1))}] "
                    f"/Resources << >> >>").encode())

    # The figure: the gauges on the footbridge, an 8-bit gray drawing, RunLength-encoded.
    fig = Image.new("L", (360, 180), 236)
    d = ImageDraw.Draw(fig)
    d.rectangle((0, 120, 360, 180), fill=150)
    for x in range(0, 360, 6):
        d.line((x, 126 + (x // 6 % 3), x + 4, 126 + (x // 6 % 3)), fill=120)
    d.rectangle((20, 40, 340, 52), fill=70)
    for x in (40, 120, 200, 280, 330):
        d.rectangle((x, 52, x + 6, 130), fill=90)
    for i, x in enumerate((90, 180, 260)):
        d.rectangle((x - 8, 52, x + 8, 66), fill=30)
        for k in range(6):
            d.line((x, 70 + k * 9, x, 74 + k * 9), fill=60)
        d.text((x - 3, 22), f"G{i + 1}", fill=20)
    image = wx.stream("/Type /XObject /Subtype /Image /Width 360 /Height 180 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /RunLengthDecode",
                      run_length(fig.tobytes()))

    fonts = (f"/F1 {garamond} 0 R /F2 {helvetica} 0 R /F3 {courier} 0 R /F4 {times} 0 R /F5 {symbol} 0 R /F6 {dingbats} 0 R "
             f"/F7 {type3} 0 R")
    resources = wx.add(f"<< /ProcSet [/PDF /Text /ImageB] /Font << {fonts} >> /XObject << /Im1 {image} 0 R >> >>".encode())

    def text(font: str, size: float, x: float, y: float, s: str) -> str:
        return f"BT /{font} {size} Tf {x:.2f} {y:.2f} Td {s} Tj ET\n"

    def para(lines: list[str], x: float, y: float, size: float = 11, leading: float = 14) -> tuple[str, float]:
        out = ""
        for line in lines:
            out += text("F1", size, x, y, pdf_string(line))
            y -= leading
        return out, y

    def footer(n: int) -> str:
        return ("0.5 w 72 60 m 540 60 l S\n" + text("F1", 9, 72, 46, pdf_string("Riverside Water Authority  Hydrometry Section  TM-96-14")) +
                text("F1", 9, 520, 46, pdf_string(f"Page {n}")))

    measure = 468
    pages = []
    # Page 1.
    c = text("F2", 9, 72, 740, pdf_string("RIVERSIDE WATER AUTHORITY  ·  TECHNICAL MEMORANDUM TM-96-14".replace("·", "-")))
    c += text("F2", 18, 72, 712, pdf_string(TITLE))
    c += text("F1", 11, 72, 692, pdf_string("Prepared by the Hydrometry Section, June 1996"))
    c += "1 w 72 680 m 540 680 l S\n"
    c += text("F2", 10, 72, 660, pdf_string("Abstract"))
    block, y = para(wrap(ABSTRACT, widths, 10.5, measure), 72, 644, 10.5, 13.5)
    c += block
    y -= 12
    for heading, paragraphs in SECTIONS[:1]:
        c += text("F2", 12, 72, y, pdf_string(heading))
        y -= 18
        for p in paragraphs:
            block, y = para(wrap(p, widths, 11, measure), 72, y)
            c += block
            y -= 6
    c += (f"BT /F4 12 Tf 200 {y - 6:.2f} Td (Q = C) Tj /F5 12 Tf ( \\327 ) Tj /F4 12 Tf (b) Tj /F5 12 Tf ( \\327 ) Tj /F4 12 Tf (h) Tj "
          "4 Ts /F4 8 Tf (3/2) Tj 0 Ts /F4 12 Tf (,    h ) Tj /F5 12 Tf (\\263) Tj /F4 12 Tf ( 0.05 m       \\(1\\)) Tj ET\n")
    block, y = para(wrap("where Q is the discharge, b the crest width, h the head and C the coefficient (1.62 here).", widths, 11, measure), 72, y - 28)
    c += block
    c += footer(1)
    pages.append(c)
    # Page 2.
    c = ""
    y = 720
    for heading, paragraphs in SECTIONS[1:]:
        c += text("F2", 12, 72, y, pdf_string(heading))
        y -= 18
        for p in paragraphs:
            block, y = para(wrap(p, widths, 11, measure), 72, y)
            c += block
    y -= 10
    c += text("F2", 10, 72, y, pdf_string("Table 1  Gauge readings and discharges, 1996 (Q in cubic meters per second)"))
    y -= 16
    for row in TABLE:
        c += text("F3", 9, 72, y, pdf_string(row))
        y -= 11
    y -= 14
    c += f"q 360 0 0 180 126 {y - 180:.2f} cm /Im1 Do Q\n"
    y -= 196
    c += text("F1", 10, 126, y, pdf_string("Figure 1. The level gauges on the footbridge, seen from downstream."))
    y -= 26
    c += text("F2", 12, 72, y, pdf_string("3  Findings"))
    y -= 18
    for finding in FINDINGS:
        c += text("F6", 10, 80, y, "(4)")
        c += text("F1", 11, 96, y, pdf_string(finding))
        y -= 15
    c += text("F4", 10, 72, y - 8, "(Field team: J. K\\232rber, A. Nu\\226ez, R. S\\207ndor \\245 resum\\216 of visits on file.)")
    c += footer(2)
    pages.append(c)
    # Page 3.
    c = text("F2", 12, 72, 720, pdf_string("Appendix A  Printout of logger 2 on May 14"))
    c += "0.92 g 66 " + f"{720 - 26 - len(PRINTOUT) * 13:.0f}" + f" 480 {len(PRINTOUT) * 13 + 14} re f 0 g\n"
    y = 700
    for line in PRINTOUT:
        if line:
            c += f"BT /F7 10 Tf 76 {y:.2f} Td {pdf_string(line, 'ascii')} Tj ET\n"
        y -= 13
    c += footer(3)
    pages.append(c)

    kids = []
    for content in pages:
        stream = wx.stream("/Filter [/ASCII85Decode /LZWDecode]", ascii85(lzw(content.encode("latin-1"))))
        kids.append(wx.add(f"<< /Type /Page /Parent {pages_node} 0 R /Resources {resources} 0 R /Contents {stream} 0 R >>".encode()))
    wx.set(pages_node, f"<< /Type /Pages /Kids [{' '.join(f'{k} 0 R' for k in kids)}] /Count {len(kids)} /MediaBox [0 0 {PAGE_W} {PAGE_H}] >>".encode())
    wx.set(catalog, f"<< /Type /Catalog /Pages {pages_node} 0 R >>".encode())
    wx.set(info, (b"<< /Title (" + TITLE.encode() + b") /Author (Riverside Water Authority, Hydrometry Section) "
                  b"/Creator (tools/report_1996.py, Broadside sample documents) /Producer (tools/report_1996.py) "
                  b"/CreationDate (D:19960612103000) /ModDate (D:19960612103000) >>"))
    with open(target, "wb") as f:
        f.write(wx.bytes(catalog, info))
    print(f"{target}: {len(kids)} pages, {os.path.getsize(target):,} bytes")


if __name__ == "__main__":
    main()
