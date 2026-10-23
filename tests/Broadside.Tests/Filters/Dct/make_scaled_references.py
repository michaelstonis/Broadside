"""Writes scaled-references.txt: libjpeg-turbo's reduced decodes of every DCT test vector, at 1/2, 1/4 and 1/8 (issue #362).

Usage: make_scaled_references.py [<vector dir>]   (default: this script's directory)

For each vector and scale it records the output size and the SHA-256 of the 8-bit interleaved samples libjpeg-turbo decodes with
the islow IDCT and replication upsampling: `djpeg -scale 1/s -dct int -nosmooth -pnm` for one or three components, TurboJPEG's raw
CMYK decode (tj3SetScalingFactor, TJPARAM_FASTUPSAMPLE 1, TJPARAM_FASTDCT 0) for four. 12-bit samples are reduced to 8 bits by
(255 v + 2047) / 4095, as the filter delivers them (ISO 32000-2 Table 87). Run with libjpeg-turbo 3.2.0 on PATH (JSIMD_FORCENONE=1
is set; the SIMD build gives the same bytes); set TURBOJPEG to the library's path when ctypes cannot find it. dnl.jpg is skipped:
djpeg refuses DNL.
"""
import ctypes, ctypes.util, hashlib, os, pathlib, re, subprocess, sys

here = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else pathlib.Path(__file__).parent)
env = dict(os.environ, JSIMD_FORCENONE="1")

library = os.environ.get("TURBOJPEG") or ctypes.util.find_library("turbojpeg") or "/opt/homebrew/opt/jpeg-turbo/lib/libturbojpeg.dylib"
tj = ctypes.CDLL(library)
tj.tj3InitVersion.restype = ctypes.c_void_p
tj.tj3InitVersion.argtypes = [ctypes.c_int, ctypes.c_int]
tj.tj3Set.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
tj.tj3Get.argtypes = [ctypes.c_void_p, ctypes.c_int]
tj.tj3DecompressHeader.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t]


class ScalingFactor(ctypes.Structure):
    _fields_ = [("num", ctypes.c_int), ("denom", ctypes.c_int)]


tj.tj3SetScalingFactor.argtypes = [ctypes.c_void_p, ScalingFactor]
tj.tj3Decompress8.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
tj.tj3Decompress12.argtypes = tj.tj3Decompress8.argtypes
tj.tj3Destroy.argtypes = [ctypes.c_void_p]
tj.tj3GetErrorStr.restype = ctypes.c_char_p
tj.tj3GetErrorStr.argtypes = [ctypes.c_void_p]
TJINIT_DECOMPRESS, VERSION = 1, 3002000
TJPARAM_JPEGWIDTH, TJPARAM_JPEGHEIGHT, TJPARAM_PRECISION, TJPARAM_FASTUPSAMPLE, TJPARAM_FASTDCT, TJPARAM_COLORSPACE = 5, 6, 7, 9, 10, 8
TJPF_CMYK = 11


def check(handle, result):
    if result != 0:
        raise RuntimeError(tj.tj3GetErrorStr(handle).decode())


def reduce12(values):
    return bytes((255 * v + 2047) // 4095 for v in values)


def turbojpeg_cmyk(jpeg, scale):
    handle = tj.tj3InitVersion(TJINIT_DECOMPRESS, VERSION)
    check(handle, tj.tj3Set(handle, TJPARAM_FASTUPSAMPLE, 1))
    check(handle, tj.tj3Set(handle, TJPARAM_FASTDCT, 0))
    check(handle, tj.tj3DecompressHeader(handle, jpeg, len(jpeg)))
    check(handle, tj.tj3SetScalingFactor(handle, ScalingFactor(1, scale)))
    width = (tj.tj3Get(handle, TJPARAM_JPEGWIDTH) + scale - 1) // scale
    height = (tj.tj3Get(handle, TJPARAM_JPEGHEIGHT) + scale - 1) // scale
    if tj.tj3Get(handle, TJPARAM_PRECISION) == 8:
        target = (ctypes.c_ubyte * (width * height * 4))()
        check(handle, tj.tj3Decompress8(handle, jpeg, len(jpeg), target, 0, TJPF_CMYK))
        samples = bytes(target)
    else:
        target = (ctypes.c_short * (width * height * 4))()
        check(handle, tj.tj3Decompress12(handle, jpeg, len(jpeg), target, 0, TJPF_CMYK))
        samples = reduce12(target)
    tj.tj3Destroy(handle)
    return width, height, 4, samples


def djpeg(path, scale):
    data = subprocess.run(["djpeg", "-scale", f"1/{scale}", "-dct", "int", "-nosmooth", "-pnm", str(path)],
                          check=True, capture_output=True, env=env).stdout
    header = re.match(rb"(P[56])\s+(\d+)\s+(\d+)\s+(\d+)\s", data)
    magic, width, height, maxval = header.groups()
    pixels = data[header.end():]
    components = 1 if magic == b"P5" else 3
    width, height, maxval = int(width), int(height), int(maxval)
    if maxval == 255:
        samples = pixels
    else:
        samples = reduce12(int.from_bytes(pixels[i:i + 2], "big") for i in range(0, len(pixels), 2))
    return width, height, components, samples


def components(jpeg):
    pos = 2
    while pos < len(jpeg):
        marker, length = jpeg[pos + 1], (jpeg[pos + 2] << 8) | jpeg[pos + 3]
        if marker in (0xC0, 0xC1, 0xC2, 0xC9, 0xCA):
            return jpeg[pos + 9]
        pos += 2 + length
    raise ValueError("no frame")


lines = []
for path in sorted(here.glob("*.jpg")):
    if path.name == "dnl.jpg":
        continue
    jpeg = path.read_bytes()
    for scale in (2, 4, 8):
        if components(jpeg) == 4:
            width, height, count, samples = turbojpeg_cmyk(jpeg, scale)
        else:
            width, height, count, samples = djpeg(path, scale)
        assert len(samples) == width * height * count, path.name
        lines.append(f"{path.name} {scale} {width} {height} {count} {hashlib.sha256(samples).hexdigest()}")

(here / "scaled-references.txt").write_text(
    "# libjpeg-turbo 3.2.0 reduced decodes of the DCT vectors (make_scaled_references.py): name, 1/scale, width, height, components,\n"
    "# SHA-256 of the 8-bit interleaved samples.\n" + "\n".join(lines) + "\n")
print(f"{len(lines)} references")
