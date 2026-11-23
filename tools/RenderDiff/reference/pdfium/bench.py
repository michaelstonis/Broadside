"""PDFium timings for the Phase 3 baseline (issue #140) and the v1 comparison (issue #275), in the venv
tools/RenderDiff/acquire-references.sh creates.

Usage: python -I bench.py <pages-file> <corpus-dir> <dpis> <iterations> [<document-dpi>]
       python -I bench.py --memory [<file> [<dpi>]]

<pages-file> is tests/Broadside.Rendering.Tests/CorpusGate/pinned-pages.txt (path<TAB>zero-based pages<TAB>reason); <dpis> is a
comma-separated list. Prints one JSON object per line: file, page, op, dpi, median_ms, min_ms, runs (or error). Each case warms up
untimed for 0.5 s or 20 runs, whichever comes first, then runs <iterations> timed runs (at least 3, fewer once a case has used 10 s),
the protocol of tools/PerfCompare. The operations mirror bench/Broadside.Benchmarks and tools/PerfCompare:

  open           FPDF_LoadMemDocument + FPDF_LoadPage + the page size, then closed      (RealWorldPageBenchmarks.Open)
  text           on an open document: load the page, its text page and all its text      (RealWorldPageBenchmarks.ExtractText)
  render         on an open document: load the page and render it, annotations and forms (RealWorldRenderBenchmarks.Render)
  open-render    open the document from memory, load the page and render it              (RealWorldRenderBenchmarks.OpenAndRender)
  document       with <document-dpi>: open the document and render every page

--memory opens the file, renders every page at <dpi> (150) and extracts its text, then prints {"peak_rss_bytes": n}; without a file,
only the module is loaded (the floor the report subtracts).
"""

import json
import os
import resource
import statistics
import sys
import time

import pypdfium2 as pdfium

BUDGET_SECONDS = 10.0
WARMUP_SECONDS = 0.5
WARMUP_RUNS = 20


def measure(action, iterations):
    started = time.perf_counter()
    warmups = 0
    while True:
        action()
        warmups += 1
        if warmups >= WARMUP_RUNS or time.perf_counter() - started >= WARMUP_SECONDS:
            break
    samples = []
    spent = 0.0
    while len(samples) < iterations and (len(samples) < 3 or spent < BUDGET_SECONDS):
        start = time.perf_counter()
        action()
        elapsed = time.perf_counter() - start
        spent += elapsed
        samples.append(elapsed * 1000)
    return {"median_ms": statistics.median(samples), "min_ms": min(samples), "runs": len(samples)}


def open_document(data):
    document = pdfium.PdfDocument(data)
    try:
        document.init_forms()
    except Exception:  # noqa: BLE001 - a file whose form cannot be set up still renders
        pass
    return document


def render(document, index, scale):
    page = document[index]
    bitmap = page.render(scale=scale, draw_annots=True, may_draw_forms=True, rev_byteorder=True)
    width = bitmap.width
    bitmap.close()
    page.close()
    return width


def run_open(data, index):
    document = pdfium.PdfDocument(data)
    page = document[index]
    page.get_size()
    page.close()
    document.close()


def run_text(document, index):
    page = document[index]
    text_page = page.get_textpage()
    length = len(text_page.get_text_range())
    text_page.close()
    page.close()
    return length


def run_open_render(data, index, scale):
    document = open_document(data)
    render(document, index, scale)
    document.close()


def run_document(data, scale):
    document = open_document(data)
    for index in range(len(document)):
        render(document, index, scale)
    document.close()


def peak_rss_bytes():
    peak = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss
    return peak if sys.platform == "darwin" else peak * 1024


def memory(argv):
    if len(argv) > 2:
        dpi = int(argv[3]) if len(argv) > 3 else 150
        with open(argv[2], "rb") as stream:
            document = open_document(stream.read())
        for index in range(len(document)):
            render(document, index, dpi / 72)
            page = document[index]
            text_page = page.get_textpage()
            text_page.get_text_range()
            text_page.close()
            page.close()
        document.close()
    print(json.dumps({"peak_rss_bytes": peak_rss_bytes()}))
    return 0


def cases(pages_file):
    with open(pages_file, encoding="utf-8") as stream:
        for line in stream:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            path, pages = line.split("\t")[:2]
            for page in pages.split(","):
                yield path, int(page)


def main(argv):
    if len(argv) > 1 and argv[1] == "--memory":
        return memory(argv)
    if len(argv) not in (5, 6):
        print(__doc__, file=sys.stderr)
        return 2
    pages_file, corpus, dpis, iterations = argv[1], argv[2], [int(d) for d in argv[3].split(",")], int(argv[4])
    document_dpi = int(argv[5]) if len(argv) > 5 else None
    documented = set()
    for path, index in cases(pages_file):
        full = os.path.join(corpus, path)
        if not os.path.exists(full):
            continue
        with open(full, "rb") as stream:
            data = stream.read()
        record = {"file": path, "page": index}

        def emit(op, dpi, action):
            try:
                result = measure(action, iterations)
            except Exception as error:  # noqa: BLE001 - a failing case is reported, not raised
                result = {"error": str(error)}
            print(json.dumps({**record, "op": op, "dpi": dpi, **result}), flush=True)

        emit("open", None, lambda: run_open(data, index))
        document = open_document(data)
        emit("text", None, lambda: run_text(document, index))
        for dpi in dpis:
            emit("render", dpi, lambda dpi=dpi: render(document, index, dpi / 72))
            emit("open-render", dpi, lambda dpi=dpi: run_open_render(data, index, dpi / 72))
        if document_dpi is not None and path not in documented:
            documented.add(path)
            emit("document", document_dpi, lambda: run_document(data, document_dpi / 72))
        document.close()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
