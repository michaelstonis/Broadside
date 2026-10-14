// pdf.js reference renderer for tools/RenderDiff (issue #137).
//
// Usage: node render.mjs <pdf> <out-dir> <dpi> <pages>
//
// <pages> is a comma-separated list of zero-based page indexes. Writes <out-dir>/page<N>.png (the page's crop box with its /Rotate,
// white background, no annotations or form fields) for every page it can render and reports the others on stderr. Exits 0 when the
// document opened, 1 when it did not, 2 on bad arguments. The modules come from the directory acquire-references.sh installed, named
// by RENDERDIFF_PDFJS_DIR, so this script can stay in the repository.
import { mkdir, readFile, rename, writeFile } from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";

const [pdfPath, outDir, dpiText, pagesText] = process.argv.slice(2);
const installDir = process.env.RENDERDIFF_PDFJS_DIR;
if (!pdfPath || !outDir || !dpiText || pagesText === undefined || !installDir) {
  console.error("usage: RENDERDIFF_PDFJS_DIR=<install-dir> node render.mjs <pdf> <out-dir> <dpi> <pages>");
  process.exit(2);
}

const distDir = path.join(installDir, "node_modules", "pdfjs-dist");
const pdfjs = await import(pathToFileURL(path.join(distDir, "legacy", "build", "pdf.mjs")).href);
const resource = (name) => path.join(distDir, name) + path.sep;

const scale = Number(dpiText) / 72;
const pages = pagesText.split(",").filter((p) => p !== "").map(Number);

const loadingTask = pdfjs.getDocument({
  data: new Uint8Array(await readFile(pdfPath)),
  cMapUrl: resource("cmaps"),
  cMapPacked: true,
  standardFontDataUrl: resource("standard_fonts"),
  wasmUrl: resource("wasm"),
  iccUrl: resource("iccs"),
  verbosity: 0,
});
let document;
try {
  document = await loadingTask.promise;
} catch (error) {
  console.error(`cannot open: ${error?.message ?? error}`);
  process.exit(1);
}

await mkdir(outDir, { recursive: true });
for (const index of pages) {
  try {
    const page = await document.getPage(index + 1);
    const viewport = page.getViewport({ scale });
    // The same pixel size rule as Broadside's RenderOptions.GetPixelSize: the extent rounded up, at least one pixel.
    const width = Math.max(1, Math.ceil(viewport.width - 1e-6));
    const height = Math.max(1, Math.ceil(viewport.height - 1e-6));
    const { canvas, context } = document.canvasFactory.create(width, height);
    await page.render({
      canvas,
      canvasContext: context,
      viewport,
      background: "white",
      annotationMode: pdfjs.AnnotationMode.DISABLE,
    }).promise;
    const target = path.join(outDir, `page${index}.png`);
    await writeFile(target + ".partial", await canvas.encode("png"));
    await rename(target + ".partial", target);
    page.cleanup();
  } catch (error) {
    console.error(`page ${index}: ${error?.message ?? error}`);
  }
}

await loadingTask.destroy();
