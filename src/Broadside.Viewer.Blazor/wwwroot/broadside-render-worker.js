// Broadside render worker (#357): a module Web Worker that boots a second .NET runtime from the app's own framework files and renders
// content tiles of documents the page sends it, so the page's thread never builds a display list or rasterizes. Tiles go back as
// transferable ImageBitmaps. Requests are rendered one at a time, newest-wanted first; a cancelled request that has not started is
// dropped. The app's Main runs here with globalThis.__broadsideRenderWorker set, and calls RenderWorker.Serve (see RenderWorker.cs).

globalThis.__broadsideRenderWorker = true;

let exports = null;
const queue = [];
let busy = false;

function reply(message, transfer) {
  self.postMessage(message, transfer || []);
}

async function boot(dotnetUrl) {
  const { dotnet } = await import(dotnetUrl);
  const runtime = await dotnet.create();
  runtime.setModuleImports("broadside-render-worker", {
    deliver: (job, width, height, pixels) => {
      const image = new ImageData(width, height);
      pixels.copyTo(new Uint8Array(image.data.buffer));
      pending.set(job, createImageBitmap(image));
    },
  });
  await runtime.runMain();
  const all = await runtime.getAssemblyExports("Broadside.Viewer.Blazor.dll");
  exports = all.Broadside.Viewer.Blazor.RenderWorker;
}

// The bitmap a render produced, keyed by job (createImageBitmap is asynchronous).
const pending = new Map();
// Documents still opening (range requests), keyed by document.
const opening = new Map();

async function pump() {
  if (busy || exports === null) return;
  busy = true;
  try {
    while (queue.length > 0) {
      const r = queue.shift();
      if (opening.has(r.doc)) {
        try { await opening.get(r.doc); } catch { /* reported as openFailed; the render fails below */ }
      }
      let error = null;
      try {
        await exports.Render(r.job, r.doc, r.page, r.scale, r.rotation, r.x, r.y, r.width, r.height, r.layers);
      } catch (e) {
        error = String(e && e.message ? e.message : e);
      }
      const bitmap = pending.get(r.job);
      pending.delete(r.job);
      if (error !== null || !bitmap) {
        reply({ type: "failed", job: r.job, message: error ?? "no pixels" });
        continue;
      }
      const image = await bitmap;
      reply({ type: "rendered", job: r.job, image }, [image]);
      // Let cancellations that arrived during the render reach the queue before the next one starts.
      await new Promise((resolve) => setTimeout(resolve, 0));
    }
  } finally {
    busy = false;
  }
}

// The runtime boots as soon as the module loads (it must start before this worker handles any message): the page passes the
// address of its dotnet.js in the worker's own query string.
const booted = (async () => {
  try {
    await boot(new URL(self.location.href).searchParams.get("dotnet"));
    reply({ type: "ready", served: exports.IsServing() });
  } catch (error) {
    reply({ type: "bootFailed", message: String(error && error.message ? error.message : error) });
  }
})();

self.addEventListener("message", async (e) => {
  await booted;
  const m = e.data;
  switch (m.type) {
    case "open":
      try {
        exports.OpenDocument(m.doc, m.intent, new Uint8Array(m.bytes));
        reply({ type: "opened", doc: m.doc });
      } catch (error) {
        reply({ type: "openFailed", doc: m.doc, message: String(error && error.message ? error.message : error) });
      }
      break;
    case "openAddress":
      try {
        // Renders wait for the open: the queue starts once the document is there.
        opening.set(m.doc, exports.OpenAddress(m.doc, m.intent, m.address));
        await opening.get(m.doc);
        reply({ type: "opened", doc: m.doc });
      } catch (error) {
        reply({ type: "openFailed", doc: m.doc, message: String(error && error.message ? error.message : error) });
      } finally {
        opening.delete(m.doc);
        pump();
      }
      break;
    case "close":
      exports?.CloseDocument(m.doc);
      for (let i = queue.length - 1; i >= 0; i--) {
        if (queue[i].doc === m.doc) {
          reply({ type: "cancelled", job: queue[i].job });
          queue.splice(i, 1);
        }
      }
      break;
    case "render":
      queue.push(m);
      pump();
      break;
    case "cancel": {
      const i = queue.findIndex((r) => r.job === m.job);
      if (i >= 0) {
        queue.splice(i, 1);
        reply({ type: "cancelled", job: m.job });
      }
      break;
    }
  }
});
