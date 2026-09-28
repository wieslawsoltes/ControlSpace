// This JavaScript model belongs to the interaction prototype, not the Uno build.
export const FORMAT = 'controlspace.project';
export const VERSION = 1;
export const TYPES = ['Bool', 'Int', 'DInt', 'Real', 'Time'];
export const KINDS = ['Contact', 'NegatedContact', 'RisingEdge', 'FallingEdge', 'Greater', 'Less', 'Equal', 'Coil', 'SetCoil', 'ResetCoil', 'TimerOn', 'TimerOff', 'Pulse', 'CountUp', 'Move'];
export const HMI_KINDS = ['Label', 'Button', 'Lamp', 'Tank', 'Numeric', 'Gauge'];
export const uid = (prefix = 'id') => `${prefix}-${globalThis.crypto?.randomUUID?.() ?? Math.random().toString(36).slice(2)}`;
export const clone = value => structuredClone(value);
export const isInput = tag => /^%I/i.test(tag.address);
export const isOutput = tag => /^%Q/i.test(tag.address);
export const isValidValue = (type, value) => Number.isFinite(value) && ({
  Bool: () => value === 0 || value === 1,
  Int: () => Number.isInteger(value) && value >= -32768 && value <= 32767,
  DInt: () => Number.isInteger(value) && value >= -2147483648 && value <= 2147483647,
  Real: () => Math.abs(value) <= 3.4028234663852886e38,
  Time: () => Number.isInteger(value) && value >= 0 && value <= 2147483647
}[type]?.() ?? false);
export function formatValue(type, value) {
  if (type === 'Bool') return value ? 'TRUE' : 'FALSE';
  if (type === 'Time') return `T#${value}ms`;
  return Number(value.toFixed(3)).toString();
}
export const instruction = (kind, tag, parameter = 0, auxiliary = '') => ({ id: uid('op'), kind, tag, parameter, auxiliary });
export function createDemo() {
  const tag = (name, type, address, initialValue = 0, comment = '', retain = false) => ({ name, type, address, initialValue, comment, retain });
  const i = (id, kind, name, parameter = 0, auxiliary = '') => ({ id, kind, tag: name, parameter, auxiliary });
  const obj = (id, kind, text, tag, x, y, width, height, color = '#008C95') => ({ id, kind, text, tag, x, y, width, height, color });
  return {
    format: FORMAT, version: VERSION, id: 'conveyor-demo', name: 'Conveyor_Line', revision: 0,
    tags: [tag('Start_PB', 'Bool', '%I0.0', 0, 'Start conveyor'), tag('Stop_PB', 'Bool', '%I0.1', 0, 'Stop conveyor (simulation only)'), tag('Part_Sensor', 'Bool', '%I0.2', 0, 'Product detection'), tag('Reset_Count', 'Bool', '%I0.3', 0, 'Reset production counter'), tag('Motor_Run', 'Bool', '%Q0.0', 0, 'Conveyor motor enable'), tag('Ready_Lamp', 'Bool', '%Q0.1', 0, 'Run-up complete'), tag('Run_Delay', 'Bool', '%M0.0', 0, 'TON output'), tag('Delay_ET', 'Time', '%MD4', 0, 'Elapsed run-up time'), tag('Part_Count', 'DInt', '%MD8', 0, 'Rising-edge product count', true), tag('Speed_Setpoint', 'Real', '%MD12', 65, 'Conveyor setpoint (%)', true), tag('Speed_Actual', 'Real', '%MD16', 0, 'Simulated conveyor speed (%)')],
    blocks: [{ id: 'main', name: 'Main', number: 1, language: 'LAD', cyclic: true, source: '', networks: [
      { id: 'net-start', title: 'Conveyor start / stop', comment: 'Seal-in circuit • Start_PB or Motor_Run, inhibited by Stop_PB.', branches: [[i('start', 'Contact', 'Start_PB'), i('stop', 'NegatedContact', 'Stop_PB')], [i('hold', 'Contact', 'Motor_Run'), i('stop-hold', 'NegatedContact', 'Stop_PB')]], output: i('coil', 'Coil', 'Motor_Run') },
      { id: 'net-delay', title: 'Run-up delay', comment: 'Signal ready after the motor has been running for two seconds.', branches: [[i('run-contact', 'Contact', 'Motor_Run')]], output: i('ton', 'TimerOn', 'Run_Delay', 2000, 'Delay_ET') },
      { id: 'net-ready', title: 'Ready indication', comment: 'Enable the ready indicator after the run-up time.', branches: [[i('ready-contact', 'Contact', 'Run_Delay')]], output: i('ready-coil', 'Coil', 'Ready_Lamp') },
      { id: 'net-count', title: 'Production counter', comment: 'Count rising edges at the product sensor; Reset_Count resets the count.', branches: [[i('part-contact', 'Contact', 'Part_Sensor')]], output: i('ctu', 'CountUp', 'Part_Count', 0, 'Reset_Count') }
    ] }, { id: 'speed', name: 'Speed_Control', number: 2, language: 'SCL', cyclic: true, networks: [], source: '// Cyclic speed control — simulation only\nIF "Motor_Run" THEN\n    "Speed_Actual" := "Speed_Setpoint";\nELSE\n    "Speed_Actual" := 0;\nEND_IF;\n' }],
    devices: [
      { id: 'plc', name: 'PLC_1', kind: 'Controller', model: 'Generic CPU 1500 · simulation', ipAddress: '192.168.0.1', x: 50, y: 100, modules: ['CPU', 'DI 16×24 V', 'DQ 16×24 V'] },
      { id: 'hmi', name: 'HMI_1', kind: 'Hmi', model: 'Comfort panel · simulation', ipAddress: '192.168.0.2', x: 385, y: 100, modules: [] },
      { id: 'io', name: 'IO_Station', kind: 'RemoteIo', model: 'Remote I/O · simulation', ipAddress: '192.168.0.3', x: 710, y: 100, modules: ['IM', 'DI 8', 'DQ 8'] }
    ],
    links: [{ id: 'link1', from: 'plc', to: 'hmi', subnet: 'PN/IE_1' }, { id: 'link2', from: 'hmi', to: 'io', subnet: 'PN/IE_1' }],
    screens: [{ id: 'overview', name: 'Overview', width: 960, height: 540, objects: [obj('title', 'Label', 'CONVEYOR LINE / 01', '', 32, 24, 650, 48), obj('subtitle', 'Label', 'Production overview', '', 32, 82, 450, 30, '#667C8C'), obj('motor-lamp', 'Lamp', 'MOTOR RUNNING', 'Motor_Run', 40, 160, 190, 88), obj('ready-lamp', 'Lamp', 'SYSTEM READY', 'Ready_Lamp', 270, 160, 190, 88), obj('speed-display', 'Gauge', 'CONVEYOR SPEED', 'Speed_Actual', 540, 145, 330, 185), obj('count-display', 'Numeric', 'PARTS PRODUCED', 'Part_Count', 42, 290, 370, 100), obj('start-button', 'Button', 'START', 'Start_PB', 42, 438, 180, 58), obj('stop-button', 'Button', 'STOP', 'Stop_PB', 250, 438, 180, 58, '#B65045')] }]
  };
}
const fields = {
  project: ['format', 'version', 'id', 'name', 'revision', 'tags', 'blocks', 'devices', 'links', 'screens'],
  tag: ['name', 'type', 'address', 'initialValue', 'comment', 'retain'],
  block: ['id', 'name', 'number', 'language', 'cyclic', 'networks', 'source'],
  network: ['id', 'title', 'comment', 'branches', 'output'],
  instruction: ['id', 'kind', 'tag', 'parameter', 'auxiliary'],
  device: ['id', 'name', 'kind', 'model', 'ipAddress', 'x', 'y', 'modules'],
  link: ['id', 'from', 'to', 'subnet'],
  screen: ['id', 'name', 'width', 'height', 'objects'],
  object: ['id', 'kind', 'text', 'tag', 'x', 'y', 'width', 'height', 'color']
};
export function validateProject(p) {
  const diagnostics = [];
  const error = (code, message, location = '') => diagnostics.push({ severity: 'Error', code, message, location });
  const shape = (value, type) => {
    if (!value || typeof value !== 'object' || Array.isArray(value)) { error('CS003', `Invalid ${type}.`); return false; }
    if (Object.keys(value).some(key => !fields[type].includes(key))) error('CS006', `Unknown ${type} field.`);
    if (fields[type].some(key => !(key in value))) error('CS007', `Missing ${type} field.`);
    return true;
  };
  if (!shape(p, 'project')) return diagnostics;
  if (p.format !== FORMAT || p.version !== VERSION) error('CS001', 'Unsupported project format/version.');
  if (typeof p.name !== 'string' || !p.name.trim() || p.name.length > 128) error('CS002', 'Project name is required and limited to 128 characters.');
  if (!Number.isSafeInteger(p.revision) || p.revision < 0) error('CS008', 'Invalid project revision.');
  if (!['tags', 'blocks', 'devices', 'links', 'screens'].every(k => Array.isArray(p[k]))) { error('CS003', 'Project collections must be arrays.'); return diagnostics; }
  if (p.tags.length > 10000 || p.blocks.length > 1000 || p.devices.length > 2000 || p.screens.length > 1000 || p.links.length > 10000) { error('CS004', 'Project exceeds supported limits.'); return diagnostics; }
  const names = new Set(), memory = new Map(), ids = new Set();
  const id = value => { if (typeof value !== 'string' || !value || ids.has(value)) error('CS020', 'IDs must be nonempty and unique.', String(value)); else ids.add(value); };
  id(p.id);
  for (const tag of p.tags) {
    if (!shape(tag, 'tag')) continue;
    if (typeof tag.name !== 'string' || !/^[A-Za-z_][A-Za-z0-9_]{0,63}$/.test(tag.name) || names.has(tag.name.toLowerCase())) error('CS010', 'Invalid or duplicate tag name.', tag.name);
    names.add(String(tag.name).toLowerCase());
    if (!isValidValue(tag.type, tag.initialValue)) error('CS011', 'Initial value is not valid for the declared type.', tag.name);
    if (typeof tag.comment !== 'string' || typeof tag.retain !== 'boolean') error('CS009', 'Invalid tag comment or retain flag.', tag.name);
    const match = typeof tag.address === 'string' && /^%([IQM])([BWD]?)([0-9]{1,6})(?:\.([0-7]))?$/i.exec(tag.address);
    if (!match) { error('CS012', 'Invalid PLC address.', tag.name); continue; }
    const bit = match[4] !== undefined, width = match[2].toUpperCase();
    if (tag.type === 'Bool' ? !bit || width : bit || width !== (tag.type === 'Int' ? 'W' : 'D')) error('CS013', 'Address width does not match tag type.', tag.name);
    const start = Number(match[3]) * 8 + (bit ? Number(match[4]) : 0), bits = bit ? 1 : width === 'W' ? 16 : 32;
    for (let n = start; n < start + bits; n++) { const key = match[1].toUpperCase() + n; if (memory.has(key)) { error('CS014', `Address overlaps '${memory.get(key)}'.`, tag.name); break; } memory.set(key, tag.name); }
  }
  for (const b of p.blocks) {
    if (!shape(b, 'block')) continue; id(b.id);
    if (!Array.isArray(b.networks) || b.networks.length > 1000 || typeof b.source !== 'string' || b.source.length > 1000000) { error('CS022', 'Invalid or oversized block.', b.id); continue; }
    if (!['LAD', 'SCL'].includes(b.language) || typeof b.cyclic !== 'boolean' || !Number.isInteger(b.number) || typeof b.name !== 'string') error('CS023', 'Invalid block properties.', b.id);
    for (const n of b.networks) {
      if (!shape(n, 'network')) continue; id(n.id);
      if (!Array.isArray(n.branches) || n.branches.length < 1 || n.branches.length > 16 || typeof n.title !== 'string' || typeof n.comment !== 'string') { error('CS025', 'Invalid LAD network.', n.id); continue; }
      const instructions = [n.output];
      for (const path of n.branches) { if (!Array.isArray(path) || path.length < 1 || path.length > 64) error('CS026', 'Invalid or oversized path.', n.id); else instructions.push(...path); }
      for (const op of instructions) { if (!shape(op, 'instruction')) continue; id(op.id); if (!KINDS.includes(op.kind) || typeof op.tag !== 'string' || typeof op.auxiliary !== 'string' || !Number.isFinite(op.parameter)) error('CS027', 'Invalid instruction.', op.id); }
    }
  }
  const deviceIds = new Set(), ips = new Set();
  for (const d of p.devices) {
    if (!shape(d, 'device')) continue; id(d.id); deviceIds.add(d.id);
    const octets = typeof d.ipAddress === 'string' ? d.ipAddress.split('.') : [];
    if (octets.length !== 4 || !octets.every(n => /^\d{1,3}$/.test(n) && Number(n) <= 255)) error('CS031', 'An IPv4 address is required.', d.name);
    else { const normalized = octets.map(Number).join('.'); if (ips.has(normalized)) error('CS032', 'Duplicate IPv4 address.', d.name); ips.add(normalized); }
    if (!Number.isFinite(d.x) || !Number.isFinite(d.y) || !Array.isArray(d.modules) || d.modules.length > 32 || !d.modules.every(m => typeof m === 'string') || typeof d.name !== 'string' || typeof d.model !== 'string' || !['Controller', 'Hmi', 'RemoteIo', 'Switch'].includes(d.kind)) error('CS033', 'Invalid device.', d.id);
  }
  for (const l of p.links) { if (!shape(l, 'link')) continue; id(l.id); if (!deviceIds.has(l.from) || !deviceIds.has(l.to) || l.from === l.to || typeof l.subnet !== 'string') error('CS035', 'Invalid network link.', l.id); }
  for (const s of p.screens) {
    if (!shape(s, 'screen')) continue; id(s.id);
    if (!Number.isFinite(s.width) || !Number.isFinite(s.height) || s.width < 100 || s.width > 8192 || s.height < 100 || s.height > 8192 || !Array.isArray(s.objects) || s.objects.length > 10000 || typeof s.name !== 'string') { error('CS041', 'Invalid HMI screen.', s.id); continue; }
    for (const o of s.objects) {
      if (!shape(o, 'object')) continue; id(o.id);
      if (![o.x, o.y, o.width, o.height].every(Number.isFinite) || o.x < 0 || o.y < 0 || o.width <= 0 || o.height <= 0 || o.width > 8192 || o.height > 8192 || o.x + o.width > s.width || o.y + o.height > s.height || !HMI_KINDS.includes(o.kind) || typeof o.text !== 'string' || !/^#[0-9a-f]{6}$/i.test(o.color)) error('CS043', 'Invalid HMI object.', o.id);
      if (typeof o.tag !== 'string' || o.tag && !names.has(o.tag.toLowerCase())) error('CS045', 'HMI tag does not exist.', o.id);
    }
  }
  return diagnostics;
}
function rejectDuplicateKeys(source) {
  let pos = 0;
  const skip = () => { while (/\s/.test(source[pos] ?? '') && pos < source.length) pos++; };
  const string = () => { const start = pos++; while (pos < source.length) { if (source[pos] === '\\') pos += 2; else if (source[pos++] === '"') return JSON.parse(source.slice(start, pos)); } throw new Error('Unterminated JSON string.'); };
  const value = (depth = 0) => {
    if (depth > 64) throw new Error('JSON nesting exceeds 64 levels.'); skip();
    if (source[pos] === '{') {
      pos++; skip(); const keys = new Set(); if (source[pos] === '}') { pos++; return; }
      while (pos < source.length) {
        skip(); if (source[pos] !== '"') throw new Error('Expected JSON property.'); const key = string();
        if (keys.has(key)) throw new Error(`Duplicate JSON property: ${key}`); keys.add(key);
        skip(); if (source[pos++] !== ':') throw new Error('Expected colon.'); value(depth + 1); skip();
        if (source[pos] === '}') { pos++; return; } if (source[pos++] !== ',') throw new Error('Expected comma.');
      }
    } else if (source[pos] === '[') {
      pos++; skip(); if (source[pos] === ']') { pos++; return; }
      while (pos < source.length) { value(depth + 1); skip(); if (source[pos] === ']') { pos++; return; } if (source[pos++] !== ',') throw new Error('Expected comma.'); }
    } else if (source[pos] === '"') string();
    else { const match = /^(?:true|false|null|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/.exec(source.slice(pos)); if (!match) throw new Error('Invalid JSON value.'); pos += match[0].length; }
  };
  value(); skip(); if (pos !== source.length) throw new Error('Unexpected trailing JSON content.');
}
export function parseProject(source) {
  if (typeof source !== 'string' || new TextEncoder().encode(source).byteLength > 8 * 1024 * 1024) throw new Error('Project exceeds the 8 MiB import limit.');
  rejectDuplicateKeys(source); const project = JSON.parse(source); const errors = validateProject(project);
  if (errors.length) throw new Error(errors.slice(0, 20).map(d => `${d.code}: ${d.message} ${d.location}`).join('\n'));
  return project;
}
export const serializeProject = project => JSON.stringify(project, null, 2) + '\n';
export function tagsToCsv(tags) {
  const quote = value => `"${String(value).replaceAll('"', '""')}"`;
  return 'Name,DataType,Address,InitialValue,Comment,Retain\r\n' + tags.map(t => [t.name, t.type, t.address, t.initialValue, t.comment, t.retain].map(quote).join(',')).join('\r\n') + '\r\n';
}
export function tagsFromCsv(source) {
  if (new TextEncoder().encode(source).byteLength > 8 * 1024 * 1024) throw new Error('CSV exceeds import limit.');
  const rows = []; let row = [], cell = '', quoted = false, closed = false;
  for (let i = 0; i < source.length; i++) {
    const c = source[i];
    if (quoted) { if (c === '"') { if (source[i + 1] === '"') { cell += '"'; i++; } else { quoted = false; closed = true; } } else cell += c; }
    else if (c === ',' || c === '\r' || c === '\n') { row.push(cell); cell = ''; closed = false; if (c !== ',') { if (c === '\r' && source[i + 1] === '\n') i++; if (row.length !== 1 || row[0]) rows.push(row); row = []; } }
    else if (c === '"' && !cell && !closed) quoted = true;
    else if (c === '"' || closed) throw new Error('Malformed CSV quoting.');
    else cell += c;
  }
  if (quoted) throw new Error('Unterminated CSV field.');
  if (cell || row.length || closed) { row.push(cell); rows.push(row); }
  if (!rows.length || rows[0].join(',').toLowerCase() !== 'name,datatype,address,initialvalue,comment,retain') throw new Error('Invalid tag CSV header.');
  return rows.slice(1).map(r => { if (r.length !== 6 || !TYPES.includes(r[1]) || r[3].trim() === '' || !Number.isFinite(Number(r[3])) || !/^(true|false)$/i.test(r[5])) throw new Error('Invalid tag CSV row.'); return { name: r[0], type: r[1], address: r[2], initialValue: Number(r[3]), comment: r[4], retain: r[5].toLowerCase() === 'true' }; });
}
