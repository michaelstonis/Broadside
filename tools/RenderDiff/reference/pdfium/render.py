"""PDFium reference renderer for tools/RenderDiff (issue #137).

Usage: python -I render.py <pdf> <out-dir> <dpi> <pages>

<pages> is a comma-separated list of zero-based page indexes. Writes <out-dir>/page<N>.png (opaque RGB, the page's crop box with
its /Rotate, white background, no annotations or form fields) for every page it can render and reports the others on stderr.
Exits 0 when the document opened, 1 when it did not, 2 on bad arguments. Runs in the venv acquire-references.sh creates; needs
only pypdfium2 and the standard library.
"""

import os
import struct
import sys
import zlib

import pypdfium2 as pdfium


def write_png(path, width, height, rows):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + row for row in rows)
    with open(path + ".partial", "wb") as stream:
        stream.write(b"\x89PNG\r\n\x1a\n")
        stream.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)))
        stream.write(chunk(b"IDAT", zlib.compress(raw, 6)))
        stream.write(chunk(b"IEND", b""))
    os.replace(path + ".partial", path)


def main(argv):
    if len(argv) != 5:
        print(__doc__, file=sys.stderr)
        return 2

    pdf_path, out_dir, dpi, pages = argv[1], argv[2], float(argv[3]), [int(p) for p in argv[4].split(",") if p]
    try:
        document = pdfium.PdfDocument(pdf_path)
    except Exception as error:  # noqa: BLE001 - any failure to open is reported, not raised
        print(f"cannot open: {error}", file=sys.stderr)
        return 1

    os.makedirs(out_dir, exist_ok=True)
    for index in pages:
        try:
            page = document[index]
            bitmap = page.render(scale=dpi / 72, draw_annots=False, may_draw_forms=False, rev_byteorder=True)
            width, height, stride = bitmap.width, bitmap.height, bitmap.stride
            channels = bitmap.n_channels
            buffer = bytes(bitmap.buffer)
            rows = []
            for y in range(height):
                row = buffer[y * stride : y * stride + width * channels]
                if channels == 4:
                    row = b"".join(row[x : x + 3] for x in range(0, len(row), 4))
                rows.append(row)
            write_png(os.path.join(out_dir, f"page{index}.png"), width, height, rows)
        except Exception as error:  # noqa: BLE001
            print(f"page {index}: {error}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
