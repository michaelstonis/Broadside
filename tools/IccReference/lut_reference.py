#!/usr/bin/env python3
"""Reference values for tests/Corpus/icc-lut.pdf, computed by Little CMS.

Development only: the values it prints are committed in tests/Broadside.Rendering.Tests/Icc/IccLutTests.cs, and neither this
script nor Little CMS is a dependency of anything that ships (ADR 0001).

For each lookup-table profile of the corpus file (tests/Corpus/generate.py, icc_lut_profiles) it prints, with Little CMS doing
every conversion (double precision, no optimisation, no black point compensation):

  * "srgb": each colour the file paints (ICC_LUT_SAMPLES), media-relative colorimetric to Little CMS's built-in sRGB, 0..255;
  * "pcs": the same colours to the profile's own PCS (PCSLAB through the built-in v4 Lab profile, PCSXYZ through the built-in
    XYZ profile), media-relative and ICC-absolute colorimetric. Little CMS picks the AToB tag for the intent by ICC.1:2022 8.10.2,
    so CS3 (matrix/TRC tags and an A2B0) checks the precedence independently;
  * "perceptual": CS1's A2B0 (the perceptual tag) evaluated directly with cmsReadTag and cmsPipelineEvalFloat, the result decoded
    from the 16-bit PCSLAB encoding (6.3.4.2 Tables 12 and 13). A whole-transform perceptual reference is not used because Little
    CMS forces black point compensation for version 4 profiles in that intent;
  * "btoa": PCS values to the device colours of the profiles with BToA tags (CS0 B2A0 lut16Type, CS1 B2A1 lutBToAType, CS3 B2A0
    lutBToAType), media-relative colorimetric.

Device CMYK in Little CMS's floating-point formats is 0..100; it is printed 0..1.

    python3 -I tools/IccReference/lut_reference.py
"""

from __future__ import annotations

import ctypes
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))

FLOAT = 1 << 22


def fmt(space: int, channels: int) -> int:
    """lcms2.h: FLOAT_SH(1) | COLORSPACE_SH(space) | CHANNELS_SH(channels) | BYTES_SH(0), i.e. doubles."""
    return FLOAT | (space << 16) | (channels << 3)


PT_GRAY, PT_RGB, PT_CMYK, PT_XYZ, PT_LAB = 3, 4, 6, 9, 10
TYPE = {"gray": fmt(PT_GRAY, 1), "rgb": fmt(PT_RGB, 3), "cmyk": fmt(PT_CMYK, 4)}
TYPE_LAB_DBL = fmt(PT_LAB, 3)
TYPE_XYZ_DBL = fmt(PT_XYZ, 3)
TYPE_RGB_DBL = TYPE["rgb"]
RELATIVE, ABSOLUTE = 1, 3
NOOPTIMIZE = 0x0100
A2B0 = 0x41324230

LAB_SAMPLES = [(50, 0, 0), (75, 20, -30), (30, -40, 25), (90, 5, 60), (20, 10, 10)]
XYZ_SAMPLES = [(0.2, 0.2, 0.15), (0.5, 0.45, 0.3), (0.77, 0.8, 0.66), (0.1, 0.05, 0.4)]


def load(name: str, path: str):
    sys.dont_write_bytecode = True  # leave no __pycache__ in tests/Corpus or here
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def bind(lib):
    lib.cmsOpenProfileFromMem.restype = ctypes.c_void_p
    lib.cmsOpenProfileFromMem.argtypes = [ctypes.c_char_p, ctypes.c_uint32]
    for name in ("cmsCreate_sRGBProfile", "cmsCreateXYZProfile"):
        getattr(lib, name).restype = ctypes.c_void_p
    lib.cmsCreateLab4Profile.restype = ctypes.c_void_p
    lib.cmsCreateLab4Profile.argtypes = [ctypes.c_void_p]
    lib.cmsCreateTransform.restype = ctypes.c_void_p
    lib.cmsCreateTransform.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_uint32]
    lib.cmsDoTransform.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32]
    lib.cmsDeleteTransform.argtypes = [ctypes.c_void_p]
    lib.cmsReadTag.restype = ctypes.c_void_p
    lib.cmsReadTag.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
    lib.cmsPipelineEvalFloat.argtypes = [ctypes.POINTER(ctypes.c_float), ctypes.POINTER(ctypes.c_float), ctypes.c_void_p]
    lib.cmsGetEncodedCMMversion.restype = ctypes.c_int
    return lib


def transform(lib, source, in_type, target, out_type, intent, values, outputs):
    handle = lib.cmsCreateTransform(source, in_type, target, out_type, intent, NOOPTIMIZE)
    if not handle:
        raise RuntimeError("Little CMS refused the transform")
    data = (ctypes.c_double * len(values))(*values)
    out = (ctypes.c_double * outputs)()
    lib.cmsDoTransform(handle, data, out, 1)
    lib.cmsDeleteTransform(handle)
    return list(out)


def floats(values) -> str:
    return ", ".join("%gf" % v for v in values)


def fixed(values) -> str:
    return ", ".join("%.4ff" % v for v in values)


def main() -> int:
    reference = load("reference", os.path.join(HERE, "reference.py"))
    lib = reference.little_cms()
    if lib is None:
        print("Little CMS not found (set LCMS2_LIBRARY)", file=sys.stderr)
        return 1
    bind(lib)
    generator = load("generate", os.path.join(ROOT, "tests", "Corpus", "generate.py"))
    profiles = generator.icc_lut_profiles()
    srgb = lib.cmsCreate_sRGBProfile()
    lab = lib.cmsCreateLab4Profile(None)
    xyz = lib.cmsCreateXYZProfile()
    print("// Little CMS %d" % lib.cmsGetEncodedCMMversion())
    handles = {}
    for name, data in profiles.items():
        handle = lib.cmsOpenProfileFromMem(data, len(data))
        if not handle:
            raise RuntimeError("Little CMS cannot open " + name)
        handles[name] = handle

    print("// srgb")
    for name, handle in handles.items():
        kind = generator.ICC_LUT_KINDS[name]
        for sample in generator.ICC_LUT_SAMPLES[kind]:
            scale = 100 if kind == "cmyk" else 1
            rgb = transform(lib, handle, TYPE[kind], srgb, TYPE_RGB_DBL, RELATIVE, [v * scale for v in sample], 3)
            print('{ "%s", [%s], [%s] },' % (name, floats(sample), ", ".join("%.3ff" % (255 * min(max(v, 0), 1)) for v in rgb)))

    print("// pcs")
    for intent, label in ((RELATIVE, "RelativeColorimetric"), (ABSOLUTE, "AbsoluteColorimetric")):
        for name, handle in handles.items():
            kind = generator.ICC_LUT_KINDS[name]
            target, out_type = (xyz, TYPE_XYZ_DBL) if name == "CS3" else (lab, TYPE_LAB_DBL)
            for sample in generator.ICC_LUT_SAMPLES[kind]:
                scale = 100 if kind == "cmyk" else 1
                pcs = transform(lib, handle, TYPE[kind], target, out_type, intent, [v * scale for v in sample], 3)
                print('{ "%s", RenderingIntent.%s, [%s], [%s] },' % (name, label, floats(sample), fixed(pcs)))

    print("// perceptual (CS1 A2B0)")
    pipeline = lib.cmsReadTag(handles["CS1"], A2B0)
    for sample in generator.ICC_LUT_SAMPLES["cmyk"]:
        data = (ctypes.c_float * 4)(*sample)
        out = (ctypes.c_float * 3)()
        lib.cmsPipelineEvalFloat(data, out, pipeline)
        decoded = (out[0] * 100, out[1] * 255 - 128, out[2] * 255 - 128)
        print('{ "CS1", RenderingIntent.Perceptual, [%s], [%s] },' % (floats(sample), fixed(decoded)))

    print("// btoa")
    for name, kind in (("CS0", "cmyk"), ("CS1", "cmyk"), ("CS3", "rgb")):
        source, in_type, samples = (xyz, TYPE_XYZ_DBL, XYZ_SAMPLES) if name == "CS3" else (lab, TYPE_LAB_DBL, LAB_SAMPLES)
        outputs = 4 if kind == "cmyk" else 3
        for sample in samples:
            device = transform(lib, source, in_type, handles[name], TYPE[kind], RELATIVE, list(sample), outputs)
            if kind == "cmyk":
                device = [v / 100 for v in device]
            print('{ "%s", [%s], [%s] },' % (name, floats(sample), fixed(device)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
