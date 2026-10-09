# Two-layer object model, both layers public

The library exposes a low-level COS layer (dictionaries, arrays, names, streams, references exactly as the file stores them) and a high-level document model (Page, Font, Annotation, Form field), and both are public API. A document-model object is a live view over its COS object, never a copy, so a change through either layer is immediately visible through the other.

Hiding COS would make the library unable to handle the many real-world files that contain structures no typed model anticipates, and would deny power users the escape hatch every serious PDF library (PDFBox, iText, PdfPig, PDFium) provides. Making the document model a copy rather than a view would create the two-sources-of-truth problem that incremental update and round-trip fidelity cannot tolerate.
