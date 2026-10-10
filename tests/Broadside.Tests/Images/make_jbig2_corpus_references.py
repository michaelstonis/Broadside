"""Checks the references of Jbig2CorpusResults.txt (issue #65): for every image listed as a match, decodes it with jbig2dec (embedded
mode, on the page and JBIG2Globals streams qpdf extracts) and with poppler (pdfimages PBM), and reports whether each reference the
line names gives the recorded SHA-256 of the PDF samples (0 = black, rows padded with 0). Broadside's own result is checked by
RealWorldJbig2Tests; this script only re-derives the references, so it needs jbig2dec, qpdf and pdfimages on the PATH and never runs
in CI.

usage: python3 -I make_jbig2_corpus_references.py <corpus dir> <work dir> [pdf.js test_manifest.json for encrypted files]
"""
import hashlib
import json
import os
import re
import subprocess
import sys
import zlib

GOLDEN = '68729cd515c668992c596df83a7437956a7f5a9df0d603e26b43aa3fc4a88f28'


def run(cmd):
    return subprocess.run(cmd, capture_output=True, timeout=600)


def raster(path):
    """A P4 PBM's width, height and rows (1 = black), padding bits cleared; None when unreadable."""
    if not os.path.exists(path) or os.path.getsize(path) == 0:
        return None
    data = open(path, 'rb').read()
    match = re.match(rb'P4\s+(?:#[^\n]*\n\s*)*(\d+)\s+(\d+)\s', data)
    if not match:
        return None
    width, height = int(match.group(1)), int(match.group(2))
    stride = (width + 7) // 8
    body = bytearray(data[match.end():][:stride * height].ljust(stride * height, b'\0'))
    return width, height, stride, body


def pdf_sha(image, invert=True):
    """SHA-256 of the PDF samples: the raster inverted (unless the PBM already has PDF polarity), padding bits 0."""
    width, height, stride, body = image
    out = bytearray((~b) & 0xFF for b in body) if invert else bytearray(body)
    if width % 8:
        mask = (0xFF << (8 - width % 8)) & 0xFF
        for y in range(height):
            out[y * stride + stride - 1] &= mask
    return hashlib.sha256(bytes(out)).hexdigest()


def main():
    corpus, work = sys.argv[1], sys.argv[2]
    passwords = {}
    if len(sys.argv) > 3:
        for entry in json.load(open(sys.argv[3])):
            if 'password' in entry:
                passwords['pdfjs/' + os.path.basename(entry['file'])] = entry['password']
    os.makedirs(work, exist_ok=True)
    results = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'Jbig2CorpusResults.txt')
    poppler = {}
    failures = 0
    for line in open(results):
        if line.startswith('#') or not line.strip():
            continue
        file, obj, size, sha, verdict, codes, reference = line.rstrip('\n').split('\t')
        if verdict != 'match':
            continue
        path = os.path.join(corpus, file)
        if not os.path.exists(path):
            print('skip (not fetched)', file)
            continue
        qpdf = ['qpdf'] + (['--password=' + passwords[file]] if file in passwords else [])
        tag = os.path.join(work, file.replace('/', '_') + '-' + obj)

        def stream(number):
            dictionary = run(qpdf + ['--show-object=%s' % number, path]).stdout.decode('latin1')
            raw = run(qpdf + ['--show-object=%s' % number, '--raw-stream-data', path]).stdout
            if '/FlateDecode' in dictionary.split('/JBIG2Decode')[0]:
                raw = zlib.decompress(raw)
            return raw, dictionary

        page, dictionary = stream(obj)
        open(tag + '.jb2e', 'wb').write(page)
        command = ['jbig2dec', '-e', '-o', tag + '.jbig2dec.pbm']
        globals_ref = re.search(r'/JBIG2Globals\s+(\d+)\s+0\s+R', dictionary)
        if globals_ref:
            open(tag + '.jb2g', 'wb').write(stream(globals_ref.group(1))[0])
            command.append(tag + '.jb2g')
        if file == 'pdfjs/jbig2_file_header.pdf':
            command = ['jbig2dec', '-o', tag + '.jbig2dec.pbm']
        run(command + [tag + '.jb2e'])
        decoded = raster(tag + '.jbig2dec.pbm')
        jbig2dec = pdf_sha(decoded) if decoded else '-'

        if file not in poppler:
            prefix = os.path.join(work, 'poppler-' + file.replace('/', '_'))
            upw = ['-upw', passwords[file]] if file in passwords else []
            listing = run(['pdfimages'] + upw + ['-list', path]).stdout.decode('latin1').splitlines()[2:]
            run(['pdfimages'] + upw + [path, prefix])
            found = {}
            for row in (r.split() for r in listing):
                if len(row) > 10 and row[8] == 'jbig2':
                    image = raster('%s-%03d.pbm' % (prefix, int(row[1])))
                    if image and row[10] not in found:
                        # Poppler writes a stencil mask's samples as they are, which is already PDF polarity.
                        found[row[10]] = pdf_sha(image, invert=row[2] != 'stencil')
            poppler[file] = found
        pop = poppler[file].get(obj, '-')

        expected = []
        if 'golden' in reference:
            expected.append(('golden', GOLDEN))
        if 'jbig2dec' in reference:
            expected.append(('jbig2dec', jbig2dec))
        if 'poppler' in reference and 'differs' not in reference:
            expected.append(('poppler', pop))
        bad = [name for name, value in expected if value != sha]
        if bad:
            failures += 1
            print('MISMATCH', file, obj, 'references disagree:', ', '.join(bad))
    print('done,', failures, 'mismatches')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
