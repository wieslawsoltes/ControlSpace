const colorCache = new Map();
function rgba(hex) {
  if (colorCache.has(hex)) return colorCache.get(hex);
  const text = hex.replace('#', ''), color = [parseInt(text.slice(0, 2), 16) / 255, parseInt(text.slice(2, 4), 16) / 255, parseInt(text.slice(4, 6), 16) / 255, text.length === 8 ? parseInt(text.slice(6, 8), 16) / 255 : 1];
  colorCache.set(hex, color); return color;
}
const INK = '#343F49', TEAL = '#007D87', GREEN = '#169849', GRID = '#D6DEE3';
export class Scene {
  constructor(width, height) { this.width = width; this.height = height; this.shapes = []; this.labels = []; this.hits = []; this.transform = { scale: 1, x: 0, y: 0 }; }
  point(x, y) { const t = this.transform; return [x * t.scale + t.x, y * t.scale + t.y]; }
  rect(x, y, width, height, fill = '#FFFFFF', stroke = null, lineWidth = 1) {
    const p = this.point(x, y), s = this.transform.scale; this.shapes.push({ kind: 'rect', x: p[0], y: p[1], width: width * s, height: height * s, fill });
    if (stroke) { this.line(x, y, x + width, y, stroke, lineWidth); this.line(x + width, y, x + width, y + height, stroke, lineWidth); this.line(x + width, y + height, x, y + height, stroke, lineWidth); this.line(x, y + height, x, y, stroke, lineWidth); }
  }
  line(x1, y1, x2, y2, color = INK, width = 1) { const a = this.point(x1, y1), b = this.point(x2, y2); this.shapes.push({ kind: 'line', x1: a[0], y1: a[1], x2: b[0], y2: b[1], color, width: width * this.transform.scale }); }
  circle(x, y, radius, fill) { const p = this.point(x, y); this.shapes.push({ kind: 'circle', x: p[0], y: p[1], radius: radius * this.transform.scale, fill }); }
  arc(x, y, rx, ry, from, to, color = INK, width = 1.3) { for (let n = 0; n < 24; n++) { const a = from + (to - from) * n / 24, b = from + (to - from) * (n + 1) / 24; this.line(x + Math.cos(a) * rx, y + Math.sin(a) * ry, x + Math.cos(b) * rx, y + Math.sin(b) * ry, color, width); } }
  text(text, x, y, options = {}) { const p = this.point(x, y); this.labels.push({ text: String(text), x: p[0], y: p[1], color: options.color ?? INK, size: (options.size ?? 12) * this.transform.scale, weight: options.bold ? 600 : 400, align: options.align ?? 'left', maxWidth: options.maxWidth ? options.maxWidth * this.transform.scale : 0, mono: options.mono ?? false }); }
  hit(id, kind, x, y, width, height, extra = {}) { const p = this.point(x, y), scale = this.transform.scale; this.hits.push({ id, kind, x: p[0], y: p[1], width: width * scale, height: height * scale, ...extra }); }
}
export class GraphicsSurface {
  constructor(geometryCanvas, textCanvas, onBackend) {
    this.canvas = geometryCanvas; this.textCanvas = textCanvas; this.textContext = textCanvas.getContext('2d'); this.onBackend = onBackend; this.backend = 'Initializing'; this.device = null; this.capacity = 0; this.lastWidth = this.lastHeight = 0; this.disposed = false;
    this.ready = this.initialize();
  }
  async initialize() {
    try {
      if (!navigator.gpu) throw new Error('WebGPU unavailable');
      const adapter = await navigator.gpu.requestAdapter(); if (!adapter) throw new Error('No WebGPU adapter');
      this.device = await adapter.requestDevice(); this.context = this.canvas.getContext('webgpu'); if (!this.context) throw new Error('WebGPU canvas unavailable');
      this.format = navigator.gpu.getPreferredCanvasFormat(); this.context.configure({ device: this.device, format: this.format, alphaMode: 'opaque' });
      const module = this.device.createShaderModule({ code: `
struct Uniforms { resolution: vec2f, padding: vec2f }; @group(0) @binding(0) var<uniform> u: Uniforms;
struct VertexOut { @builtin(position) position: vec4f, @location(0) color: vec4f };
@vertex fn vs(@location(0) point: vec2f, @location(1) color: vec4f) -> VertexOut {
  var output: VertexOut; output.position=vec4f(point.x/u.resolution.x*2.-1.,1.-point.y/u.resolution.y*2.,0.,1.); output.color=color; return output;
}
@fragment fn fs(input: VertexOut) -> @location(0) vec4f { return input.color; }` });
      this.pipeline = this.device.createRenderPipeline({ layout: 'auto', vertex: { module, entryPoint: 'vs', buffers: [{ arrayStride: 24, attributes: [{ shaderLocation: 0, offset: 0, format: 'float32x2' }, { shaderLocation: 1, offset: 8, format: 'float32x4' }] }] }, fragment: { module, entryPoint: 'fs', targets: [{ format: this.format, blend: { color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha' }, alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha' } } }] }, primitive: { topology: 'triangle-list' }, multisample: { count: 4 } });
      this.uniform = this.device.createBuffer({ size: 16, usage: GPUBufferUsage.UNIFORM | GPUBufferUsage.COPY_DST }); this.bindGroup = this.device.createBindGroup({ layout: this.pipeline.getBindGroupLayout(0), entries: [{ binding: 0, resource: { buffer: this.uniform } }] });
      this.backend = 'WebGPU geometry'; this.device.lost.then(() => { if (!this.disposed) this.fallback('Device lost'); });
    } catch (e) { this.fallback(e.message); }
    this.onBackend?.(this.backend); return this.backend;
  }
  fallback(reason) {
    if (this.context) { const next = this.canvas.cloneNode(false); this.canvas.replaceWith(next); this.canvas = next; this.context = null; }
    this.device = null; this.context2d = this.canvas.getContext('2d', { alpha: false }); this.backend = 'Canvas 2D fallback'; this.fallbackReason = reason; this.onBackend?.(this.backend);
    if (this.lastScene) this.render(this.lastScene);
  }
  resize(width, height) {
    const dpr = Math.min(window.devicePixelRatio || 1, 3), w = Math.max(1, Math.round(width * dpr)), h = Math.max(1, Math.round(height * dpr)); this.dpr = dpr;
    if (this.canvas.width !== w || this.canvas.height !== h) { this.canvas.width = this.textCanvas.width = w; this.canvas.height = this.textCanvas.height = h; this.canvas.style.width = this.textCanvas.style.width = `${width}px`; this.canvas.style.height = this.textCanvas.style.height = `${height}px`; }
    if (this.device && (this.lastWidth !== w || this.lastHeight !== h || !this.msaa)) { this.msaa?.destroy(); this.msaa = this.device.createTexture({ size: [w, h], sampleCount: 4, format: this.format, usage: GPUTextureUsage.RENDER_ATTACHMENT }); this.lastWidth = w; this.lastHeight = h; }
  }
  render(scene) {
    this.lastScene = scene; this.resize(scene.width, scene.height);
    if (this.device) this.renderGpu(scene); else if (this.context2d) this.renderCanvas(scene);
    const c = this.textContext; c.setTransform(this.dpr, 0, 0, this.dpr, 0, 0); c.clearRect(0, 0, scene.width, scene.height); c.textBaseline = 'alphabetic';
    for (const label of scene.labels) {
      if (label.y < -30 || label.y > scene.height + 30) continue;
      c.fillStyle = label.color; c.font = `${label.weight} ${label.size}px ${label.mono ? 'Consolas, monospace' : 'Arial, sans-serif'}`; c.textAlign = label.align;
      let text = label.text; if (label.maxWidth && c.measureText(text).width > label.maxWidth) { while (text.length && c.measureText(text + '…').width > label.maxWidth) text = text.slice(0, -1); text += '…'; }
      c.fillText(text, label.x, label.y);
    }
  }
  renderCanvas(scene) {
    const c = this.context2d; c.setTransform(this.dpr, 0, 0, this.dpr, 0, 0); c.fillStyle = '#FFFFFF'; c.fillRect(0, 0, scene.width, scene.height);
    for (const s of scene.shapes) {
      if (s.kind === 'rect') { c.fillStyle = s.fill; c.fillRect(s.x, s.y, s.width, s.height); }
      else if (s.kind === 'circle') { c.fillStyle = s.fill; c.beginPath(); c.arc(s.x, s.y, s.radius, 0, Math.PI * 2); c.fill(); }
      else { c.strokeStyle = s.color; c.lineWidth = s.width; c.beginPath(); c.moveTo(s.x1, s.y1); c.lineTo(s.x2, s.y2); c.stroke(); }
    }
  }
  renderGpu(scene) {
    const vertices = [], vertex = (x, y, color) => vertices.push(x, y, ...color);
    const triangle = (a, b, c, color) => { vertex(...a, color); vertex(...b, color); vertex(...c, color); };
    const quad = (a, b, c, d, color) => { triangle(a, b, c, color); triangle(a, c, d, color); };
    for (const s of scene.shapes) {
      if (s.kind === 'rect') quad([s.x, s.y], [s.x + s.width, s.y], [s.x + s.width, s.y + s.height], [s.x, s.y + s.height], rgba(s.fill));
      else if (s.kind === 'line') { const dx = s.x2 - s.x1, dy = s.y2 - s.y1, length = Math.hypot(dx, dy); if (!length) continue; const nx = -dy / length * s.width / 2, ny = dx / length * s.width / 2; quad([s.x1 + nx, s.y1 + ny], [s.x2 + nx, s.y2 + ny], [s.x2 - nx, s.y2 - ny], [s.x1 - nx, s.y1 - ny], rgba(s.color)); }
      else { const segments = Math.max(8, Math.min(48, Math.ceil(s.radius * 3))); for (let i = 0; i < segments; i++) triangle([s.x, s.y], [s.x + Math.cos(i / segments * Math.PI * 2) * s.radius, s.y + Math.sin(i / segments * Math.PI * 2) * s.radius], [s.x + Math.cos((i + 1) / segments * Math.PI * 2) * s.radius, s.y + Math.sin((i + 1) / segments * Math.PI * 2) * s.radius], rgba(s.fill)); }
    }
    const data = new Float32Array(vertices);
    if (data.byteLength > this.capacity) { this.buffer?.destroy(); this.capacity = Math.max(4096, 2 ** Math.ceil(Math.log2(data.byteLength))); this.buffer = this.device.createBuffer({ size: this.capacity, usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST }); }
    if (data.byteLength) this.device.queue.writeBuffer(this.buffer, 0, data);
    this.device.queue.writeBuffer(this.uniform, 0, new Float32Array([scene.width, scene.height, 0, 0]));
    const encoder = this.device.createCommandEncoder(), pass = encoder.beginRenderPass({ colorAttachments: [{ view: this.msaa.createView(), resolveTarget: this.context.getCurrentTexture().createView(), clearValue: { r: 1, g: 1, b: 1, a: 1 }, loadOp: 'clear', storeOp: 'discard' }] });
    if (data.length) { pass.setPipeline(this.pipeline); pass.setBindGroup(0, this.bindGroup); pass.setVertexBuffer(0, this.buffer); pass.draw(data.length / 6); } pass.end(); this.device.queue.submit([encoder.finish()]);
  }
  dispose() { this.disposed = true; this.buffer?.destroy(); this.msaa?.destroy(); this.uniform?.destroy(); this.device?.destroy(); }
}
export function ladderScene(width, height, project, block, runtime, selection, zoom = 1, scroll = 0) {
  const s = new Scene(width, height), tags = new Map(project.tags.map(t => [t.name, t])), flow = id => runtime?.flow.get(id) ?? false;
  s.transform = { scale: zoom, x: 0, y: -scroll * zoom }; const logicalWidth = Math.max(540, width / zoom); let y = 8;
  block.networks.forEach((n, ni) => {
    const nh = Math.max(180, 124 + n.branches.length * 58);
    if (y + nh < scroll || y > scroll + height / zoom) { y += nh + 10; return; }
    s.rect(8, y, logicalWidth - 16, nh, '#FFFFFF', '#D8DEE2'); s.rect(8, y, logicalWidth - 16, 26, selection === n.id ? '#D2E6F5' : '#DADDE0'); s.hit(n.id, 'network', 8, y, logicalWidth - 16, 26);
    s.text('▾', 17, y + 17, { size: 11 }); s.text(`Network ${ni + 1}:`, 33, y + 17, { bold: true }); s.text(n.title, 116, y + 17, { bold: true, maxWidth: logicalWidth - 160 });
    s.text(n.comment, 24, y + 46, { size: 11, color: '#7A838C', maxWidth: logicalWidth - 48 });
    const left = 48, merge = logicalWidth - 205, outputX = logicalWidth - 105, firstY = y + 105, lastY = firstY + (n.branches.length - 1) * 58;
    s.line(left, firstY - 18, left, lastY + 21, flow(n.id) ? GREEN : INK, 2);
    if (n.branches.length > 1) s.line(merge, firstY, merge, lastY, flow(n.id) ? GREEN : INK, 1.2);
    n.branches.forEach((branch, bi) => {
      const by = firstY + bi * 58, spacing = (merge - left - 20) / Math.max(1, branch.length);
      let previousX = left, previousPower = true;
      branch.forEach((op, ci) => {
        const x = left + 12 + (ci + .5) * spacing, active = flow(op.id), col = active ? GREEN : INK;
        s.line(previousX, by, x - 14, by, runtime?.flow.size && previousPower ? GREEN : INK, 1.3);
        s.hit(op.id, 'instruction', x - 55, by - 42, 110, 60, { network: n.id });
        if (selection === op.id) s.rect(x - 52, by - 44, 104, 65, '#EAF4FC', '#55A0D0');
        s.rect(x - 14, by - 12, 28, 25, '#FFFFFF');
        if (['Greater', 'Less', 'Equal'].includes(op.kind)) { s.rect(x - 18, by - 12, 36, 24, '#EDF1F5', col); s.text(op.kind === 'Greater' ? '>' : op.kind === 'Less' ? '<' : '==', x, by + 5, { size: 15, align: 'center', color: col }); s.text(op.parameter, x, by + 29, { align: 'center', color: TEAL, size: 10 }); }
        else { s.line(x - 7, by - 10, x - 7, by + 10, col, 1.5); s.line(x + 7, by - 10, x + 7, by + 10, col, 1.5); if (op.kind === 'NegatedContact') s.line(x - 12, by + 12, x + 12, by - 12, col, 1.4); if (op.kind === 'RisingEdge' || op.kind === 'FallingEdge') s.text(op.kind === 'RisingEdge' ? 'P' : 'N', x, by + 4, { align: 'center', size: 10, color: col }); }
        s.text(tags.get(op.tag)?.address ?? '???', x, by - 33, { color: TEAL, align: 'center', size: 10 }); s.text(`"${op.tag}"`, x, by - 19, { color: TEAL, align: 'center', size: 11, maxWidth: Math.max(65, spacing - 8) });
        previousX = x + 14; previousPower = active;
      });
      s.line(previousX, by, merge, by, runtime?.flow.size && previousPower ? GREEN : INK, 1.3);
    });
    const o = n.output, oc = flow(o.id) ? GREEN : INK, blockOutput = ['TimerOn', 'TimerOff', 'Pulse', 'CountUp', 'Move'].includes(o.kind);
    s.line(merge, firstY, outputX - (blockOutput ? 49 : 17), firstY, flow(n.id) ? GREEN : INK, 1.3); s.line(outputX + (blockOutput ? 49 : 17), firstY, logicalWidth - 31, firstY, flow(o.id) ? GREEN : INK, 1.3);
    s.hit(o.id, 'instruction', outputX - 61, firstY - 48, 122, blockOutput ? 108 : 79, { network: n.id });
    if (selection === o.id) s.rect(outputX - 60, firstY - 47, 120, blockOutput ? 107 : 78, '#EAF4FC', '#55A0D0');
    s.text(tags.get(o.tag)?.address ?? '???', outputX, firstY - (blockOutput ? 42 : 33), { color: TEAL, align: 'center', size: 10 }); s.text(`"${o.tag}"`, outputX, firstY - (blockOutput ? 28 : 19), { color: TEAL, align: 'center', size: 11, maxWidth: 125 });
    if (blockOutput) {
      s.rect(outputX - 48, firstY - 17, 96, 69, '#EEF0F5', oc); s.rect(outputX - 48, firstY - 17, 96, 20, '#D4DBE5');
      s.text(({ TimerOn: 'TON', TimerOff: 'TOF', Pulse: 'TP', CountUp: 'CTU', Move: 'MOVE' })[o.kind], outputX, firstY - 3, { bold: true, align: 'center' });
      s.text('IN', outputX - 42, firstY + 19, { size: 10 }); s.text('Q', outputX + 32, firstY + 19, { size: 10 }); s.text(o.kind === 'CountUp' ? 'R' : 'PT', outputX - 42, firstY + 43, { size: 10 });
      s.text(o.kind === 'CountUp' ? o.auxiliary : `T#${o.parameter}ms`, outputX - 16, firstY + 43, { size: 10, color: TEAL, maxWidth: 62 });
      if (runtime) s.text(String(runtime.read(o.tag)), outputX + 54, firstY + 6, { size: 10, color: GREEN });
    } else {
      s.arc(outputX + 1, firstY, 11, 12, Math.PI * .63, Math.PI * 1.37, oc, 1.4); s.arc(outputX - 1, firstY, 11, 12, -Math.PI * .37, Math.PI * .37, oc, 1.4);
      if (o.kind === 'SetCoil' || o.kind === 'ResetCoil') s.text(o.kind === 'SetCoil' ? 'S' : 'R', outputX, firstY + 4, { size: 10, align: 'center', color: oc });
      if (runtime) s.text(runtime.read(o.tag) ? 'TRUE' : 'FALSE', outputX, firstY + 30, { size: 10, align: 'center', color: oc });
    }
    y += nh + 10;
  });
  s.contentHeight = y; return s;
}
export function devicesScene(width, height, project, selection, drag = null) {
  const s = new Scene(width, height), scale = Math.min(1, (width - 20) / 1010); s.transform = { scale, x: 10, y: 15 };
  for (let x = 0; x < width / scale; x += 20) for (let y = 0; y < height / scale; y += 20) s.circle(x, y, .6, '#D4DEE5');
  const position = d => drag?.id === d.id ? drag : d;
  for (const l of project.links) { const a = position(project.devices.find(d => d.id === l.from)), b = position(project.devices.find(d => d.id === l.to)), ax = a.x + 108, bx = b.x + 108, y = Math.max(a.y, b.y) + 213; s.line(ax, a.y + 142, ax, y, '#359B57', 3); s.line(ax, y, bx, y, '#359B57', 3); s.line(bx, y, bx, b.y + 142, '#359B57', 3); s.text(l.subnet, (ax + bx) / 2, y + 22, { align: 'center', color: GREEN, size: 12 }); }
  for (const d of project.devices) {
    const { x, y } = position(d); s.hit(d.id, 'device', x - 5, y - 36, 252, 230);
    if (selection === d.id) s.rect(x - 8, y - 38, 254, 226, '#E8F3FB', '#4394CB');
    s.text(d.name, x + 4, y - 19, { size: 16, bold: true, color: TEAL }); s.text(d.model, x + 4, y - 3, { size: 10, color: '#7E8994' });
    s.rect(x, y + 8, 237, 133, '#3C454D', '#222B32'); s.rect(x + 8, y + 16, 221, 24, '#16808A'); s.text(d.kind === 'Hmi' ? 'CONTROLSPACE  HMI' : 'CONTROLSPACE  /  IO', x + 18, y + 32, { color: '#FFFFFF', size: 10, bold: true });
    if (d.kind === 'Hmi') { s.rect(x + 14, y + 50, 209, 78, '#CAD9E3', '#222B32'); s.text('Production overview', x + 26, y + 69, { size: 12, color: TEAL }); s.rect(x + 27, y + 86, 64, 25, TEAL); s.text('START', x + 59, y + 102, { size: 10, color: '#FFFFFF', align: 'center' }); s.rect(x + 108, y + 86, 99, 25, '#ECF2F6'); s.text('65 %', x + 132, y + 102, { size: 13, color: TEAL }); }
    else d.modules.slice(0, 5).forEach((m, index) => { const mx = x + 10 + index * 44; s.rect(mx, y + 51, 39, 78, index === 0 ? '#75818B' : '#65727B', '#273139'); for (let k = 0; k < 4; k++) s.circle(mx + 7, y + 61 + k * 10, 2.1, k === 0 ? '#72C371' : '#B0B9AE'); s.text(index === 0 ? 'CPU' : m.startsWith('DI') ? 'DI' : 'DQ', mx + 19, y + 114, { size: 9, color: '#FFFFFF', align: 'center' }); s.text(index + 1, mx + 19, y + 126, { size: 8, color: '#CCD5DB', align: 'center' }); });
    s.rect(x + 100, y + 135, 19, 13, '#2D754C', '#1C402B'); s.text(d.ipAddress, x + 6, y + 168, { size: 12, color: TEAL }); s.text('Offline configuration  •  Simulated device', x + 6, y + 184, { size: 10, color: '#768694' });
  }
  s.editScale = scale; return s;
}
export function hmiScene(width, height, screen, project, runtime, selection, preview, drag = null) {
  const s = new Scene(width, height); s.rect(0, 0, width, height, '#DCE1E5');
  if (!screen) { s.text('This project has no HMI screens.', 30, 50, { size: 16, color: TEAL }); return s; }
  const scale = Math.max(.1, Math.min((width - 44) / screen.width, (height - 44) / screen.height)), tx = (width - screen.width * scale) / 2, ty = 22;
  s.transform = { scale, x: tx, y: ty }; s.rect(0, 0, screen.width, screen.height, '#F5F8FB', '#92A3B0');
  if (!preview) for (let x = 0; x <= screen.width; x += 20) for (let y = 0; y <= screen.height; y += 20) s.circle(x, y, .55, '#D4DFE7');
  s.rect(0, screen.height - 12, screen.width, 12, '#007E88');
  for (const o of screen.objects) {
    const { x, y } = drag?.id === o.id ? drag : o, w = o.width, h = o.height, tag = project.tags.find(t => t.name.toLowerCase() === o.tag.toLowerCase()), value = tag ? runtime?.read(tag.name) ?? tag.initialValue : 0;
    s.hit(o.id, 'hmi', x, y, w, h, { tag: o.tag, hmiKind: o.kind });
    switch (o.kind) {
      case 'Label': s.text(o.text, x, y + h * .72, { size: h > 35 ? 26 : 15, bold: h > 35, color: o.color, maxWidth: w }); break;
      case 'Button': s.rect(x, y, w, h, value ? '#23865B' : o.color); s.text(o.text, x + w / 2, y + h / 2 + 6, { size: 16, bold: true, align: 'center', color: '#FFFFFF' }); break;
      case 'Lamp': s.rect(x, y, w, h, '#FFFFFF', '#D3DFE6'); s.circle(x + 24, y + h / 2, 8, value ? '#1DAB66' : '#9FAEBB'); s.text(o.text, x + 43, y + h / 2 - 5, { size: 11, bold: true, maxWidth: w - 53 }); s.text(value ? 'Active' : 'Inactive', x + 43, y + h / 2 + 17, { size: 12, color: '#6C8292' }); break;
      case 'Numeric': s.rect(x, y, w, h, '#FFFFFF', '#D3DFE6'); s.text(o.text, x + 20, y + 24, { size: 11, bold: true, color: '#607C91' }); s.text(Number(value.toFixed(2)), x + 20, y + 77, { size: 39, bold: true, color: '#27465B' }); break;
      case 'Gauge': s.rect(x, y, w, h, '#FFFFFF', '#D3DFE6'); s.text(o.text, x + 21, y + 28, { size: 11, bold: true, color: '#607C91' }); s.text(`${Number(value.toFixed(1))}`, x + 22, y + h * .65, { size: 52, bold: true, color: '#27465B' }); s.text('%', x + 113, y + h * .65, { size: 22, color: '#768E9F' }); s.rect(x + 22, y + h - 39, w - 44, 7, '#DDE6EE'); s.rect(x + 22, y + h - 39, (w - 44) * Math.min(1, Math.max(0, value / 100)), 7, o.color); s.text('0', x + 22, y + h - 14, { size: 9, color: '#8299A9' }); s.text('100', x + w - 22, y + h - 14, { size: 9, color: '#8299A9', align: 'right' }); break;
      case 'Tank': s.rect(x, y, w, h, '#DAE8F0', '#698DA3'); const fill = h * Math.min(1, Math.max(0, value / 100)); s.rect(x + 3, y + h - fill, w - 6, fill, o.color); s.text(o.text, x + 12, y + 27, { bold: true, maxWidth: w - 24 }); break;
    }
    if (!preview && selection === o.id) { s.rect(x - 3, y - 3, w + 6, h + 6, '#FFFFFF00', '#267DB9', 1 / scale); for (const cx of [x, x + w]) for (const cy of [y, y + h]) s.rect(cx - 3.5, cy - 3.5, 7, 7, '#FFFFFF', '#267DB9', 1 / scale); }
  }
  s.editScale = scale; return s;
}
export function traceScene(width, height, runtime, channel = 'Speed_Actual') {
  const s = new Scene(width, height), x0 = 62, y0 = 45, w = width - 90, h = height - 101, samples = runtime?.trace.read().slice(-300) ?? [];
  if (runtime && !runtime.program.symbols.has(channel.toLowerCase())) channel = runtime.program.project.tags.find(t => t.type !== 'Bool')?.name ?? runtime.program.project.tags[0]?.name ?? '';
  if (!channel) { s.text('This project has no tags to trace.', x0, y0, { size: 16, color: TEAL }); return s; }
  const slot = runtime?.slot(channel), max = Math.max(100, ...samples.map(sample => sample.values[slot])), min = Math.min(0, ...samples.map(sample => sample.values[slot])), range = max - min;
  s.text(channel, x0, 24, { bold: true, color: TEAL }); s.text('Virtual scan time · fixed 100 ms samples', width - 22, 24, { size: 11, color: '#7B8C98', align: 'right' });
  for (let i = 0; i <= 5; i++) { const y = y0 + h / 5 * i; s.line(x0, y, x0 + w, y, '#DFE5EB'); s.text(Number((max - range * i / 5).toFixed(0)), x0 - 12, y + 4, { align: 'right', size: 10, color: '#7A8C99' }); }
  for (let i = 0; i <= 10; i++) s.line(x0 + i * w / 10, y0, x0 + i * w / 10, y0 + h, '#EDF0F3');
  if (samples.length > 1) { for (let i = 1; i < samples.length; i++) s.line(x0 + (i - 1) * w / (samples.length - 1), y0 + h * (1 - (samples[i - 1].values[slot] - min) / range), x0 + i * w / (samples.length - 1), y0 + h * (1 - (samples[i].values[slot] - min) / range), TEAL, 2); s.text(`${(samples[0].virtualMilliseconds / 1000).toFixed(1)} s`, x0, height - 28, { size: 10, color: '#788E9D' }); s.text(`${(samples.at(-1).virtualMilliseconds / 1000).toFixed(1)} s`, width - 28, height - 28, { size: 10, color: '#788E9D', align: 'right' }); }
  else s.text('Start simulation to acquire trace samples.', x0 + 24, y0 + 38, { size: 14, color: '#728795' });
  return s;
}
