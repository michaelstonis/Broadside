"""Builds the DCT test vectors and their libjpeg-turbo goldens.

Usage: make_vectors.py <download dir> <output dir> [<tests/Corpus directory>]

The download directory holds testorig.jpg, testimgint.jpg, testimgari.jpg and testorig.ppm from libjpeg-turbo's testimages at
commit 43ea809765004508d8da72ebe562cf570c6a62b2. With the corpus directory, the goldens of the images of dct-baseline.pdf,
dct-progressive.pdf and dct-cmyk.pdf are written too (the JPEGs are extracted with poppler's pdfimages -j). Needs libjpeg-turbo's cjpeg, djpeg and jpegtran on PATH and its TurboJPEG library
(3.2.0 was used; set TURBOJPEG to the library's path when ctypes cannot find it), for the CMYK and YCCK vectors.
"""
import ctypes, ctypes.util, gzip, hashlib, os, pathlib, shutil, subprocess, sys

src = pathlib.Path(sys.argv[1])
out = pathlib.Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, JSIMD_FORCENONE="1")


def run(*args, stdin=None):
    return subprocess.run(args, check=True, capture_output=True, env=env, input=stdin).stdout


def read_pnm(data):
    parts = data.split(maxsplit=4)
    return parts[0], int(parts[1]), int(parts[2]), int(parts[3]), parts[4]


# 75 x 43 crop of testorig.ppm at (80, 40): odd sizes, partial MCUs in both directions for every subsampling.
magic, w, h, maxval, pixels = read_pnm((src / "testorig.ppm").read_bytes())
assert magic == b"P6" and maxval == 255
cx, cy, cw, ch = 80, 40, 75, 43
crop = bytearray()
for y in range(cy, cy + ch):
    crop += pixels[(y * w + cx) * 3:(y * w + cx + cw) * 3]
crop_ppm = b"P6\n%d %d\n255\n" % (cw, ch) + bytes(crop)
tmp = out / "_crop.ppm"
tmp.write_bytes(crop_ppm)

shutil.copy(src / "testorig.jpg", out / "testorig.jpg")
shutil.copy(src / "testimgint.jpg", out / "testimgint.jpg")
shutil.copy(src / "testimgari.jpg", out / "testimgari.jpg")


def cjpeg(name, *args):
    (out / name).write_bytes(run("cjpeg", "-dct", "int", *args, str(tmp)))


cjpeg("sampling-444.jpg", "-sample", "1x1")
cjpeg("sampling-422.jpg", "-sample", "2x1", "-restart", "1")
cjpeg("sampling-440.jpg", "-sample", "1x2")
cjpeg("sampling-420.jpg", "-sample", "2x2", "-restart", "1")
cjpeg("sampling-411.jpg", "-sample", "4x1,1x1,1x1")
cjpeg("gray-2x2.jpg", "-grayscale", "-sample", "2x2")
cjpeg("rgb.jpg", "-rgb")
cjpeg("precision12.jpg", "-precision", "12", "-sample", "2x2", "-restart", "1")
cjpeg("precision12-gray.jpg", "-precision", "12", "-grayscale", "-quality", "15")

scan = out / "_seq.scan"
scan.write_text("0;\n1;\n2;\n")
(out / "multiscan.jpg").write_bytes(run("jpegtran", "-scans", str(scan), str(out / "sampling-420.jpg")))
(out / "restart-blocks.jpg").write_bytes(run("jpegtran", "-restart", "2B", str(src / "testorig.jpg")))

# Progressive (SOF2) and arithmetic-coded (SOF9, SOF10) re-encodings: jpegtran keeps the quantized coefficients, so each decodes
# exactly like its source. The scan script refines DC and AC by three successive-approximation steps in an unusual band order.
script = out / "_progressive.scan"
script.write_text(
    "0,1,2: 0-0, 0, 3;\n0: 1-5, 0, 3;\n0: 6-63, 0, 3;\n1: 1-63, 0, 2;\n2: 1-63, 0, 2;\n0: 1-63, 3, 2;\n0,1,2: 0-0, 3, 2;\n"
    "0: 1-63, 2, 1;\n1: 1-63, 2, 1;\n2: 1-63, 2, 1;\n0,1,2: 0-0, 2, 1;\n0,1,2: 0-0, 1, 0;\n0: 1-63, 1, 0;\n1: 1-63, 1, 0;\n"
    "2: 1-63, 1, 0;\n")


def jpegtran(name, source, *args):
    (out / name).write_bytes(run("jpegtran", *args, str(source)))


jpegtran("progressive.jpg", src / "testorig.jpg", "-progressive")
jpegtran("progressive-gray-2x2.jpg", out / "gray-2x2.jpg", "-progressive")
jpegtran("progressive-restart.jpg", out / "sampling-420.jpg", "-progressive", "-restart", "3B")
jpegtran("progressive-scans.jpg", out / "sampling-444.jpg", "-scans", str(script))
jpegtran("arithmetic.jpg", out / "sampling-420.jpg", "-arithmetic")
jpegtran("arithmetic-progressive.jpg", src / "testorig.jpg", "-arithmetic", "-progressive")
jpegtran("arithmetic-progressive-scans.jpg", out / "sampling-444.jpg", "-arithmetic", "-scans", str(script), "-restart", "5B")
jpegtran("precision12-progressive.jpg", out / "precision12.jpg", "-progressive")
jpegtran("precision12-arithmetic.jpg", out / "precision12.jpg", "-arithmetic", "-progressive")
script.unlink()

base = (out / "sampling-420.jpg").read_bytes()


def segments(data):
    """Yields (offset, marker, length) of each marker segment before the first SOS."""
    pos = 2
    while pos < len(data):
        assert data[pos] == 0xFF
        marker = data[pos + 1]
        length = (data[pos + 2] << 8) | data[pos + 3]
        yield pos, marker, length
        if marker == 0xDA:
            return
        pos += 2 + length


# SOF1 (extended sequential) with the same content.
sof = next(off for off, m, _ in segments(base) if m == 0xC0)
sof1 = bytearray(base)
sof1[sof + 1] = 0xC1
(out / "sof1.jpg").write_bytes(bytes(sof1))

# Y = 0 in the frame header and a DNL segment (FF DC, Lq 4, NL) after the scan, before EOI.
dnl = bytearray(base)
dnl[sof + 5] = 0
dnl[sof + 6] = 0
assert dnl[-2:] == b"\xff\xd9"
dnl[-2:-2] = b"\xff\xdc\x00\x04" + ch.to_bytes(2, "big")
(out / "dnl.jpg").write_bytes(bytes(dnl))

# No DHT segments: the decoder must install the Annex K.3 tables cjpeg used.
no_dht = bytearray()
last = 0
for off, m, length in segments(base):
    if m == 0xC4:
        no_dht += base[last:off]
        last = off + 2 + length
no_dht += base[last:]
(out / "no-dht.jpg").write_bytes(bytes(no_dht))

# CMYK and YCCK (Adobe APP14 transform 0 and 2) through TurboJPEG, which cjpeg cannot write: the crop as CMYK (C = 255 - R,
# M = 255 - G, Y = 255 - B, K = a ramp), 8-bit and 12-bit.
library = os.environ.get("TURBOJPEG") or ctypes.util.find_library("turbojpeg") or "/opt/homebrew/opt/jpeg-turbo/lib/libturbojpeg.dylib"
tj = ctypes.CDLL(library)
tj.tj3InitVersion.restype = ctypes.c_void_p
tj.tj3InitVersion.argtypes = [ctypes.c_int, ctypes.c_int]
tj.tj3Set.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
tj.tj3Get.argtypes = [ctypes.c_void_p, ctypes.c_int]
tj.tj3DecompressHeader.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t]
tj.tj3Decompress8.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
tj.tj3Decompress12.argtypes = tj.tj3Decompress8.argtypes
tj.tj3Compress8.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                            ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_size_t)]
tj.tj3Compress12.argtypes = tj.tj3Compress8.argtypes
tj.tj3Free.argtypes = [ctypes.c_void_p]
tj.tj3Destroy.argtypes = [ctypes.c_void_p]
tj.tj3GetErrorStr.restype = ctypes.c_char_p
tj.tj3GetErrorStr.argtypes = [ctypes.c_void_p]
# turbojpeg.h 3.2.0: TJINIT, TJPARAM, TJPF, TJCS and TJSAMP enum values, and TURBOJPEG_VERSION_NUMBER.
TJINIT_COMPRESS, TJINIT_DECOMPRESS, VERSION = 0, 1, 3002000
TJPARAM_QUALITY, TJPARAM_SUBSAMP, TJPARAM_JPEGWIDTH, TJPARAM_JPEGHEIGHT, TJPARAM_PRECISION = 3, 4, 5, 6, 7
TJPARAM_COLORSPACE, TJPARAM_FASTUPSAMPLE, TJPARAM_FASTDCT = 8, 9, 10
TJPF_CMYK, TJCS_CMYK, TJCS_YCCK, TJSAMP_444, TJSAMP_420 = 11, 3, 4, 0, 2


def check(handle, result):
    if result != 0:
        raise RuntimeError(tj.tj3GetErrorStr(handle).decode())


def tj_compress(samples, width, height, colorspace, subsamp, precision):
    handle = tj.tj3InitVersion(TJINIT_COMPRESS, VERSION)
    for param, value in ((TJPARAM_QUALITY, 85), (TJPARAM_SUBSAMP, subsamp), (TJPARAM_COLORSPACE, colorspace),
                         (TJPARAM_PRECISION, precision), (TJPARAM_FASTDCT, 0)):
        check(handle, tj.tj3Set(handle, param, value))
    buffer, size = ctypes.c_void_p(), ctypes.c_size_t()
    if precision == 8:
        source = (ctypes.c_ubyte * len(samples)).from_buffer_copy(bytes(samples))
        check(handle, tj.tj3Compress8(handle, source, width, 0, height, TJPF_CMYK, ctypes.byref(buffer), ctypes.byref(size)))
    else:
        source = (ctypes.c_short * len(samples))(*[v * 4095 // 255 for v in samples])
        check(handle, tj.tj3Compress12(handle, source, width, 0, height, TJPF_CMYK, ctypes.byref(buffer), ctypes.byref(size)))
    data = ctypes.string_at(buffer, size.value)
    tj.tj3Free(buffer)
    tj.tj3Destroy(handle)
    return data


def tj_decompress_cmyk(jpeg):
    """A PAM of the raw CMYK samples (islow IDCT, replication upsampling); TurboJPEG converts YCCK to CMYK without inverting."""
    handle = tj.tj3InitVersion(TJINIT_DECOMPRESS, VERSION)
    check(handle, tj.tj3Set(handle, TJPARAM_FASTUPSAMPLE, 1))
    check(handle, tj.tj3Set(handle, TJPARAM_FASTDCT, 0))
    check(handle, tj.tj3DecompressHeader(handle, jpeg, len(jpeg)))
    width, height = tj.tj3Get(handle, TJPARAM_JPEGWIDTH), tj.tj3Get(handle, TJPARAM_JPEGHEIGHT)
    precision = tj.tj3Get(handle, TJPARAM_PRECISION)
    if precision == 8:
        target = (ctypes.c_ubyte * (width * height * 4))()
        check(handle, tj.tj3Decompress8(handle, jpeg, len(jpeg), target, 0, TJPF_CMYK))
        samples = bytes(target)
    else:
        target = (ctypes.c_short * (width * height * 4))()
        check(handle, tj.tj3Decompress12(handle, jpeg, len(jpeg), target, 0, TJPF_CMYK))
        samples = b"".join(v.to_bytes(2, "big") for v in target)
    tj.tj3Destroy(handle)
    maxval = 255 if precision == 8 else 4095
    return b"P7\nWIDTH %d\nHEIGHT %d\nDEPTH 4\nMAXVAL %d\nTUPLTYPE CMYK\nENDHDR\n" % (width, height, maxval) + samples


cmyk = bytearray()
for y in range(ch):
    for x in range(cw):
        r, g, b = crop[(y * cw + x) * 3:(y * cw + x) * 3 + 3]
        cmyk += bytes((255 - r, 255 - g, 255 - b, (x * 255) // (cw - 1) * (ch - y) // ch))
(out / "cmyk.jpg").write_bytes(tj_compress(cmyk, cw, ch, TJCS_CMYK, TJSAMP_444, 8))
(out / "ycck.jpg").write_bytes(tj_compress(cmyk, cw, ch, TJCS_YCCK, TJSAMP_420, 8))
(out / "ycck-precision12.jpg").write_bytes(tj_compress(cmyk, cw, ch, TJCS_YCCK, TJSAMP_420, 12))
jpegtran("ycck-progressive.jpg", out / "ycck.jpg", "-progressive")
jpegtran("ycck-arithmetic.jpg", out / "ycck.jpg", "-arithmetic")

for p in (tmp, scan):
    p.unlink()


def golden(name):
    if name.startswith(("cmyk", "ycck", "dct-cmyk")):
        data = tj_decompress_cmyk((out / name).read_bytes())
    else:
        data = run("djpeg", "-dct", "int", "-nosmooth", "-pnm", str(out / name))
    stem = name[:-4]
    kind = {b"P5": "pgm", b"P6": "ppm", b"P7": "pam"}[data[:2]]
    with open(out / f"{stem}.{kind}.gz", "wb") as f:
        with gzip.GzipFile(filename="", mode="wb", fileobj=f, mtime=0, compresslevel=9) as g:
            g.write(data)
    return data


goldens = {}
for name in sorted(p.name for p in out.glob("*.jpg")):
    if name in ("dnl.jpg",):
        continue
    goldens[name] = golden(name)

# Same-content vectors decode exactly like their base (verified here, so the tests compare against the base golden).
same = {
    "sof1.jpg": "sampling-420.jpg", "no-dht.jpg": "sampling-420.jpg", "multiscan.jpg": "sampling-420.jpg",
    "restart-blocks.jpg": "testorig.jpg", "progressive.jpg": "testorig.jpg", "progressive-gray-2x2.jpg": "gray-2x2.jpg",
    "progressive-restart.jpg": "sampling-420.jpg", "progressive-scans.jpg": "sampling-444.jpg",
    "arithmetic.jpg": "sampling-420.jpg", "arithmetic-progressive.jpg": "testorig.jpg",
    "arithmetic-progressive-scans.jpg": "sampling-444.jpg", "precision12-progressive.jpg": "precision12.jpg",
    "precision12-arithmetic.jpg": "precision12.jpg", "ycck-progressive.jpg": "ycck.jpg", "ycck-arithmetic.jpg": "ycck.jpg",
    "testimgari.jpg": "testimgint.jpg",
}
for name, base_name in same.items():
    assert goldens[name] == goldens[base_name], name
    for kind in ("pgm", "ppm", "pam"):
        (out / f"{name[:-4]}.{kind}.gz").unlink(missing_ok=True)

if len(sys.argv) > 3:
    corpus = pathlib.Path(sys.argv[3])
    images = {
        "dct-baseline.pdf": ["dct-baseline-color.jpg", "dct-baseline-gray.jpg"],
        "dct-progressive.pdf": ["dct-progressive.jpg"],
        "dct-cmyk.pdf": ["dct-cmyk-cmyk.jpg", "dct-cmyk-ycck.jpg"],
    }
    for pdf, names in images.items():
        extracted = out / "_extracted"
        extracted.mkdir()
        run("pdfimages", "-j", str(corpus / pdf), str(extracted / "img"))
        for index, name in enumerate(names):
            shutil.move(extracted / f"img-{index:03}.jpg", out / name)
            golden(name)
            (out / name).unlink()
        extracted.rmdir()

for p in sorted(out.iterdir()):
    print(f"{p.name:28} {p.stat().st_size:7} {hashlib.sha256(p.read_bytes()).hexdigest()}")
