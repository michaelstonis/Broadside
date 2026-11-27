# Image import test files (issue #642)

Inputs of `TiffImportTests`, `GifImportTests`, `BmpImportTests` and `ImageImportTests`, and seeds of the `tiff`, `gif` and `bmp`
fuzz targets. All are made by `make_import_images.py` (run `python3 -I make_import_images.py`; it needs libtiff's `tiffcp` on the
`PATH`, version 4.7.2 was used):

- The uncompressed TIFF files (`bilevel.tif`, `bilevel-orientation6.tif`, `gray8.tif`, `gray4.tif`, `rgb8.tif`, `smooth8.tif`,
  `rgba8.tif`, `rgb16.tif`, `cmyk8.tif`, `palette8.tif`, `palette4.tif`), every GIF and every BMP are written by the script itself
  (Python standard library only), from the pixel patterns at its top; `ImportSamples` in `tests/Broadside.TestSupport` computes the
  same patterns for the expected values.
- Every other TIFF file is made from those by `tiffcp` (libtiff, BSD-style license): CCITT Group 3 1-D, 2-D and 2-D with fill
  bits, Group 4 (also with `FillOrder` 2 and in 16-row strips), PackBits, tiles, LZW with and without the horizontal predictor,
  Deflate in big-endian byte order, separate planes, JPEG (YCbCr and RGB, one strip and several), and `multipage.tif` (three pages:
  Group 4, LZW, JPEG). libtiff is an encoder independent of Broadside's decoders, so the tests check the importer against what real
  scanners and tools write.

The files are committed so the tests need neither Python nor libtiff; regenerate them only when the script changes.
