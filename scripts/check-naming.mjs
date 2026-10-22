// Run directly with Node >=22.18; no build required.
import assert from 'node:assert/strict';
import { exportName, fileSafeName, generateFileName } from '../plugin/src/naming.ts';
import { DEFAULT_EXPORT_OPTIONS } from '../plugin/src/types.ts';
import { AssetBundle } from '../plugin/src/asset-bundle.ts';

assert.equal(DEFAULT_EXPORT_OPTIONS.cleanNames, false);
for (const name of ['首页', '确认按钮', 'My Button', ' 中文 Mixed_Name ']) {
  assert.equal(exportName(name), name);
  assert.equal(exportName(name, false), name);
}
assert.equal(exportName('My Button', true), 'my_button');
assert.equal(exportName('确认按钮', true), 'node');
assert.equal(generateFileName('首页', '确认按钮'), '首页_确认按钮.png');
assert.equal(generateFileName('Main Screen', 'My Button', true), 'main_screen_my_button.png');
assert.equal(generateFileName('Main Screen', 'My Button', false), 'Main Screen_My Button.png');
assert.equal(fileSafeName('中文/图标:大'), '中文_图标_大');
assert.equal(fileSafeName('..'), '_');

// Verify Unicode survives asset packaging and references still resolve.
const bytes = new Uint8Array(25);
bytes.set([137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82]);
const view = new DataView(bytes.buffer);
view.setUint32(16, 32);
view.setUint32(20, 32);
const file = generateFileName('首页', '确认按钮');
const bundle = new AssetBundle();
const result = JSON.parse(await bundle.manifest(JSON.stringify({
  assets: [{ file }], elements: [{ name: '确认按钮', asset: file }],
}), [{ name: file, data: bytes }]));
assert.equal(result.elements[0].name, '确认按钮');
assert.equal(result.elements[0].asset, `Imgs/AltasImgs/${file}`);
assert.ok(bundle.files.has(result.elements[0].asset));
console.log('Naming checks passed.');
