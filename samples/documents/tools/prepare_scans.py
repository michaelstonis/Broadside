"""Makes the page images of the scanned samples from the National Archives' scans (MANIFEST.md lists them).

    python3 samples/documents/tools/prepare_scans.py <folder of original scans> samples/documents/scans

The originals are two PDFs from the National Archives catalog (as mirrored on Wikimedia Commons), each page a JPEG: the Senate
Committee on Aeronautical and Space Sciences' memo on the Phillips report (1967, 9 pages at 506 ppi, `gehrig.pdf`) and William
Safire's memo "In event of moon disaster" (1969, 2 pages, `safire.pdf`). They are not stored in the repository.

- memo-NN.tif: each page of the 1967 memo, grayscale, scaled to 300 ppi and thresholded to one bit, CCITT Group 4 TIFF. The
  G4 and JBIG2 samples are made from these.
- moon-NN.jp2: each page of the 1969 memo in color, scaled to 300 ppi (the second page was scanned at 200 ppi and is scaled up),
  JPEG 2000 (irreversible wavelet, compression ratio 60, Pillow's OpenJPEG encoder). The JPEG 2000 sample is made from these.

Needs Pillow (with OpenJPEG) and poppler's pdfimages.
"""

import os
import subprocess
import sys
import tempfile

from PIL import Image, ImageFilter, ImageOps

LETTER = (8.5, 11.0)


def extract(pdf: str, folder: str) -> list[str]:
    subprocess.run(["pdfimages", "-j", pdf, os.path.join(folder, "p")], check=True)
    return sorted(os.path.join(folder, f) for f in os.listdir(folder) if f.endswith(".jpg"))


def main() -> None:
    src, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        for i, path in enumerate(extract(os.path.join(src, "gehrig.pdf"), tmp), start=1):
            page = Image.open(path).convert("L")
            ppi = page.width / LETTER[0]
            size = (round(page.width * 300 / ppi), round(page.height * 300 / ppi))
            page = page.resize(size, Image.Resampling.LANCZOS).filter(ImageFilter.UnsharpMask(radius=1.2, percent=60))
            page = ImageOps.autocontrast(page, cutoff=1)
            bilevel = page.point(lambda v: 255 if v > 150 else 0, mode="1")
            target = os.path.join(out, f"memo-{i:02d}.tif")
            bilevel.save(target, compression="group4", dpi=(300, 300))
            print(f"{target}: {bilevel.width} x {bilevel.height}, {os.path.getsize(target):,} bytes")
    with tempfile.TemporaryDirectory() as tmp:
        for i, path in enumerate(extract(os.path.join(src, "safire.pdf"), tmp), start=1):
            page = Image.open(path).convert("RGB")
            ppi = page.width / LETTER[0]
            size = (round(page.width * 300 / ppi), round(page.height * 300 / ppi))
            page = page.resize(size, Image.Resampling.LANCZOS)
            target = os.path.join(out, f"moon-{i:02d}.jp2")
            page.save(target, quality_mode="rates", quality_layers=[60], irreversible=True)
            print(f"{target}: {page.width} x {page.height}, {os.path.getsize(target):,} bytes")


if __name__ == "__main__":
    main()
