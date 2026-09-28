import test from 'node:test';
import assert from 'node:assert/strict';
import { createDemo } from '../../prototype/engine/model.js';
import { compileProject, renameSclTag } from '../../prototype/engine/languages.js';
import { VirtualPlc } from '../../prototype/engine/runtime.js';
for (const newline of ['\n', '\r\n', '\r']) test(`editor newline conformance ${JSON.stringify(newline)}`, () => {
  const project = createDemo();
  const block = project.blocks.find(b => b.language === 'SCL');
  project.blocks = [{ ...block, source: `// editor comment${newline}Speed_Actual := 42;` }];
  const compiled = compileProject(project); assert.equal(compiled.success, true);
  const plc = new VirtualPlc(compiled.program); plc.step(100, true); assert.equal(plc.read('Speed_Actual'), 42);
  project.blocks[0].source = `// editor comment${newline}Missing_Tag := 42;`;
  const error = compileProject(project).diagnostics.find(d => d.severity === 'Error');
  assert.equal(error.line, 2); assert.equal(error.column, 1);
  assert.equal(renameSclTag(`// Speed_Actual${newline}Speed_Actual := 42;`, 'Speed_Actual', 'Actual'), `// Speed_Actual${newline}Actual := 42;`);
});
