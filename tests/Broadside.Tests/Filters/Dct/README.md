# DCTDecode test vectors

JPEG vectors for the DCTDecode filter (issue #61; ISO 32000-2 §7.4.8, ITU-T T.81) and their reference decodes. Everything here
is rebuilt by `make_vectors.py`:

```sh
python3 -I tests/Broadside.Tests/Filters/Dct/make_vectors.py <download dir> <output dir> tests/Corpus/dct-baseline.pdf
```

## Sources

- `testorig.jpg`, `testimgint.jpg` and the source image `testorig.ppm` (not committed) are libjpeg-turbo's
  `testimages/` at commit `43ea809765004508d8da72ebe562cf570c6a62b2`
  (SHA-256 `acc6ec55…ec73b`, `491679b8…90963`, `4afe49cb…1a002`). They were part of the Independent JPEG Group's software
  and are provided under the IJG License (`src/Broadside/Filters/Dct/README.ijg`, LEGAL ISSUES). 227 x 149, SOF0, JFIF,
  YCbCr 4:2:0.
- Every other vector is derived from them with libjpeg-turbo 3.2.0 (`cjpeg -dct int` on the 75 x 43 crop at (80, 40) of
  `testorig.ppm`, `jpegtran`, or a byte patch), so it is under the same licence:

| Vector | Made with | Covers |
|---|---|---|
| `sampling-444.jpg` | `cjpeg -sample 1x1` | 4:4:4, partial blocks |
| `sampling-422.jpg` | `cjpeg -sample 2x1 -restart 1` | 4:2:2, restart interval of one MCU row |
| `sampling-440.jpg` | `cjpeg -sample 1x2` | 4:4:0 |
| `sampling-420.jpg` | `cjpeg -sample 2x2 -restart 1` | 4:2:0, restart interval of one MCU row |
| `sampling-411.jpg` | `cjpeg -sample 4x1,1x1,1x1` | 4:1:1, 32-sample-wide MCUs |
| `gray-2x2.jpg` | `cjpeg -grayscale -sample 2x2` | one component with factors 2x2: a non-interleaved scan over ceil(X/8) x ceil(Y/8) blocks |
| `rgb.jpg` | `cjpeg -rgb` | APP14 transform 0, component identifiers R, G, B |
| `multiscan.jpg` | `jpegtran -scans` (`0;` `1;` `2;`) of `sampling-420.jpg` | one sequential scan per component (buffered coefficients) |
| `restart-blocks.jpg` | `jpegtran -restart 2B testorig.jpg` | restart interval in blocks |
| `sof1.jpg` | `sampling-420.jpg` with C0 -> C1 | extended sequential frame |
| `dnl.jpg` | `sampling-420.jpg` with Y = 0 and `FF DC 00 04 00 2B` before EOI | number of lines from DNL (djpeg refuses it: "DNL not supported") |
| `no-dht.jpg` | `sampling-420.jpg` without its DHT segment | Annex K.3 tables when none are defined |

## Goldens

`<vector>.ppm.gz` / `.pgm.gz` = `djpeg -dct int -nosmooth -pnm <vector>.jpg` (libjpeg-turbo 3.2.0, `JSIMD_FORCENONE=1`;
the SIMD build gives the same bytes), gzipped with mtime 0. `-dct int` is the islow IDCT the decoder ports; `-nosmooth`
replicates subsampled components as Adobe's DCTDecode does (default djpeg interpolates, which differs by up to 34). Vectors
with the same content as another (`multiscan`, `sof1`, `no-dht`, `dnl` -> `sampling-420`; `restart-blocks` -> `testorig`)
have no golden of their own; `make_vectors.py` checks that djpeg decodes them identically. The managed decoder is bit-exact
with every golden; the tests assert the acceptance criterion, ±1 per sample.

`dct-baseline-color.ppm.gz` and `dct-baseline-gray.pgm.gz` are the goldens of the two images of `tests/Corpus/dct-baseline.pdf`
(written by `generate.py`'s `jpeg_encode`), extracted with `pdfimages -j`; regenerate them when that file changes.

## Real-world references

`make_corpus_references.py <corpus dir>` writes `<corpus dir>/references/dct/<sha-256>.pnm` for every baseline 1- or
3-component JPEG of the fetched corpora that djpeg decodes without a warning (180 files, 274 MB, about 10 minutes);
`CorpusDctReferenceTests` compares against them and skips when they are absent.
