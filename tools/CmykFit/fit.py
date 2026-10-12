#!/usr/bin/env python3
"""Fits the DeviceCMYK to sRGB polynomial of Broadside's managed colour management.

ISO 32000-2 §10.4.2.5 gives a "crude" DeviceCMYK to DeviceRGB conversion (red = 1 - min(1, c + k), ...) for processors without
a CMYK characterisation. Viewers instead convert through a SWOP profile, so a rich cyan shows as about (0, 0.68, 0.94), not
(0, 1, 1). Broadside's default approximates that characterisation with a cubic polynomial per sRGB channel whose coefficients
this script derives from public measurement data, so no profile or third-party coefficient table is copied into the core.

Data: ANSI CGATS TR 001-1995 "Color characterization data for Type 1 printing" (SWOP), file TR001CLR.AVG of TR001-DATADISK.zip,
published by the Association for PRINT Technologies at https://printtechnologies.org/standards/characterization-data-sets
(928 IT8.7/3 patches: CMYK dot values and CIELAB, D50, 2 degree observer). The dataset is not redistributed here; pass the path of
the extracted TR001CLR.AVG:

    python3 -I tools/CmykFit/fit.py path/to/TR001CLR.AVG

Pipeline per patch: L*a*b* (D50) to XYZ; media-relative scaling so the paper (0 0 0 0) is the D50 white, as the ICC relative
colorimetric intent does; Bradford adaptation D50 to D65; XYZ to linear sRGB (IEC 61966-2-1); sRGB encoding; clamp to [0, 1].
Then a least-squares fit, per channel, of 1 plus the 34 monomials c^i m^j y^l k^n with 1 <= i + j + l + n <= 3 (inputs as fractions
0..1; the constant is fixed at 1 so that the paper, 0 0 0 0, is exactly white).
Prints the C# coefficient table (term order as generated below) and the fit's error in 8-bit units. Stdlib only, deterministic.
"""

from __future__ import annotations

import itertools
import math
import sys

D50 = (0.9642, 1.0, 0.8249)
D65 = (0.95047, 1.0, 1.08883)
BRADFORD = ((0.8951, 0.2664, -0.1614), (-0.7502, 1.7135, 0.0367), (0.0389, -0.0685, 1.0296))
BRADFORD_INV = ((0.9869929, -0.1470543, 0.1599627), (0.4323053, 0.5183603, 0.0492912), (-0.0085287, 0.0400428, 0.9684867))
XYZ_TO_SRGB = ((3.2404542, -1.5371385, -0.4985314), (-0.9692660, 1.8760108, 0.0415560), (0.0556434, -0.2040259, 1.0572252))


def mul(m, v):
    return tuple(sum(m[r][c] * v[c] for c in range(3)) for r in range(3))


def lab_to_xyz(lab, white):
    l, a, b = lab
    fy = (l + 16) / 116
    fx = fy + a / 500
    fz = fy - b / 200

    def g(t):
        return t ** 3 if t >= 6 / 29 else 108 / 841 * (t - 4 / 29)

    return (white[0] * g(fx), white[1] * g(fy), white[2] * g(fz))


def adapt(xyz, source, target):
    lms = mul(BRADFORD, xyz)
    ws = mul(BRADFORD, source)
    wt = mul(BRADFORD, target)
    return mul(BRADFORD_INV, tuple(lms[i] * wt[i] / ws[i] for i in range(3)))


def encode(c):
    c = 12.92 * c if c <= 0.0031308 else 1.055 * math.copysign(abs(c) ** (1 / 2.4), c) - 0.055
    return min(1.0, max(0.0, c))


def read(path):
    rows = []
    inside = False
    with open(path, encoding="ascii") as fh:
        for line in fh:
            line = line.strip()
            if line == "BEGIN_DATA":
                inside = True
                continue
            if line == "END_DATA":
                break
            if not inside or not line or line.startswith("#"):
                continue
            f = line.split()
            if f[2] == "Paper":
                # The unprinted-paper patch is written as "Paper" instead of four zero dot values.
                rows.append(((0.0, 0.0, 0.0, 0.0), tuple(float(v) for v in f[3:6])))
            else:
                rows.append((tuple(float(v) / 100 for v in f[2:6]), tuple(float(v) for v in f[6:9])))
    return rows


# Every monomial of degree 1 to 3; the constant term is fixed at 1 so that 0 0 0 0 (the paper) is exactly white.
TERMS = [e for d in range(1, 4) for e in itertools.product(range(4), repeat=4) if sum(e) == d]


def features(cmyk):
    return [math.prod(cmyk[i] ** e[i] for i in range(4)) for e in TERMS]


def solve(a, b):
    n = len(b)
    m = [row[:] + [b[i]] for i, row in enumerate(a)]
    for col in range(n):
        pivot = max(range(col, n), key=lambda r: abs(m[r][col]))
        m[col], m[pivot] = m[pivot], m[col]
        for r in range(n):
            if r != col and m[r][col] != 0:
                f = m[r][col] / m[col][col]
                m[r] = [x - f * y for x, y in zip(m[r], m[col])]
    return [m[i][n] / m[i][i] for i in range(n)]


def main(argv):
    rows = read(argv[1])
    papers = [lab for cmyk, lab in rows if cmyk == (0, 0, 0, 0)]
    paper = lab_to_xyz(tuple(sum(p[i] for p in papers) / len(papers) for i in range(3)), D50)
    samples = []
    for cmyk, lab in rows:
        xyz = lab_to_xyz(lab, D50)
        xyz = tuple(xyz[i] * D50[i] / paper[i] for i in range(3))
        rgb = tuple(encode(v) for v in mul(XYZ_TO_SRGB, adapt(xyz, D50, D65)))
        samples.append((features(cmyk), rgb))

    n = len(TERMS)
    ata = [[sum(s[0][i] * s[0][j] for s in samples) for j in range(n)] for i in range(n)]
    coefficients = []
    for channel in range(3):
        atb = [sum(s[0][i] * (s[1][channel] - 1) for s in samples) for i in range(n)]
        coefficients.append(solve(ata, atb))

    errors = [abs(min(1.0, max(0.0, 1 + sum(c * f for c, f in zip(coefficients[ch], s[0])))) - s[1][ch]) * 255
              for s in samples for ch in range(3)]
    print(f"// {len(samples)} patches, {len(papers)} paper samples; error in 8-bit units: rms "
          f"{math.sqrt(sum(e * e for e in errors) / len(errors)):.2f}, max {max(errors):.2f}")
    print("// Terms after the constant 1: " + " ".join("c%dm%dy%dk%d" % e for e in TERMS))
    for name, row in zip(("Red", "Green", "Blue"), coefficients):
        print(f"private static readonly double[] {name} =")
        print("[")
        for i in range(0, n, 5):
            print("    " + " ".join(f"{v:.6f}," for v in row[i:i + 5]))
        print("];")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
