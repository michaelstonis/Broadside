// pdf.js timings for the Phase 3 baseline (issue #140) and the v1 comparison (issue #275), with the install
// tools/RenderDiff/acquire-references.sh creates.
//
// Usage: RENDERDIFF_PDFJS_DIR=<install-dir> node bench.mjs <pages-file> <corpus-dir> <dpis> <iterations> [<document-dpi>]
//        RENDERDIFF_PDFJS_DIR=<install-dir> node bench.mjs --memory [<file> [<dpi>]]
//
// Same protocol and operations as ../pdfium/bench.py: one JSON object per line (file, page, op, dpi, median_ms, min_ms, runs or
// error); each case warms up untimed for 0.5 s or 20 runs, whichever comes first, then runs <iterations> timed runs (at least 3,
// fewer once a case has used 10 s). --memory prints {"peak_rss_bytes": n} after rendering every page of the file at <dpi> (150) and
// extracting its text, or with no file after loading pdf.js only. pdf.js runs in Node with its in-thread
// ("fake") worker and @napi-rs/canvas; each document gets a fresh copy of the bytes, made before the clock starts, because
// getDocument takes ownership of its buffer.
//   open         getDocument + getPage + the page's view, then destroyed
//   text         on an open document: getPage + getTextContent, then page.cleanup()
//   render       on an open document: getPage + render (annotations enabled), then page.cleanup() so the next run parses again
//   open-render  getDocument + getPage + render, then destroyed
//   document     with <document-dpi>: getDocument + render every page, then destroyed (once per file)
import { readFile } from "node:fs/promises";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const [pagesFile, corpus, dpisText, iterationsText, documentDpiText] = process.argv.slice(2);
const installDir = process.env.RENDERDIFF_PDFJS_DIR;
const memoryMode = pagesFile === "--memory";
if (!installDir || (!memoryMode && (!pagesFile || !corpus || !dpisText || !iterationsText))) {
  console.error("usage: RENDERDIFF_PDFJS_DIR=<install-dir> node bench.mjs <pages-file> <corpus-dir> <dpis> <iterations>");
  process.exit(2);
}

const distDir = path.join(installDir, "node_modules", "pdfjs-dist");
const pdfjs = await import(pathToFileURL(path.join(distDir, "legacy", "build", "pdf.mjs")).href);
const resource = (name) => path.join(distDir, name) + path.sep;
const budgetMs = 10_000;
const warmupMs = 500;
const warmupRuns = 20;

const load = (bytes) =>
  pdfjs.getDocument({
    data: bytes,
    cMapUrl: resource("cmaps"),
    cMapPacked: true,
    standardFontDataUrl: resource("standard_fonts"),
    wasmUrl: resource("wasm"),
    iccUrl: resource("iccs"),
    verbosity: 0,
  });

async function measure(prepare, action) {
  const warmupStart = process.hrtime.bigint();
  for (let runs = 0; ; ) {
    await action(prepare());
    runs++;
    if (runs >= warmupRuns || Number(process.hrtime.bigint() - warmupStart) / 1e6 >= warmupMs) break;
  }
  const samples = [];
  let spent = 0;
  while (samples.length < iterations && (samples.length < 3 || spent < budgetMs)) {
    const input = prepare();
    const start = process.hrtime.bigint();
    await action(input);
    const elapsed = Number(process.hrtime.bigint() - start) / 1e6;
    spent += elapsed;
    samples.push(elapsed);
  }
  samples.sort((a, b) => a - b);
  const middle = samples.length >> 1;
  const median = samples.length % 2 ? samples[middle] : (samples[middle - 1] + samples[middle]) / 2;
  return { median_ms: median, min_ms: samples[0], runs: samples.length };
}

async function render(document, index, dpi) {
  const page = await document.getPage(index + 1);
  const viewport = page.getViewport({ scale: dpi / 72 });
  const width = Math.max(1, Math.ceil(viewport.width - 1e-6));
  const height = Math.max(1, Math.ceil(viewport.height - 1e-6));
  const { canvas, context } = document.canvasFactory.create(width, height);
  await page.render({ canvas, canvasContext: context, viewport, background: "white", annotationMode: pdfjs.AnnotationMode.ENABLE }).promise;
  page.cleanup();
}

if (memoryMode) {
  // corpus is the file and dpisText the resolution here.
  if (corpus) {
    const dpi = Number(dpisText ?? 150);
    const task = load(new Uint8Array(await readFile(corpus)));
    const document = await task.promise;
    for (let index = 0; index < document.numPages; index++) {
      await render(document, index, dpi);
      const page = await document.getPage(index + 1);
      await page.getTextContent();
      page.cleanup();
    }
    await task.destroy();
  }
  console.log(JSON.stringify({ peak_rss_bytes: process.resourceUsage().maxRSS * 1024 }));
  process.exit(0);
}

const dpis = dpisText.split(",").map(Number);
const iterations = Number(iterationsText);
const documentDpi = documentDpiText ? Number(documentDpiText) : null;
const documented = new Set();
const cases = [];
for (const line of readFileSync(pagesFile, "utf8").split("\n")) {
  if (!line || line.startsWith("#")) continue;
  const [file, pages] = line.split("\t");
  for (const page of pages.split(",")) cases.push([file, Number(page)]);
}

for (const [file, index] of cases) {
  const full = path.join(corpus, file);
  if (!existsSync(full)) continue;
  const bytes = new Uint8Array(await readFile(full));
  const copy = () => bytes.slice();
  const emit = async (op, dpi, prepare, action) => {
    let result;
    try {
      result = await measure(prepare, action);
    } catch (error) {
      result = { error: String(error?.message ?? error) };
    }
    console.log(JSON.stringify({ file, page: index, op, dpi, ...result }));
  };

  await emit("open", null, copy, async (data) => {
    const task = load(data);
    const document = await task.promise;
    const page = await document.getPage(index + 1);
    void page.view;
    await task.destroy();
  });

  let task;
  try {
    task = load(copy());
    const document = await task.promise;
    await emit("text", null, () => null, async () => {
      const page = await document.getPage(index + 1);
      const content = await page.getTextContent();
      void content.items.length;
      page.cleanup();
    });
    for (const dpi of dpis) {
      await emit("render", dpi, () => null, () => render(document, index, dpi));
      await emit("open-render", dpi, copy, async (data) => {
        const own = load(data);
        await render(await own.promise, index, dpi);
        await own.destroy();
      });
    }
    if (documentDpi !== null && !documented.has(file)) {
      documented.add(file);
      await emit("document", documentDpi, copy, async (data) => {
        const own = load(data);
        const whole = await own.promise;
        for (let page = 0; page < whole.numPages; page++) await render(whole, page, documentDpi);
        await own.destroy();
      });
    }
  } catch (error) {
    console.log(JSON.stringify({ file, page: index, op: "open", dpi: null, error: String(error?.message ?? error) }));
  } finally {
    await task?.destroy();
  }
}
