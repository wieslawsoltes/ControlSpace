import { clone, isInput, isValidValue, validateProject } from './model.js';
export class SclError extends Error {
  constructor(message, line = 1, column = 1) { super(message); this.line = line; this.column = column; }
}
export function lexScl(source) {
  if (source.length > 1000000) throw new SclError('Source exceeds 1 MB.');
  const tokens = []; let index = 0, line = 1, column = 1;
  const advance = () => { if (source[index++] === '\n') { line++; column = 1; } else column++; };
  while (index < source.length) {
    if (tokens.length >= 100000) throw new SclError('Token limit exceeded.', line, column);
    if (/\s/.test(source[index])) { advance(); continue; }
    if (source.slice(index, index + 2) === '//') { while (index < source.length && source[index] !== '\n') advance(); continue; }
    if (source.slice(index, index + 2) === '(*') { advance(); advance(); while (index + 1 < source.length && source.slice(index, index + 2) !== '*)') advance(); if (index + 1 >= source.length) throw new SclError('Unterminated comment.', line, column); advance(); advance(); continue; }
    const start = index, sl = line, sc = column;
    if (source[index] === '"') { advance(); const ns = index; while (index < source.length && source[index] !== '"') advance(); if (index >= source.length) throw new SclError('Unterminated quoted tag name.', sl, sc); const text = source.slice(ns, index); advance(); tokens.push({ text, line: sl, column: sc, quoted: true, start, end: index }); continue; }
    if (/[A-Za-z_]/.test(source[index])) { while (index < source.length && /[A-Za-z0-9_]/.test(source[index])) advance(); }
    else if (/\d/.test(source[index]) || source[index] === '.' && /\d/.test(source[index + 1] ?? '')) {
      while (index < source.length && /[\d.]/.test(source[index])) advance();
      if (/[eE]/.test(source[index] ?? '')) { advance(); if (/[+-]/.test(source[index] ?? '')) advance(); while (index < source.length && /\d/.test(source[index])) advance(); }
    } else { advance(); if ([':=', '<=', '>=', '<>'].includes(source.slice(start, index + 1))) advance(); else if (!'()+-*/=<>;'.includes(source[start])) throw new SclError(`Unsupported character '${source[start]}'.`, sl, sc); }
    tokens.push({ text: source.slice(start, index), line: sl, column: sc, quoted: false, start, end: index });
  }
  tokens.push({ text: '<EOF>', line, column, start: index, end: index }); return tokens;
}
export function parseScl(source, symbols, tags) {
  const tokens = lexScl(source); let position = 0, depth = 0;
  const peek = () => tokens[position];
  const is = text => !peek().quoted && peek().text.toUpperCase() === text;
  const take = text => { if (is(text)) { position++; return true; } return false; };
  const fail = message => { throw new SclError(message, peek().line, peek().column); };
  const need = text => { if (!take(text)) fail(`Expected '${text}'.`); };
  const precedence = op => ({ OR: 1, XOR: 2, AND: 3, '=': 4, '<>': 4, '>': 4, '<': 4, '>=': 4, '<=': 4, '+': 5, '-': 5, '*': 6, '/': 6, MOD: 6 })[op.toUpperCase()] ?? 0;
  const expr = (minimum = 1) => {
    if (++depth > 64) fail('Expression nesting exceeds 64 levels.'); let left;
    if (take('NOT')) { const operand = expr(7); if (operand.type !== 'Bool') fail('NOT requires BOOL.'); left = { kind: 'unary', op: 'NOT', operand, type: 'Bool' }; }
    else if (is('-') || is('+')) { const op = tokens[position++].text, operand = expr(7); if (operand.type === 'Bool') fail('Unary arithmetic requires a number.'); left = { kind: 'unary', op, operand, type: 'Real' }; }
    else if (take('(')) { left = expr(); need(')'); }
    else if (take('TRUE')) left = { kind: 'literal', value: 1, type: 'Bool' };
    else if (take('FALSE')) left = { kind: 'literal', value: 0, type: 'Bool' };
    else {
      if (is('<EOF>')) fail('Expected expression.'); const token = tokens[position++];
      if (!token.quoted && /^(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/.test(token.text) && Number.isFinite(Number(token.text))) left = { kind: 'literal', value: Number(token.text), type: 'Real' };
      else if (symbols.has(token.text.toLowerCase())) { const slot = symbols.get(token.text.toLowerCase()); left = { kind: 'tag', slot, type: tags[slot].type }; }
      else throw new SclError(`Unknown tag or literal '${token.text}'.`, token.line, token.column);
    }
    while (!peek().quoted && precedence(peek().text) >= minimum) {
      const op = tokens[position++].text.toUpperCase(), right = expr(precedence(op) + 1);
      const boolean = ['AND', 'OR', 'XOR'].includes(op), comparison = ['=', '<>', '>', '<', '>=', '<='].includes(op);
      if (boolean ? left.type !== 'Bool' || right.type !== 'Bool' : comparison ? (left.type === 'Bool') !== (right.type === 'Bool') : left.type === 'Bool' || right.type === 'Bool') fail('Incompatible expression types.');
      left = { kind: 'binary', op, left, right, type: boolean || comparison ? 'Bool' : 'Real' };
    }
    depth--; return left;
  };
  const statements = () => {
    if (++depth > 64) fail('Nesting exceeds 64 levels.'); const result = [];
    while (!is('<EOF>') && !is('ELSE') && !is('END_IF')) {
      if (take(';')) continue;
      if (take('IF')) { const condition = expr(); if (condition.type !== 'Bool') fail('IF requires BOOL.'); need('THEN'); const then = statements(), otherwise = take('ELSE') ? statements() : []; need('END_IF'); need(';'); result.push({ kind: 'if', condition, then, otherwise }); }
      else {
        const token = tokens[position++], slot = symbols.get(token.text.toLowerCase());
        if (slot === undefined) throw new SclError(`Unknown tag '${token.text}' or unsupported statement.`, token.line, token.column);
        if (isInput(tags[slot])) throw new SclError('Program cannot assign to an input image tag.', token.line, token.column);
        need(':='); const value = expr(); need(';');
        if ((tags[slot].type === 'Bool') !== (value.type === 'Bool')) throw new SclError('BOOL and numeric assignments cannot be mixed.', token.line, token.column);
        result.push({ kind: 'assign', slot, value });
      }
    }
    depth--; return result;
  };
  const result = statements(); if (!is('<EOF>')) fail(`Unexpected token '${peek().text}'.`); return result;
}
export function evaluateExpression(node, values) {
  if (node.kind === 'literal') return node.value;
  if (node.kind === 'tag') return values[node.slot];
  if (node.kind === 'unary') { const v = evaluateExpression(node.operand, values); return node.op === 'NOT' ? Number(!v) : node.op === '-' ? -v : v; }
  const a = evaluateExpression(node.left, values), b = evaluateExpression(node.right, values);
  switch (node.op) {
    case '+': return a + b; case '-': return a - b; case '*': return a * b;
    case '/': if (b === 0) throw new Error('Division by zero.'); return a / b;
    case 'MOD': if (b === 0) throw new Error('Modulo by zero.'); return a % b;
    case 'AND': return Number(Boolean(a) && Boolean(b)); case 'OR': return Number(Boolean(a) || Boolean(b)); case 'XOR': return Number(Boolean(a) !== Boolean(b));
    case '=': return Number(a === b); case '<>': return Number(a !== b); case '>': return Number(a > b); case '<': return Number(a < b); case '>=': return Number(a >= b); case '<=': return Number(a <= b);
    default: throw new Error('Unknown operator.');
  }
}
export function compileProject(input) {
  const project = clone(input), diagnostics = validateProject(project);
  if (diagnostics.length) return { success: false, program: null, diagnostics };
  const symbols = new Map(project.tags.map((t, slot) => [t.name.toLowerCase(), slot])), blocks = [], writes = new Map();
  const error = (message, location) => diagnostics.push({ severity: 'Error', code: 'CS100', message, location });
  const slot = (name, location) => { const value = symbols.get(name.toLowerCase()); if (value === undefined) { error(`Unknown tag '${name}'.`, location); return -1; } return value; };
  for (const block of project.blocks) {
    const networks = []; let statements = [];
    if (block.language === 'SCL') {
      try { statements = parseScl(block.source, symbols, project.tags); }
      catch (e) { if (!(e instanceof SclError)) throw e; diagnostics.push({ severity: 'Error', code: 'CS110', message: e.message, location: block.id, line: e.line, column: e.column }); }
    } else for (const network of block.networks) {
      const branches = network.branches.map(path => path.map(op => {
        const index = slot(op.tag, op.id), comparison = ['Greater', 'Less', 'Equal'].includes(op.kind), contact = ['Contact', 'NegatedContact', 'RisingEdge', 'FallingEdge'].includes(op.kind);
        if (!comparison && !contact) error('Only contacts, edges and comparisons are supported in a path.', op.id);
        if (index >= 0 && (contact ? project.tags[index].type !== 'Bool' : project.tags[index].type === 'Bool')) error('Instruction operand type does not match tag type.', op.id);
        return { ...op, slot: index };
      }));
      const o = network.output, target = slot(o.tag, o.id), aux = o.auxiliary ? slot(o.auxiliary, o.id) : -1;
      const timer = ['TimerOn', 'TimerOff', 'Pulse'].includes(o.kind), bit = ['Coil', 'SetCoil', 'ResetCoil'].includes(o.kind) || timer, number = ['CountUp', 'Move'].includes(o.kind);
      if (!bit && !number) error('Unsupported network output instruction.', o.id);
      if (target >= 0) {
        if (isInput(project.tags[target])) error('Program cannot write to input image tags.', o.id);
        if (bit !== (project.tags[target].type === 'Bool')) error('Output tag has the wrong type.', o.id);
        if (o.kind === 'CountUp' && !['Int', 'DInt'].includes(project.tags[target].type)) error('Counter output must be INT or DINT.', o.id);
        if (block.cyclic && writes.has(o.tag.toLowerCase())) diagnostics.push({ severity: 'Warning', code: 'CS120', message: `Multiple network writers for '${o.tag}'; scan order is significant.`, location: o.id });
        if (block.cyclic) writes.set(o.tag.toLowerCase(), o.id);
        if (o.kind === 'Move' && !isValidValue(project.tags[target].type, o.parameter)) error('MOVE value cannot be represented by the target type.', o.id);
      }
      if (timer && (!Number.isInteger(o.parameter) || o.parameter < 0 || o.parameter > 2147483647)) error('Timer preset must be an integer in 0..2147483647 ms.', o.id);
      if (aux >= 0 && timer && (project.tags[aux].type !== 'Time' || isInput(project.tags[aux]))) error('Timer elapsed tag must be a writable TIME tag.', o.id);
      if (aux >= 0 && o.kind === 'CountUp' && project.tags[aux].type !== 'Bool') error('Counter reset tag must be BOOL.', o.id);
      networks.push({ id: network.id, branches, output: { ...o, slot: target, auxiliarySlot: aux } });
    }
    if (block.cyclic) blocks.push({ id: block.id, networks, statements });
  }
  if (!blocks.length) diagnostics.push({ severity: 'Warning', code: 'CS121', message: 'No cyclic blocks are enabled.', location: '' });
  const success = !diagnostics.some(d => d.severity === 'Error');
  if (success) diagnostics.push({ severity: 'Info', code: 'CS000', message: `Compiling finished: ${project.blocks.length} blocks, ${project.tags.length} symbols, ${blocks.length} cyclic blocks.`, location: project.name });
  return { success, diagnostics, program: success ? { project, symbols, blocks } : null };
}
export function renameSclTag(source, before, after) {
  const keywords = new Set(['IF', 'THEN', 'ELSE', 'END_IF', 'TRUE', 'FALSE', 'AND', 'OR', 'XOR', 'NOT', 'MOD']);
  const matches = lexScl(source).filter(t => (t.quoted || !keywords.has(t.text.toUpperCase())) && t.text.toLowerCase() === before.toLowerCase());
  for (const token of matches.reverse()) source = source.slice(0, token.start) + (token.quoted || keywords.has(after.toUpperCase()) ? `"${after}"` : after) + source.slice(token.end);
  return source;
}
