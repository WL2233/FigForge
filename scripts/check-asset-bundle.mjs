// Run directly with Node >=22.18; no build or compiler required.
import assert from 'node:assert/strict';
import { AssetBundle } from '../plugin/src/asset-bundle.ts';

function png(width, height, marker = 0) {
  const bytes = new Uint8Array(25);
  bytes.set([137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82]);
  const view = new DataView(bytes.buffer);
  view.setUint32(16, width); view.setUint32(20, height);
  bytes[24] = marker;
  return bytes;
}
const store = new AssetBundle();
const small = png(511, 511);
const assets = [{ name: 'a.png', data: small }, { name: 'b.png', data: small.slice() }];
const json = JSON.stringify({ assets: assets.map(a => ({ file: a.name })), elements: [
  { name: 'a.png', asset: 'a.png', canonical: { iconAsset: 'b.png', states: { normal: 'b.png' },
    subtree: { elements: [{ fills: [{ asset: 'b.png' }] }] } } },
] });
const frame = JSON.parse(await store.manifest(json, assets));
assert.equal(store.files.size, 1);
assert.equal(frame.assets.length, 1);
const path = frame.assets[0].file;
assert.match(path, /^Imgs\/AltasImgs\/[a-f0-9]{64}\.png$/);
assert.equal(frame.elements[0].asset, path);
assert.equal(frame.elements[0].name, 'a.png');
assert.equal(frame.elements[0].canonical.iconAsset, path);
assert.equal(frame.elements[0].canonical.states.normal, path);
assert.equal(frame.elements[0].canonical.subtree.elements[0].fills[0].asset, path);
const page = JSON.parse(await store.manifest(json, assets, '../'));
assert.equal(page.assets[0].file, '../' + path);
assert.equal(store.files.size, 1);
for (const [w, h] of [[512, 1], [1, 512], [513, 511]]) {
  const result = JSON.parse(await store.manifest('{"assets":[{"file":"large.png"}]}', [{ name: 'large.png', data: png(w, h) }]));
  assert.match(result.assets[0].file, /^Imgs\/RawImgs\//);
}
await store.manifest('{"assets":[{"file":"different.png"}]}', [{ name: 'different.png', data: png(511, 511, 1) }]);
assert.equal(store.files.size, 5);
await assert.rejects(store.manifest(json, [{ name: 'bad.png', data: new Uint8Array(4) }]), /Invalid PNG/);
console.log('PASS: content deduplication, cross-screen paths, nested references, dimension boundaries, distinct content and invalid headers');
