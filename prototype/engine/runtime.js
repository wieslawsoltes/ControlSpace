import { isInput, isOutput, isValidValue } from './model.js';
import { evaluateExpression } from './languages.js';

export class RingBuffer {
  constructor(capacity = 2048) { if (!Number.isInteger(capacity) || capacity < 1 || capacity > 100000) throw new Error('Invalid buffer capacity.'); this.capacity = capacity; this.items = new Array(capacity); this.count = this.next = 0; }
  add(item) { this.items[this.next] = item; this.next = (this.next + 1) % this.capacity; this.count = Math.min(this.capacity, this.count + 1); }
  read() { const start = (this.next - this.count + this.capacity) % this.capacity; return Array.from({ length: this.count }, (_, i) => this.items[(start + i) % this.capacity]); }
  clear() { this.items.fill(undefined); this.count = this.next = 0; }
}
export class VirtualPlc {
  constructor(program) {
    if (!program) throw new Error('A successfully compiled program is required.');
    this.program = program; this.values = Float64Array.from(program.project.tags, t => t.initialValue); this.state = 'STOP'; this.fault = null;
    this.cycle = this.virtualMilliseconds = this.cpuMilliseconds = 0; this.inputs = new Map(); this.forces = new Map(); this.memory = new Map(); this.flow = new Map(); this.trace = new RingBuffer();
    program.project.tags.forEach((t, i) => { if (isInput(t)) this.inputs.set(i, this.values[i]); }); this.clearOutputs();
  }
  slot(name) { const slot = this.program.symbols.get(name.toLowerCase()); if (slot === undefined) throw new Error(`Unknown tag: ${name}`); return slot; }
  read(name) { return this.values[this.slot(name)]; }
  check(slot, value) { if (!isValidValue(this.program.project.tags[slot].type, value)) throw new Error(`Value for '${this.program.project.tags[slot].name}' is out of range or has the wrong type.`); }
  setInput(name, value) { const slot = this.slot(name); if (!isInput(this.program.project.tags[slot])) throw new Error('Only %I tags can be written as inputs.'); this.check(slot, value); this.inputs.set(slot, value); this.values[slot] = value; }
  force(name, value) { const slot = this.slot(name); this.check(slot, value); this.forces.set(slot, value); }
  release(name) { this.forces.delete(this.slot(name)); }
  releaseAll() { this.forces.clear(); }
  run() { if (this.state === 'FAULT') throw new Error('Reset the virtual controller before restarting after a fault.'); this.state = 'RUN'; }
  stop() { if (this.state !== 'FAULT') this.state = 'STOP'; this.forces.clear(); this.memory.clear(); this.flow.clear(); this.clearOutputs(); }
  clearOutputs() { this.program.project.tags.forEach((t, i) => { if (isOutput(t)) this.values[i] = 0; }); }
  reset(retain = false) { this.stop(); this.state = 'STOP'; this.fault = null; this.cycle = this.virtualMilliseconds = this.cpuMilliseconds = 0; this.trace.clear(); this.program.project.tags.forEach((t, i) => { if (!retain || !t.retain) this.values[i] = t.initialValue; if (isInput(t)) this.inputs.set(i, this.values[i]); }); this.clearOutputs(); }
  snapshot() { return { cycle: this.cycle, virtualMilliseconds: this.virtualMilliseconds, cpuMilliseconds: this.cpuMilliseconds, state: this.state, values: Array.from(this.values), flow: Object.fromEntries(this.flow), fault: this.fault }; }
  memoryFor(id) { if (!this.memory.has(id)) this.memory.set(id, { elapsed: 0, previous: false, active: false }); return this.memory.get(id); }
  contact(c, value) {
    const bit = Boolean(value);
    if (c.kind === 'RisingEdge' || c.kind === 'FallingEdge') { const m = this.memoryFor(c.id), result = c.kind === 'RisingEdge' ? bit && !m.previous : !bit && m.previous; m.previous = bit; return result; }
    switch (c.kind) { case 'Contact': return bit; case 'NegatedContact': return !bit; case 'Greater': return value > c.parameter; case 'Less': return value < c.parameter; case 'Equal': return value === c.parameter; default: throw new Error('Unsupported contact.'); }
  }
  output(o, power, period, values) {
    const m = this.memoryFor(o.id);
    switch (o.kind) {
      case 'Coil': values[o.slot] = Number(power); break;
      case 'SetCoil': if (power) values[o.slot] = 1; break;
      case 'ResetCoil': if (power) values[o.slot] = 0; break;
      case 'Move': if (power) values[o.slot] = o.parameter; break;
      case 'TimerOn': m.elapsed = power ? Math.min(o.parameter, m.elapsed + period) : 0; values[o.slot] = Number(power && m.elapsed >= o.parameter); if (o.auxiliarySlot >= 0) values[o.auxiliarySlot] = m.elapsed; break;
      case 'TimerOff': if (power) { m.elapsed = 0; m.active = true; } else if (m.active) { m.elapsed = Math.min(o.parameter, m.elapsed + period); if (m.elapsed >= o.parameter) m.active = false; } values[o.slot] = Number(m.active); if (o.auxiliarySlot >= 0) values[o.auxiliarySlot] = m.elapsed; break;
      case 'Pulse':
        if (power && !m.previous && !m.active) { m.active = true; m.elapsed = 0; }
        values[o.slot] = Number(m.active && m.elapsed < o.parameter);
        if (m.active) { m.elapsed = Math.min(o.parameter, m.elapsed + period); if (m.elapsed >= o.parameter) m.active = false; }
        if (o.auxiliarySlot >= 0) values[o.auxiliarySlot] = m.elapsed; break;
      case 'CountUp': if (o.auxiliarySlot >= 0 && values[o.auxiliarySlot]) values[o.slot] = 0; else if (power && !m.previous) values[o.slot] = Math.min(this.program.project.tags[o.slot].type === 'Int' ? 32767 : 2147483647, values[o.slot] + 1); break;
      default: throw new Error('Unsupported output instruction.');
    }
    m.previous = power;
  }
  statements(statements, values) { for (const s of statements) { if (s.kind === 'assign') { const value = evaluateExpression(s.value, values); this.check(s.slot, value); values[s.slot] = value; } else this.statements(evaluateExpression(s.condition, values) ? s.then : s.otherwise, values); } }
  step(period = 100, singleStep = false) {
    if (!Number.isInteger(period) || period <= 0 || period > 1000) throw new Error('Scan period must be an integer in 1..1000 ms.');
    if (this.state === 'FAULT' || this.state !== 'RUN' && !singleStep) return false;
    const start = performance.now(), working = this.values.slice(), flow = new Map();
    for (const [slot, value] of this.inputs) working[slot] = value; for (const [slot, value] of this.forces) working[slot] = value;
    try {
      for (const block of this.program.blocks) {
        for (const n of block.networks) {
          let power = false;
          for (const path of n.branches) { let branch = true; for (const contact of path) { const active = this.contact(contact, working[contact.slot]); branch = branch && active; flow.set(contact.id, branch); } power = power || branch; }
          flow.set(n.id, power); this.output(n.output, power, period, working); flow.set(n.output.id, Boolean(working[n.output.slot]));
        }
        this.statements(block.statements, working);
      }
      for (const [slot, value] of this.forces) working[slot] = value;
      working.forEach((value, slot) => this.check(slot, value));
      this.values = working; this.flow = flow; this.cycle++; this.virtualMilliseconds += period; this.cpuMilliseconds = performance.now() - start; this.trace.add(this.snapshot()); return true;
    } catch (e) { this.state = 'FAULT'; this.fault = e.message; this.forces.clear(); this.memory.clear(); this.flow.clear(); this.clearOutputs(); this.cpuMilliseconds = performance.now() - start; return false; }
  }
}
