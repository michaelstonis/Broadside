"""Regenerates the predefined CMap resources of Broadside.Fonts.Cmaps, their manifest, and the core's fallback table.

Usage (from the repository root; downloads nothing, run with -I):

    python3 -I src/Broadside.Fonts.Cmaps/Data/generate.py <cmap-resources> <mapping-resources-pdf>

<cmap-resources> is a checkout (or the files) of https://github.com/adobe-type-tools/cmap-resources at
f5cf3bca7fdfeaceb77aa82847e974f2306c20b4 and <mapping-resources-pdf> of https://github.com/adobe-type-tools/mapping-resources-pdf
at 2dd5e53fb74a01718b9dfd448a0d1cce6fff2aa5, laid out as upstream. Treat them as untrusted data: this script only reads them.

Writes:
- CMaps/<name>.deflate and CidToUnicode/<name>.deflate: each upstream file unmodified, raw Deflate (zlib level 9). The package
  embeds them and inflates on first use.
- Data/manifest.tsv: one row per file (kind, name, upstream path, size, SHA-256 of the upstream bytes, character collection,
  WMode, usecmap, codespace ranges, sample codes). When the manifest exists, every input must match its SHA-256 (pin check).
  Samples are code -> CID pairs taken from the collection's cid2code.txt (an independent table) and kept only where a plain
  reading of the CMap file agrees; the package tests check them through Broadside's CMap parser.
- ../Broadside/Fonts/PredefinedCMapTable.g.cs: name, character collection, writing mode and codespace ranges (usecmap resolved)
  of every packaged CMap, so the core splits codes correctly when the package is not configured.
- licenses/ (and ../Broadside/Fonts/Data/CMaps/LICENSE.md for the core table): the upstream licence files, verbatim.
"""

import hashlib
import os
import re
import shutil
import sys
import zlib

HERE = os.path.dirname(os.path.abspath(__file__))
PACKAGE = os.path.dirname(HERE)
CORE_TABLE = os.path.join(os.path.dirname(PACKAGE), 'Broadside', 'Fonts', 'PredefinedCMapTable.g.cs')
MANIFEST = os.path.join(HERE, 'manifest.tsv')

# ISO 32000-2:2020 Table 116 without Identity-H/V (built into the core), in table order, with the cmap-resources folder.
TABLE_116 = [
    ('Adobe-GB1-6', 'GB-EUC-H GB-EUC-V GBpc-EUC-H GBpc-EUC-V GBK-EUC-H GBK-EUC-V GBKp-EUC-H GBKp-EUC-V GBK2K-H GBK2K-V '
                    'UniGB-UCS2-H UniGB-UCS2-V UniGB-UTF16-H UniGB-UTF16-V'),
    ('Adobe-CNS1-7', 'B5pc-H B5pc-V HKscs-B5-H HKscs-B5-V ETen-B5-H ETen-B5-V ETenms-B5-H ETenms-B5-V CNS-EUC-H CNS-EUC-V '
                     'UniCNS-UCS2-H UniCNS-UCS2-V UniCNS-UTF16-H UniCNS-UTF16-V'),
    ('Adobe-Japan1-7', '83pv-RKSJ-H 90ms-RKSJ-H 90ms-RKSJ-V 90msp-RKSJ-H 90msp-RKSJ-V 90pv-RKSJ-H Add-RKSJ-H Add-RKSJ-V EUC-H EUC-V '
                       'Ext-RKSJ-H Ext-RKSJ-V H V UniJIS-UCS2-H UniJIS-UCS2-V UniJIS-UCS2-HW-H UniJIS-UCS2-HW-V UniJIS-UTF16-H '
                       'UniJIS-UTF16-V'),
    ('Adobe-Korea1-2', 'KSC-EUC-H KSC-EUC-V KSCms-UHC-H KSCms-UHC-V KSCms-UHC-HW-H KSCms-UHC-HW-V KSCpc-EUC-H UniKS-UCS2-H '
                       'UniKS-UCS2-V UniKS-UTF16-H UniKS-UTF16-V'),
]

# ISO 32000-2 §9.10.2: CID -> Unicode for the Adobe CJK collections (#58), from mapping-resources-pdf/pdf2unicode.
CID_TO_UNICODE = ['Adobe-CNS1-UCS2', 'Adobe-GB1-UCS2', 'Adobe-Japan1-UCS2', 'Adobe-Korea1-UCS2', 'Adobe-KR-UCS2']

HEX_PAIR = re.compile(rb'<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>')


def blocks(text, name):
    return re.findall(rb'begin' + name + rb'(.*?)end' + name, text, re.S)


class CMapFile:
    def __init__(self, data):
        self.registry = re.search(rb'/Registry\s*\(([^)]*)\)', data).group(1).decode('ascii')
        self.ordering = re.search(rb'/Ordering\s*\(([^)]*)\)', data).group(1).decode('ascii')
        self.supplement = int(re.search(rb'/Supplement\s+(\d+)', data).group(1))
        self.wmode = int(re.search(rb'/WMode\s+(\d)', data).group(1))
        use = re.search(rb'/(\S+)\s+usecmap', data)
        self.usecmap = use.group(1).decode('ascii') if use else None
        self.codespace = []
        for block in blocks(data, rb'codespacerange'):
            for low, high in HEX_PAIR.findall(block):
                self.codespace.append((len(low) // 2, int(low, 16), int(high, 16)))
        self.cids = []
        for block in blocks(data, rb'cidrange'):
            for low, high, cid in re.findall(rb'<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(\d+)', block):
                self.cids.append((len(low) // 2, int(low, 16), int(high, 16), int(cid)))
        for block in blocks(data, rb'cidchar'):
            for code, cid in re.findall(rb'<([0-9A-Fa-f]+)>\s*(\d+)', block):
                self.cids.append((len(code) // 2, int(code, 16), int(code, 16), int(cid)))

    def lookup(self, length, code):
        found = None
        for (n, low, high, cid) in self.cids:
            if n == length and low <= code <= high:
                found = cid + code - low
        return found


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def read_manifest():
    pins = {}
    if os.path.exists(MANIFEST):
        with open(MANIFEST, encoding='utf-8') as handle:
            for line in handle:
                if line.startswith('#') or not line.strip():
                    continue
                cells = line.rstrip('\n').split('\t')
                pins[(cells[0], cells[1])] = cells[4]
    return pins


def cid2code(path):
    with open(path, encoding='ascii') as handle:
        rows = [line.rstrip('\n').split('\t') for line in handle if not line.startswith('#')]
    header = rows[0]
    return header, rows[1:]


def samples(name, cmap, chain, table):
    """Up to one code -> CID pair per code length from cid2code.txt, kept where the CMap file (and its usecmap chain) agrees."""
    header, rows = table
    vertical = name.endswith('V')
    column = 'H' if name in ('H', 'V') else name[:-2]
    index = header.index(column)
    plain, marked = {}, {}
    for row in rows:
        cell = row[index]
        if cell == '*':
            continue
        for entry in cell.split(','):
            target = marked if entry.endswith('v') else plain
            code = entry.rstrip('v')
            target.setdefault(code, set()).add(int(row[0]))
    # A 'v' entry is the code's CID in the V CMap; any other code of a V CMap maps as in the H CMap.
    candidates = {code: cids for code, cids in plain.items() if len(cids) == 1 and not (vertical and code in marked)}
    if vertical:
        candidates.update({code: cids for code, cids in marked.items() if len(cids) == 1})
    chosen = []
    for length in (1, 2, 3, 4):
        pool = sorted((next(iter(cids)), code) for code, cids in candidates.items() if len(code) == 2 * length)
        for cid, code in pool[len(pool) // 2:] + pool[:len(pool) // 2]:
            expected = None
            for file in chain:
                expected = file.lookup(length, int(code, 16))
                if expected is not None:
                    break
            if expected == cid:
                chosen.append(f'{code.upper()}={cid}')
                break
    return chosen


def main(cmap_resources, mapping_resources):
    pins = read_manifest()
    rows = []
    files = {}
    for folder, names in TABLE_116:
        for name in names.split():
            path = f'{folder}/CMap/{name}'
            with open(os.path.join(cmap_resources, path), 'rb') as handle:
                data = handle.read()
            files[name] = (folder, path, data, CMapFile(data))

    tables = {}
    core = []
    for name, (folder, path, data, cmap) in files.items():
        chain = [cmap]
        while chain[-1].usecmap is not None:
            chain.append(files[chain[-1].usecmap][3])
        codespace = []
        for file in chain:
            codespace += [range_ for range_ in file.codespace if range_ not in codespace]
        if folder not in tables:
            tables[folder] = cid2code(os.path.join(cmap_resources, folder, 'cid2code.txt'))
        vectors = samples(name, cmap, chain, tables[folder])
        if not vectors:
            sys.exit(f'no sample for {name}')
        rows.append(('CMap', name, path, data, cmap, codespace, vectors))
        core.append((name, cmap, codespace))

    for name in CID_TO_UNICODE:
        path = f'pdf2unicode/{name}'
        with open(os.path.join(mapping_resources, path), 'rb') as handle:
            data = handle.read()
        rows.append(('CidToUnicode', name, path, data, None, [], []))

    for kind, name, path, data, _, _, _ in rows:
        pinned = pins.get((kind, name))
        if pinned is not None and pinned != sha256(data):
            sys.exit(f'{path}: SHA-256 {sha256(data)} does not match the manifest ({pinned}); check the upstream commit')

    for kind, folder in (('CMap', 'CMaps'), ('CidToUnicode', 'CidToUnicode')):
        target = os.path.join(PACKAGE, folder)
        os.makedirs(target, exist_ok=True)
        for row in rows:
            if row[0] == kind:
                compressor = zlib.compressobj(9, zlib.DEFLATED, -15, 9)
                with open(os.path.join(target, row[1] + '.deflate'), 'wb') as handle:
                    handle.write(compressor.compress(row[3]) + compressor.flush())

    with open(MANIFEST, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write('# Generated by generate.py from cmap-resources@f5cf3bca7fdfeaceb77aa82847e974f2306c20b4 and '
                     'mapping-resources-pdf@2dd5e53fb74a01718b9dfd448a0d1cce6fff2aa5. Do not edit.\n')
        handle.write('# kind\tname\tupstream path\tsize\tsha256\tregistry\tordering\tsupplement\twmode\tusecmap\tcodespace\tsamples\n')
        for kind, name, path, data, cmap, codespace, vectors in rows:
            cells = [kind, name, path, str(len(data)), sha256(data)]
            if cmap is None:
                cells += [''] * 7
            else:
                cells += [cmap.registry, cmap.ordering, str(cmap.supplement), str(cmap.wmode), cmap.usecmap or '',
                          ' '.join(f'{low:0{2 * n}X}-{high:0{2 * n}X}' for n, low, high in codespace), ' '.join(vectors)]
            handle.write('\t'.join(cells) + '\n')

    with open(CORE_TABLE, 'w', encoding='utf-8', newline='\n') as handle:
        handle.write('// <auto-generated>\n')
        handle.write('// Generated by src/Broadside.Fonts.Cmaps/Data/generate.py from Adobe cmap-resources\n')
        handle.write('// (https://github.com/adobe-type-tools/cmap-resources, f5cf3bca7fdfeaceb77aa82847e974f2306c20b4, BSD-3-Clause; see\n')
        handle.write('// THIRD-PARTY-NOTICES.txt). Do not edit.\n')
        handle.write('// </auto-generated>\n')
        handle.write('namespace Broadside.Fonts;\n\n')
        handle.write('/// <summary>The predefined CMaps of ISO 32000-2 Table 116 (Identity aside): character collection, writing mode, codespace.</summary>\n')
        handle.write('internal static partial class PredefinedCMapTable\n{\n')
        handle.write('    private static readonly Entry[] Entries =\n    [\n')
        for name, cmap, codespace in core:
            ranges = ', '.join(f'new({n}, 0x{low:0{2 * n}X}, 0x{high:0{2 * n}X})' for n, low, high in codespace)
            handle.write(f'        new("{name}", "{cmap.registry}", "{cmap.ordering}", {cmap.supplement}, {cmap.wmode}, [{ranges}]),\n')
        handle.write('    ];\n}\n')

    licenses = os.path.join(PACKAGE, 'licenses')
    for source, target in ((os.path.join(cmap_resources, 'LICENSE.md'), 'cmap-resources/LICENSE.md'),
                           (os.path.join(mapping_resources, 'LICENSE.txt'), 'mapping-resources-pdf/LICENSE.txt')):
        os.makedirs(os.path.dirname(os.path.join(licenses, target)), exist_ok=True)
        shutil.copyfile(source, os.path.join(licenses, target))
    # The core's fallback table is derived from cmap-resources too; its licence travels with the core package.
    shutil.copyfile(os.path.join(cmap_resources, 'LICENSE.md'), os.path.join(os.path.dirname(CORE_TABLE), 'Data', 'CMaps', 'LICENSE.md'))

    print(f'{len(rows)} files, {sum(len(r[6]) for r in rows)} samples')


if __name__ == '__main__':
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
