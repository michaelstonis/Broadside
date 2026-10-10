"""Writes libjpeg-turbo references for the JPEG images of the fetched real-world corpora.

Usage: python3 -I make_corpus_references.py <corpus dir>

For every PDF under the corpus directory, poppler's ``pdfimages -j`` extracts the DCTDecode images (the bytes the DCT filter
receives, after any filter before it). Every baseline or extended-sequential (SOF0/SOF1) 8-bit JPEG of one or three components
that libjpeg-turbo decodes without a warning gets ``<corpus dir>/references/dct/<sha-256 of the JPEG>.pnm``, written by
``djpeg -dct int -nosmooth -pnm`` (the reference of the +-1 acceptance; JSIMD_FORCENONE=1). CorpusDctReferenceTests compares the
managed decoder against these files and skips when there are none. Needs pdfimages and djpeg (libjpeg-turbo 3.2.0 was used) on
PATH.
"""
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
            if not jpeg.startswith(b"\xff\xd8") or frame(jpeg) not in [(m, 8, n) for m in (0xC0, 0xC1) for n in (1, 3)]:
                continue
            target = out / (hashlib.sha256(jpeg).hexdigest() + ".pnm")
            if target.exists():
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
