//go:build ignore

// Reference pixels for the PNG vectors (issue #153), decoded by Go's image/png, a decoder independent of Broadside's.
//
//	go run tests/Broadside.Tests/Graphics/Png/make_png_references.go tests/Broadside.Tests/Graphics/Png
//
// For every *.png in pngsuite/ and generated/ it writes <name>.rgba64.gz next to it: width x height pixels, row by row, each
// as four big-endian 16-bit values R, G, B, A, NOT premultiplied, gzipped without a timestamp. Samples of fewer than 16 bits
// are scaled to 16 bits the way PNG and PDF both scale them (v * 65535 / (2^n - 1)); a gray sample is repeated in R, G and B.
// image/png ignores gAMA, sBIT, bKGD and iCCP, so these are the samples as stored.
package main

import (
	"bytes"
	"compress/gzip"
	"fmt"
	"image"
	"image/color"
	"image/png"
	"os"
	"path/filepath"
	"sort"
)

func main() {
	root := os.Args[1]
	for _, dir := range []string{"pngsuite", "generated"} {
		files, err := filepath.Glob(filepath.Join(root, dir, "*.png"))
		if err != nil {
			panic(err)
		}
		sort.Strings(files)
		for _, file := range files {
			if err := reference(file); err != nil {
				panic(fmt.Sprintf("%s: %v", file, err))
			}
		}
	}
}

func reference(file string) error {
	data, err := os.ReadFile(file)
	if err != nil {
		return err
	}
	img, err := png.Decode(bytes.NewReader(data))
	if err != nil {
		return err
	}
	bounds := img.Bounds()
	var out bytes.Buffer
	put := func(v uint16) { out.WriteByte(byte(v >> 8)); out.WriteByte(byte(v)) }
	for y := bounds.Min.Y; y < bounds.Max.Y; y++ {
		for x := bounds.Min.X; x < bounds.Max.X; x++ {
			r, g, b, a := straight(img, x, y)
			put(r)
			put(g)
			put(b)
			put(a)
		}
	}
	var gz bytes.Buffer
	w, _ := gzip.NewWriterLevel(&gz, gzip.BestCompression)
	if _, err := w.Write(out.Bytes()); err != nil {
		return err
	}
	if err := w.Close(); err != nil {
		return err
	}
	return os.WriteFile(file[:len(file)-len(".png")]+".rgba64.gz", gz.Bytes(), 0o644)
}

// straight returns the pixel's non-premultiplied 16-bit components, read from the concrete type so nothing is lost.
func straight(img image.Image, x, y int) (uint16, uint16, uint16, uint16) {
	switch m := img.(type) {
	case *image.Gray:
		v := uint16(m.GrayAt(x, y).Y) * 0x101
		return v, v, v, 0xFFFF
	case *image.Gray16:
		v := m.Gray16At(x, y).Y
		return v, v, v, 0xFFFF
	case *image.RGBA:
		c := m.RGBAAt(x, y)
		if c.A != 0xFF {
			panic("translucent RGBA")
		}
		return uint16(c.R) * 0x101, uint16(c.G) * 0x101, uint16(c.B) * 0x101, 0xFFFF
	case *image.RGBA64:
		c := m.RGBA64At(x, y)
		if c.A != 0xFFFF {
			panic("translucent RGBA64")
		}
		return c.R, c.G, c.B, 0xFFFF
	case *image.NRGBA:
		c := m.NRGBAAt(x, y)
		return uint16(c.R) * 0x101, uint16(c.G) * 0x101, uint16(c.B) * 0x101, uint16(c.A) * 0x101
	case *image.NRGBA64:
		c := m.NRGBA64At(x, y)
		return c.R, c.G, c.B, c.A
	case *image.Paletted:
		switch c := m.Palette[m.ColorIndexAt(x, y)].(type) {
		case color.RGBA:
			if c.A != 0xFF {
				panic("translucent palette RGBA")
			}
			return uint16(c.R) * 0x101, uint16(c.G) * 0x101, uint16(c.B) * 0x101, 0xFFFF
		case color.NRGBA:
			return uint16(c.R) * 0x101, uint16(c.G) * 0x101, uint16(c.B) * 0x101, uint16(c.A) * 0x101
		default:
			panic(fmt.Sprintf("palette colour %T", c))
		}
	default:
		panic(fmt.Sprintf("image type %T", img))
	}
}
