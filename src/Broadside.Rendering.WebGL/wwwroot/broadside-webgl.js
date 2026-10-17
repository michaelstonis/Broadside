// Broadside WebGL 2 backend (#353): executes the command list the .NET recorder (Broadside.Rendering.WebGL) builds from a display
// list. No dependencies. One context for the page; .NET drives it synchronously (single-threaded WebAssembly).
//
// Stencil layout: bit 7 = inside the current clip, bits 0-6 = the winding of the path being filled (non-zero: modulo 128 by
// INCR_WRAP/DECR_WRAP on the two faces; even-odd: bit 0 by INVERT; union: set to 1). A fill writes its winding only inside the clip,
// then a cover quad paints where the winding is not zero and resets it (stencil-then-cover). A clip writes the winding of its path
// the same way, then one full quad keeps bit 7 only where the winding is not zero.

let canvas = null;
let gl = null;
let program = null;
let uniforms = null;
let buffer = null;
let framebuffer = null;
let colorBuffer = null;
let stencilBuffer = null;
let width = 0;
let height = 0;
let samples = 0;
let maxTextureSize = 0;
let readBuffer = new Uint8Array(0);
const textures = new Map();

const vertexSource = `#version 300 es
in vec4 a;
uniform vec2 size;
out vec2 uv;
void main() {
  uv = a.zw;
  gl_Position = vec4(a.x / size.x * 2.0 - 1.0, 1.0 - a.y / size.y * 2.0, 0.0, 1.0);
}`;

const fragmentSource = `#version 300 es
precision highp float;
in vec2 uv;
uniform int mode;
uniform vec4 color;
uniform sampler2D image;
out vec4 result;
void main() {
  if (mode == 0) {
    result = color;
  } else if (mode == 1) {
    result = texture(image, uv) * color.a;
  } else {
    result = color * texture(image, uv).r;
  }
}`;

function compile(type, source) {
  const shader = gl.createShader(type);
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    throw new Error(gl.getShaderInfoLog(shader) ?? 'shader');
  }
  return shader;
}

/** Creates the context; returns "" when WebGL 2 is unavailable, else a description of the renderer. */
export function initialize() {
  if (gl) {
    return describe();
  }
  try {
    canvas = typeof OffscreenCanvas === 'function' ? new OffscreenCanvas(256, 256) : document.createElement('canvas');
    gl = canvas.getContext('webgl2', { alpha: true, antialias: false, depth: false, stencil: false, premultipliedAlpha: true, preserveDrawingBuffer: true });
  } catch {
    gl = null;
  }
  if (!gl) {
    return '';
  }
  program = gl.createProgram();
  gl.attachShader(program, compile(gl.VERTEX_SHADER, vertexSource));
  gl.attachShader(program, compile(gl.FRAGMENT_SHADER, fragmentSource));
  gl.bindAttribLocation(program, 0, 'a');
  gl.linkProgram(program);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    gl = null;
    return '';
  }
  gl.useProgram(program);
  uniforms = {
    size: gl.getUniformLocation(program, 'size'),
    mode: gl.getUniformLocation(program, 'mode'),
    color: gl.getUniformLocation(program, 'color'),
    image: gl.getUniformLocation(program, 'image'),
  };
  gl.uniform1i(uniforms.image, 0);
  buffer = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.enableVertexAttribArray(0);
  gl.vertexAttribPointer(0, 4, gl.FLOAT, false, 16, 0);
  gl.enable(gl.BLEND);
  gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
  gl.disable(gl.CULL_FACE);
  gl.disable(gl.DEPTH_TEST);
  samples = Math.min(8, gl.getParameter(gl.MAX_SAMPLES) | 0);
  maxTextureSize = gl.getParameter(gl.MAX_TEXTURE_SIZE) | 0;
  return describe();
}

function describe() {
  let renderer = '';
  try {
    const info = gl.getExtension('WEBGL_debug_renderer_info');
    renderer = info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
  } catch {
    renderer = 'WebGL 2';
  }
  return `${renderer || 'WebGL 2'}; ${samples}x MSAA; max texture ${maxTextureSize}`;
}

export function getMaxTextureSize() {
  return maxTextureSize;
}

export function getCanvas() {
  return canvas;
}

function ensureSize(w, h) {
  if (w <= width && h <= height && framebuffer) {
    return;
  }
  width = Math.max(width, w, 256);
  height = Math.max(height, h, 256);
  canvas.width = width;
  canvas.height = height;
  if (framebuffer) {
    gl.deleteFramebuffer(framebuffer);
    gl.deleteRenderbuffer(colorBuffer);
    gl.deleteRenderbuffer(stencilBuffer);
  }
  framebuffer = gl.createFramebuffer();
  gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
  colorBuffer = gl.createRenderbuffer();
  gl.bindRenderbuffer(gl.RENDERBUFFER, colorBuffer);
  gl.renderbufferStorageMultisample(gl.RENDERBUFFER, samples, gl.RGBA8, width, height);
  gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.RENDERBUFFER, colorBuffer);
  stencilBuffer = gl.createRenderbuffer();
  gl.bindRenderbuffer(gl.RENDERBUFFER, stencilBuffer);
  gl.renderbufferStorageMultisample(gl.RENDERBUFFER, samples, gl.DEPTH24_STENCIL8, width, height);
  gl.framebufferRenderbuffer(gl.FRAMEBUFFER, gl.DEPTH_STENCIL_ATTACHMENT, gl.RENDERBUFFER, stencilBuffer);
}

/** Uploads a texture: format 0 = premultiplied RGBA, 1 = one 8-bit channel (a mask). */
export function upload(id, w, h, format, pixels, interpolate) {
  release(id);
  const texture = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  const data = pixels.slice();
  if (format === 0) {
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, w, h, 0, gl.RGBA, gl.UNSIGNED_BYTE, data);
  } else {
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.R8, w, h, 0, gl.RED, gl.UNSIGNED_BYTE, data);
  }
  gl.generateMipmap(gl.TEXTURE_2D);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR_MIPMAP_LINEAR);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, interpolate ? gl.LINEAR : gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  textures.set(id, texture);
}

export function release(id) {
  const texture = textures.get(id);
  if (texture) {
    gl.deleteTexture(texture);
    textures.delete(id);
  }
}

const STENCIL = 1;
const COVER = 2;
const SOLID = 3;
const CLIP = 4;
const CLIP_RESET = 5;
const IMAGE = 6;
const MASK = 7;

function setStencilFill(rule) {
  gl.colorMask(false, false, false, false);
  if (rule === 1) {
    gl.stencilFunc(gl.EQUAL, 0x80, 0x80);
    gl.stencilMask(0x01);
    gl.stencilOp(gl.KEEP, gl.KEEP, gl.INVERT);
  } else if (rule === 2) {
    gl.stencilFunc(gl.EQUAL, 0x81, 0x80);
    gl.stencilMask(0x7f);
    gl.stencilOp(gl.KEEP, gl.KEEP, gl.REPLACE);
  } else {
    gl.stencilFunc(gl.EQUAL, 0x80, 0x80);
    gl.stencilMask(0x7f);
    gl.stencilOpSeparate(gl.FRONT, gl.KEEP, gl.KEEP, gl.INCR_WRAP);
    gl.stencilOpSeparate(gl.BACK, gl.KEEP, gl.KEEP, gl.DECR_WRAP);
  }
}

function setClipped() {
  gl.colorMask(true, true, true, true);
  gl.stencilFunc(gl.EQUAL, 0x80, 0x80);
  gl.stencilMask(0);
  gl.stencilOp(gl.KEEP, gl.KEEP, gl.KEEP);
}

/**
 * Draws a command list into a w x h area and either reads the pixels back into `target` (premultiplied RGBA, bottom row first) or,
 * when `target` is empty, leaves them in the canvas's top-left corner for `getCanvas()`.
 */
export function render(w, h, red, green, blue, alpha, vertexBytes, commandView, target) {
  ensureSize(w, h);
  const vertices = new Float32Array(vertexBytes.slice().buffer);
  const commands = commandView.slice();
  const floats = new Float32Array(commands.buffer);
  gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
  gl.viewport(0, height - h, w, h);
  gl.enable(gl.SCISSOR_TEST);
  gl.scissor(0, height - h, w, h);
  gl.disable(gl.STENCIL_TEST);
  gl.colorMask(true, true, true, true);
  gl.clearColor(red, green, blue, alpha);
  gl.stencilMask(0xff);
  gl.clearStencil(0x80);
  gl.clear(gl.COLOR_BUFFER_BIT | gl.STENCIL_BUFFER_BIT);
  gl.enable(gl.STENCIL_TEST);
  gl.useProgram(program);
  gl.uniform2f(uniforms.size, w, h);
  gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
  gl.bufferData(gl.ARRAY_BUFFER, vertices, gl.STREAM_DRAW);
  let mode = -1;
  const setMode = (m) => {
    if (m !== mode) {
      gl.uniform1i(uniforms.mode, m);
      mode = m;
    }
  };
  let i = 0;
  while (i < commands.length) {
    const op = commands[i];
    if (op === STENCIL) {
      setStencilFill(commands[i + 1]);
      gl.drawArrays(gl.TRIANGLES, commands[i + 2], commands[i + 3]);
      i += 4;
    } else if (op === COVER || op === SOLID) {
      if (op === COVER) {
        gl.colorMask(true, true, true, true);
        gl.stencilFunc(gl.NOTEQUAL, 0, 0x7f);
        gl.stencilMask(0x7f);
        gl.stencilOp(gl.KEEP, gl.KEEP, gl.ZERO);
      } else {
        setClipped();
      }
      setMode(0);
      gl.uniform4f(uniforms.color, floats[i + 3], floats[i + 4], floats[i + 5], floats[i + 6]);
      gl.drawArrays(gl.TRIANGLES, commands[i + 1], commands[i + 2]);
      i += 7;
    } else if (op === CLIP) {
      setStencilFill(commands[i + 1]);
      gl.drawArrays(gl.TRIANGLES, commands[i + 2], commands[i + 3]);
      gl.stencilFunc(gl.NOTEQUAL, 0x80, 0x7f);
      gl.stencilMask(0xff);
      gl.stencilOp(gl.ZERO, gl.ZERO, gl.REPLACE);
      gl.drawArrays(gl.TRIANGLES, 0, 6);
      i += 4;
    } else if (op === CLIP_RESET) {
      gl.stencilMask(0xff);
      gl.clear(gl.STENCIL_BUFFER_BIT);
      i += 1;
    } else if (op === IMAGE || op === MASK) {
      const texture = textures.get(commands[i + 1]);
      if (texture) {
        setClipped();
        gl.bindTexture(gl.TEXTURE_2D, texture);
        setMode(op === IMAGE ? 1 : 2);
        gl.uniform4f(uniforms.color, floats[i + 4], floats[i + 5], floats[i + 6], floats[i + 7]);
        gl.drawArrays(gl.TRIANGLES, commands[i + 2], commands[i + 3]);
      }
      i += 8;
    } else {
      throw new Error(`broadside-webgl: unknown command ${op} at ${i}`);
    }
  }
  gl.disable(gl.STENCIL_TEST);
  gl.disable(gl.SCISSOR_TEST);
  // Resolve the samples into the canvas's own buffer, top-left corner.
  gl.bindFramebuffer(gl.READ_FRAMEBUFFER, framebuffer);
  gl.bindFramebuffer(gl.DRAW_FRAMEBUFFER, null);
  gl.blitFramebuffer(0, height - h, w, height, 0, height - h, w, height, gl.COLOR_BUFFER_BIT, gl.NEAREST);
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  if (target.byteLength > 0) {
    readLast(w, h, target);
  }
}

/** Reads the last render (w x h, the canvas's top-left corner) into `target`: premultiplied RGBA, bottom row first. */
export function readLast(w, h, target) {
  const length = w * h * 4;
  if (readBuffer.length < length) {
    readBuffer = new Uint8Array(length);
  }
  const view = readBuffer.subarray(0, length);
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  gl.readPixels(0, height - h, w, h, gl.RGBA, gl.UNSIGNED_BYTE, view);
  target.set(view);
}
