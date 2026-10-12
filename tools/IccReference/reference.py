#!/usr/bin/env python3
"""Reference colours for tests/Corpus/icc-rgb-matrix.pdf, from the profile maths, cross-checked against Little CMS.

Development only: the values it prints are committed in tests/Broadside.Rendering.Tests/Icc/IccMatrixTests.cs, and neither this
script nor Little CMS is a dependency of anything that ships (ADR 0001).

For each profile of the corpus file (tests/Corpus/generate.py, icc_matrix_profiles) and each colour it paints (ICC_MATRIX_SAMPLES),
the media-relative colorimetric result in sRGB, 0..255, computed independently of the C# engine:

  device -> TRC (ICC.1:2022 10.6 curveType: gamma, or linear interpolation of the samples; 10.18 parametricCurveType, Table 68,
  clipped to [0, 1]) -> PCSXYZ (Annex F.3: the rXYZ, gXYZ, bXYZ columns; Annex F.2: the TRC value times the D50 PCS white) ->
  Bradford adaptation D50 to D65 -> linear sRGB (IEC 61966-2-1) -> sRGB encoding, clipped -> x 255.

The tag values are read back from the profile bytes (s15Fixed16Number, u8Fixed8Number, uInt16Number), so the encoding's rounding
is the one the engine sees. When liblcms2 is found (macOS Homebrew, or LCMS2_LIBRARY), every colour is also converted by Little
CMS (relative colorimetric, double precision, no optimisation, to its built-in sRGB profile) and the largest difference is
printed; the script fails when it exceeds 0.5.

    python3 -I tools/IccReference/reference.py
"""

from __future__ import annotations

import ctypes
import ctypes.util
import importlib.util
import os
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

D50 = (0.9642, 1.0, 0.8249)
D65 = (0.95047, 1.0, 1.08883)
BRADFORD = ((0.8951, 0.2664, -0.1614), (-0.7502, 1.7135, 0.0367), (0.0389, -0.0685, 1.0296))
BRADFORD_INV = ((0.9869929, -0.1470543, 0.1599627), (0.4323053, 0.5183603, 0.0492912), (-0.0085287, 0.0400428, 0.9684867))
XYZ_TO_SRGB = ((3.2404542, -1.5371385, -0.4985314), (-0.9692660, 1.8760108, 0.0415560), (0.0556434, -0.2040259, 1.0572252))

# lcms2.h: FLOAT_SH(1) | COLORSPACE_SH(PT_RGB = 4 / PT_GRAY = 3) | CHANNELS_SH(n) | BYTES_SH(0, i.e. 8-byte doubles)
TYPE_RGB_DBL = (1 << 22) | (4 << 16) | (3 << 3)
TYPE_GRAY_DBL = (1 << 22) | (3 << 16) | (1 << 3)
INTENT_RELATIVE_COLORIMETRIC = 1
CMS_FLAGS_NOOPTIMIZE = 0x0100


def load_generator():
    spec = importlib.util.spec_from_file_location("generate", os.path.join(ROOT, "tests", "Corpus", "generate.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def mul(m, v):
    return tuple(sum(m[r][c] * v[c] for c in range(3)) for r in range(3))


def tags(profile: bytes) -> dict[bytes, bytes]:
    count = struct.unpack_from(">I", profile, 128)[0]
    result = {}
    for i in range(count):
        sig, offset, size = struct.unpack_from(">4sII", profile, 132 + 12 * i)
        result[sig] = profile[offset:offset + size]
    return result


def fixed(data: bytes, at: int) -> float:
    return struct.unpack_from(">i", data, at)[0] / 65536


def curve(tag: bytes):
    if tag[:4] == b"curv":
        n = struct.unpack_from(">I", tag, 8)[0]
        if n == 0:
            return lambda x: x
        if n == 1:
            gamma = struct.unpack_from(">H", tag, 12)[0] / 256
            return lambda x: x ** gamma
        table = [v / 65535 for v in struct.unpack_from(">%dH" % n, tag, 12)]

        def sampled(x):
            p = x * (n - 1)
            i = min(int(p), n - 2)
            return table[i] + (p - i) * (table[i + 1] - table[i])
        return sampled
    kind = struct.unpack_from(">H", tag, 8)[0]
    p = [fixed(tag, 12 + 4 * i) for i in range((1, 3, 4, 5, 7)[kind])]

    def para(x):
        if kind == 0:
            y = x ** p[0]
        elif kind == 1:
            y = (p[1] * x + p[2]) ** p[0] if x >= -p[2] / p[1] else 0
        elif kind == 2:
            y = (p[1] * x + p[2]) ** p[0] + p[3] if x >= -p[2] / p[1] else p[3]
        elif kind == 3:
            y = (p[1] * x + p[2]) ** p[0] if x >= p[4] else p[3] * x
        else:
            y = (p[1] * x + p[2]) ** p[0] + p[5] if x >= p[4] else p[3] * x + p[6]
        return min(max(y, 0.0), 1.0)
    return para


def encode(v: float) -> float:
    v = min(max(v, 0.0), 1.0)
    return 12.92 * v if v <= 0.0031308 else 1.055 * v ** (1 / 2.4) - 0.055


def adapt_to_srgb():
    source, target = mul(BRADFORD, D50), mul(BRADFORD, D65)
    scaled = [[target[r] / source[r] * BRADFORD[r][c] for c in range(3)] for r in range(3)]
    adapt = [[sum(BRADFORD_INV[r][k] * scaled[k][c] for k in range(3)) for c in range(3)] for r in range(3)]
    return [[sum(XYZ_TO_SRGB[r][k] * adapt[k][c] for k in range(3)) for c in range(3)] for r in range(3)]


def reference(profile: bytes, sample) -> tuple[float, ...]:
    t = tags(profile)
    if len(sample) == 1:
        y = curve(t[b"kTRC"])(sample[0])
        xyz = tuple(w * y for w in D50)
    else:
        linear = [curve(t[sig])(v) for sig, v in zip((b"rTRC", b"gTRC", b"bTRC"), sample)]
        columns = [(fixed(t[sig], 8), fixed(t[sig], 12), fixed(t[sig], 16)) for sig in (b"rXYZ", b"gXYZ", b"bXYZ")]
        xyz = tuple(sum(columns[c][r] * linear[c] for c in range(3)) for r in range(3))
    rgb = mul(adapt_to_srgb(), xyz)
    return tuple(255 * encode(v) for v in rgb)


def little_cms():
    candidates = [os.environ.get("LCMS2_LIBRARY"), ctypes.util.find_library("lcms2"),
                  "/opt/homebrew/lib/liblcms2.dylib", "/usr/local/lib/liblcms2.dylib", "liblcms2.so.2"]
    lib = None
    for candidate in candidates:
        if not candidate:
            continue
        try:
            lib = ctypes.CDLL(candidate)
            break
        except OSError:
            continue
    if lib is None:
        return None
    lib.cmsOpenProfileFromMem.restype = ctypes.c_void_p
    lib.cmsOpenProfileFromMem.argtypes = [ctypes.c_char_p, ctypes.c_uint32]
    lib.cmsCreate_sRGBProfile.restype = ctypes.c_void_p
    lib.cmsCreateTransform.restype = ctypes.c_void_p
    lib.cmsCreateTransform.argtypes = [ctypes.c_void_p, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_uint32]
    lib.cmsDoTransform.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32]
    lib.cmsDeleteTransform.argtypes = [ctypes.c_void_p]
    lib.cmsCloseProfile.argtypes = [ctypes.c_void_p]
    lib.cmsGetEncodedCMMversion.restype = ctypes.c_int
    return lib


def lcms_convert(lib, profile: bytes, sample) -> tuple[float, ...]:
    source = lib.cmsOpenProfileFromMem(profile, len(profile))
    srgb = lib.cmsCreate_sRGBProfile()
    kind = TYPE_GRAY_DBL if len(sample) == 1 else TYPE_RGB_DBL
    transform = lib.cmsCreateTransform(source, kind, srgb, TYPE_RGB_DBL, INTENT_RELATIVE_COLORIMETRIC, CMS_FLAGS_NOOPTIMIZE)
    if not transform:
        raise RuntimeError("Little CMS refused the profile")
    data = (ctypes.c_double * len(sample))(*sample)
    out = (ctypes.c_double * 3)()
    lib.cmsDoTransform(transform, data, out, 1)
    lib.cmsDeleteTransform(transform)
    lib.cmsCloseProfile(source)
    lib.cmsCloseProfile(srgb)
    return tuple(255 * min(max(v, 0.0), 1.0) for v in out)


def main() -> int:
    generator = load_generator()
    profiles = generator.icc_matrix_profiles()
    lib = little_cms()
    worst = 0.0
    for index, (name, profile) in enumerate(profiles.items()):
        samples = generator.ICC_MATRIX_SAMPLES["rgb" if index < 2 else "gray"]
        for sample in samples:
            ours = reference(profile, sample)
            line = '{ "%s", [%s], [%s] },' % (name, ", ".join("%gf" % v for v in sample), ", ".join("%.3f" % v for v in ours))
            if lib is not None:
                theirs = lcms_convert(lib, profile, sample)
                diff = max(abs(a - b) for a, b in zip(ours, theirs))
                worst = max(worst, diff)
                line += "  // Little CMS %s (%.3f)" % (" ".join("%.3f" % v for v in theirs), diff)
            print(line)
    if lib is None:
        print("Little CMS not found: values not cross-checked", file=sys.stderr)
        return 0
    print("Little CMS %d: largest difference %.3f of 255" % (lib.cmsGetEncodedCMMversion(), worst))
    return 0 if worst <= 0.5 else 1


if __name__ == "__main__":
    sys.exit(main())
