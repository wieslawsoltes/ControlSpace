import { clone, createDemo, parseProject, serializeProject, validateProject } from './model.js';
import { compileProject, renameSclTag } from './languages.js';
import { VirtualPlc } from './runtime.js';

export class Workspace {
  constructor(project = createDemo(), historyLimit = 80) {
    if (!Number.isInteger(historyLimit) || historyLimit < 1 || historyLimit > 1000) throw new Error('Invalid history limit.');
    this.project = parseProject(serializeProject(project)); this.historyLimit = historyLimit; this.undoStack = []; this.redoStack = []; this.listeners = new Set(); this.controller = null; this.compilation = null; this.saved = serializeProject(this.project);
  }
  get dirty() { return this.saved !== serializeProject(this.project); }
  subscribe(listener) { this.listeners.add(listener); return () => this.listeners.delete(listener); }
  notify() { for (const listener of this.listeners) listener(); }
  invalidate() { this.controller?.stop(); this.controller = this.compilation = null; this.notify(); }
  edit(description, change) {
    if (this.controller?.state === 'RUN') throw new Error('Stop simulation before changing the project.');
    const before = clone(this.project), next = clone(before); change(next);
    if (serializeProject(next) === serializeProject(before)) return false;
    next.revision++; const errors = validateProject(next); if (errors.length) throw new Error(errors.slice(0, 10).map(d => d.message).join('\n'));
    this.project = next; this.undoStack.push({ description, before, after: clone(next) }); this.redoStack = [];
    if (this.undoStack.length > this.historyLimit) this.undoStack.shift();
    // Bound snapshot memory as well as history count.
    let bytes = 0; for (let i = this.undoStack.length - 1; i >= 0; i--) { bytes += serializeProject(this.undoStack[i].before).length * 2; if (bytes > 32 * 1024 * 1024) { this.undoStack.splice(0, i + 1); break; } }
    this.invalidate(); return true;
  }
  undo() { if (!this.undoStack.length) return false; const item = this.undoStack.pop(); this.redoStack.push(item); this.project = clone(item.before); this.invalidate(); return true; }
  redo() { if (!this.redoStack.length) return false; const item = this.redoStack.pop(); this.undoStack.push(item); this.project = clone(item.after); this.invalidate(); return true; }
  load(project) { const next = parseProject(serializeProject(project)); this.controller?.stop(); this.project = next; this.undoStack = []; this.redoStack = []; this.saved = serializeProject(next); this.invalidate(); }
  markSaved() { this.saved = serializeProject(this.project); this.notify(); }
  compile() { this.controller?.stop(); this.compilation = compileProject(this.project); this.controller = this.compilation.success ? new VirtualPlc(this.compilation.program) : null; this.notify(); return this.compilation; }
  renameTag(before, after, update = null) {
    this.edit('Rename tag', p => {
      const tag = p.tags.find(t => t.name.toLowerCase() === before.toLowerCase()); if (!tag) throw new Error('Tag does not exist.'); tag.name = after;
      for (const b of p.blocks) { b.source = renameSclTag(b.source, before, after); for (const n of b.networks) for (const op of [...n.branches.flat(), n.output]) { if (op.tag.toLowerCase() === before.toLowerCase()) op.tag = after; if (op.auxiliary.toLowerCase() === before.toLowerCase()) op.auxiliary = after; } }
      for (const s of p.screens) for (const o of s.objects) if (o.tag.toLowerCase() === before.toLowerCase()) o.tag = after;
      if (update) update(tag);
    });
  }
  crossReferences() {
    const references = [];
    for (const b of this.project.blocks) {
      for (const n of b.networks) {
        for (const op of n.branches.flat()) references.push({ tag: op.tag, access: 'Read', block: b.name, network: n.title, id: op.id });
        references.push({ tag: n.output.tag, access: 'Write', block: b.name, network: n.title, id: n.output.id });
        if (n.output.auxiliary) references.push({ tag: n.output.auxiliary, access: n.output.kind === 'CountUp' ? 'Read' : 'Write', block: b.name, network: n.title, id: n.output.id });
      }
    }
    return references;
  }
}
