# Known limitations of v1

This is the one list of what Broadside v1 does not do, for the release notes and for anyone deciding whether to depend on it. Each item names the open issue that tracks it. The clause-by-clause detail is in the [conformance map](conformance/README.md): every row there that is neither `done` nor `n/a` names one of the issues below (`Tracked by #N`), and `tools/ConformanceCheck` fails a row that names none.

What v1 leaves out on purpose is not listed as a limitation: JavaScript execution, XFA rendering, 3D, rich media and multimedia playback, printing and OCR are out of scope. Broadside reads those objects as data where the specification defines them and keeps them unchanged when it saves (the conformance map marks them `n/a`, each with a round-trip test). Viewer and printer behavior (page transitions, halftones, separations, trapping) is `n/a` for the same reason: it is not file content.

## Reading and the document model

- **Typed views of preserved data.** Articles and threads, thumbnails, measurement and geospatial dictionaries, page-piece dictionaries, web capture and document parts are kept as stored but have no typed view; some output intent entries are not read and output intents are written only by PDF/A saves; stream data in external files (`F`, `FFilter`) is not read. [#531](https://github.com/michaelstonis/Broadside/issues/531)
- **Forms.** The page's annotation tab order (`Tabs`) is not exposed; rich text field values appear as plain text, and an existing field appearance is replaced rather than updated; FDF import and export are not implemented. [#532](https://github.com/michaelstonis/Broadside/issues/532)
- **Linearization.** Thumbnail, generic, extended generic and embedded file hint tables are neither read nor written (the outline table is written), part ordering is not validated, and an updated linearized file is read as an ordinary one. [#530](https://github.com/michaelstonis/Broadside/issues/530)

## Images and fonts

- **JPEG 2000.** A fragmented codestream (fragment table box) is reported, not reassembled. [#259](https://github.com/michaelstonis/Broadside/issues/259)
- **JBIG2.** Generic regions with extended templates (ITU-T T.88 Amendment 2) are not decoded; the image is declined with a diagnostic. No real-world file in the corpus uses them. [#260](https://github.com/michaelstonis/Broadside/issues/260)
- **Multiple master fonts.** Instances are drawn from the default design unless the program carries a matching weight vector. [#524](https://github.com/michaelstonis/Broadside/issues/524)
- **Complex scripts.** Text layout does not shape Indic, Southeast Asian and other complex scripts. [#230](https://github.com/michaelstonis/Broadside/issues/230)

## Rendering

- **Color management.** ICC version 4 profiles convert through their AToB/BToA or matrix/TRC tags; DToB/BToD (floating-point) tags are not read. [#522](https://github.com/michaelstonis/Broadside/issues/522)
- **Transparency.** CIE-based RGB and gray group color spaces blend as the device space; alpha is shape is not carried; a page without a transparency group is not composited as an isolated group; text knockout is not applied; overprint preview does not cover groups and soft masks in colorant space; transfer functions apply per paint, before compositing. [#523](https://github.com/michaelstonis/Broadside/issues/523)
- **Other rendering cases.** A stencil mask painted with a pattern color is not drawn; reference XObjects draw their proxy, not the referenced page; with anti-aliasing off a zero-area shape paints nothing. [#524](https://github.com/michaelstonis/Broadside/issues/524)
- **Speed and size.** Rendering is slower than PDFium on the pinned real-world pages ([#336](https://github.com/michaelstonis/Broadside/issues/336)), stroking is about twice as slow as before the overlap fix ([#335](https://github.com/michaelstonis/Broadside/issues/335)), and the software rasterizer has no glyph cache ([#370](https://github.com/michaelstonis/Broadside/issues/370)). The core assembly is larger than it needs to be ([#361](https://github.com/michaelstonis/Broadside/issues/361)), and so is the Direct2D backend ([#360](https://github.com/michaelstonis/Broadside/issues/360)). Large images decode slowly in the browser ([#365](https://github.com/michaelstonis/Broadside/issues/365), [#352](https://github.com/michaelstonis/Broadside/issues/352)); the CoreGraphics backend decodes JPEG 2000 in managed code ([#482](https://github.com/michaelstonis/Broadside/issues/482)).

## Text extraction and tagged PDF

- **Tagged PDF reading.** Words divided by a soft hyphen are not joined; annotations are not placed in the logical reading order and `ReversedChars` is not applied; implicit table headers and continued lists are not linked; `Lang`, `Alt` and `E` on marked content (rather than on structure elements) are not applied; pronunciation hints are not read. [#529](https://github.com/michaelstonis/Broadside/issues/529)
- **Structure validation.** The nesting rules of the standard structure elements (Annex L) and the structure namespace rules of ISO/TS 32005 are not checked. [#528](https://github.com/michaelstonis/Broadside/issues/528)

## Conformance output

- **PDF/UA and Well-Tagged PDF.** A PDF/UA-1, PDF/UA-2 or WTPDF save checks what a flow document can get wrong; requirements a read and edited document can break elsewhere (optional content, embedded files, signatures, navigation, actions, XObjects, mathematical expressions) are left to a validator. [#527](https://github.com/michaelstonis/Broadside/issues/527)
- **PDF/A.** A PDF/A-2, -3 or -4 save checks the rules listed in the conformance map; image bit depths, JPEG 2000 data, font program internals of fonts from read files, signatures and a few stream and optional content rules are left to veraPDF. [#533](https://github.com/michaelstonis/Broadside/issues/533)

## Signatures

- **Algorithms.** EdDSA (Ed25519, Ed448), SHAKE256 and brainpool curves for signing are not supported. [#525](https://github.com/michaelstonis/Broadside/issues/525)
- **Signature features.** Seed value dictionaries have no typed view; `Changes`, `Prop_Build` and `Cert` are not written; usage rights signatures and PKCS #1 (`adbe.x509.rsa_sha1`) signatures are not validated; signing embeds no revocation data in the CMS; CAdES signature policies and signer location are not handled; validation does not restrict a signature to its VRI entries; DocMDP is enforced at validation but other edits of a certified document are not refused; legal content attestations are not read. [#526](https://github.com/michaelstonis/Broadside/issues/526)
- **Signatures in the browser.** .NET on WebAssembly has no X.509 support, so signatures are listed but not validated there.
