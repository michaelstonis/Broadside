// Broadside browser image decoder (#373): decodes JPEG data with the browser's own decoder for BrowserImageDecoder, off the page's
// thread where the browser does that. Feature-detected at run time; the user agent is never read. No dependencies.
//
// Tier 1, WebCodecs ImageDecoder (Chrome and Edge 94+, Firefox 133+; page and dedicated worker):
//   full size: the decoder's YCbCr planes (I420, I422, I444), converted to RGB here with libjpeg's integer transform and chroma
//   replication, which is what the managed decoder does, so the samples are bit-identical to it;
//   reduced: desiredWidth/desiredHeight (libjpeg DCT scaling), copied out as RGB.
// Tier 2, createImageBitmap and an OffscreenCanvas read back (a worker without ImageDecoder, such as Safari's): full size, RGB. Never
// on the page's thread by default: WebKit's read back takes 60 to 141 ms on the calling thread.

const decodes = new Map();
let next = 0;

// libjpeg-turbo jdcolor.c build_ycc_rgb_table, 16-bit fixed point (the managed decoder's JpegColor).
const crToRed = new Int32Array(256);
const cbToBlue = new Int32Array(256);
const crToGreen = new Int32Array(256);
const cbToGreen = new Int32Array(256);
for (let i = 0; i < 256; i++) {
  const x = i - 128;
  crToRed[i] = (91881 * x + 32768) >> 16;
  cbToBlue[i] = (116130 * x + 32768) >> 16;
  crToGreen[i] = -46802 * x;
  cbToGreen[i] = -22554 * x + 32768;
}

const clamp = new Uint8Array(1024);
for (let i = 0; i < 1024; i++) clamp[i] = Math.max(0, Math.min(255, i - 384));

function isWorker() {
  return typeof WorkerGlobalScope !== "undefined" && self instanceof WorkerGlobalScope;
}

/** What this context can decode with: bit 1 ImageDecoder for JPEG, bit 2 createImageBitmap with an OffscreenCanvas, bit 4 a worker. */
export async function probe() {
  let tiers = 0;
  if (typeof ImageDecoder === "function") {
    try {
      if (await ImageDecoder.isTypeSupported("image/jpeg")) tiers |= 1;
    } catch {
      // not supported
    }
  }
  if (typeof createImageBitmap === "function" && typeof OffscreenCanvas === "function") tiers |= 2;
  if (isWorker()) tiers |= 4;
  return tiers;
}

/**
 * Starts decoding a copy of `data` (a MemoryView, read now) with `tier` (1 ImageDecoder, 2 createImageBitmap); returns the id to wait
 * for. `reduction` is the power of two wanted, `width` x `height` the image's own size, `components` 1 or 3.
 */
export function begin(data, tier, reduction, width, height, components) {
  const bytes = data.slice();
  const id = ++next;
  // Diagnostics for the browser tests and benchmarks: decodes started, by reduction.
  const counts = (globalThis.__broadsideImageDecodes ??= {});
  counts[reduction] = (counts[reduction] ?? 0) + 1;
  const entry = { result: null, error: null };
  entry.promise = (tier === 1 ? withImageDecoder(bytes, reduction, width, height, components) : withImageBitmap(bytes, components))
    .then(r => { entry.result = r; }, e => { entry.error = String(e && e.message ? e.message : e); });
  decodes.set(id, entry);
  return id;
}

/** Waits for a decode: "ok <width> <height> <exact 0|1> <format>" or "error <reason>". */
export async function wait(id) {
  const entry = decodes.get(id);
  if (!entry) return "error the decode was released";
  await entry.promise;
  if (entry.error !== null) {
    decodes.delete(id);
    return "error " + entry.error;
  }
  const r = entry.result;
  return `ok ${r.width} ${r.height} ${r.exact ? 1 : 0} ${r.format}`;
}

/** Copies a finished decode's samples into `destination` (a MemoryView of width x height x components bytes) and releases it. */
export function read(id, destination) {
  const entry = decodes.get(id);
  decodes.delete(id);
  if (!entry || !entry.result) return false;
  destination.set(entry.result.samples);
  return true;
}

/** Forgets a decode whose samples are not wanted (cancelled). */
export function release(id) {
  decodes.delete(id);
}

async function withImageDecoder(bytes, reduction, width, height, components) {
  const init = { data: bytes, type: "image/jpeg", colorSpaceConversion: "none", preferAnimation: false };
  if (reduction > 0) {
    init.desiredWidth = Math.ceil(width / (1 << reduction));
    init.desiredHeight = Math.ceil(height / (1 << reduction));
  }
  const decoder = new ImageDecoder(init);
  try {
    const { image } = await decoder.decode({ frameIndex: 0 });
    try {
      const rect = image.visibleRect ?? { x: 0, y: 0, width: image.codedWidth, height: image.codedHeight };
      const w = rect.width;
      const h = rect.height;
      const format = image.format ?? "unknown";
      if (format === "I420" || format === "I422" || format === "I444" || format === "I420A") {
        const buffer = new Uint8Array(image.allocationSize());
        const layout = await image.copyTo(buffer);
        const samples = await fromPlanes(buffer, layout, rect, format, components);
        return { width: w, height: h, exact: reduction === 0, format, samples };
      }
      if (format === "RGBA" || format === "RGBX" || format === "BGRA" || format === "BGRX") {
        const buffer = new Uint8Array(image.allocationSize({ rect }));
        const layout = await image.copyTo(buffer, { rect });
        const samples = fromRgba(buffer, layout[0], w, h, format.startsWith("B"), components);
        return { width: w, height: h, exact: false, format, samples };
      }
      throw new Error(`the decoder returned ${format} frames`);
    } finally {
      image.close();
    }
  } finally {
    decoder.close();
  }
}

// Gives the event loop a turn without setTimeout's 4 ms clamp.
const channel = typeof MessageChannel === "function" ? new MessageChannel() : null;
const resumes = [];
if (channel) channel.port1.onmessage = () => resumes.shift()?.();
function yieldToEventLoop() {
  return new Promise(resolve => {
    if (!channel) {
      setTimeout(resolve, 0);
      return;
    }
    resumes.push(resolve);
    channel.port2.postMessage(0);
  });
}

/**
 * YCbCr planes to RGB, chroma replicated (libjpeg's box upsampling for integral factors), or the luma plane for gray. Converts in
 * slices of about 6 ms, so a large photograph never holds the page's thread for a whole frame.
 */
async function fromPlanes(buffer, layout, rect, format, components) {
  const w = rect.width;
  const h = rect.height;
  const out = new Uint8Array(w * h * components);
  const y0 = layout[0];
  if (components === 1) {
    for (let y = 0; y < h; y++) {
      out.set(buffer.subarray(y0.offset + (rect.y + y) * y0.stride + rect.x, y0.offset + (rect.y + y) * y0.stride + rect.x + w), y * w);
    }
    return out;
  }
  const sx = format === "I444" ? 0 : 1;
  const sy = format === "I420" || format === "I420A" ? 1 : 0;
  const u0 = layout[1];
  const v0 = layout[2];
  let o = 0;
  let slice = performance.now();
  for (let y = 0; y < h; y++) {
    if ((y & 31) === 0 && performance.now() - slice > 6) {
      await yieldToEventLoop();
      slice = performance.now();
    }
    const yy = rect.y + y;
    const lumaRow = y0.offset + yy * y0.stride + rect.x;
    const cy = yy >> sy;
    const cbRow = u0.offset + cy * u0.stride;
    const crRow = v0.offset + cy * v0.stride;
    for (let x = 0; x < w; x++) {
      const xx = rect.x + x;
      const luma = buffer[lumaRow + x] + 384;
      const cx = xx >> sx;
      const blue = buffer[cbRow + cx];
      const red = buffer[crRow + cx];
      out[o] = clamp[luma + crToRed[red]];
      out[o + 1] = clamp[luma + ((cbToGreen[blue] + crToGreen[red]) >> 16)];
      out[o + 2] = clamp[luma + cbToBlue[blue]];
      o += 3;
    }
  }
  return out;
}

function fromRgba(buffer, plane, w, h, bgr, components) {
  const out = new Uint8Array(w * h * components);
  const r = bgr ? 2 : 0;
  const b = bgr ? 0 : 2;
  let o = 0;
  for (let y = 0; y < h; y++) {
    let i = plane.offset + y * plane.stride;
    for (let x = 0; x < w; x++, i += 4) {
      if (components === 1) {
        out[o++] = buffer[i + 1];
      } else {
        out[o] = buffer[i + r];
        out[o + 1] = buffer[i + 1];
        out[o + 2] = buffer[i + b];
        o += 3;
      }
    }
  }
  return out;
}

async function withImageBitmap(bytes, components) {
  const bitmap = await createImageBitmap(new Blob([bytes], { type: "image/jpeg" }), { colorSpaceConversion: "none", premultiplyAlpha: "none" });
  try {
    const w = bitmap.width;
    const h = bitmap.height;
    const canvas = new OffscreenCanvas(w, h);
    const context = canvas.getContext("2d", { willReadFrequently: true });
    context.drawImage(bitmap, 0, 0);
    const rgba = context.getImageData(0, 0, w, h).data;
    return { width: w, height: h, exact: false, format: "ImageBitmap", samples: fromRgba(rgba, { offset: 0, stride: w * 4 }, w, h, false, components) };
  } finally {
    bitmap.close();
  }
}
