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


XMP = (b'<?xpacket begin="\xef\xbb\xbf" id="W5M0MpCehiHzreSzNTczkc9d"?>\n'
       b'<x:xmpmeta xmlns:x="adobe:ns:meta/">\n'
       b' <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">\n'
       b'  <rdf:Description rdf:about="" xmlns:dc="http://purl.org/dc/elements/1.1/">\n'
       b'   <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Broadside</rdf:li></rdf:Alt></dc:title>\n'
       b'  </rdf:Description>\n'
       b' </rdf:RDF>\n'
       b'</x:xmpmeta>\n'
       b'<?xpacket end="w"?>')


def gen_metadata_xmp() -> bytes:
    return simple_file([
        (1, catalog(b" /Metadata 4 0 R")),
        (2, pages()),
        (3, page()),
        (4, stream(b"/Type /Metadata /Subtype /XML", XMP)),
        (5, b"<< /Title (Broadside) >>"),
    ], trailer_extra=b" /Info 5 0 R")


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


FILES = {
    "empty-page.pdf": gen_empty_page,
    "pdf20-header.pdf": gen_pdf20_header,
    "text-standard14.pdf": gen_text_standard14,
    "text-truetype-embedded.pdf": gen_text_truetype_embedded,
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
    "outline.pdf": gen_outline,
    "name-tree-dests.pdf": gen_name_tree_dests,
    "metadata-xmp.pdf": gen_metadata_xmp,
    "name-tree-deep.pdf": gen_name_tree_deep,
    "number-tree-deep.pdf": gen_number_tree_deep,
    "name-tree-broken.pdf": gen_name_tree_broken,
    "tagged-structure.pdf": gen_tagged_structure,
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
    # RFC 5869 A.1
    assert hkdf_sha256(b"\x0b" * 22, bytes(range(13)), bytes(range(0xF0, 0xFA)), 42) == bytes.fromhex(
        "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865")


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
