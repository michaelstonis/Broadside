---
name: writing-a-corpus-file
description: "Use when a test needs a hand-written minimal PDF that does not exist yet in tests/Corpus/: add a generator to generate.py, regenerate deterministically, verify with qpdf and poppler, and register the file in the README, Corpus.cs and the Verify snapshot."
---

# Writing a corpus file

`tests/Corpus/` holds hand-written PDFs, one feature per file, all emitted by `tests/Corpus/generate.py` (stdlib only, deterministic). A file is as small as it can be while remaining a complete document: catalog, page tree, one page, plus only the objects the feature needs. If a test needs two features combined, that is a new file.

## 1. How the script defines a file

`FILES` at the bottom of `generate.py` maps a file name to a zero-argument function that returns the bytes:

```python
FILES = {
    "empty-page.pdf": gen_empty_page,
    "text-truetype-embedded.pdf": gen_text_truetype_embedded,
    ...
}
```

Building blocks, in the order the script defines them:

- Serialization (§7.3, §7.5): `header(version, binary)`, `obj(num, body)`, `stream(dict_entries, data, length=None)`, `catalog()`, `pages(kids)`, `page(parent, mediabox, contents, font, resources, extra)`, `HELVETICA`, `text_content(text)`, `xref_table(...)`.
- `simple_file(objects, version, binary, trailer_extra)`: a list of `(num, body)` pairs becomes a file with a classic cross-reference table and a `/Size /Root` trailer; offsets are computed for you. `text_file(content)` is the one-Helvetica-line shortcut.
- `File`: for anything the simple path cannot express. `add(num, body)` records the offset, `raw(bytes)` appends verbatim, `pos()` is the current offset, `finish_classic(trailer)` writes the table. `gen_xref_stream`, `gen_object_stream`, `gen_incremental_update` and `gen_hybrid_xref` show cross-reference streams (`xref_stream_rows`), object streams (`object_stream`) and update sections.
- Filters (§7.4): `flate`, `lzw_encode`, `ascii85_encode`, `asciihex_encode`, `runlength_encode`, `png_up_predict`.
- Encryption (§7.6): `encrypted_file(name, version, label, encrypt_dict, encrypt_stream)` with `gen_encrypted_legacy` (R2–R4) and `gen_encrypted_aes_256` (R6); primitives `rc4`, `aes_cbc_encrypt`, `legacy_file_key`, `legacy_o_entry`, `legacy_u_entry`, `object_key`, `hash_2b`.
- Determinism: every value the spec wants random comes from `file_id(name)` or `fixed_bytes(label, n)`. Never call `os.urandom` or `random`.
- Font programs: `minimal_truetype()` synthesizes a licence-free TrueType font with `.notdef`, `H`, `I` as rectangles. Follow that pattern for other formats; never embed a real typeface.
- `self_test()` runs known-answer tests for the codecs before anything is written; add one when you add a codec.

## 2. Naming

Kebab-case, feature first, variant second: `flate-stream.pdf`, `encrypted-aes-256.pdf`, `text-truetype-embedded.pdf`. Deliberately broken files are named by the defect: `missing-endobj.pdf`. The README table and `Corpus.cs` use the same name.

## 3. Steps

1. Write `gen_<name>()` next to its neighbours in `generate.py` and register it in `FILES`. Cite the clauses in the docstring.
2. Regenerate into a scratch directory and diff. Only your new file may differ ("Only in"); any other difference means you changed shared code and must explain it in the PR:

   ```sh
   python3 -I tests/Corpus/generate.py /tmp/corpus-regen
   diff -rq /tmp/corpus-regen tests/Corpus --exclude=README.md --exclude=generate.py
   python3 -I tests/Corpus/generate.py          # then write in place
   ```

3. Verify with external tools and record what they print:

   ```sh
   qpdf --check tests/Corpus/<name>.pdf          # exit 0, "No syntax or stream encoding errors found"
   pdfinfo tests/Corpus/<name>.pdf               # one Letter page, no syntax message
   pdftotext tests/Corpus/<name>.pdf -           # the text the content stream draws
   pdffonts tests/Corpus/<name>.pdf              # for font files: type, encoding, emb=yes
   ```

   A broken file is verified the other way round: record exactly which complaint qpdf and poppler produce.
4. Add a row to the right table in `tests/Corpus/README.md` (well-formed: File, Feature, Clauses, How to verify, Result; broken: File, Defect, Clauses, What the tools report, Lenient mode).
5. Add the name to `WellFormed` or `Malformed` in `tests/Broadside.TestSupport/Corpus.cs`, in the same position as the README row.
6. Refresh the Verify snapshot. `CorpusSmokeTests.Corpus_file_names_match_snapshot` fails once, writes a `.received.txt`, and you accept it:

   ```sh
   dotnet test tests/Broadside.Tests
   mv tests/Broadside.Tests/Snapshots/CorpusSmokeTests.Corpus_file_names_match_snapshot.received.txt \
      tests/Broadside.Tests/Snapshots/CorpusSmokeTests.Corpus_file_names_match_snapshot.verified.txt
   dotnet test tests/Broadside.Tests          # green: File_lists_match_the_corpus_directory and the snapshot agree
   ```

7. Commit the PDF, `generate.py`, the README, `Corpus.cs` and the snapshot together, and update the conformance rows for the clauses the file exercises.

## Worked example: `text-type1-embedded.pdf`

Goal: one line of text in an embedded Type 1 font, the `/FontFile` counterpart of `text-truetype-embedded.pdf` (§9.6.2, §9.8.2, §9.9 Table 125).

1. Add `minimal_type1()` next to `minimal_truetype()`: a cleartext portion (`%!PS-AdobeFont-1.0`, `/FontName /BroadsideMinimal`, `/Encoding` mapping codes 72 and 73 to `/H` and `/I`, `/FontMatrix [0.001 0 0 0.001 0 0]`), an eexec-encrypted private portion with Type 1 charstrings for `.notdef`, `H` and `I` drawn as rectangles with `hsbw`, `rlineto`, `closepath`, `endchar`, and the 512 zeros trailer. The eexec and charstring ciphers (r = 55665 and 4330) come from the Type 1 Font Format specification, not ISO 32000-2; implement them in the script with a known-answer check in `self_test()`. Return the three section lengths too; `/Length1 /Length2 /Length3` need them.
2. `gen_text_type1_embedded()` mirrors `gen_text_truetype_embedded()`: objects 1–4 as there; object 5 is `/Subtype /Type1 /BaseFont /BroadsideMinimal /FirstChar 72 /LastChar 73 /Widths [...] /FontDescriptor 6 0 R`; object 6 the descriptor with `/FontFile 7 0 R`; object 7 `stream(b"/Length1 %d /Length2 %d /Length3 %d" % lengths, font)`. Use `binary=True` because the eexec portion is binary.
3. Register `"text-type1-embedded.pdf": gen_text_type1_embedded` after the TrueType entry.
4. Expected verification: `qpdf --check` clean; `pdftotext` prints `HI`; `pdffonts` shows `Type 1  WinAnsi  emb=yes`; `pdftoppm` renders two block glyphs. Record those in the README row with clauses `9.6.2, 9.8.2, 9.9 Table 125`.
5. Insert the name after `text-truetype-embedded.pdf` in `Corpus.cs`, refresh the snapshot, and update the §9.9 conformance row if the PR also implements the reader.

## Checklist

- [ ] `gen_<name>()` registered in `FILES`; docstring cites the clauses
- [ ] No randomness: file identifiers and keys come from `file_id` / `fixed_bytes`
- [ ] Scratch regeneration diffs only in the new file; then regenerated in place
- [ ] `qpdf --check`, `pdfinfo`, `pdftotext` (and `pdffonts`, `pdftoppm` where relevant) run and recorded
- [ ] Row added to `tests/Corpus/README.md`
- [ ] Name added to `Corpus.cs` (`WellFormed` or `Malformed`)
- [ ] Verify snapshot refreshed; `dotnet test tests/Broadside.Tests` green
- [ ] Conformance rows updated for the clauses the file exercises
