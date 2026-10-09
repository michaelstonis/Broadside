# Availability of the PDF subset standards (PDF/A, PDF/UA, PDF/X, PDF/VT, PDF/E)

Researched 2026-10-09. Prices are as displayed on that date; ISO lists CHF, ANSI lists USD (ANSI pages also showed a second, ~20% lower figure, presumably a member/promotional price). Purchased ISO PDFs are single-user licensed.

## Summary

| Standard | Free of charge? | iso.org page | ISO price | ANSI webstore page | ANSI price |
|---|---|---|---|---|---|
| ISO 19005-1:2005 (PDF/A-1) | No (only Cor.1 and Cor.2 are free, via the PDF Association store) | https://www.iso.org/standard/38920.html | CHF 159 | https://webstore.ansi.org/standards/iso/iso190052005 | $261.00 |
| ISO 19005-2:2011 (PDF/A-2) | No | https://www.iso.org/standard/50655.html | CHF 181 | https://webstore.ansi.org/standards/iso/iso190052011 | $297.00 |
| ISO 19005-3:2012 (PDF/A-3) | No | https://www.iso.org/standard/57229.html | CHF 181 | https://webstore.ansi.org/standards/iso/iso190052012 | $297.00 |
| ISO 19005-4:2020 (PDF/A-4) | No | https://www.iso.org/standard/71832.html | CHF 159 | https://webstore.ansi.org/standards/iso/iso190052020 | $261.00 |
| ISO 14289-1:2014 (PDF/UA-1) | **Yes**, sponsored by the PDF Association (see below) | https://www.iso.org/standard/64599.html | CHF 100 | https://webstore.ansi.org/standards/iso/iso142892014 | $164.00 |
| ISO 14289-2:2024 (PDF/UA-2) | **Yes**, sponsored by the PDF Association (see below) | https://www.iso.org/standard/82278.html | CHF 181 | https://webstore.ansi.org/standards/iso/iso142892024 | $297.00 |
| ISO 15930-7:2010 (PDF/X-4, PDF/X-4p) | No | https://www.iso.org/standard/55843.html | CHF 159 | https://webstore.ansi.org/standards/iso/iso159302010 | $261.00 |
| ISO 15930-8:2010 (PDF/X-5g/-5n/-5pg) | No | https://www.iso.org/standard/55844.html | CHF 100 | Not located (the ANSI slug `iso159302010` resolves to part 7); use https://webstore.ansi.org/ search for "15930-8" | n/a |
| ISO 16612-2:2010 (PDF/VT-1, PDF/VT-2) | No | https://www.iso.org/standard/46428.html | CHF 181 | https://webstore.ansi.org/standards/iso/iso166122010 | $297.00 |
| ISO 24517-1:2008 (PDF/E-1) | No | https://www.iso.org/standard/42274.html | CHF 135 | https://webstore.ansi.org/standards/iso/iso245172008 | $222.00 |
| ISO 24517-2 (PDF/E-2) | No published ISO standard found; it reached DIS stage years ago and does not appear in ISO's catalogue or the PDF Association's ISO work list. Treat as non-existent for implementation purposes. | n/a | n/a | n/a | n/a |

ISO's own "free read" option (the Online Browsing Platform, https://www.iso.org/obp) only shows the preview (foreword, introduction, scope, terms) for these documents; none of them is in ISO's Publicly Available Standards list.

The PDF Association also resells ISO 19005-1..4 via its own store (https://www.pdfa-inc.org/cart/?add-to-cart=610 / 608 / 607 / 594 for parts 1-4; PDF/A-1 corrigenda 1 and 2 are free: add-to-cart=636 and 609). Prices there were not checked (cart-only flow).

## What is free (sponsored by the PDF Association)

https://pdfa.org/sponsored-standards/ lists two no-cost bundles:

1. **ISO 32000-2 bundle** (ISO 32000-2:2020 Errata Collection 3, ISO/TS 32001-32004, plus PDF Association publications): already in `Specs/`.
2. **PDF/UA bundle** (updated 2 Sept 2025, sponsored by Allyant, Axes4, GrackleDocs, TargetStream): **ISO 14289-1:2014, ISO 14289-2:2024, ISO/TS 32005:2023**, plus WTPDF, "Tagged PDF Best Practice Guide: Syntax" and "PDF Declarations". Download: https://www.pdfa-inc.org/product/pdf-ua-bundle/ (every item is $0.00, but delivery is a web-shop checkout that asks for an email address, so it was not automated; a human should place the $0 order and drop the PDFs into `Specs/`). Note that WTPDF 1.0, ISO/TS 32005 and PDF Declarations in that bundle are already present in `Specs/`; only the two ISO 14289 parts are new.

## Free companion material that substitutes in practice

Downloaded into `Specs/References/` (see its README for sizes and URLs):

- **Matterhorn Protocol 1.1** (PDF/UA-1 checkpoints, 31 checkpoints / 136 failure conditions). This is the de-facto validation spec for PDF/UA-1 and is what veraPDF's PDF/UA-1 profile is built from.
- **PDF/A in a Nutshell 2.0** (overview of PDF/A-1/-2/-3 requirements; useful orientation, not a normative substitute).

Not PDFs, but the most useful practical substitutes for the paid PDF/A texts:

- **veraPDF validation profiles and rule documentation**: every "shall" of PDF/A-1a/b, -2a/b/u, -3a/b/u, -4/-4e/-4f, PDF/UA-1, PDF/UA-2 and WTPDF 1.0 is encoded as a machine rule with the clause number, object type, test expression and error description.
  - Rules: https://docs.verapdf.org/validation/ and https://github.com/veraPDF/veraPDF-validation-profiles/wiki (pages `PDFA-Part-1-rules`, `PDFA-Parts-2-and-3-rules`, `PDFA-Part-4-rules`, `PDFUA-Part-1-rules`, `PDFUA-Part-2-rules`, `WTPDF-1.0-rules`).
  - XML profiles: https://github.com/veraPDF/veraPDF-validation-profiles
  - Test corpus (Isartor, BFO, veraPDF-generated PDF/A and PDF/UA test files, each with expected pass/fail and clause): https://github.com/veraPDF/veraPDF-corpus
  Together these give a clause-by-clause map of PDF/A and PDF/UA requirements that is sufficient to implement and test conformance checking; they do not reproduce the standards' prose.
- **PDF Association Technical Note 0010** "Clarifications of ISO 19005 parts 1-3 for developers of PDF/A creators and validators" (2017) and TN 0001-0009 on PDF/A-1 (namespaces, colour, metadata, signatures, XMP) are linked from https://pdfa.org/resource/iso-19005-pdfa/.
- **WTPDF 1.0** (already in `Specs/`) is technically equivalent to PDF/UA-2's requirements ("Well-Tagged PDF" conformance level for accessibility was standardized as ISO 14289-2), so PDF/UA-2 can be implemented from WTPDF + ISO/TS 32005 even before the free ISO 14289-2 copy is fetched.
- PDF/A-4 errata resolutions: https://pdf-issues.pdfa.org/ (also covers ISO 32000-2).

For PDF/X, PDF/VT and PDF/E there is no free substitute; the Ghent Workgroup (https://gwg.org) publishes free PDF/X-4-based output specifications and test suites, which are useful for testing but not a replacement for ISO 15930-7/-8.

## Recommendation

- Fetch the free PDF/UA bundle (ISO 14289-1 and -2) manually; no purchase needed.
- Buy ISO 19005-2:2011 and 19005-4:2020 first if PDF/A output is in scope (CHF 181 + CHF 159 at iso.org); PDF/A-3 is PDF/A-2 plus arbitrary attachments and PDF/A-1 is largely superseded by -2 for new output, so they can wait. Until then, implement against the veraPDF rule set.
- Defer PDF/X, PDF/VT and PDF/E purchases until those profiles are scheduled.
