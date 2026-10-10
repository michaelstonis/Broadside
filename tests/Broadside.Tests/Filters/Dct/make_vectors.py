"""Builds the DCT test vectors and their libjpeg-turbo goldens.

Usage: make_vectors.py <download dir> <output dir> [<tests/Corpus/dct-baseline.pdf>]

The download directory holds testorig.jpg, testimgint.jpg and testorig.ppm from libjpeg-turbo's testimages at commit
43ea809765004508d8da72ebe562cf570c6a62b2. With the corpus file, the goldens of its two images are written too (the JPEGs are
extracted with poppler's pdfimages -j). Needs libjpeg-turbo's cjpeg, djpeg and jpegtran on PATH (3.2.0 was used).
"""
import gzip, hashlib, os, pathlib, shutil, subprocess, sys

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


def cjpeg(name, *args):
    (out / name).write_bytes(run("cjpeg", "-dct", "int", *args, str(tmp)))


cjpeg("sampling-444.jpg", "-sample", "1x1")
cjpeg("sampling-422.jpg", "-sample", "2x1", "-restart", "1")
cjpeg("sampling-440.jpg", "-sample", "1x2")
cjpeg("sampling-420.jpg", "-sample", "2x2", "-restart", "1")
cjpeg("sampling-411.jpg", "-sample", "4x1,1x1,1x1")
cjpeg("gray-2x2.jpg", "-grayscale", "-sample", "2x2")
cjpeg("rgb.jpg", "-rgb")

scan = out / "_seq.scan"
scan.write_text("0;\n1;\n2;\n")
(out / "multiscan.jpg").write_bytes(run("jpegtran", "-scans", str(scan), str(out / "sampling-420.jpg")))
(out / "restart-blocks.jpg").write_bytes(run("jpegtran", "-restart", "2B", str(src / "testorig.jpg")))

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

for p in (tmp, scan):
    p.unlink()


def golden(name):
    data = run("djpeg", "-dct", "int", "-nosmooth", "-pnm", str(out / name))
    stem = name[:-4]
    kind = "pgm" if data.startswith(b"P5") else "ppm"
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
for name in ("sof1.jpg", "no-dht.jpg", "multiscan.jpg"):
    assert goldens[name] == goldens["sampling-420.jpg"], name
    (out / f"{name[:-4]}.ppm.gz").unlink()
assert goldens["restart-blocks.jpg"] == goldens["testorig.jpg"]
(out / "restart-blocks.ppm.gz").unlink()

if len(sys.argv) > 3:
    extracted = out / "_extracted"
    extracted.mkdir()
    run("pdfimages", "-j", sys.argv[3], str(extracted / "img"))
    shutil.move(extracted / "img-000.jpg", out / "dct-baseline-color.jpg")
    shutil.move(extracted / "img-001.jpg", out / "dct-baseline-gray.jpg")
    extracted.rmdir()
    for name in ("dct-baseline-color.jpg", "dct-baseline-gray.jpg"):
        golden(name)
        (out / name).unlink()

for p in sorted(out.iterdir()):
    print(f"{p.name:28} {p.stat().st_size:7} {hashlib.sha256(p.read_bytes()).hexdigest()}")
