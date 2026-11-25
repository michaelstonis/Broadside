---
title: "Documentation"
---

# Documentation

Broadside's packages are on NuGet.org as the 1.0 release candidate (`1.0.0-rc.N`); 1.0.0 follows the soak period, and the public API is frozen for 1.0. These pages say how to start using it, how it is designed, what is implemented, and how to follow or join the work.

- [Getting started](getting-started.md): installation, opening, inspecting and saving a file, the viewers and the samples.
- [Guides](guides.md): each part of the library with code from compiled, tested samples: [reading](reading.md), [rendering](rendering.md), [writing](writing.md), [building documents](building-documents.md), [editing](editing.md), [extracting text](extracting-text.md), [signatures](signatures.md), [accessibility and conformance](accessibility-and-conformance.md), [viewers](viewers.md) and [migration](migration.md).
- [Architecture](architecture.md): the terms used throughout the code and the architecture decision records.
- [Roadmap](roadmap.md): the phases and their status, how releases are made, and the known issues.
- [Performance](performance.md): how fast Broadside opens, renders and extracts text next to PDFium and pdf.js.
- [Versioning and support](versioning.md): how packages are numbered, the 1.x support policy, and where release notes come from.
- [Conformance](conformance.md): the per-clause record of what is implemented.
- [Diagnostic codes](diagnostics.md): every code a repair can record, what it means and how it is repaired, and the stability policy that says how codes may change between releases.
- [Contributing](contributing.md): the issue, branch and pull request flow.
- [API review checklist](api-review.md): the rules every public API change follows, and which of them the tests enforce.
- [API coverage](api-coverage.md): which target framework each package's API reference is built from, and the API of the one package it cannot build.

The [API reference](../api/readme.md) is generated from the XML documentation of every package on every build of this site.
