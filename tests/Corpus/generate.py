#!/usr/bin/env python3
"""Generates the hand-written minimal PDF corpus in tests/Corpus/.

Run with ``python3 -I generate.py [output-dir]`` (stdlib only, no third-party
packages). Every file is emitted deterministically: cross-reference offsets and
stream lengths are computed, the "random" values the encryption algorithms
need (file identifiers, initialization vectors, salts, file keys) are fixed
constants, so a reviewer can regenerate the corpus and ``git diff`` it.

Each file contains one feature and nothing else. See README.md for the table
of files, the spec clauses they exercise and how they were verified.

Clause references are to ISO 32000-2:2020.
"""

from __future__ import annotations

import hashlib
import hmac
import math
import os
import struct
import sys
import zlib

# ---------------------------------------------------------------------------
# Serialization helpers (clause 7.3 objects, 7.5 file structure)
# ---------------------------------------------------------------------------

LETTER = b"[0 0 612 792]"
A4 = b"[0 0 595 842]"
BINARY_COMMENT = b"%\xe2\xe3\xcf\xd3\n"  # 7.5.2: marks the file as binary


def header(version: str = "1.7", binary: bool = False) -> bytes:
    return b"%PDF-" + version.encode() + b"\n" + (BINARY_COMMENT if binary else b"")


def obj(num: int, body: bytes, gen: int = 0) -> bytes:
    """7.3.10 indirect object."""
    return b"%d %d obj\n%s\nendobj\n" % (num, gen, body)


def stream(dict_entries: bytes, data: bytes, length: int | None = None) -> bytes:
    """7.3.8 stream object. ``length`` overrides the real length (for the broken file)."""
    n = len(data) if length is None else length
    return b"<< " + dict_entries + b" /Length %d >>\nstream\n" % n + data + b"\nendstream"


def catalog(extra: bytes = b"") -> bytes:
    return b"<< /Type /Catalog /Pages 2 0 R" + extra + b" >>"


def pages(kids: list[int] | None = None, count: int | None = None, extra: bytes = b"") -> bytes:
    kids = kids or [3]
    refs = b" ".join(b"%d 0 R" % k for k in kids)
    return b"<< /Type /Pages /Kids [%s] /Count %d%s >>" % (refs, count if count is not None else len(kids), extra)


def page(parent: int = 2, mediabox: bytes | None = LETTER, contents: int | None = None,
         font: int | None = None, resources: bool = True, extra: bytes = b"") -> bytes:
    """7.7.3.3 page object. /Resources is required (Table 31) unless inherited, so an
    empty dictionary is written when the page uses no resources."""
    body = b"<< /Type /Page /Parent %d 0 R" % parent
    if mediabox is not None:
        body += b" /MediaBox " + mediabox
    if contents is not None:
        body += b" /Contents %d 0 R" % contents
    if font is not None:
        body += b" /Resources << /Font << /F1 %d 0 R >> >>" % font
    elif resources:
        body += b" /Resources << >>"
    return body + extra + b" >>"


HELVETICA = b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"


def text_content(text: bytes) -> bytes:
    return b"BT /F1 24 Tf 72 700 Td (" + text + b") Tj ET"


def xref_table(offsets: dict[int, int], size: int, trailer: bytes, delta: int = 0) -> bytes:
    """7.5.4 classic cross-reference table with one subsection covering 0..size-1.

    Objects missing from ``offsets`` are written as free entries. ``delta`` is
    added to every in-use offset (used only by broken-xref-offsets.pdf).
    """
    out = b"xref\n0 %d\n" % size
    out += b"0000000000 65535 f \n"
    for n in range(1, size):
        if n in offsets:
            out += b"%010d 00000 n \n" % (offsets[n] + delta)
        else:
            out += b"0000000000 00000 f \n"
    return out + b"trailer\n" + trailer + b"\n"


class File:
    """Accumulates a PDF file and records the offset of each indirect object."""

    def __init__(self, version: str = "1.7", binary: bool = False) -> None:
        self.buf = bytearray(header(version, binary))
        self.offsets: dict[int, int] = {}

    def add(self, num: int, body: bytes) -> int:
        self.offsets[num] = len(self.buf)
        self.buf += obj(num, body)
        return self.offsets[num]

    def raw(self, data: bytes) -> int:
        start = len(self.buf)
        self.buf += data
        return start

    def pos(self) -> int:
        return len(self.buf)

    def finish_classic(self, trailer: bytes, size: int | None = None, delta: int = 0,
                       startxref_delta: int = 0) -> bytes:
        size = size or max(self.offsets) + 1
        start = self.pos()
        self.raw(xref_table(self.offsets, size, trailer, delta))
        self.raw(b"startxref\n%d\n%%%%EOF\n" % (start + startxref_delta))
        return bytes(self.buf)


def xref_stream_rows(entries: list[tuple[int, int, int]]) -> bytes:
    """7.5.8.3 entries with /W [1 2 1]: (type, field2, field3)."""
    return b"".join(struct.pack(">BHB", t, f2, f3) for t, f2, f3 in entries)


def simple_file(objects: list[tuple[int, bytes]], version: str = "1.7", binary: bool = False,
                trailer_extra: bytes = b"") -> bytes:
    f = File(version, binary)
    for num, body in objects:
        f.add(num, body)
    size = max(n for n, _ in objects) + 1
    return f.finish_classic(b"<< /Size %d /Root 1 0 R%s >>" % (size, trailer_extra))


def text_file(content: bytes, version: str = "1.7", binary: bool = False) -> bytes:
    """One Helvetica line; ``content`` is the already-serialized stream object body."""
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, font=5)),
        (4, content),
        (5, HELVETICA),
    ], version, binary)


# ---------------------------------------------------------------------------
# Filters (clause 7.4)
# ---------------------------------------------------------------------------

def flate(data: bytes) -> bytes:
    return zlib.compress(data, 9)


def lzw_encode(data: bytes, early_change: int = 1) -> bytes:
    """7.4.4.2 LZW with 9..12-bit codes, clear (256) first, EOD (257) last.

    With EarlyChange = 1 (the default) the code width grows one code early:
    the first 10-bit code is the one emitted after table entry 511 is created.
    """
    out_bits: list[tuple[int, int]] = []  # (code, width)
    table: dict[bytes, int] = {bytes([i]): i for i in range(256)}
    next_code = 258
    width = 9
    out_bits.append((256, width))
    w = b""
    for byte in data:
        wc = w + bytes([byte])
        if wc in table:
            w = wc
            continue
        out_bits.append((table[w], width))
        table[wc] = next_code
        next_code += 1
        if next_code + early_change > (1 << width) and width < 12:
            width += 1
        if next_code + early_change >= 4096:
            out_bits.append((256, width))
            table = {bytes([i]): i for i in range(256)}
            next_code = 258
            width = 9
        w = bytes([byte])
    if w:
        out_bits.append((table[w], width))
    out_bits.append((257, width))
    acc = 0
    nbits = 0
    out = bytearray()
    for code, wd in out_bits:
        acc = (acc << wd) | code
        nbits += wd
        while nbits >= 8:
            out.append((acc >> (nbits - 8)) & 0xFF)
            nbits -= 8
    if nbits:
        out.append((acc << (8 - nbits)) & 0xFF)
    return bytes(out)


def ascii85_encode(data: bytes) -> bytes:
    """7.4.3 ASCII85Decode encoder (no 'z' shortcut), terminated by '~>'."""
    out = bytearray()
    for i in range(0, len(data), 4):
        chunk = data[i:i + 4]
        pad = 4 - len(chunk)
        n = int.from_bytes(chunk + b"\0" * pad, "big")
        digits = []
        for _ in range(5):
            digits.append(n % 85)
            n //= 85
        out += bytes(33 + d for d in reversed(digits))[:5 - pad]
    return bytes(out) + b"~>"


def asciihex_encode(data: bytes) -> bytes:
    """7.4.2 ASCIIHexDecode encoder, terminated by '>'."""
    return data.hex().upper().encode() + b">"


def runlength_encode(data: bytes) -> bytes:
    """7.4.5 RunLengthDecode encoder, terminated by 128 (EOD)."""
    out = bytearray()
    i = 0
    while i < len(data):
        run = 1
        while i + run < len(data) and run < 128 and data[i + run] == data[i]:
            run += 1
        if run >= 2:
            out += bytes([257 - run, data[i]])
            i += run
            continue
        start = i
        while i < len(data) and i - start < 128 and not (i + 1 < len(data) and data[i + 1] == data[i]):
            i += 1
        out += bytes([i - start - 1]) + data[start:i]
    return bytes(out) + b"\x80"


def png_up_predict(rows: list[bytes]) -> bytes:
    """7.4.4.4 PNG 'Up' predictor (filter type 2) applied to every row."""
    out = bytearray()
    prev = bytes(len(rows[0]))
    for row in rows:
        out.append(2)
        out += bytes((row[i] - prev[i]) & 0xFF for i in range(len(row)))
        prev = row
    return bytes(out)


# ---------------------------------------------------------------------------
# Cryptography (clause 7.6): MD5/SHA from hashlib, RC4 and AES implemented here
# ---------------------------------------------------------------------------

def rc4(key: bytes, data: bytes) -> bytes:
    s = list(range(256))
    j = 0
    for i in range(256):
        j = (j + s[i] + key[i % len(key)]) & 0xFF
        s[i], s[j] = s[j], s[i]
    out = bytearray()
    i = j = 0
    for b in data:
        i = (i + 1) & 0xFF
        j = (j + s[i]) & 0xFF
        s[i], s[j] = s[j], s[i]
        out.append(b ^ s[(s[i] + s[j]) & 0xFF])
    return bytes(out)


def _aes_sbox() -> list[int]:
    sbox = [0] * 256
    p = q = 1
    while True:
        p = (p ^ (p << 1) ^ (0x1B if p & 0x80 else 0)) & 0xFF
        q ^= (q << 1) & 0xFF
        q ^= (q << 2) & 0xFF
        q ^= (q << 4) & 0xFF
        q &= 0xFF
        if q & 0x80:
            q ^= 0x09
        rot = lambda x, n: ((x << n) | (x >> (8 - n))) & 0xFF  # noqa: E731
        sbox[p] = q ^ rot(q, 1) ^ rot(q, 2) ^ rot(q, 3) ^ rot(q, 4) ^ 0x63
        if p == 1:
            break
    sbox[0] = 0x63
    return sbox


SBOX = _aes_sbox()


def _xtime(b: int) -> int:
    return ((b << 1) ^ 0x1B) & 0xFF if b & 0x80 else b << 1


def _aes_round_keys(key: bytes) -> list[bytes]:
    nk = len(key) // 4
    nr = nk + 6
    w = [list(key[4 * i:4 * i + 4]) for i in range(nk)]
    rcon = 1
    for i in range(nk, 4 * (nr + 1)):
        t = list(w[i - 1])
        if i % nk == 0:
            t = [SBOX[b] for b in t[1:] + t[:1]]
            t[0] ^= rcon
            rcon = _xtime(rcon)
        elif nk > 6 and i % nk == 4:
            t = [SBOX[b] for b in t]
        w.append([w[i - nk][j] ^ t[j] for j in range(4)])
    return [bytes(b for col in w[4 * r:4 * r + 4] for b in col) for r in range(nr + 1)]


def aes_encrypt_block(round_keys: list[bytes], block: bytes) -> bytes:
    s = [block[i] ^ round_keys[0][i] for i in range(16)]
    nr = len(round_keys) - 1
    for r in range(1, nr + 1):
        s = [SBOX[b] for b in s]
        s = [s[row + 4 * ((col + row) % 4)] for col in range(4) for row in range(4)]
        if r != nr:
            m = []
            for c in range(4):
                a0, a1, a2, a3 = s[4 * c:4 * c + 4]
                m += [
                    _xtime(a0) ^ _xtime(a1) ^ a1 ^ a2 ^ a3,
                    a0 ^ _xtime(a1) ^ _xtime(a2) ^ a2 ^ a3,
                    a0 ^ a1 ^ _xtime(a2) ^ _xtime(a3) ^ a3,
                    _xtime(a0) ^ a0 ^ a1 ^ a2 ^ _xtime(a3),
                ]
            s = m
        s = [s[i] ^ round_keys[r][i] for i in range(16)]
    return bytes(s)


def aes_cbc_encrypt(key: bytes, iv: bytes, data: bytes, pad: bool) -> bytes:
    """AES-CBC; with ``pad`` applies the PKCS#5 padding 7.6.3.1 requires."""
    if pad:
        n = 16 - len(data) % 16
        data += bytes([n]) * n
    assert len(data) % 16 == 0
    rk = _aes_round_keys(key)
    out = bytearray()
    prev = iv
    for i in range(0, len(data), 16):
        prev = aes_encrypt_block(rk, bytes(a ^ b for a, b in zip(data[i:i + 16], prev)))
        out += prev
    return bytes(out)


def aes_ecb_encrypt_block(key: bytes, block: bytes) -> bytes:
    return aes_encrypt_block(_aes_round_keys(key), block)


PASSWORD_PAD = bytes.fromhex(
    "28BF4E5E4E758A4164004E56FFFA01082E2E00B6D0683E802F0CA9FE6453697A")  # Algorithm 2 step (a)


def pad_password(pw: bytes) -> bytes:
    return (pw + PASSWORD_PAD)[:32]


def legacy_file_key(user_pw: bytes, o_entry: bytes, p: int, id0: bytes, r: int, n: int,
                    encrypt_metadata: bool = True) -> bytes:
    """7.6.4.3.2 Algorithm 2 (R2..R4); ``n`` is the key length in bytes."""
    extra = b"\xff\xff\xff\xff" if r >= 4 and not encrypt_metadata else b""  # step (f)
    h = hashlib.md5(pad_password(user_pw) + o_entry + struct.pack("<I", p & 0xFFFFFFFF) + id0 + extra).digest()
    if r >= 3:
        for _ in range(50):
            h = hashlib.md5(h[:n]).digest()
    return h[:n]


def legacy_o_entry(owner_pw: bytes, user_pw: bytes, r: int, n: int, rehash_first_n: bool = False) -> bytes:
    """7.6.4.4.2 Algorithm 3. ``rehash_first_n`` rehashes only the first n bytes in step (c), as qpdf
    and PDFBox do (the same as the spec for 128-bit keys)."""
    k = hashlib.md5(pad_password(owner_pw or user_pw)).digest()
    if r >= 3:
        for _ in range(50):
            k = hashlib.md5(k[:n] if rehash_first_n else k).digest()
    k = k[:n]
    o = rc4(k, pad_password(user_pw))
    if r >= 3:
        for i in range(1, 20):
            o = rc4(bytes(b ^ i for b in k), o)
    return o


def legacy_u_entry(key: bytes, id0: bytes, r: int) -> bytes:
    """7.6.4.4.3 Algorithm 4 (R2) and 7.6.4.4.4 Algorithm 5 (R3, R4)."""
    if r == 2:
        return rc4(key, PASSWORD_PAD)
    u = rc4(key, hashlib.md5(PASSWORD_PAD + id0).digest())
    for i in range(1, 20):
        u = rc4(bytes(b ^ i for b in key), u)
    return u + bytes(16)  # step (f): 16 bytes of arbitrary padding


def object_key(file_key: bytes, num: int, gen: int, aes: bool) -> bytes:
    """7.6.3.2 Algorithm 1 steps (b)-(d)."""
    salt = struct.pack("<I", num)[:3] + struct.pack("<H", gen) + (b"sAlT" if aes else b"")
    return hashlib.md5(file_key + salt).digest()[:min(len(file_key) + 5, 16)]


def hash_2b(data: bytes, password: bytes, udata: bytes) -> bytes:
    """7.6.4.3.4 Algorithm 2.B (revision 6)."""
    k = hashlib.sha256(data).digest()
    i = 0
    e = b"\0"
    while i < 64 or e[-1] > i - 32:
        k1 = (password + k + udata) * 64
        e = aes_cbc_encrypt(k[:16], k[16:32], k1, pad=False)
        mod = int.from_bytes(e[:16], "big") % 3
        k = (hashlib.sha256, hashlib.sha384, hashlib.sha512)[mod](e).digest()
        i += 1
    return k[:32]


def gcm_encrypt(key: bytes, iv: bytes, data: bytes) -> tuple[bytes, bytes]:
    """NIST SP 800-38D AES-GCM with a 96-bit IV and no AAD (ISO/TS 32003 5.2); returns (ciphertext, tag)."""
    rk = _aes_round_keys(key)
    h = int.from_bytes(aes_encrypt_block(rk, bytes(16)), "big")
    out = bytearray()
    for i in range(0, len(data), 16):
        ks = aes_encrypt_block(rk, iv + (i // 16 + 2).to_bytes(4, "big"))
        out += bytes(a ^ b for a, b in zip(data[i:i + 16], ks))

    def mul(x: int, y: int) -> int:  # GF(2^128), SP 800-38D 6.3 Algorithm 1
        z, v = 0, y
        for bit in range(128):
            if (x >> (127 - bit)) & 1:
                z ^= v
            v = (v >> 1) ^ (0xE1 << 120) if v & 1 else v >> 1
        return z

    y = 0
    for i in range(0, len(out), 16):
        y = mul(y ^ int.from_bytes(bytes(out[i:i + 16]).ljust(16, b"\0"), "big"), h)
    y = mul(y ^ (len(out) * 8), h)  # len(A) = 0 in the high 64 bits
    tag = y ^ int.from_bytes(aes_encrypt_block(rk, iv + b"\0\0\0\1"), "big")
    return bytes(out), tag.to_bytes(16, "big")


def aes_key_wrap(kek: bytes, key: bytes) -> bytes:
    """RFC 3394 2.2.1 key wrap (index-based), the key wrapping ISO/TS 32004 Table 7 uses."""
    rk = _aes_round_keys(kek)
    n = len(key) // 8
    a = bytes.fromhex("A6A6A6A6A6A6A6A6")
    r = [key[8 * i:8 * i + 8] for i in range(n)]
    for j in range(6):
        for i in range(n):
            b = aes_encrypt_block(rk, a + r[i])
            a = (int.from_bytes(b[:8], "big") ^ (n * j + i + 1)).to_bytes(8, "big")
            r[i] = b[8:]
    return a + b"".join(r)


def hkdf_sha256(ikm: bytes, salt: bytes, info: bytes, length: int) -> bytes:
    """RFC 5869 HKDF with SHA-256 (ISO/TS 32004 6.4 pdfMacWrapKdf)."""
    prk = hmac.new(salt, ikm, hashlib.sha256).digest()
    okm, t, i = b"", b"", 1
    while len(okm) < length:
        t = hmac.new(prk, t + info + bytes([i]), hashlib.sha256).digest()
        okm += t
        i += 1
    return okm[:length]


def der(tag: int, content: bytes) -> bytes:
    """ITU-T X.690 DER tag-length-value."""
    n = len(content)
    if n < 0x80:
        return bytes([tag, n]) + content
    length = n.to_bytes((n.bit_length() + 7) // 8, "big")
    return bytes([tag, 0x80 | len(length)]) + length + content


def der_oid(dotted: str) -> bytes:
    arcs = [int(x) for x in dotted.split(".")]
    body = bytearray([40 * arcs[0] + arcs[1]])
    for arc in arcs[2:]:
        chunk = [arc & 0x7F]
        arc >>= 7
        while arc:
            chunk.append(0x80 | (arc & 0x7F))
            arc >>= 7
        body += bytes(reversed(chunk))
    return der(0x06, bytes(body))


def der_seq(*items: bytes) -> bytes:
    return der(0x30, b"".join(items))


def der_set(*items: bytes) -> bytes:
    return der(0x31, b"".join(sorted(items)))  # DER: SET OF sorted by encoding


OID_AUTH_DATA = "1.2.840.113549.1.9.16.1.2"
OID_PDF_MAC_INTEGRITY_INFO = "1.0.32004.1.0"
OID_PDF_MAC_WRAP_KDF = "1.0.32004.1.1"
OID_AES256_WRAP = "2.16.840.1.101.3.4.1.45"
OID_HMAC_SHA256 = "1.2.840.113549.2.9"
OID_SHA256 = "2.16.840.1.101.3.4.2.1"
OID_CONTENT_TYPE = "1.2.840.113549.1.9.3"
OID_MESSAGE_DIGEST = "1.2.840.113549.1.9.4"
OID_CMS_ALGORITHM_PROTECTION = "1.2.840.113549.1.9.52"


def pdf_mac_token(file_key: bytes, kdf_salt: bytes, mac_key: bytes, data_digest: bytes) -> bytes:
    """ISO/TS 32004 6.2-6.4: a CMS AuthenticatedData (RFC 5652 9) over PdfMacIntegrityInfo, one
    PasswordRecipientInfo with pdfMacWrapKdf and AES-256 key wrap, HMAC-SHA-256, SHA-256 digests."""
    sha256_alg = der_seq(der_oid(OID_SHA256))
    hmac_alg = der_seq(der_oid(OID_HMAC_SHA256))
    info = der_seq(der(0x02, b"\0"), der(0x04, data_digest))  # version 0, dataDigest
    encap = der_seq(der_oid(OID_PDF_MAC_INTEGRITY_INFO), der(0xA0, der(0x04, info)))
    attrs = sorted([
        der_seq(der_oid(OID_CONTENT_TYPE), der_set(der_oid(OID_PDF_MAC_INTEGRITY_INFO))),
        der_seq(der_oid(OID_MESSAGE_DIGEST), der_set(der(0x04, hashlib.sha256(info).digest()))),
        der_seq(der_oid(OID_CMS_ALGORITHM_PROTECTION),
                der_set(der_seq(sha256_alg, der(0xA2, der_oid(OID_HMAC_SHA256))))),  # RFC 6211, macAlgorithm [2]
    ])
    mac = hmac.new(mac_key, der(0x31, b"".join(attrs)), hashlib.sha256).digest()  # RFC 5652 9.2
    kek = hkdf_sha256(file_key, kdf_salt, b"PDFMAC", 32)
    pwri = der(0xA3, der(0x02, b"\0") + der(0xA0, der_oid(OID_PDF_MAC_WRAP_KDF))
               + der_seq(der_oid(OID_AES256_WRAP)) + der(0x04, aes_key_wrap(kek, mac_key)))
    auth_data = der_seq(
        der(0x02, b"\0"),                      # version 0
        der_set(pwri),                          # recipientInfos
        hmac_alg,                               # macAlgorithm
        der(0xA1, der_oid(OID_SHA256)),         # digestAlgorithm [1]
        encap,                                  # encapContentInfo
        der(0xA2, b"".join(attrs)),             # authAttrs [2]
        der(0x04, mac))                         # mac
    return der_seq(der_oid(OID_AUTH_DATA), der(0xA0, auth_data))


# ---------------------------------------------------------------------------
# Minimal TrueType font program (OpenType spec; PDF clause 9.6.3 / 9.9)
# ---------------------------------------------------------------------------

def _ttf_glyph(contours: list[list[tuple[int, int]]]) -> bytes:
    """A simple glyph with on-curve points only, int16 deltas."""
    if not contours:
        return b""
    pts = [p for c in contours for p in c]
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    out = struct.pack(">hhhhh", len(contours), min(xs), min(ys), max(xs), max(ys))
    end = -1
    for c in contours:
        end += len(c)
        out += struct.pack(">H", end)
    out += struct.pack(">H", 0)  # no instructions
    out += bytes([0x01]) * len(pts)  # flag: on-curve, x and y as int16
    px = py = 0
    for x, _ in pts:
        out += struct.pack(">h", x - px)
        px = x
    for _, y in pts:
        out += struct.pack(">h", y - py)
        py = y
    return out


def _checksum(data: bytes) -> int:
    data += bytes((-len(data)) % 4)
    return sum(struct.unpack(">%dI" % (len(data) // 4), data)) & 0xFFFFFFFF


def minimal_truetype() -> tuple[bytes, dict[int, int]]:
    """Builds a TrueType font with .notdef, 'H' and 'I' drawn from rectangles.

    Returns the font program and a map of character code to advance width
    (1000 units per em). Only the tables FreeType, Windows and macOS need to
    load a bare TrueType are present: head, hhea, maxp, OS/2, hmtx, cmap, loca,
    glyf, name, post.
    """
    upem = 1000
    glyphs = [
        [],  # .notdef
        [[(100, 0), (100, 700), (300, 700), (300, 400), (500, 400), (500, 700),  # H
          (700, 700), (700, 0), (500, 0), (500, 300), (300, 300), (300, 0)]],
        [[(100, 0), (100, 700), (300, 700), (300, 0)]],  # I
    ]
    advances = [500, 800, 400]
    chars = {0x48: 1, 0x49: 2}

    glyf = bytearray()
    loca = []
    for g in glyphs:
        loca.append(len(glyf))
        data = _ttf_glyph(g)
        glyf += data + bytes((-len(data)) % 4)
    loca.append(len(glyf))
    loca_tbl = b"".join(struct.pack(">H", o // 2) for o in loca)

    hmtx = b"".join(struct.pack(">Hh", adv, 100 if i else 0) for i, adv in enumerate(advances))

    head = struct.pack(">IIIIHHqqhhhhHHhhh",
                       0x00010000, 0x00010000, 0, 0x5F0F3CF5, 0x000B, upem, 0, 0,
                       0, 0, 700, 700, 0, 8, 2, 0, 0)
    hhea = struct.pack(">IhhhHhhhhhhhhhhhH",
                       0x00010000, 800, -200, 0, max(advances), 0, 0, 700, 1, 0, 0, 0, 0, 0, 0, 0, len(glyphs))
    maxp = struct.pack(">IHHHHHHHHHHHHHH", 0x00010000, len(glyphs), 12, 1, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0)
    os2 = struct.pack(">HhHHH" + "h" * 11, 3, 500, 400, 5, 0, 500, 300, 0, 0, 500, 300, 0, 0, 50, 300, 0)
    os2 += bytes(10)  # panose
    os2 += struct.pack(">IIII", 1, 0, 0, 0)  # ulUnicodeRange1..4: Basic Latin
    os2 += b"BRDS"  # achVendID
    os2 += struct.pack(">HHHhhhHHIIhhHHH", 0x0040, 0x48, 0x49, 800, -200, 0, 800, 200, 1, 0, 500, 700, 0, 0, 0)
    assert len(os2) == 96

    # cmap: one (3,1) format 4 subtable with a single segment for H..I.
    segs = [(0x48, 0x49, (1 - 0x48) & 0xFFFF), (0xFFFF, 0xFFFF, 1)]
    seg_x2 = len(segs) * 2
    sub = struct.pack(">HHHHHHH", 4, 16 + 8 * len(segs), 0, seg_x2, 4, 1, 0)
    sub += b"".join(struct.pack(">H", e) for _, e, _ in segs) + b"\0\0"
    sub += b"".join(struct.pack(">H", s) for s, _, _ in segs)
    sub += b"".join(struct.pack(">H", d) for _, _, d in segs)
    sub += b"\0\0" * len(segs)
    cmap = struct.pack(">HHHHI", 0, 1, 3, 1, 12) + sub

    names = [(1, "Broadside Minimal"), (2, "Regular"), (4, "Broadside Minimal"), (6, "BroadsideMinimal")]
    name_data = b""
    recs = b""
    for nid, text in names:
        enc = text.encode("utf-16-be")
        recs += struct.pack(">HHHHHH", 3, 1, 0x409, nid, len(enc), len(name_data))
        name_data += enc
    name = struct.pack(">HHH", 0, len(names), 6 + 12 * len(names)) + recs + name_data

    post = struct.pack(">IiHHIIIII", 0x00030000, 0, 0, 0, 0, 0, 0, 0, 0)

    tables = {b"OS/2": os2, b"cmap": cmap, b"glyf": bytes(glyf), b"head": head, b"hhea": hhea,
              b"hmtx": hmtx, b"loca": loca_tbl, b"maxp": maxp, b"name": name, b"post": post}
    tags = sorted(tables)
    n = len(tags)
    es = n.bit_length() - 1
    sr = (1 << es) * 16
    font = bytearray(struct.pack(">IHHHH", 0x00010000, n, sr, es, n * 16 - sr))
    offset = 12 + 16 * n
    body = bytearray()
    for tag in tags:
        data = tables[tag]
        font += tag + struct.pack(">III", _checksum(data), offset + len(body), len(data))
        body += data + bytes((-len(data)) % 4)
    font += body
    head_off = offset + sum(len(tables[t]) + (-len(tables[t])) % 4 for t in tags if t < b"head")
    adj = (0xB1B0AFBA - _checksum(bytes(font))) & 0xFFFFFFFF
    font[head_off + 8:head_off + 12] = struct.pack(">I", adj)
    return bytes(font), {c: advances[g] for c, g in chars.items()}


# The standard Macintosh glyph order of the 'post' table (Apple TrueType Reference Manual, "The 'post' table").
MAC_STANDARD_NAMES = (
    ".notdef .null nonmarkingreturn space exclam quotedbl numbersign dollar percent ampersand quotesingle parenleft "
    "parenright asterisk plus comma hyphen period slash zero one two three four five six seven eight nine colon "
    "semicolon less equal greater question at A B C D E F G H I J K L M N O P Q R S T U V W X Y Z bracketleft "
    "backslash bracketright asciicircum underscore grave a b c d e f g h i j k l m n o p q r s t u v w x y z "
    "braceleft bar braceright asciitilde Adieresis Aring Ccedilla Eacute Ntilde Odieresis Udieresis aacute agrave "
    "acircumflex adieresis atilde aring ccedilla eacute egrave ecircumflex edieresis iacute igrave icircumflex "
    "idieresis ntilde oacute ograve ocircumflex odieresis otilde uacute ugrave ucircumflex udieresis dagger degree "
    "cent sterling section bullet paragraph germandbls registered copyright trademark acute dieresis notequal AE "
    "Oslash infinity plusminus lessequal greaterequal yen mu partialdiff summation product pi integral ordfeminine "
    "ordmasculine Omega ae oslash questiondown exclamdown logicalnot radical florin approxequal Delta guillemotleft "
    "guillemotright ellipsis nonbreakingspace Agrave Atilde Otilde OE oe endash emdash quotedblleft quotedblright "
    "quoteleft quoteright divide lozenge ydieresis Ydieresis fraction currency guilsinglleft guilsinglright fi fl "
    "daggerdbl periodcentered quotesinglbase quotedblbase perthousand Acircumflex Ecircumflex Aacute Edieresis Egrave "
    "Iacute Icircumflex Idieresis Igrave Oacute Ocircumflex apple Ograve Uacute Ucircumflex Ugrave dotlessi "
    "circumflex tilde macron breve dotaccent ring cedilla hungarumlaut ogonek caron Lslash lslash Scaron scaron "
    "Zcaron zcaron brokenbar Eth eth Yacute yacute Thorn thorn minus multiply onesuperior twosuperior threesuperior "
    "onehalf onequarter threequarters franc Gbreve gbreve Idotaccent Scedilla scedilla Cacute cacute Ccaron ccaron "
    "dcroat").split()
assert len(MAC_STANDARD_NAMES) == 258

# Composite glyph component flags (OpenType 'glyf' table).
ARG_WORDS, ARGS_XY, HAVE_SCALE, MORE_COMPONENTS = 0x0001, 0x0002, 0x0008, 0x0020
HAVE_X_AND_Y_SCALE, HAVE_TWO_BY_TWO, HAVE_INSTRUCTIONS, USE_MY_METRICS = 0x0040, 0x0080, 0x0100, 0x0200
SCALED_OFFSET, UNSCALED_OFFSET = 0x0800, 0x1000


def f2dot14(value: float) -> bytes:
    return struct.pack(">h", round(value * 16384))


class TtfGlyph:
    """A glyph of a synthesized TrueType font: simple (points with on-curve flags) or composite (component records)."""

    def __init__(self, contours=None, components=None, instructions: bytes = b""):
        self.contours = contours or []      # [[(x, y, on_curve), ...], ...]
        self.components = components or []  # [(flags, glyph, arg1, arg2, transform), ...]
        self.instructions = instructions


def _f2(data: bytes) -> list[float]:
    return [v / 16384 for v in struct.unpack(">%dh" % (len(data) // 2), data)]


def _glyph_points(glyphs: list[TtfGlyph], gid: int) -> list[tuple[float, float]]:
    """The points of a glyph with composites applied (only to compute the header bounding boxes)."""
    g = glyphs[gid]
    if not g.components:
        return [(x, y) for c in g.contours for x, y, _ in c]
    points: list[tuple[float, float]] = []
    for flags, child, a1, a2, transform in g.components:
        pts = _glyph_points(glyphs, child)
        m = [1.0, 0.0, 0.0, 1.0]  # xscale, scale01, scale10, yscale
        if flags & HAVE_SCALE:
            s = _f2(transform)[0]
            m = [s, 0.0, 0.0, s]
        elif flags & HAVE_X_AND_Y_SCALE:
            sx, sy = _f2(transform)
            m = [sx, 0.0, 0.0, sy]
        elif flags & HAVE_TWO_BY_TWO:
            m = _f2(transform)
        pts = [(m[0] * x + m[2] * y, m[1] * x + m[3] * y) for x, y in pts]
        if flags & ARGS_XY:
            dx, dy = a1, a2
            if transform and flags & SCALED_OFFSET and not flags & UNSCALED_OFFSET:
                dx, dy = dx * (m[0] ** 2 + m[2] ** 2) ** 0.5, dy * (m[3] ** 2 + m[1] ** 2) ** 0.5
        else:
            dx, dy = points[a1][0] - pts[a2][0], points[a1][1] - pts[a2][1]
        points += [(x + dx, y + dy) for x, y in pts]
    return points


def _glyph_bbox(glyphs: list[TtfGlyph], gid: int) -> tuple[int, int, int, int]:
    pts = _glyph_points(glyphs, gid)
    if not pts:
        return (0, 0, 0, 0)
    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    return (int(min(xs)), int(min(ys)), int(max(xs)), int(max(ys)))


def _encode_glyph(glyphs: list[TtfGlyph], gid: int) -> bytes:
    """Encodes a glyph description: simple glyphs with short and same coordinates and repeated flags where they fit."""
    g = glyphs[gid]
    if not g.contours and not g.components:
        return b""
    bbox = _glyph_bbox(glyphs, gid)
    if g.components:
        out = struct.pack(">hhhhh", -1, *bbox)
        for i, (flags, child, a1, a2, transform) in enumerate(g.components):
            last = i == len(g.components) - 1
            flags |= 0 if last else MORE_COMPONENTS
            if last and g.instructions:
                flags |= HAVE_INSTRUCTIONS
            out += struct.pack(">HH", flags, child)
            fmt = (">hh" if flags & ARGS_XY else ">HH") if flags & ARG_WORDS else (">bb" if flags & ARGS_XY else ">BB")
            out += struct.pack(fmt, a1, a2) + transform
        if g.instructions:
            out += struct.pack(">H", len(g.instructions)) + g.instructions
        return out
    pts = [p for c in g.contours for p in c]
    out = struct.pack(">hhhhh", len(g.contours), *bbox)
    end = -1
    for c in g.contours:
        end += len(c)
        out += struct.pack(">H", end)
    out += struct.pack(">H", len(g.instructions)) + g.instructions
    flags, xs, ys = [], b"", b""
    px = py = 0
    for x, y, on in pts:
        dx, dy = x - px, y - py
        px, py = x, y
        f = 0x01 if on else 0x00
        if dx == 0:
            f |= 0x10
        elif -255 <= dx <= 255:
            f |= 0x02 | (0x10 if dx > 0 else 0)
            xs += bytes([abs(dx)])
        else:
            xs += struct.pack(">h", dx)
        if dy == 0:
            f |= 0x20
        elif -255 <= dy <= 255:
            f |= 0x04 | (0x20 if dy > 0 else 0)
            ys += bytes([abs(dy)])
        else:
            ys += struct.pack(">h", dy)
        flags.append(f)
    packed = b""
    i = 0
    while i < len(flags):  # a run of equal flags is written once with REPEAT (0x08) and a count
        run = 1
        while i + run < len(flags) and flags[i + run] == flags[i] and run < 256:
            run += 1
        packed += bytes([flags[i] | 0x08, run - 1]) if run > 1 else bytes([flags[i]])
        i += run
    return out + packed + xs + ys


def cmap_format0(mapping: dict[int, int]) -> bytes:
    return struct.pack(">HHH", 0, 262, 0) + bytes(mapping.get(c, 0) for c in range(256))


def cmap_format4(segments: list[tuple[int, int, int, list[int] | None]]) -> bytes:
    """Segments (start, end, idDelta, glyph ids or None); glyph ids go through idRangeOffset into glyphIdArray."""
    segments = segments + [(0xFFFF, 0xFFFF, 1, None)]
    n = len(segments)
    ends = b"".join(struct.pack(">H", e) for _, e, _, _ in segments)
    starts = b"".join(struct.pack(">H", s) for s, _, _, _ in segments)
    deltas = b"".join(struct.pack(">H", d & 0xFFFF) for _, _, d, _ in segments)
    offsets, array = b"", b""
    for i, (_, _, _, ids) in enumerate(segments):
        if ids is None:
            offsets += struct.pack(">H", 0)
        else:
            offsets += struct.pack(">H", 2 * (n - i) + len(array))
            array += b"".join(struct.pack(">H", g) for g in ids)
    es = n.bit_length() - 1
    body = struct.pack(">HHHH", 2 * n, 2 * (1 << es), es, 2 * n - 2 * (1 << es)) + ends + b"\0\0" + starts + deltas + offsets + array
    return struct.pack(">HHH", 4, 6 + len(body), 0) + body


def cmap_format6(first: int, ids: list[int]) -> bytes:
    return struct.pack(">HHHHH", 6, 10 + 2 * len(ids), 0, first, len(ids)) + b"".join(struct.pack(">H", g) for g in ids)


def cmap_format12(groups: list[tuple[int, int, int]]) -> bytes:
    body = b"".join(struct.pack(">III", s, e, g) for s, e, g in groups)
    return struct.pack(">HHIII", 12, 0, 16 + len(body), 0, len(groups)) + body


def cmap_table(subtables: list[tuple[int, int, bytes]]) -> bytes:
    subtables = sorted(subtables, key=lambda t: (t[0], t[1]))
    out = struct.pack(">HH", 0, len(subtables))
    offset = 4 + 8 * len(subtables)
    data = b""
    for platform, encoding, sub in subtables:
        out += struct.pack(">HHI", platform, encoding, offset + len(data))
        data += sub
    return out + data


def post_table(version: int, names: list[str] | None = None) -> bytes:
    """'post' version 0x00010000, 0x00020000 (names: index into the standard order or Pascal strings) or 0x00030000."""
    out = struct.pack(">IiHHIIIII", version, 0, 0, 0, 0, 0, 0, 0, 0)
    if version == 0x00020000:
        indexes, strings = [], b""
        custom: list[str] = []
        for name in names or []:
            if name in MAC_STANDARD_NAMES:
                indexes.append(MAC_STANDARD_NAMES.index(name))
            else:
                indexes.append(258 + len(custom))
                custom.append(name)
                strings += bytes([len(name)]) + name.encode("ascii")
        out += struct.pack(">H", len(indexes)) + b"".join(struct.pack(">H", i) for i in indexes) + strings
    return out


def ttf_font(glyphs: list[TtfGlyph], metrics: list[tuple[int, int]], n_hmetrics: int, cmap: bytes, post: bytes,
             ps_name: str, long_loca: bool = False, pad: bool = True) -> bytes:
    """A TrueType program from glyphs, (advance, lsb) per glyph (advances past n_hmetrics are dropped), cmap and post.

    Tables: head, hhea, maxp, hmtx, loca, glyf, cmap, name, post (the ones ISO 32000-2 Table 124 and 9.9 require, plus
    name and post). Glyphs are padded to 4 bytes unless ``pad`` is false (long loca permits odd offsets)."""
    upem = 1000
    glyf = bytearray()
    loca = []
    for gid in range(len(glyphs)):
        loca.append(len(glyf))
        data = _encode_glyph(glyphs, gid)
        glyf += data + (bytes((-len(data)) % 4) if pad else b"")
    loca.append(len(glyf))
    loca_tbl = b"".join(struct.pack(">I", o) if long_loca else struct.pack(">H", o // 2) for o in loca)
    boxes = [_glyph_bbox(glyphs, g) for g in range(len(glyphs)) if glyphs[g].contours or glyphs[g].components]
    bbox = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
    hmtx = b"".join(struct.pack(">Hh", a, l) for a, l in metrics[:n_hmetrics])
    hmtx += b"".join(struct.pack(">h", l) for _, l in metrics[n_hmetrics:])
    head = struct.pack(">IIIIHHqqhhhhHHhhh", 0x00010000, 0x00010000, 0, 0x5F0F3CF5, 0x000B, upem, 0, 0,
                       *bbox, 0, 8, 2, 1 if long_loca else 0, 0)
    hhea = struct.pack(">IhhhHhhhhhhhhhhhH", 0x00010000, 800, -200, 0, max(a for a, _ in metrics), 0, 0, bbox[2],
                       1, 0, 0, 0, 0, 0, 0, 0, n_hmetrics)
    maxp = struct.pack(">IHHHHHHHHHHHHHH", 0x00010000, len(glyphs), 64, 4, 64, 4, 2, 0, 0, 0, 0, 0, 0, 4, 2)
    names = [(1, ps_name), (2, "Regular"), (4, ps_name), (6, ps_name)]
    name_data, recs = b"", b""
    for nid, text in names:
        enc = text.encode("utf-16-be")
        recs += struct.pack(">HHHHHH", 3, 1, 0x409, nid, len(enc), len(name_data))
        name_data += enc
    name = struct.pack(">HHH", 0, len(names), 6 + 12 * len(names)) + recs + name_data
    tables = {b"cmap": cmap, b"glyf": bytes(glyf), b"head": head, b"hhea": hhea, b"hmtx": hmtx, b"loca": loca_tbl,
              b"maxp": maxp, b"name": name, b"post": post}
    tags = sorted(tables)
    n = len(tags)
    es = n.bit_length() - 1
    sr = (1 << es) * 16
    font = bytearray(struct.pack(">IHHHH", 0x00010000, n, sr, es, n * 16 - sr))
    offset = 12 + 16 * n
    body = bytearray()
    head_off = 0
    for tag in tags:
        data = tables[tag]
        if tag == b"head":
            head_off = offset + len(body)
        font += tag + struct.pack(">III", _checksum(data), offset + len(body), len(data))
        body += data + bytes((-len(data)) % 4)
    font += body
    adj = (0xB1B0AFBA - _checksum(bytes(font))) & 0xFFFFFFFF
    font[head_off + 8:head_off + 12] = struct.pack(">I", adj)
    return bytes(font)


def ttf_rect(x0: int, y0: int, x1: int, y1: int) -> TtfGlyph:
    return TtfGlyph([[(x0, y0, True), (x0, y1, True), (x1, y1, True), (x1, y0, True)]])


# ---------------------------------------------------------------------------
# Minimal CFF font program (Adobe TN 5176 CFF, TN 5177 Type 2 charstrings; PDF clause 9.9)
# ---------------------------------------------------------------------------

T2_OPERATORS = {
    "hstem": [1], "vstem": [3], "vmoveto": [4], "rlineto": [5], "hlineto": [6], "vlineto": [7], "rrcurveto": [8],
    "callsubr": [10], "return": [11], "endchar": [14], "hstemhm": [18], "hintmask": [19], "cntrmask": [20],
    "rmoveto": [21], "hmoveto": [22], "vstemhm": [23], "rcurveline": [24], "rlinecurve": [25], "vvcurveto": [26],
    "hhcurveto": [27], "callgsubr": [29], "vhcurveto": [30], "hvcurveto": [31],
    "div": [12, 12], "flex": [12, 35], "hflex": [12, 34], "hflex1": [12, 36], "flex1": [12, 37],
}

# The standard strings (TN 5176 Appendix A) the corpus fonts use; other names go to the String INDEX.
CFF_STANDARD_SIDS = {".notdef": 0, "space": 1, "A": 34, "H": 41, "I": 42, "O": 48, "S": 52, "acute": 125, "Aacute": 171}


def t2_number(value: int) -> bytes:
    """TN 5177 Table 1: integers in the shortest form (32-246, 247-254 two-byte, 28 three-byte)."""
    if -107 <= value <= 107:
        return bytes([value + 139])
    if 108 <= value <= 1131:
        return bytes([((value - 108) >> 8) + 247, (value - 108) & 0xFF])
    if -1131 <= value <= -108:
        return bytes([((-value - 108) >> 8) + 251, (-value - 108) & 0xFF])
    return b"\x1c" + struct.pack(">h", value)


def t2(*tokens) -> bytes:
    """A Type 2 charstring: ints are operands, strings operator names, bytes raw (hint masks)."""
    out = b""
    for token in tokens:
        if isinstance(token, int):
            out += t2_number(token)
        elif isinstance(token, str):
            out += bytes(T2_OPERATORS[token])
        else:
            out += token
    return out


def cff_index(items: list[bytes]) -> bytes:
    """TN 5176 section 5 Table 7: count, offSize from the largest offset, 1-based offsets, data."""
    if not items:
        return b"\x00\x00"
    total = sum(len(i) for i in items) + 1
    off_size = 1 if total < 0x100 else 2 if total < 0x10000 else 3 if total < 0x1000000 else 4
    out = struct.pack(">HB", len(items), off_size)
    offset = 1
    for i in range(len(items) + 1):
        out += offset.to_bytes(off_size, "big")
        if i < len(items):
            offset += len(items[i])
    return out + b"".join(items)


def cff_dict_int(value: int) -> bytes:
    """TN 5176 Table 3, in the 5-byte form (operator 29) so every offset has a fixed size."""
    return b"\x1d" + struct.pack(">i", value)


def cff_font(name: bytes, glyphs: list[tuple[str, bytes]], encoding: bytes, local_subrs: list[bytes],
             global_subrs: list[bytes], default_width: int, nominal_width: int) -> bytes:
    """A one-font CFF program: header, Name, Top DICT, String and Global Subr INDEXes, a format 0 charset, the
    given custom encoding, CharStrings, Private DICT (defaultWidthX, nominalWidthX, Subrs) and local Subrs."""
    strings: list[bytes] = []

    def sid(glyph: str) -> int:
        if glyph in CFF_STANDARD_SIDS:
            return CFF_STANDARD_SIDS[glyph]
        if glyph.encode() not in strings:
            strings.append(glyph.encode())
        return 391 + strings.index(glyph.encode())

    charset = b"\x00" + b"".join(struct.pack(">H", sid(g)) for g, _ in glyphs[1:])
    char_strings = cff_index([cs for _, cs in glyphs])
    subrs = cff_index(local_subrs) if local_subrs else b""
    private = cff_dict_int(default_width) + b"\x14" + cff_dict_int(nominal_width) + b"\x15"
    if local_subrs:
        private += cff_dict_int(len(private) + 6) + b"\x13"  # Subrs, relative to the Private DICT (TN 5176 p.25)

    def top(charset_off: int, encoding_off: int, char_strings_off: int, private_off: int) -> bytes:
        return (cff_dict_int(charset_off) + b"\x0f" + cff_dict_int(encoding_off) + b"\x10"
                + cff_dict_int(char_strings_off) + b"\x11" + cff_dict_int(len(private)) + cff_dict_int(private_off) + b"\x12")

    head = b"\x01\x00\x04\x04" + cff_index([name])
    top_len = len(cff_index([top(0, 0, 0, 0)]))
    body = cff_index(strings) + cff_index(global_subrs)
    charset_off = len(head) + top_len + len(body)
    encoding_off = charset_off + len(charset)
    char_strings_off = encoding_off + len(encoding)
    private_off = char_strings_off + len(char_strings)
    top_dict = cff_index([top(charset_off, encoding_off, char_strings_off, private_off)])
    assert len(top_dict) == top_len
    return head + top_dict + body + charset + encoding + char_strings + private + subrs


def minimal_cff() -> tuple[bytes, list[tuple[str, int, int]]]:
    """A CFF font (TN 5176) with Type 2 charstrings (TN 5177) covering lines, curves, hints, subroutines, flex,
    an arithmetic operand, an accented character and both width forms. Returns the program and, per glyph after
    .notdef, (name, code in the custom encoding, advance). nominalWidthX 500, defaultWidthX 600.

    Outlines, in glyph space (1000 units per em):
      H      M 100,0 L 100,700 L 300,700 L 300,400 L 500,400 L 500,700 L 700,700 L 700,0 L 500,0 L 500,300
             L 300,300 L 300,0 Z  (vlineto, hlineto, rlineto; hstemhm, vstemhm, hintmask; width 500 + 300)
      I      M 100,0 L 100,700 L 300,700 L 300,0 Z M 50,0 L 350,0 L 350,50 L 50,50 Z  (body in local subr 0,
             serif in global subr 0, both called with the bias 107; default width)
      O      M 400,0 C 550,0 650,200 650,400 C 650,600 550,800 400,800 C 250,800 150,600 150,400
             C 150,200 250,0 400,0 Z  (hvcurveto, vhcurveto, rrcurveto; width 500 + 200)
      A      M 0,0 L 300,700 L 600,0 Z
      acute  M 0,0 L 100,100 L 150,50 Z  (width 500 - 300)
      Aacute A, then acute moved by (150, 750): endchar with adx ady bchar achar (StandardEncoding 65 and 194)
      S      M 0,300 C 100,350 200,400 300,400 C 400,400 500,350 600,300 L 600,0 L 0,0 Z  (rmoveto dy = 600 2 div,
             flex)
    Custom encoding format 1 (0x80: with a supplement): ranges H I (0x48-0x49), O (0x4F), A (0x41), acute (0xC2),
    Aacute (0xC1), S (0x53); the supplement also encodes A at 0x61 (TN 5176 section 12, Table 14)."""
    glyphs = [
        (".notdef", t2("endchar")),
        ("H", t2(300, 0, 50, 650, 50, "hstemhm", 100, 200, 400, 200, "vstemhm", "hintmask", b"\xf0",
                 100, 0, "rmoveto", 700, 200, -300, "vlineto", 200, 300, 200, "hlineto", 0, -700, -200, 0, "rlineto",
                 300, -200, -300, "vlineto", "endchar")),
        ("I", t2(-107, "callsubr", -107, "callgsubr", "endchar")),
        ("O", t2(200, 400, 0, "rmoveto", 150, 100, 200, 200, "hvcurveto", 200, -100, 200, -150, "vhcurveto",
                 -150, 0, -100, -200, 0, -200, "rrcurveto", -200, 100, -200, 150, "vhcurveto", "endchar")),
        ("A", t2(0, 0, "rmoveto", 300, 700, 300, -700, "rlineto", "endchar")),
        ("acute", t2(-300, 0, 0, "rmoveto", 100, 100, 50, -50, "rlineto", "endchar")),
        ("Aacute", t2(150, 750, 65, 194, "endchar")),
        ("S", t2(0, 600, 2, "div", "rmoveto", 100, 50, 100, 50, 100, 0, 100, 0, 100, -50, 100, -50, 50, "flex",
                 -300, "vlineto", -600, "hlineto", "endchar")),
    ]
    local_subrs = [t2(100, 0, "rmoveto", 700, 200, -700, "vlineto", "return")]
    global_subrs = [t2(-250, 0, "rmoveto", 300, 50, -300, "hlineto", "return")]
    ranges = [(0x48, 1), (0x4F, 0), (0x41, 0), (0xC2, 0), (0xC1, 0), (0x53, 0)]
    encoding = bytes([0x81, len(ranges)]) + b"".join(bytes(r) for r in ranges)
    encoding += bytes([1, 0x61]) + struct.pack(">H", CFF_STANDARD_SIDS["A"])
    font = cff_font(b"BroadsideCff", glyphs, encoding, local_subrs, global_subrs, 600, 500)
    advances = {"H": 800, "I": 600, "O": 700, "A": 600, "acute": 200, "Aacute": 600, "S": 600}
    codes = {"H": 0x48, "I": 0x49, "O": 0x4F, "A": 0x41, "acute": 0xC2, "Aacute": 0xC1, "S": 0x53}
    return font, [(g, codes[g], advances[g]) for g, _ in glyphs[1:]]


def sfnt(version: int, tables: dict[bytes, bytes]) -> bytes:
    """An sfnt file (OpenType "Organization of an OpenType font"): sorted table directory with checksums, tables
    padded to 4 bytes, head.checkSumAdjustment set."""
    tags = sorted(tables)
    n = len(tags)
    es = n.bit_length() - 1
    sr = (1 << es) * 16
    font = bytearray(struct.pack(">IHHHH", version, n, sr, es, n * 16 - sr))
    offset = 12 + 16 * n
    body = bytearray()
    head_off = None
    for tag in tags:
        data = tables[tag]
        if tag == b"head":
            head_off = offset + len(body)
        font += tag + struct.pack(">III", _checksum(data), offset + len(body), len(data))
        body += data + bytes((-len(data)) % 4)
    font += body
    if head_off is not None:
        adj = (0xB1B0AFBA - _checksum(bytes(font))) & 0xFFFFFFFF
        font[head_off + 8:head_off + 12] = struct.pack(">I", adj)
    return bytes(font)


def minimal_otf_cff() -> bytes:
    """minimal_cff() wrapped in an OpenType font (OTTO) with the tables an OpenType CFF font has: CFF, cmap ((3,1)
    format 4 consistent with the charset), head, hhea, hmtx, maxp 0.5, name, OS/2, post 3.0."""
    cff, glyphs = minimal_cff()
    unicode = {"H": 0x48, "I": 0x49, "O": 0x4F, "A": 0x41, "acute": 0xB4, "Aacute": 0xC1, "S": 0x53}
    gid = {g: i + 1 for i, (g, _, _) in enumerate(glyphs)}
    segments = sorted((unicode[g], gid[g]) for g in unicode)
    cmap = cmap_table([(3, 1, cmap_format4([(u, u, (g - u) & 0xFFFF, None) for u, g in segments]))])
    advances = [600] + [adv for _, _, adv in glyphs]
    head = struct.pack(">IIIIHHqqhhhhHHhhh", 0x00010000, 0x00010000, 0, 0x5F0F3CF5, 0x000B, 1000, 0, 0,
                       0, 0, 700, 850, 0, 8, 2, 0, 0)
    hhea = struct.pack(">IhhhHhhhhhhhhhhhH", 0x00010000, 800, -200, 0, max(advances), 0, 0, 700, 1, 0, 0, 0, 0,
                       0, 0, 0, len(advances))
    hmtx = b"".join(struct.pack(">Hh", a, 0) for a in advances)
    maxp = struct.pack(">IH", 0x00005000, len(advances))
    names = [(1, "Broadside Cff"), (2, "Regular"), (4, "Broadside Cff"), (6, "BroadsideCff")]
    name_data, recs = b"", b""
    for nid, text in names:
        enc = text.encode("utf-16-be")
        recs += struct.pack(">HHHHHH", 3, 1, 0x409, nid, len(enc), len(name_data))
        name_data += enc
    name = struct.pack(">HHH", 0, len(names), 6 + 12 * len(names)) + recs + name_data
    os2 = struct.pack(">HhHHH" + "h" * 11, 3, 600, 400, 5, 0, 500, 300, 0, 0, 500, 300, 0, 0, 50, 300, 0)
    os2 += bytes(10) + struct.pack(">IIII", 1, 0, 0, 0) + b"BRDS"
    os2 += struct.pack(">HHHhhhHHIIhhHHH", 0x0040, 0x41, 0xC1, 800, -200, 0, 850, 200, 1, 0, 500, 700, 0, 0, 0)
    assert len(os2) == 96
    post = struct.pack(">IiHHIIIII", 0x00030000, 0, 0, 0, 0, 0, 0, 0, 0)
    return sfnt(0x4F54544F, {b"CFF ": cff, b"cmap": cmap, b"head": head, b"hhea": hhea, b"hmtx": hmtx,
                             b"maxp": maxp, b"name": name, b"OS/2": os2, b"post": post})


# ---------------------------------------------------------------------------
# The corpus
# ---------------------------------------------------------------------------

def file_id(name: str) -> bytes:
    return hashlib.md5(b"broadside-corpus:" + name.encode()).digest()


def fixed_bytes(label: str, n: int) -> bytes:
    """Deterministic stand-in for the random bytes the spec asks for."""
    out = b""
    i = 0
    while len(out) < n:
        out += hashlib.sha256(b"broadside-corpus:%s:%d" % (label.encode(), i)).digest()
        i += 1
    return out[:n]


def gen_empty_page() -> bytes:
    return simple_file([(1, catalog()), (2, pages()), (3, page())])


def gen_pdf20_header() -> bytes:
    """7.5.2 header and 7.7.2 Table 29 Version; 7.5.5 Table 15: a PDF 2.0 trailer shall carry ID."""
    id0 = file_id("pdf20-header").hex().encode()
    return simple_file([(1, catalog(b" /Version /2.0")), (2, pages()), (3, page())], version="2.0",
                       trailer_extra=b" /ID [<%s> <%s>]" % (id0, id0))


def gen_text_standard14() -> bytes:
    return text_file(stream(b"", text_content(b"Hello, Broadside")))


def gen_text_truetype_embedded() -> bytes:
    font, widths = minimal_truetype()
    first, last = min(widths), max(widths)
    w = b" ".join(b"%d" % widths[c] for c in range(first, last + 1))
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, font=5)),
        (4, stream(b"", text_content(b"HI"))),
        (5, b"<< /Type /Font /Subtype /TrueType /BaseFont /BroadsideMinimal /FirstChar %d /LastChar %d "
            b"/Widths [%s] /Encoding /WinAnsiEncoding /FontDescriptor 6 0 R >>" % (first, last, w)),
        (6, b"<< /Type /FontDescriptor /FontName /BroadsideMinimal /Flags 32 /FontBBox [0 0 700 700] "
            b"/ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 200 /FontFile2 7 0 R >>"),
        (7, stream(b"/Length1 %d" % len(font), font)),
    ], binary=True)


def codes_content(lines: list[tuple[str, bytes]]) -> bytes:
    """One text line per (font resource, codes); codes as a hexadecimal string so any byte can be shown."""
    out = b"BT /%s 24 Tf 72 700 Td <%s> Tj" % (lines[0][0].encode(), lines[0][1].hex().upper().encode())
    for font, codes in lines[1:]:
        out += b" /%s 24 Tf 0 -36 Td <%s> Tj" % (font.encode(), codes.hex().upper().encode())
    return out + b" ET"


def font_file(fonts: list[bytes], codes: list[bytes], extra: list[tuple[int, bytes]] | None = None) -> bytes:
    """A page showing ``codes[i]`` in font ``F{i+1}`` (object 5 + i); ``extra`` objects follow the fonts."""
    refs = b" ".join(b"/F%d %d 0 R" % (i + 1, 5 + i) for i in range(len(fonts)))
    objects = [
        (1, catalog()),
        (2, pages()),
        (3, b"<< /Type /Page /Parent 2 0 R /MediaBox %s /Contents 4 0 R /Resources << /Font << %s >> >> >>" % (LETTER, refs)),
        (4, stream(b"", codes_content([("F%d" % (i + 1), c) for i, c in enumerate(codes)]))),
    ]
    objects += [(5 + i, body) for i, body in enumerate(fonts)]
    objects += extra or []
    return simple_file(objects)


def std14(base_font: bytes, entries: bytes = b"", subtype: bytes = b"Type1") -> bytes:
    """9.6.2.2: a non-embedded standard 14 font dictionary without FirstChar, LastChar, Widths or FontDescriptor."""
    return b"<< /Type /Font /Subtype /%s /BaseFont /%s%s >>" % (subtype, base_font, b" " + entries if entries else b"")


def gen_text_standard14_differences() -> bytes:
    """9.6.5.1 Table 112: an encoding dictionary without BaseEncoding on a non-embedded nonsymbolic font differs from
    StandardEncoding; codes 0x27 0x60 0x80 0x81 0xC8."""
    enc = b"/Encoding << /Type /Encoding /Differences [39 /quotesingle 128 /Euro /bullet 200 /Adieresis] >>"
    return font_file([std14(b"Helvetica", enc)], [b"\x27\x60\x80\x81\xc8"])


def gen_text_standard14_winansi_quirks() -> bytes:
    """Annex D.2 and its notes 1, 2, 3, 5, 6: the WinAnsiEncoding codes that differ from StandardEncoding or are
    shared by two names."""
    return font_file([HELVETICA], [b"\x27\x60\x7f\x81\x80\xa0\xad\x8e"])


def gen_text_standard14_macroman() -> bytes:
    """Annex D.2: MacRomanEncoding, its note 6 (0xCA space) and its differences from Mac OS Roman (9.6.5.4 Table 113:
    0xDB is currency, 0xAD is unused)."""
    return font_file([std14(b"Times-Roman", b"/Encoding /MacRomanEncoding")], [b"\x80\xca\xdb\xa5\xad"])


def gen_text_standard14_symbol() -> bytes:
    """9.6.5.1 and Annex D.5: the Symbol font's built-in encoding; F2 names WinAnsiEncoding, which a reader ignores for
    the non-embedded symbolic font (as pdf.js does, issue16464)."""
    return font_file([std14(b"Symbol"), std14(b"Symbol", b"/Encoding /WinAnsiEncoding")], [b"abg\xa0", b"abg\xa0"])


def gen_text_standard14_symbol_differences() -> bytes:
    """9.6.5.1 Table 112: without BaseEncoding the differences of a symbolic font apply to its built-in encoding,
    so 0x61 stays alpha."""
    enc = b"/Encoding << /Type /Encoding /Differences [66 /Gamma] >>"
    return font_file([std14(b"Symbol", enc)], [b"aB"])


def gen_text_standard14_zapfdingbats() -> bytes:
    """Annex D.6: the ZapfDingbats built-in encoding, including 0x80-0x8D, which its AFM file encodes but Table D.6
    does not list."""
    return font_file([std14(b"ZapfDingbats")], [b"\x21\x6c\x80\x8d"])


def gen_text_standard14_widths() -> bytes:
    """9.6.2.1 Table 109: a standard 14 font with all four of FirstChar, LastChar, Widths and FontDescriptor; the
    Widths override the font's metrics and other codes take MissingWidth (9.8.1 Table 120)."""
    font = std14(b"Courier", b"/FirstChar 65 /LastChar 66 /Widths [500 700] /Encoding /WinAnsiEncoding /FontDescriptor 6 0 R")
    descriptor = (b"<< /Type /FontDescriptor /FontName /Courier /Flags 33 /FontBBox [-23 -250 715 805] /ItalicAngle 0 "
                  b"/Ascent 629 /Descent -157 /CapHeight 562 /XHeight 426 /StemV 51 /StemH 51 /MissingWidth 777 >>")
    return font_file([font], [b"ABC"], [(6, descriptor)])


def gen_text_standard14_alias() -> bytes:
    """9.6.2.2 and 9.6.3: a non-embedded TrueType font named /Arial,Bold, which readers take as Helvetica-Bold
    (PDFBox Standard14Fonts, pdf.js getStdFontMap)."""
    return font_file([std14(b"Arial,Bold", b"/Encoding /WinAnsiEncoding", subtype=b"TrueType")], [b"Hi!"])


def gen_text_nonembedded_substitute() -> bytes:
    """9.6.2.1 Table 109, 9.8.1 Table 120 and 9.8.2 Table 121: a non-embedded Type 1 font that is not a standard 14
    font, /BroadsideSerif-Italic, whose descriptor says serif, nonsymbolic and italic (Flags 98, ItalicAngle -12), so a
    reader without that font substitutes a similar one (Times-Italic) and keeps the Widths."""
    widths = b" ".join(b"611" if code == 83 else b"500" for code in range(83, 115))
    font = (b"<< /Type /Font /Subtype /Type1 /BaseFont /BroadsideSerif-Italic /FirstChar 83 /LastChar 114 /Widths ["
            + widths + b"] /Encoding /WinAnsiEncoding /FontDescriptor 6 0 R >>")
    descriptor = (b"<< /Type /FontDescriptor /FontName /BroadsideSerif-Italic /Flags 98 /FontBBox [-169 -217 1010 883] "
                  b"/ItalicAngle -12 /Ascent 683 /Descent -217 /CapHeight 653 /XHeight 441 /StemV 76 >>")
    return font_file([font], [b"Serif"], [(6, descriptor)])


def gen_text_type1_symbolic_noencoding() -> bytes:
    """9.6.5.1 and 9.8.2: a non-embedded symbolic Type 1 font that is not a standard 14 font and has no Encoding; its
    built-in encoding is unknown without its program, and its widths come from Widths."""
    font = (b"<< /Type /Font /Subtype /Type1 /BaseFont /BroadsideSymbolic /FirstChar 65 /LastChar 67 "
            b"/Widths [600 650 700] /FontDescriptor 6 0 R >>")
    descriptor = (b"<< /Type /FontDescriptor /FontName /BroadsideSymbolic /Flags 4 /FontBBox [0 -200 1000 800] "
                  b"/ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 >>")
    return font_file([font], [b"ABC"], [(6, descriptor)])


def truetype_file(font: bytes, base_font: bytes, flags: int, first: int, widths: list[int], encoding: bytes | None,
                  codes: bytes) -> bytes:
    """A page showing ``codes`` in an embedded TrueType font (9.6.3, 9.8, 9.9 Table 125: Length1 is the program length)."""
    enc = b" /Encoding " + encoding if encoding is not None else b""
    w = b" ".join(b"%d" % x for x in widths)
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, b"<< /Type /Page /Parent 2 0 R /MediaBox %s /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>" % LETTER),
        (4, stream(b"", codes_content([("F1", codes)]))),
        (5, b"<< /Type /Font /Subtype /TrueType /BaseFont /%s /FirstChar %d /LastChar %d /Widths [%s]%s "
            b"/FontDescriptor 6 0 R >>" % (base_font, first, first + len(widths) - 1, w, enc)),
        (6, b"<< /Type /FontDescriptor /FontName /%s /Flags %d /FontBBox [-300 0 700 1400] /ItalicAngle 0 /Ascent 800 "
            b"/Descent -200 /CapHeight 700 /StemV 200 /FontFile2 7 0 R >>" % (base_font, flags)),
        (7, stream(b"/Length1 %d" % len(font), font)),
    ], binary=True)


def gen_text_truetype_composite() -> bytes:
    """9.6.3, 9.6.5.4, 9.9: simple glyphs with off-curve points (an all-off-curve contour, a contour starting off-curve),
    short, same and repeated coordinate flags, and composite glyphs with every argument and transform form of the
    OpenType 'glyf' table (word and byte offsets, scale with SCALED/UNSCALED/neither offset flag, x-and-y scale, 2x2,
    point matching, nesting, USE_MY_METRICS, instructions). hmtx has fewer advances than glyphs; the (3,1) format 4
    cmap has a glyphIdArray segment (with a 0 entry that stays 0) and negative deltas. Glyph 15 has lsb != xMin."""
    I = ttf_rect(100, 0, 300, 700)
    glyphs = [
        TtfGlyph(),                                                                          # 0 .notdef
        I,                                                                                   # 1 'I'
        TtfGlyph([[(250, 0, False), (500, 250, False), (250, 500, False), (0, 250, False)]]),  # 2 'o': all off-curve
        TtfGlyph([[(0, 0, False), (200, 0, True), (200, 200, True), (0, 200, True)]]),        # 3 'c': starts off-curve
        TtfGlyph(components=[(ARG_WORDS | ARGS_XY, 1, 400, 300, b"")]),                      # 4 word offset
        TtfGlyph(components=[(ARGS_XY, 1, 10, 20, b"")]),                                    # 5 byte offset
        TtfGlyph(components=[(ARGS_XY | HAVE_SCALE | SCALED_OFFSET, 1, 100, 40, f2dot14(0.5))]),    # 6
        TtfGlyph(components=[(ARGS_XY | HAVE_SCALE | UNSCALED_OFFSET, 1, 100, 40, f2dot14(0.5))]),  # 7
        TtfGlyph(components=[(ARGS_XY | HAVE_SCALE, 1, 100, 40, f2dot14(0.5))]),                    # 8
        TtfGlyph(components=[(ARG_WORDS | ARGS_XY | HAVE_X_AND_Y_SCALE, 1, 400, 0,
                              f2dot14(-1) + f2dot14(1))]),                                   # 9 mirror
        TtfGlyph(components=[(ARG_WORDS | ARGS_XY | HAVE_TWO_BY_TWO, 1, 700, 0,
                              f2dot14(0) + f2dot14(1) + f2dot14(-1) + f2dot14(0))]),         # 10 rotation by 90
        TtfGlyph(components=[(ARGS_XY, 1, 0, 0, b""), (0, 1, 2, 0, b"")]),                   # 11 point matching
        TtfGlyph(components=[(ARGS_XY, 5, 0, 100, b"")]),                                    # 12 nested
        TtfGlyph(components=[(ARGS_XY | USE_MY_METRICS, 3, 50, 0, b"")]),                    # 13 USE_MY_METRICS
        TtfGlyph(components=[(ARGS_XY, 2, 0, 0, b"")], instructions=b"\x00\x01\x02"),        # 14 instructions
        ttf_rect(100, 0, 300, 700),                                                          # 15 lsb 150, xMin 100
    ]
    advances = [500, 400, 500, 300, 700, 400, 400, 400, 400, 400, 700, 600]
    metrics = []
    for gid in range(len(glyphs)):
        lsb = _glyph_bbox(glyphs, gid)[0] + (50 if gid == 15 else 0)
        metrics.append((advances[min(gid, len(advances) - 1)], lsb))
    cmap = cmap_table([(3, 1, cmap_format4([
        (0x30, 0x3C, 1, [g - 1 for g in range(4, 15)] + [0, 14]),  # '0'..':' -> 4..14, ';' -> 0, '<' -> 15
        (0x49, 0x49, 1 - 0x49, None),
        (0x63, 0x63, 3 - 0x63, None),
        (0x6F, 0x6F, 2 - 0x6F, None),
    ]))])
    font = ttf_font(glyphs, metrics, len(advances), cmap, post_table(0x00030000), "BroadsideComposite")
    codes = b"Ioc0123456789:;<"
    gids = {0x49: 1, 0x6F: 2, 0x63: 3, 0x3B: 0, 0x3C: 15}
    gids.update({0x30 + i: 4 + i for i in range(11)})
    widths = [metrics[gids[c]][0] if c in gids else 0 for c in range(0x30, 0x70)]
    return truetype_file(font, b"BroadsideComposite", 32, 0x30, widths, b"/WinAnsiEncoding", codes)


def gen_text_truetype_symbolic() -> bytes:
    """9.6.5.4: a symbolic font (Flags 4) without Encoding selects glyphs by code: the (3,0) subtable at 0xF000 + code
    comes before the (1,0) subtable, which maps the same codes to other glyphs; code 0x44 is only in (1,0)."""
    glyphs = [TtfGlyph(), ttf_rect(100, 0, 300, 700), ttf_rect(100, 0, 500, 500), ttf_rect(100, 0, 700, 300)]
    metrics = [(500, 0), (400, 100), (600, 100), (800, 100)]
    cmap = cmap_table([
        (1, 0, cmap_format0({0x41: 3, 0x42: 2, 0x43: 1, 0x44: 2})),
        (3, 0, cmap_format4([(0xF041, 0xF043, 1 - 0xF041, None)])),
    ])
    font = ttf_font(glyphs, metrics, len(metrics), cmap, post_table(0x00030000), "BroadsideSymbolic")
    return truetype_file(font, b"BroadsideSymbolic", 4, 0x41, [400, 600, 800, 600], None, b"ABCD")


def gen_text_truetype_macroman() -> bytes:
    """9.6.5.4 and Table 113: a nonsymbolic font whose program has only a (1,0) cmap (format 6): names from the encoding
    (WinAnsi base with Differences) map to Mac OS Roman codes (Euro is 219, eacute 0x8E); a name with no Mac OS Roman code
    (brds.alt) is found through the 'post' format 2 names."""
    glyphs = [TtfGlyph(), ttf_rect(100, 0, 300, 700), ttf_rect(100, 0, 500, 500), ttf_rect(100, 0, 700, 300)]
    metrics = [(500, 0), (400, 100), (600, 100), (800, 100)]
    ids = [0] * (0xDB - 0x8E + 1)
    ids[0] = 2            # 0x8E eacute
    ids[0xDB - 0x8E] = 1  # 0xDB Euro (Table 113)
    cmap = cmap_table([(1, 0, cmap_format6(0x8E, ids))])
    post = post_table(0x00020000, [".notdef", "Euro", "eacute", "brds.alt"])
    font = ttf_font(glyphs, metrics, len(metrics), cmap, post, "BroadsideMacRoman")
    widths = [0] * (0xE9 - 0x80 + 1)
    widths[0], widths[1], widths[0xE9 - 0x80] = 400, 800, 600
    return truetype_file(font, b"BroadsideMacRoman", 32, 0x80, widths,
                         b"<< /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [129 /brds.alt] >>",
                         b"\x80\xe9\x81")


def gen_text_truetype_loca_long() -> bytes:
    """9.6.5.4 and the OpenType 'loca' table: long offsets (indexToLocFormat 1) with an odd-length, unpadded glyph; the
    only cmap is (3,10) format 12, a Unicode subtable that stands in for (3,1); 'post' version 1.0."""
    H = TtfGlyph([[(x, y, True) for x, y in [(100, 0), (100, 700), (300, 700), (300, 400), (500, 400), (500, 700),
                                             (700, 700), (700, 0), (500, 0), (500, 300), (300, 300), (300, 0)]]],
                 instructions=b"\x00")
    glyphs = [TtfGlyph(), H, ttf_rect(100, 0, 300, 700)]
    metrics = [(500, 0), (800, 100), (400, 100)]
    cmap = cmap_table([(3, 10, cmap_format12([(0x48, 0x49, 1), (0x1F600, 0x1F600, 2)]))])
    font = ttf_font(glyphs, metrics, len(metrics), cmap, post_table(0x00010000), "BroadsideLongLoca",
                    long_loca=True, pad=False)
    assert len(_encode_glyph(glyphs, 1)) % 2 == 1
    return truetype_file(font, b"BroadsideLongLoca", 32, 0x48, [800, 400], b"/WinAnsiEncoding", b"HI")


def cff_file(subtype: bytes, font: bytes, flags: int, widths: dict[int, int], encoding: bytes | None,
             codes: bytes) -> bytes:
    """A page showing ``codes`` in a Type 1 font dictionary whose program is a FontFile3 stream (9.6.2, 9.9 Tables
    124 and 125: Subtype required, no Length1/2/3)."""
    first, last = min(widths), max(widths)
    w = b" ".join(b"%d" % widths.get(c, 0) for c in range(first, last + 1))
    enc = b" /Encoding " + encoding if encoding is not None else b""
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, b"<< /Type /Page /Parent 2 0 R /MediaBox %s /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>" % LETTER),
        (4, stream(b"", codes_content([("F1", codes)]))),
        (5, b"<< /Type /Font /Subtype /Type1 /BaseFont /BroadsideCff /FirstChar %d /LastChar %d /Widths [%s]%s "
            b"/FontDescriptor 6 0 R >>" % (first, last, w, enc)),
        (6, b"<< /Type /FontDescriptor /FontName /BroadsideCff /Flags %d /FontBBox [0 0 700 850] /ItalicAngle 0 "
            b"/Ascent 800 /Descent -200 /CapHeight 700 /StemV 200 /FontFile3 7 0 R >>" % flags),
        (7, stream(b"/Subtype /" + subtype, font)),
    ], binary=True)


def gen_text_cff_embedded() -> bytes:
    """9.6.5.2, 9.9 (Tables 124, 125): an embedded CFF program (FontFile3 /Type1C) in a symbolic Type 1 font with no
    Encoding, so the program's custom encoding (TN 5176 section 12, with a supplement) selects the glyphs. Shows H I O
    (lines with hints, subroutines, curves), Aacute (endchar seac), the supplement code 0x61 (A) and S (flex, div);
    outlines in minimal_cff()."""
    font, glyphs = minimal_cff()
    widths = {code: advance for _, code, advance in glyphs}
    widths[0x61] = 600
    return cff_file(b"Type1C", font, 4, widths, None, b"HIO\xc1aS")


def gen_text_opentype_cff_embedded() -> bytes:
    """9.6.5.2, 9.9 (Table 124 OpenType, p.370): the same CFF program inside an OpenType font (FontFile3 /OpenType)
    in a nonsymbolic Type 1 font with WinAnsiEncoding: the codes' glyph names are looked up in the CFF charset, not
    the "cmap" table. Shows H I O, Aacute (0xC1), A, S and acute (0xB4)."""
    _, glyphs = minimal_cff()
    winansi = {"H": 0x48, "I": 0x49, "O": 0x4F, "A": 0x41, "acute": 0xB4, "Aacute": 0xC1, "S": 0x53}
    widths = {winansi[name]: advance for name, _, advance in glyphs}
    return cff_file(b"OpenType", minimal_otf_cff(), 32, widths, b"/WinAnsiEncoding", b"HIO\xc1AS\xb4")
# ---------------------------------------------------------------------------
# Type 1 font programs (9.9 Table 125; Adobe Type 1 Font Format, TN 5015, TN 5040)
# ---------------------------------------------------------------------------

T1_OPS = {"hstem": 1, "vstem": 3, "vmoveto": 4, "rlineto": 5, "hlineto": 6, "vlineto": 7, "rrcurveto": 8,
          "closepath": 9, "callsubr": 10, "return": 11, "hsbw": 13, "endchar": 14, "rmoveto": 21, "hmoveto": 22,
          "vhcurveto": 30, "hvcurveto": 31}
T1_ESCAPES = {"dotsection": 0, "vstem3": 1, "hstem3": 2, "seac": 6, "sbw": 7, "div": 12, "callothersubr": 16,
              "pop": 17, "setcurrentpoint": 33}


def t1_num(v: int) -> bytes:
    """Type 1 Font Format 6.2: charstring number encoding."""
    if -107 <= v <= 107:
        return bytes([v + 139])
    if 108 <= v <= 1131:
        v -= 108
        return bytes([(v >> 8) + 247, v & 0xFF])
    if -1131 <= v <= -108:
        v = -v - 108
        return bytes([(v >> 8) + 251, v & 0xFF])
    return b"\xff" + struct.pack(">i", v)


def t1_charstring(program: str) -> bytes:
    """A plaintext charstring from text: integers and command names (6.4, Appendix 2)."""
    out = b""
    for token in program.split():
        if token in T1_OPS:
            out += bytes([T1_OPS[token]])
        elif token in T1_ESCAPES:
            out += bytes([12, T1_ESCAPES[token]])
        else:
            out += t1_num(int(token))
    return out


def t1_encrypt(plain: bytes, r: int) -> bytes:
    """Type 1 Font Format 7.1: c = p ^ (r >> 8); r = ((c + r) * 52845 + 22719) mod 65536."""
    out = bytearray()
    for p in plain:
        c = p ^ (r >> 8)
        r = ((c + r) * 52845 + 22719) & 0xFFFF
        out.append(c)
    return bytes(out)


def t1_decrypt(cipher: bytes, r: int) -> bytes:
    out = bytearray()
    for c in cipher:
        out.append(c ^ (r >> 8))
        r = ((c + r) * 52845 + 22719) & 0xFFFF
    return bytes(out)


def t1_eexec(plain: bytes, label: str) -> bytes:
    """7.2: eexec encryption with four leading bytes chosen so that the first cipher byte is not white space and not all
    of the first four are hexadecimal digits (the binary form a reader can tell from the hexadecimal one)."""
    hexdigits = set(b"0123456789abcdefABCDEF")
    i = 0
    while True:
        cipher = t1_encrypt(fixed_bytes("%s:%d" % (label, i), 4) + plain, 55665)
        if cipher[0] not in b" \t\r\n" and not all(b in hexdigits for b in cipher[:4]):
            return cipher
        i += 1


# The glyphs of BroadsideT1 (glyph space 1000 units per em). Each exercises a part of the charstring language:
# hints, h/v line and curve shortcuts, nested subroutines, flex (Subrs 0-2, 8.3-8.4), hint replacement (OtherSubrs 3,
# 8.2), the 5-byte number form with div, closepath leaving the current point, sbw, and seac (6.4, TN 5015 errata).
T1_SUBRS = [
    "3 0 callothersubr pop pop setcurrentpoint return",   # 0: flex end (8.4)
    "0 1 callothersubr return",                            # 1: flex start
    "0 2 callothersubr return",                            # 2: flex point
    "return",                                              # 3: hint replacement fallback
    "0 0 rmoveto 6 callsubr closepath return",             # 4: the I stem, through subr 6
    "0 100 hstem 600 100 hstem 0 100 vstem return",        # 5: replacement hints for E
    "200 hlineto 700 vlineto -200 hlineto return",         # 6: nested in 4
]
T1_GLYPHS = [
    (".notdef", "0 500 hsbw endchar"),
    ("H", "100 800 hsbw 0 200 vstem 400 200 vstem 0 0 rmoveto 200 hlineto 300 vlineto 200 hlineto -300 vlineto "
          "200 hlineto 700 vlineto -200 hlineto -300 vlineto -200 hlineto 300 vlineto -200 hlineto closepath endchar"),
    ("I", "100 400 hsbw 4 callsubr endchar"),
    ("O", "50 800 hsbw 0 350 rmoveto -193 157 -157 193 vhcurveto 193 157 157 193 hvcurveto "
          "0 193 -157 157 -193 0 rrcurveto -193 -157 -157 -193 hvcurveto closepath endchar"),
    ("F", "100 300 hsbw 0 -10 rmoveto 1 callsubr 50 0 rmoveto 2 callsubr -35 0 rmoveto 2 callsubr "
          "10 10 rmoveto 2 callsubr 25 0 rmoveto 2 callsubr 25 0 rmoveto 2 callsubr 10 -10 rmoveto 2 callsubr "
          "15 0 rmoveto 2 callsubr 50 200 -10 0 callsubr 100 vlineto -100 hlineto closepath endchar"),
    ("E", "100 600 hsbw 0 100 hstem 0 0 rmoveto 5 1 3 callothersubr pop callsubr 400 hlineto 100 vlineto "
          "-300 hlineto 500 vlineto 300 hlineto 100 vlineto -400 hlineto closepath dotsection endchar"),
    ("T", "50 500000 1000 div hsbw 0 600 rmoveto 400 hlineto 100 vlineto -400 hlineto closepath "
          "150 -700 rmoveto 100 hlineto 600 vlineto -100 hlineto closepath endchar"),
    ("A", "20 740 hsbw 0 0 rmoveto 200 hlineto 150 500 rlineto 150 -500 rlineto 200 hlineto -250 700 rlineto "
          "-200 hlineto closepath endchar"),
    ("acute", "200 0 400 0 sbw 0 600 rmoveto 100 hlineto 100 100 rlineto -100 hlineto closepath endchar"),
    ("Aacute", "20 740 hsbw 200 150 150 65 194 seac"),
]
# Codes of the built-in encoding; 49 ('1') and 193 map glyphs StandardEncoding would not.
T1_ENCODING = [(49, "H"), (65, "A"), (69, "E"), (70, "F"), (72, "H"), (73, "I"), (79, "O"), (84, "T"),
               (193, "Aacute"), (194, "acute")]
T1_TEXT = b"1HIOFETA\xc1"


def minimal_type1() -> tuple[bytes, bytes, bytes]:
    """A synthesized Type 1 program (no licence): clear text, eexec-encrypted portion, fixed portion (T1 2.2-2.6).
    Entries alternate between the RD/ND/NP and -|/|-/| procedure names (2.5)."""
    encoding = b"".join(b"dup %d /%s put\n" % (code, name.encode()) for code, name in T1_ENCODING)
    clear = (b"%!PS-AdobeFont-1.0: BroadsideT1 001.000\n"
             b"%%Title: BroadsideT1\n"
             b"11 dict begin\n"
             b"/FontInfo 2 dict dup begin\n"
             b"/FullName (Broadside Type 1 \\(synthesized\\)) readonly def\n"
             b"/Notice (Synthesized for the Broadside corpus) readonly def\n"
             b"end readonly def\n"
             b"/FontName /BroadsideT1 def\n"
             b"/PaintType 0 def\n"
             b"/FontType 1 def\n"
             b"/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n"
             b"/FontBBox {0 -10 750 850} readonly def\n"
             b"/Encoding 256 array\n"
             b"0 1 255 {1 index exch /.notdef put} for\n" + encoding +
             b"readonly def\n"
             b"currentfile eexec\n")

    def entry(cs: str) -> bytes:
        return t1_encrypt(b"\x00\x00\x00\x00" + t1_charstring(cs), 4330)

    private = (b"dup /Private 10 dict dup begin\n"
               b"/RD{string currentfile exch readstring pop}executeonly def\n"
               b"/ND{noaccess def}executeonly def\n"
               b"/NP{noaccess put}executeonly def\n"
               b"/-|{string currentfile exch readstring pop}executeonly def\n"
               b"/|-{noaccess def}executeonly def\n"
               b"/|{noaccess put}executeonly def\n"
               b"/BlueValues [-10 0 700 710] def\n"
               b"/MinFeature{16 16}def\n"
               b"/password 5839 def\n"
               b"/lenIV 4 def\n"
               b"/OtherSubrs [{} {} {} {systemdict /internaldict known not {pop 3} {1183615869 systemdict "
               b"/internaldict get exec dup /startlock known {/startlock get exec} {dup /strtlck known "
               b"{/strtlck get exec} {pop 3} ifelse} ifelse} ifelse} executeonly] noaccess def\n"
               b"/Subrs %d array\n" % len(T1_SUBRS))
    for i, cs in enumerate(T1_SUBRS):
        data = entry(cs)
        rd, np = (b"RD", b"NP") if i % 2 == 0 else (b"-|", b"|")
        private += b"dup %d %d %s %s %s\n" % (i, len(data), rd, data, np)
    private += b"ND\n2 index /CharStrings %d dict dup begin\n" % len(T1_GLYPHS)
    for i, (name, cs) in enumerate(T1_GLYPHS):
        data = entry(cs)
        rd, nd = (b"RD", b"ND") if i % 2 == 0 else (b"-|", b"|-")
        private += b"/%s %d %s %s %s\n" % (name.encode(), len(data), rd, data, nd)
    private += b"end\nend\nreadonly put\nnoaccess put\ndup/FontName get exch definefont pop\nmark currentfile closefile\n"
    fixed = (b"0" * 64 + b"\n") * 8 + b"cleartomark\n"
    return clear, t1_eexec(private, "type1-eexec"), fixed


def type1_file(program: bytes, lengths: tuple[int, int, int]) -> bytes:
    """A page showing every glyph of BroadsideT1, embedded with FontFile and no /Encoding, so the program's built-in
    encoding applies (9.6.2.1 Table 109, 9.6.5.2, 9.9 Table 125)."""
    first, last = min(T1_TEXT), max(T1_TEXT)
    width_of = {"H": 800, "I": 400, "O": 800, "F": 300, "E": 600, "T": 500, "A": 740, "Aacute": 740, "acute": 400}
    names = dict(T1_ENCODING)
    w = b" ".join(b"%d" % width_of.get(names.get(c, ""), 0) for c in range(first, last + 1))
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, b"<< /Type /Page /Parent 2 0 R /MediaBox %s /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>" % LETTER),
        (4, stream(b"", codes_content([("F1", T1_TEXT)]))),
        (5, b"<< /Type /Font /Subtype /Type1 /BaseFont /BroadsideT1 /FirstChar %d /LastChar %d /Widths [%s] "
            b"/FontDescriptor 6 0 R >>" % (first, last, w)),
        (6, b"<< /Type /FontDescriptor /FontName /BroadsideT1 /Flags 32 /FontBBox [0 -10 750 850] /ItalicAngle 0 "
            b"/Ascent 850 /Descent -10 /CapHeight 700 /StemV 100 /FontFile 7 0 R >>"),
        (7, stream(b"/Length1 %d /Length2 %d /Length3 %d" % lengths, program)),
    ], binary=True)


def gen_text_type1_embedded() -> bytes:
    """9.9 Table 125: an embedded Type 1 program in the PDF layout (clear text, binary eexec portion, 512 zeros and
    cleartomark), its lengths exact; the font dictionary has no Encoding (9.6.5.2: the program's applies)."""
    clear, cipher, fixed = minimal_type1()
    return type1_file(clear + cipher + fixed, (len(clear), len(cipher), len(fixed)))


def pfb_segment(kind: int, data: bytes = b"") -> bytes:
    """TN 5040 3.3 Table 1: 128, type (1 ASCII, 2 binary, 3 end of file), 4-byte little-endian length, data."""
    return bytes([128, kind]) + (struct.pack("<I", len(data)) + data if kind != 3 else b"")


def gen_text_type1_pfb() -> bytes:
    """A whole PFB file (TN 5040) as the FontFile, the binary portion split over two segments; ISO 32000-2 9.9 wants the
    unwrapped program, so a reader strips the segment headers with a diagnostic. Lengths are the unwrapped portions."""
    clear, cipher, fixed = minimal_type1()
    half = len(cipher) // 2
    pfb = (pfb_segment(1, clear) + pfb_segment(2, cipher[:half]) + pfb_segment(2, cipher[half:]) +
           pfb_segment(1, fixed) + pfb_segment(3))
    return type1_file(pfb, (len(clear), len(cipher), len(fixed)))


def gen_text_type1_hex_eexec() -> bytes:
    """A PFA-style program: the eexec portion in hexadecimal, 64 digits per line (Type 1 Font Format 7.2); ISO 32000-2
    9.9 allows only the binary form. Length2 counts the hexadecimal text."""
    clear, cipher, fixed = minimal_type1()
    digits = cipher.hex().encode()
    hex_text = b"".join(digits[i:i + 64] + b"\n" for i in range(0, len(digits), 64))
    return type1_file(clear + hex_text + fixed, (len(clear), len(hex_text), len(fixed)))


def gen_text_type1_bad_lengths() -> bytes:
    """Length1 five bytes short of the clear text (pdf.js issue5686), Length2 larger than the stream (issue3928 has
    such values), Length3 0 and no fixed portion."""
    clear, cipher, _ = minimal_type1()
    return type1_file(clear + cipher, (len(clear) - 5, 99999999999, 0))
# Composite fonts (clause 9.7): Type 0 font over a CIDFontType2 descendant
# ---------------------------------------------------------------------------

def cmap_stream(name: bytes, body: bytes, cmap_type: int = 1, extra: bytes = b"", wmode: int | None = None,
                registry: bytes = b"Adobe", ordering: bytes = b"Identity", supplement: int = 0) -> bytes:
    """A CMap file (Adobe TN 5014 §7, TN 5099 §1.3) as a stream (9.7.5.3 Table 118); ``body`` holds the range blocks."""
    ros = b"<< /Registry (%s) /Ordering (%s) /Supplement %d >>" % (registry, ordering, supplement)
    text = (b"%!PS-Adobe-3.0 Resource-CMap\n/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n"
            b"/CIDSystemInfo 3 dict dup begin\n  /Registry (" + registry + b") def\n  /Ordering (" + ordering
            + b") def\n  /Supplement %d def\nend def\n/CMapName /%s def\n/CMapType %d def\n" % (supplement, name, cmap_type))
    if wmode is not None:
        text += b"/WMode %d def\n" % wmode
    text += body + b"endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n"
    entries = b"/Type /CMap /CMapName /%s /CIDSystemInfo %s" % (name, ros)
    if wmode is not None:
        entries += b" /WMode %d" % wmode
    return stream(entries + extra, text)


def to_unicode(codespace: bytes, mappings: bytes) -> bytes:
    """9.10.3 ToUnicode CMap: a codespace block and bfchar/bfrange blocks (TN 5099 §1.4)."""
    return cmap_stream(b"Adobe-Identity-UCS", codespace + mappings, cmap_type=2, ordering=b"UCS")


def cid_file(encoding: bytes, cid_entries: bytes, codes: list[bytes], tounicode: bytes,
             extra: list[tuple[int, bytes]] | None = None, origin: bytes = b"72 700") -> bytes:
    """A page showing each of ``codes`` (one Tj each) in a Type 0 font (9.7.6.1 Table 119) whose descendant is an
    embedded CIDFontType2 (9.7.4.1 Table 115) over ``minimal_truetype()``: GID 1 'H' 800, GID 2 'I' 400, upem 1000."""
    font, _ = minimal_truetype()
    shows = b" ".join((b"<%s> Tj" % c.hex().upper().encode()) if c[:1] != b"(" else c + b" Tj" for c in codes)
    content = b"BT /F1 24 Tf " + origin + b" Td " + shows + b" ET"
    objects = [
        (1, catalog()),
        (2, pages()),
        (3, b"<< /Type /Page /Parent 2 0 R /MediaBox %s /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>" % LETTER),
        (4, stream(b"", content)),
        (5, b"<< /Type /Font /Subtype /Type0 /BaseFont /BroadsideMinimal /Encoding %s /DescendantFonts [8 0 R] "
            b"/ToUnicode 9 0 R >>" % encoding),
        (6, b"<< /Type /FontDescriptor /FontName /BroadsideMinimal /Flags 4 /FontBBox [0 0 700 700] /ItalicAngle 0 "
            b"/Ascent 800 /Descent -200 /CapHeight 700 /StemV 200 /FontFile2 7 0 R >>"),
        (7, stream(b"/Length1 %d" % len(font), font)),
        (8, b"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /BroadsideMinimal "
            b"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor 6 0 R %s >>"
            % cid_entries),
        (9, tounicode),
    ]
    return simple_file(objects + (extra or []), binary=True)


def gen_text_cid_identity_h() -> bytes:
    """9.7.4.1 Table 115, 9.7.4.3, 9.7.5.2, 9.7.6: Identity-H over CIDFontType2 with CIDToGIDMap /Identity; W in both
    forms (1 [800] and 2 2 400) and DW 600. Codes 0001 0003 0002: CID 3 is past the program's 3 glyphs, so it shows
    the CID 0 glyph with the DW width."""
    tu = to_unicode(b"1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n",
                    b"2 beginbfchar\n<0001> <0048>\n<0003> <0020>\nendbfchar\n"
                    b"1 beginbfrange\n<0002> <0002> <0049>\nendbfrange\n")
    return cid_file(b"/Identity-H", b"/CIDToGIDMap /Identity /DW 600 /W [1 [800] 2 2 400]",
                    [b"\x00\x01\x00\x03\x00\x02"], tu)


def gen_text_cid_identity_v() -> bytes:
    """9.7.4.1 Table 115 (CIDToGIDMap stream, DW2, W2), 9.7.4.3 (W2 in both forms, DW2 fallback, v.x = w0/2), 9.7.5.2
    Identity-V. The map covers CIDs 0 to 0x22 (0x21 -> 1, 0x22 -> 2); CID 0x23 is past its end and shows CID 0's glyph."""
    gid_map = bytearray(2 * 0x23)
    gid_map[2 * 0x21:2 * 0x21 + 2] = b"\x00\x01"
    gid_map[2 * 0x22:2 * 0x22 + 2] = b"\x00\x02"
    tu = to_unicode(b"1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n",
                    b"3 beginbfchar\n<0021> <0048>\n<0022> <0049>\n<0023> <0020>\nendbfchar\n")
    return cid_file(b"/Identity-V", b"/CIDToGIDMap 10 0 R /DW2 [900 -1100] /W2 [33 [-1200 400 880] 34 34 -900 200 880]",
                    [b"\x00\x21\x00\x22\x00\x23"], tu, [(10, stream(b"", bytes(gid_map)))], origin=b"300 700")


def gen_text_cid_embedded_cmap() -> bytes:
    """9.7.5.3 Table 118, 9.7.5.4, 9.7.6.2, 9.7.6.3, Adobe TN 5014 §5.4 and §7: an embedded CMap stream whose UseCMap is
    another embedded CMap stream (in-file usecmap names it too). The parent has a mixed codespace (1-byte <00>-<7F>,
    2-byte <8140>-<9FFC>), a cidrange and a cidchar; the child adds one cidchar. <8210> is invalid by the per-byte
    rule (its second byte is below 0x40) and consumes two bytes, showing CID 0."""
    parent = cmap_stream(b"Broadside-Parent-H",
                         b"2 begincodespacerange\n<00> <7F>\n<8140> <9FFC>\nendcodespacerange\n"
                         b"1 begincidrange\n<48> <49> 1\nendcidrange\n"
                         b"1 begincidchar\n<8140> 1\nendcidchar\n")
    child = cmap_stream(b"Broadside-Child-H", b"/Broadside-Parent-H usecmap\n1 begincidchar\n<8141> 2\nendcidchar\n",
                        extra=b" /UseCMap 11 0 R")
    tu = to_unicode(b"2 begincodespacerange\n<00> <7F>\n<8140> <9FFC>\nendcodespacerange\n",
                    b"4 beginbfchar\n<48> <0048>\n<49> <0049>\n<8140> <0048>\n<8141> <0049>\nendbfchar\n")
    return cid_file(b"10 0 R", b"/CIDToGIDMap /Identity /W [1 [800 400]]",
                    [b"(HI)", b"\x81\x41\x81\x40", b"\x82\x10"], tu, [(10, child), (11, parent)])


def gen_xref_stream() -> bytes:
    f = File("1.5", binary=True)
    f.add(1, catalog())
    f.add(2, pages())
    f.add(3, page())
    start = f.pos()
    rows = xref_stream_rows([(0, 0, 255), (1, f.offsets[1], 0), (1, f.offsets[2], 0), (1, f.offsets[3], 0), (1, start, 0)])
    f.add(4, stream(b"/Type /XRef /Size 5 /W [1 2 1] /Root 1 0 R", rows))
    f.raw(b"startxref\n%d\n%%%%EOF\n" % start)
    return bytes(f.buf)


def object_stream(members: list[tuple[int, bytes]]) -> bytes:
    """7.5.7 object stream body: header of 'num offset' pairs then the objects."""
    hdr = b""
    data = b""
    for num, body in members:
        hdr += b"%d %d " % (num, len(data))
        data += body + b"\n"
    return stream(b"/Type /ObjStm /N %d /First %d" % (len(members), len(hdr)), hdr + data)


def gen_object_stream() -> bytes:
    f = File("1.5", binary=True)
    f.add(4, object_stream([(1, catalog()), (2, pages()), (3, page())]))
    start = f.pos()
    rows = xref_stream_rows([(0, 0, 255), (2, 4, 0), (2, 4, 1), (2, 4, 2), (1, f.offsets[4], 0), (1, start, 0)])
    f.add(5, stream(b"/Type /XRef /Size 6 /W [1 2 1] /Root 1 0 R", rows))
    f.raw(b"startxref\n%d\n%%%%EOF\n" % start)
    return bytes(f.buf)


def gen_incremental_update() -> bytes:
    base = gen_empty_page()
    prev = int(base[base.rindex(b"startxref\n") + 10:].split()[0])
    out = bytearray(base)
    off3 = len(out)
    out += obj(3, page(mediabox=A4))
    start = len(out)
    out += b"xref\n3 1\n%010d 00000 n \n" % off3
    out += b"trailer\n<< /Size 4 /Root 1 0 R /Prev %d >>\n" % prev
    out += b"startxref\n%d\n%%%%EOF\n" % start
    return bytes(out)


def gen_hybrid_xref() -> bytes:
    """7.5.8.4: objects 1-3 in the classic table; the /Info dictionary (4) lives in
    an object stream (5) that only the cross-reference stream (6) knows about.
    The main table lists 4-6 as free, as in the example in 7.5.8.4. The main
    section and the update section each end with startxref and %%EOF (7.5.6)."""
    f = File("1.5", binary=True)
    f.add(1, catalog())
    f.add(2, pages())
    f.add(3, page())
    main = f.pos()
    f.raw(xref_table(f.offsets, 7, b"<< /Size 7 /Root 1 0 R >>"))
    # 7.5.6: each trailer is terminated by its own %%EOF; the example in 7.5.8.4 ends the main section so too.
    f.raw(b"startxref\n%d\n%%%%EOF\n" % main)
    f.add(5, object_stream([(4, b"<< /Title (hybrid) >>")]))
    xs = f.pos()
    rows = xref_stream_rows([(2, 5, 0), (1, f.offsets[5], 0), (1, xs, 0)])
    f.add(6, stream(b"/Type /XRef /Size 7 /Index [4 3] /W [1 2 1]", rows))
    start = f.pos()
    f.raw(b"xref\n0 0\ntrailer\n<< /Size 7 /Root 1 0 R /Info 4 0 R /Prev %d /XRefStm %d >>\n" % (main, xs))
    f.raw(b"startxref\n%d\n%%%%EOF\n" % start)
    return bytes(f.buf)


def gen_flate_stream() -> bytes:
    return text_file(stream(b"/Filter /FlateDecode", flate(text_content(b"FlateDecode"))), binary=True)


def gen_lzw_stream() -> bytes:
    return text_file(stream(b"/Filter /LZWDecode", lzw_encode(text_content(b"LZWDecode"))), binary=True)


def gen_ascii85_stream() -> bytes:
    return text_file(stream(b"/Filter /ASCII85Decode", ascii85_encode(text_content(b"ASCII85Decode"))))


def gen_asciihex_stream() -> bytes:
    return text_file(stream(b"/Filter /ASCIIHexDecode", asciihex_encode(text_content(b"ASCIIHexDecode"))))


def gen_runlength_stream() -> bytes:
    content = text_content(b"RunLengthDecode") + b" " * 10  # a run so the filter has something to do
    return text_file(stream(b"/Filter /RunLengthDecode", runlength_encode(content)))


def gen_filter_chain() -> bytes:
    data = ascii85_encode(flate(text_content(b"ASCII85 then Flate")))
    return text_file(stream(b"/Filter [/ASCII85Decode /FlateDecode]", data))


def gen_png_predictor() -> bytes:
    f = File("1.5", binary=True)
    f.add(1, catalog())
    f.add(2, pages())
    f.add(3, page())
    start = f.pos()
    rows = [struct.pack(">BHB", *e) for e in
            [(0, 0, 255), (1, f.offsets[1], 0), (1, f.offsets[2], 0), (1, f.offsets[3], 0), (1, start, 0)]]
    data = flate(png_up_predict(rows))
    f.add(4, stream(b"/Type /XRef /Size 5 /W [1 2 1] /Root 1 0 R /Filter /FlateDecode "
                    b"/DecodeParms << /Predictor 12 /Columns 4 >>", data))
    f.raw(b"startxref\n%d\n%%%%EOF\n" % start)
    return bytes(f.buf)


PERMISSIONS = -4  # all permission bits set, reserved bits 1-2 clear (Table 22)
OWNER_PASSWORD = b"owner"
USER_PASSWORD = b""
USER_PASSWORD_R6 = "p\u00e4sswort".encode("utf-8")  # SASLprep leaves it unchanged (NFKC, nothing to map)
USER_PASSWORD_LEGACY = b"caf\xe9"  # "cafe" with e-acute, PDFDocEncoding


def encrypted_file(name: str, version: str, label: bytes, encrypt_dict, encrypt_stream) -> bytes:
    """Shared layout: text page whose content stream (object 4) is encrypted."""
    id0 = file_id(name)
    f = File(version, binary=True)
    f.add(1, catalog())
    f.add(2, pages())
    f.add(3, page(contents=4, font=5))
    f.add(4, stream(b"", encrypt_stream(4, 0, text_content(label))))
    f.add(5, HELVETICA)
    f.add(6, encrypt_dict)
    return f.finish_classic(b"<< /Size 7 /Root 1 0 R /Encrypt 6 0 R /ID [<%s> <%s>] >>" % ((id0.hex().encode(),) * 2))


def gen_encrypted_legacy(name: str, r: int, label: bytes, n: int | None = None, rehash_first_n: bool = False,
                         omit_length: bool = False) -> bytes:
    """Standard security handler R2-R4. ``omit_length`` drops /Length although the key is longer than the 40-bit default
    (7.6.2 Table 20: "Default value: 40"), the defect of ``encrypted-rc4-length-missing.pdf``."""
    id0 = file_id(name)
    n = n or (5 if r == 2 else 16)
    o = legacy_o_entry(OWNER_PASSWORD, USER_PASSWORD, r, n, rehash_first_n)
    key = legacy_file_key(USER_PASSWORD, o, PERMISSIONS, id0, r, n)
    u = legacy_u_entry(key, id0, r)
    aes = r == 4
    common = b"<< /Filter /Standard /R %d /Length %d /P %d /O <%s> /U <%s>" % (
        r, n * 8, PERMISSIONS, o.hex().encode(), u.hex().encode())
    if omit_length:
        common = common.replace(b" /Length %d" % (n * 8), b"")
    if r == 2:
        enc = common + b" /V 1 >>"
        version = "1.1"
    elif r == 3:
        enc = common + b" /V 2 >>"
        version = "1.4"
    else:
        enc = common + b" /V 4 /CF << /StdCF << /CFM /AESV2 /AuthEvent /DocOpen /Length 16 >> >> /StmF /StdCF /StrF /StdCF >>"
        version = "1.6"

    def encrypt(num: int, gen: int, data: bytes) -> bytes:
        k = object_key(key, num, gen, aes)
        if aes:
            iv = fixed_bytes("%s:iv:%d" % (name, num), 16)
            return iv + aes_cbc_encrypt(k, iv, data, pad=True)
        return rc4(k, data)

    return encrypted_file(name, version, label, enc, encrypt)


def gen_encrypted_aes_256(name: str = "encrypted-aes-256") -> bytes:
    key = fixed_bytes(name + ":file-key", 32)
    uvs, uks = fixed_bytes(name + ":user-salts", 16)[:8], fixed_bytes(name + ":user-salts", 16)[8:]
    ovs, oks = fixed_bytes(name + ":owner-salts", 16)[:8], fixed_bytes(name + ":owner-salts", 16)[8:]
    # Algorithm 8
    u = hash_2b(USER_PASSWORD + uvs, USER_PASSWORD, b"") + uvs + uks
    ue = aes_cbc_encrypt(hash_2b(USER_PASSWORD + uks, USER_PASSWORD, b""), bytes(16), key, pad=False)
    # Algorithm 9
    o = hash_2b(OWNER_PASSWORD + ovs + u, OWNER_PASSWORD, u) + ovs + oks
    oe = aes_cbc_encrypt(hash_2b(OWNER_PASSWORD + oks + u, OWNER_PASSWORD, u), bytes(16), key, pad=False)
    # Algorithm 10
    perms_block = struct.pack("<q", PERMISSIONS) + b"Tadb" + fixed_bytes(name + ":perms", 4)
    perms = aes_ecb_encrypt_block(key, perms_block)
    enc = (b"<< /Filter /Standard /V 5 /R 6 /Length 256 /P %d /O <%s> /U <%s> /OE <%s> /UE <%s> /Perms <%s> "
           b"/CF << /StdCF << /CFM /AESV3 /AuthEvent /DocOpen /Length 32 >> >> /StmF /StdCF /StrF /StdCF >>" % (
               PERMISSIONS, o.hex().encode(), u.hex().encode(), oe.hex().encode(), ue.hex().encode(),
               perms.hex().encode()))

    def encrypt(num: int, gen: int, data: bytes) -> bytes:  # Algorithm 1.A
        iv = fixed_bytes("%s:iv:%d" % (name, num), 16)
        return iv + aes_cbc_encrypt(key, iv, data, pad=True)

    return encrypted_file(name, "2.0", b"AES-256 (R6)", enc, encrypt)


def r6_entries(name: str, key: bytes, user_pw: bytes, owner_pw: bytes, p: int,
               encrypt_metadata: bool = True) -> bytes:
    """7.6.4.4.7-7.6.4.4.9 Algorithms 8-10: the /O /U /OE /UE /Perms entries of revisions 6 and 7."""
    uvs, uks = fixed_bytes(name + ":user-salts", 16)[:8], fixed_bytes(name + ":user-salts", 16)[8:]
    ovs, oks = fixed_bytes(name + ":owner-salts", 16)[:8], fixed_bytes(name + ":owner-salts", 16)[8:]
    u = hash_2b(user_pw + uvs, user_pw, b"") + uvs + uks
    ue = aes_cbc_encrypt(hash_2b(user_pw + uks, user_pw, b""), bytes(16), key, pad=False)
    o = hash_2b(owner_pw + ovs + u, owner_pw, u) + ovs + oks
    oe = aes_cbc_encrypt(hash_2b(owner_pw + oks + u, owner_pw, u), bytes(16), key, pad=False)
    perms_block = struct.pack("<q", p) + (b"T" if encrypt_metadata else b"F") + b"adb" + fixed_bytes(name + ":perms", 4)
    perms = aes_ecb_encrypt_block(key, perms_block)
    return b"/P %d /O <%s> /U <%s> /OE <%s> /UE <%s> /Perms <%s>" % (
        p, o.hex().encode(), u.hex().encode(), oe.hex().encode(), ue.hex().encode(), perms.hex().encode())


def aes256_encryptor(name: str, key: bytes):
    """7.6.3.3 Algorithm 1.A; ``label`` keeps every IV of the file distinct."""
    def encrypt(num: int, label: str, data: bytes) -> bytes:
        iv = fixed_bytes("%s:iv:%d:%s" % (name, num, label), 16)
        return iv + aes_cbc_encrypt(key, iv, data, pad=True)
    return encrypt


def hex_string(data: bytes) -> bytes:
    return b"<" + data.hex().encode() + b">"


def extensions(level: int, revision: bytes, url: bytes, encrypt, num: int = 1) -> bytes:
    """7.12 developer extensions dictionary under the ISO_ prefix; its strings are encrypted (ISO/TS 32004 4 NOTE)."""
    return (b" /Extensions << /ISO_ [<< /Type /DeveloperExtensions /BaseVersion /2.0 /ExtensionLevel %d"
            b" /ExtensionRevision %s /URL %s >>] >>" % (
                level, hex_string(encrypt(num, "rev", revision)), hex_string(encrypt(num, "url", url))))


def gen_encrypted_aes_gcm(name: str = "encrypted-aes-gcm") -> bytes:
    """ISO/TS 32003: V 6, R 7, crypt filter /StdCF with /CFM /AESV4 (AES-256-GCM: 12-byte IV, ciphertext,
    16-byte tag, no AAD), the revision 6 password algorithms, the 32003 extension declared in the catalog,
    and an encrypted Info string."""
    key = fixed_bytes(name + ":file-key", 32)

    def encrypt(num: int, label: str, data: bytes) -> bytes:
        iv = fixed_bytes("%s:iv:%d:%s" % (name, num, label), 12)  # never reused under the one key
        ciphertext, tag = gcm_encrypt(key, iv, data)
        return iv + ciphertext + tag

    enc = (b"<< /Filter /Standard /V 6 /R 7 /Length 256 " + r6_entries(name, key, USER_PASSWORD, OWNER_PASSWORD, PERMISSIONS)
           + b" /CF << /StdCF << /CFM /AESV4 /AuthEvent /DocOpen /Length 32 >> >> /StmF /StdCF /StrF /StdCF >>")
    id0 = file_id(name)
    f = File("2.0", binary=True)
    f.add(1, catalog(extensions(32003, b":2023", b"https://www.iso.org/standard/45876.html", encrypt)))
    f.add(2, pages())
    f.add(3, page(contents=4, font=5))
    f.add(4, stream(b"", encrypt(4, "stream", text_content(b"AES-256-GCM (R7)"))))
    f.add(5, HELVETICA)
    f.add(6, enc)
    f.add(7, b"<< /Title %s >>" % hex_string(encrypt(7, "title", b"AES-GCM")))
    return f.finish_classic(b"<< /Size 8 /Root 1 0 R /Info 7 0 R /Encrypt 6 0 R /ID [<%s> <%s>] >>" % ((id0.hex().encode(),) * 2))


def gen_encrypted_user_password(name: str = "encrypted-user-password") -> bytes:
    """R6 (V 5, AES-256) with a non-ASCII user password, prepared with SASLprep and UTF-8 (7.6.4.1), and
    restricted user permissions: P -3372 grants printing (bit 3) and copying (bit 5) only (Table 22)."""
    key = fixed_bytes(name + ":file-key", 32)
    encrypt = aes256_encryptor(name, key)
    enc = (b"<< /Filter /Standard /V 5 /R 6 /Length 256 " + r6_entries(name, key, USER_PASSWORD_R6, OWNER_PASSWORD, -3372)
           + b" /CF << /StdCF << /CFM /AESV3 /AuthEvent /DocOpen /Length 32 >> >> /StmF /StdCF /StrF /StdCF >>")
    id0 = file_id(name)
    f = File("2.0", binary=True)
    f.add(1, catalog())
    f.add(2, pages())
    f.add(3, page(contents=4, font=5))
    f.add(4, stream(b"", encrypt(4, "stream", text_content(b"User password (R6)"))))
    f.add(5, HELVETICA)
    f.add(6, enc)
    f.add(7, b"<< /Title %s >>" % hex_string(encrypt(7, "title", b"Protected")))
    return f.finish_classic(b"<< /Size 8 /Root 1 0 R /Info 7 0 R /Encrypt 6 0 R /ID [<%s> <%s>] >>" % ((id0.hex().encode(),) * 2))


def gen_encrypted_empty_owner_password(name: str = "encrypted-empty-owner-password") -> bytes:
    """R6 (V 5, AES-256) whose owner password is empty and whose user password is ``user`` (7.6.4.4.7-7.6.4.4.9
    Algorithms 8-10 with an empty owner password string): opening without a password authenticates as the owner
    (7.6.4.3.3 Algorithm 2.A tries the owner password first). The case of pdf.js's pr6531_2.pdf."""
    key = fixed_bytes(name + ":file-key", 32)
    encrypt = aes256_encryptor(name, key)
    enc = (b"<< /Filter /Standard /V 5 /R 6 /Length 256 " + r6_entries(name, key, b"user", b"", PERMISSIONS)
           + b" /CF << /StdCF << /CFM /AESV3 /AuthEvent /DocOpen /Length 32 >> >> /StmF /StdCF /StrF /StdCF >>")
    id0 = file_id(name)
    f = File("2.0", binary=True)
    f.add(1, catalog())
    f.add(2, pages())
    f.add(3, page(contents=4, font=5))
    f.add(4, stream(b"", encrypt(4, "stream", text_content(b"Empty owner password (R6)"))))
    f.add(5, HELVETICA)
    f.add(6, enc)
    return f.finish_classic(b"<< /Size 7 /Root 1 0 R /Encrypt 6 0 R /ID [<%s> <%s>] >>" % ((id0.hex().encode(),) * 2))


def gen_encrypted_rc4_user_password(name: str = "encrypted-rc4-user-password") -> bytes:
    """R3 (V 2, 128-bit RC4) with the user password ``caf\xe9`` in PDFDocEncoding (7.6.4.3.2 step a) and an
    encrypted Info string: Algorithms 2, 5, 6 and 7 with a non-empty user password."""
    id0 = file_id(name)
    n = 16
    o = legacy_o_entry(OWNER_PASSWORD, USER_PASSWORD_LEGACY, 3, n)
    key = legacy_file_key(USER_PASSWORD_LEGACY, o, PERMISSIONS, id0, 3, n)
    u = legacy_u_entry(key, id0, 3)
    f = File("1.4", binary=True)
    f.add(1, catalog())
    f.add(2, pages())
    f.add(3, page(contents=4, font=5))
    f.add(4, stream(b"", rc4(object_key(key, 4, 0, False), text_content(b"User password (R3)"))))
    f.add(5, HELVETICA)
    f.add(6, b"<< /Filter /Standard /V 2 /R 3 /Length 128 /P %d /O <%s> /U <%s> >>" % (
        PERMISSIONS, o.hex().encode(), u.hex().encode()))
    f.add(7, b"<< /Title %s >>" % hex_string(rc4(object_key(key, 7, 0, False), b"Protected")))
    return f.finish_classic(b"<< /Size 8 /Root 1 0 R /Info 7 0 R /Encrypt 6 0 R /ID [<%s> <%s>] >>" % ((id0.hex().encode(),) * 2))


def gen_encrypted_crypt_filters(name: str = "encrypted-crypt-filters") -> bytes:
    """R4 (V 4) crypt filters (7.6.6, 7.4.10): StmF and StrF /StdCF (AESV2), /EncryptMetadata false (the
    metadata stream is plaintext and Algorithm 2 step f appends FF FF FF FF), a content stream with
    /Filter /Crypt /Name /Identity left unencrypted, one with /Name /StdCF (Algorithm 1 object key, as
    qpdf and poppler read it), and an encrypted Info string."""
    id0 = file_id(name)
    n = 16
    o = legacy_o_entry(OWNER_PASSWORD, USER_PASSWORD, 4, n)
    key = legacy_file_key(USER_PASSWORD, o, PERMISSIONS, id0, 4, n, encrypt_metadata=False)
    u = legacy_u_entry(key, id0, 4)

    def encrypt(num: int, label: str, data: bytes) -> bytes:
        iv = fixed_bytes("%s:iv:%d:%s" % (name, num, label), 16)
        return iv + aes_cbc_encrypt(object_key(key, num, 0, True), iv, data, pad=True)

    def text(y: int, label: bytes) -> bytes:
        return b"BT /F1 24 Tf 72 %d Td (%s) Tj ET" % (y, label)

    f = File("1.6", binary=True)
    f.add(1, catalog(b" /Metadata 8 0 R"))
    f.add(2, pages())
    f.add(3, page(contents=None, font=5, extra=b" /Contents [4 0 R 7 0 R 10 0 R]"))
    f.add(4, stream(b"", encrypt(4, "stream", text(700, b"StmF StdCF"))))
    f.add(5, HELVETICA)
    f.add(6, b"<< /Filter /Standard /V 4 /R 4 /Length 128 /P %d /O <%s> /U <%s> /EncryptMetadata false"
             b" /CF << /StdCF << /CFM /AESV2 /AuthEvent /DocOpen /Length 16 >> >> /StmF /StdCF /StrF /StdCF >>" % (
                 PERMISSIONS, o.hex().encode(), u.hex().encode()))
    f.add(7, stream(b"/Filter /Crypt /DecodeParms << /Type /CryptFilterDecodeParms /Name /Identity >>", text(660, b"Identity crypt filter")))
    f.add(8, stream(b"/Type /Metadata /Subtype /XML", XMP))
    f.add(9, b"<< /Title %s >>" % hex_string(encrypt(9, "title", b"Crypt filters")))
    f.add(10, stream(b"/Filter /Crypt /DecodeParms << /Type /CryptFilterDecodeParms /Name /StdCF >>", encrypt(10, "stream", text(620, b"StdCF crypt filter"))))
    return f.finish_classic(b"<< /Size 11 /Root 1 0 R /Info 9 0 R /Encrypt 6 0 R /ID [<%s> <%s>] >>" % ((id0.hex().encode(),) * 2))


MAC_TAMPER_TARGET = b"% Integrity check target: original\n"


def gen_encrypted_mac(name: str = "encrypted-mac") -> bytes:
    """ISO/TS 32004: R6 (V 5) with /KDFSalt, P -4100 (bit 13 clear: a MAC token is required) and a
    standalone PDF MAC token in the trailer's /AuthCode, its /ByteRange covering the whole file except
    the token, the 32004 extension declared in the catalog."""
    key = fixed_bytes(name + ":file-key", 32)
    kdf_salt = fixed_bytes(name + ":kdf-salt", 32)
    mac_key = fixed_bytes(name + ":mac-key", 32)
    encrypt = aes256_encryptor(name, key)
    p = -4100
    enc = (b"<< /Filter /Standard /V 5 /R 6 /Length 256 " + r6_entries(name, key, USER_PASSWORD, OWNER_PASSWORD, p)
           + b" /KDFSalt <%s> /CF << /StdCF << /CFM /AESV3 /AuthEvent /DocOpen /Length 32 >> >>"
             b" /StmF /StdCF /StrF /StdCF >>" % kdf_salt.hex().encode())
    id0 = file_id(name)
    f = File("2.0", binary=True)
    f.add(1, catalog(extensions(32004, b":2024", b"https://www.iso.org/standard/45877.html", encrypt)))
    f.add(2, pages())
    f.add(3, page(contents=4, font=5))
    f.add(4, stream(b"", encrypt(4, "stream", text_content(b"Integrity MAC (R6)"))))
    f.add(5, HELVETICA)
    f.add(6, enc)
    f.raw(MAC_TAMPER_TARGET)
    token_length = len(pdf_mac_token(key, kdf_salt, mac_key, bytes(32)))
    placeholder = b"<" + b"0" * (2 * token_length) + b">"
    range_placeholder = b"[0 0000000000 0000000000 0000000000]"
    data = bytearray(f.finish_classic(
        b"<< /Size 7 /Root 1 0 R /Encrypt 6 0 R /ID [<%s> <%s>] /AuthCode << /MACLocation /Standalone"
        b" /ByteRange %s /MAC %s >> >>" % (id0.hex().encode(), id0.hex().encode(), range_placeholder, placeholder)))
    l1 = data.index(placeholder)
    start = l1 + len(placeholder)
    byte_range = b"[0 %010d %010d %010d]" % (l1, start, len(data) - start)
    r = data.index(range_placeholder)
    data[r:r + len(range_placeholder)] = byte_range
    digest = hashlib.sha256(bytes(data[:l1]) + bytes(data[start:])).digest()
    token = pdf_mac_token(key, kdf_salt, mac_key, digest)
    data[l1:start] = b"<" + token.hex().upper().encode() + b">"
    return bytes(data)


def gen_encrypted_mac_tampered() -> bytes:
    """encrypted-mac.pdf with one byte of a comment changed after the MAC was computed."""
    data = gen_encrypted_mac()
    return data.replace(MAC_TAMPER_TARGET, MAC_TAMPER_TARGET.replace(b"original", b"Original"), 1)


def gen_broken_xref_offsets() -> bytes:
    f = File()
    f.add(1, catalog())
    f.add(2, pages())
    f.add(3, page())
    return f.finish_classic(b"<< /Size 4 /Root 1 0 R >>", delta=10)


def gen_missing_endobj() -> bytes:
    f = File()
    f.add(1, catalog())
    f.add(2, pages())
    f.offsets[3] = f.pos()
    f.raw(b"3 0 obj\n" + page() + b"\n")  # no endobj
    return f.finish_classic(b"<< /Size 4 /Root 1 0 R >>")


def gen_wrong_stream_length() -> bytes:
    content = text_content(b"Length is wrong")
    return text_file(stream(b"", content, length=10))


def gen_no_xref() -> bytes:
    f = File()
    f.add(1, catalog())
    f.add(2, pages())
    f.add(3, page())
    f.raw(b"%%EOF\n")
    return bytes(f.buf)


def gen_startxref_wrong() -> bytes:
    f = File()
    f.add(1, catalog())
    f.add(2, pages())
    f.add(3, page())
    return f.finish_classic(b"<< /Size 4 /Root 1 0 R >>", startxref_delta=100)


def gen_inline_image() -> bytes:
    content = (b"q 100 0 0 100 72 600 cm BI /W 2 /H 2 /CS /G /BPC 8 ID \x00\xff\xff\x00 EI Q")
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4)),
        (4, stream(b"", content)),
    ], binary=True)


def gen_page_tree_inherited() -> bytes:
    return simple_file([
        (1, catalog()),
        (2, pages(kids=[3], count=2)),
        (3, b"<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 5 0 R] /Count 2 /MediaBox " + LETTER +
            b" /Resources << /Font << /F1 8 0 R >> >> >>"),
        (4, page(parent=3, mediabox=None, contents=6, resources=False)),
        (5, page(parent=3, mediabox=None, contents=7, resources=False)),
        (6, stream(b"", text_content(b"Page 1"))),
        (7, stream(b"", text_content(b"Page 2"))),
        (8, HELVETICA),
    ])


def gen_annotations_link() -> bytes:
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(extra=b" /Annots [4 0 R]")),
        (4, b"<< /Type /Annot /Subtype /Link /Rect [72 700 300 724] /Border [0 0 0] "
            b"/A << /S /URI /URI (https://example.com/) >> >>"),
    ])


def annotation(subtype: bytes, rect: bytes, extra: bytes = b"", ap: bool = True) -> bytes:
    """12.5.2 Table 166: an annotation dictionary on page 3 (/P) whose normal appearance is form 4."""
    body = b"<< /Type /Annot /Subtype /" + subtype + b" /Rect " + rect + b" /P 3 0 R"
    if ap:
        body += b" /AP << /N 4 0 R >>"
    return body + extra + b" >>"


def gen_annotations_subtypes() -> bytes:
    """12.5.6 Table 171: one annotation of each of the 28 standard subtypes on one page, plus one of the
    unknown subtype XBroadsideTest with every flag bit of Table 167 set; TrapNet is last (12.5.6.21). Every
    annotation that needs one (12.5.2: all but Popup, Link and a zero-size Projection) shares the normal
    appearance form 4. Relations: Text 10 <-> Popup 11 (Popup/Parent), reply 12 (IRT 10, RT R, State
    Accepted in the Review model); colours C/IC with 1, 3 and 4 components; Border array with a dash, BS
    and BE dictionaries; QuadPoints on Link, text markup and Redact; the Widget is a push button field
    listed in the catalog's AcroForm."""
    def rect(i: int) -> bytes:
        x, y = 40 + (i % 6) * 90, 700 - (i // 6) * 90
        return b"[%d %d %d %d]" % (x, y, x + 60, y + 40)

    objects: list[tuple[int, bytes]] = [
        (1, catalog(b" /AcroForm << /Fields [34 0 R] >>")),
        (2, pages()),
        (3, page(extra=b" /Annots [%s]" % b" ".join(b"%d 0 R" % n for n in [
            10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 30, 32, 33, 34, 35, 36,
            37, 39, 40, 41, 42, 43]))),
        (4, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10]", b"0 g 0 0 10 10 re f")),
        (10, annotation(b"Text", rect(0), b" /Contents (Note text) /NM (note-1) /M (D:20240102030405Z)"
                        b" /F 28 /C [1 1 0] /T (Alice) /Subj (Review) /CreationDate (D:20240101120000Z)"
                        b" /Popup 11 0 R /Name /Comment /Open true")),
        (11, annotation(b"Popup", b"[300 600 450 700]", b" /Parent 10 0 R /Open true", ap=False)),
        (12, annotation(b"Text", rect(2), b" /T (Bob) /IRT 10 0 R /RT /R /State (Accepted) /StateModel (Review)")),
        (13, annotation(b"Link", b"[220 700 280 740]", b" /Dest [3 0 R /Fit] /H /O"
                        b" /QuadPoints [222 702 278 702 278 738 222 738] /BS << /W 2 /S /U >>", ap=False)),
        (14, annotation(b"FreeText", rect(4), b" /DA (/Helv 12 Tf 0 g) /Q 1 /IT /FreeTextCallout"
                        b" /CL [10 10 50 50 60 50] /LE /OpenArrow /DS (font: 12pt Helvetica) /RC (<p>rich</p>)")),
        (15, annotation(b"Line", rect(5), b" /L [100 100 200 200] /LE [/Circle /ClosedArrow] /IC [1 0 0]"
                        b" /LL 10 /LLE 2 /LLO 1 /Cap true /CP /Top /CO [0 5] /IT /LineDimension")),
        (16, annotation(b"Square", rect(6), b" /BS << /Type /Border /W 2 /S /D /D [3 2] >> /IC [0 0 1]"
                        b" /BE << /S /C /I 1 >> /RD [1 2 3 4]")),
        (17, annotation(b"Circle", rect(7), b" /Border [0 0 2 [4 1]] /IC [0.5] /C [0 0 0 1]")),
        (18, annotation(b"Polygon", rect(8), b" /Vertices [10 10 50 10 30 40] /IT /PolygonCloud /BE << /S /C /I 2 >>")),
        (19, annotation(b"PolyLine", rect(9), b" /Path [[10 10] [50 10] [60 20 70 30 80 10]] /LE [/Square /Slash]")),
        (20, annotation(b"Highlight", rect(10), b" /QuadPoints [10 10 50 10 50 20 10 20]")),
        (21, annotation(b"Underline", rect(11), b" /QuadPoints [10 30 50 30 50 40 10 40]")),
        (22, annotation(b"Squiggly", rect(12), b" /QuadPoints [10 50 50 50 50 60 10 60]")),
        (23, annotation(b"StrikeOut", rect(13), b" /QuadPoints [10 70 50 70 50 80 10 80 60 70 90 70 90 80 60 80]")),
        (24, annotation(b"Caret", rect(14), b" /Sy /P /RD [1 1 1 1]")),
        (25, annotation(b"Stamp", rect(15), b" /IT /StampImage")),
        (26, annotation(b"Ink", rect(16), b" /InkList [[10 10 20 20 30 10] [40 40 50 50]]")),
        (27, annotation(b"FileAttachment", rect(17), b" /FS 28 0 R /Name /Paperclip /Contents (An attached file)"
                        b" /AF [28 0 R]")),
        (28, b"<< /Type /Filespec /F (a.txt) /UF (a.txt) /AFRelationship /Data /EF << /F 29 0 R /UF 29 0 R >> >>"),
        (29, stream(b"/Type /EmbeddedFile /Subtype /text#2Fplain", b"attached")),
        (30, annotation(b"Sound", rect(18), b" /Sound 31 0 R /Name /Mic")),
        (31, stream(b"/Type /Sound /R 8000", b"\x00\x40\x80\xc0")),
        (32, annotation(b"Movie", rect(19), b" /T (Clip) /Movie << /F (movie.mp4) >> /A false")),
        (33, annotation(b"Screen", rect(20), b" /T (Screen) /MK << /R 90 /BC [1 0 0] /BG [1] /CA (Play) >>"
                        b" /A << /S /URI /URI (https://example.org/media) >>")),
        (34, annotation(b"Widget", rect(21), b" /FT /Btn /Ff 65536 /T (push) /H /P /MK << /CA (Push) /TP 0 >>")),
        (35, annotation(b"PrinterMark", rect(22), b" /F 68 /MN /ColorBar")),
        (36, annotation(b"Watermark", rect(23), b" /FixedPrint << /Type /FixedPrint /Matrix [1 0 0 1 72 -72] /H 0 /V 1 >>")),
        (37, annotation(b"3D", rect(24), b" /3DD 38 0 R /3DI false /3DB [0 0 60 40]")),
        (38, stream(b"/Type /3D /Subtype /U3D", b"U3D\x00")),
        (39, annotation(b"Redact", rect(25), b" /QuadPoints [10 10 50 10 50 20 10 20] /IC [1 0 0]"
                        b" /OverlayText (X) /Repeat true /DA (/Helv 10 Tf 0 g) /Q 2")),
        (40, annotation(b"Projection", b"[0 0 0 0]", ap=False)),
        (41, annotation(b"RichMedia", rect(27), b" /RichMediaContent << >> /RichMediaSettings << >>")),
        (42, annotation(b"XBroadsideTest", rect(28), b" /F 1023")),
        (43, annotation(b"TrapNet", b"[0 0 612 792]", b" /F 68 /LastModified (D:20240101000000Z) /AS /T1", ap=False)[:-3]
            + b" /AP << /N << /T1 4 0 R >> >> >>"),
    ]
    id0 = file_id("annotations-subtypes").hex().encode()
    return simple_file(objects, version="2.0", trailer_extra=b" /ID [<%s> <%s>]" % (id0, id0))


def gen_annotations_appearance() -> bytes:
    """12.5.5 Algorithm "Appearance streams" and Table 170. Square 10: N stream with BBox [0 0 100 50] and
    Matrix [0 1 -1 0 0 0] on Rect [100 100 150 200] (AA = [0 1 -1 0 150 100]). Circle 11: Rect written
    unnormalized as [50 30 10 10] (7.9.5) with N BBox [0 0 20 20] (AA = [2 0 0 1 10 10]). Square 12: N is a
    state subdictionary (On, Off), D has only On, R is absent, AS /On."""
    objects: list[tuple[int, bytes]] = [
        (1, catalog()),
        (2, pages()),
        (3, page(extra=b" /Annots [10 0 R 11 0 R 12 0 R]")),
        (4, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 100 50] /Matrix [0 1 -1 0 0 0]", b"1 0 0 rg 0 0 100 50 re f")),
        (5, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 20 20]", b"0 0 1 rg 0 0 20 20 re f")),
        (6, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10]", b"0 1 0 rg 0 0 10 10 re f")),
        (7, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10]", b"0.5 g 0 0 10 10 re f")),
        (8, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10]", b"0 g 0 0 10 10 re f")),
        (10, b"<< /Type /Annot /Subtype /Square /Rect [100 100 150 200] /AP << /N 4 0 R >> >>"),
        (11, b"<< /Type /Annot /Subtype /Circle /Rect [50 30 10 10] /AP << /N 5 0 R >> >>"),
        (12, b"<< /Type /Annot /Subtype /Square /Rect [300 300 340 340] /AS /On"
             b" /AP << /N << /On 6 0 R /Off 7 0 R >> /D << /On 8 0 R >> >> >>"),
    ]
    return simple_file(objects)


def gen_annotations_malformed() -> bytes:
    """12.5 real-world deviations, two pages. Page 3's Annots: [null 9 0 R (an integer) 10 10 11 12 13 14 15
    16 << direct >>]: 10 is listed twice; 11 has no Subtype; 12 has a three-number Rect; 13 is a Highlight
    with 7 QuadPoints numbers; 14 has an N state subdictionary but no AS; 15's N stream has no BBox; 16's P
    names page 4, whose Annots lists 16 too. Page 4 also holds 17: a Line with RT but no IRT, Popup -> 18
    (a Popup whose Parent is 19), State without StateModel, F 2048, C with two components, Border [1],
    and a Link 19 with both A and Dest. PDF 1.7, so a missing appearance is not a deviation."""
    objects: list[tuple[int, bytes]] = [
        (1, catalog()),
        (2, pages([3, 4])),
        (3, page(extra=b" /Annots [null 9 0 R 10 0 R 10 0 R 11 0 R 12 0 R 13 0 R 14 0 R 15 0 R 16 0 R"
                       b" << /Type /Annot /Subtype /Square /Rect [0 0 10 10] >>]")),
        (4, page(extra=b" /Annots [16 0 R 17 0 R 18 0 R 19 0 R]")),
        (5, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10]", b"0 g 0 0 10 10 re f")),
        (6, stream(b"/Type /XObject /Subtype /Form", b"0 g 0 0 10 10 re f")),
        (9, b"42"),
        (10, b"<< /Type /Annot /Subtype /Square /Rect [10 10 20 20] /P 3 0 R >>"),
        (11, b"<< /Type /Annot /Rect [30 10 40 20] /P 3 0 R >>"),
        (12, b"<< /Type /Annot /Subtype /Circle /Rect [1 2 3] /P 3 0 R >>"),
        (13, b"<< /Type /Annot /Subtype /Highlight /Rect [50 10 60 20] /P 3 0 R /QuadPoints [1 2 3 4 5 6 7] >>"),
        (14, b"<< /Type /Annot /Subtype /Square /Rect [70 10 80 20] /P 3 0 R /AP << /N << /On 5 0 R >> >> >>"),
        (15, b"<< /Type /Annot /Subtype /Square /Rect [90 10 100 20] /P 3 0 R /AP << /N 6 0 R >> >>"),
        (16, b"<< /Type /Annot /Subtype /Square /Rect [110 10 120 20] /P 4 0 R >>"),
        (17, b"<< /Type /Annot /Subtype /Text /Rect [10 50 20 60] /P 4 0 R /RT /Group /Popup 18 0 R"
             b" /State (Accepted) /F 2048 /C [1 0] /Border [1] >>"),
        (18, b"<< /Type /Annot /Subtype /Popup /Rect [30 50 90 90] /P 4 0 R /Parent 19 0 R >>"),
        (19, b"<< /Type /Annot /Subtype /Link /Rect [10 100 50 120] /P 4 0 R /Dest [4 0 R /Fit]"
             b" /A << /S /URI /URI (https://example.com/) >> >>"),
    ]
    return simple_file(objects)


def gen_outline() -> bytes:
    return simple_file([
        (1, catalog(b" /Outlines 4 0 R /PageMode /UseOutlines")),
        (2, pages()),
        (3, page()),
        (4, b"<< /Type /Outlines /First 5 0 R /Last 6 0 R /Count 2 >>"),
        (5, b"<< /Title (First) /Parent 4 0 R /Next 6 0 R /Dest [3 0 R /Fit] >>"),
        (6, b"<< /Title (Second) /Parent 4 0 R /Prev 5 0 R /Dest [3 0 R /XYZ 0 792 0] >>"),
    ])


def gen_name_tree_dests() -> bytes:
    return simple_file([
        (1, catalog(b" /Names << /Dests 4 0 R >>")),
        (2, pages()),
        (3, page()),
        (4, b"<< /Kids [5 0 R 6 0 R] >>"),
        (5, b"<< /Limits [(alpha) (beta)] /Names [(alpha) [3 0 R /Fit] (beta) [3 0 R /XYZ 0 792 0]] >>"),
        (6, b"<< /Limits [(gamma) (gamma)] /Names [(gamma) [3 0 R /FitH 792]] >>"),
    ])


def tree_key(key: bytes) -> bytes:
    """A name-tree key as a PDF string: literal when printable ASCII, hexadecimal otherwise (7.3.4)."""
    if all(0x20 <= b < 0x7F and b not in b"()\\" for b in key):
        return b"(" + key + b")"
    return b"<" + key.hex().upper().encode() + b">"


def build_tree(objects: list[tuple[int, bytes]], first_num: int, entries: list[tuple[bytes, bytes]],
               fanouts: list[int], entries_key: bytes, key_bytes) -> int:
    """7.9.6/7.9.7: lays sorted (key, value) pairs out as a balanced tree of indirect nodes, appended to
    ``objects`` from ``first_num``. ``fanouts[0]`` pairs per leaf, then kids per node for each level up;
    the root is written last, has only Kids and no Limits. Returns the root's object number."""
    num = first_num
    level = []  # (num, least key, greatest key)
    for i in range(0, len(entries), fanouts[0]):
        chunk = entries[i:i + fanouts[0]]
        pairs = b" ".join(key_bytes(k) + b" " + v for k, v in chunk)
        objects.append((num, b"<< /Limits [%s %s] /%s [%s] >>" % (
            key_bytes(chunk[0][0]), key_bytes(chunk[-1][0]), entries_key, pairs)))
        level.append((num, chunk[0][0], chunk[-1][0]))
        num += 1
    for fanout in fanouts[1:]:
        upper = []
        for i in range(0, len(level), fanout):
            chunk = level[i:i + fanout]
            kids = b" ".join(b"%d 0 R" % n for n, _, _ in chunk)
            objects.append((num, b"<< /Limits [%s %s] /Kids [%s] >>" % (
                key_bytes(chunk[0][1]), key_bytes(chunk[-1][2]), kids)))
            upper.append((num, chunk[0][1], chunk[-1][2]))
            num += 1
        level = upper
    objects.append((num, b"<< /Kids [%s] >>" % b" ".join(b"%d 0 R" % n for n, _, _ in level)))
    return num


def gen_name_tree_deep() -> bytes:
    """7.9.6, Table 36; 12.3.2.4: a Dests name tree four levels deep (root, two intermediate levels,
    leaves) holding 200 keys, sorted byte by byte: a key that is a prefix of another ((a) before (ab)),
    PDFDocEncoding keys whose byte order differs from their text order (<18> breve sorts before (A);
    <80> bullet before <E9> e-acute), and UTF-16BE keys with the BOM (<FEFF0061> also reads as "a").
    Each value is [3 0 R /XYZ 0 i null] where i is the key's position in byte order."""
    keys = [b"a", b"ab", b"\x18", b"\x80", b"\xe9", b"\xfe\xff\x00a", b"\xfe\xff\x00\xe9"]
    keys += [b"n%03d" % i for i in range(1, 194)]
    keys.sort()
    entries = [(k, b"[3 0 R /XYZ 0 %d null]" % i) for i, k in enumerate(keys)]
    objects = [(2, pages()), (3, page())]
    root = build_tree(objects, 4, entries, [10, 4, 3], b"Names", tree_key)
    return simple_file([(1, catalog(b" /Names << /Dests %d 0 R >>" % root))] + objects)


def gen_number_tree_deep() -> bytes:
    """7.9.7, Table 37; 12.4.2: twelve pages whose /PageLabels number tree has three levels (root,
    intermediate nodes, leaves) and nine keys, so a page between two keys takes the nearest lower key."""
    labels = [(0, b"<< /S /r >>"), (1, b"<< /S /D >>"), (2, b"<< /S /D /St 10 >>"), (4, b"<< /P (A-) >>"),
              (5, b"<< /S /A >>"), (7, b"<< /S /a /P (x) >>"), (8, b"<< /S /R /St 4 >>"),
              (10, b"<< /S /D /P (B-) /St 1 >>"), (11, b"<< /S /D >>")]
    objects: list[tuple[int, bytes]] = []
    root = build_tree(objects, 3, [(str(k).encode(), v) for k, v in labels], [3, 2], b"Nums",
                      lambda k: k)
    first_page = root + 1
    page_nums = list(range(first_page, first_page + 12))
    return simple_file([(1, catalog(b" /PageLabels %d 0 R" % root)), (2, pages(page_nums))]
                       + objects + [(n, page()) for n in page_nums])


def gen_name_tree_broken() -> bytes:
    """7.9.6, Table 36, broken six ways: leaf 7's Limits [(a) (c)] do not hold its key (d); leaf 8's
    keys are not sorted ((f) before (e)); intermediate node 6 has no Limits; key (g) is in leaf 8 and
    again in leaf 9; node 6's Kids list the root 4 (a cycle); leaf 9's Names array has an odd length
    (a dangling key (i)). Every key still resolves."""
    def dest(top: int) -> bytes:
        return b"[3 0 R /XYZ 0 %d null]" % top
    return simple_file([
        (1, catalog(b" /Names << /Dests 4 0 R >>")),
        (2, pages()),
        (3, page()),
        (4, b"<< /Kids [5 0 R 6 0 R] >>"),
        (5, b"<< /Limits [(a) (g)] /Kids [7 0 R 8 0 R] >>"),
        (6, b"<< /Kids [9 0 R 4 0 R] >>"),
        (7, b"<< /Limits [(a) (c)] /Names [(a) %s (b) %s (d) %s] >>" % (dest(1), dest(2), dest(4))),
        (8, b"<< /Limits [(e) (g)] /Names [(f) %s (e) %s (g) %s] >>" % (dest(6), dest(5), dest(7))),
        (9, b"<< /Limits [(g) (i)] /Names [(g) %s (h) %s (i)] >>" % (dest(70), dest(8))),
    ])


def gen_destinations_all() -> bytes:
    """12.3.2.2 Table 149 and 12.3.2.4: a Dests name tree with one entry per explicit form (XYZ with
    numbers, with nulls and with zoom 0; Fit; FitH with a number and with null; FitV; FitR; FitB;
    FitBH; FitBV), a value in the dictionary form << /D [...] >> with an extra attribute, and a value
    that is an indirect array; the PDF 1.1 catalog /Dests dictionary keyed by name (/Chap6); and an
    outline whose items refer to a destination by name (/Dest /Chap6) and by string (/Dest (alpha))."""
    names = [
        (b"alpha", b"[3 0 R /XYZ 0 792 null]"),
        (b"fit", b"[3 0 R /Fit]"),
        (b"fitb", b"[3 0 R /FitB]"),
        (b"fitbh", b"[3 0 R /FitBH 700]"),
        (b"fitbv", b"[3 0 R /FitBV 36]"),
        (b"fith", b"[3 0 R /FitH 650]"),
        (b"fith-null", b"[3 0 R /FitH null]"),
        (b"fitr", b"[3 0 R /FitR 10 20 300 400.5]"),
        (b"fitv", b"[3 0 R /FitV 50]"),
        (b"indirect", b"9 0 R"),
        (b"with-d", b"<< /D [3 0 R /Fit] /Note (an additional attribute) >>"),
        (b"xyz", b"[3 0 R /XYZ 72 720 1.5]"),
        (b"xyz-null", b"[3 0 R /XYZ null null null]"),
        (b"xyz-zero", b"[3 0 R /XYZ 10 20 0]"),
    ]
    assert [k for k, _ in names] == sorted(k for k, _ in names)
    pairs = b" ".join(b"(" + k + b") " + v for k, v in names)
    return simple_file([
        (1, catalog(b" /Names << /Dests 4 0 R >> /Dests 5 0 R /Outlines 6 0 R")),
        (2, pages()),
        (3, page()),
        (4, b"<< /Names [" + pairs + b"] >>"),
        (5, b"<< /Chap6 [3 0 R /FitV 72] >>"),
        (6, b"<< /Type /Outlines /First 7 0 R /Last 8 0 R /Count 2 >>"),
        (7, b"<< /Title (Chapter 6) /Parent 6 0 R /Next 8 0 R /Dest /Chap6 >>"),
        (8, b"<< /Title (Alpha) /Parent 6 0 R /Prev 7 0 R /Dest (alpha) >>"),
        (9, b"[3 0 R /FitH 500]"),
    ])


def gen_outline_full() -> bytes:
    """12.3.3 Tables 150-152: an outline three levels deep. Part I (open, Count 2) holds Chapter 1
    (closed, Count -1, holding Section 1.1) and Chapter 2 (a URI action); Part II (closed, Count -2)
    holds Chapter 3 (UTF-16BE title, red /C [1 0 0], bold italic /F 3, a GoTo action to the named
    destination (alpha)) and Chapter 4 (an /SE structure element and an explicit destination).
    The outline's Count is 4: the two parts and Part I's two children."""
    utf16_title = b"<FEFF" + "Chapter 3 – Übersicht".encode("utf-16-be").hex().upper().encode() + b">"
    return simple_file([
        (1, catalog(b" /Outlines 4 0 R /PageMode /UseOutlines /StructTreeRoot 12 0 R"
                    b" /Names << /Dests << /Names [(alpha) [3 0 R /XYZ 0 792 null]] >> >>")),
        (2, pages()),
        (3, page()),
        (4, b"<< /Type /Outlines /First 5 0 R /Last 9 0 R /Count 4 >>"),
        (5, b"<< /Title (Part I) /Parent 4 0 R /Next 9 0 R /First 6 0 R /Last 8 0 R /Count 2"
            b" /Dest [3 0 R /Fit] >>"),
        (6, b"<< /Title (Chapter 1) /Parent 5 0 R /Next 8 0 R /First 7 0 R /Last 7 0 R /Count -1"
            b" /Dest [3 0 R /FitH 700] >>"),
        (7, b"<< /Title (Section 1.1) /Parent 6 0 R /Dest [3 0 R /XYZ 72 700 null] >>"),
        (8, b"<< /Title (Chapter 2) /Parent 5 0 R /Prev 6 0 R"
            b" /A << /S /URI /URI (https://example.org/chapter-2) >> >>"),
        (9, b"<< /Title (Part II) /Parent 4 0 R /Prev 5 0 R /First 10 0 R /Last 11 0 R /Count -2 >>"),
        (10, b"<< /Title " + utf16_title + b" /Parent 9 0 R /Next 11 0 R /C [1 0 0] /F 3"
             b" /A << /S /GoTo /D (alpha) >> >>"),
        (11, b"<< /Title (Chapter 4) /Parent 9 0 R /Prev 10 0 R /SE 13 0 R /Dest [3 0 R /FitB] >>"),
        (12, b"<< /Type /StructTreeRoot /K 13 0 R >>"),
        (13, b"<< /Type /StructElem /S /H1 /P 12 0 R >>"),
    ])


def gen_outline_broken() -> bytes:
    """12.3.3 Tables 150-151, broken: item 5 has both /Dest and /A; item 6 has no /Title; item 7's
    /Next points back at item 6 (a cycle in the sibling chain); item 8's /First points at its
    ancestor 5; the outline's /Last names item 6, not 7 where the chain ends; item 7 has a child
    but Count 0 (neither open nor closed); the outline's Count is negative."""
    return simple_file([
        (1, catalog(b" /Outlines 4 0 R")),
        (2, pages()),
        (3, page()),
        (4, b"<< /Type /Outlines /First 5 0 R /Last 6 0 R /Count -5 >>"),
        (5, b"<< /Title (One) /Parent 4 0 R /Next 6 0 R /First 8 0 R /Last 8 0 R /Count 1"
            b" /Dest [3 0 R /Fit] /A << /S /URI /URI (https://example.org/) >> >>"),
        (6, b"<< /Parent 4 0 R /Prev 5 0 R /Next 7 0 R /Dest [3 0 R /Fit] >>"),
        (7, b"<< /Title (Three) /Parent 4 0 R /Prev 6 0 R /Next 6 0 R /First 9 0 R /Last 9 0 R"
            b" /Count 0 >>"),
        (8, b"<< /Title (One.One) /Parent 5 0 R /First 5 0 R /Last 5 0 R >>"),
        (9, b"<< /Title (Three.One) /Parent 7 0 R >>"),
    ])


def link(action: bytes) -> bytes:
    """12.5.6.5 Table 176: a link annotation with no border whose A entry is ``action``."""
    return b"<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /Border [0 0 0] /A " + action + b" >>"


def gen_actions_all() -> bytes:
    """12.6 Actions: every action type of Table 201, each in the A entry of its own link annotation
    (objects 40-59, in table order: GoTo with D and SD, GoToR with a string F and a page number, GoToE
    with a nested target dictionary three levels deep (Table 205), GoToDp, Launch with a file
    specification dictionary and the deprecated Win dictionary (Table 208), Thread with an index
    into the catalog's Threads and a bead index, URI relative to the catalog's URI Base (Table 211)
    with IsMap, Sound, Movie naming a movie annotation, Hide with an annotation and a field name,
    Named NextPage with a non-standard /Print next, SubmitForm (12.7.6.2: URL file specification,
    Fields mixing a reference and a name, Flags 13, CharSet), ResetForm (Flags 1), ImportData,
    SetOCGState with PreserveRB false, Rendition with OP 0 and JS, Trans, GoTo3DView with the
    name /F, JavaScript in a stream, RichMediaExecute with a command and two arguments). The GoTo
    action's Next is an array of two actions, the first of which has a Next of its own.
    Trigger events (12.6.3): the catalog's OpenAction (a go-to action) and AA (Table 200: WC WS DS WP
    DP), the page's AA (Table 198: O C), a link annotation's AA (Table 197: E X D U PO PC PV PI) and
    a widget merged with its text field (object 69) whose AA has Table 199's K F V C and Table 197's
    Fo Bl. The name dictionary's JavaScript tree has one document-level script (7.7.4)."""
    id0 = file_id("actions-all").hex().encode()

    def js(text: bytes) -> bytes:
        return b"<< /S /JavaScript /JS (" + text + b") >>"

    catalog_aa = b" ".join(b"/%s %s" % (k, js(k + b"\\(\\);")) for k in [b"WC", b"WS", b"DS", b"WP", b"DP"])
    annot_aa = b" ".join(b"/%s %s" % (k, js(k + b"\\(\\);")) for k in [b"E", b"X", b"D", b"U", b"PO", b"PC", b"PV", b"PI"])
    field_aa = b" ".join(b"/%s %s" % (k, js(k + b"\\(\\);")) for k in [b"K", b"F", b"V", b"C", b"Fo", b"Bl"])
    appearance = b" /AP << /N 81 0 R >>"
    annots = list(range(40, 60)) + [60, 68, 69, 72, 74, 77]
    return simple_file([
        (1, catalog(b" /OpenAction << /S /GoTo /D [3 0 R /Fit] >>"
                    b" /AA << " + catalog_aa + b" >>"
                    b" /URI << /Base (https://example.com/) >>"
                    b" /Names << /JavaScript << /Names [(init) 79 0 R] >> >>"
                    b" /Threads [65 0 R] /OCProperties << /OCGs [70 0 R 71 0 R] /D << /Order [70 0 R 71 0 R] >> >>"
                    b" /AcroForm << /Fields [69 0 R] /CO [69 0 R] >> /StructTreeRoot 61 0 R /DPartRoot 63 0 R")),
        (2, pages()),
        (3, page(extra=b" /AA << /O << /S /Named /N /FirstPage >> /C " + js(b"pageClosed\\(\\);") + b" >>"
                       b" /B [66 0 R] /Annots [" + b" ".join(b"%d 0 R" % n for n in annots) + b"]")),
        (40, link(b"<< /Type /Action /S /GoTo /D [3 0 R /XYZ 0 792 null] /SD [62 0 R /Fit]"
                  b" /Next [<< /S /Named /N /LastPage /Next " + js(b"nested\\(\\);") + b" >>"
                  b" << /S /URI /URI (https://example.com/next) >>] >>")),
        (41, link(b"<< /S /GoToR /F (other.pdf) /D [0 /Fit] /NewWindow true >>")),
        (42, link(b"<< /S /GoToE /D (Chapter 1) /NewWindow false"
                  b" /T << /R /P /T << /R /C /N (embedded.pdf) /T << /R /C /P 0 /A (attached) >> >> >> >>")),
        (43, link(b"<< /S /GoToDp /Dp 82 0 R >>")),
        (44, link(b"<< /S /Launch /F << /Type /Filespec /F (readme.txt) /UF (readme.txt) >>"
                  b" /Win << /F (notepad.exe) /D (C:\\\\Temp) /O (print) /P (readme.txt) >> /NewWindow true >>")),
        (45, link(b"<< /S /Thread /D 0 /B 0 >>")),
        (46, link(b"<< /S /URI /URI (docs/index.html) /IsMap true >>")),
        (47, link(b"<< /S /Sound /Sound 67 0 R /Volume 0.25 /Synchronous true /Repeat false /Mix true >>")),
        (48, link(b"<< /S /Movie /Annotation 68 0 R /Operation /Pause >>")),
        (49, link(b"<< /S /Hide /T [40 0 R (email)] /H false >>")),
        (50, link(b"<< /S /Named /N /NextPage /Next << /S /Named /N /Print >> >>")),
        (51, link(b"<< /S /SubmitForm /F << /FS /URL /F (https://example.com/submit) >>"
                  b" /Fields [69 0 R (name.first)] /Flags 13 /CharSet (utf-8) >>")),
        (52, link(b"<< /S /ResetForm /Fields [(email)] /Flags 1 >>")),
        (53, link(b"<< /S /ImportData /F (data.fdf) >>")),
        (54, link(b"<< /S /SetOCGState /State [/OFF 70 0 R /Toggle 71 0 R 70 0 R] /PreserveRB false >>")),
        (55, link(b"<< /S /Rendition /OP 0 /AN 72 0 R /R 73 0 R /JS (play\\(\\);) >>")),
        (56, link(b"<< /S /Trans /Trans << /Type /Trans /S /Dissolve /D 0.5 >> >>")),
        (57, link(b"<< /S /GoTo3DView /TA 74 0 R /V /F >>")),
        (58, link(b"<< /S /JavaScript /JS 76 0 R >>")),
        (59, link(b"<< /S /RichMediaExecute /TA 77 0 R /TI 78 0 R"
                  b" /CMD << /Type /RichMediaCommand /C (play) /A [(intro) 2 true] >> >>")),
        (60, b"<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /Border [0 0 0] /AA << " + annot_aa + b" >> >>"),
        (61, b"<< /Type /StructTreeRoot /K 62 0 R >>"),
        (62, b"<< /Type /StructElem /S /P /P 61 0 R /Pg 3 0 R >>"),
        (63, b"<< /Type /DPartRoot /DPartRootNode 64 0 R >>"),
        (64, b"<< /Type /DPart /Parent 63 0 R /DParts [[82 0 R]] >>"),
        (65, b"<< /Type /Thread /F 66 0 R /I << /Title (Article) >> >>"),
        (66, b"<< /Type /Bead /T 65 0 R /N 66 0 R /V 66 0 R /P 3 0 R /R [0 0 100 100] >>"),
        (67, stream(b"/Type /Sound /R 8000 /C 1 /B 8 /E /Raw", b"\x80\xa0\x80\x60")),
        (68, b"<< /Type /Annot /Subtype /Movie /Rect [0 0 10 10] /T (Clip) /Movie << /F (clip.mov) >>" + appearance + b" >>"),
        (69, b"<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /P 3 0 R /FT /Tx /T (email)"
             b" /AA << " + field_aa + b" >>" + appearance + b" >>"),
        (70, b"<< /Type /OCG /Name (One) >>"),
        (71, b"<< /Type /OCG /Name (Two) >>"),
        (72, b"<< /Type /Annot /Subtype /Screen /Rect [0 0 10 10] /P 3 0 R" + appearance + b" >>"),
        (73, b"<< /Type /Rendition /S /MR /C << /Type /MediaClip /S /MCD /D << /Type /Filespec /F (clip.mp4) >> /CT (video/mp4) >> >>"),
        (74, b"<< /Type /Annot /Subtype /3D /Rect [0 0 10 10] /3DD 75 0 R" + appearance + b" >>"),
        (75, stream(b"/Type /3D /Subtype /U3D", b"")),
        (76, stream(b"", b'app.alert("stream");')),
        (77, b"<< /Type /Annot /Subtype /RichMedia /Rect [0 0 10 10] /RichMediaContent << /Configurations [] >>"
             + appearance + b" >>"),
        (78, b"<< /Type /RichMediaInstance /Subtype /Video >>"),
        (79, js(b"var initialised = true;")),
        (82, b"<< /Type /DPart /Parent 64 0 R /Start 3 0 R >>"),
        (81, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10]", b"")),
    ], version="2.0", trailer_extra=b" /ID [<%s> <%s>]" % (id0, id0))


def gen_actions_preserved() -> bytes:
    """12.6 Actions that the library keeps as data and never performs (JavaScript 12.6.4.17, Sound
    12.6.4.9, Movie 12.6.4.10, Rendition 12.6.4.14, Rich-Media-Execute 12.6.4.18), each an indirect
    object written in bytes a re-serializer would change, so that a save proves they are copied
    byte for byte: object 10, a script in a literal string with escapes (\\( \\" \\053 \\r \\n, 7.3.4.2
    Table 3) and a backslash-EOL continuation; 11, a script in a hexadecimal string; 12, a script in a
    Flate stream (object 27; the catalog's OpenAction); 13, a script action whose type is written /Java#53cript
    (7.3.5: the same name as /JavaScript); 14, a sound action with /Volume .50 and a four-sample raw
    sound (13.3, object 15); 16, a rendition action with a rendition dictionary and a script stream
    (object 17); 18, a movie action naming the movie annotation 19; 20, a rich-media-execute action
    with TA, TI and CMD; 23 and 24, two go-to actions whose Next entries name each other (a cycle).
    Objects 30-37 are the link annotations holding them; object 13 is the name dictionary's
    document-level script (7.7.4) and object 11 the page's open action (Table 198)."""
    sound = bytes([0x80, 0xA0, 0x80, 0x60])
    script = b"app.alert('flate');"
    links = list(range(30, 38))
    return simple_file([
        (1, catalog(b" /OpenAction 12 0 R /Names << /JavaScript << /Names [(doc) 13 0 R] >> >>")),
        (2, pages()),
        (3, page(extra=b" /AA << /O 11 0 R >> /Annots [" + b" ".join(b"%d 0 R" % n for n in links + [19, 21]) + b"]")),
        (10, b"<<  /S/JavaScript /JS (app.alert\\(\\\"hi\\\"\\);\\053\\r\\n// one \\\nline) >>"),
        (11, b"<</S /JavaScript/JS <6170702E616C6572742827686578272920>>>"),
        (12, b"<< /S /JavaScript /JS 27 0 R >>"),
        (13, b"<< /Type /Action /S /Java#53cript /JS (var x = 1;) >>"),
        (14, b"<< /S /Sound /Sound 15 0 R /Volume .50 /Mix true >>"),
        (15, stream(b"/Type /Sound /R 8000 /C 1 /B 8 /E /Raw", sound)),
        (16, b"<< /S /Rendition /OP 4 /AN 21 0 R /R << /Type /Rendition /S /MR"
             b" /C << /Type /MediaClip /S /MCD /D << /Type /Filespec /F (clip.mp4) >> /CT (video/mp4) >> >> /JS 17 0 R >>"),
        (17, stream(b"", b"play();")),
        (18, b"<< /S /Movie /Annotation 19 0 R /Operation /Play /Rate 2.0 >>"),
        (19, b"<< /Type /Annot /Subtype /Movie /Rect [0 0 10 10] /Movie << /F (clip.mov) >> /AP << /N 25 0 R >> >>"),
        (20, b"<< /S /RichMediaExecute /TA 22 0 R /TI 26 0 R /CMD << /C (rewind) /A 0.0 >> >>"),
        (21, b"<< /Type /Annot /Subtype /Screen /Rect [0 0 10 10] /P 3 0 R /AP << /N 25 0 R >> >>"),
        (22, b"<< /Type /Annot /Subtype /RichMedia /Rect [0 0 10 10] /RichMediaContent << >> /AP << /N 25 0 R >> >>"),
        (23, b"<< /S /GoTo /D [3 0 R /Fit] /Next 24 0 R >>"),
        (24, b"<< /S /GoTo /D [3 0 R /FitH 700] /Next [23 0 R] >>"),
        (25, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10]", b"")),
        (26, b"<< /Type /RichMediaInstance /Subtype /Video >>"),
        (27, stream(b"/Filter /FlateDecode", flate(script))),
        (30, link(b"10 0 R")),
        (31, link(b"11 0 R")),
        (32, link(b"13 0 R")),
        (33, link(b"14 0 R")),
        (34, link(b"16 0 R")),
        (35, link(b"18 0 R")),
        (36, link(b"20 0 R")),
        (37, link(b"23 0 R")),
    ])


XMP = (b'<?xpacket begin="\xef\xbb\xbf" id="W5M0MpCehiHzreSzNTczkc9d"?>\n'
       b'<x:xmpmeta xmlns:x="adobe:ns:meta/">\n'
       b' <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">\n'
       b'  <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">\n'
       b'   <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Broadside</rdf:li></rdf:Alt></dc:title>\n'
       b'  </rdf:Description>\n'
       b' </rdf:RDF>\n'
       b'</x:xmpmeta>\n'
       b'<?xpacket end="w"?>')


def gen_acroform_fields() -> bytes:
    """12.7 interactive forms: one field of each type, PDF 2.0, two pages. AcroForm 4 (12.7.3 Table 224):
    Fields, CO, DA, DR (Helv 5, ZaDb 6), Q, SigFlags 1. Fields (12.7.4, Tables 226-228): 10 `name` text
    field merged with its widget (TU, TM, V, DV, MaxLen, DA, AA with a C trigger 26); 11 `person`
    non-terminal carrying FT Tx, DA, Q 1 and Ff 2 (Required) for its merged kids 12 `first` (inherits all)
    and 13 `last` (own Ff 1: ReadOnly only, UTF-16 V); 14 `agree` check box (12.7.5.2.3) with widgets 15
    (page 3) and 25 (page 30), on state Yes; 16 `color` radio group (12.7.5.2.4, Ff Radio + NoToggleToOff,
    Opt with a duplicate export value, index-named states /0 /1 /2, V /1); 20 `submit` push button whose A
    is a reset-form action naming `person` and field 14; 21 `country` combo box (Combo + Edit, export/display
    pairs, V the export value, DA from the AcroForm); 22 `toppings` multi-select list box (V array, I, TI);
    23 `sig` invisible signature field (12.7.5.5, zero Rect) with Lock 27 and SV 28 (Tables 235-237)."""
    text = b" /AP << /N 7 0 R >>"

    def states(state: bytes) -> bytes:
        return b" /AP << /N << /%s 8 0 R /Off 9 0 R >> /D << /%s 8 0 R /Off 9 0 R >> >>" % (state, state)

    def widget(rect: bytes, page_num: int = 3) -> bytes:
        return b"<< /Type /Annot /Subtype /Widget /Rect " + rect + b" /P %d 0 R /F 4" % page_num

    objects: list[tuple[int, bytes]] = [
        (1, catalog(b" /AcroForm 4 0 R")),
        (2, pages([3, 30])),
        (3, page(extra=b" /Annots [10 0 R 12 0 R 13 0 R 15 0 R 17 0 R 18 0 R 19 0 R 20 0 R 21 0 R 22 0 R 23 0 R]")),
        (4, b"<< /Fields [10 0 R 11 0 R 14 0 R 16 0 R 20 0 R 21 0 R 22 0 R 23 0 R] /CO [10 0 R]"
            b" /DA (/Helv 0 Tf 0 g) /DR << /Font << /Helv 5 0 R /ZaDb 6 0 R >> >> /Q 0 /SigFlags 1 >>"),
        (5, HELVETICA),
        (6, b"<< /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats >>"),
        (7, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 200 20] /Resources << /Font << /Helv 5 0 R >> >>",
                   b"/Tx BMC BT /Helv 12 Tf 2 5 Td (Ada) Tj ET EMC")),
        (8, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 20 20] /Resources << /Font << /ZaDb 6 0 R >> >>",
                   b"q BT /ZaDb 12 Tf 4 5 Td (4) Tj ET Q")),
        (9, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 20 20]", b"0 g 0 0 20 20 re S")),
        (10, widget(b"[50 700 250 720]") + text + b" /FT /Tx /T (name) /TU (Your name) /TM (full_name)"
             b" /V (Ada) /DV (Anonymous) /MaxLen 40 /DA (/Helv 12 Tf 0 g) /AA << /C 26 0 R >> >>"),
        (11, b"<< /T (person) /FT /Tx /DA (/Helv 10 Tf 0 0 1 rg) /Q 1 /Ff 2 /Kids [12 0 R 13 0 R] >>"),
        (12, widget(b"[50 670 150 690]") + text + b" /Parent 11 0 R /T (first) /V (Grace) >>"),
        (13, widget(b"[160 670 260 690]") + text + b" /Parent 11 0 R /T (last) /Ff 1 /V <FEFF004C00F60076> >>"),
        (14, b"<< /FT /Btn /T (agree) /V /Yes /DV /Off /Kids [15 0 R 25 0 R] >>"),
        (15, widget(b"[50 640 70 660]") + states(b"Yes") + b" /Parent 14 0 R /AS /Yes /MK << /CA (4) >> >>"),
        (16, b"<< /FT /Btn /Ff 49152 /T (color) /V /1 /Opt [(Red) (Green) (Green)] /Kids [17 0 R 18 0 R 19 0 R] >>"),
        (17, widget(b"[50 610 70 630]") + states(b"0") + b" /Parent 16 0 R /AS /Off >>"),
        (18, widget(b"[80 610 100 630]") + states(b"1") + b" /Parent 16 0 R /AS /1 >>"),
        (19, widget(b"[110 610 130 630]") + states(b"2") + b" /Parent 16 0 R /AS /Off >>"),
        (20, widget(b"[50 570 150 590]") + text + b" /FT /Btn /Ff 65536 /T (submit) /MK << /CA (Reset) >>"
             b" /A << /S /ResetForm /Fields [(person) 14 0 R] >> >>"),
        (21, widget(b"[50 540 250 560]") + text + b" /FT /Ch /Ff 393216 /T (country)"
             b" /Opt [[(us) (United States)] [(ca) (Canada)]] /V (ca) >>"),
        (22, widget(b"[50 460 250 530]") + text + b" /FT /Ch /Ff 2097152 /T (toppings) /Opt [(Cheese) (Ham) (Olives)]"
             b" /V [(Cheese) (Olives)] /I [0 2] /TI 1 /DA (/Helv 10 Tf 0 g) >>"),
        (23, widget(b"[0 0 0 0]") + b" /FT /Sig /T (sig) /Lock 27 0 R /SV 28 0 R >>"),
        (25, widget(b"[50 700 70 720]", 30) + states(b"Yes") + b" /Parent 14 0 R /AS /Yes >>"),
        (26, b"<< /S /JavaScript /JS (event.value = 1;) >>"),
        (27, b"<< /Type /SigFieldLock /Action /Include /Fields [(name) (person.first)] /P 2 >>"),
        (28, b"<< /Type /SV /Filter /Adobe.PPKLite /Ff 1 >>"),
        (30, page(extra=b" /Annots [25 0 R]")),
    ]
    id0 = file_id("acroform-fields").hex().encode()
    return simple_file(objects, version="2.0", trailer_extra=b" /ID [<%s> <%s>]" % (id0, id0))


def gen_acroform_xfa() -> bytes:
    """Annex K XFA forms (12.7.3 Table 224 XFA): one text field 10 merged with its widget, and an XFA entry
    that is an array of packets [(xdp:xdp) 11 (template) 12 (datasets) 13 (</xdp:xdp>) 14] (Annex K example
    1 shape). The catalog has no NeedsRendering (Table 29), so the XFA form is static. PDF 1.7."""
    objects: list[tuple[int, bytes]] = [
        (1, catalog(b" /AcroForm 4 0 R")),
        (2, pages()),
        (3, page(extra=b" /Annots [10 0 R]")),
        (4, b"<< /Fields [10 0 R] /DA (/Helv 0 Tf 0 g) /DR << /Font << /Helv 5 0 R >> >>"
            b" /XFA [(xdp:xdp) 11 0 R (template) 12 0 R (datasets) 13 0 R (</xdp:xdp>) 14 0 R] >>"),
        (5, HELVETICA),
        (6, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 200 20] /Resources << /Font << /Helv 5 0 R >> >>",
                   b"/Tx BMC BT /Helv 12 Tf 2 5 Td (Ada) Tj ET EMC")),
        (10, b"<< /Type /Annot /Subtype /Widget /Rect [50 700 250 720] /P 3 0 R /F 4 /AP << /N 6 0 R >>"
             b" /FT /Tx /T (name) /V (Ada) >>"),
        (11, stream(b"", b'<xdp:xdp xmlns:xdp="http://ns.adobe.com/xdp/">')),
        (12, stream(b"", b'<template xmlns="http://www.xfa.org/schema/xfa-template/3.3/"><subform name="form1">'
                         b'<field name="name"/></subform></template>')),
        (13, stream(b"", b'<xfa:datasets xmlns:xfa="http://www.xfa.org/schema/xfa-data/1.0/"><xfa:data>'
                         b'<form1><name>Ada</name></form1></xfa:data></xfa:datasets>')),
        (14, stream(b"", b"</xdp:xdp>")),
    ]
    return simple_file(objects)


def gen_metadata_xmp() -> bytes:
    return simple_file([
        (1, catalog(b" /Metadata 4 0 R")),
        (2, pages()),
        (3, page()),
        (4, stream(b"/Type /Metadata /Subtype /XML", XMP)),
        (5, b"<< /Title (Broadside) >>"),
    ], trailer_extra=b" /Info 5 0 R")


def gen_info_dictionary() -> bytes:
    """14.3.3 Table 349: every key, a custom key, Trapped as a name; 7.9.4 dates with an offset, with Z and in the
    legacy form with the terminating apostrophe (NOTE 2); 7.9.2.2 a UTF-16BE title with its byte order marker."""
    title = b"\xfe\xff" + "Broadside – Info".encode("utf-16-be")
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page()),
        (4, b"<< /Title <" + title.hex().upper().encode() + b"> /Author (Ada Lovelace) /Subject (Document information)"
            b" /Keywords (info, metadata) /Creator (generate.py) /Producer (Broadside corpus)"
            b" /CreationDate (D:20140314124211+01'00) /ModDate (D:20140924212303Z) /Trapped /True"
            b" /Printed (D:19981223195200-08'00') /Department (Corpus) >>"),
    ], trailer_extra=b" /Info 4 0 R")


XMP_FORMS = (b'<?xpacket begin="\xef\xbb\xbf" id="W5M0MpCehiHzreSzNTczkc9d"?>\n'
             b'<x:xmpmeta xmlns:x="adobe:ns:meta/">\n'
             b' <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">\n'
             b'  <rdf:Description rdf:about="" xmlns:pdf="http://ns.adobe.com/pdf/1.3/"'
             b' xmlns:xmp="http://ns.adobe.com/xap/1.0/" pdf:Producer="Broadside corpus"'
             b' xmp:CreateDate="2014-09-24T21:23:03+02:00">\n'
             b'   <pdf:Keywords>xmp, forms</pdf:Keywords>\n'
             b'  </rdf:Description>\n'
             b'  <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/"'
             b' xmlns:ex="http://example.com/broadside/">\n'
             b'   <dc:title><rdf:Alt><rdf:li xml:lang="x-default">XMP forms</rdf:li>'
             b'<rdf:li xml:lang="de">XMP-Formen</rdf:li></rdf:Alt></dc:title>\n'
             b'   <dc:creator><rdf:Seq><rdf:li>Ada Lovelace</rdf:li><rdf:li>Grace Hopper</rdf:li></rdf:Seq></dc:creator>\n'
             b'   <dc:subject><rdf:Bag><rdf:li>pdf</rdf:li><rdf:li>xmp</rdf:li></rdf:Bag></dc:subject>\n'
             b'   <ex:Resource rdf:parseType="Resource"><ex:Name>parseType</ex:Name><ex:Count>2</ex:Count></ex:Resource>\n'
             b'  </rdf:Description>\n'
             b'  <rdf:Description rdf:about="" xmlns:pdfaid="http://www.aiim.org/pdfa/ns/id/"'
             b' xmlns:pdfuaid="http://www.aiim.org/pdfua/ns/id/" xmlns:xmpMM="http://ns.adobe.com/xap/1.0/mm/"'
             b' pdfaid:part="2" pdfaid:conformance="B" pdfuaid:part="1">\n'
             b'   <xmpMM:DocumentID>uuid:6b1f3c2e-69a0-4c6b-9d1e-000000000069</xmpMM:DocumentID>\n'
             b'   <xmpMM:InstanceID>uuid:6b1f3c2e-69a0-4c6b-9d1e-000000000070</xmpMM:InstanceID>\n'
             b'  </rdf:Description>\n'
             b' </rdf:RDF>\n'
             b'</x:xmpmeta>\n'
             + b' ' * 2048 + b'\n'
             b'<?xpacket end="w"?>')


def gen_metadata_xmp_forms() -> bytes:
    """14.3.2 metadata stream; ISO 16684-1 7.3 packet wrapper with padding, 7.5-7.7 attribute and element forms,
    arrays of the three kinds, a language alternative, a parseType="Resource" structure, several rdf:Description."""
    return simple_file([
        (1, catalog(b" /Metadata 4 0 R")),
        (2, pages()),
        (3, page()),
        (4, stream(b"/Type /Metadata /Subtype /XML", XMP_FORMS)),
    ])


def gen_viewer_preferences() -> bytes:
    """12.2 Tables 147 and 148: every viewer preference with a value other than its default, Enforce (PDF 2.0);
    7.7.2 Table 29 PageLayout and PageMode."""
    id0 = file_id("viewer-preferences").hex().encode()
    prefs = (b"<< /HideToolbar true /HideMenubar true /HideWindowUI true /FitWindow true /CenterWindow true"
             b" /DisplayDocTitle true /NonFullScreenPageMode /UseOutlines /Direction /R2L /ViewArea /MediaBox"
             b" /ViewClip /BleedBox /PrintArea /TrimBox /PrintClip /ArtBox /PrintScaling /None"
             b" /Duplex /DuplexFlipLongEdge /PickTrayByPDFSize true /PrintPageRange [1 1 1 1] /NumCopies 2"
             b" /Enforce [/PrintScaling] >>")
    return simple_file([
        (1, catalog(b" /ViewerPreferences " + prefs + b" /PageLayout /TwoPageRight /PageMode /UseOC")),
        (2, pages()),
        (3, page()),
    ], version="2.0", trailer_extra=b" /ID [<%s> <%s>]" % (id0, id0))


def gen_catalog_version_extensions() -> bytes:
    """7.7.2 Table 29 Version later than the header; 7.12 Tables 48-49 developer extensions as a dictionary and, in
    PDF 2.0, as an array (the ISO/TS 32001 declaration); 12.11 Tables 273-274 Requirements; 14.4 distinct IDs."""
    id0 = file_id("catalog-version-extensions").hex().encode()
    id1 = hashlib.md5(b"broadside-corpus:catalog-version-extensions:changed").hexdigest().encode()
    extra = (b" /Version /2.0 /Extensions << /Type /Extensions /ADBE << /BaseVersion /1.7 /ExtensionLevel 3 >>"
             b" /ISO_ [<< /Type /DeveloperExtensions /BaseVersion /2.0 /ExtensionLevel 32001 /ExtensionRevision (:2022)"
             b" /URL (https://www.iso.org/standard/45874.html) >>] >>"
             b" /Requirements [<< /Type /Requirement /S /EnableJavaScripts /Penalty 50 /RH << /Type /ReqHandler /S /NoOp >> >>]")
    return simple_file([
        (1, catalog(extra)),
        (2, pages()),
        (3, page()),
    ], version="1.7", trailer_extra=b" /ID [<%s> <%s>]" % (id0, id1))


def gen_page_labels() -> bytes:
    """12.4.2 Table 161: page label ranges of every numbering style, St, P, a prefix-only range; 7.9.7 a number tree
    whose root holds Nums."""
    count = 12
    kids = list(range(3, 3 + count))
    labels = (b" /PageLabels << /Nums [0 << /S /r >> 3 << /S /D >> 5 << /Type /PageLabel /S /R /St 4 >>"
              b" 7 << /S /A /St 26 >> 9 << /S /a /St 52 /P (A-) >> 11 << /P (Cover) >>] >>")
    return simple_file([
        (1, catalog(labels)),
        (2, pages(kids=kids)),
    ] + [(k, page()) for k in kids])
# ---------------------------------------------------------------------------
# Optional content, files, associated files, object metadata, declarations (issue #76)
# ---------------------------------------------------------------------------


def gen_optional_content() -> bytes:
    """8.11: four groups (A, B in D's OFF, C with Intent Design, D2 with a View usage OFF but no AS),
    OCMD 9 (/OCGs [A B] /P /AllOff) and OCMD 10 (/VE [/Or A [/Not B]]), content sections /OC /a, /m1, /m2
    and /a nested in /b, a form XObject with /OC B, an annotation with /OC C; D carries Order (a label and
    an unlabelled nested array), RBGroups and Locked; Configs has one alternate configuration
    (8.11.4.3 Table 99: BaseState OFF, ON [C], Intent All)."""
    content = (b"/OC /a BDC 0 0 10 10 re f EMC\n"
               b"/OC /m1 BDC 20 0 10 10 re f EMC\n"
               b"/OC /m2 BDC 40 0 10 10 re f EMC\n"
               b"/OC /b BDC /OC /a BDC 60 0 10 10 re f EMC EMC\n"
               b"/Fm Do\n")
    ocprops = (b" /OCProperties << /OCGs [5 0 R 6 0 R 7 0 R 8 0 R]"
               b" /D << /Name (Default) /Creator (Broadside corpus) /OFF [6 0 R]"
               b" /Order [5 0 R [(Labelled) 6 0 R 7 0 R] [8 0 R]] /RBGroups [[5 0 R 6 0 R]] /Locked [7 0 R] >>"
               b" /Configs [<< /Name (Only C) /BaseState /OFF /ON [7 0 R] /Intent /All /ListMode /VisiblePages >>] >>")
    return simple_file([
        (1, catalog(ocprops)),
        (2, pages()),
        (3, page(contents=4, resources=False,
                 extra=b" /Resources << /Properties << /a 5 0 R /b 6 0 R /m1 9 0 R /m2 10 0 R >>"
                       b" /XObject << /Fm 11 0 R >> >> /Annots [12 0 R]")),
        (4, stream(b"", content)),
        (5, b"<< /Type /OCG /Name (A) >>"),
        (6, b"<< /Type /OCG /Name (B) >>"),
        (7, b"<< /Type /OCG /Name (C) /Intent /Design /Usage << /CreatorInfo << /Creator (Broadside) /Subtype /Technical >> >> >>"),
        (8, b"<< /Type /OCG /Name (D2) /Usage << /View << /ViewState /OFF >> /Zoom << /min 1.5 >> >> >>"),
        (9, b"<< /Type /OCMD /OCGs [5 0 R 6 0 R] /P /AllOff >>"),
        (10, b"<< /Type /OCMD /VE [/Or 5 0 R [/Not 6 0 R]] >>"),
        (11, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10] /OC 6 0 R", b"80 0 10 10 re f")),
        (12, b"<< /Type /Annot /Subtype /Square /Rect [100 100 200 200] /OC 7 0 R >>"),
    ])


def utf16_text(text: str) -> bytes:
    """7.9.2.2 text string as a hexadecimal string: FEFF byte order mark, then UTF-16BE."""
    return b"<FEFF" + text.encode("utf-16-be").hex().upper().encode() + b">"


def gen_embedded_files() -> bytes:
    """7.11.3-7.11.4 and 7.7.4 EmbeddedFiles: two file specifications in the name tree. hello.txt has
    Subtype text/plain and Params Size, CreationDate, ModDate and CheckSum (the MD5 of the data, Table 45);
    data.csv has a non-ASCII UF, Desc, a Flate-encoded stream and one related file (RF, 7.11.4.2)."""
    hello = b"Hello, world!"
    csv = b"name,value\nalpha,1\nbeta,2\n"
    return simple_file([
        (1, catalog(b" /Names << /EmbeddedFiles 4 0 R >>")),
        (2, pages()),
        (3, page()),
        (4, b"<< /Names [(data.csv) 5 0 R (hello.txt) 7 0 R] >>"),
        (5, b"<< /Type /Filespec /F (data.csv) /UF " + utf16_text("d\u00e4t\u00e4.csv") + b" /Desc (Comma-separated data)"
            b" /EF << /F 6 0 R /UF 6 0 R >> /RF << /F 9 0 R /UF 9 0 R >> >>"),
        (6, stream(b"/Type /EmbeddedFile /Subtype /text#2Fcsv /Filter /FlateDecode /Params << /Size %d >>" % len(csv), flate(csv))),
        (7, b"<< /Type /Filespec /F (hello.txt) /UF (hello.txt) /EF << /F 8 0 R /UF 8 0 R >> >>"),
        (8, stream(b"/Type /EmbeddedFile /Subtype /text#2Fplain /Params << /Size %d /CreationDate (D:20240102030405Z)"
                   b" /ModDate (D:20240607080910+02'00) /CheckSum <%s> >>" % (len(hello), hashlib.md5(hello).hexdigest().encode()),
                   hello)),
        (9, b"[(data.schema) 10 0 R]"),
        (10, stream(b"/Type /EmbeddedFile /Subtype /text#2Fplain", b"name:text,value:int")),
    ])


def gen_collection_portfolio() -> bytes:
    """12.3.5 portable collection: Collection with View D, initial document D, Schema (S, D, N, F, Size fields
    with O, V and E), Sort (S array, short A array), Colors, Split and a Folders tree (root ID 0 with Free,
    child ID 1); EmbeddedFiles keys <1>report.txt (in folder 1) and notes.txt (root folder); collection items
    with a subitem (7.11.6, Tables 46 and 47)."""
    collection = (b"<< /Type /Collection /View /D /D (notes.txt)"
                  b" /Schema << /Type /CollectionSchema"
                  b" /name << /Type /CollectionField /Subtype /S /N (Name) /O 0 >>"
                  b" /date << /Type /CollectionField /Subtype /D /N (Date) /O 1 >>"
                  b" /pages << /Type /CollectionField /Subtype /N /N (Pages) /O 2 /V false >>"
                  b" /fname << /Type /CollectionField /Subtype /F /N (File) /O 3 /E true >>"
                  b" /size << /Type /CollectionField /Subtype /Size /N (Size) /O 4 >> >>"
                  b" /Sort << /Type /CollectionSort /S [/date /name] /A [false] >>"
                  b" /Colors << /Background [1 1 1] /CardBackground [0.9 0.9 0.9] /CardBorder [0 0 0]"
                  b" /PrimaryText [0 0 0] /SecondaryText [0.5 0.5 0.5] >>"
                  b" /Split << /Direction /V /Position 30 >> /Folders 10 0 R >>")
    return simple_file([
        (1, catalog(b" /Names << /EmbeddedFiles << /Names [(<1>report.txt) 5 0 R (notes.txt) 7 0 R] >> >> /Collection 9 0 R")),
        (2, pages()),
        (3, page()),
        (5, b"<< /Type /Filespec /F (report.txt) /UF (report.txt) /EF << /F 12 0 R >> /CI 6 0 R >>"),
        (6, b"<< /Type /CollectionItem /name << /Type /CollectionSubitem /D (Quarterly report) /P (Q1: ) >>"
            b" /date (D:20240301000000Z) /pages 3 >>"),
        (7, b"<< /Type /Filespec /F (notes.txt) /UF (notes.txt) /EF << /F 13 0 R >> /CI 8 0 R >>"),
        (8, b"<< /Type /CollectionItem /name (Notes) /pages 1 >>"),
        (9, collection),
        (10, b"<< /Type /Folder /ID 0 /Name (Portfolio) /Child 11 0 R /Free [2 10] >>"),
        (11, b"<< /Type /Folder /ID 1 /Name (Reports) /Parent 10 0 R /Desc (Quarterly reports) >>"),
        (12, stream(b"/Type /EmbeddedFile /Subtype /text#2Fplain", b"Revenue up.")),
        (13, stream(b"/Type /EmbeddedFile /Subtype /text#2Fplain", b"Remember the milk.")),
    ])


def xmp_titled(title: bytes) -> bytes:
    """A minimal XMP packet (14.3.2) whose dc:title is ``title``."""
    return XMP.replace(b">Broadside<", b">" + title + b"<")


def af_filespec(name: bytes, relationship: bytes | None, ef: int | None = None) -> bytes:
    """7.11.3 file specification for an associated file (14.13): embedded when ``ef`` is given, else external."""
    body = b"<< /Type /Filespec /F (%s) /UF (%s)" % (name, name)
    if relationship is not None:
        body += b" /AFRelationship /" + relationship
    if ef is not None:
        body += b" /EF << /F %d 0 R /UF %d 0 R >>" % (ef, ef)
    return body + b" >>"


def gen_associated_files() -> bytes:
    """14.13 associated files at every location: catalog (Source, embedded), page (Data), form XObject
    (Supplement, embedded application/mathml+xml), image XObject (Data), annotation (no AFRelationship:
    Unspecified), structure tree root (Alternative), structure element (Alternative), DPart (Source),
    metadata stream (Schema), marked content /AF /MF1 with an MCAF property list (errata Table 409a) and
    /AF /MF2 with a bare array resource (14.13.5 Example 2). PDF 2.0 with a trailer ID (7.5.5)."""
    id0 = file_id("associated-files").hex().encode()
    content = b"/AF /MF1 BDC 0 0 10 10 re f EMC\n/AF /MF2 BDC /Fm Do EMC\nq 10 0 0 10 20 0 cm /Im Do Q\n"
    return simple_file([
        (1, catalog(b" /AF [20 0 R] /StructTreeRoot 6 0 R /DPartRoot 8 0 R /Metadata 10 0 R /MarkInfo << /Marked true >>")),
        (2, pages()),
        (3, page(contents=4, resources=False,
                 extra=b" /AF [21 0 R] /DPart 9 0 R /Annots [5 0 R] /Resources << /XObject << /Fm 11 0 R /Im 12 0 R >>"
                       b" /Properties << /MF1 13 0 R /MF2 [31 0 R] >> >>")),
        (4, stream(b"", content)),
        (5, b"<< /Type /Annot /Subtype /Square /Rect [100 100 200 200] /AF [22 0 R] >>"),
        (6, b"<< /Type /StructTreeRoot /K 7 0 R /AF [23 0 R] >>"),
        (7, b"<< /Type /StructElem /S /Document /P 6 0 R /AF [24 0 R] >>"),
        (8, b"<< /Type /DPartRoot /DPartRootNode 9 0 R >>"),
        (9, b"<< /Type /DPart /Parent 8 0 R /Start 3 0 R /End 3 0 R /AF [25 0 R] >>"),
        (10, stream(b"/Type /Metadata /Subtype /XML /AF [26 0 R]", XMP)),
        (11, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10] /AF [27 0 R]", b"40 0 10 10 re f")),
        (12, stream(b"/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /AF [28 0 R]", b"\x80")),
        (13, b"<< /MCAF [29 0 R] >>"),
        (20, af_filespec(b"source.txt", b"Source", ef=30)),
        (21, af_filespec(b"page-data.csv", b"Data")),
        (22, af_filespec(b"annotation.txt", None)),
        (23, af_filespec(b"tree.txt", b"Alternative")),
        (24, af_filespec(b"element.txt", b"Alternative")),
        (25, af_filespec(b"part.txt", b"Source")),
        (26, af_filespec(b"schema.xsd", b"Schema")),
        (27, af_filespec(b"equation.mml", b"Supplement", ef=32)),
        (28, af_filespec(b"image-data.csv", b"Data")),
        (29, af_filespec(b"marked.csv", b"Data")),
        (30, stream(b"/Type /EmbeddedFile /Subtype /text#2Fplain /Params << /ModDate (D:20240101000000Z) /Size 6 >>", b"source")),
        (31, af_filespec(b"marked-legacy.txt", b"Supplement")),
        (32, stream(b"/Type /EmbeddedFile /Subtype /application#2Fmathml+xml /Params << /ModDate (D:20240101000000Z) >>",
                    b"<math><mi>x</mi></math>")),
    ], version="2.0", trailer_extra=b" /ID [<%s> <%s>]" % (id0, id0))


def gen_object_metadata() -> bytes:
    """14.3.2 and PDF 2.0 Application Note 003: a Metadata stream, each with a distinct dc:title, on the catalog
    (Document), a page, an image XObject, a form XObject, an ICCBased colour space stream, an embedded TrueType
    font program (FontFile2), a tiling pattern, a shading dictionary, a marked-content property list, an
    optional content group, an annotation and an embedded file stream."""
    font, widths = minimal_truetype()
    first, last = min(widths), max(widths)
    w = b" ".join(b"%d" % widths[c] for c in range(first, last + 1))
    titles = [b"Document", b"Page", b"Image", b"Form", b"ICC profile", b"Font program", b"Tiling pattern",
              b"Shading", b"Marked content", b"Optional content", b"Annotation", b"Embedded file"]
    meta = {title: 40 + i for i, title in enumerate(titles)}
    objects = [
        (1, catalog(b" /Metadata %d 0 R /OCProperties << /OCGs [15 0 R] /D << >> >>"
                    b" /Names << /EmbeddedFiles << /Names [(attached.txt) 16 0 R] >> >>" % meta[b"Document"])),
        (2, pages()),
        (3, page(contents=4, resources=False,
                 extra=b" /Metadata %d 0 R /Annots [14 0 R] /Resources << /XObject << /Im 5 0 R /Fm 6 0 R >>"
                       b" /ColorSpace << /CS0 [/ICCBased 7 0 R] >> /Font << /F1 8 0 R >> /Pattern << /P0 10 0 R >>"
                       b" /Shading << /Sh0 11 0 R >> /Properties << /MC0 12 0 R /OC0 15 0 R >> >>" % meta[b"Page"])),
        (4, stream(b"", b"/Im Do /Fm Do /Span /MC0 BDC EMC /OC /OC0 BDC EMC BT /F1 24 Tf 72 700 Td (HI) Tj ET\n")),
        (5, stream(b"/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Metadata %d 0 R"
                   % meta[b"Image"], b"\x80")),
        (6, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Metadata %d 0 R" % meta[b"Form"], b"0 0 10 10 re f")),
        (7, stream(b"/N 1 /Alternate /DeviceGray /Metadata %d 0 R" % meta[b"ICC profile"], b"ICC")),
        (8, b"<< /Type /Font /Subtype /TrueType /BaseFont /BroadsideMinimal /FirstChar %d /LastChar %d "
            b"/Widths [%s] /Encoding /WinAnsiEncoding /FontDescriptor 9 0 R >>" % (first, last, w)),
        (9, b"<< /Type /FontDescriptor /FontName /BroadsideMinimal /Flags 32 /FontBBox [0 0 700 700] "
            b"/ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 200 /FontFile2 13 0 R >>"),
        (10, stream(b"/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10"
                    b" /Resources << >> /Metadata %d 0 R" % meta[b"Tiling pattern"], b"0 0 5 5 re f")),
        (11, b"<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [0 0 1 0] /Function << /FunctionType 2 /Domain [0 1]"
             b" /C0 [0] /C1 [1] /N 1 >> /Metadata %d 0 R >>" % meta[b"Shading"]),
        (12, b"<< /Metadata %d 0 R >>" % meta[b"Marked content"]),
        (13, stream(b"/Length1 %d /Metadata %d 0 R" % (len(font), meta[b"Font program"]), font)),
        (14, b"<< /Type /Annot /Subtype /Square /Rect [100 100 200 200] /Metadata %d 0 R >>" % meta[b"Annotation"]),
        (15, b"<< /Type /OCG /Name (Layer) /Metadata %d 0 R >>" % meta[b"Optional content"]),
        (16, b"<< /Type /Filespec /F (attached.txt) /UF (attached.txt) /EF << /F 17 0 R /UF 17 0 R >> >>"),
        (17, stream(b"/Type /EmbeddedFile /Subtype /text#2Fplain /Metadata %d 0 R" % meta[b"Embedded file"], b"attached")),
    ]
    objects += [(meta[title], stream(b"/Type /Metadata /Subtype /XML", xmp_titled(title))) for title in titles]
    return simple_file(objects, binary=True)


def declarations_xmp(namespace: bytes, body: bytes) -> bytes:
    """An XMP packet (ISO 16684-1) holding a pdfd:declarations bag (PDF Declarations 7.1, 8.1)."""
    return (b'<?xpacket begin="\xef\xbb\xbf" id="W5M0MpCehiHzreSzNTczkc9d"?>\n'
            b'<x:xmpmeta xmlns:x="adobe:ns:meta/">\n'
            b' <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">\n'
            b'  <rdf:Description rdf:about="" xmlns:pdfd="' + namespace + b'">\n'
            b'   <pdfd:declarations><rdf:Bag>\n' + body +
            b'   </rdf:Bag></pdfd:declarations>\n'
            b'  </rdf:Description>\n'
            b' </rdf:RDF>\n'
            b'</x:xmpmeta>\n'
            b'<?xpacket end="w"?>')


def gen_declarations() -> bytes:
    """PDF Declarations 7-8: the catalog XMP holds two declarations, ISO/TS 32005 with one claim (claimBy,
    claimDate, claimCredentials, claimReport as an Annex O #ef= fragment) written with rdf:parseType="Resource",
    and WTPDF reuse written as a nested rdf:Description with white space around the URI; the page XMP holds one
    object-level declaration under the https namespace TS 32005 Table 1 shows. PDF 2.0 with a trailer ID."""
    id0 = file_id("declarations").hex().encode()
    document = declarations_xmp(b"http://pdfa.org/declarations/",
        b'    <rdf:li rdf:parseType="Resource">\n'
        b'     <pdfd:conformsTo>https://pdfa.org/declarations#iso32005</pdfd:conformsTo>\n'
        b'     <pdfd:claimData><rdf:Bag><rdf:li rdf:parseType="Resource">\n'
        b'      <pdfd:claimBy>Broadside corpus</pdfd:claimBy>\n'
        b'      <pdfd:claimDate>2024-05-06</pdfd:claimDate>\n'
        b'      <pdfd:claimCredentials>Generated by generate.py</pdfd:claimCredentials>\n'
        b'      <pdfd:claimReport>#ef=report.html</pdfd:claimReport>\n'
        b'     </rdf:li></rdf:Bag></pdfd:claimData>\n'
        b'    </rdf:li>\n'
        b'    <rdf:li><rdf:Description>\n'
        b'     <pdfd:conformsTo>\n       http://pdfa.org/declarations/wtpdf/#reuse1.0\n     </pdfd:conformsTo>\n'
        b'    </rdf:Description></rdf:li>\n')
    page_xmp = declarations_xmp(b"https://pdfa.org/declarations/",
        b'    <rdf:li rdf:parseType="Resource"><pdfd:conformsTo>http://pdfa.org/declarations/wtpdf/#accessibility1.0</pdfd:conformsTo></rdf:li>\n')
    return simple_file([
        (1, catalog(b" /Metadata 4 0 R")),
        (2, pages()),
        (3, page(extra=b" /Metadata 5 0 R")),
        (4, stream(b"/Type /Metadata /Subtype /XML", document)),
        (5, stream(b"/Type /Metadata /Subtype /XML", page_xmp)),
    ], version="2.0", binary=True, trailer_extra=b" /ID [<%s> <%s>]" % (id0, id0))


def gen_tagged_structure() -> bytes:
    """14.6 marked content, 14.7 logical structure and 14.8 tagged PDF: a heading, a paragraph with a
    Link annotation (OBJR, StructParent), a two-item list and a two-by-two table, all as MCIDs 0..9 on
    one page (StructParents 0), plus a pagination artifact outside the tree. ParentTree (14.7.5.4),
    IDTree, RoleMap (Para -> P in the default 1.7 namespace), ClassMap with C and A on the heading
    (14.7.6.2: A wins), namespaces (14.7.4) PDF 2.0 and a custom one whose RoleMapNS maps Chapter to
    [/Sect <2.0>] (14.8.6.2), and one marked-content reference dictionary (Table 357) for MCID 9."""
    content = b"\n".join([
        b"/Artifact <</Type /Pagination /Subtype /PageNum>> BDC BT /F1 10 Tf 300 40 Td (1) Tj ET EMC",
        b"/H1 <</MCID 0>> BDC BT /F1 24 Tf 72 720 Td (Broadside) Tj ET EMC",
        b"/P <</MCID 1>> BDC BT /F1 12 Tf 72 690 Td (A paragraph with a link.) Tj ET EMC",
        b"/Lbl <</MCID 2>> BDC BT /F1 12 Tf 72 660 Td (1.) Tj ET EMC",
        b"/LBody <</MCID 3>> BDC BT /F1 12 Tf 96 660 Td (First item) Tj ET EMC",
        b"/Lbl <</MCID 4>> BDC BT /F1 12 Tf 72 645 Td (2.) Tj ET EMC",
        b"/LBody <</MCID 5>> BDC BT /F1 12 Tf 96 645 Td (Second item) Tj ET EMC",
        b"/TH <</MCID 6>> BDC BT /F1 12 Tf 72 610 Td (Name) Tj ET EMC",
        b"/TH <</MCID 7>> BDC BT /F1 12 Tf 200 610 Td (Value) Tj ET EMC",
        b"/TD <</MCID 8>> BDC BT /F1 12 Tf 72 595 Td (Alpha) Tj ET EMC",
        b"/TD <</MCID 9>> BDC BT /F1 12 Tf 200 595 Td (1) Tj ET EMC",
    ])
    ns20 = b"/NS 7 0 R"
    id0 = file_id("tagged-structure").hex().encode()

    def elem(s: bytes, parent: int, extra: bytes) -> bytes:
        return b"<< /Type /StructElem /S /%s /P %d 0 R %s >>" % (s, parent, extra)

    return simple_file([
        (1, catalog(b" /MarkInfo << /Marked true >> /StructTreeRoot 10 0 R /Lang (en-US)")),
        (2, pages()),
        (3, page(contents=4, font=6, extra=b" /StructParents 0 /Tabs /S /Annots [5 0 R]")),
        (4, stream(b"", content)),
        (5, b"<< /Type /Annot /Subtype /Link /Rect [72 686 240 702] /Border [0 0 0] "
            b"/A << /S /URI /URI (https://example.com/) >> /StructParent 1 >>"),
        (6, HELVETICA),
        (7, b"<< /Type /Namespace /NS (http://iso.org/pdf2/ssn) >>"),
        (8, b"<< /Type /Namespace /NS (https://example.com/broadside-corpus) /RoleMapNS << /Chapter [/Sect 7 0 R] >> >>"),
        (10, b"<< /Type /StructTreeRoot /K [11 0 R] "
             b"/ParentTree << /Nums [0 [13 0 R 14 0 R 17 0 R 18 0 R 20 0 R 21 0 R 24 0 R 25 0 R 27 0 R 28 0 R] 1 14 0 R] >> "
             b"/ParentTreeNextKey 2 /IDTree << /Names [(h1) 24 0 R (tbl1) 22 0 R] >> /RoleMap << /Para /P >> "
             b"/ClassMap << /Centered << /O /Layout /TextAlign /Center /SpaceAfter 6 >> >> /Namespaces [7 0 R 8 0 R] >>"),
        (11, elem(b"Document", 10, ns20 + b" /K [12 0 R]")),
        (12, elem(b"Chapter", 11, b"/NS 8 0 R /T (Chapter 1) /K [13 0 R 14 0 R 15 0 R 22 0 R]")),
        (13, elem(b"H1", 12, ns20 + b" /Pg 3 0 R /K 0 /C /Centered /A << /O /Layout /SpaceAfter 12 >>")),
        (14, elem(b"Para", 12, b"/Pg 3 0 R /K [1 << /Type /OBJR /Obj 5 0 R >>] /A << /O /Layout /TextAlign /Justify >>")),
        (15, elem(b"L", 12, ns20 + b" /A << /O /List /ListNumbering /Decimal >> /K [16 0 R 19 0 R]")),
        (16, elem(b"LI", 15, ns20 + b" /K [17 0 R 18 0 R]")),
        (17, elem(b"Lbl", 16, ns20 + b" /Pg 3 0 R /K 2")),
        (18, elem(b"LBody", 16, ns20 + b" /Pg 3 0 R /K 3")),
        (19, elem(b"LI", 15, ns20 + b" /K [20 0 R 21 0 R]")),
        (20, elem(b"Lbl", 19, ns20 + b" /Pg 3 0 R /K 4")),
        (21, elem(b"LBody", 19, ns20 + b" /Pg 3 0 R /K 5")),
        (22, elem(b"Table", 12, ns20 + b" /ID (tbl1) /A << /O /Table /Summary (Names and values) >> /K [23 0 R 26 0 R]")),
        (23, elem(b"TR", 22, ns20 + b" /K [24 0 R 25 0 R]")),
        (24, elem(b"TH", 23, ns20 + b" /ID (h1) /Pg 3 0 R /K 6 /A [<< /O /Table /Scope /Column >> 0]")),
        (25, elem(b"TH", 23, ns20 + b" /Pg 3 0 R /K 7")),
        (26, elem(b"TR", 22, ns20 + b" /K [27 0 R 28 0 R]")),
        (27, elem(b"TD", 26, ns20 + b" /Pg 3 0 R /K 8 /A << /O /Table /Headers [(h1)] >>")),
        (28, elem(b"TD", 26, ns20 + b" /K << /Type /MCR /MCID 9 /Pg 3 0 R >>")),
    ], version="2.0", trailer_extra=b" /ID [<%s> <%s>]" % (id0, id0))


def gen_functions() -> bytes:
    """7.10 functions as Separation tint transforms (8.6.6.4), one rectangle per colour space.
    CS0: Type 0 (7.10.2), 8-bit, 1 in, 4 out (DeviceCMYK), Size [3], painted at 0.5 (the middle sample).
    CS1: Type 2 (7.10.3) N 1 to DeviceRGB, at 0.5. CS2: Type 2 N 2 to DeviceGray, at 0.5.
    CS3: Type 3 (7.10.4) stitching two Type 2 halves (N 1 and N 2) with Bounds [0.5] and Encode [0 1 1 0], at 0.75.
    CS4: Type 4 (7.10.5), the LogoGreen example of 8.6.6.4, at 0.5. CS5 shares CS1's indirect function, at 0.25."""
    spaces = [(b"Sampled", b"DeviceCMYK", 11, b"0.5"), (b"Linear", b"DeviceRGB", 12, b"0.5"),
              (b"Square", b"DeviceGray", 13, b"0.5"), (b"Stitched", b"DeviceGray", 14, b"0.75"),
              (b"LogoGreen", b"DeviceCMYK", 17, b"0.5"), (b"Shared", b"DeviceRGB", 12, b"0.25")]
    content = b""
    for i, (_, _, _, tint) in enumerate(spaces):
        x = 50 + (i % 3) * 180
        y = 550 if i < 3 else 350
        content += b"/CS%d cs %s scn %d %d 150 150 re f\n" % (i, tint, x, y)
    color_spaces = b" ".join(b"/CS%d %d 0 R" % (i, 5 + i) for i in range(len(spaces)))
    objects = [
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources << /ColorSpace << " + color_spaces + b" >> >>")),
        (4, stream(b"", content)),
    ]
    for i, (name, alternate, function, _) in enumerate(spaces):
        objects.append((5 + i, b"[/Separation /%s /%s %d 0 R]" % (name, alternate, function)))
    # Samples at t = 0, 0.5, 1: (0 0 0 0), (0.2 0.4 0 0), (1 0 0 0.2); 8-bit, Decode = Range = [0 1] x 4.
    samples = bytes([0, 0, 0, 0, 51, 102, 0, 0, 255, 0, 0, 51])
    objects += [
        (11, stream(b"/FunctionType 0 /Domain [0 1] /Range [0 1 0 1 0 1 0 1] /Size [3] /BitsPerSample 8", samples)),
        (12, b"<< /FunctionType 2 /Domain [0 1] /C0 [1 1 1] /C1 [0 0 1] /N 1 >>"),
        (13, b"<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 2 >>"),
        (14, b"<< /FunctionType 3 /Domain [0 1] /Functions [15 0 R 16 0 R] /Bounds [0.5] /Encode [0 1 1 0] >>"),
        (15, b"<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >>"),
        (16, b"<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 2 >>"),
        (17, stream(b"/FunctionType 4 /Domain [0.0 1.0] /Range [0.0 1.0 0.0 1.0 0.0 1.0 0.0 1.0]",
                    b"{dup 0.84 mul\nexch 0.00 exch dup 0.44 mul exch 0.21 mul\n}")),
    ]
    return simple_file(objects)

# ---------------------------------------------------------------------------
# Colour spaces (clause 8.6)
# ---------------------------------------------------------------------------

def s15f16(value: float) -> bytes:
    """ICC.1:2022 4.6 s15Fixed16Number."""
    return struct.pack(">i", round(value * 65536))


def icc_profile(device_class: bytes, space: bytes, tags: list[tuple[bytes, bytes]]) -> bytes:
    """A version 2.1 ICC profile (ICC.1:2022 7.2 header, 7.3 tag table): XYZ PCS, D50 illuminant, the tags 4-byte aligned."""
    count = len(tags)
    offset = 128 + 4 + 12 * count
    table = struct.pack(">I", count)
    data = b""
    for sig, body in tags:
        table += sig + struct.pack(">II", offset + len(data), len(body))
        data += body + b"\x00" * (-len(body) % 4)
    size = 128 + len(table) + len(data)
    header = (struct.pack(">I", size) + b"\x00" * 4 + bytes([2, 0x10, 0, 0]) + device_class + space + b"XYZ "
              + struct.pack(">6H", 2026, 1, 1, 0, 0, 0) + b"acsp" + b"\x00" * 24 + struct.pack(">I", 0)
              + s15f16(0.9642) + s15f16(1.0) + s15f16(0.8249) + b"\x00" * 48)
    assert len(header) == 128
    return header + table + data


def icc_desc(text: bytes) -> bytes:
    """ICC.1:2001 textDescriptionType (version 2 profiles): ASCII, empty Unicode and ScriptCode parts."""
    return b"desc" + b"\x00" * 4 + struct.pack(">I", len(text) + 1) + text + b"\x00" + b"\x00" * 8 + b"\x00" * 3 + b"\x00" * 67


def icc_xyz(x: float, y: float, z: float) -> bytes:
    return b"XYZ " + b"\x00" * 4 + s15f16(x) + s15f16(y) + s15f16(z)


def icc_gamma(gamma: float) -> bytes:
    """curveType with one entry, a u8Fixed8Number gamma."""
    return b"curv" + b"\x00" * 4 + struct.pack(">IH", 1, round(gamma * 256))


ICC_COPYRIGHT = b"text" + b"\x00" * 4 + b"No copyright, use freely\x00"


def icc_rgb_profile() -> bytes:
    """Display (mntr) RGB matrix/TRC profile: the sRGB primaries adapted to D50 (IEC 61966-2-1 Annex), gamma 2.2."""
    return icc_profile(b"mntr", b"RGB ", [
        (b"desc", icc_desc(b"Broadside RGB gamma 2.2")),
        (b"cprt", ICC_COPYRIGHT),
        (b"wtpt", icc_xyz(0.9642, 1.0, 0.8249)),
        (b"rXYZ", icc_xyz(0.4361, 0.2225, 0.0139)),
        (b"gXYZ", icc_xyz(0.3851, 0.7169, 0.0971)),
        (b"bXYZ", icc_xyz(0.1431, 0.0606, 0.7141)),
        (b"rTRC", icc_gamma(2.2)),
        (b"gTRC", icc_gamma(2.2)),
        (b"bTRC", icc_gamma(2.2)),
    ])


def icc_gray_profile() -> bytes:
    """Display (mntr) GRAY profile: a gamma 2.2 tone curve."""
    return icc_profile(b"mntr", b"GRAY", [
        (b"desc", icc_desc(b"Broadside gray gamma 2.2")),
        (b"cprt", ICC_COPYRIGHT),
        (b"wtpt", icc_xyz(0.9642, 1.0, 0.8249)),
        (b"kTRC", icc_gamma(2.2)),
    ])


D65 = b"/WhitePoint [0.9505 1 1.089]"
SRGB_MATRIX = b"/Matrix [0.4124 0.2126 0.0193 0.3576 0.7152 0.1192 0.1805 0.0722 0.9505]"


def colour_rects(entries: list[tuple[bytes, bytes]], columns: int = 4) -> bytes:
    """One 100 x 100 rectangle per (colour space resource, colour operator text), left to right, top to bottom."""
    content = b""
    for i, (name, colour) in enumerate(entries):
        x = 40 + (i % columns) * 140
        y = 640 - (i // columns) * 140
        content += b"/%s cs %s %d %d 100 100 re f\n" % (name, colour, x, y)
    return content


def gen_colorspace_families() -> bytes:
    """8.6.3 Table 61: one ColorSpace resource per family and one filled rectangle each (8.6.4 to 8.6.6), in reading order:
    CS0 DeviceGray 0.5; CS1 DeviceRGB 1 0 0; CS2 DeviceCMYK 0 1 0 0; CS3 CalGray (D65, gamma 2.2) 0.5; CS4 CalRGB (D65, sRGB
    primaries, gamma 2.2) 0 0 1; CS5 Lab (D50) 50 60 40; CS6 ICCBased RGB (an ICC v2 mntr matrix/TRC profile, /Alternate
    /DeviceRGB) 0 1 0; CS7 ICCBased GRAY (gamma 2.2, no Alternate) 0.25; CS8 Indexed DeviceRGB, index 2 of red, green, blue;
    CS9 Separation /Spot to DeviceCMYK (Type 2, C1 [0 0.4 1 0]) at 1; CS10 DeviceN [/Cyan /Magenta] to DeviceCMYK (Type 4
    {0 0}) at 1 0.4 (both CMYK values are IT8.7/3 patches measured in CGATS TR 001); CS11 [/Pattern /DeviceRGB] with the uncoloured tiling pattern P0 (a 10 x 10 cell, half filled) in 0 0.5 0."""
    entries = [(b"CS%d" % i, colour) for i, colour in enumerate([
        b"0.5 sc", b"1 0 0 sc", b"0 1 0 0 sc", b"0.5 sc", b"0 0 1 sc", b"50 60 40 sc", b"0 1 0 scn", b"0.25 scn",
        b"2 sc", b"1 scn", b"1 0.4 scn", b"0 0.5 0 /P0 scn"])]
    spaces = [
        b"/DeviceGray", b"/DeviceRGB", b"/DeviceCMYK",
        b"[/CalGray << " + D65 + b" /Gamma 2.2 >>]",
        b"[/CalRGB << " + D65 + b" /Gamma [2.2 2.2 2.2] " + SRGB_MATRIX + b" >>]",
        b"[/Lab << /WhitePoint [0.9642 1 0.8249] /Range [-128 127 -128 127] >>]",
        b"[/ICCBased 6 0 R]", b"[/ICCBased 7 0 R]",
        b"[/Indexed /DeviceRGB 2 <FF0000 00FF00 0000FF>]",
        b"[/Separation /Spot /DeviceCMYK << /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 0.4 1 0] /N 1 >>]",
        b"[/DeviceN [/Cyan /Magenta] /DeviceCMYK 8 0 R]",
        b"[/Pattern /DeviceRGB]",
    ]
    resources = (b"<< /ColorSpace << " + b" ".join(b"/CS%d %s" % (i, s) for i, s in enumerate(spaces))
                 + b" >> /Pattern << /P0 5 0 R >> >>")
    rgb = icc_rgb_profile()
    gray = icc_gray_profile()
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources " + resources)),
        (4, stream(b"", colour_rects(entries))),
        (5, stream(b"/PatternType 1 /PaintType 2 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10 /Resources << >>",
                   b"0 0 10 5 re f")),
        (6, stream(b"/N 3 /Alternate /DeviceRGB", rgb)),
        (7, stream(b"/N 1", gray)),
        (8, stream(b"/FunctionType 4 /Domain [0 1 0 1] /Range [0 1 0 1 0 1 0 1]", b"{0 0}")),
    ], binary=True)


def gen_color_operators() -> bytes:
    """8.6.8 Table 73: all twelve colour operators, each followed by a filled and stroked rectangle (B), in reading order:
    (1) 0.25 G 0.75 g; (2) 1 0 0 RG 0 0 1 rg; (3) 0 0 0 1 K 0 1 0 0 k; (4) /CS0 CS 0.2 SC /CS0 cs 0.8 sc with CS0 CalGray;
    (5) /CS1 CS /CS1 cs with CS1 Separation (CS resets both colours to the initial tint 1.0); (6) /CS1 CS 0.5 SCN /CS1 cs
    0.25 scn; (7) /CS2 CS 0 0 1 /P0 SCN /CS2 cs 1 0 0 /P0 scn with CS2 [/Pattern /DeviceRGB] and the uncoloured tiling pattern
    P0; (8) /DeviceCMYK CS /DeviceRGB cs (initial colours 0 0 0 1 and 0 0 0). Then an inline image (8.9.7 Tables 91-92) in
    the abbreviated Indexed space /CS [/I /RGB 1 <FF0000 0000FF>], two pixels: red, blue."""
    ops = [b"0.25 G 0.75 g", b"1 0 0 RG 0 0 1 rg", b"0 0 0 1 K 0 1 0 0 k", b"/CS0 CS 0.2 SC /CS0 cs 0.8 sc",
           b"/CS1 CS /CS1 cs", b"/CS1 CS 0.5 SCN /CS1 cs 0.25 scn", b"/CS2 CS 0 0 1 /P0 SCN /CS2 cs 1 0 0 /P0 scn",
           b"/DeviceCMYK CS /DeviceRGB cs"]
    content = b"8 w\n"
    for i, op in enumerate(ops):
        x = 40 + (i % 4) * 140
        y = 640 - (i // 4) * 140
        content += b"%s %d %d 100 100 re B\n" % (op, x, y)
    content += b"q 200 0 0 100 40 300 cm BI /W 2 /H 1 /CS [/I /RGB 1 <FF0000 0000FF>] /BPC 8 ID \x00\x01 EI Q\n"
    resources = (b"<< /ColorSpace << /CS0 [/CalGray << " + D65 + b" >>] "
                 b"/CS1 [/Separation /Spot /DeviceGray << /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>] "
                 b"/CS2 [/Pattern /DeviceRGB] >> /Pattern << /P0 5 0 R >> >>")
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources " + resources)),
        (4, stream(b"", content)),
        (5, stream(b"/PatternType 1 /PaintType 2 /TilingType 1 /BBox [0 0 10 10] /XStep 10 /YStep 10 /Resources << >>",
                   b"0 0 10 5 re f")),
    ], binary=True)


def gen_default_colorspaces() -> bytes:
    """8.6.5.6 default colour spaces. The page's ColorSpace resources have DefaultRGB = CalRGB (D50 white, gamma 1, the sRGB
    primaries adapted to D50) and DefaultGray = CalGray (D50, gamma 1), so a remapped 0.5 is linear and shows as sRGB 188, a
    device 0.5 as 128. Reading order: (1) 0.5 0.5 0.5 rg rectangle; (2) 0.5 g rectangle; (3) the Form XObject Fm0, whose own
    Resources have DefaultRGB = CalRGB (D65, gamma 2.2, sRGB primaries), painting 0.5 0.5 0.5 rg (inside it its DefaultRGB
    applies, found at paint time: 128); (4) Im0, a 2 x 1 image in [/Indexed /DeviceRGB 1 <FF0000 808080>] (the base DeviceRGB
    remapped to the page's DefaultRGB), pixels 0 and 1."""
    d50 = b"/WhitePoint [0.9642 1 0.8249]"
    d50_matrix = b"/Matrix [0.4361 0.2225 0.0139 0.3851 0.7169 0.0971 0.1431 0.0606 0.7141]"
    content = (b"0.5 0.5 0.5 rg 40 640 100 100 re f\n"
               b"0.5 g 180 640 100 100 re f\n"
               b"q 1 0 0 1 320 640 cm /Fm0 Do Q\n"
               b"q 100 0 0 100 460 640 cm /Im0 Do Q\n")
    resources = (b"<< /ColorSpace << /DefaultRGB [/CalRGB << " + d50 + b" " + d50_matrix + b" >>] "
                 b"/DefaultGray [/CalGray << " + d50 + b" >>] >> /XObject << /Fm0 5 0 R /Im0 6 0 R >> >>")
    form_resources = b"<< /ColorSpace << /DefaultRGB [/CalRGB << " + D65 + b" /Gamma [2.2 2.2 2.2] " + SRGB_MATRIX + b" >>] >> >>"
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources " + resources)),
        (4, stream(b"", content)),
        (5, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources " + form_resources,
                   b"0.5 0.5 0.5 rg 0 0 100 100 re f")),
        (6, stream(b"/Type /XObject /Subtype /Image /Width 2 /Height 1 /BitsPerComponent 8 "
                   b"/ColorSpace [/Indexed /DeviceRGB 1 <FF0000 808080>]", b"\x00\x01")),
    ], binary=True)


def gen_separation_special() -> bytes:
    """8.6.6.4 and 8.6.6.5 special colourant names, one rectangle each at tint 0.5 over a light gray (0.8) band: CS0 Separation
    /All (every colourant; on an RGB device 1 - tint on every component, a 50% gray); CS1 Separation /None (paints nothing, the
    band shows through); CS2 DeviceN [/None /None] (never paints). The tint transforms, which would paint white, are ignored."""
    tint = b"<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [1] /N 1 >>"
    content = (b"0.8 g 0 600 612 160 re f\n"
               + colour_rects([(b"CS0", b"0.5 scn"), (b"CS1", b"0.5 scn"), (b"CS2", b"0.5 0.5 scn")]))
    resources = (b"<< /ColorSpace << /CS0 [/Separation /All /DeviceGray " + tint + b"] /CS1 [/Separation /None /DeviceGray "
                 + tint + b"] /CS2 [/DeviceN [/None /None] /DeviceGray 5 0 R] >> >>")
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources " + resources)),
        (4, stream(b"", content)),
        (5, stream(b"/FunctionType 4 /Domain [0 1 0 1] /Range [0 1]", b"{pop pop 1}")),
    ])

# ---------------------------------------------------------------------------
# Shadings and patterns (clause 8.7)
# ---------------------------------------------------------------------------

class BitWriter:
    """Packs unsigned fields most significant bit first (8.7.4.5.5: mesh data is a bit stream)."""

    def __init__(self) -> None:
        self.bits: list[int] = []

    def write(self, value: int, width: int) -> None:
        assert 0 <= value < (1 << width), (value, width)
        for i in range(width - 1, -1, -1):
            self.bits.append((value >> i) & 1)

    def align(self) -> None:
        while len(self.bits) % 8:
            self.bits.append(0)

    def data(self) -> bytes:
        self.align()
        return bytes(int("".join(map(str, self.bits[i:i + 8])), 2) for i in range(0, len(self.bits), 8))


def mesh_raw(value: float, low: float, high: float, bits: int) -> int:
    """The inverse of the 8.9.5.2 Decode formula: the raw field that decodes to ``value``."""
    return round((value - low) * ((1 << bits) - 1) / (high - low))


def shading_page(shading: bytes, clip: bytes = b"0 0 612 792", extra_objects: list[tuple[int, bytes]] | None = None,
                 version: str = "1.7") -> bytes:
    """One page painting shading 5 0 R (8.7.4.2 sh) inside a rectangular clip: q <clip> re W n /Sh0 sh Q."""
    content = b"q %s re W n /Sh0 sh Q\n" % clip
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources << /Shading << /Sh0 5 0 R >> >>")),
        (4, stream(b"", content)),
        (5, shading),
    ] + (extra_objects or []), version=version, binary=True)


def gen_shading_type1() -> bytes:
    """8.7.4.5.2 Table 78 function-based shading: DeviceRGB, Domain [0 1 0 1] mapped by Matrix [200 0 0 200 100 400] onto
    the square 100..300 x 400..600; Function a 7.10.2 Type 0 sampled function with 2 inputs and 3 outputs, 2 x 2 samples at
    8 bits: (0,0) red, (1,0) green, (0,1) blue, (1,1) white, interpolated bilinearly."""
    samples = bytes([255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255])
    return shading_page(
        b"<< /ShadingType 1 /ColorSpace /DeviceRGB /Domain [0 1 0 1] /Matrix [200 0 0 200 100 400] /Function 6 0 R >>",
        extra_objects=[(6, stream(b"/FunctionType 0 /Domain [0 1 0 1] /Range [0 1 0 1 0 1] /Size [2 2] /BitsPerSample 8",
                                  samples))])


def gen_shading_type2() -> bytes:
    """8.7.4.5.3 Table 79 axial shading: Coords [72 400 540 400], Domain default [0 1], a 7.10.3 Type 2 function from red to
    blue, Extend [true false]: left of x = 72 is red, right of x = 540 unpainted; clipped to 0 300 612 200."""
    return shading_page(
        b"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [72 400 540 400] "
        b"/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> /Extend [true false] >>",
        clip=b"0 300 612 200")


def gen_shading_type3() -> bytes:
    """8.7.4.5.4 Table 80 radial shading with non-nested circles (a cone): Coords [200 400 20 400 420 100], red to blue,
    Extend [true true]. Where blend circles overlap, the greatest s decides the colour."""
    return shading_page(
        b"<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [200 400 20 400 420 100] "
        b"/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> /Extend [true true] >>",
        clip=b"0 200 612 400")


def gen_shading_type4() -> bytes:
    """8.7.4.5.5 Table 81 free-form triangle mesh: BitsPerFlag 2, BitsPerCoordinate 12, BitsPerComponent 4, DeviceRGB, so a
    vertex is 2 + 12 + 12 + 3 x 4 = 38 bits padded to 40. Flags 0 0 0 1 2 make three triangles: (v0 v1 v2), (v1 v2 v3) and
    (v1 v3 v4). Decode [0 4095 0 4095 0 1 0 1 0 1] so raw coordinates are user-space units."""
    vertices = [(0, 100, 100, (15, 0, 0)), (0, 300, 100, (0, 15, 0)), (0, 200, 300, (0, 0, 15)),
                (1, 400, 300, (15, 0, 0)), (2, 300, 500, (0, 15, 0))]
    w = BitWriter()
    for flag, x, y, rgb in vertices:
        w.write(flag, 2)
        w.write(x, 12)
        w.write(y, 12)
        for c in rgb:
            w.write(c, 4)
        w.align()
    return shading_page(stream(
        b"/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 12 /BitsPerComponent 4 /BitsPerFlag 2 "
        b"/Decode [0 4095 0 4095 0 1 0 1 0 1]", w.data()))


def gen_shading_type5() -> bytes:
    """8.7.4.5.6 Table 82 lattice-form triangle mesh: VerticesPerRow 3, three rows, BitsPerCoordinate 12, BitsPerComponent 4
    with a Type 2 Function of t (red to blue), so a vertex is 12 + 12 + 4 = 28 bits padded to 32 (pdf.js does not pad).
    Rows at y 100, 250, 400, columns at x 100, 250, 400; t = (column + row) / 4 encoded in 4 bits."""
    w = BitWriter()
    for row in range(3):
        for column in range(3):
            w.write(100 + 150 * column, 12)
            w.write(100 + 150 * row, 12)
            w.write(mesh_raw((column + row) / 4, 0, 1, 4), 4)
            w.align()
    return shading_page(stream(
        b"/ShadingType 5 /ColorSpace /DeviceRGB /BitsPerCoordinate 12 /BitsPerComponent 4 /VerticesPerRow 3 "
        b"/Decode [0 4095 0 4095 0 1] /Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>", w.data()))


# Four patches sharing edges through flags 0, 1, 2 and 3 (8.7.4.5.7 Table 84): corners (p00, p03, p33, p30), straight edges.
PATCH_CORNERS = [((100, 100), (100, 250), (250, 250), (250, 100)),
                 ((100, 250), (250, 250), (250, 400), (100, 400)),
                 ((250, 400), (100, 400), (100, 550), (250, 550)),
                 ((250, 550), (250, 400), (400, 400), (400, 550))]
# Stream order of the control points by (column i, row j): 12 for Coons (8.7.4.5.7), 16 for tensor (8.7.4.5.8, Table 85).
RING = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 3), (2, 3), (3, 3), (3, 2), (3, 1), (3, 0), (2, 0), (1, 0)]
INNER = [(1, 1), (1, 2), (2, 2), (2, 1)]


def patch_point(corners, i: int, j: int) -> tuple[float, float]:
    """p_ij of a straight-edged patch: the bilinear blend of its corners at u = i/3, v = j/3."""
    (p00, p03, p33, p30) = corners
    u, v = i / 3, j / 3
    x = (1 - u) * ((1 - v) * p00[0] + v * p03[0]) + u * ((1 - v) * p30[0] + v * p33[0])
    y = (1 - u) * ((1 - v) * p00[1] + v * p03[1]) + u * ((1 - v) * p30[1] + v * p33[1])
    return x, y


def patch_mesh(tensor: bool, bits_coordinate: int, bits_component: int, colours) -> bytes:
    """Patch mesh data for PATCH_CORNERS with flags 0 1 2 3: a flag-0 patch writes every point and four colours, the others
    skip the four points and two colours shared with the previous patch (Tables 84 and 85). No per-patch padding."""
    order = RING + (INNER if tensor else [])
    w = BitWriter()
    for flag, corners in enumerate(PATCH_CORNERS):
        w.write(flag, 8)
        points = order if flag == 0 else order[4:]
        for (i, j) in points:
            x, y = patch_point(corners, i, j)
            w.write(mesh_raw(x, 0, 1000, bits_coordinate), bits_coordinate)
            w.write(mesh_raw(y, 0, 1000, bits_coordinate), bits_coordinate)
        for colour in (colours[flag] if flag == 0 else colours[flag][2:]):
            for c in colour:
                w.write(c, bits_component)
    return w.data()


def gen_shading_type6() -> bytes:
    """8.7.4.5.7 Tables 83-84 Coons patch mesh: four patches with flags 0, 1, 2, 3; BitsPerCoordinate 32 (Decode 0..1000),
    BitsPerComponent 16, BitsPerFlag 8, DeviceRGB. Corner colours (c1..c4 at p00 p03 p33 p30) per patch; implicit ones repeat."""
    r, g, b, k = (65535, 0, 0), (0, 65535, 0), (0, 0, 65535), (0, 0, 0)
    colours = [[r, g, b, k], [g, b, r, g], [r, g, b, r], [b, r, g, b]]
    return shading_page(stream(
        b"/ShadingType 6 /ColorSpace /DeviceRGB /BitsPerCoordinate 32 /BitsPerComponent 16 /BitsPerFlag 8 "
        b"/Decode [0 1000 0 1000 0 1 0 1 0 1]", patch_mesh(False, 32, 16, colours)))


def gen_shading_type7() -> bytes:
    """8.7.4.5.8 Table 85 tensor-product patch mesh: four patches with flags 0, 1, 2, 3; BitsPerCoordinate 24 (Decode
    0..1000), BitsPerComponent 8, BitsPerFlag 8, DeviceCMYK; the 16 points in stream order 1 p00 ... 16 p21."""
    c, m, y, k = (255, 0, 0, 0), (0, 255, 0, 0), (0, 0, 255, 0), (0, 0, 0, 255)
    colours = [[c, m, y, k], [m, y, c, m], [c, m, y, c], [y, c, m, y]]
    return shading_page(stream(
        b"/ShadingType 7 /ColorSpace /DeviceCMYK /BitsPerCoordinate 24 /BitsPerComponent 8 /BitsPerFlag 8 "
        b"/Decode [0 1000 0 1000 0 1 0 1 0 1 0 1]", patch_mesh(True, 24, 8, colours)))


def pattern_page(content: bytes, resources: bytes, objects: list[tuple[int, bytes]]) -> bytes:
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources " + resources)),
        (4, stream(b"", content)),
    ] + objects, binary=True)


def gen_pattern_tiling_colored() -> bytes:
    """8.7.3.1 Table 74 and 8.7.3.2 coloured tiling pattern: PaintType 1, TilingType 1, BBox [0 0 20 20], XStep 25, YStep 25,
    Matrix [1 0 0 1 10 10]; the cell paints a red and a blue square with rg. The page scales by 2 (cm) before the fill: the
    pattern is not scaled, because the pattern matrix maps to the page's default space (8.7.2)."""
    return pattern_page(
        b"2 0 0 2 0 0 cm /Pattern cs /P1 scn 0 0 200 200 re f\n",
        b"<< /Pattern << /P1 5 0 R >> >>",
        [(5, stream(b"/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 20 20] /XStep 25 /YStep 25 "
                    b"/Matrix [1 0 0 1 10 10] /Resources << >>",
                    b"1 0 0 rg 0 0 10 10 re f 0 0 1 rg 10 10 10 10 re f"))])


def gen_pattern_tiling_uncolored() -> bytes:
    """8.7.3.3 uncoloured tiling pattern: PaintType 2, colour space /Cs1 [/Pattern /DeviceRGB], 0.8 0.2 0 /P1 scn; the cell
    has no colour operators (a stencil painted in the underlying colour)."""
    return pattern_page(
        b"/Cs1 cs 0.8 0.2 0 /P1 scn 50 50 300 300 re f\n",
        b"<< /ColorSpace << /Cs1 [/Pattern /DeviceRGB] >> /Pattern << /P1 5 0 R >> >>",
        [(5, stream(b"/Type /Pattern /PatternType 1 /PaintType 2 /TilingType 2 /BBox [0 0 12 12] /XStep 15 /YStep 15 "
                    b"/Resources << >>", b"0 0 m 12 0 l 6 12 l f"))])


def gen_pattern_shading() -> bytes:
    """8.7.4.1 Table 75 shading pattern: PatternType 2, Matrix [0.5 0 0 0.5 0 0], an axial shading with a Background (light
    gray, 8.7.4.3 Table 77) and an ExtGState with CA 0.5; the page fills a rectangle with it (re f)."""
    return pattern_page(
        b"/Pattern cs /P1 scn 50 50 500 500 re f\n",
        b"<< /Pattern << /P1 5 0 R >> >>",
        [(5, b"<< /Type /Pattern /PatternType 2 /Matrix [0.5 0 0 0.5 0 0] /Shading 6 0 R /ExtGState << /CA 0.5 >> >>"),
         (6, b"<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [200 0 800 0] /Background [0.9 0.9 0.9] "
             b"/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> >>")])


def gen_pattern_in_form() -> bytes:
    """8.7.2 pattern in a form XObject: the form (Matrix [0.5 0 0 0.5 100 100]) has its own Pattern resource and fills with
    it; the page paints the form after 1 0 0 1 50 50 cm, so the pattern matrix maps to the form's space at Do."""
    return pattern_page(
        b"1 0 0 1 50 50 cm /Fm0 Do\n",
        b"<< /XObject << /Fm0 5 0 R >> >>",
        [(5, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 400 400] /Matrix [0.5 0 0 0.5 100 100] "
                    b"/Resources << /Pattern << /P1 6 0 R >> >>", b"/Pattern cs /P1 scn 0 0 400 400 re f")),
         (6, stream(b"/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 20 20] /XStep 20 /YStep 20 "
                    b"/Resources << >>", b"0 0.5 0 rg 0 0 10 10 re f"))])


def gen_pattern_recursive() -> bytes:
    """Broken (8.7.3.1): the tiling pattern's cell fills with the pattern itself, an endless recursion."""
    return pattern_page(
        b"/Pattern cs /P1 scn 0 0 200 200 re f\n",
        b"<< /Pattern << /P1 5 0 R >> >>",
        [(5, stream(b"/Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 20 20] /XStep 20 /YStep 20 "
                    b"/Resources << /Pattern << /P1 5 0 R >> >>", b"/Pattern cs /P1 scn 0 0 10 10 re f"))])


def gen_shading_mesh_truncated() -> bytes:
    """Broken (8.7.4.5.5): a Type 4 mesh with two whole triangles (flags 0 0 0 1) whose data stops in the middle of a fifth
    vertex (flag 2): 5 bytes per vertex, 22 bytes in all."""
    vertices = [(0, 100, 100, (15, 0, 0)), (0, 300, 100, (0, 15, 0)), (0, 200, 300, (0, 0, 15)),
                (1, 400, 300, (15, 0, 0)), (2, 300, 500, (0, 15, 0))]
    w = BitWriter()
    for flag, x, y, rgb in vertices:
        w.write(flag, 2)
        w.write(x, 12)
        w.write(y, 12)
        for c in rgb:
            w.write(c, 4)
        w.align()
    return shading_page(stream(
        b"/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 12 /BitsPerComponent 4 /BitsPerFlag 2 "
        b"/Decode [0 4095 0 4095 0 1 0 1 0 1]", w.data()[:22]))




# Content streams: text, form XObjects, graphics state parameters, marked content (issue #56)

def gen_form_xobject_nested() -> bytes:
    """8.10.1 form XObjects: the page paints form A (/Matrix [2 0 0 2 100 100], /BBox [0 0 50 50], its own
    /Resources with form B and font F1, /StructParents 3); A paints form B, which has no /Resources and so
    uses A's (7.8.3: F1 resolves through them); then the page paints form C, whose own resources name C
    itself: a cycle (one ContentFormCycle diagnostic, C's content runs once)."""
    content = b"q /FA Do Q /FC Do\n"
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources << /XObject << /FA 5 0 R /FC 7 0 R >> >>")),
        (4, stream(b"", content)),
        (5, stream(b"/Type /XObject /Subtype /Form /Matrix [2 0 0 2 100 100] /BBox [0 0 50 50] /StructParents 3"
                   b" /Resources << /XObject << /FB 6 0 R >> /Font << /F1 8 0 R >> >>",
                   b"0 0 10 10 re f /FB Do")),
        (6, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 20 20]",
                   b"BT /F1 12 Tf 1 2 Td (B) Tj ET 5 5 m 6 6 l S")),
        (7, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 30 30] /Resources << /XObject << /FC 7 0 R >> >>",
                   b"1 1 m 2 2 l S /FC Do")),
        (8, HELVETICA),
    ])


def gen_extgstate_params() -> bytes:
    """8.4.5 Table 57 graphics state parameter dictionaries (PDF 2.0): GS1 sets LW LC LJ ML D RI OP op OPM Font FL SA
    BM CA ca AIS TK UseBlackPtComp and a Luminosity soft mask (11.6.5.1 Table 142) whose group G (11.6.6) is a
    DeviceGray transparency group, with BC and TR /Identity; GS2 sets SM, HTO, BG2/UCR2 (Type 2 functions, 7.10.3),
    TR2 /Identity, HT /Default, a deprecated blend-mode array whose first known name is Screen, and SMask /None.
    The first rectangle is painted under GS1 with the CTM scaled by 2 (the soft mask records that CTM), the second
    after Q under GS1 then GS2."""
    content = b"q 2 0 0 2 0 0 cm /GS1 gs 0 0 10 10 re f Q /GS1 gs /GS2 gs 20 20 10 10 re S\n"
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources << /ExtGState << /GS1 5 0 R /GS2 6 0 R >> >>")),
        (4, stream(b"", content)),
        (5, b"<< /Type /ExtGState /LW 3 /LC 1 /LJ 2 /ML 5 /D [[4 2] 1] /RI /Saturation /OP true /op false /OPM 1"
            b" /Font [9 0 R 14] /FL 2 /SA true /BM /Multiply /CA 0.5 /ca 0.25 /AIS true /TK false /UseBlackPtComp /ON"
            b" /SMask << /Type /Mask /S /Luminosity /G 7 0 R /BC [0.5] /TR /Identity >> >>"),
        (6, b"<< /Type /ExtGState /SM 0.5 /HTO [1 2] /BG2 8 0 R /UCR2 8 0 R /TR2 /Identity /HT /Default"
            b" /BM [/BroadsideUnknown /Screen] /SMask /None >>"),
        (7, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 10 10] /Group << /S /Transparency /CS /DeviceGray >>",
                   b"0.5 g 0 0 10 10 re f")),
        (8, b"<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >>"),
        (9, HELVETICA),
    ], version="2.0", trailer_extra=b" /ID [<%s> <%s>]" % (file_id("extgstate-params").hex().encode(), file_id("extgstate-params").hex().encode()))


def gen_marked_content() -> bytes:
    """14.6 marked content: BMC /Artifact; BDC /P with an inline <</MCID 0>>; BDC /Span naming /P1 in the page's
    /Properties (MCID 1, Lang); MP /Pt; DP /Pt2 with an inline property list; BDC /OC naming an optional content
    group that is OFF in the default configuration (8.11.3.2: its rectangle is hidden); BDC /Span with an inline
    /ActualText (x). One text line in each of the /P and the last /Span sequences."""
    content = (b"/Artifact BMC 0 0 1 1 re f EMC\n"
               b"/P << /MCID 0 >> BDC BT /F1 12 Tf 72 700 Td (a) Tj ET EMC\n"
               b"/Span /P1 BDC 0 0 2 2 re f EMC\n"
               b"/Pt MP\n"
               b"/Pt2 << /X 1 >> DP\n"
               b"/OC /oc1 BDC 0 0 3 3 re f EMC\n"
               b"/Span << /ActualText (x) >> BDC BT /F1 12 Tf 72 680 Td (y) Tj ET EMC\n")
    return simple_file([
        (1, catalog(b" /OCProperties << /OCGs [6 0 R] /D << /OFF [6 0 R] >> >>")),
        (2, pages()),
        (3, page(contents=4, resources=False,
                 extra=b" /Resources << /Font << /F1 7 0 R >> /Properties << /P1 5 0 R /oc1 6 0 R >> >>")),
        (4, stream(b"", content)),
        (5, b"<< /MCID 1 /Lang (en) >>"),
        (6, b"<< /Type /OCG /Name (Hidden layer) >>"),
        (7, HELVETICA),
    ])
# ---------------------------------------------------------------------------
# Images (clause 8.9): one masking mode or sample depth per file
# ---------------------------------------------------------------------------

def pack_samples(rows: list[list[int]], bpc: int) -> bytes:
    """8.9.3 sample layout: each row's samples (components interleaved) packed MSB-first at ``bpc`` bits, 16-bit samples
    big-endian, every row starting on a byte boundary. Padding bits are set to 1 so a reader that uses them is caught."""
    out = bytearray()
    for row in rows:
        if bpc == 16:
            out += b"".join(struct.pack(">H", v) for v in row)
            continue
        acc = 0
        nbits = 0
        for v in row:
            assert 0 <= v < (1 << bpc)
            acc = (acc << bpc) | v
            nbits += bpc
            while nbits >= 8:
                nbits -= 8
                out.append((acc >> nbits) & 0xFF)
        if nbits:
            pad = 8 - nbits
            out.append(((acc << pad) | ((1 << pad) - 1)) & 0xFF)
    return bytes(out)


def image_page(objects: list[tuple[int, bytes]], version: str = "1.7", fill: bytes = b"") -> bytes:
    """A Letter page whose resources name object 5 as the XObject ``/Im0`` and whose content paints it into a 100 x 100 square at
    (72, 600), after ``fill``; ``objects`` are object 5 and anything it refers to."""
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources << /XObject << /Im0 5 0 R >> >>")),
        (4, stream(b"", fill + b"q 100 0 0 100 72 600 cm /Im0 Do Q")),
    ] + objects, version=version, binary=True)


def one_image(entries: bytes, data: bytes, version: str = "1.7", fill: bytes = b"") -> bytes:
    """A page painting one image XObject (object 5) with the dictionary entries ``entries`` and the data ``data``."""
    return image_page([(5, stream(b"/Type /XObject /Subtype /Image " + entries, data))], version, fill)


STENCIL_ARROW = [
    [0, 0, 0, 1, 0, 0, 0, 0],
    [0, 0, 0, 1, 1, 0, 0, 0],
    [1, 1, 1, 1, 1, 1, 0, 0],
    [1, 1, 1, 1, 1, 1, 1, 0],
    [1, 1, 1, 1, 1, 1, 0, 0],
    [0, 0, 0, 1, 1, 0, 0, 0],
    [0, 0, 0, 1, 0, 0, 0, 0],
    [0, 0, 0, 0, 0, 0, 0, 0],
]


def gen_image_stencil_mask() -> bytes:
    """8.9.6.2 stencil mask: an 8 x 8 /ImageMask true /Decode [1 0] (no ColorSpace, no BitsPerComponent) right-pointing arrow,
    painted in red: with Decode [1 0] a sample 1 paints and 0 leaves the backdrop."""
    return one_image(b"/Width 8 /Height 8 /ImageMask true /Decode [1 0]", pack_samples(STENCIL_ARROW, 1),
                     fill=b"1 0 0 rg ")


def rgb_ramp_4x4() -> list[list[int]]:
    """A 4 x 4 RGB image with every pixel distinct: R = 60 x, G = 60 y, B = 255 - 16 (4y + x)."""
    return [[c for x in range(4) for c in (60 * x, 60 * y, 255 - 16 * (4 * y + x))] for y in range(4)]


def gen_image_explicit_mask() -> bytes:
    """8.9.6.3 explicit masking: a 4 x 4 DeviceRGB 8-bit image whose /Mask is a 16 x 16 image mask (a disc of radius 7 about the
    centre; sample 0, inside, shows the image) at a different resolution."""
    disc = [[0 if (x - 7.5) ** 2 + (y - 7.5) ** 2 <= 49 else 1 for x in range(16)] for y in range(16)]
    return image_page([
        (5, stream(b"/Type /XObject /Subtype /Image /Width 4 /Height 4 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask 6 0 R",
                   pack_samples(rgb_ramp_4x4(), 8))),
        (6, stream(b"/Type /XObject /Subtype /Image /Width 16 /Height 16 /ImageMask true", pack_samples(disc, 1))),
    ])


COLOR_KEY_PIXELS = [
    [(255, 0, 0), (252, 3, 1), (249, 0, 0), (255, 6, 0)],
    [(0, 0, 255), (255, 0, 0), (0, 255, 0), (250, 5, 5)],
    [(128, 128, 128), (0, 0, 0), (255, 255, 255), (251, 2, 4)],
    [(10, 20, 30), (40, 50, 60), (255, 0, 6), (250, 0, 0)],
]


def gen_image_color_key_mask() -> bytes:
    """8.9.6.4 colour key masking: a 4 x 4 DeviceRGB 8-bit image with /Mask [250 255 0 5 0 5], which masks out every pixel whose
    red is 250-255 and green and blue 0-5 (exact red, (252 3 1), (250 5 5) ...) but not (249 0 0), (255 6 0) or (255 0 6). A blue
    square is painted first, so the masked pixels show blue."""
    rows = [[c for px in row for c in px] for row in COLOR_KEY_PIXELS]
    return one_image(b"/Width 4 /Height 4 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask [250 255 0 5 0 5]",
                     pack_samples(rows, 8), fill=b"0 0 1 rg 72 600 100 100 re f ")


def gen_image_smask() -> bytes:
    """11.6.5.2 soft-mask image: a 4 x 4 DeviceRGB 8-bit image (Flate) whose /SMask is a 2 x 8 DeviceGray 8-bit image (Flate),
    alpha 32 y + 16 x, at a different width and height from its parent."""
    alpha = [[32 * y + 16 * x for x in range(2)] for y in range(8)]
    return image_page([
        (5, stream(b"/Type /XObject /Subtype /Image /Width 4 /Height 4 /ColorSpace /DeviceRGB /BitsPerComponent 8 "
                   b"/SMask 6 0 R /Filter /FlateDecode", flate(pack_samples(rgb_ramp_4x4(), 8)))),
        (6, stream(b"/Type /XObject /Subtype /Image /Width 2 /Height 8 /ColorSpace /DeviceGray /BitsPerComponent 8 "
                   b"/Filter /FlateDecode", flate(pack_samples(alpha, 8)))),
    ])


MATTE_ORIGINAL = [(10, 20, 30), (0, 128, 255), (200, 100, 50), (40, 80, 160)]
MATTE_ALPHA = [0, 64, 128, 255]


def gen_image_smask_matte() -> bytes:
    """11.6.5.2 Table 144 Matte: a 2 x 2 DeviceRGB image pre-blended against /Matte [1 1 1] with the alphas 0, 64, 128, 255 of its
    same-size SMask: c' = m + alpha (c - m), rounded to 8 bits, from the colours MATTE_ORIGINAL (the alpha 0 pixel is any colour:
    it pre-blends to the matte)."""
    blended = [[int(255 + a / 255 * (c - 255) + 0.5) for c in px] for px, a in zip(MATTE_ORIGINAL, MATTE_ALPHA)]
    rows = [blended[0] + blended[1], blended[2] + blended[3]]
    return image_page([
        (5, stream(b"/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R",
                   pack_samples(rows, 8))),
        (6, stream(b"/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8 "
                   b"/Matte [1 1 1]", pack_samples([MATTE_ALPHA[0:2], MATTE_ALPHA[2:4]], 8))),
    ])


def gen_image_1bpc() -> bytes:
    """8.9.3 sample layout at 1 bit: a 10 x 3 DeviceGray image, 2 bytes per row with 6 padding bits (set to 1); also
    /Interpolate true (8.9.5.3) and /Intent /Perceptual (8.6.5.8)."""
    rows = [[1, 0, 0, 0, 0, 0, 0, 0, 0, 1], [0, 1, 0, 1, 0, 1, 0, 1, 0, 1], [1, 1, 1, 1, 1, 0, 0, 0, 0, 0]]
    return one_image(b"/Width 10 /Height 3 /ColorSpace /DeviceGray /BitsPerComponent 1 /Interpolate true /Intent /Perceptual",
                     pack_samples(rows, 1))


def gen_image_2bpc() -> bytes:
    """8.9.3 sample layout at 2 bits: a 5 x 2 DeviceGray image (values 0-3, gray 0, 85, 170, 255), 2 bytes per row with 6
    padding bits."""
    return one_image(b"/Width 5 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 2",
                     pack_samples([[0, 1, 2, 3, 0], [3, 3, 2, 1, 1]], 2))


INDEXED_PALETTE = bytes(v for i in range(16) for v in (i * 17, 255 - i * 17, (i * 51) % 256))


def gen_image_4bpc_indexed() -> bytes:
    """8.6.6.3 Indexed at 4 bits: a 3 x 2 image in [/Indexed /DeviceRGB 15 <48 bytes>] (default Decode [0 15]); 12 bits per row
    -> 2 bytes with 4 padding bits."""
    return one_image(b"/Width 3 /Height 2 /BitsPerComponent 4 /ColorSpace [/Indexed /DeviceRGB 15 <"
                     + INDEXED_PALETTE.hex().upper().encode() + b">]",
                     pack_samples([[0, 5, 15], [9, 1, 14]], 4))


def gen_image_16bpc() -> bytes:
    """8.9.2 16-bit components (PDF 1.5): a 3 x 2 DeviceRGB image whose samples have distinct high and low bytes (0x12FF,
    0xFF12 ...), so byte order matters."""
    rows = [[0x12FF, 0xFF12, 0x0001, 0x8000, 0x7FFF, 0xFFFF, 0x0000, 0x1234, 0xABCD],
            [0xFFFF, 0x0000, 0x00FF, 0xFF00, 0x4321, 0x8001, 0x0F0F, 0xF0F0, 0x5555]]
    return one_image(b"/Width 3 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 16", pack_samples(rows, 16), version="1.5")


def gen_image_decode_inverted() -> bytes:
    """8.9.5.2 Table 88 Decode: a 4 x 1 DeviceGray 8-bit ramp 0, 85, 170, 255 with /Decode [1 0], which reverses it."""
    return one_image(b"/Width 4 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Decode [1 0]", bytes([0, 85, 170, 255]))


INLINE_FILTERS_SAMPLES = bytes(range(0, 240, 5))


def gen_inline_image_filters() -> bytes:
    """8.9.7 Tables 91-92 abbreviations: an inline 4 x 4 RGB 8-bit image with /F [/AHx /Fl], /DP [null null], /I true
    (Interpolate), /D [1 0 1 0 1 0] and the PDF 2.0 /L (the length of the ASCII hex data, its '>' included). PDF 2.0."""
    data = asciihex_encode(flate(INLINE_FILTERS_SAMPLES))
    content = (b"q 100 0 0 100 72 600 cm BI /W 4 /H 4 /BPC 8 /CS /RGB /F [/AHx /Fl] /DP [null null] /I true "
               b"/D [1 0 1 0 1 0] /L %d ID " % len(data) + data + b" EI Q")
    id0 = file_id("inline-image-filters").hex().encode()
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4)),
        (4, stream(b"", content)),
    ], version="2.0", binary=True, trailer_extra=b" /ID [<%s> <%s>]" % (id0, id0))


INLINE_EI_SAMPLES = b"\nEI Q \n "


def gen_inline_image_ei_in_data() -> bytes:
    """8.9.7: an unfiltered inline 8 x 1 DeviceGray image without /L whose data contains "EI Q" between white-space; only the
    computed data length (W x H x BPC / 8) finds the real EI."""
    content = b"q 100 0 0 10 72 600 cm BI /W 8 /H 1 /CS /G /BPC 8 ID " + INLINE_EI_SAMPLES + b" EI Q"
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4)),
        (4, stream(b"", content)),
    ], binary=True)


# ---------------------------------------------------------------------------
# JPEG 2000 (clause 7.4.9; ITU-T T.800 | ISO/IEC 15444-1): a minimal lossless encoder, the inverse of the reader's
# decoding path: DC level shift, reversible colour transform, 5/3 wavelet, EBCOT tier-1 with the MQ coder, one layer,
# one tile, one precinct, one code-block per sub-band, LRCP packets, and the JP2 boxes around the codestream.
# ---------------------------------------------------------------------------

# T.800 Table C.2: (Qe, NMPS, NLPS, SWITCH) per state.
MQ_STATES = [
    (0x5601, 1, 1, 1), (0x3401, 2, 6, 0), (0x1801, 3, 9, 0), (0x0AC1, 4, 12, 0), (0x0521, 5, 29, 0), (0x0221, 38, 33, 0),
    (0x5601, 7, 6, 1), (0x5401, 8, 14, 0), (0x4801, 9, 14, 0), (0x3801, 10, 14, 0), (0x3001, 11, 17, 0), (0x2401, 12, 18, 0),
    (0x1C01, 13, 20, 0), (0x1601, 29, 21, 0), (0x5601, 15, 14, 1), (0x5401, 16, 14, 0), (0x5101, 17, 15, 0), (0x4801, 18, 16, 0),
    (0x3801, 19, 17, 0), (0x3401, 20, 18, 0), (0x3001, 21, 19, 0), (0x2801, 22, 19, 0), (0x2401, 23, 20, 0), (0x2201, 24, 21, 0),
    (0x1C01, 25, 22, 0), (0x1801, 26, 23, 0), (0x1601, 27, 24, 0), (0x1401, 28, 25, 0), (0x1201, 29, 26, 0), (0x1101, 30, 27, 0),
    (0x0AC1, 31, 28, 0), (0x09C1, 32, 29, 0), (0x08A1, 33, 30, 0), (0x0521, 34, 31, 0), (0x0441, 35, 32, 0), (0x02A1, 36, 33, 0),
    (0x0221, 37, 34, 0), (0x0141, 38, 35, 0), (0x0111, 39, 36, 0), (0x0085, 40, 37, 0), (0x0049, 41, 38, 0), (0x0025, 42, 39, 0),
    (0x0015, 43, 40, 0), (0x0009, 44, 41, 0), (0x0005, 45, 42, 0), (0x0001, 45, 43, 0), (0x5601, 46, 46, 0),
]


class MqEncoder:
    """T.800 C.2: INITENC, ENCODE (CODEMPS, CODELPS), RENORME, BYTEOUT and FLUSH (with SETBITS), software conventions."""

    def __init__(self) -> None:
        self.a = 0x8000
        self.c = 0
        self.ct = 12
        self.out = bytearray([0])  # out[0] is the byte before the first one (BP = BPST - 1)

    def encode(self, cx: list[int], label: int, d: int) -> None:
        state, mps = cx[label] >> 1, cx[label] & 1
        qe, nmps, nlps, switch = MQ_STATES[state]
        self.a -= qe
        if d == mps:
            if self.a & 0x8000:
                self.c += qe
                return
            if self.a < qe:
                self.a = qe
            else:
                self.c += qe
            cx[label] = (nmps << 1) | mps
        else:
            if self.a < qe:
                self.c += qe
            else:
                self.a = qe
            cx[label] = (nlps << 1) | (mps ^ switch)
        while True:
            self.a = (self.a << 1) & 0xFFFF
            self.c = (self.c << 1) & 0xFFFFFFFF
            self.ct -= 1
            if self.ct == 0:
                self._byte_out()
            if self.a & 0x8000:
                break

    def _byte_out(self) -> None:
        if self.out[-1] == 0xFF:
            self.out.append((self.c >> 20) & 0xFF)
            self.c &= 0xFFFFF
            self.ct = 7
        elif not self.c & 0x8000000:
            self.out.append((self.c >> 19) & 0xFF)
            self.c &= 0x7FFFF
            self.ct = 8
        else:
            self.out[-1] += 1
            if self.out[-1] == 0xFF:
                self.c &= 0x7FFFFFF
                self.out.append((self.c >> 20) & 0xFF)
                self.c &= 0xFFFFF
                self.ct = 7
            else:
                self.out.append((self.c >> 19) & 0xFF)
                self.c &= 0x7FFFF
                self.ct = 8

    def flush(self) -> bytes:
        temp = self.c + self.a
        self.c |= 0xFFFF
        if self.c >= temp:
            self.c -= 0x8000
        self.c = (self.c << self.ct) & 0xFFFFFFFF
        self._byte_out()
        self.c = (self.c << self.ct) & 0xFFFFFFFF
        self._byte_out()
        data = bytes(self.out[1:])
        return data[:-1] if data.endswith(b"\xff") else data  # D.4.1: a codeword segment never ends with 0xFF


def _j2k_zc_context(orientation: int, h: int, v: int, d: int) -> int:
    """T.800 Table D.1."""
    if orientation == 3:
        hv = h + v
        if d >= 3:
            return 8
        if d == 2:
            return 7 if hv >= 1 else 6
        if d == 1:
            return 5 if hv >= 2 else 4 if hv == 1 else 3
        return 2 if hv >= 2 else hv
    if orientation == 1:
        h, v = v, h
    if h == 2:
        return 8
    if h == 1:
        return 7 if v >= 1 else 6 if d >= 1 else 5
    return 4 if v == 2 else 3 if v == 1 else 2 if d >= 2 else d


# T.800 Tables D.2 and D.3: (H, V) -> (context label, XOR bit).
J2K_SIGN = {(1, 1): (13, 0), (1, 0): (12, 0), (1, -1): (11, 0), (0, 1): (10, 0), (0, 0): (9, 0),
            (0, -1): (10, 1), (-1, 1): (11, 1), (-1, 0): (12, 1), (-1, -1): (13, 1)}


def j2k_encode_block(coefficients: list[list[int]], orientation: int, mb: int) -> tuple[int, int, bytes]:
    """T.800 Annex D: every coding pass of one code-block (no style bits), one codeword segment. Returns (P, passes, bytes);
    P = Mb when every coefficient is zero (the code-block is then never included)."""
    h = len(coefficients)
    w = len(coefficients[0]) if h else 0
    mag = [[abs(v) for v in row] for row in coefficients]
    neg = [[v < 0 for v in row] for row in coefficients]
    top = max((m for row in mag for m in row), default=0).bit_length()
    assert top <= mb, "a coefficient needs more than Mb magnitude bit-planes"
    if top == 0:
        return mb, 0, b""
    sig = [[False] * w for _ in range(h)]
    visited = [[False] * w for _ in range(h)]
    refined = [[False] * w for _ in range(h)]
    cx = [0] * 19
    cx[0], cx[17], cx[18] = 4 << 1, 3 << 1, 46 << 1  # Table D.7
    mq = MqEncoder()

    def s(y: int, x: int) -> int:
        return 1 if 0 <= y < h and 0 <= x < w and sig[y][x] else 0

    def zc(y: int, x: int) -> int:
        return _j2k_zc_context(orientation, s(y, x - 1) + s(y, x + 1), s(y - 1, x) + s(y + 1, x),
                               s(y - 1, x - 1) + s(y - 1, x + 1) + s(y + 1, x - 1) + s(y + 1, x + 1))

    def contribution(y: int, x: int) -> int:
        return 0 if not s(y, x) else (-1 if neg[y][x] else 1)

    def code_sign(y: int, x: int) -> None:
        hc = max(-1, min(1, contribution(y, x - 1) + contribution(y, x + 1)))
        vc = max(-1, min(1, contribution(y - 1, x) + contribution(y + 1, x)))
        label, xor = J2K_SIGN[(hc, vc)]
        mq.encode(cx, label, (1 if neg[y][x] else 0) ^ xor)

    def bit(y: int, x: int, p: int) -> int:
        return (mag[y][x] >> p) & 1

    def scan():
        for y0 in range(0, h, 4):
            for x in range(w):
                for y in range(y0, min(y0 + 4, h)):
                    yield y, x

    passes = 0
    for p in range(top - 1, -1, -1):
        if p != top - 1:
            for y, x in scan():  # D.3.1 significance propagation
                if not sig[y][x] and zc(y, x):
                    visited[y][x] = True
                    mq.encode(cx, zc(y, x), bit(y, x, p))
                    if bit(y, x, p):
                        code_sign(y, x)
                        sig[y][x] = True
            for y, x in scan():  # D.3.3 magnitude refinement
                if sig[y][x] and not visited[y][x]:
                    if refined[y][x]:
                        label = 16
                    else:
                        label = 15 if any(s(y + dy, x + dx) for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dy or dx) else 14
                    mq.encode(cx, label, bit(y, x, p))
                    refined[y][x] = True
            passes += 2
        for y0 in range(0, h, 4):  # D.3.4 cleanup
            for x in range(w):
                y = y0
                rows = range(y0, min(y0 + 4, h))
                if len(rows) == 4 and all(not sig[r][x] and not visited[r][x] and zc(r, x) == 0 for r in rows):
                    first = next((r for r in rows if bit(r, x, p)), None)
                    if first is None:
                        mq.encode(cx, 17, 0)
                        continue
                    mq.encode(cx, 17, 1)
                    mq.encode(cx, 18, (first - y0) >> 1)
                    mq.encode(cx, 18, (first - y0) & 1)
                    code_sign(first, x)
                    sig[first][x] = True
                    y = first + 1
                for r in range(y, min(y0 + 4, h)):
                    if not sig[r][x] and not visited[r][x]:
                        mq.encode(cx, zc(r, x), bit(r, x, p))
                        if bit(r, x, p):
                            code_sign(r, x)
                            sig[r][x] = True
        passes += 1
        visited = [[False] * w for _ in range(h)]
    return mb - top, passes, mq.flush()


def _pseo(i: int, n: int) -> int:
    """T.800 F-4 for a signal starting at 0: the mirrored index of ``i``."""
    if n == 1:
        return 0
    period = 2 * (n - 1)
    i %= period
    return min(i, period - i)


def j2k_forward_53(x: list[int]) -> list[int]:
    """T.800 F.4.8.2 (F-9, F-10 as corrected in T.800 (2019)): the 5/3 forward lifting of a signal starting at index 0."""
    n = len(x)
    if n == 1:
        return list(x)
    y = list(x)
    for k in range(1, n, 2):
        y[k] = x[k] - ((x[_pseo(k - 1, n)] + x[_pseo(k + 1, n)]) >> 1)
    for k in range(0, n, 2):
        y[k] = x[k] + ((y[_pseo(k - 1, n)] + y[_pseo(k + 1, n)] + 2) >> 2)
    return y


def j2k_bands(samples: list[list[int]], levels: int) -> list[tuple[int, int, list[list[int]]]]:
    """T.800 F.4 (FDWT with origin 0): the sub-bands as (orientation, decomposition level, rows), in the codestream's order
    (LL, then HL, LH, HH from the lowest resolution up)."""
    a = [list(row) for row in samples]
    h, w = len(a), len(a[0])
    found = []
    for level in range(1, levels + 1):
        for x in range(w):  # VER_SD
            col = j2k_forward_53([a[y][x] for y in range(h)])
            for y in range(h):
                a[y][x] = col[y]
        for y in range(h):  # HOR_SD
            a[y][:w] = j2k_forward_53(a[y][:w])
        lw, lh = (w + 1) // 2, (h + 1) // 2
        found.append((level, [
            (1, [[a[y][x] for x in range(1, w, 2)] for y in range(0, h, 2)]),
            (2, [[a[y][x] for x in range(0, w, 2)] for y in range(1, h, 2)]),
            (3, [[a[y][x] for x in range(1, w, 2)] for y in range(1, h, 2)])]))
        low = [[a[y][x] for x in range(0, w, 2)] for y in range(0, h, 2)]
        for y in range(lh):
            a[y][:lw] = low[y]
        w, h = lw, lh
    bands = [(0, levels, [row[:w] for row in a[:h]])]
    for level, details in reversed(found):
        bands += [(orientation, level, rows) for orientation, rows in details]
    return bands


class _BitWriter:
    """T.800 B.10.1: packet header bits, MSB first, a stuffed 0 bit after every 0xFF byte."""

    def __init__(self) -> None:
        self.out = bytearray()
        self.acc = 0
        self.n = 0

    def bits(self, value: int, count: int) -> None:
        for i in range(count - 1, -1, -1):
            self.acc = (self.acc << 1) | ((value >> i) & 1)
            self.n += 1
            if self.n == (7 if self.out and self.out[-1] == 0xFF else 8):
                self.out.append(self.acc)
                self.acc = self.n = 0

    def finish(self) -> bytes:
        if self.n:
            self.bits(0, (7 if self.out and self.out[-1] == 0xFF else 8) - self.n)
        if self.out and self.out[-1] == 0xFF:
            self.out.append(0)
        return bytes(self.out)


def _j2k_passes(w: _BitWriter, n: int) -> None:
    """T.800 Table B.4."""
    if n == 1:
        w.bits(0, 1)
    elif n == 2:
        w.bits(0b10, 2)
    elif n <= 5:
        w.bits(0b1100 | (n - 3), 4)
    elif n <= 36:
        w.bits(0b1111, 4)
        w.bits(n - 6, 5)
    else:
        w.bits(0b111111111, 9)
        w.bits(n - 37, 7)


def j2k_codestream(components: list[list[list[int]]], depth: int, levels: int, mct: bool, guard: int = 2) -> bytes:
    """A lossless codestream (T.800 Annex A) of unsigned ``depth``-bit components: one tile, LRCP, one layer, 64 x 64
    code-blocks (each sub-band one code-block), no precincts, quantization style 0 with exponent depth + log2(gain)."""
    height, width = len(components[0]), len(components[0][0])
    shifted = [[[v - (1 << (depth - 1)) for v in row] for row in comp] for comp in components]
    if mct:  # T.800 G.2.1 forward RCT
        r, g, b = shifted[:3]
        shifted[:3] = [[[(r[y][x] + 2 * g[y][x] + b[y][x]) >> 2 for x in range(width)] for y in range(height)],
                       [[b[y][x] - g[y][x] for x in range(width)] for y in range(height)],
                       [[r[y][x] - g[y][x] for x in range(width)] for y in range(height)]]
    gain = [0, 1, 1, 2]
    exponents = [depth + gain[o] for o, _, _ in j2k_bands(shifted[0], levels)]
    data = bytearray()
    per_component = [j2k_bands(comp, levels) for comp in shifted]
    for res in range(levels + 1):
        for bands in per_component:
            chosen = bands[:1] if res == 0 else bands[1 + 3 * (res - 1):4 + 3 * (res - 1)]
            w = _BitWriter()
            w.bits(1, 1)
            bodies = []
            for orientation, _, rows in chosen:
                mb = guard + depth + gain[orientation] - 1
                if not rows or not rows[0]:
                    continue
                zero, passes, body = j2k_encode_block(rows, orientation, mb)
                if passes == 0:
                    w.bits(0, 1)
                    continue
                w.bits(1, 1)
                w.bits(1, zero + 1)  # tag tree of one node: P zeros, then a one
                _j2k_passes(w, passes)
                lblock, extra = 3, passes.bit_length() - 1
                while len(body) >= 1 << (lblock + extra):
                    w.bits(1, 1)
                    lblock += 1
                w.bits(0, 1)
                w.bits(len(body), lblock + extra)
                bodies.append(body)
            data += w.finish() + b"".join(bodies)
    siz = struct.pack(">HIIIIIIIIH", 0, width, height, 0, 0, width, height, 0, 0, len(components))
    siz += b"".join(bytes([depth - 1, 1, 1]) for _ in components)
    cod = bytes([0, 0]) + struct.pack(">H", 1) + bytes([1 if mct else 0, levels, 4, 4, 0, 1])
    qcd = bytes([guard << 5]) + bytes(e << 3 for e in exponents)

    def segment(marker: int, body: bytes) -> bytes:
        return struct.pack(">HH", marker, len(body) + 2) + body

    tile_header = struct.pack(">HHHIBB", 0xFF90, 10, 0, 14 + len(data), 0, 1) + b"\xff\x93"
    return (b"\xff\x4f" + segment(0xFF51, siz) + segment(0xFF5C, qcd) + segment(0xFF52, cod)
            + tile_header + bytes(data) + b"\xff\xd9")


def jp2_file(codestream: bytes, width: int, height: int, components: int, depth: int, enumerated: int) -> bytes:
    """T.800 Annex I: signature, File Type ('jp2 '), JP2 Header (Image Header, Colour Specification with an enumerated colour
    space) and Contiguous Codestream boxes."""
    def box(kind: bytes, body: bytes) -> bytes:
        return struct.pack(">I", len(body) + 8) + kind + body

    ihdr = struct.pack(">IIHBBBB", height, width, components, depth - 1, 7, 0, 0)
    colr = bytes([1, 0, 0]) + struct.pack(">I", enumerated)
    return (box(b"jP  ", b"\r\n\x87\n") + box(b"ftyp", b"jp2 " + struct.pack(">I", 0) + b"jp2 ")
            + box(b"jp2h", box(b"ihdr", ihdr) + box(b"colr", colr)) + box(b"jp2c", codestream))


def jpx_sample(x: int, y: int, c: int) -> int:
    """The 8-bit source image of jpx-lossless.pdf (the same formula as JpxSamples.Sample in the tests)."""
    return (x * 3 + y * 5 + c * 40 + ((x * x + 3 * y * y + 7 * x * y + 11 * c) % 23)) & 0xFF


def gen_jpx_lossless() -> bytes:
    """7.4.9 JPXDecode: a 17 x 13 8-bit RGB image in a JP2 file (enumerated sRGB), coded losslessly: RCT, two levels of the
    5/3 wavelet, one tile, one layer, LRCP. The dictionary's ColorSpace DeviceRGB wins over the file's colour box and its
    BitsPerComponent is ignored (Table 87). Odd sizes exercise the symmetric extension at both parities."""
    width, height = 17, 13
    comps = [[[jpx_sample(x, y, c) for x in range(width)] for y in range(height)] for c in range(3)]
    data = jp2_file(j2k_codestream(comps, 8, 2, mct=True), width, height, 3, 8, 16)
    return one_image(b"/Width 17 /Height 13 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /JPXDecode", data,
                     version="1.5")


def type3_font(scale: int, char_procs: int, resources: int, tounicode: int) -> bytes:
    """9.6.4 Table 110 Type 3 font dictionary for gen_text_type3(): glyph space is 1000/``scale`` units per em, so the
    FontMatrix is ``scale``/1000 and every width and coordinate is divided by ``scale``."""
    unit = 1000 // scale
    return (b"<< /Type /Font /Subtype /Type3 /FontBBox [0 0 %d %d] /FontMatrix [%s 0 0 %s 0 0]"
            b" /CharProcs << /square %d 0 R /triangle %d 0 R /bitmap %d 0 R >>"
            b" /Encoding << /Type /Encoding /Differences [97 /square /triangle /bitmap] >>"
            b" /FirstChar 97 /LastChar 99 /Widths [%d %d %d] /Resources << /XObject << /Fm0 %d 0 R >> >>"
            b" /ToUnicode %d 0 R >>"
            % (750 // scale, 750 // scale, (b"0.001" if scale == 1 else b"0.01"), (b"0.001" if scale == 1 else b"0.01"),
               char_procs, char_procs + 1, char_procs + 2, unit, unit, unit, resources, tounicode))


def type3_glyphs(scale: int) -> list[bytes]:
    """The square (d1), triangle (d0) and bitmap (d1) glyph descriptions of gen_text_type3(), in a glyph space of
    1000/``scale`` units per em. Numbers are written as integers or with one decimal, exactly."""
    def n(value: float) -> bytes:
        value = value / scale
        return (b"%d" % value) if value == int(value) else (b"%.1f" % value)
    square = b"%s 0 0 0 %s %s d1 0 1 0 rg 0 0 %s %s re f /Fm0 Do" % (n(1000), n(750), n(750), n(750), n(750))
    triangle = b"%s 0 d0 1 0 0 rg 0 0 m %s %s l %s 0 l f" % (n(1000), n(375), n(750), n(750))
    bitmap = (b"%s 0 0 0 %s %s d1 q %s 0 0 %s 0 0 cm BI /W 8 /H 1 /IM true /F /AHx ID AA> EI"
              b" BI /W 1 /H 1 /CS /G /BPC 8 /F /AHx ID 00> EI Q" % (n(1000), n(750), n(750), n(750), n(750)))
    return [square, triangle, bitmap]


def gen_text_type3() -> bytes:
    """9.6.4 Type 3 fonts (Tables 110, 111), 8.6.8 (d1 colour restriction), 9.2.4 (FontMatrix), 7.8.3 (font Resources).
    Two fonts with the same three glyphs: T3a in a 1000-unit glyph space (FontMatrix 0.001), T3b in a 100-unit one
    (FontMatrix 0.01, every number divided by 10), so both render identically. Glyph a (square, d1) sets 0 1 0 rg (ignored)
    and paints its font's own form /Fm0, whose 1 0 0 rg is ignored too (8.6.8: the restriction holds in every stream the
    glyph invokes); glyph b (triangle, d0) paints red; glyph c (bitmap, d1) paints an 8x1 inline image mask and a 1x1
    DeviceGray inline image (ignored in d1). The page defines a different /Fm0 (a page-sized square) that the glyphs must
    not find. The page fills blue, shows (abc) in T3a 24 pt at (72, 700) and in T3b 24 pt at (72, 650). A ToUnicode CMap
    maps 97-99 to U+25A0, U+25B2, U+25CF."""
    content = b"0 0 1 rg BT /T3a 24 Tf 72 700 Td (abc) Tj ET BT /T3b 24 Tf 72 650 Td (abc) Tj ET\n"
    tounicode = to_unicode(b"1 begincodespacerange\n<00> <FF>\nendcodespacerange\n",
                           b"3 beginbfchar\n<61> <25A0>\n<62> <25B2>\n<63> <25CF>\nendbfchar\n")
    a_square, a_triangle, a_bitmap = type3_glyphs(1)
    b_square, b_triangle, b_bitmap = type3_glyphs(10)
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False,
                 extra=b" /Resources << /Font << /T3a 5 0 R /T3b 6 0 R >> /XObject << /Fm0 16 0 R >> >>")),
        (4, stream(b"", content)),
        (5, type3_font(1, 7, 10, 15)),
        (6, type3_font(10, 11, 14, 15)),
        (7, stream(b"", a_square)),
        (8, stream(b"", a_triangle)),
        (9, stream(b"", a_bitmap)),
        (10, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 750 750]", b"1 0 0 rg 100 100 200 200 re f")),
        (11, stream(b"", b_square)),
        (12, stream(b"", b_triangle)),
        (13, stream(b"", b_bitmap)),
        (14, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 75 75]", b"1 0 0 rg 10 10 20 20 re f")),
        (15, tounicode),
        (16, stream(b"/Type /XObject /Subtype /Form /BBox [0 0 612 792]", b"0 g 0 0 612 792 re f")),
    ])


def gen_text_type3_recursive() -> bytes:
    """9.6.4: a Type 3 font (FontMatrix 0.001, no Resources: names resolve in the page's) whose glyph r shows text
    without Tf, so in the inherited current font, which is the font itself: (a) runs glyph a (legal: another glyph of
    the same font), (r) would run glyph r inside itself (refused, ContentType3GlyphRecursion). Glyph n has no d0 or d1
    (Table 111: it shall be the first operator; ContentType3GlyphMetricsMissing, run as d0). The page shows (arn) at
    24 pt from (72, 700)."""
    content = b"BT /T3 24 Tf 72 700 Td (arn) Tj ET\n"
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources << /Font << /T3 5 0 R >> >>")),
        (4, stream(b"", content)),
        (5, b"<< /Type /Font /Subtype /Type3 /FontBBox [0 0 1000 1000] /FontMatrix [0.001 0 0 0.001 0 0]"
            b" /CharProcs << /a 6 0 R /r 7 0 R /n 8 0 R >> /Encoding << /Type /Encoding /Differences [97 /a 110 /n 114 /r] >>"
            b" /FirstChar 97 /LastChar 114 /Widths [1000 0 0 0 0 0 0 0 0 0 0 0 0 1000 0 0 0 1000] >>"),
        (6, stream(b"", b"1000 0 0 0 1000 1000 d1 0 0 1000 1000 re f")),
        (7, stream(b"", b"1000 0 d0 BT (ar) Tj ET")),
        (8, stream(b"", b"0 0 500 500 re f")),
    ])


# ---------------------------------------------------------------------------
# CCITT fax images (clause 7.4.6; ITU-T T.4 clause 4, T.6 clause 2): one mode or framing per file. The decoded samples are
# ccitt_pack(ccitt_sample_bitmap(w, h)), which the C# twin CcittEncoder.SampleBitmap / Pack reproduces for the tests.
# ---------------------------------------------------------------------------

# T.4 Table 2 (terminating 0-63) and Table 3a (make-up 64-1728) code words, white and black; Table 3b (1792-2560, both).
CCITT_WHITE_TERM = (
    "00110101 000111 0111 1000 1011 1100 1110 1111 10011 10100 00111 01000 001000 000011 110100 110101 101010 101011 "
    "0100111 0001100 0001000 0010111 0000011 0000100 0101000 0101011 0010011 0100100 0011000 00000010 00000011 00011010 "
    "00011011 00010010 00010011 00010100 00010101 00010110 00010111 00101000 00101001 00101010 00101011 00101100 00101101 "
    "00000100 00000101 00001010 00001011 01010010 01010011 01010100 01010101 00100100 00100101 01011000 01011001 01011010 "
    "01011011 01001010 01001011 00110010 00110011 00110100").split()
CCITT_BLACK_TERM = (
    "0000110111 010 11 10 011 0011 0010 00011 000101 000100 0000100 0000101 0000111 00000100 00000111 000011000 0000010111 "
    "0000011000 0000001000 00001100111 00001101000 00001101100 00000110111 00000101000 00000010111 00000011000 000011001010 "
    "000011001011 000011001100 000011001101 000001101000 000001101001 000001101010 000001101011 000011010010 000011010011 "
    "000011010100 000011010101 000011010110 000011010111 000001101100 000001101101 000011011010 000011011011 000001010100 "
    "000001010101 000001010110 000001010111 000001100100 000001100101 000001010010 000001010011 000000100100 000000110111 "
    "000000111000 000000100111 000000101000 000001011000 000001011001 000000101011 000000101100 000001011010 000001100110 "
    "000001100111").split()
CCITT_WHITE_MAKEUP = (
    "11011 10010 010111 0110111 00110110 00110111 01100100 01100101 01101000 01100111 011001100 011001101 011010010 "
    "011010011 011010100 011010101 011010110 011010111 011011000 011011001 011011010 011011011 010011000 010011001 010011010 "
    "011000 010011011").split()
CCITT_BLACK_MAKEUP = (
    "0000001111 000011001000 000011001001 000001011011 000000110011 000000110100 000000110101 0000001101100 0000001101101 "
    "0000001001010 0000001001011 0000001001100 0000001001101 0000001110010 0000001110011 0000001110100 0000001110101 "
    "0000001110110 0000001110111 0000001010010 0000001010011 0000001010100 0000001010101 0000001011010 0000001011011 "
    "0000001100100 0000001100101").split()
CCITT_EXT_MAKEUP = (
    "00000001000 00000001100 00000001101 000000010010 000000010011 000000010100 000000010101 000000010110 000000010111 "
    "000000011100 000000011101 000000011110 000000011111").split()
CCITT_EOL = "000000000001"
CCITT_VERTICAL = {-3: "0000010", -2: "000010", -1: "010", 0: "1", 1: "011", 2: "000011", 3: "0000011"}  # T.4 Table 4


def ccitt_run_codes(run: int, black: bool) -> str:
    """T.4 4.1.2 / T.6 2.2.4 Step 2 iii: make-up codes (2560 repeated for runs >= 2624) then one terminating code."""
    out = ""
    while run >= 2560:
        out += CCITT_EXT_MAKEUP[-1]
        run -= 2560
    if run >= 64:
        m = run // 64
        out += (CCITT_BLACK_MAKEUP if black else CCITT_WHITE_MAKEUP)[m - 1] if m <= 27 else CCITT_EXT_MAKEUP[m - 28]
        run %= 64
    return out + (CCITT_BLACK_TERM if black else CCITT_WHITE_TERM)[run]


def ccitt_encode_1d(row: list[bool]) -> str:
    """T.4 4.1: alternating runs from white (the first white run may be 0)."""
    out, pos, black = "", 0, False
    while pos < len(row):
        end = pos
        while end < len(row) and row[end] == black:
            end += 1
        out += ccitt_run_codes(end - pos, black)
        pos, black = end, not black
    return out


def ccitt_encode_2d(row: list[bool], ref: list[bool]) -> str:
    """T.4 4.2.1.3 Figure 7 / T.6 2.2: pass, vertical and horizontal modes against the reference line."""
    w = len(row)

    def px(line: list[bool], x: int) -> bool:
        return x >= 0 and line[x]

    def next_change(line: list[bool], after: int, colour: bool) -> int:
        p = after + 1
        while p < w and line[p] == colour:
            p += 1
        return min(p, w)

    out, a0, colour = "", -1, False
    while a0 < w:
        a1 = next_change(row, a0, colour)
        b1 = a0 + 1
        while b1 < w and not (px(ref, b1) != px(ref, b1 - 1) and px(ref, b1) != colour):
            b1 += 1
        b2 = b1 + 1
        while b2 < w and px(ref, b2) == px(ref, b1):
            b2 += 1
        b2 = min(b2, w)
        if b2 < a1:
            out += "0001"
            a0 = b2
        elif abs(a1 - b1) <= 3:
            out += CCITT_VERTICAL[a1 - b1]
            a0, colour = a1, not colour
        else:
            a2 = next_change(row, a1, not colour)
            out += "001" + ccitt_run_codes(a1 - max(a0, 0), colour) + ccitt_run_codes(a2 - a1, not colour)
            a0 = a2
    return out


def ccitt_encode(rows: list[list[bool]], k: int = 0, end_of_line: bool = False, byte_align: bool = False,
                 end_of_block: bool = True) -> bytes:
    """Frames coded lines as CCITTFaxDecode (Table 11) expects: EOL (+ tag bit for K > 0, T.4 4.2.2) before each line when
    EndOfLine or K > 0, fill so the EOL ends on a byte boundary (or pad before the line when there are no EOLs) under
    EncodedByteAlign, then EOFB (2 EOL, T.6 2.4.1.1) or RTC (6 EOL / 6 EOL+1, T.4 4.1.4, 4.2.4); zero pad to a byte."""
    bits: list[str] = []
    count = 0

    def put(s: str) -> None:
        nonlocal count
        bits.append(s)
        count += len(s)

    def pad_to(multiple_end: int = 0) -> None:
        while (count + multiple_end) % 8:
            put("0")

    width = len(rows[0]) if rows else 0
    prev = [False] * width
    for i, row in enumerate(rows):
        two_d = k < 0 or (k > 0 and i % k != 0)
        if end_of_line or k > 0:
            eol = CCITT_EOL + ("" if k <= 0 else ("0" if two_d else "1"))
            if byte_align:
                pad_to(len(eol))
            put(eol)
        elif byte_align:
            pad_to()
        put(ccitt_encode_2d(row, prev) if two_d else ccitt_encode_1d(row))
        prev = row
    if end_of_block:
        if byte_align:
            pad_to()
        put(CCITT_EOL * 2 if k < 0 else (CCITT_EOL + ("1" if k > 0 else "")) * 6)
    pad_to()
    s = "".join(bits)
    return bytes(int(s[i:i + 8], 2) for i in range(0, len(s), 8))


def ccitt_sample_bitmap(width: int, height: int, seed: int = 63) -> list[list[bool]]:
    """A deterministic bitmap (port of CcittEncoder.SampleBitmap): every 8 rows a white row, a black row, a dithered gradient, a
    row starting with a long black run, and four rows of drifting text-like strokes (vertical and pass modes)."""
    state = seed
    rows = []
    for y in range(height):
        kind = y % 8
        if kind == 0:
            row = [False] * width
        elif kind == 1:
            row = [True] * width
        elif kind == 2:
            row = []
            for x in range(width):
                state = (state * 1664525 + 1013904223) & 0xFFFFFFFF
                row.append((state >> 16) % width < x)
        elif kind == 3:
            row = [x < width * 2 // 3 or x % 97 == 5 for x in range(width)]
        else:
            row = []
            for x in range(width):
                phase = (x + y // 3 + seed) % 23
                row.append(phase < 3 or (phase in (9, 10) and y % 3 != 0) or ((x // 41) % 5 == y % 5 and phase < 15))
        rows.append(row)
    return rows


def ccitt_pack(rows: list[list[bool]], black_is_1: bool = False) -> bytes:
    """The expected CCITTFaxDecode output: ceil(w/8) bytes per row, MSB first, black 0 (1 under BlackIs1), pad bits white."""
    out = bytearray()
    for row in rows:
        bits = [(1 if b else 0) if black_is_1 else (0 if b else 1) for b in row]
        bits += [0 if black_is_1 else 1] * (-len(row) % 8)
        out += bytes(int("".join(map(str, bits[i:i + 8])), 2) for i in range(0, len(bits), 8))
    return bytes(out)


def ccitt_image(width: int, height: int, parms: bytes, data: bytes, extra: bytes = b"") -> bytes:
    """A page painting one DeviceGray 1-bit CCITTFaxDecode image of ``width`` x ``height`` with DecodeParms ``parms``."""
    return one_image(b"/Width %d /Height %d /ColorSpace /DeviceGray /BitsPerComponent 1 /Filter /CCITTFaxDecode "
                     b"/DecodeParms << %s >>%s" % (width, height, parms, extra), data)


def gen_ccitt_g3_1d() -> bytes:
    """7.4.6 Group 3 1-D (K 0): 203 x 40 (Columns not a multiple of 8), no EOLs, no RTC (EndOfBlock false, Rows 40)."""
    rows = ccitt_sample_bitmap(203, 40)
    return ccitt_image(203, 40, b"/K 0 /Columns 203 /Rows 40 /EndOfBlock false", ccitt_encode(rows, 0, end_of_block=False))


def gen_ccitt_g3_1d_eol_align() -> bytes:
    """7.4.6 Group 3 1-D with EndOfLine and EncodedByteAlign: fill before each EOL so it ends on a byte boundary, RTC, no Rows."""
    rows = ccitt_sample_bitmap(150, 30)
    return ccitt_image(150, 30, b"/K 0 /Columns 150 /EndOfLine true /EncodedByteAlign true",
                       ccitt_encode(rows, 0, end_of_line=True, byte_align=True))


def gen_ccitt_g3_2d() -> bytes:
    """7.4.6 Group 3 2-D (K 4): EOL + tag bit before every line, a 1-D line every 4th, 2600 columns (extended make-up codes,
    T.4 Table 3b), RTC of 6 x (EOL + 1)."""
    rows = ccitt_sample_bitmap(2600, 30)
    return ccitt_image(2600, 30, b"/K 4 /Columns 2600 /Rows 30 /EndOfLine true", ccitt_encode(rows, 4, end_of_line=True))


def gen_ccitt_g4() -> bytes:
    """7.4.6 Group 4 (K -1): 1728 x 40 (Columns given explicitly at its default), EOFB, no Rows."""
    rows = ccitt_sample_bitmap(1728, 40)
    return ccitt_image(1728, 40, b"/K -1 /Columns 1728", ccitt_encode(rows, -1))


def gen_ccitt_g4_no_eob() -> bytes:
    """7.4.6 Group 4 without EOFB: EndOfBlock false, Rows 40 ends the data."""
    rows = ccitt_sample_bitmap(120, 40)
    return ccitt_image(120, 40, b"/K -1 /Columns 120 /Rows 40 /EndOfBlock false", ccitt_encode(rows, -1, end_of_block=False))


def gen_ccitt_g4_align() -> bytes:
    """7.4.6 Group 4 with EncodedByteAlign: every line starts on a byte boundary (no EOLs), EOFB aligned too."""
    rows = ccitt_sample_bitmap(77, 24)
    return ccitt_image(77, 24, b"/K -1 /Columns 77 /EncodedByteAlign true", ccitt_encode(rows, -1, byte_align=True))


def gen_ccitt_blackis1_mask() -> bytes:
    """7.4.6 BlackIs1 and 8.9.6.2 stencil masks: /Im0 is a Group 4 /ImageMask with BlackIs1 true and /Decode [1 0] (black pixels
    are 1 and paint), painted red; /Im1 the same bitmap with BlackIs1 false and the default Decode [0 1] (black pixels are 0 and
    paint), painted blue below it."""
    rows = ccitt_sample_bitmap(48, 16)
    data = ccitt_encode(rows, -1)
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources << /XObject << /Im0 5 0 R /Im1 6 0 R >> >>")),
        (4, stream(b"", b"1 0 0 rg q 192 0 0 64 72 600 cm /Im0 Do Q 0 0 1 rg q 192 0 0 64 72 520 cm /Im1 Do Q")),
        (5, stream(b"/Type /XObject /Subtype /Image /Width 48 /Height 16 /ImageMask true /Decode [1 0] /Filter /CCITTFaxDecode "
                   b"/DecodeParms << /K -1 /Columns 48 /BlackIs1 true >>", data)),
        (6, stream(b"/Type /XObject /Subtype /Image /Width 48 /Height 16 /ImageMask true /Filter /CCITTFaxDecode "
                   b"/DecodeParms << /K -1 /Columns 48 >>", data)),
    ], binary=True)


def gen_ccitt_inline() -> bytes:
    """8.9.7 inline image with the /CCF abbreviation: /F /CCF /DP << /K -1 /Columns 64 >> (DecodeParms keys are not
    abbreviated), a 64 x 16 image mask, EOFB, and /L."""
    data = ccitt_encode(ccitt_sample_bitmap(64, 16), -1)
    content = (b"q 128 0 0 32 72 600 cm BI /W 64 /H 16 /IM true /F /CCF /DP << /K -1 /Columns 64 >> /L %d ID " % len(data)
               + data + b" EI Q")
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4)),
        (4, stream(b"", content)),
    ], binary=True)


def gen_ccitt_g3_damaged() -> bytes:
    """Broken: Group 3 1-D with EOLs and DamagedRowsBeforeError 2 whose row 5 (of 24) holds an invalid code (13 bits that are
    neither a run code nor an EOL); a decoder resynchronizes at the next EOL and replaces the row by row 4."""
    rows = ccitt_sample_bitmap(96, 24)
    bits = "".join(CCITT_EOL + ("0000000000111" if y == 5 else ccitt_encode_1d(row)) for y, row in enumerate(rows))
    bits += CCITT_EOL * 6
    bits += "0" * (-len(bits) % 8)
    data = bytes(int(bits[i:i + 8], 2) for i in range(0, len(bits), 8))
    return ccitt_image(96, 24, b"/K 0 /Columns 96 /EndOfLine true /DamagedRowsBeforeError 2", data)


def gen_ccitt_g4_truncated() -> bytes:
    """Broken: a 64 x 32 Group 4 image whose data is cut in the middle of row 20 (the first 20 rows' bytes plus 3 more)."""
    rows = ccitt_sample_bitmap(64, 32)
    whole = ccitt_encode(rows, -1, end_of_block=False)
    head = len(ccitt_encode(rows[:20], -1, end_of_block=False))
    assert head + 3 < len(ccitt_encode(rows[:21], -1, end_of_block=False))
    return ccitt_image(64, 32, b"/K -1 /Columns 64", whole[:head + 3])


# ---------------------------------------------------------------------------
# DCTDecode (clause 7.4.8): a minimal baseline JPEG encoder (ITU-T T.81), integer arithmetic only so the bytes never depend on
# the platform's floating point
# ---------------------------------------------------------------------------

# T.81 Figure A.6: the natural (row-major) index of each zig-zag position.
JPEG_ZIGZAG = [
    0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
    35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55,
    62, 63,
]

# T.81 Tables K.1 and K.2 (natural order): the example luminance and chrominance quantization tables.
JPEG_LUMA_QUANT = [
    16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87,
    80, 62, 18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78, 87, 103, 121, 120, 101, 72, 92,
    95, 98, 112, 100, 103, 99,
]
JPEG_CHROMA_QUANT = [
    17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99, 24, 26, 56, 99, 99, 99, 99, 99, 47, 66, 99, 99, 99, 99,
    99, 99] + [99] * 32

# T.81 Tables K.3 to K.6: the typical Huffman tables as (BITS, HUFFVAL).
JPEG_DC_LUMA = ([0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0], list(range(12)))
JPEG_DC_CHROMA = ([0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0], list(range(12)))
JPEG_AC_LUMA = ([0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 125], list(bytes.fromhex(
    "01020300041105122131410613516107227114328191a1082342b1c11552d1f02433627282090a161718191a25262728292a3435363738393a"
    "434445464748494a535455565758595a636465666768696a737475767778797a838485868788898a92939495969798999aa2a3a4a5a6a7a8a9"
    "aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae1e2e3e4e5e6e7e8e9eaf1f2f3f4f5f6f7f8f9fa")))
JPEG_AC_CHROMA = ([0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 119], list(bytes.fromhex(
    "000102031104052131061241510761711322328108144291a1b1c109233352f0156272d10a162434e125f11718191a262728292a3536373839"
    "3a434445464748494a535455565758595a636465666768696a737475767778797a82838485868788898a92939495969798999aa2a3a4a5a6a7"
    "a8a9aab2b3b4b5b6b7b8b9bac2c3c4c5c6c7c8c9cad2d3d4d5d6d7d8d9dae2e3e4e5e6e7e8e9eaf2f3f4f5f6f7f8f9fa")))

# cos((2x + 1) u pi / 16) scaled by 2^13, and C(u) (1/sqrt 2 for u = 0) scaled by 2^13 (T.81 A.3.3).
JPEG_COS = [[round(math.cos((2 * x + 1) * u * math.pi / 16) * 8192) for u in range(8)] for x in range(8)]
JPEG_CU = [round(8192 / math.sqrt(2))] + [8192] * 7


def jpeg_huffman_codes(table: tuple[list[int], list[int]]) -> dict[int, tuple[int, int]]:
    """T.81 Annex C (Figures C.1 to C.3): symbol -> (code, length)."""
    bits, values = table
    codes: dict[int, tuple[int, int]] = {}
    code = 0
    k = 0
    for length in range(1, 17):
        for _ in range(bits[length - 1]):
            codes[values[k]] = (code, length)
            code += 1
            k += 1
        code <<= 1
    return codes


def jpeg_round_div(numerator: int, denominator: int) -> int:
    """numerator / denominator rounded half away from zero (denominator > 0)."""
    q = (abs(numerator) * 2 + denominator) // (2 * denominator)
    return q if numerator >= 0 else -q


def jpeg_fdct_quantize(block: list[int], quant: list[int]) -> list[int]:
    """T.81 A.3.3 forward DCT of 64 level-shifted samples (natural order), then A.3.4 quantization; integer arithmetic."""
    rows = [[sum(block[8 * y + x] * JPEG_COS[x][u] for x in range(8)) for u in range(8)] for y in range(8)]
    out = []
    for v in range(8):
        for u in range(8):
            total = sum(rows[y][u] * JPEG_COS[y][v] for y in range(8)) * JPEG_CU[u] * JPEG_CU[v]
            coefficient = jpeg_round_div(total, 4 << 52)
            out.append(jpeg_round_div(coefficient, quant[8 * v + u]))
    return out


class JpegBitWriter:
    """T.81 B.1.1.5 entropy-coded segment: MSB first, a 00 stuffed after every FF, padding with 1 bits."""

    def __init__(self) -> None:
        self.out = bytearray()
        self.acc = 0
        self.n = 0

    def write(self, value: int, length: int) -> None:
        self.acc = (self.acc << length) | (value & ((1 << length) - 1))
        self.n += length
        while self.n >= 8:
            self.n -= 8
            byte = (self.acc >> self.n) & 0xFF
            self.out.append(byte)
            if byte == 0xFF:
                self.out.append(0)
        self.acc &= (1 << self.n) - 1

    def flush(self) -> None:
        if self.n:
            self.write((1 << (8 - self.n)) - 1, 8 - self.n)


def jpeg_category(value: int) -> int:
    return abs(value).bit_length()


def jpeg_encode_block(w: JpegBitWriter, coefficients: list[int], previous_dc: int, dc: dict, ac: dict) -> int:
    """T.81 F.1.2.1 and F.1.2.2 (Figures F.1 to F.5): the DC difference and the AC run/size codes of one block."""
    diff = coefficients[0] - previous_dc
    size = jpeg_category(diff)
    w.write(*dc[size])
    if size:
        w.write(diff if diff > 0 else diff - 1, size)
    run = 0
    for k in range(1, 64):
        value = coefficients[JPEG_ZIGZAG[k]]
        if value == 0:
            run += 1
            continue
        while run > 15:
            w.write(*ac[0xF0])
            run -= 16
        size = jpeg_category(value)
        w.write(*ac[(run << 4) | size])
        w.write(value if value > 0 else value - 1, size)
        run = 0
    if run:
        w.write(*ac[0x00])
    return coefficients[0]


def jpeg_segment(marker: int, body: bytes) -> bytes:
    return bytes([0xFF, marker]) + struct.pack(">H", len(body) + 2) + body


def jpeg_encode(width: int, height: int, planes: list[list[list[int]]], sampling: list[tuple[int, int]],
                restart_interval: int = 0, ids: bytes = b"\x01\x02\x03", jfif: bool = True,
                adobe_transform: int | None = None) -> bytes:
    """A baseline (SOF0) JPEG of 1 to 4 components, one interleaved scan, the K.1/K.2 quantization tables (component 0 uses the
    luminance tables, the others the chrominance ones) and the K.3-K.6 Huffman tables. ``planes`` are full-resolution sample
    planes (planes[c][y][x], 0-255, already in the coded colour space); a subsampled component is the mean of each Hmax/H x
    Vmax/V box of its plane (A.1.1), padded by edge replication to whole MCUs (A.2.4)."""
    count = len(planes)
    hmax = max(h for h, _ in sampling)
    vmax = max(v for _, v in sampling)
    if count == 1:
        sampling = [(1, 1)]
        hmax = vmax = 1
    mcus_x = -(-width // (8 * hmax))
    mcus_y = -(-height // (8 * vmax))
    components = []
    for c, (h, v) in enumerate(sampling):
        sx, sy = hmax // h, vmax // v
        cw, ch = -(-width * h // hmax), -(-height * v // vmax)
        plane = planes[c]
        samples = [[0] * cw for _ in range(ch)]
        for y in range(ch):
            for x in range(cw):
                box = [plane[min(height - 1, y * sy + j)][min(width - 1, x * sx + i)] for j in range(sy) for i in range(sx)]
                samples[y][x] = (sum(box) + len(box) // 2) // len(box)
        components.append((h, v, cw, ch, samples))

    quant = [JPEG_LUMA_QUANT, JPEG_CHROMA_QUANT]
    dc = [jpeg_huffman_codes(JPEG_DC_LUMA), jpeg_huffman_codes(JPEG_DC_CHROMA)]
    ac = [jpeg_huffman_codes(JPEG_AC_LUMA), jpeg_huffman_codes(JPEG_AC_CHROMA)]
    w = JpegBitWriter()
    predictors = [0] * count
    mcu = 0
    restarts = 0
    for my in range(mcus_y):
        for mx in range(mcus_x):
            if restart_interval and mcu and mcu % restart_interval == 0:
                w.flush()
                w.out += bytes([0xFF, 0xD0 + restarts % 8])
                restarts += 1
                predictors = [0] * count
            for c, (h, v, cw, ch, samples) in enumerate(components):
                t = min(c, 1)
                for by in range(v):
                    for bx in range(h):
                        x0, y0 = (mx * h + bx) * 8, (my * v + by) * 8
                        block = [samples[min(ch - 1, y0 + j)][min(cw - 1, x0 + i)] - 128 for j in range(8) for i in range(8)]
                        predictors[c] = jpeg_encode_block(w, jpeg_fdct_quantize(block, quant[t]), predictors[c], dc[t], ac[t])
            mcu += 1
    w.flush()

    out = bytearray(b"\xff\xd8")
    if jfif:
        out += jpeg_segment(0xE0, b"JFIF\x00\x01\x01\x00\x00\x01\x00\x01\x00\x00")
    if adobe_transform is not None:
        out += jpeg_segment(0xEE, b"Adobe" + struct.pack(">HHHB", 100, 0, 0, adobe_transform))
    tables = 1 if count == 1 else 2
    out += jpeg_segment(0xDB, b"".join(bytes([t]) + bytes(quant[t][JPEG_ZIGZAG[k]] for k in range(64)) for t in range(tables)))
    out += jpeg_segment(0xC0, struct.pack(">BHHB", 8, height, width, count) + b"".join(
        bytes([ids[c], (h << 4) | v, min(c, 1)]) for c, (h, v, _, _, _) in enumerate(components)))
    huffman = [(0x00, JPEG_DC_LUMA), (0x10, JPEG_AC_LUMA), (0x01, JPEG_DC_CHROMA), (0x11, JPEG_AC_CHROMA)][:2 * tables]
    out += jpeg_segment(0xC4, b"".join(bytes([tc]) + bytes(bits) + bytes(values) for tc, (bits, values) in huffman))
    if restart_interval:
        out += jpeg_segment(0xDD, struct.pack(">H", restart_interval))
    out += jpeg_segment(0xDA, bytes([count]) + b"".join(bytes([ids[c], 0x11 * min(c, 1)]) for c in range(count)) + b"\x00\x3f\x00")
    out += w.out + b"\xff\xd9"
    return bytes(out)


def rgb_to_ycbcr(r: int, g: int, b: int) -> tuple[int, int, int]:
    """The JFIF / CCIR 601-1 RGB to YCbCr transform (Adobe TN 5116 13.1) in 16-bit fixed point, clamped."""
    y = (19595 * r + 38470 * g + 7471 * b + 32768) >> 16
    cb = (-11059 * r - 21709 * g + 32768 * b + (128 << 16) + 32767) >> 16
    cr = (32768 * r - 27439 * g - 5329 * b + (128 << 16) + 32767) >> 16
    return y, max(0, min(255, cb)), max(0, min(255, cr))


def dct_test_picture(width: int, height: int) -> list[list[tuple[int, int, int]]]:
    """A smooth RGB picture with sharp features: a diagonal ramp, a dark disc and a white bar, so every coefficient band is used."""
    picture = []
    for y in range(height):
        row = []
        for x in range(width):
            r, g, b = (255 * x) // (width - 1), (255 * y) // (height - 1), 255 - (127 * (x + y)) // (width + height - 2)
            if (2 * x - width) ** 2 + (2 * y - height) ** 2 <= (height * 3 // 5) ** 2:
                r, g, b = r // 4, g // 4, 64
            if height // 3 <= y < height // 3 + 3:
                r = g = b = 255
            row.append((r, g, b))
        picture.append(row)
    return picture


def dct_color_jpeg() -> bytes:
    """45 x 29 YCbCr 4:2:0 (partial MCUs on both axes), JFIF, restart interval of 2 MCUs."""
    picture = dct_test_picture(45, 29)
    ycc = [[rgb_to_ycbcr(*pixel) for pixel in row] for row in picture]
    planes = [[[pixel[c] for pixel in row] for row in ycc] for c in range(3)]
    return jpeg_encode(45, 29, planes, [(2, 2), (1, 1), (1, 1)], restart_interval=2)


def dct_gray_jpeg() -> bytes:
    """37 x 21 grayscale, one component, no JFIF segment."""
    picture = dct_test_picture(37, 21)
    plane = [[(r * 77 + g * 150 + b * 29 + 128) >> 8 for r, g, b in row] for row in picture]
    return jpeg_encode(37, 21, [plane], [(1, 1)], jfif=False)


def gen_dct_baseline() -> bytes:
    """7.4.8 DCTDecode: two baseline JPEG image XObjects painted side by side, a 45 x 29 YCbCr 4:2:0 DeviceRGB image with a
    restart interval (Im0) and a 37 x 21 DeviceGray image (Im1), both encoded by ``jpeg_encode``."""
    color = dct_color_jpeg()
    gray = dct_gray_jpeg()
    return simple_file([
        (1, catalog()),
        (2, pages()),
        (3, page(contents=4, resources=False, extra=b" /Resources << /XObject << /Im0 5 0 R /Im1 6 0 R >> >>")),
        (4, stream(b"", b"q 180 0 0 116 72 600 cm /Im0 Do Q q 148 0 0 84 300 600 cm /Im1 Do Q")),
        (5, stream(b"/Type /XObject /Subtype /Image /Width 45 /Height 29 /ColorSpace /DeviceRGB /BitsPerComponent 8 "
                   b"/Filter /DCTDecode", color)),
        (6, stream(b"/Type /XObject /Subtype /Image /Width 37 /Height 21 /ColorSpace /DeviceGray /BitsPerComponent 8 "
                   b"/Filter /DCTDecode", gray)),
    ], binary=True)


# T.88 Figures 3 to 6 in reading order: (dx, dy) of a fixed pixel, or ("A", n) for AT pixel n at its nominal slot.
JBIG2_TEMPLATES = [
    [("A", 4), (-1, -2), (0, -2), (1, -2), ("A", 3), ("A", 2), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), ("A", 1),
     (-4, 0), (-3, 0), (-2, 0), (-1, 0)],
    [(-1, -2), (0, -2), (1, -2), (2, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), ("A", 1), (-3, 0), (-2, 0), (-1, 0)],
    [(-1, -2), (0, -2), (1, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), ("A", 1), (-2, 0), (-1, 0)],
    [(-3, -1), (-2, -1), (-1, -1), (0, -1), (1, -1), ("A", 1), (-4, 0), (-3, 0), (-2, 0), (-1, 0)],
]
JBIG2_NOMINAL_AT = [[(3, -1), (-3, -1), (2, -2), (-2, -2)], [(3, -1)], [(2, -1)], [(2, -1)]]
JBIG2_SLTP = [0x9B25, 0x0795, 0x00E5, 0x0195]  # Figures 8 to 11 gathered in the same order


def jbig2_encode_generic(rows: list[list[bool]], template: int, tpgdon: bool, at: list[tuple[int, int]] | None = None) -> bytes:
    """T.88 6.2.5: template-based arithmetic coding of a bitmap (True = 1 = black) with typical prediction, through the MQ
    coder of E.2 (shared with JPEG 2000), terminated by FLUSH and the 0xFF 0xAC marker. AT pixels keep their nominal bit."""
    at = at or JBIG2_NOMINAL_AT[template]
    height, width = len(rows), len(rows[0]) if rows else 0
    mq, cx, ltp = MqEncoder(), [0] * 65536, False

    def px(x: int, y: int) -> int:
        return 1 if 0 <= y < height and 0 <= x < width and rows[y][x] else 0

    for y in range(height):
        if tpgdon:
            typical = rows[y] == (rows[y - 1] if y else [False] * width)
            mq.encode(cx, JBIG2_SLTP[template], int(typical != ltp))
            ltp = typical
            if ltp:
                continue
        for x in range(width):
            context = 0
            for a, b in JBIG2_TEMPLATES[template]:
                dx, dy = at[b - 1] if a == "A" else (a, b)
                context = (context << 1) | px(x + dx, y + dy)
            mq.encode(cx, context, int(rows[y][x]))
    return mq.flush() + b"\xff\xac"


def jbig2_segment(number: int, seg_type: int, page: int, data: bytes) -> bytes:
    """T.88 7.2: a segment header (short page association, no referred-to segments) followed by its data part (D.1)."""
    return struct.pack(">IBBBI", number, seg_type, 0, page, len(data)) + data


def jbig2_page_info(width: int, height: int, flags: int = 0) -> bytes:
    """T.88 7.4.8: width, height, unknown resolution, page flags, no striping."""
    return struct.pack(">IIIIBH", width, height, 0, 0, flags, 0)


def jbig2_generic_region(rows: list[list[bool]], mmr: bool, template: int = 0, tpgdon: bool = False) -> bytes:
    """T.88 7.4.6: region information at (0, 0) with OR, generic region flags, nominal AT bytes, the coded bitmap."""
    header = struct.pack(">IIIIB", len(rows[0]), len(rows), 0, 0, 0) + bytes([int(mmr) | (template << 1) | (int(tpgdon) << 3)])
    if mmr:
        return header + ccitt_encode(rows, -1)
    at = b"".join(struct.pack(">bb", x, y) for x, y in JBIG2_NOMINAL_AT[template])
    return header + at + jbig2_encode_generic(rows, template, tpgdon)


def jbig2_comment(name: bytes, value: bytes) -> bytes:
    """T.88 7.4.15.1: a single-byte coded comment extension."""
    return struct.pack(">I", 0x20000000) + name + b"\0" + value + b"\0\0"


def gen_jbig2_generic() -> bytes:
    """7.4.7 JBIG2Decode with Table 12's JBIG2Globals: a 150 x 48 page (the CCITT sample bitmap, 1 = black) coded as one immediate
    lossless generic region, template 0 with typical prediction (T.88 6.2.5, 7.4.6); the globals stream holds one comment
    extension segment (page 0, 7.4.15.1). Embedded sequential organisation, page association 1, no file header, no EOP."""
    rows = ccitt_sample_bitmap(150, 48)
    page = (jbig2_segment(1, 48, 1, jbig2_page_info(150, 48))
            + jbig2_segment(2, 39, 1, jbig2_generic_region(rows, mmr=False, template=0, tpgdon=True)))
    globals_ = jbig2_segment(0, 62, 0, jbig2_comment(b"Title", b"Broadside JBIG2 globals"))
    return image_page([
        (5, stream(b"/Type /XObject /Subtype /Image /Width 150 /Height 48 /ColorSpace /DeviceGray /BitsPerComponent 1"
                   b" /Filter /JBIG2Decode /DecodeParms << /JBIG2Globals 6 0 R >>", page)),
        (6, stream(b"", globals_)),
    ], version="1.4")


def gen_jbig2_generic_mmr() -> bytes:
    """7.4.7 JBIG2Decode as an image mask (8.9.6.2): the same 150 x 48 bitmap as one MMR generic region (T.88 6.2.6, T.6 with
    EOFB); the filter's 0 = black samples paint with the default Decode [0 1]. No JBIG2Globals."""
    rows = ccitt_sample_bitmap(150, 48)
    page = jbig2_segment(0, 48, 1, jbig2_page_info(150, 48)) + jbig2_segment(1, 39, 1, jbig2_generic_region(rows, mmr=True))
    return one_image(b"/Width 150 /Height 48 /ImageMask true /Filter /JBIG2Decode", page, version="1.4")


FILES = {
    "empty-page.pdf": gen_empty_page,
    "pdf20-header.pdf": gen_pdf20_header,
    "text-standard14.pdf": gen_text_standard14,
    "text-truetype-embedded.pdf": gen_text_truetype_embedded,
    "text-standard14-differences.pdf": gen_text_standard14_differences,
    "text-standard14-winansi-quirks.pdf": gen_text_standard14_winansi_quirks,
    "text-standard14-macroman.pdf": gen_text_standard14_macroman,
    "text-standard14-symbol.pdf": gen_text_standard14_symbol,
    "text-standard14-symbol-differences.pdf": gen_text_standard14_symbol_differences,
    "text-standard14-zapfdingbats.pdf": gen_text_standard14_zapfdingbats,
    "text-standard14-widths.pdf": gen_text_standard14_widths,
    "text-standard14-alias.pdf": gen_text_standard14_alias,
    "text-nonembedded-substitute.pdf": gen_text_nonembedded_substitute,
    "text-type1-symbolic-noencoding.pdf": gen_text_type1_symbolic_noencoding,
    "text-truetype-composite.pdf": gen_text_truetype_composite,
    "text-truetype-symbolic.pdf": gen_text_truetype_symbolic,
    "text-truetype-macroman.pdf": gen_text_truetype_macroman,
    "text-truetype-loca-long.pdf": gen_text_truetype_loca_long,
    "text-cff-embedded.pdf": gen_text_cff_embedded,
    "text-opentype-cff-embedded.pdf": gen_text_opentype_cff_embedded,
    "text-type1-embedded.pdf": gen_text_type1_embedded,
    "text-cid-identity-h.pdf": gen_text_cid_identity_h,
    "text-cid-identity-v.pdf": gen_text_cid_identity_v,
    "text-cid-embedded-cmap.pdf": gen_text_cid_embedded_cmap,
    "xref-stream.pdf": gen_xref_stream,
    "object-stream.pdf": gen_object_stream,
    "incremental-update.pdf": gen_incremental_update,
    "hybrid-xref.pdf": gen_hybrid_xref,
    "flate-stream.pdf": gen_flate_stream,
    "lzw-stream.pdf": gen_lzw_stream,
    "ascii85-stream.pdf": gen_ascii85_stream,
    "asciihex-stream.pdf": gen_asciihex_stream,
    "runlength-stream.pdf": gen_runlength_stream,
    "filter-chain.pdf": gen_filter_chain,
    "png-predictor.pdf": gen_png_predictor,
    "encrypted-rc4-40.pdf": lambda: gen_encrypted_legacy("encrypted-rc4-40", 2, b"RC4 40-bit (R2)"),
    "encrypted-rc4-128.pdf": lambda: gen_encrypted_legacy("encrypted-rc4-128", 3, b"RC4 128-bit (R3)"),
    "encrypted-aes-128.pdf": lambda: gen_encrypted_legacy("encrypted-aes-128", 4, b"AES-128 (R4)"),
    "encrypted-aes-256.pdf": gen_encrypted_aes_256,
    "encrypted-rc4-40-r3.pdf": lambda: gen_encrypted_legacy("encrypted-rc4-40-r3", 3, b"RC4 40-bit (R3)", n=5),
    "encrypted-crypt-filters.pdf": gen_encrypted_crypt_filters,
    "encrypted-aes-gcm.pdf": gen_encrypted_aes_gcm,
    "encrypted-mac.pdf": gen_encrypted_mac,
    "encrypted-empty-owner-password.pdf": gen_encrypted_empty_owner_password,
    "encrypted-user-password.pdf": gen_encrypted_user_password,
    "encrypted-rc4-user-password.pdf": gen_encrypted_rc4_user_password,
    "encrypted-mac-tampered.pdf": gen_encrypted_mac_tampered,
    "encrypted-owner-key-variant.pdf": lambda: gen_encrypted_legacy(
        "encrypted-owner-key-variant", 3, b"RC4 40-bit (R3), qpdf owner key", n=5, rehash_first_n=True),
    "encrypted-rc4-length-missing.pdf": lambda: gen_encrypted_legacy(
        "encrypted-rc4-length-missing", 3, b"RC4 128-bit (R3), no Length", omit_length=True),
    "broken-xref-offsets.pdf": gen_broken_xref_offsets,
    "missing-endobj.pdf": gen_missing_endobj,
    "wrong-stream-length.pdf": gen_wrong_stream_length,
    "no-xref.pdf": gen_no_xref,
    "startxref-wrong.pdf": gen_startxref_wrong,
    "inline-image.pdf": gen_inline_image,
    "page-tree-inherited.pdf": gen_page_tree_inherited,
    "annotations-link.pdf": gen_annotations_link,
    "annotations-subtypes.pdf": gen_annotations_subtypes,
    "annotations-appearance.pdf": gen_annotations_appearance,
    "annotations-malformed.pdf": gen_annotations_malformed,
    "text-type1-pfb.pdf": gen_text_type1_pfb,
    "text-type1-hex-eexec.pdf": gen_text_type1_hex_eexec,
    "text-type1-bad-lengths.pdf": gen_text_type1_bad_lengths,
    "outline.pdf": gen_outline,
    "name-tree-dests.pdf": gen_name_tree_dests,
    "metadata-xmp.pdf": gen_metadata_xmp,
    "metadata-xmp-forms.pdf": gen_metadata_xmp_forms,
    "info-dictionary.pdf": gen_info_dictionary,
    "viewer-preferences.pdf": gen_viewer_preferences,
    "catalog-version-extensions.pdf": gen_catalog_version_extensions,
    "page-labels.pdf": gen_page_labels,
    "name-tree-deep.pdf": gen_name_tree_deep,
    "number-tree-deep.pdf": gen_number_tree_deep,
    "name-tree-broken.pdf": gen_name_tree_broken,
    "tagged-structure.pdf": gen_tagged_structure,
    "destinations-all.pdf": gen_destinations_all,
    "outline-full.pdf": gen_outline_full,
    "outline-broken.pdf": gen_outline_broken,
    "functions.pdf": gen_functions,
    "optional-content.pdf": gen_optional_content,
    "embedded-files.pdf": gen_embedded_files,
    "collection-portfolio.pdf": gen_collection_portfolio,
    "associated-files.pdf": gen_associated_files,
    "object-metadata.pdf": gen_object_metadata,
    "declarations.pdf": gen_declarations,
    "actions-all.pdf": gen_actions_all,
    "actions-preserved.pdf": gen_actions_preserved,
    "acroform-fields.pdf": gen_acroform_fields,
    "acroform-xfa.pdf": gen_acroform_xfa,
    "colorspace-families.pdf": gen_colorspace_families,
    "color-operators.pdf": gen_color_operators,
    "default-colorspaces.pdf": gen_default_colorspaces,
    "separation-special.pdf": gen_separation_special,
    "form-xobject-nested.pdf": gen_form_xobject_nested,
    "extgstate-params.pdf": gen_extgstate_params,
    "marked-content.pdf": gen_marked_content,
    "image-stencil-mask.pdf": gen_image_stencil_mask,
    "image-explicit-mask.pdf": gen_image_explicit_mask,
    "image-color-key-mask.pdf": gen_image_color_key_mask,
    "image-smask.pdf": gen_image_smask,
    "image-smask-matte.pdf": gen_image_smask_matte,
    "image-1bpc.pdf": gen_image_1bpc,
    "image-2bpc.pdf": gen_image_2bpc,
    "image-4bpc-indexed.pdf": gen_image_4bpc_indexed,
    "image-16bpc.pdf": gen_image_16bpc,
    "image-decode-inverted.pdf": gen_image_decode_inverted,
    "jpx-lossless.pdf": gen_jpx_lossless,
    "inline-image-filters.pdf": gen_inline_image_filters,
    "inline-image-ei-in-data.pdf": gen_inline_image_ei_in_data,
    "dct-baseline.pdf": gen_dct_baseline,
    "shading-type1-function.pdf": gen_shading_type1,
    "shading-type2-axial.pdf": gen_shading_type2,
    "shading-type3-radial.pdf": gen_shading_type3,
    "shading-type4-freeform.pdf": gen_shading_type4,
    "shading-type5-lattice.pdf": gen_shading_type5,
    "shading-type6-coons.pdf": gen_shading_type6,
    "shading-type7-tensor.pdf": gen_shading_type7,
    "pattern-tiling-colored.pdf": gen_pattern_tiling_colored,
    "pattern-tiling-uncolored.pdf": gen_pattern_tiling_uncolored,
    "pattern-shading-axial.pdf": gen_pattern_shading,
    "pattern-in-form.pdf": gen_pattern_in_form,
    "text-type3.pdf": gen_text_type3,
    "pattern-recursive.pdf": gen_pattern_recursive,
    "shading-mesh-truncated.pdf": gen_shading_mesh_truncated,
    "text-type3-recursive.pdf": gen_text_type3_recursive,
    "ccitt-g3-1d.pdf": gen_ccitt_g3_1d,
    "ccitt-g3-1d-eol-align.pdf": gen_ccitt_g3_1d_eol_align,
    "ccitt-g3-2d.pdf": gen_ccitt_g3_2d,
    "ccitt-g4.pdf": gen_ccitt_g4,
    "ccitt-g4-no-eob.pdf": gen_ccitt_g4_no_eob,
    "ccitt-g4-align.pdf": gen_ccitt_g4_align,
    "ccitt-blackis1-mask.pdf": gen_ccitt_blackis1_mask,
    "ccitt-inline.pdf": gen_ccitt_inline,
    "ccitt-g3-damaged.pdf": gen_ccitt_g3_damaged,
    "ccitt-g4-truncated.pdf": gen_ccitt_g4_truncated,
    "jbig2-generic.pdf": gen_jbig2_generic,
    "jbig2-generic-mmr.pdf": gen_jbig2_generic_mmr,
}


def self_test() -> None:
    """Known-answer tests for the codecs and ciphers implemented above."""
    # 7.4.4.2 Table 7 / the packed example that follows it
    assert lzw_encode(bytes([45, 45, 45, 45, 45, 65, 45, 45, 45, 66])) == bytes.fromhex("800B6050220C0C8501")
    # ASCII85: the classic 'Man ' example
    assert ascii85_encode(b"Man ") == b"9jqo^~>"
    assert runlength_encode(b"aaaab") == b"\xfda\x00b\x80"
    # FIPS-197 Appendix C known answers
    k128 = bytes(range(16))
    k256 = bytes(range(32))
    pt = bytes.fromhex("00112233445566778899aabbccddeeff")
    assert aes_ecb_encrypt_block(k128, pt) == bytes.fromhex("69c4e0d86a7b0430d8cdb78070b4c55a")
    assert aes_ecb_encrypt_block(k256, pt) == bytes.fromhex("8ea2b7ca516745bfeafc49904b496089")
    # RC4 known answer (key "Key", plaintext "Plaintext")
    assert rc4(b"Key", b"Plaintext") == bytes.fromhex("BBF316E8D940AF0AD3")
    # GCM: the McGrew-Viega test cases 13 and 14 (AES-256, 96-bit zero IV, no AAD)
    assert gcm_encrypt(bytes(32), bytes(12), b"") == (b"", bytes.fromhex("530f8afbc74536b9a963b4f1c4cb738b"))
    assert gcm_encrypt(bytes(32), bytes(12), bytes(16)) == (
        bytes.fromhex("cea7403d4d606b6e074ec5d3baf39d18"), bytes.fromhex("d0d1c8a799996bf0265b98b5d48ab919"))
    # RFC 3394 4.6: wrap 256 bits of key data with a 256-bit KEK
    assert aes_key_wrap(bytes(range(32)), bytes.fromhex(
        "00112233445566778899AABBCCDDEEFF000102030405060708090A0B0C0D0E0F")) == bytes.fromhex(
        "28C9F404C4B810F4CBCCB35CFB87F8263F5786E2D80ED326CBC7F0E71A99F43BFB988B9B7A02DD21")
    # Type 1 Font Format 7.3: the charstring of the letter C (6.6), encrypted with four zero bytes (r = 4330)
    c_plain = bytes.fromhex("BDF9B40D8BEF038BEF01F8ECEF018B16F95006EF07FCEC06F88807F8EC06EF07FD5006090E")
    c_cipher = bytes.fromhex("10BF31704FAB5B1F03F9B68B1F39A66521B1841F1481697F8E12B7F7DDD6E3D7248D965B1CD45E2114")
    assert t1_encrypt(bytes(4) + c_plain, 4330) == c_cipher
    assert t1_charstring("50 800 hsbw 0 100 vstem 0 100 hstem 600 100 hstem 0 hmoveto 700 hlineto 100 vlineto "
                         "-600 hlineto 500 vlineto 600 hlineto 100 vlineto -700 hlineto closepath endchar") == c_plain
    # TN 5015 section 6 errata: the corrected eexec text of the Symbol font begins its Private dictionary
    assert t1_decrypt(bytes.fromhex("a8686bfddf470dd119f86e1b8e5b290ae7d910e9317a36f6768d8de89e7ed5b8"), 55665)[4:] \
        == b"dup /Private 13 dict dup beg"
    # RFC 5869 A.1
    assert hkdf_sha256(b"\x0b" * 22, bytes(range(13)), bytes(range(0xF0, 0xFA)), 42) == bytes.fromhex(
        "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865")
    # 8.9.3 sample packing: MSB first, rows padded to a byte with 1 bits, 16-bit big-endian
    assert pack_samples([[1, 0, 1]], 1) == bytes([0b10111111])
    assert pack_samples([[3, 0, 1, 2, 3]], 2) == bytes([0xC6, 0xFF])
    assert pack_samples([[0x12FF]], 16) == bytes([0x12, 0xFF])
    # T.81 Annex K: the typical tables hold 12 DC and 162 AC symbols; the first luminance DC code is 00 (Table K.3)
    for bits, values in (JPEG_DC_LUMA, JPEG_DC_CHROMA, JPEG_AC_LUMA, JPEG_AC_CHROMA):
        assert sum(bits) == len(values) and len(set(values)) == len(values)
    assert len(JPEG_AC_LUMA[1]) == len(JPEG_AC_CHROMA[1]) == 162
    assert jpeg_huffman_codes(JPEG_DC_LUMA)[0] == (0, 2) and jpeg_huffman_codes(JPEG_AC_LUMA)[0x00] == (0b1010, 4)
    # T.81 A.3.3: a flat block of 100 has F(0,0) = 1/4 x 1/2 x 64 x 100 = 800 and no AC energy
    assert jpeg_fdct_quantize([100] * 64, [1] * 64) == [800] + [0] * 63
    # CCITT (T.4 Tables 2-3): prefix-free code tables (EOL included); an all-white 1728 line in 1-D is make-up 1728 + white 0,
    # in 2-D against a white reference V(0); a line starting black begins with white 0.
    for table in (CCITT_WHITE_TERM + CCITT_WHITE_MAKEUP + CCITT_EXT_MAKEUP, CCITT_BLACK_TERM + CCITT_BLACK_MAKEUP + CCITT_EXT_MAKEUP):
        codes = table + [CCITT_EOL]
        assert all(a == b or not b.startswith(a) for a in codes for b in codes)
    assert ccitt_encode_1d([False] * 1728) == "010011011" + "00110101"
    assert ccitt_encode_2d([False] * 1728, [False] * 1728) == "1"
    assert ccitt_encode_1d([True] * 3 + [False] * 5) == "00110101" + "10" + "1100"
    assert ccitt_run_codes(2624 + 2560, False) == "000000011111" * 2 + "11011" + "00110101"
    # T.800 J.11: the worked example's samples re-encode to its codestream, except the last byte of each of the two codeword
    # segments (offsets 90 and 97), where its encoder used a shorter termination than C.2.9 FLUSH; both decode alike.
    j11 = bytes.fromhex(
        "FF4FFF510029000000000001000000090000000000000000000000010000000900000000000000000001070101FF5C00074040484850"
        "FF52000C00000001000104040001FF90000A00000000001E0001FF93C7D40C018F0DC8755DC07C21800FB176FFD9")
    ours = j2k_codestream([[[v] for v in [101, 103, 104, 105, 96, 97, 96, 102, 109]]], 8, 1, mct=False)
    assert len(ours) == len(j11) and (ours[90], ours[97]) == (0x7F, 0x7F)
    assert ours[:90] == j11[:90] and ours[91:97] == j11[91:97] and ours[98:] == j11[98:]
    # T.88 H.2: the arithmetic coder test sequence (one context, I = 0, MPS = 0) encodes to the listed 30 bytes once the 0xFF 0xAC
    # marker JBIG2 generic regions end with is appended.
    h2 = bytes.fromhex("00020051000000C00352872AAAAAAAAA82C02000FCD79EF6BF7FED904F46A3BF")
    mq, cx = MqEncoder(), [0]
    for bit in (b >> (7 - i) & 1 for b in h2 for i in range(8)):
        mq.encode(cx, 0, bit)
    assert mq.flush() + b"\xff\xac" == bytes.fromhex("84C73BFCE1A1430402200000410DBB86F4317FFF88FF37471ADB6ADFFFAC")


def main(argv: list[str]) -> int:
    self_test()
    out_dir = argv[1] if len(argv) > 1 else os.path.dirname(os.path.abspath(__file__))
    for name, gen in FILES.items():
        data = gen()
        with open(os.path.join(out_dir, name), "wb") as fh:
            fh.write(data)
        print("%-28s %6d bytes" % (name, len(data)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
