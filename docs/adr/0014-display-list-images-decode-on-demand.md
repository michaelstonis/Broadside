# Display lists hold images that decode on demand

Issue #362: a large photograph drawn much smaller than its resolution, the normal case on phones and in fit-to-width views, dominated render time. We decoded every sample, converted it and box-filtered it, then threw away about 98% of it (`docs/research/image-decode-scaled-and-native.md`). A reduced decode cannot happen at list build: a display list is device-independent and is replayed at many scales, so the resolution an image needs is known only when a backend draws it. This amends ADR 0006's "decoded images" for images whose codec decodes reduced resolutions.

When a list is built, an image whose codec says in its header that it decodes reduced resolutions (`ImageHeader.MaxReduction` above 0, the DCT and JPEG 2000 codecs) is no longer decoded. The builder reads the header, applies the size limits, and keeps the encoded data that remains after the filters that precede the codec, with shallow copies of the image and parameter dictionaries. A backend asks `DisplayImage.GetSamplesAt(reduction)` when it draws. The reduction is chosen by one rule, `DisplayImage.GetReduction`: the largest power of two that still leaves k samples per device pixel along the image's most enlarged direction, k = 2 by default and configurable, capped by what the codec delivers and by the colour conversion. Each reduction is decoded at most once per image behind a per-reduction gate, so concurrent tile replays decode once. The samples are kept in the document's image cache under the image cache budget, keyed by the image and the reduction. A finer reduction already decoded serves a coarser request. The samples carry the source size and the delivered reduction, so geometry stays exact: ⌈W / s⌉ samples span ⌈W / s⌉·s / W of the unit square, clipped at 1.

Every other image (Flate and raw samples, CCITT, JBIG2, inline images, masks, an image without a `ColorSpace`, `SMaskInData`, strict mode) is still decoded at build, as before.

Reduced decodes are for display only. `RenderOptions.ScaledImageDecode` is off by default, so `Render(page)` callers, printing, export and image extraction always use full-resolution samples; the viewer's tile backend turns it on. `IImageFilter` gains an optional reduction, `ImageFilterContext.Reduction` in and `DecodedImage.Reduction` out, which extends an existing extension point; no new one is added (ADR 0001).

## Consequences

- A list stays self-contained: the encoded bytes are in memory, so a backend still never reads the file, and replays still need no lease from the document session's edit gate.
- Decode diagnostics of an image kept encoded are recorded when it is first drawn, not when its list is built. A decode that fails then paints nothing and is reported once. The documentation of `DisplayImage` says so.
- The image cache may hold several reductions of one image at once; the coarser ones are small (a quarter of a 20-megapixel photo is 3.75 MB, against 60 MB in full). Samples larger than the budget, or with a budget of 0, are held by the image for as long as its list is.
- A codec averages samples in its own space, before the PDF colour conversion. That equals averaging after conversion only for an affine conversion, so a curved conversion caps the reduction at 1/2 (1/4 for CalGray and CalRGB tone curves) and an Indexed image is never reduced before conversion; the rasterizer averages the rest after converting.
- The platform backends keep using full-resolution samples through the existing `DisplayImage` members until they adopt the rule.

## Considered Options

- Keep decoding at build and resample afterwards: saves nothing of the decode and keeps the full-size samples in memory.
- Choose the reduction when the list is built, from a scale hint: a list is replayed at many scales (tiles, zoom), so it would decode either too much or too little.
- A separate image source object held by backends: the rule and the cache would then be repeated in each backend. One `DisplayImage` method keeps the rule in one place.
