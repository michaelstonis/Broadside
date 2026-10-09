# CorpusFetcher

Downloads real-world PDF test corpora into the gitignored `corpus/` folder at the repository root. Corpus tests (`tests/`) read from there; nothing in `corpus/` is ever committed.

## Usage

```sh
dotnet run --project tools/CorpusFetcher -- --list                     # show the manifest
dotnet run --project tools/CorpusFetcher -- --only pdf20examples       # one corpus
dotnet run --project tools/CorpusFetcher -- --only pdfjs,qpdf          # several
dotnet run --project tools/CorpusFetcher                               # everything that is not manual
dotnet run --project tools/CorpusFetcher -- --include-linked           # also fetch pdf.js .link targets
```

| Option | Effect |
|---|---|
| `--only <id,id>` | Fetch only these corpora. |
| `--include-linked` | Download the targets of `.link` files (pdf.js keeps 460 external PDFs this way). Dead links are reported, not fatal. |
| `--list` | Print the manifest and exit. |
| `--corpus-dir <dir>` | Write somewhere other than `<repo>/corpus`. |
| `--manifest <file>` | Use another manifest file. |

The repository root is found by walking up from the current directory to `Broadside.slnx`.

Re-running is a no-op for anything already present: every file in `corpus/manifest.lock.json` is re-hashed, and a corpus whose files all match is skipped without downloading. A corpus with missing or changed files is re-downloaded and only the missing files are written. The exit code is 1 when any corpus failed to download, 0 otherwise.

## Safety

Everything downloaded is untrusted. The tool never executes anything it fetched. Archives are extracted only into `corpus/<id>/`; every entry name is normalized (backslashes to slashes) and rejected when it is absolute, contains `.` or `..` components, a drive or stream separator (`:`), or characters invalid in file names; the resolved path is then checked to lie inside the target folder. Tar entries that are not regular files (symlinks, hard links, devices) are dropped. Downloads go to a `.partial` file and are moved into place only when complete.

## `corpora.json`

One entry per corpus:

| Field | Meaning |
|---|---|
| `id` | Folder name under `corpus/`. |
| `description` | What it is. |
| `license` | License or terms-of-use note. Read it before redistributing anything. |
| `kind` | `git-sparse` (repository archive pinned to a commit, subpath extracted), `zip` (archives extracted as-is; `.tar.gz` is also accepted), `files` (individual URLs saved by file name). |
| `urls` | Download locations. For `git-sparse`, `https://github.com/<org>/<repo>/archive/<sha>.zip` or a Gitiles `+archive/<sha>.tar.gz`. |
| `homepage` | Where to read about the corpus. |
| `ref` | The commit SHA the archive is pinned to (required for `git-sparse`). |
| `stripPrefix` | Leading path components to drop (1 for GitHub archives, which wrap everything in `repo-sha/`; 0 for Gitiles). |
| `subpath` | Only entries under this path are kept. |
| `extensions` | Only files with these extensions are kept (case-insensitive). `.link` files are never written; their first line is parsed as a URL and recorded in the lock. |
| `approximateSize` | For humans. |
| `manual` | `true`: never downloaded, the URL and notes are printed instead. |
| `notes` | Anything else, including when the ref was pinned. |

To bump a corpus, change `ref` and the URL together; the next run sees the ref mismatch and re-fetches.

## `corpus/manifest.lock.json`

Written after each corpus:

```json
{
  "version": 1,
  "corpora": {
    "pdf20examples": {
      "ref": "c20f2c17bfcc4baab7cfe62e70fae64caf14d5fa",
      "fetchedAt": "2026-10-09T16:20:00+00:00",
      "files": [
        { "path": "pdf20examples/Simple PDF 2.0 file.pdf", "size": 5211, "sha256": "..." }
      ],
      "links": [
        { "path": "pdfjs/issue1001.pdf", "url": "https://..." }
      ]
    }
  }
}
```

`path` is relative to `corpus/` with forward slashes. Files fetched from a `.link` target carry a `source` URL. `links` lists every `.link` target found in the archive, so `--include-linked` can fetch them later without re-downloading the archive.

## Corpora and licensing

| id | Source | License / terms | Size |
|---|---|---|---|
| `pdfjs` | mozilla/pdf.js `test/pdfs` @ pinned commit | Apache-2.0 for the repository; linked PDFs belong to their publishers | 106 MB archive, 989 PDFs (119 MB); links extra |
| `pdf20examples` | pdf-association/pdf20examples | CC BY-SA 4.0 | 7 PDFs, 55 KB |
| `verapdf-corpus` | veraPDF/veraPDF-corpus (staging) | CC BY 4.0; embedded Isartor files under PDF Association terms | 149 MB archive, 2906 PDFs (160 MB) |
| `pdfbox` | apache/pdfbox `pdfbox/src/test/resources` | Apache-2.0, see NOTICE | 22 MB archive, 303 files (14 MB) |
| `gwg-output-suite` | gwg.org Ghent PDF Output Suite 5.0 (two zips) | Copyright GWG; use for testing, redistribution needs permission | 24 MB + 132 MB |
| `safedocs` | Digital Corpora CC-MAIN-2021-31-PDF-UNTRUNCATED | Common Crawl terms of use; manual | ~8 TB (1.0-2.8 GB per zip) |
| `pdfium-tests` | pdfium.googlesource.com/pdfium_tests @ PDFium DEPS revision | BSD-3-Clause; third_party/ varies | 318 MB tar.gz, PDFs only kept |
| `qpdf` | qpdf/qpdf `qpdf/qtest/qpdf` | Apache-2.0 | 21 MB archive, 628 PDFs (~35 MB) |
| `bfo-pdfa-testsuite` | bfosupport/pdfa-testsuite | No license file; community test suite by BFO | 340 KB |
| `isartor` | pdfa.org Isartor test suite | PDF Association terms of use; manual (403 to automated clients); also inside `verapdf-corpus` | ~8 MB |

None of these files may be committed to this repository or shipped in a package. Tests that snapshot rendering output of a corpus file must store only hashes or our own renders.
