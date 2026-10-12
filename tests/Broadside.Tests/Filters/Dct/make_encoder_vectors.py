"""Reference encodings for the DCT encoder (issue #143), written by libjpeg-turbo's TurboJPEG API.

    python3 -I tests/Broadside.Tests/Filters/Dct/make_encoder_vectors.py [output dir]

The sources are reference decodes already committed next to this script: rgb.ppm.gz (75 x 43 RGB), cmyk.pam.gz (75 x 43 CMYK)
and dct-baseline-gray.pgm.gz (37 x 21 gray). Each is compressed with the integer DCT (TJPARAM_FASTDCT 0), the Annex K Huffman
tables (TJPARAM_OPTIMIZE 0) and the IJG quality scaling, which is what DctEncoder implements, so its output must equal these files
byte for byte. Needs libturbojpeg (3.x) via ctypes; set TURBOJPEG to its path when ctypes cannot find it.
"""

import ctypes
import ctypes.util
import gzip
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[1] if len(sys.argv) > 1 else HERE

library = os.environ.get("TURBOJPEG") or ctypes.util.find_library("turbojpeg") or "/opt/homebrew/opt/jpeg-turbo/lib/libturbojpeg.dylib"
tj = ctypes.CDLL(library)
tj.tj3Init.restype = ctypes.c_void_p
tj.tj3Init.argtypes = [ctypes.c_int]
tj.tj3Set.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
tj.tj3Compress8.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                            ctypes.POINTER(ctypes.POINTER(ctypes.c_ubyte)), ctypes.POINTER(ctypes.c_size_t)]
tj.tj3Free.argtypes = [ctypes.c_void_p]
tj.tj3Destroy.argtypes = [ctypes.c_void_p]
tj.tj3GetErrorStr.restype = ctypes.c_char_p
tj.tj3GetErrorStr.argtypes = [ctypes.c_void_p]

# turbojpeg.h 3.2.0
TJINIT_COMPRESS = 0
TJPARAM_QUALITY, TJPARAM_SUBSAMP, TJPARAM_COLORSPACE, TJPARAM_FASTDCT, TJPARAM_OPTIMIZE = 3, 4, 8, 10, 11
TJSAMP = {"444": 0, "422": 1, "420": 2, "gray": 3}
TJCS = {"rgb": 0, "ycbcr": 1, "gray": 2, "cmyk": 3, "ycck": 4}
TJPF = {1: 6, 3: 0, 4: 11}  # TJPF_GRAY, TJPF_RGB, TJPF_CMYK


def read_netpbm(name):
    data = gzip.open(os.path.join(HERE, name)).read()
    if data.startswith(b"P7"):
        header, _, body = data.partition(b"ENDHDR\n")
        fields = dict(line.split(b" ", 1) for line in header.split(b"\n")[1:] if b" " in line)
        return int(fields[b"WIDTH"]), int(fields[b"HEIGHT"]), int(fields[b"DEPTH"]), body
    tokens = data.split(maxsplit=4)
    components = 3 if tokens[0] == b"P6" else 1
    return int(tokens[1]), int(tokens[2]), components, tokens[4]


def compress(source, quality, subsampling, colorspace):
    width, height, components, pixels = read_netpbm(source)
    handle = tj.tj3Init(TJINIT_COMPRESS)
    try:
        for parameter, value in ((TJPARAM_QUALITY, quality), (TJPARAM_SUBSAMP, TJSAMP[subsampling]),
                                 (TJPARAM_COLORSPACE, TJCS[colorspace]), (TJPARAM_FASTDCT, 0), (TJPARAM_OPTIMIZE, 0)):
            if tj.tj3Set(handle, parameter, value) != 0:
                raise RuntimeError(tj.tj3GetErrorStr(handle))
        buffer = ctypes.POINTER(ctypes.c_ubyte)()
        size = ctypes.c_size_t(0)
        if tj.tj3Compress8(handle, pixels, width, 0, height, TJPF[components], ctypes.byref(buffer), ctypes.byref(size)) != 0:
            raise RuntimeError(tj.tj3GetErrorStr(handle))
        jpeg = ctypes.string_at(buffer, size.value)
        tj.tj3Free(buffer)
        return jpeg
    finally:
        tj.tj3Destroy(handle)


# name: (source, quality, subsampling, colorspace)
VECTORS = {
    "encoded-rgb-q90-444": ("rgb.ppm.gz", 90, "444", "ycbcr"),
    "encoded-rgb-q90-422": ("rgb.ppm.gz", 90, "422", "ycbcr"),
    "encoded-rgb-q90-420": ("rgb.ppm.gz", 90, "420", "ycbcr"),
    "encoded-rgb-q75-420": ("rgb.ppm.gz", 75, "420", "ycbcr"),
    "encoded-rgb-q10-420": ("rgb.ppm.gz", 10, "420", "ycbcr"),
    "encoded-rgb-q100-444": ("rgb.ppm.gz", 100, "444", "ycbcr"),
    "encoded-rgb-untransformed-q90": ("rgb.ppm.gz", 90, "444", "rgb"),
    "encoded-gray-q90": ("dct-baseline-gray.pgm.gz", 90, "gray", "gray"),
    "encoded-cmyk-q90": ("cmyk.pam.gz", 90, "444", "cmyk"),
    "encoded-ycck-q90-420": ("cmyk.pam.gz", 90, "420", "ycck"),
}

for name, (source, quality, subsampling, colorspace) in VECTORS.items():
    with open(os.path.join(OUT, name + ".jpg"), "wb") as handle:
        handle.write(compress(source, quality, subsampling, colorspace))
    print(name)
