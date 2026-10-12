"""Writes libjpeg-turbo references for the JPEG images of the fetched real-world corpora.

Usage: python3 -I make_corpus_references.py <corpus dir>

For every PDF under the corpus directory, poppler's ``pdfimages -j`` extracts the DCTDecode images (the bytes the DCT filter
receives, after any filter before it). Every baseline, extended, progressive or arithmetic-coded (SOF0, SOF1, SOF2, SOF9, SOF10)
JPEG of 8 or 12 bits that libjpeg-turbo decodes without a warning gets ``<corpus dir>/references/dct/<sha-256 of the JPEG>.pnm``:
for one or three components ``djpeg -dct int -nosmooth -pnm`` (JSIMD_FORCENONE=1), for four components a PAM of TurboJPEG's raw
CMYK samples (islow IDCT, replication upsampling; TurboJPEG converts YCCK to CMYK without inverting, like the filter). These are
the references of the +-1 acceptance; CorpusDctReferenceTests compares the managed decoder against them and skips when there are
none. Needs pdfimages and djpeg on PATH and the TurboJPEG library (libjpeg-turbo 3.2.0 was used; set TURBOJPEG to its path when
ctypes cannot find it).
"""
import ctypes
import ctypes.util
import hashlib
import os
import pathlib
import subprocess
import sys
import tempfile

corpus = pathlib.Path(sys.argv[1]).resolve()
out = corpus / "references" / "dct"
out.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, JSIMD_FORCENONE="1")


def frame(jpeg: bytes) -> tuple[int, int, int] | None:
    """(SOF marker, precision, components) of the first frame header, or None."""
    pos = 2
    while pos + 4 <= len(jpeg):
        if jpeg[pos] != 0xFF:
            return None
        marker = jpeg[pos + 1]
        if marker == 0xFF:
            pos += 1
            continue
        length = (jpeg[pos + 2] << 8) | jpeg[pos + 3]
        if 0xC0 <= marker <= 0xCF and marker not in (0xC4, 0xC8, 0xCC):
            return (marker, jpeg[pos + 4], jpeg[pos + 9]) if pos + 10 <= len(jpeg) else None
        if marker == 0xDA:
            return None
        pos += 2 + length
    return None


library = os.environ.get("TURBOJPEG") or ctypes.util.find_library("turbojpeg") or "/opt/homebrew/opt/jpeg-turbo/lib/libturbojpeg.dylib"
tj = ctypes.CDLL(library)
tj.tj3InitVersion.restype = ctypes.c_void_p
tj.tj3InitVersion.argtypes = [ctypes.c_int, ctypes.c_int]
tj.tj3Set.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
tj.tj3Get.argtypes = [ctypes.c_void_p, ctypes.c_int]
tj.tj3DecompressHeader.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t]
tj.tj3Decompress8.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_int, ctypes.c_int]
tj.tj3Decompress12.argtypes = tj.tj3Decompress8.argtypes
tj.tj3Destroy.argtypes = [ctypes.c_void_p]
tj.tj3GetErrorCode.argtypes = [ctypes.c_void_p]
# turbojpeg.h 3.2.0: TJINIT_DECOMPRESS, TJPARAM_STOPONWARNING, JPEGWIDTH, JPEGHEIGHT, PRECISION, FASTUPSAMPLE, FASTDCT, TJPF_CMYK.
TJINIT_DECOMPRESS, VERSION, STOPONWARNING, WIDTH, HEIGHT, PRECISION, FASTUPSAMPLE, FASTDCT, TJPF_CMYK = 1, 3002000, 0, 5, 6, 7, 9, 10, 11


def cmyk_reference(jpeg: bytes) -> bytes | None:
    """A PAM of TurboJPEG's raw CMYK decode, or None when it warns or fails."""
    handle = tj.tj3InitVersion(TJINIT_DECOMPRESS, VERSION)
    try:
        for param, value in ((STOPONWARNING, 1), (FASTUPSAMPLE, 1), (FASTDCT, 0)):
            tj.tj3Set(handle, param, value)
        if tj.tj3DecompressHeader(handle, jpeg, len(jpeg)) != 0:
            return None
        width, height, precision = tj.tj3Get(handle, WIDTH), tj.tj3Get(handle, HEIGHT), tj.tj3Get(handle, PRECISION)
        if precision == 8:
            target = (ctypes.c_ubyte * (width * height * 4))()
            if tj.tj3Decompress8(handle, jpeg, len(jpeg), target, 0, TJPF_CMYK) != 0:
                return None
            samples = bytes(target)
        else:
            target = (ctypes.c_short * (width * height * 4))()
            if tj.tj3Decompress12(handle, jpeg, len(jpeg), target, 0, TJPF_CMYK) != 0:
                return None
            samples = b"".join(v.to_bytes(2, "big") for v in target)
        maxval = 255 if precision == 8 else 4095
        return b"P7\nWIDTH %d\nHEIGHT %d\nDEPTH 4\nMAXVAL %d\nTUPLTYPE CMYK\nENDHDR\n" % (width, height, maxval) + samples
    finally:
        tj.tj3Destroy(handle)


kinds = [(m, p, n) for m in (0xC0, 0xC1, 0xC2, 0xC9, 0xCA) for p in (8, 12) for n in (1, 3, 4)]
written = 0
for pdf in sorted(corpus.rglob("*.pdf")):
    if out in pdf.parents:
        continue
    with tempfile.TemporaryDirectory() as temp:
        try:
            subprocess.run(["pdfimages", "-j", str(pdf), os.path.join(temp, "img")], capture_output=True, timeout=120)
        except subprocess.TimeoutExpired:
            continue
        for image in sorted(pathlib.Path(temp).glob("*.jpg")):
            jpeg = image.read_bytes()
            kind = frame(jpeg) if jpeg.startswith(b"\xff\xd8") else None
            if kind not in kinds:
                continue
            target = out / (hashlib.sha256(jpeg).hexdigest() + ".pnm")
            if target.exists():
                continue
            if kind[2] == 4:
                reference = cmyk_reference(jpeg)
                if reference is not None:
                    target.write_bytes(reference)
                    written += 1
                continue
            try:
                result = subprocess.run(["djpeg", "-dct", "int", "-nosmooth", "-pnm", str(image)],
                                        capture_output=True, env=env, timeout=120)
            except subprocess.TimeoutExpired:
                continue
            if result.returncode == 0 and not result.stderr:
                target.write_bytes(result.stdout)
                written += 1

print(f"{written} references written to {out}")
