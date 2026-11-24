---
title: "Signatures"
---

# Signatures

This guide is being written for v1 ([#270](https://github.com/michaelstonis/Broadside/issues/270)). Like the [reading guide](reading.md), its code will come from compiled samples that CI runs.

It will show, with runnable samples: signing at each PAdES level, timestamps, long-term validation data, external signers such as hardware tokens and cloud key services, and validating the signatures of a document. Every option that reaches the network will be shown with a client the application supplies.

Until then, `PdfDocument.Sign`, `PdfDocument.SignAsync` and `PdfDocument.ValidateSignatures` are documented in the [API reference](../api/readme.md).
