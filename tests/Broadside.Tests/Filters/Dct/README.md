# DCTDecode test vectors

JPEG vectors for the DCTDecode filter (issues #61 and #62; ISO 32000-2 §7.4.8, ITU-T T.81) and their reference decodes.
Everything here is rebuilt by `make_vectors.py`:

```sh
python3 -I tests/Broadside.Tests/Filters/Dct/make_vectors.py <download dir> <output dir> tests/Corpus
```

## Sources

- `testorig.jpg`, `testimgint.jpg`, `testimgari.jpg` and the source image `testorig.ppm` (not committed) are libjpeg-turbo's
  `testimages/` at commit `43ea809765004508d8da72ebe562cf570c6a62b2`
  (SHA-256 `acc6ec55…ec73b`, `491679b8…90963`, `4672c7f0…89b38`, `4afe49cb…1a002`). `testimgari.jpg` is the arithmetic-coded
  (SOF9, DAC) encoding of the `testimgint.jpg` picture. They were part of the Independent JPEG Group's software
  and are provided under the IJG License (`src/Broadside/Filters/Dct/README.ijg`, LEGAL ISSUES). 227 x 149, SOF0, JFIF,
  YCbCr 4:2:0.
- Every other vector is derived from them with libjpeg-turbo 3.2.0 (`cjpeg -dct int` on the 75 x 43 crop at (80, 40) of
  `testorig.ppm`, `jpegtran`, TurboJPEG for four components, or a byte patch), so it is under the same licence:

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
| `progressive.jpg` | `jpegtran -progressive testorig.jpg` | SOF2: interleaved DC (Al 1), AC bands, refinements of both |
| `progressive-gray-2x2.jpg` | `jpegtran -progressive gray-2x2.jpg` | progressive single component with factors 2x2 |
| `progressive-restart.jpg` | `jpegtran -progressive -restart 3B sampling-420.jpg` | EOBRUN and predictors reset at restart markers mid-row |
| `progressive-scans.jpg` | `jpegtran -scans` (script in `make_vectors.py`) of `sampling-444.jpg` | three successive-approximation steps for DC and AC, bands out of order |
| `arithmetic.jpg` | `jpegtran -arithmetic sampling-420.jpg` | SOF9 sequential arithmetic coding, DAC, restart intervals |
| `arithmetic-progressive.jpg` | `jpegtran -arithmetic -progressive testorig.jpg` | SOF10 progressive arithmetic coding |
| `arithmetic-progressive-scans.jpg` | `jpegtran -arithmetic -scans` (same script) `-restart 5B` of `sampling-444.jpg` | SOF10 with deep refinement and restarts (statistics reset) |
| `precision12.jpg` | `cjpeg -precision 12 -sample 2x2 -restart 1` | SOF1, 12-bit, 4:2:0 |
| `precision12-gray.jpg` | `cjpeg -precision 12 -grayscale -quality 15` | SOF1, 12-bit, one component, 16-bit (Pq 1) quantization table |
| `precision12-progressive.jpg` | `jpegtran -progressive precision12.jpg` | SOF2, 12-bit |
| `precision12-arithmetic.jpg` | `jpegtran -arithmetic -progressive precision12.jpg` | SOF10, 12-bit |
| `cmyk.jpg` | TurboJPEG `tj3Compress8`, `TJCS_CMYK`, 4:4:4, quality 85, of the crop as CMYK (C = 255 - R, M = 255 - G, Y = 255 - B, K a ramp) | APP14 transform 0, four components, no transform |
| `ycck.jpg` | the same with `TJCS_YCCK`, 4:2:0 | APP14 transform 2: YCCK to CMYK |
| `ycck-precision12.jpg` | the same with `tj3Compress12` (samples scaled to 12 bits) | 12-bit YCCK |
| `ycck-progressive.jpg`, `ycck-arithmetic.jpg` | `jpegtran -progressive`, `jpegtran -arithmetic` of `ycck.jpg` | four-component progressive and arithmetic-coded |

## Goldens

`<vector>.ppm.gz` / `.pgm.gz` = `djpeg -dct int -nosmooth -pnm <vector>.jpg` (libjpeg-turbo 3.2.0, `JSIMD_FORCENONE=1`;
the SIMD build gives the same bytes), gzipped with mtime 0. For 12-bit vectors djpeg writes maxval 4095; the tests reduce each
sample to 8 bits by (255 v + 2047) / 4095, as the filter must deliver 8 bits (ISO 32000-2 Table 87). Four-component vectors
have a `.pam.gz` instead: a PAM (P7, TUPLTYPE CMYK) of TurboJPEG's raw CMYK decode (`tj3Decompress8`/`tj3Decompress12` with
`TJPF_CMYK`, `TJPARAM_FASTUPSAMPLE` 1 = replication, `TJPARAM_FASTDCT` 0 = islow), which converts YCCK to CMYK without inverting;
djpeg's PPM output is not used for CMYK because it converts to RGB assuming Adobe-inverted CMYK. `-dct int` is the islow IDCT the decoder ports; `-nosmooth`
replicates subsampled components as Adobe's DCTDecode does (default djpeg interpolates, which differs by up to 34). Vectors
with the same content as another (`multiscan`, `sof1`, `no-dht`, `dnl` -> `sampling-420`; `restart-blocks` -> `testorig`)
have no golden of their own; `make_vectors.py` checks that djpeg decodes them identically. The managed decoder is bit-exact
with every golden (12-bit ones after the reduction); the tests assert the acceptance criterion, ±1 per sample. The
progressive, arithmetic-coded and 12-bit re-encodings by jpegtran keep the quantized coefficients, so they have no golden of
their own: `make_vectors.py` checks that each decodes exactly like its source (`progressive*`, `arithmetic*` -> `testorig`,
`gray-2x2`, `sampling-420` or `sampling-444`; `precision12-progressive`, `precision12-arithmetic` -> `precision12`;
`ycck-progressive`, `ycck-arithmetic` -> `ycck`; `testimgari` -> `testimgint`).

`dct-baseline-color.ppm.gz` and `dct-baseline-gray.pgm.gz` are the goldens of the two images of `tests/Corpus/dct-baseline.pdf`
(written by `generate.py`'s `jpeg_encode`), `dct-progressive.ppm.gz` of `dct-progressive.pdf` (`jpeg_encode_progressive`) and
`dct-cmyk-cmyk.pam.gz` and `dct-cmyk-ycck.pam.gz` of the two images of `dct-cmyk.pdf`, extracted with `pdfimages -j`; regenerate
them when those files change.

## Real-world references

`make_corpus_references.py <corpus dir>` writes `<corpus dir>/references/dct/<sha-256>.pnm` for every baseline, extended,
progressive or arithmetic-coded JPEG of the fetched corpora that libjpeg-turbo decodes without a warning: djpeg for one or three
components, a TurboJPEG raw CMYK PAM for four (208 files, about 10 minutes; the fetched corpora hold no arithmetic-coded or
12-bit JPEG); `CorpusDctReferenceTests` compares against them (330 streams) and skips when they are absent.
