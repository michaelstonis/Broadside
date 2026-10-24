"""Writes jpx-scaled-references.txt: OpenJPEG's reduced decodes (`opj_decompress -r d`) of the JPEG 2000 test vectors (issue #362).

Usage: make_jpx_scaled_references.py [<repository root>]

Reads the vectors from tests/Broadside.TestSupport/JpxSamples.cs (name, size, components, the opj_compress command, the base-64
codestream) and, for every vector of one or three components whose image origin is on the reduced grid, decodes it with
opj_decompress 2.5.4 (-threads 1) at every reduction d from 1 to min(3, its decomposition levels). Each line holds the name, d,
the output width, height and components, and the samples (row by row, component by component; signed components offset by
2^(n-1), as both decoders deliver them) as base-64 16-bit big-endian values.
"""
import base64, pathlib, re, subprocess, sys, tempfile

root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else pathlib.Path(__file__).resolve().parents[3])
source = (root / "tests" / "Broadside.TestSupport" / "JpxSamples.cs").read_text()
pattern = re.compile(r'new\("(\w+)", (\d+), (\d+), (\d+), (\d+), (true|false), "([^"]*)",\s*((?:"[A-Za-z0-9+/=]*"\s*\+?\s*)+)')
lines = []
with tempfile.TemporaryDirectory() as work:
    work = pathlib.Path(work)
    for match in pattern.finditer(source):
        name, width, height, components, depth, signed, command, chunks = match.groups()
        components = int(components)
        if components not in (1, 3):
            continue
        data = base64.b64decode("".join(re.findall(r'"([A-Za-z0-9+/=]*)"', chunks)))
        resolutions = int(re.search(r"-n (\d+)", command).group(1)) if "-n " in command else 6
        offset = re.search(r"-d (\d+),(\d+)", command)
        levels = resolutions - 1
        suffix = ".jp2" if command.split("-o ")[1].split()[0].endswith(".jp2") else ".j2k"
        path = work / (name + suffix)
        path.write_bytes(data)
        for d in range(1, min(3, levels) + 1):
            if offset and (int(offset.group(1)) % (1 << d) or int(offset.group(2)) % (1 << d)):
                continue
            out = work / f"{name}-{d}.{'pgm' if components == 1 else 'ppm'}"
            subprocess.run(["opj_decompress", "-i", str(path), "-o", str(out), "-r", str(d), "-threads", "1"],
                           check=True, capture_output=True)
            pnm = out.read_bytes()
            header = re.match(rb"(P[56])\s+(?:#[^\n]*\n\s*)*(\d+)\s+(\d+)\s+(\d+)\s", pnm)
            w, h, maxval = int(header.group(2)), int(header.group(3)), int(header.group(4))
            pixels = pnm[header.end():]
            if maxval < 256:
                values = list(pixels)
            else:
                values = [int.from_bytes(pixels[i:i + 2], "big") for i in range(0, len(pixels), 2)]
            assert len(values) == w * h * components, name
            packed = b"".join(v.to_bytes(2, "big") for v in values)
            lines.append(f"{name} {d} {w} {h} {components} {base64.b64encode(packed).decode()}")

(root / "tests" / "Broadside.Tests" / "Filters" / "jpx-scaled-references.txt").write_text(
    "# opj_decompress 2.5.4 -r d of the JpxSamples vectors (make_jpx_scaled_references.py): name, d, width, height, components,\n"
    "# base-64 16-bit big-endian samples.\n" + "\n".join(lines) + "\n")
print(f"{len(lines)} references")
