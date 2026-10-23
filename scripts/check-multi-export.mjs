// Run with Node >=22.18. Execute the actual main.ts via native type stripping;
// replace only Figma tree traversal/rendering, which require the Figma runtime.
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';

const messages = [];
const calls = [];
const events = {};
const parent = { id: 'page' };
const roots = ['背包', '商店'].map((name, i) => ({ id: String(i), name, type: 'FRAME', parent }));
let performExport = async root => ({ manifest: { screen: { name: root.name }, assets: [] }, assets: [] });
globalThis.__figforgeTest = {
  buildTree: async root => ({ id: root.id, children: [] }),
  exportDesign: async (...args) => {
    calls.push(args[0].id);
    args.at(-1)(1, 1, 'done');
    return performExport(args[0]);
  },
};
globalThis.__html__ = '';
globalThis.figma = {
  showUI() {}, loadAllPagesAsync: async () => {},
  currentPage: { selection: roots },
  ui: { postMessage: msg => messages.push(msg) },
  on: (name, fn) => { events[name] = fn; },
  getNodeByIdAsync: async id => roots.find(root => root.id === id),
  viewport: { scrollAndZoomIntoView() {} },
};
const hooks = registerHooks({
  resolve(specifier, context, next) {
    if (context.parentURL?.endsWith('/plugin/src/main.ts')) {
      if (specifier === './types') return next(new URL('../plugin/src/types.ts', import.meta.url).href, context);
      if (specifier === './exporter' || specifier === './traverser')
        return { url: `figforge-test:${specifier.slice(2)}`, shortCircuit: true };
    }
    return next(specifier, context);
  },
  load(url, context, next) {
    if (url.startsWith('figforge-test:')) {
      const name = url.endsWith('exporter') ? 'exportDesign' : 'buildTree';
      return { format: 'module', source: `export const ${name} = (...args) => globalThis.__figforgeTest.${name}(...args);`, shortCircuit: true };
    }
    return next(url, context);
  },
});
try {
  await import('../plugin/src/main.ts');
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(messages.find(m => m.type === 'selection-info').trees.length, 2);
  const request = { type: 'export', bundleName: '公共UI' };
  await figma.ui.onmessage(request);
  assert.deepEqual(calls, ['0', '1']);
  const complete = messages.find(m => m.type === 'export-complete');
  assert.equal(complete.bundleName, '公共UI');
  assert.equal(complete.frames.length, 2);
  assert.deepEqual(messages.filter(m => m.type === 'progress').map(m => m.current), [1, 2]);
  await figma.ui.onmessage({ type: 'highlight-element', nodeId: '0' });
  assert.equal(figma.currentPage.selection, roots, 'preview must preserve multi-selection');

  for (const invalid of [[], [roots[0], { ...roots[1], type: 'TEXT' }],
    [roots[0], { ...roots[1], parent: { id: 'other' } }]]) {
    figma.currentPage.selection = invalid;
    messages.length = calls.length = 0;
    await figma.ui.onmessage(request);
    assert.equal(calls.length, 0);
    assert.ok(messages.some(m => m.type === 'export-error'));
  }

  // A batch owns a selection snapshot and prevents concurrent renderer calls.
  figma.currentPage.selection = roots;
  messages.length = calls.length = 0;
  let release;
  const pending = new Promise(resolve => { release = resolve; });
  performExport = async root => {
    if (root.id === '0') await pending;
    return { manifest: { screen: { name: root.name }, assets: [] }, assets: [] };
  };
  const batch = figma.ui.onmessage(request);
  await new Promise(resolve => setImmediate(resolve));
  figma.currentPage.selection = [roots[1]];
  await figma.ui.onmessage(request);
  assert.ok(messages.some(m => m.type === 'status' && m.error));
  release();
  await batch;
  assert.deepEqual(calls, ['0', '1']);

  // Failure produces no partial download; the next request can still succeed.
  messages.length = 0;
  performExport = async () => { throw new Error('render failed'); };
  await figma.ui.onmessage(request);
  assert.ok(messages.some(m => m.type === 'export-error'));
  assert.ok(!messages.some(m => m.type === 'export-complete'));
  performExport = async root => ({ manifest: { screen: { name: root.name }, assets: [] }, assets: [] });
  await figma.ui.onmessage(request);
  assert.equal(messages.find(m => m.type === 'export-complete').frames.length, 1);
  console.log('PASS: sibling selection, batch export, preview selection, invalid roots, snapshot, concurrency and failure recovery');
} finally {
  hooks.deregister();
  delete globalThis.__figforgeTest;
  delete globalThis.__html__;
  delete globalThis.figma;
}
