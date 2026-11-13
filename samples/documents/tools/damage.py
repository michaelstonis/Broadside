"""Makes the two damaged samples from intact ones, the way files get damaged in the wild.

    python3 samples/documents/tools/damage.py samples/documents/pdf

- showcase-broken-xref.pdf: showcase.pdf (two revisions) after a tool inserted a line of text after the header without fixing
  the file: every offset in both cross-reference sections is now 54 bytes short, and the last startxref points at the wrong
  place. A reader has to rebuild the cross-reference table by scanning for objects.
- novel-large-print-truncated.pdf: novel-large-print.pdf cut off at 60 percent of its length, as an interrupted download leaves
  it: no trailer and no cross-reference stream, the last stream unterminated, and the objects after the cut missing. Broadside
  wrote the catalog and the page tree near the start, so a reader that scans the file can show the pages whose objects
  survive.

The output is deterministic.
"""

import os
import sys

NOTE = b"%Converted by MailGate 2.1 attachment filter (sample)\n"


def broken_xref(folder: str) -> None:
    data = open(os.path.join(folder, "showcase.pdf"), "rb").read()
    header_end = data.index(b"\n", data.index(b"%PDF-")) + 1
    damaged = data[:header_end] + NOTE + data[header_end:]
    # Point the last startxref ten bytes too far: into the middle of the cross-reference keyword.
    at = damaged.rindex(b"startxref")
    line_start = damaged.index(b"\n", at) + 1
    line_end = damaged.index(b"\n", line_start)
    offset = int(damaged[line_start:line_end])
    damaged = damaged[:line_start] + str(offset + 10).encode() + damaged[line_end:]
    open(os.path.join(folder, "showcase-broken-xref.pdf"), "wb").write(damaged)
    print(f"showcase-broken-xref.pdf: {len(damaged):,} bytes, offsets {len(NOTE)} bytes short")


def truncated(folder: str) -> None:
    data = open(os.path.join(folder, "novel-large-print.pdf"), "rb").read()
    cut = len(data) * 6 // 10
    open(os.path.join(folder, "novel-large-print-truncated.pdf"), "wb").write(data[:cut])
    print(f"novel-large-print-truncated.pdf: {cut:,} of {len(data):,} bytes")


if __name__ == "__main__":
    broken_xref(sys.argv[1])
    truncated(sys.argv[1])
