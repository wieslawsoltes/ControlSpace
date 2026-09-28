import { createDemo, clone, uid, instruction, TYPES, KINDS, HMI_KINDS, parseProject, serializeProject, tagsToCsv, tagsFromCsv, formatValue, isInput } from '../engine/model.js';
import { Workspace } from '../engine/workspace.js';
import { GraphicsSurface, ladderScene, devicesScene, hmiScene, traceScene } from './renderer.js';

const $ = selector => document.querySelector(selector);
const esc = value => String(value ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
const recoveryKey = 'controlspace.prototype.project.v1';
let recovered = null, recoveryError = '';
try { const text = localStorage.getItem(recoveryKey); if (text) recovered = parseProject(text); } catch (error) { recoveryError = `Recovery was not loaded: ${error.message}`; }
const workspace = new Workspace(recovered ?? createDemo());
let view = 'block:main', selected = '', task = 'instructions', bottom = 'messages', zoom = 1, scroll = 0, preview = false, monitoring = true, scene = null, drag = null, momentary = null, sourceDirty = false, frameQueued = false, refreshQueued = false, toastTimer, saveTimer, buildMessages = [{ severity: 'Info', code: 'CS-DEMO', message: 'Conveyor demo loaded. Compile, start simulation, then toggle Start_PB in Simulation inputs.', location: 'Project' }];
const tabs = ['block:main', 'block:speed', 'tags', 'devices', 'hmi'];
const titles = { portal: 'Start', tags: 'Default tag table', watch: 'Watch table_1', devices: 'Devices & networks', hmi: 'Overview [HMI_1]', trace: 'Trace_1', diagnostics: 'Online & diagnostics', references: 'Cross-references', library: 'Project library', blocks: 'Program blocks' };
const icons = { portal: '◧', tags: '▦', watch: '▦', devices: '⌘', hmi: '▣', trace: '⌁', diagnostics: '◉', references: '⇄', library: '▤', blocks: '▧' };
function blockNow() { return workspace.project.blocks.find(b => b.id === view.split(':')[1]); }
function titleOf(id) { if (!id.startsWith('block:')) return titles[id] ?? id; const b = workspace.project.blocks.find(b => b.id === id.slice(6)); return b ? `${b.name} [${b.id === 'main' ? 'OB' : 'FC'}${b.number}]` : 'Program block'; }
function displayStatus(message, error = false) { $('#status-message').textContent = message; $('#toast').textContent = message; $('#toast').className = error ? 'error' : ''; $('#toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => { $('#toast').hidden = true; }, error ? 8000 : 3800); }
function guard(action) { try { return action(); } catch (error) { displayStatus(error.message, true); return false; } }
function safeAsync(action) { Promise.resolve().then(action).catch(error => displayStatus(error.message, true)); }
function modal(title, html) { $('#modal-title').textContent = title; $('#modal-body').innerHTML = html; $('#modal').showModal(); }
function download(name, text, mime = 'application/json') { const url = URL.createObjectURL(new Blob([text], { type: mime })); const anchor = document.createElement('a'); anchor.href = url; anchor.download = name; document.body.append(anchor); anchor.click(); anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 10000); }
function scheduleDraw() { if (frameQueued) return; frameQueued = true; requestAnimationFrame(() => { frameQueued = false; draw(); }); }
function scheduleRefresh() { if (refreshQueued) return; refreshQueued = true; queueMicrotask(() => { refreshQueued = false; refresh(); }); }
const graphics = new GraphicsSurface($('#geometry'), $('#labels'), backend => { $('#backend').textContent = `${backend} · 2D text`; scheduleDraw(); });
function draw() {
  const viewport = $('#viewport'); if (viewport.hidden) return;
  const { width, height } = viewport.getBoundingClientRect(); if (width < 1 || height < 1) return;
  const p = workspace.project, runtime = monitoring ? workspace.controller : null;
  if (view.startsWith('block:') && blockNow()?.language === 'LAD') scene = ladderScene(width, height, p, blockNow(), runtime, selected, zoom, scroll);
  else if (view === 'devices') scene = devicesScene(width, height, p, selected, drag);
  else if (view === 'hmi') scene = hmiScene(width, height, p.screens[0], p, runtime, selected, preview, drag);
  else if (view === 'trace') scene = traceScene(width, height, workspace.controller);
  else return;
  graphics.render(scene);
  const thumb = $('#scroll-thumb'); const content = (scene.contentHeight ?? 0) * zoom; thumb.hidden = content <= height;
  if (content > height) { const th = Math.max(25, height * height / content); thumb.style.height = `${th}px`; thumb.style.top = `${(height - th) * scroll / Math.max(1, scene.contentHeight - height / zoom)}px`; }
}
function commitSource() {
  const source = $('#scl-source'); if (!source || !sourceDirty) return;
  const text = source.value, id = blockNow().id; workspace.edit('Edit SCL source', p => { p.blocks.find(b => b.id === id).source = text; }); sourceDirty = false;
}
function navigate(next) {
  guard(() => {
    commitSource(); view = next === 'blocks' ? 'block:main' : next; selected = ''; scroll = 0; preview = false;
    if (!tabs.includes(view) && !['portal', 'library', 'diagnostics', 'references'].includes(view)) tabs.push(view);
    refresh();
  });
}
function edit(label, change) { commitSource(); return workspace.edit(label, change); }
function ensureRuntime() { commitSource(); if (!workspace.controller) { const result = workspace.compile(); buildMessages = result.diagnostics; if (!result.success) { bottom = 'messages'; renderOutput(); throw new Error('Compilation failed. Correct the diagnostics before simulating.'); } } return workspace.controller; }
function compile() { commitSource(); const result = workspace.compile(); buildMessages = result.diagnostics; bottom = 'messages'; renderOutput(); displayStatus(result.success ? 'Compile completed: no errors. Ready for simulation.' : `Compilation failed: ${result.diagnostics.filter(d => d.severity === 'Error').length} errors.`, !result.success); }
function uniqueTagName(prefix = 'Tag') { let n = 1; while (workspace.project.tags.some(t => t.name.toLowerCase() === `${prefix}_${n}`.toLowerCase())) n++; return `${prefix}_${n}`; }
function addTag() {
  const name = uniqueTagName(); edit('Add PLC tag', p => { let byte = 100; const used = new Set(p.tags.map(t => t.address.toUpperCase())); while (used.has(`%M${byte}.0`) || p.tags.some(t => /^%M[DBW]/i.test(t.address) && Math.abs(Number(t.address.slice(3)) - byte) < 4)) byte++; p.tags.push({ name, type: 'Bool', address: `%M${byte}.0`, initialValue: 0, comment: '', retain: false }); });
  view = 'tags'; selected = `tag:${name}`; task = 'properties'; refresh();
}
function selectedNetwork() { const b = blockNow(); return b?.networks.find(n => n.id === selected || n.output.id === selected || n.branches.flat().some(i => i.id === selected)) ?? b?.networks.at(-1); }
function addNetwork() {
  if (!blockNow() || blockNow().language !== 'LAD') navigate('block:main'); const id = uid('network'), bid = blockNow().id;
  edit('Add ladder network', p => p.blocks.find(b => b.id === bid).networks.push({ id, title: 'New network', comment: '', branches: [[instruction('Contact', 'Start_PB')]], output: instruction('Coil', 'Ready_Lamp') })); selected = id; task = 'properties'; scroll = Math.max(0, blockNow().networks.length * 195 - $('#viewport').clientHeight); refresh();
}
function addInstruction(kind) {
  if (!view.startsWith('block:') || blockNow()?.language !== 'LAD') navigate('block:main'); const n = selectedNetwork(); if (!n) return addNetwork();
  const numeric = ['Greater', 'Less', 'Equal', 'Move', 'CountUp'].includes(kind), output = KINDS.indexOf(kind) >= KINDS.indexOf('Coil');
  const name = kind === 'CountUp' ? 'Part_Count' : numeric ? 'Speed_Setpoint' : output ? 'Ready_Lamp' : 'Start_PB';
  const op = instruction(kind, name, ['TimerOn', 'TimerOff', 'Pulse'].includes(kind) ? 1000 : 0, kind === 'CountUp' ? 'Reset_Count' : ['TimerOn', 'TimerOff', 'Pulse'].includes(kind) ? 'Delay_ET' : '');
  const bid = blockNow().id; edit(`Insert ${kind}`, p => { const net = p.blocks.find(b => b.id === bid).networks.find(net => net.id === n.id); if (output) net.output = op; else net.branches[0].push(op); }); selected = op.id; task = 'properties'; refresh();
}
function addHmi(kind) {
  if (view !== 'hmi') navigate('hmi'); if (!workspace.project.screens[0]) throw new Error('This project has no HMI screen. Open a project with a screen before inserting objects.'); const id = uid('hmi'), count = workspace.project.screens[0].objects.length;
  edit(`Add HMI ${kind}`, p => p.screens[0].objects.push({ id, kind, text: kind === 'Label' ? 'New label' : kind.toUpperCase(), tag: kind === 'Label' ? '' : kind === 'Button' ? 'Start_PB' : kind === 'Lamp' ? 'Motor_Run' : 'Speed_Actual', x: 50 + count % 5 * 40, y: 120 + count % 4 * 40, width: kind === 'Label' ? 250 : 180, height: kind === 'Label' ? 45 : 85, color: '#008C95' })); selected = id; task = 'properties'; refresh();
}
function addDevice() {
  const id = uid('device'); edit('Add remote I/O', p => { const names = new Set(p.devices.map(d => d.name)); let suffix = 1; while (names.has(`IO_${suffix}`)) suffix++; const used = new Set(p.devices.map(d => d.ipAddress)); let host = 10; while (used.has(`192.168.0.${host}`)) host++; if (host > 254) throw new Error('No free address in the demo subnet.'); p.devices.push({ id, name: `IO_${suffix}`, kind: 'RemoteIo', model: 'Generic remote I/O · simulation', ipAddress: `192.168.0.${host}`, x: 50 + (p.devices.length % 3) * 325, y: 100 + Math.floor(p.devices.length / 3) * 280, modules: ['IM', 'DI 8'] }); }); view = 'devices'; selected = id; task = 'properties'; refresh();
}
const actions = {
  new() { commitSource(); if (workspace.dirty && !confirm('Discard unsaved project edits and load a new conveyor demo?')) return; workspace.load(createDemo()); view = 'block:main'; selected = ''; preview = false; sourceDirty = false; buildMessages = []; refresh(); displayStatus('New conveyor demo created.'); },
  open() { $('#file-input').value = ''; $('#file-input').click(); },
  save() { commitSource(); download(`${workspace.project.name}.controlspace.json`, serializeProject(workspace.project)); workspace.markSaved(); displayStatus('Project exported as ControlSpace JSON.'); },
  undo() { commitSource(); workspace.undo(); selected = ''; sourceDirty = false; }, redo() { commitSource(); workspace.redo(); selected = ''; sourceDirty = false; }, compile,
  run() { const runtime = ensureRuntime(); runtime.run(); $('#scl-source') && ($('#scl-source').disabled = true); bottom = 'inputs'; renderOutput(); updateRuntime(); displayStatus('Virtual PLC is running. Toggle Start_PB in Simulation inputs.'); },
  stop() { workspace.controller?.stop(); if ($('#scl-source')) $('#scl-source').disabled = false; updateRuntime(); displayStatus('Simulation stopped. Outputs and forces cleared.'); },
  step() { const runtime = ensureRuntime(); runtime.step(100, true); updateRuntime(); },
  reset() { ensureRuntime().reset(); updateRuntime(); displayStatus('Virtual controller reset.'); },
  monitor() { monitoring = !monitoring; updateRuntime(); scheduleDraw(); displayStatus(monitoring ? 'Live power-flow monitoring enabled.' : 'Power-flow monitoring hidden.'); },
  'add-network': addNetwork, 'add-tag': addTag, 'add-device': addDevice,
  'add-branch'() { const n = selectedNetwork(); if (!n) throw new Error('Select a ladder network.'); const bid = blockNow().id; edit('Insert parallel branch', p => p.blocks.find(b => b.id === bid).networks.find(net => net.id === n.id).branches.push([instruction('Contact', 'Start_PB')])); },
  'apply-source'() { commitSource(); displayStatus('Source applied to the project. Compile to validate.'); },
  'runtime-preview'() { ensureRuntime(); preview = !preview; task = 'instructions'; refresh(); displayStatus(preview ? 'HMI runtime: START and STOP are momentary virtual inputs.' : 'HMI design mode. Drag objects to reposition.'); },
  'zoom-in'() { zoom = Math.min(2, zoom + .1); scheduleDraw(); updateRuntime(); }, 'zoom-out'() { zoom = Math.max(.5, zoom - .1); scheduleDraw(); updateRuntime(); }, fit() { zoom = 1; scroll = 0; scheduleDraw(); updateRuntime(); },
  'export-tags'() { download(`${workspace.project.name}.tags.csv`, tagsToCsv(workspace.project.tags), 'text/csv'); displayStatus('Tag table exported.'); },
  'export-trace'() { const runtime = ensureRuntime(), header = ['VirtualMilliseconds', ...runtime.program.project.tags.map(t => t.name)].join(','); download(`${workspace.project.name}.trace.csv`, header + '\r\n' + runtime.trace.read().map(s => [s.virtualMilliseconds, ...s.values].join(',')).join('\r\n'), 'text/csv'); },
  'release-forces'() { workspace.controller?.releaseAll(); updateRuntime(); displayStatus('All virtual forces released.'); },
  delete() {
    if (!selected) throw new Error('Select an instruction, device, HMI object or tag first.');
    edit('Delete selection', p => {
      if (selected.startsWith('tag:')) { const name = selected.slice(4); if (workspace.crossReferences().some(r => r.tag.toLowerCase() === name.toLowerCase()) || p.screens.some(s => s.objects.some(o => o.tag.toLowerCase() === name.toLowerCase())) || p.blocks.some(b => b.language === 'SCL' && b.source.toLowerCase().includes(name.toLowerCase()))) throw new Error('This tag is referenced. Remove its references before deleting it.'); p.tags = p.tags.filter(t => t.name !== name); }
      else if (view === 'hmi') p.screens[0].objects = p.screens[0].objects.filter(o => o.id !== selected);
      else if (view === 'devices') { p.devices = p.devices.filter(d => d.id !== selected); p.links = p.links.filter(l => l.from !== selected && l.to !== selected); }
      else { const b = p.blocks.find(b => b.id === blockNow()?.id); if (!b) throw new Error('No editable selection.'); if (b.networks.some(n => n.output.id === selected)) throw new Error('Replace the output instruction or delete its network.'); b.networks = b.networks.filter(n => n.id !== selected); for (const n of b.networks) { n.branches = n.branches.map(path => path.filter(op => op.id !== selected)).filter(path => path.length); if (!n.branches.length) throw new Error('A network must contain at least one nonempty branch.'); } }
    }); selected = ''; refresh();
  },
  duplicate() { const n = selectedNetwork(); if (!n) throw new Error('Select a ladder network to duplicate.'); const copy = clone(n); copy.id = uid('net'); copy.title += ' (copy)'; copy.output.id = uid('op'); copy.branches.flat().forEach(op => op.id = uid('op')); const bid = blockNow().id; edit('Duplicate network', p => p.blocks.find(b => b.id === bid).networks.push(copy)); selected = copy.id; refresh(); },
  'link-device'() { if (view !== 'devices' || !selected) throw new Error('Select a device first.'); edit('Connect device to subnet', p => { const from = p.devices.find(d => d.kind === 'Controller' && d.id !== selected)?.id ?? p.devices.find(d => d.id !== selected)?.id; if (!from) throw new Error('Add another device first.'); if (p.links.some(l => (l.from === from && l.to === selected) || (l.to === from && l.from === selected))) throw new Error('These devices are already linked.'); p.links.push({ id: uid('link'), from, to: selected, subnet: 'PN/IE_1' }); }); },
  'add-module'() { edit('Add I/O module', p => { const device = p.devices.find(d => d.id === selected); if (!device) throw new Error('Select a device first.'); device.modules.push('DI 8×24 V'); }); },
  about() { modal('About ControlSpace', '<h2>ControlSpace</h2><p>An independent, TIA Portal-style engineering workspace. Original implementation, original graphics, and a deterministic virtual PLC.</p><p><b>This executable preview is JavaScript, not Uno.</b> The repository source includes a separate .NET 10 / Uno Platform 6.7 application with eight reusable libraries. Its desktop and WebAssembly builds have not been compiled in the delivery environment.</p><p>The preview uses WebGPU for geometry when available, with Canvas 2D text and a Canvas 2D fallback. It does not connect to real controllers and is not suitable for commissioning or safety applications.</p><p>Not affiliated with Siemens. Native TIA project formats, S7 communication, complete IEC semantics, FBD, STL, GRAPH, WinCC, motion, safety, and drives are not implemented.</p>'); },
  help() { modal('Engineering quick start', '<p><b>1. Compile</b> the project with F7. The demo contains a LAD seal-in circuit, a two-second timer, a counter, and an SCL speed-control block.</p><p><b>2. Start simulation</b> with F5. In Simulation inputs, turn Start_PB on and then off. Motor_Run remains latched. Stop_PB clears the circuit.</p><p><b>3. Open Overview</b> to edit the HMI, or choose Runtime to use its momentary buttons. Start simulation separately to execute scans.</p><p><b>4. Stop before editing.</b> Select ladder instructions or tags and use Properties to change them. Compile again after changes.</p><p>Ctrl+S exports JSON. Ctrl+Z / Ctrl+Y undo and redo. Escape stops simulation. Ctrl+F searches the project. The prototype keeps a local recovery copy; export important work.</p>'); },
  'close-modal'() { $('#modal').close(); }
};
const menus = {
  Project: [['New project', 'new', ''], ['Open…', 'open', 'Ctrl+O'], ['Save / export…', 'save', 'Ctrl+S'], null, ['Export PLC tags…', 'export-tags', ''], ['Project view', 'view:block:main', ''], ['Portal view', 'view:portal', '']],
  Edit: [['Undo', 'undo', 'Ctrl+Z'], ['Redo', 'redo', 'Ctrl+Y'], null, ['Delete selection', 'delete', 'Delete'], ['Duplicate network', 'duplicate', ''], ['Search project', 'search', 'Ctrl+F']],
  View: [['Devices & networks', 'view:devices', ''], ['Program editor', 'view:block:main', ''], ['PLC tags', 'view:tags', ''], ['HMI screen', 'view:hmi', ''], ['Watch table', 'view:watch', ''], ['Trace', 'view:trace', ''], ['Cross-references (LAD)', 'view:references', '']],
  Insert: [['Network', 'add-network', ''], ['PLC tag', 'add-tag', ''], ['Remote I/O device', 'add-device', ''], ['Parallel branch', 'add-branch', '']],
  Simulation: [['Compile', 'compile', 'F7'], ['Start simulation', 'run', 'F5'], ['Stop', 'stop', 'Escape'], ['Single scan', 'step', ''], ['Reset virtual controller', 'reset', ''], null, ['Release all virtual forces', 'release-forces', ''], ['Export trace…', 'export-trace', '']],
  Tools: [['Project library', 'view:library', ''], ['Diagnostics', 'view:diagnostics', ''], ['Fit editor', 'fit', '']],
  Help: [['Quick start', 'help', ''], ['About ControlSpace', 'about', '']]
};
function renderMenus() { $('#menubar').innerHTML = Object.entries(menus).map(([title, items]) => `<div class="menu"><button aria-haspopup="menu" aria-expanded="false">${title}</button><div class="menu-panel" role="menu">${items.map(item => item ? `<button role="menuitem" data-action="${item[1]}">${item[0]}<kbd>${item[2]}</kbd></button>` : '<div class="menu-separator"></div>').join('')}</div></div>`).join(''); }
function treeRows() { const p = workspace.project; return [
  { text: p.name, depth: 0, folder: true, icon: '▣', key: 'portal' },
  { text: 'Devices & networks', depth: 1, icon: '⌘', key: 'devices' },
  { text: 'PLC_1 [Virtual controller]', depth: 1, folder: true, icon: '▣', key: 'diagnostics' },
  { text: 'Device configuration', depth: 2, icon: '▤', key: 'devices' },
  { text: 'Online & diagnostics', depth: 2, icon: '◉', key: 'diagnostics' },
  { text: 'Program blocks', depth: 2, folder: true, icon: '▰', key: 'blocks' },
  ...p.blocks.map(b => ({ text: titleOf(`block:${b.id}`), depth: 3, icon: b.language === 'LAD' ? '▧' : '≡', key: `block:${b.id}`, badge: b.language })),
  { text: 'PLC tags', depth: 2, folder: true, icon: '▰', key: 'tags' }, { text: 'Default tag table', depth: 3, icon: '▦', key: 'tags' },
  { text: 'PLC data types', depth: 2, folder: true, icon: '▰', key: 'library' },
  { text: 'Watch and force tables', depth: 2, folder: true, icon: '▰', key: 'watch' }, { text: 'Watch table_1', depth: 3, icon: '▦', key: 'watch' },
  { text: 'Traces', depth: 2, folder: true, icon: '▰', key: 'trace' }, { text: 'Trace_1', depth: 3, icon: '⌁', key: 'trace' },
  { text: 'Cross-references', depth: 2, icon: '⇄', key: 'references' },
  { text: 'HMI_1 [Virtual panel]', depth: 1, folder: true, icon: '▣', key: 'hmi' },
  { text: 'Screens', depth: 2, folder: true, icon: '▰', key: 'hmi' }, { text: 'Overview', depth: 3, icon: '▣', key: 'hmi' },
  { text: 'Project library', depth: 1, folder: true, icon: '▤', key: 'library' }
]; }
function renderTree() { const query = $('#project-search').value.toLowerCase(); $('#tree').innerHTML = treeRows().filter(row => !query || row.text.toLowerCase().includes(query)).map(row => `<button class="tree-row ${row.folder ? 'tree-folder' : ''} ${row.key === view && !row.folder ? 'active' : ''}" style="padding-left:${7 + row.depth * 15}px" data-view="${row.key}"><span class="tree-arrow">${row.folder ? '▾' : ''}</span><span class="tree-icon">${row.icon}</span><span>${esc(row.text)}</span>${row.badge ? `<small>${row.badge}</small>` : ''}</button>`).join(''); $('#tree-revision').textContent = `rev. ${workspace.project.revision}`; }
function editorTools() {
  const button = (action, label, title = '') => `<button data-action="${action}" title="${esc(title)}">${label}</button>`;
  if (view.startsWith('block:') && blockNow()?.language === 'LAD') return button('add-network', '<span class="symbol">▧</span> Network') + '<span class="tool-divider"></span>' + `<button data-insert="Contact" title="Insert normally-open contact"><span class="symbol">─│ │─</span></button><button data-insert="NegatedContact" title="Insert normally-closed contact"><span class="symbol">─│/│─</span></button><button data-insert="Coil" title="Replace network output with coil"><span class="symbol">─( )─</span></button>` + button('add-branch', '⑂ Branch') + button('delete', '× Delete') + '<span class="toolbar-spacer"></span>' + button('zoom-out', '−') + button('fit', `${Math.round(zoom * 100)}%`) + button('zoom-in', '+');
  if (blockNow()?.language === 'SCL') return button('apply-source', '✓ Apply source') + button('compile', '▧ Compile') + '<span class="toolbar-spacer"></span><span class="muted">SCL subset · typed expressions and IF</span>';
  if (view === 'tags' || view === 'watch') return button('add-tag', '+ Add tag') + button('export-tags', '↧ Export CSV') + button('open', '↥ Import JSON / CSV') + button('release-forces', 'Release forces');
  if (view === 'devices') return button('add-device', '+ Add device') + button('link-device', '⌁ Connect to subnet') + button('add-module', '+ I/O module') + button('delete', '× Delete');
  if (view === 'hmi') return button('runtime-preview', preview ? '▣ Design' : '▶ Runtime') + HMI_KINDS.slice(0, 4).map(kind => `<button data-hmi="${kind}">+ ${kind}</button>`).join('') + '<span class="toolbar-spacer"></span><span class="muted">10 px snap</span>';
  if (view === 'trace') return button('run', '▶ Start acquisition') + button('stop', '■ Stop') + button('export-trace', '↧ Export CSV');
  return button('compile', '▧ Compile project') + button('save', '▣ Save project');
}
function renderEditor() {
  const isCanvas = view === 'devices' || view === 'hmi' || view === 'trace' || blockNow()?.language === 'LAD';
  $('#viewport').hidden = !isCanvas; $('#html-editor').hidden = isCanvas; $('#declarations').hidden = blockNow()?.language !== 'LAD';
  $('#editor-name').textContent = titleOf(view); $('#document-icon').textContent = icons[view] ?? '▧'; $('#language-badge').textContent = blockNow()?.language ?? (view === 'hmi' ? preview ? 'RUNTIME' : 'DESIGN' : 'PROJECT');
  $('#breadcrumb').textContent = `${workspace.project.name} › ${view.startsWith('block:') ? 'PLC_1 › Program blocks › ' : ''}${titleOf(view)}`;
  $('#editor-tools').innerHTML = editorTools();
  $('#viewport').setAttribute('aria-label', `${titleOf(view)} graphical editor. Objects are also selectable in the task pane.`);
  $('#document-tabs').innerHTML = tabs.filter(id => !id.startsWith('block:') || workspace.project.blocks.some(b => b.id === id.slice(6))).map(id => `<button role="tab" aria-selected="${id === view}" class="${id === view ? 'active' : ''}" data-view="${id}"><span class="tab-icon">${icons[id] ?? '▧'}</span>${esc(titleOf(id))}</button>`).join('');
  if (!isCanvas) renderHtmlEditor(); scheduleDraw();
}
function renderHtmlEditor() {
  const host = $('#html-editor');
  if (blockNow()?.language === 'SCL') {
    const b = blockNow(); host.innerHTML = `<div class="source-wrap"><div class="source-heading">${esc(b.name)}　:　CYCLIC FUNCTION　/　${esc(b.language)}</div><div class="source-area"><div class="line-numbers" id="line-numbers"></div><textarea id="scl-source" aria-label="SCL source code" spellcheck="false" autocapitalize="off" ${workspace.controller?.state === 'RUN' ? 'disabled' : ''}></textarea></div><div class="source-footer">Typed tag assignments · IF / ELSE · arithmetic · comparisons · AND / OR / XOR / NOT. No live controller code generation.</div></div>`;
    $('#scl-source').value = b.source; updateLineNumbers(); $('#scl-source').addEventListener('input', () => { sourceDirty = true; updateLineNumbers(); $('#status-message').textContent = 'SCL source modified. Apply or compile to commit.'; }); $('#scl-source').addEventListener('keydown', event => { if (event.key === 'Tab') { event.preventDefault(); const text = event.target, a = text.selectionStart, z = text.selectionEnd; text.setRangeText('    ', a, z, 'end'); sourceDirty = true; updateLineNumbers(); } });
  } else if (view === 'tags' || view === 'watch') {
    host.innerHTML = `<table class="data-table"><thead><tr><th></th><th>Name</th><th>Data type</th><th>Address</th><th>${view === 'watch' ? 'Monitor value' : 'Start value'}</th><th>Retain</th><th>Comment</th></tr></thead><tbody>${workspace.project.tags.map((t, i) => `<tr class="editable-row ${selected === `tag:${t.name}` ? 'selected' : ''}" data-tag="${esc(t.name)}" tabindex="0"><td class="row-number">${i + 1}</td><td class="tag-name">${esc(t.name)}</td><td class="type-cell">${t.type}</td><td>${esc(t.address)}</td><td class="live-value" ${view === 'watch' ? `data-value="${esc(t.name)}"` : ''}>${formatValue(t.type, t.initialValue)}</td><td>${t.retain ? '✓' : ''}</td><td>${esc(t.comment)}</td></tr>`).join('')}</tbody></table><div class="empty-hint">Select a row to edit its properties. ${view === 'watch' ? 'Virtual forces are applied at scan boundaries; Stop clears all forces.' : 'Memory ranges and tag types are validated before edits are accepted.'}</div>`;
  } else if (view === 'portal') {
    host.innerHTML = `<div class="portal"><div class="portal-eyebrow">INTEGRATED ENGINEERING WORKSPACE</div><h1>Welcome to ControlSpace</h1><p>Configure your devices. Engineer your program. Bring the process into view.</p><div class="portal-grid">${[['devices', '⌘', 'Configure a device', 'Compose a virtual rack and connect your engineering topology.'], ['block:main', '▧', 'Write a PLC program', 'Edit ladder networks, typed tags, and structured control logic.'], ['hmi', '▣', 'Visualize a process', 'Build a tag-bound HMI and test it against the virtual controller.'], ['watch', '◉', 'Test your program', 'Monitor tags, simulate input signals, and inspect trace samples.']].map(([id, icon, title, text]) => `<button class="portal-card" data-view="${id}"><span class="card-icon">${icon}</span><b>${title}</b><small>${text}</small></button>`).join('')}</div><div class="portal-note"><b>Local simulation workspace.</b> No network connection to industrial controllers is made. This is a JavaScript interaction prototype; the Uno application is a separate source build.</div></div>`;
  } else if (view === 'references') {
    host.innerHTML = '<div class="info-banner">LAD operand cross-references. SCL and HMI references are not included in this view.</div><table class="data-table"><thead><tr><th>Tag</th><th>Access</th><th>Block</th><th>Network</th></tr></thead><tbody>' + workspace.crossReferences().map(r => `<tr><td class="tag-name">${esc(r.tag)}</td><td>${r.access}</td><td>${esc(r.block)}</td><td>${esc(r.network)}</td></tr>`).join('') + '</tbody></table>';
  } else if (view === 'diagnostics') {
    host.innerHTML = '<div class="info-banner">Virtual-controller diagnostics. These are local simulation values, not observations from hardware.</div><div id="large-diagnostics"></div><div class="device-help"><b>Connection status: offline</b><br>ControlSpace does not implement S7/PROFINET communication or download programs to PLCs. Device IP addresses are project metadata only.</div>';
  } else if (view === 'library') {
    host.innerHTML = `<div class="portal"><div class="portal-eyebrow">REUSABLE BUILDING BLOCKS</div><h1>Project library</h1><p>Insert supported instructions from the task pane. Each editor shares the same project model and transaction history.</p><div class="portal-grid">${[['LAD', 'Contacts, coils, parallel branches, comparators, timers, counters, and constant MOVE.'], ['SCL', 'Typed assignments, conditional branches, arithmetic, comparisons, and Boolean expressions.'], ['HMI', 'Labels, momentary buttons, indicator lamps, gauges, tanks, and numeric displays.']].map(([name, text]) => `<div class="portal-card"><b>${name}</b><small>${text}</small></div>`).join('')}</div><div class="portal-note">Library versioning, user-defined function block instances, vendor device catalogs, and protected libraries are not implemented.</div></div>`;
  } else host.innerHTML = '<div class="empty">Choose an engineering editor from the project tree.</div>';
  updateRuntime();
}
function updateLineNumbers() { const source = $('#scl-source'); if (source) $('#line-numbers').textContent = Array.from({ length: Math.max(20, source.value.split('\n').length) }, (_, i) => i + 1).join('\n'); }
const paletteGroups = [
  ['Bit logic operations', [['Contact', '─│ │─', 'Normally open contact'], ['NegatedContact', '─│/│─', 'Normally closed contact'], ['Coil', '─( )─', 'Assignment coil'], ['SetCoil', '─(S)─', 'Set output'], ['ResetCoil', '─(R)─', 'Reset output'], ['RisingEdge', '─│P│─', 'Rising edge'], ['FallingEdge', '─│N│─', 'Falling edge']]],
  ['Timer operations', [['TimerOn', 'TON', 'Generate on-delay'], ['TimerOff', 'TOF', 'Generate off-delay'], ['Pulse', 'TP', 'Generate pulse']]],
  ['Counter operations', [['CountUp', 'CTU', 'Count up on a rising edge']]],
  ['Comparator operations', [['Greater', '>', 'Greater than'], ['Less', '<', 'Less than'], ['Equal', '==', 'Equal to']]],
  ['Move operations', [['Move', 'MOVE', 'Move a numeric constant']]]
];
function renderTask() {
  document.querySelectorAll('[data-task]').forEach(button => button.classList.toggle('active', button.dataset.task === task)); $('#task-heading').textContent = task === 'properties' ? 'Properties' : view === 'hmi' ? 'Toolbox' : 'Instructions';
  if (task === 'properties') return renderProperties();
  const host = $('#task-content');
  if (view === 'hmi') host.innerHTML = `<div class="palette-heading">Basic objects</div>${HMI_KINDS.map(kind => `<button class="palette-button" data-hmi="${kind}"><span class="glyph">${({ Label: 'A', Button: '▣', Lamp: '●', Tank: '▥', Numeric: '123', Gauge: '⌁' })[kind]}</span><span>${kind}<small>Insert tag-bound ${kind.toLowerCase()}</small></span></button>`).join('')}<div class="palette-heading">Screen objects</div><div class="instructions-list">${(workspace.project.screens[0]?.objects ?? []).map(o => `<button data-select="${o.id}" class="${o.id === selected ? 'active' : ''}">${o.kind}　${esc(o.text)}</button>`).join('')}</div>`;
  else if (view === 'devices') host.innerHTML = `<div class="palette-heading">Generic simulation hardware</div><button class="palette-button" data-action="add-device"><span class="glyph">▥</span><span>Remote I/O station<small>Add a configurable virtual device</small></span></button><button class="palette-button" data-action="add-module"><span class="glyph">▯</span><span>Digital input module<small>Add to the selected device</small></span></button><div class="palette-heading">Configured devices</div><div class="instructions-list">${workspace.project.devices.map(d => `<button data-select="${d.id}">${esc(d.name)}　${esc(d.ipAddress)}</button>`).join('')}</div><div class="kbd-note">Generic illustrations, not manufacturer hardware models. IP addresses are local project metadata.</div>`;
  else {
    host.innerHTML = '<div class="palette-heading">Basic instructions　/　Simulation</div>' + paletteGroups.map(([heading, items], index) => `<details ${index < 2 ? 'open' : ''}><summary>${heading}</summary>${items.map(([kind, symbol, label]) => `<button class="palette-button" draggable="true" data-insert="${kind}"><span class="glyph">${symbol}</span><span>${label}<small>${kind}</small></span></button>`).join('')}</details>`).join('');
    if (blockNow()?.language === 'LAD') host.innerHTML += `<details><summary>Instruction list (keyboard access)</summary><div class="instructions-list">${blockNow().networks.flatMap((n, i) => [`<button data-select="${n.id}">Network ${i + 1}: ${esc(n.title)}</button>`, ...[...n.branches.flat(), n.output].map(op => `<button data-select="${op.id}">${op.kind}　${esc(op.tag)}</button>`)]).join('')}</div></details>`;
    host.innerHTML += '<div class="kbd-note">Select an instruction in the editor to edit its operands. Stop simulation before making changes.</div>';
  }
}
function inputField(name, value, label = name, type = 'text') { return `<label>${esc(label)}<input name="${name}" type="${type}" value="${esc(value)}" ${type === 'number' ? 'step="any"' : ''}></label>`; }
function selectField(name, value, values, label = name, empty = false) { return `<label>${esc(label)}<select name="${name}">${empty ? '<option value="">(none)</option>' : ''}${values.map(v => `<option value="${esc(v)}" ${v === value ? 'selected' : ''}>${esc(v)}</option>`).join('')}</select></label>`; }
function renderProperties() {
  const p = workspace.project, n = selectedNetwork(), op = n ? [...n.branches.flat(), n.output].find(op => op.id === selected) : null, tag = selected.startsWith('tag:') ? p.tags.find(t => t.name === selected.slice(4)) : null, object = view === 'hmi' ? p.screens[0]?.objects.find(o => o.id === selected) : null, device = view === 'devices' ? p.devices.find(d => d.id === selected) : null;
  let heading = 'Selection', fieldsHtml = '', more = '';
  if (op) { heading = op.kind; fieldsHtml = selectField('kind', op.kind, n.output.id === op.id ? KINDS.slice(7) : KINDS.slice(0, 7), 'Instruction') + selectField('tag', op.tag, p.tags.map(t => t.name), 'Operand') + inputField('parameter', op.parameter, 'Constant / preset (ms)', 'number') + selectField('auxiliary', op.auxiliary, p.tags.map(t => t.name), 'Elapsed / reset tag', true); more = '<button type="button" data-action="add-branch">+ Parallel branch</button><button type="button" data-action="delete">Delete</button>'; }
  else if (n?.id === selected) { heading = 'Network'; fieldsHtml = inputField('title', n.title, 'Title') + inputField('comment', n.comment, 'Comment'); more = '<button type="button" data-action="duplicate">Duplicate</button><button type="button" data-action="delete">Delete network</button>'; }
  else if (tag) { heading = tag.name; fieldsHtml = inputField('name', tag.name, 'Name') + selectField('type', tag.type, TYPES, 'Data type') + inputField('address', tag.address, 'Address') + inputField('initialValue', tag.initialValue, 'Start value', 'number') + inputField('comment', tag.comment, 'Comment') + `<label><span><input name="retain" type="checkbox" ${tag.retain ? 'checked' : ''}> Retain on warm reset</span></label>`; more = `<button type="button" data-action="delete">Delete tag</button>`; }
  else if (object) { heading = `${object.kind} object`; fieldsHtml = inputField('text', object.text, 'Text') + selectField('tag', object.tag, p.tags.map(t => t.name), 'Tag binding', true) + inputField('color', object.color, 'Color (#RRGGBB)') + '<div class="pair">' + inputField('x', object.x, 'X', 'number') + inputField('y', object.y, 'Y', 'number') + '</div><div class="pair">' + inputField('width', object.width, 'Width', 'number') + inputField('height', object.height, 'Height', 'number') + '</div>'; more = '<button type="button" data-action="delete">Delete object</button>'; }
  else if (device) { heading = device.name; fieldsHtml = inputField('name', device.name, 'Device name') + inputField('ipAddress', device.ipAddress, 'IPv4 address') + inputField('model', device.model, 'Model description') + '<div class="pair">' + inputField('x', device.x, 'X', 'number') + inputField('y', device.y, 'Y', 'number') + '</div>'; more = '<button type="button" data-action="link-device">Connect to subnet</button><button type="button" data-action="add-module">Add module</button><button type="button" data-action="delete">Delete device</button>'; }
  else { $('#task-content').innerHTML = '<div class="property-heading">No selection</div><div class="property-description">Select an instruction, network header, tag, device, or HMI object. Its editable properties appear here.</div>'; return; }
  $('#task-content').innerHTML = `<div class="property-heading">${esc(heading)}</div><form id="properties-form" class="property-form">${fieldsHtml}<button type="submit" class="primary">Apply properties</button><div class="property-actions">${more}</div></form>`;
  if (tag) $('#task-content').innerHTML += `<div class="property-heading">Virtual monitor / force</div><form id="force-form" class="property-form"><label>Value<input name="value" type="number" step="any" value="${workspace.controller?.read(tag.name) ?? tag.initialValue}"></label><div class="property-actions"><button type="submit">Force value</button><button type="button" data-release="${esc(tag.name)}">Release</button>${isInput(tag) ? `<button type="button" data-input-apply="${esc(tag.name)}">Set input</button>` : ''}</div></form><div class="property-description">Forces apply on the next virtual scan and are cleared by Stop. No physical device is affected.</div>`;
  $('#properties-form').addEventListener('submit', event => { event.preventDefault(); guard(() => {
    const form = event.target, values = Object.fromEntries(new FormData(form)), num = key => Number(values[key]);
    if (tag && values.name !== tag.name) { // Rename and property changes are one undoable transaction.
      workspace.renameTag(tag.name, values.name, target => Object.assign(target, { type: values.type, address: values.address, initialValue: num('initialValue'), comment: values.comment, retain: Boolean(values.retain) }));
      selected = `tag:${values.name}`;
    } else edit('Edit properties', project => {
      if (op) { const net = project.blocks.find(b => b.id === blockNow().id).networks.find(net => net.id === n.id), target = [...net.branches.flat(), net.output].find(i => i.id === selected); Object.assign(target, { kind: values.kind, tag: values.tag, parameter: num('parameter'), auxiliary: values.auxiliary }); }
      else if (n?.id === selected) Object.assign(project.blocks.find(b => b.id === blockNow().id).networks.find(net => net.id === selected), { title: values.title, comment: values.comment });
      else if (tag) Object.assign(project.tags.find(t => t.name === tag.name), { type: values.type, address: values.address, initialValue: num('initialValue'), comment: values.comment, retain: Boolean(values.retain) });
      else if (object) Object.assign(project.screens[0].objects.find(o => o.id === selected), { text: values.text, tag: values.tag, color: values.color, x: num('x'), y: num('y'), width: num('width'), height: num('height') });
      else if (device) Object.assign(project.devices.find(d => d.id === selected), { name: values.name, ipAddress: values.ipAddress, model: values.model, x: num('x'), y: num('y') });
    }); refresh(); displayStatus('Properties updated. Compile again before running.');
  }); });
  $('#force-form')?.addEventListener('submit', event => { event.preventDefault(); guard(() => { ensureRuntime().force(tag.name, Number(new FormData(event.target).get('value'))); updateRuntime(); displayStatus(`Virtual force prepared for ${tag.name}.`); }); });
}
function diagnosticsHtml() { const r = workspace.controller; return `<div class="diagnostic-list"><span>Controller</span><b>Virtual PLC · local simulation</b><span>State</span><b>${r?.state ?? 'STOP'}</b><span>Completed cycles</span><b>${r?.cycle ?? 0}</b><span>Virtual elapsed time</span><b>${r?.virtualMilliseconds ?? 0} ms</b><span>Last scan CPU duration</span><b>${(r?.cpuMilliseconds ?? 0).toFixed(3)} ms</b><span>Active forces</span><b>${r?.forces.size ?? 0}</b><span>Trace samples</span><b>${r?.trace.count ?? 0} / 2048</b><span>Fault</span><b>${esc(r?.fault ?? 'None')}</b></div>`; }
function renderOutput() {
  document.querySelectorAll('[data-bottom]').forEach(button => button.classList.toggle('active', button.dataset.bottom === bottom)); $('#message-count').textContent = buildMessages.length;
  const host = $('#output-content');
  if (bottom === 'messages') host.innerHTML = `<table class="data-table"><thead><tr><th style="width:32px"></th><th>Description</th><th>Code</th><th>Location</th></tr></thead><tbody>${buildMessages.map(d => `<tr><td><span class="message-icon ${d.severity === 'Error' ? 'message-error' : d.severity === 'Warning' ? 'message-warning' : ''}">${d.severity === 'Error' ? '✕' : d.severity === 'Warning' ? '△' : '✓'}</span></td><td title="${esc(d.message)}">${esc(d.message)}</td><td>${esc(d.code)}</td><td>${esc(d.location)}</td></tr>`).join('')}</tbody></table>${buildMessages.length ? '' : '<div class="empty-hint">Compile the project to display diagnostics.</div>'}`;
  else if (bottom === 'inputs') host.innerHTML = `<div class="input-controls">${workspace.project.tags.filter(t => isInput(t)).map(t => `<div class="input-card"><div><strong>${esc(t.name)}</strong><small>${esc(t.address)} · ${t.type}</small></div><button data-toggle-input="${esc(t.name)}" ${t.type !== 'Bool' ? 'disabled' : ''}>OFF</button></div>`).join('')}</div><div class="empty-hint">Switches hold their state until toggled. HMI buttons are momentary. Each scan advances 100 ms of virtual time.</div>`;
  else if (bottom === 'diagnostics') host.innerHTML = diagnosticsHtml();
  else host.innerHTML = `<div class="diagnostic-list"><span>Project</span><b>${esc(workspace.project.name)}</b><span>Format</span><b>ControlSpace JSON v1</b><span>PLC tags / blocks</span><b>${workspace.project.tags.length} / ${workspace.project.blocks.length}</b><span>Revision / modified</span><b>${workspace.project.revision} / ${workspace.dirty ? 'Yes' : 'No'}</b><span>Selection</span><b>${esc(selected || 'None — select an item to edit it in the right pane')}</b></div>`;
  updateRuntime();
}
function updateRuntime() {
  const r = workspace.controller; $('#runtime-state').textContent = `● ${r?.state ?? 'STOP'}`; $('#runtime-state').className = r?.state === 'RUN' ? 'running' : r?.state === 'FAULT' ? 'fault' : '';
  $('#runtime-metrics').textContent = `${r?.cycle ?? 0} scans · ${(r?.cpuMilliseconds ?? 0).toFixed(3)} ms CPU`; $('#zoom-label').textContent = `${Math.round(zoom * 100)}%`;
  $('#compile-status').textContent = workspace.compilation ? workspace.compilation.success ? 'Compile: 0 errors' : 'Compile: errors' : 'Not compiled';
  document.querySelectorAll('[data-action="undo"]').forEach(b => b.disabled = workspace.undoStack.length === 0); document.querySelectorAll('[data-action="redo"]').forEach(b => b.disabled = workspace.redoStack.length === 0);
  document.querySelectorAll('[data-toggle-input]').forEach(button => { const tag = workspace.project.tags.find(t => t.name === button.dataset.toggleInput), value = r ? r.inputs.get(r.slot(tag.name)) : tag.initialValue; button.textContent = value ? 'ON' : 'OFF'; button.classList.toggle('on', Boolean(value)); button.setAttribute('aria-pressed', Boolean(value)); });
  document.querySelectorAll('[data-value]').forEach(cell => { const t = workspace.project.tags.find(t => t.name === cell.dataset.value); cell.textContent = formatValue(t.type, r?.read(t.name) ?? t.initialValue); });
  if (bottom === 'diagnostics') $('#output-content').innerHTML = diagnosticsHtml(); if ($('#large-diagnostics')) $('#large-diagnostics').innerHTML = diagnosticsHtml();
  if (r?.state === 'FAULT') { $('#status-message').textContent = `Simulation fault: ${r.fault}`; if ($('#scl-source')) $('#scl-source').disabled = false; }
  scheduleDraw();
}
function refresh() { $('#project-title').textContent = `${workspace.project.name}${workspace.dirty || sourceDirty ? ' *' : ''}`; document.title = `${workspace.project.name} — ControlSpace`; renderTree(); renderEditor(); renderTask(); renderOutput(); }
workspace.subscribe(() => { scheduleRefresh(); clearTimeout(saveTimer); saveTimer = setTimeout(() => { try { localStorage.setItem(recoveryKey, serializeProject(workspace.project)); } catch (error) { displayStatus(`Recovery could not be saved: ${error.message}`, true); } }, 400); });
function closeMenus() { document.querySelectorAll('.menu.open').forEach(menu => { menu.classList.remove('open'); menu.firstElementChild.setAttribute('aria-expanded', 'false'); }); }
document.addEventListener('click', event => {
  const menuToggle = event.target.closest('.menu > button'); if (menuToggle) { const open = !menuToggle.parentElement.classList.contains('open'); closeMenus(); menuToggle.parentElement.classList.toggle('open', open); menuToggle.setAttribute('aria-expanded', open); return; }
  const button = event.target.closest('[data-action],[data-view],[data-insert],[data-hmi],[data-bottom],[data-task],[data-tag],[data-select],[data-toggle-input],[data-release],[data-input-apply]'); closeMenus(); if (!button || button.disabled) return;
  guard(() => {
    if (button.dataset.action) { const action = button.dataset.action; if (action.startsWith('view:')) navigate(action.slice(5)); else if (action === 'search') $('#project-search').focus(); else actions[action]?.(); }
    else if (button.dataset.view) navigate(button.dataset.view);
    else if (button.dataset.insert) addInstruction(button.dataset.insert);
    else if (button.dataset.hmi) addHmi(button.dataset.hmi);
    else if (button.dataset.bottom) { bottom = button.dataset.bottom; renderOutput(); }
    else if (button.dataset.task) { task = button.dataset.task; renderTask(); }
    else if (button.dataset.tag) { selected = `tag:${button.dataset.tag}`; task = 'properties'; renderTask(); if (view === 'tags' || view === 'watch') renderHtmlEditor(); }
    else if (button.dataset.select) { selected = button.dataset.select; task = 'properties'; renderTask(); scheduleDraw(); }
    else if (button.dataset.toggleInput) { const r = ensureRuntime(), name = button.dataset.toggleInput, value = r.inputs.get(r.slot(name)); r.setInput(name, value ? 0 : 1); updateRuntime(); }
    else if (button.dataset.release) { workspace.controller?.release(button.dataset.release); displayStatus('Virtual force released.'); updateRuntime(); }
    else if (button.dataset.inputApply) { ensureRuntime().setInput(button.dataset.inputApply, Number($('#force-form [name="value"]').value)); updateRuntime(); }
  });
});
$('#project-search').addEventListener('input', renderTree);
$('#file-input').addEventListener('change', () => safeAsync(async () => {
  const file = $('#file-input').files[0]; if (!file) return; if (file.size > 8 * 1024 * 1024) throw new Error('File exceeds the 8 MiB import limit.'); const text = await file.text(); commitSource();
  if (file.name.toLowerCase().endsWith('.csv')) { const tags = tagsFromCsv(text); edit('Import tag table', p => { p.tags = tags; }); view = 'tags'; }
  else { const project = parseProject(text); if (workspace.dirty && !confirm('Replace the current project and discard unsaved edits?')) return; workspace.load(project); view = project.blocks.length ? `block:${project.blocks[0].id}` : 'portal'; }
  selected = ''; sourceDirty = false; buildMessages = []; refresh(); displayStatus(`Imported ${file.name}.`);
}));
const viewport = $('#viewport');
function pointerPoint(event) { const bounds = viewport.getBoundingClientRect(); return { x: event.clientX - bounds.left, y: event.clientY - bounds.top }; }
viewport.addEventListener('pointerdown', event => {
  if (event.button !== 0) return; const point = pointerPoint(event), hit = scene?.hits.slice().reverse().find(h => point.x >= h.x && point.x <= h.x + h.width && point.y >= h.y && point.y <= h.y + h.height); if (!hit) { selected = ''; renderTask(); scheduleDraw(); return; }
  viewport.focus(); viewport.setPointerCapture(event.pointerId); selected = hit.id;
  guard(() => {
    if (view === 'hmi' && preview) { const object = workspace.project.screens[0].objects.find(o => o.id === hit.id); if (object?.kind === 'Button') { const r = ensureRuntime(), tag = workspace.project.tags.find(t => t.name === object.tag); if (tag?.type !== 'Bool' || !isInput(tag)) throw new Error('Runtime buttons require a Boolean %I tag.'); r.setInput(object.tag, 1); momentary = { name: object.tag, pointerId: event.pointerId }; updateRuntime(); } return; }
    task = 'properties'; renderTask();
    if (workspace.controller?.state !== 'RUN' && ['hmi', 'devices'].includes(view)) { const object = view === 'hmi' ? workspace.project.screens[0].objects.find(o => o.id === hit.id) : workspace.project.devices.find(d => d.id === hit.id); if (object) drag = { id: hit.id, x: object.x, y: object.y, startX: point.x, startY: point.y, originalX: object.x, originalY: object.y, pointerId: event.pointerId }; }
    scheduleDraw();
  });
});
viewport.addEventListener('pointermove', event => { if (!drag) return; const point = pointerPoint(event), scale = scene?.editScale ?? 1; drag.x = Math.max(0, Math.round((drag.originalX + (point.x - drag.startX) / scale) / 10) * 10); drag.y = Math.max(0, Math.round((drag.originalY + (point.y - drag.startY) / scale) / 10) * 10); if (view === 'hmi') { const s = workspace.project.screens[0], o = s.objects.find(o => o.id === drag.id); drag.x = Math.min(s.width - o.width, drag.x); drag.y = Math.min(s.height - o.height, drag.y); } scheduleDraw(); });
function releasePointer(event, cancelled = false) {
  if (momentary) { guard(() => workspace.controller?.setInput(momentary.name, 0)); momentary = null; updateRuntime(); }
  if (drag) { const last = drag; drag = null; if (!cancelled && (last.x !== last.originalX || last.y !== last.originalY)) guard(() => edit('Move object', p => { const target = view === 'hmi' ? p.screens[0].objects.find(o => o.id === last.id) : p.devices.find(d => d.id === last.id); target.x = last.x; target.y = last.y; })); scheduleDraw(); }
  if (event && viewport.hasPointerCapture(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
}
viewport.addEventListener('pointerup', event => releasePointer(event)); viewport.addEventListener('pointercancel', event => releasePointer(event, true)); viewport.addEventListener('lostpointercapture', event => releasePointer(null, true));
window.addEventListener('blur', () => { releasePointer(null, true); });
viewport.addEventListener('wheel', event => { if (blockNow()?.language !== 'LAD') return; event.preventDefault(); if (event.ctrlKey) zoom = Math.min(2, Math.max(.5, zoom - Math.sign(event.deltaY) * .05)); else scroll = Math.min(Math.max(0, (scene?.contentHeight ?? 0) - viewport.clientHeight / zoom), Math.max(0, scroll + event.deltaY / zoom)); updateRuntime(); }, { passive: false });
document.addEventListener('dragstart', event => { const item = event.target.closest('[data-insert]'); if (item) { event.dataTransfer.setData('text/controlspace-instruction', item.dataset.insert); event.dataTransfer.effectAllowed = 'copy'; } });
viewport.addEventListener('dragover', event => { if (event.dataTransfer.types.includes('text/controlspace-instruction') && blockNow()?.language === 'LAD') { event.preventDefault(); event.dataTransfer.dropEffect = 'copy'; } });
viewport.addEventListener('drop', event => { const kind = event.dataTransfer.getData('text/controlspace-instruction'); if (KINDS.includes(kind)) { event.preventDefault(); guard(() => addInstruction(kind)); } });
for (const [selector, variable, axis, reverse, min, max] of [['.left-splitter', '--left', 'clientX', false, 150, 440], ['.right-splitter', '--right', 'clientX', true, 185, 450], ['.bottom-splitter', '--bottom', 'clientY', true, 90, 410]]) {
  const splitter = $(selector); let sizing = null;
  splitter.addEventListener('pointerdown', event => { splitter.setPointerCapture(event.pointerId); sizing = { start: event[axis], value: parseFloat(getComputedStyle(document.documentElement).getPropertyValue(variable)) }; event.preventDefault(); });
  splitter.addEventListener('pointermove', event => { if (!sizing) return; const next = Math.min(max, Math.max(min, sizing.value + (event[axis] - sizing.start) * (reverse ? -1 : 1))); document.documentElement.style.setProperty(variable, `${next}px`); scheduleDraw(); });
  splitter.addEventListener('pointerup', () => sizing = null); splitter.addEventListener('pointercancel', () => sizing = null);
  splitter.addEventListener('keydown', event => { if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return; event.preventDefault(); const delta = ['ArrowLeft', 'ArrowUp'].includes(event.key) ? -10 : 10, value = parseFloat(getComputedStyle(document.documentElement).getPropertyValue(variable)); document.documentElement.style.setProperty(variable, `${Math.min(max, Math.max(min, value + delta * (reverse ? -1 : 1)))}px`); scheduleDraw(); });
}
document.addEventListener('keydown', event => {
  const typing = /INPUT|TEXTAREA|SELECT/.test(event.target.tagName), control = event.ctrlKey || event.metaKey;
  if (event.key === 'Escape') { closeMenus(); if ($('#modal').open) return; actions.stop(); event.preventDefault(); return; }
  if (event.key === 'F7' || event.key === 'F5') { event.preventDefault(); guard(() => event.key === 'F7' ? compile() : actions.run()); return; }
  if (control && ['s', 'o', 'f'].includes(event.key.toLowerCase())) { event.preventDefault(); guard(() => ({ s: actions.save, o: actions.open, f: () => $('#project-search').focus() })[event.key.toLowerCase()]()); return; }
  if (!typing && control && ['z', 'y'].includes(event.key.toLowerCase())) { event.preventDefault(); guard(() => event.key.toLowerCase() === 'y' || event.shiftKey ? actions.redo() : actions.undo()); }
  if (!typing && event.key === 'Delete') { event.preventDefault(); guard(actions.delete); }
  if (!typing && event.key === 'Enter' && event.target.matches('[data-tag]')) event.target.click();
});
new ResizeObserver(scheduleDraw).observe(viewport);
document.addEventListener('visibilitychange', () => { if (document.hidden) { releasePointer(null, true); if (workspace.controller?.state === 'RUN') { workspace.controller.stop(); updateRuntime(); $('#status-message').textContent = 'Simulation stopped because this page was hidden. Restart explicitly to continue.'; } } });
setInterval(() => { const r = workspace.controller; if (r?.state === 'RUN') { r.step(100); updateRuntime(); } }, 100);
window.addEventListener('beforeunload', event => { try { commitSource(); localStorage.setItem(recoveryKey, serializeProject(workspace.project)); } catch { /* Unsaved state is surfaced through the browser confirmation below. */ } if (sourceDirty) { event.preventDefault(); event.returnValue = ''; } });
// Read-only diagnostics used by browser tests; all mutations go through visible UI commands.
Object.defineProperty(window, 'ControlSpacePrototype', { value: Object.freeze({ get project() { return clone(workspace.project); }, get snapshot() { return workspace.controller?.snapshot() ?? null; }, get renderer() { return graphics.backend; }, get view() { return view; }, get hits() { return clone(scene?.hits ?? []); }, get undoCount() { return workspace.undoStack.length; }, get diagnostics() { return clone(buildMessages); } }), writable: false });
renderMenus(); refresh(); graphics.ready.then(scheduleDraw);
if (recoveryError) displayStatus(recoveryError, true); else if (recovered) $('#status-message').textContent = 'Local project recovered. Ready for offline engineering.';
console.info('[ControlSpace] JavaScript interaction prototype ready — not the Uno build.');
