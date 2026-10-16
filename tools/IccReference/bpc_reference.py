#!/usr/bin/env python3
"""Black point compensation and rendering intent references for the ICC engine (issue #119), computed by Little CMS.

Development only: the values it prints are committed in tests/Broadside.Rendering.Tests/Icc/IccBlackPointReferences.cs, and
neither this script nor Little CMS is a dependency of anything that ships (ADR 0001).

ISO 32000-2 8.6.5.9 makes black point compensation (UseBlackPtComp ON) follow ISO 18619, which is Adobe's published algorithm;
PDF 2.0 Application Note 001 explains the flag but has no worked examples. Little CMS implements the algorithm
(cmsDetectBlackPoint, cmsDetectDestinationBlackPoint, cmsFLAGS_BLACKPOINTCOMPENSATION), so its results are the test vectors:

  * "srgb": every colour the corpus files paint in their ICCBased spaces (icc-lut.pdf, icc-rgb-matrix.pdf: tests/Corpus/generate.py
    ICC_LUT_SAMPLES and ICC_MATRIX_SAMPLES) and the two colours of rendering-intents.pdf, converted to Little CMS's built-in sRGB
    with black point compensation, under the perceptual, media-relative colorimetric and saturation intents, 0..255;
  * "cmyk": the RGB and gray colours of the same files, and the colours of rendering-intents.pdf, converted to the CMYK output
    intent profile of output-intent-cmyk.pdf (the catalog's) with black point compensation, media-relative and perceptual, 0..1.

Little CMS 2.19.1 leaves the darkest-colorant black point un-neutralised (fixed after that release); a case whose source black
point is not neutral is skipped and reported, so every vector printed follows the method as published. Cases where Little CMS
finds no black point because the profile lacks the intent's own tag are kept: the engine does the same.

Double precision, no optimisation; device CMYK in Little CMS's floating-point formats is 0..100 and is printed 0..1.

    python3 -I tools/IccReference/bpc_reference.py
"""

from __future__ import annotations

import ctypes
import importlib.util
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

FLOAT = 1 << 22
PT_GRAY, PT_RGB, PT_CMYK = 3, 4, 6
NOOPTIMIZE = 0x0100
BLACKPOINTCOMPENSATION = 0x2000
INTENTS = {0: "Perceptual", 1: "RelativeColorimetric", 2: "Saturation"}
D50 = (0.9642, 1.0, 0.8249)


def fmt(space: int, channels: int) -> int:
    return FLOAT | (space << 16) | (channels << 3)


TYPE = {"gray": fmt(PT_GRAY, 1), "rgb": fmt(PT_RGB, 3), "cmyk": fmt(PT_CMYK, 4)}


class XYZ(ctypes.Structure):
    _fields_ = [("X", ctypes.c_double), ("Y", ctypes.c_double), ("Z", ctypes.c_double)]


def load(name: str, path: str):
    sys.dont_write_bytecode = True
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def bind(lib):
    lib.cmsOpenProfileFromMem.restype = ctypes.c_void_p
    lib.cmsOpenProfileFromMem.argtypes = [ctypes.c_char_p, ctypes.c_uint32]
    lib.cmsCreate_sRGBProfile.restype = ctypes.c_void_p
    lib.cmsCreateTransform.restype = ctypes.c_void_p
    lib.cmsCreateTransform.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_uint32]
    lib.cmsDoTransform.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32]
    lib.cmsDeleteTransform.argtypes = [ctypes.c_void_p]
    lib.cmsDetectBlackPoint.argtypes = [ctypes.POINTER(XYZ), ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32]
    lib.cmsGetEncodedCMMversion.restype = ctypes.c_int
    return lib


def transform(lib, source, in_type, target, out_type, intent, values, outputs):
    handle = lib.cmsCreateTransform(source, in_type, target, out_type, intent, NOOPTIMIZE | BLACKPOINTCOMPENSATION)
    if not handle:
        raise RuntimeError("Little CMS refused the transform")
    data = (ctypes.c_double * len(values))(*values)
    out = (ctypes.c_double * outputs)()
    lib.cmsDoTransform(handle, data, out, 1)
    lib.cmsDeleteTransform(handle)
    return list(out)


def neutral(lib, handle, intent) -> bool:
    """Whether Little CMS's source black point is neutral (a* = b* = 0 relative to D50), as the method requires."""
    point = XYZ()
    lib.cmsDetectBlackPoint(ctypes.byref(point), handle, intent, 0)
    if point.Y <= 0:
        return True

    def f(t: float) -> float:
        return t ** (1 / 3) if t > (6 / 29) ** 3 else t / (3 * (6 / 29) ** 2) + 4 / 29
    fx, fy, fz = f(point.X / D50[0]), f(point.Y / D50[1]), f(point.Z / D50[2])
    return math.hypot(500 * (fx - fy), 200 * (fy - fz)) < 0.05


def floats(values) -> str:
    return ", ".join("%gf" % v for v in values)


def main() -> int:
    reference = load("reference", os.path.join(HERE, "reference.py"))
    lib = reference.little_cms()
    if lib is None:
        print("Little CMS not found (set LCMS2_LIBRARY)", file=sys.stderr)
        return 1
    bind(lib)
    generator = load("generate", os.path.join(ROOT, "tests", "Corpus", "generate.py"))
    sources = []
    for name, data in generator.icc_lut_profiles().items():
        kind = generator.ICC_LUT_KINDS[name]
        sources.append(("icc-lut.pdf", name, kind, data, generator.ICC_LUT_SAMPLES[kind]))
    for index, (name, data) in enumerate(generator.icc_matrix_profiles().items()):
        kind = "rgb" if index < 2 else "gray"
        sources.append(("icc-rgb-matrix.pdf", name, kind, data, generator.ICC_MATRIX_SAMPLES[kind]))
    sources.append(("rendering-intents.pdf", "CS0", "cmyk", generator.rendering_intent_profile(),
                    list(generator.RENDERING_INTENT_COLORS.values())))
    srgb = lib.cmsCreate_sRGBProfile()
    press_data = generator.output_intent_profiles()["catalog"]
    press = lib.cmsOpenProfileFromMem(press_data, len(press_data))
    print("// Little CMS %d" % lib.cmsGetEncodedCMMversion())

    print("// srgb")
    for file, name, kind, data, samples in sources:
        handle = lib.cmsOpenProfileFromMem(data, len(data))
        for intent, label in INTENTS.items():
            if not neutral(lib, handle, intent):
                print("// skipped %s %s %s: Little CMS's black point is not neutral" % (file, name, label))
                continue
            for sample in samples:
                scale = 100 if kind == "cmyk" else 1
                rgb = transform(lib, handle, TYPE[kind], srgb, TYPE["rgb"], intent, [v * scale for v in sample], 3)
                print('{ "%s", "%s", RenderingIntent.%s, [%s], [%s] },' % (
                    file, name, label, floats(sample), ", ".join("%.3ff" % (255 * min(max(v, 0), 1)) for v in rgb)))

    print("// cmyk")
    for file, name, kind, data, samples in sources:
        if kind == "cmyk" and file != "rendering-intents.pdf":
            continue
        handle = lib.cmsOpenProfileFromMem(data, len(data))
        for intent in (0, 1):
            label = INTENTS[intent]
            if not neutral(lib, handle, intent):
                print("// skipped %s %s %s: Little CMS's black point is not neutral" % (file, name, label))
                continue
            for sample in samples:
                scale = 100 if kind == "cmyk" else 1
                cmyk = transform(lib, handle, TYPE[kind], press, TYPE["cmyk"], intent, [v * scale for v in sample], 4)
                print('{ "%s", "%s", RenderingIntent.%s, [%s], [%s] },' % (
                    file, name, label, floats(sample), ", ".join("%.4ff" % (v / 100) for v in cmyk)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
