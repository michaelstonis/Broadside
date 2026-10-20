// Broadside Blazor viewer: the minimal browser half of PdfViewer (issue #221). It blits tiles the .NET side rasterized into a
// canvas, forwards pointer, wheel, keyboard, focus and clipboard events to the viewer core, and positions the accessible text layer.
// Everything else (layout, scheduling, hit-testing, selection, accessibility tree) runs in .NET. No dependencies.

const viewers = new Map();
let interop = null;

const STYLE = `
.bs-viewer{position:relative;overflow:hidden;touch-action:none;outline:none;background:#e6e6e6;user-select:none;-webkit-user-select:none}
.bs-viewer:focus-visible{box-shadow:inset 0 0 0 2px #1a73e8}
.bs-viewer>canvas{position:absolute;left:0;top:0;width:100%;height:100%;display:block}
.bs-viewer>.bs-status{position:absolute;left:12px;top:12px;font:14px system-ui,sans-serif;color:#333}
.bs-a11y{position:absolute;left:0;top:0;width:0;height:0;pointer-events:none;color:transparent}
.bs-a11y>section{position:absolute;left:0;top:0;width:0;height:0;transform-origin:0 0}
.bs-a11y [data-bs-node]{white-space:pre;line-height:1;font-size:8px;margin:0;padding:0}
.bs-a11y [data-bs-box]{position:absolute}
.bs-a11y :focus{outline:none}
.bs-a11y :focus-visible{outline:2px solid #1a73e8;outline-offset:0}`;

async function exportsOf() {
  if (interop === null) {
    const runtime = await globalThis.getDotnetRuntime(0);
    const exports = await runtime.getAssemblyExports("Broadside.Viewer.Blazor.dll");
    interop = exports.Broadside.Viewer.Blazor.ViewerInterop;
  }
  return interop;
}

function modifiers(e) {
  return (e.shiftKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.altKey ? 4 : 0) | (e.metaKey ? 8 : 0);
}

function pointerKind(type) {
  return type === "touch" ? 1 : type === "pen" ? 2 : 0;
}

const CURSORS = ["default", "pointer", "text", "grabbing"];

export async function attach(id, elementId) {
  const x = await exportsOf();
  if (!document.getElementById("bs-viewer-style")) {
    const style = document.createElement("style");
    style.id = "bs-viewer-style";
    style.textContent = STYLE;
    document.head.appendChild(style);
  }

  const host = document.getElementById(elementId);
  const canvas = host.querySelector("canvas");
  const context = canvas.getContext("2d", { alpha: false });
  const abort = new AbortController();
  const signal = abort.signal;
  const v = { host, canvas, context, abort, tiles: new Map(), sections: new Map(), cursor: 0, frames: 0, sum: 0, max: 0, long: 0, frameStart: 0 };
  viewers.set(id, v);

  const point = (e) => {
    const r = host.getBoundingClientRect();
    return [e.clientX - r.left, e.clientY - r.top];
  };
  const resize = () => {
    const r = host.getBoundingClientRect();
    const ratio = window.devicePixelRatio || 1;
    const w = Math.max(1, Math.round(r.width * ratio));
    const h = Math.max(1, Math.round(r.height * ratio));
    if (canvas.width !== w || canvas.height !== h) {
      // Resizing clears the canvas (to black: it has no alpha). Repaint the last frame at once, before the browser shows the cleared
      // canvas; the next frame lays it out for the new size (#346).
      canvas.width = w;
      canvas.height = h;
      if (v.last) paint(v, v.last);
    }
    x.OnResize(id, Math.max(1, r.width), Math.max(1, r.height), ratio);
  };

  host.addEventListener("pointerdown", (e) => {
    if (e.pointerType === "mouse" && e.button !== 0) return;
    host.setPointerCapture(e.pointerId);
    host.focus({ preventScroll: true });
    const [px, py] = point(e);
    x.OnPointerDown(id, e.pointerId, pointerKind(e.pointerType), px, py, e.detail || 1, modifiers(e));
    e.preventDefault();
  }, { signal });
  host.addEventListener("pointermove", (e) => {
    const [px, py] = point(e);
    const cursor = x.OnPointerMove(id, e.pointerId, px, py);
    if (cursor !== v.cursor) {
      v.cursor = cursor;
      host.style.cursor = CURSORS[cursor] || "default";
    }
  }, { signal });
  host.addEventListener("pointerup", (e) => {
    const [px, py] = point(e);
    x.OnPointerUp(id, e.pointerId, px, py);
  }, { signal });
  host.addEventListener("pointercancel", (e) => x.OnPointerCancel(id, e.pointerId), { signal });
  host.addEventListener("wheel", (e) => {
    const [px, py] = point(e);
    x.OnWheel(id, e.deltaX, e.deltaY, e.deltaMode, modifiers(e), px, py);
    e.preventDefault();
  }, { signal, passive: false });
  host.addEventListener("keydown", (e) => {
    if (x.OnKeyDown(id, e.key, modifiers(e))) e.preventDefault();
  }, { signal });
  host.addEventListener("copy", (e) => {
    const text = x.OnCopy(id);
    if (text !== null && text !== undefined) {
      e.clipboardData.setData("text/plain", text);
      e.preventDefault();
    }
  }, { signal });
  host.addEventListener("focusin", (e) => {
    if (e.target !== host && e.target.id) x.OnNodeFocused(id, e.target.id);
  }, { signal });

  const observer = new ResizeObserver(resize);
  observer.observe(host);
  v.observer = observer;
  resize();
}

export function detach(id) {
  const v = viewers.get(id);
  if (!v) return;
  v.abort.abort();
  v.observer.disconnect();
  for (const tile of v.tiles.values()) closeTile(tile);
  v.tiles.clear();
  viewers.delete(id);
}

export function nextFrame(id) {
  return new Promise((resolve) => requestAnimationFrame((time) => {
    const v = viewers.get(id);
    if (v) v.frameStart = performance.now();
    resolve();
  }));
}

function closeTile(tile) {
  if (tile && typeof tile.close === "function") tile.close(); // an ImageBitmap from the render worker
}

export function putTile(id, key, width, height, pixels) {
  const v = viewers.get(id);
  if (!v) return;
  let tile = v.tiles.get(key);
  if (!(tile instanceof HTMLCanvasElement) || tile.width !== width || tile.height !== height) {
    closeTile(tile);
    tile = document.createElement("canvas");
    tile.width = width;
    tile.height = height;
    v.tiles.set(key, tile);
  }
  const image = new ImageData(width, height);
  pixels.copyTo(new Uint8Array(image.data.buffer));
  tile.getContext("2d").putImageData(image, 0, 0);
}

// A tile the WebGL backend (#353) left in its canvas: copied GPU to GPU, no read-back.
export function putTileFromCanvas(id, key, width, height, source) {
  const v = viewers.get(id);
  if (!v) return;
  let tile = v.tiles.get(key);
  if (!(tile instanceof HTMLCanvasElement) || tile.width !== width || tile.height !== height) {
    closeTile(tile);
    tile = document.createElement("canvas");
    tile.width = width;
    tile.height = height;
    v.tiles.set(key, tile);
  }
  const context = tile.getContext("2d");
  context.clearRect(0, 0, width, height);
  context.drawImage(source, 0, 0, width, height, 0, 0, width, height);
}

// A tile the render worker drew (#357): its ImageBitmap is drawn as it is, nothing is copied on this thread.
export function putTileFromImage(id, key, image) {
  const v = viewers.get(id);
  if (!v) {
    closeTile(image);
    return;
  }
  const old = v.tiles.get(key);
  if (old !== image) closeTile(old);
  v.tiles.set(key, image);
}

export function dropTile(id, key) {
  const v = viewers.get(id);
  if (!v) return;
  closeTile(v.tiles.get(key));
  v.tiles.delete(key);
}

export function markWorker(id, on) {
  const v = viewers.get(id);
  if (v) v.host.dataset.bsWorker = on ? "yes" : "no";
}

// ---- The render worker (#357) ----

let worker = null;

async function workerExports() {
  const runtime = await globalThis.getDotnetRuntime(0);
  const exports = await runtime.getAssemblyExports("Broadside.Viewer.Blazor.dll");
  return exports.Broadside.Viewer.Blazor.RenderWorker;
}

// Where the page's own runtime came from, so the worker boots the same files (any runtime folder).
function dotnetUrl() {
  const entry = performance.getEntriesByType("resource").find((e) => /\/dotnet\.js(\?|$)/.test(e.name));
  return entry ? entry.name : new URL("_framework/dotnet.js", document.baseURI).href;
}

// Starts the worker; resolves "ready", "notServed" (the app's Main does not call RenderWorker.Serve) or why it failed.
export function startWorker(timeoutMs) {
  if (typeof Worker === "undefined" || typeof createImageBitmap === "undefined") return Promise.resolve("unsupported");
  return new Promise((resolve) => {
    let settled = false;
    const started = performance.now();
    const done = (status) => {
      if (settled) return;
      settled = true;
      // Diagnostics for the app and the benchmarks: how the worker started and how long its runtime took to boot.
      globalThis.broadsideRenderWorker = { status, bootMs: Math.round(performance.now() - started) };
      if (status !== "ready" && worker) {
        worker.terminate();
        worker = null;
      }
      resolve(status);
    };
    let w;
    try {
      const script = new URL("./broadside-render-worker.js", import.meta.url);
      script.searchParams.set("dotnet", dotnetUrl());
      w = new Worker(script, { type: "module" });
    } catch (error) {
      resolve("failed: " + error);
      return;
    }
    worker = w;
    const timer = setTimeout(() => done("timeout"), timeoutMs);
    // A failure after startup (#357): the page's thread takes every tile over, including those the worker still had.
    const failed = async (message) => {
      if (!settled) {
        clearTimeout(timer);
        done("failed: " + message);
        return;
      }
      if (worker === w) {
        worker = null;
        w.terminate();
        globalThis.broadsideRenderWorker = { ...(globalThis.broadsideRenderWorker || {}), status: "failed: " + message };
        (await workerExports()).OnWorkerFailed(message);
      }
    };
    w.onerror = (e) => { e.preventDefault?.(); failed(e.message || "error"); };
    w.onmessageerror = () => failed("a message could not be read");
    w.onmessage = async (e) => {
      const m = e.data;
      if (m.type === "ready") {
        clearTimeout(timer);
        done(m.served ? "ready" : "notServed");
        return;
      }
      if (m.type === "bootFailed") {
        clearTimeout(timer);
        done("failed: " + m.message);
        return;
      }
      const x = await workerExports();
      switch (m.type) {
        case "opened": x.OnOpened(m.doc, true, null); break;
        case "openFailed": x.OnOpened(m.doc, false, m.message); break;
        case "rendered": x.OnRendered(m.job, m.image); break;
        case "failed": x.OnFailed(m.job, m.message); break;
        case "cancelled": x.OnCancelled(m.job); break;
      }
    };
  });
}

export function workerOpen(doc, intent, bytes) {
  const copy = bytes.slice(); // a MemoryView: copy it out of the runtime's heap, then hand the copy over
  worker?.postMessage({ type: "open", doc, intent, bytes: copy.buffer }, [copy.buffer]);
}

export function workerOpenAddress(doc, intent, address) {
  worker?.postMessage({ type: "openAddress", doc, intent, address });
}

export function workerClose(doc) {
  worker?.postMessage({ type: "close", doc });
}

export function workerRender(job, doc, page, scale, rotation, x, y, width, height, layers) {
  worker?.postMessage({ type: "render", job, doc, page, scale, rotation, x, y, width, height, layers });
}

export function workerCancel(job) {
  worker?.postMessage({ type: "cancel", job });
}

// Header: [records, pageCount, currentPage, zoom, tilesPresented, 0, 0, 0, 0]; then records of 9 numbers:
// 1 paper (x, y, w, h) | 2 tile (key, x, y, w, h) | 3 selection quad (x1, y1 ... x4, y4) | 4 text layer (page, a, b, c, d, e, f) |
// 5 live link or field (x, y, w, h, 1 hover | 2 pressed).
export function draw(id, commands) {
  const v = viewers.get(id);
  if (!v) return;
  const c = commands.slice();
  v.last = c;
  const link = paint(v, c);

  const ms = performance.now() - v.frameStart;
  v.frames++;
  v.sum += ms;
  if (ms > v.max) v.max = ms;
  if (ms > 50) v.long++;
  const d = v.host.dataset;
  d.pageCount = c[1];
  d.currentPage = c[2] + 1;
  d.zoom = c[3];
  d.tilesPresented = c[4];
  d.frames = v.frames;
  d.maxFrameMs = v.max.toFixed(1);
  d.meanFrameMs = (v.sum / v.frames).toFixed(2);
  d.longFrames = v.long;
  d.linkState = link;
}

// Returns the state of the link or field drawn live: "pressed", "hover" or "".
function paint(v, c) {
  let link = "";
  const g = v.context;
  g.fillStyle = "#e6e6e6";
  g.fillRect(0, 0, v.canvas.width, v.canvas.height);
  const records = c[0];
  for (let i = 0, o = 9; i < records; i++, o += 9) {
    switch (c[o]) {
      case 1:
        g.fillStyle = "#ffffff";
        g.shadowColor = "rgba(0,0,0,0.25)";
        g.shadowBlur = 4;
        g.fillRect(c[o + 1], c[o + 2], c[o + 3], c[o + 4]);
        g.shadowBlur = 0;
        g.shadowColor = "transparent";
        break;
      case 2: {
        const tile = v.tiles.get(c[o + 1]);
        if (tile) g.drawImage(tile, c[o + 2], c[o + 3], c[o + 4], c[o + 5]);
        break;
      }
      case 3:
        g.fillStyle = "rgba(26,115,232,0.3)";
        g.beginPath();
        g.moveTo(c[o + 1], c[o + 2]);
        g.lineTo(c[o + 3], c[o + 4]);
        g.lineTo(c[o + 5], c[o + 6]);
        g.lineTo(c[o + 7], c[o + 8]);
        g.closePath();
        g.fill();
        break;
      case 4: {
        let section = v.sections.get(c[o + 1]);
        if (!section || !section.isConnected) {
          section = v.host.querySelector(`section[data-bs-page="${c[o + 1]}"]`);
          if (!section) break;
          v.sections.set(c[o + 1], section);
        }
        const transform = `matrix(${c[o + 2]},${c[o + 3]},${c[o + 4]},${c[o + 5]},${c[o + 6]},${c[o + 7]})`;
        if (section.style.transform !== transform) section.style.transform = transform;
        break;
      }
      case 5: {
        const pressed = c[o + 5] === 2;
        g.fillStyle = pressed ? "rgba(26,115,232,0.28)" : "rgba(26,115,232,0.12)";
        g.fillRect(c[o + 1], c[o + 2], c[o + 3], c[o + 4]);
        g.strokeStyle = "rgba(26,115,232,0.8)";
        g.lineWidth = Math.max(1, Math.round(window.devicePixelRatio || 1));
        g.strokeRect(c[o + 1], c[o + 2], c[o + 3], c[o + 4]);
        link = pressed ? "pressed" : "hover";
        break;
      }
    }
  }

  return link;
}

export function focusNode(id, elementId, follow) {
  const element = document.getElementById(elementId);
  if (!element) return false;
  // Following the viewer to a new page (scrolling) moves the keyboard only while it is already on the text layer: a pointer user's
  // focus stays on the viewer, so no focus ring appears over content scrolling into view (#349).
  if (follow && !(document.activeElement && document.activeElement.hasAttribute("data-bs-node"))) return true;
  // The keyboard follows the viewer's reading focus only while the viewer (or nothing) has it: a focus change the host page caused,
  // e.g. through AnnotationLayer.Focus while the user types in the host's own form, must not take the keyboard from that element.
  const active = document.activeElement;
  const v = viewers.get(id);
  if (active && active !== document.body && v && !v.host.contains(active)) return true;
  if (active !== element) element.focus({ preventScroll: true });
  return true;
}

export function writeClipboard(text) {
  if (navigator.clipboard) navigator.clipboard.writeText(text).catch(() => {});
}
