"""Makes the subset Noto fonts the international samples use (leaflet, notice, rashomon) from the upstream fonts.

    python3 samples/documents/tools/subset_fonts.py <folder of upstream fonts>

The upstream fonts are large (each CJK font is 16 to 25 MB), so only subsets holding the characters the documents use are committed,
in samples/documents/fonts/. The folder must hold the files MANIFEST.md lists (Google Fonts' variable Noto fonts and the Noto CJK
OpenType fonts): the variable fonts are first instanced at Regular (400) and Bold (700), width 100. The subsets keep every OpenType
layout feature (Arabic joining, Indic and Thai shaping, the vertical alternates) and the vertical metrics. The CJK fonts stay
CID-keyed CFF. Needs fontTools.

It also writes fonts/notoserifcjk/vertical-metrics.json: the glyphs of the Japanese serif subset whose vertical metrics differ from
the font's default (advance 1000, origin 880), which the vertical sample writes into the CIDFont's W2 array.
"""

import json
import os
import re
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def text_of(*paths: str) -> str:
    """Every character in the given files (HTML tags and JSON syntax included, which only adds ASCII)."""
    out = []
    for path in paths:
        with open(os.path.join(ROOT, path), encoding="utf-8") as f:
            out.append(f.read())
    return "".join(out)


def generator_text(name: str) -> str:
    with open(os.path.join(ROOT, "generator", name), encoding="utf-8") as f:
        return "".join(ch for ch in f.read() if ord(ch) > 0x7F)


ASCII = "".join(chr(c) for c in range(0x20, 0x7F)) + " –—‘’“”…·×"


def make(source: str, target: str, text: str, weight: int | None = None) -> TTFont:
    font = TTFont(source)
    if weight is not None and "fvar" in font:
        axes = {a.axisTag: a for a in font["fvar"].axes}
        location = {"wght": weight}
        if "wdth" in axes:
            location["wdth"] = 100
        font = instancer.instantiateVariableFont(font, location, updateFontNames=True)
    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    options.notdef_outline = True
    options.hinting = False
    options.desubroutinize = False
    subsetter = subset.Subsetter(options)
    subsetter.populate(text=text + ASCII)
    subsetter.subset(font)
    os.makedirs(os.path.dirname(target), exist_ok=True)
    font.save(target)
    print(f"{os.path.relpath(target, ROOT)}: {len(font.getGlyphOrder())} glyphs, {os.path.getsize(target):,} bytes")
    return font


def vertical_metrics(font: TTFont, target: str) -> None:
    vmtx = font["vmtx"].metrics
    hmtx = font["hmtx"].metrics
    vorg = font["VORG"] if "VORG" in font else None
    default_y = vorg.defaultVertOriginY if vorg else 880
    out = {}
    for gid, name in enumerate(font.getGlyphOrder()):
        advance = vmtx[name][0]
        origin = vorg.VOriginRecords.get(name, default_y) if vorg else default_y
        if advance != 1000 or origin != 880:
            out[str(gid)] = [advance, hmtx[name][0], origin]
    with open(target, "w", encoding="utf-8") as f:
        json.dump({"default": [880, 1000], "glyphs": out}, f, indent=0, sort_keys=True)
    print(f"{os.path.relpath(target, ROOT)}: {len(out)} glyphs with their own vertical metrics")


def main() -> None:
    src = sys.argv[1]
    fonts = os.path.join(ROOT, "fonts")
    leaflet = re.sub(r"<style>.*?</style>", "", text_of("leaflet/leaflet.html"), flags=re.S)
    notice = text_of("notice/notice.json")
    rashomon = text_of("rashomon/rashomon.json") + generator_text("Rashomon.cs")
    both = leaflet + notice
    for weight, style in ((400, "Regular"), (700, "Bold")):
        make(f"{src}/NotoSans-VF.ttf", f"{fonts}/notosans/NotoSans-{style}.ttf", both, weight)
        make(f"{src}/NotoNaskhArabic-VF.ttf", f"{fonts}/notonaskharabic/NotoNaskhArabic-{style}.ttf", both, weight)
        make(f"{src}/NotoSansHebrew-VF.ttf", f"{fonts}/notosanshebrew/NotoSansHebrew-{style}.ttf", both, weight)
        make(f"{src}/NotoSansDevanagari-VF.ttf", f"{fonts}/notosansdevanagari/NotoSansDevanagari-{style}.ttf", leaflet, weight)
        make(f"{src}/NotoSansThai-VF.ttf", f"{fonts}/notosansthai/NotoSansThai-{style}.ttf", leaflet, weight)
    for region in ("sc", "jp", "kr"):
        make(f"{src}/NotoSansCJK{region}-Regular.otf", f"{fonts}/notosanscjk/NotoSansCJK{region}-Regular.otf", leaflet)
    serif = make(f"{src}/NotoSerifCJKjp-Regular.otf", f"{fonts}/notoserifcjk/NotoSerifCJKjp-Regular.otf", rashomon)
    vertical_metrics(serif, f"{fonts}/notoserifcjk/vertical-metrics.json")


if __name__ == "__main__":
    main()
