// Run directly with Node >=22.18; no build or compiler required.
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { AssetBundle, sha256 } from '../plugin/src/asset-bundle.ts';

// Match Node's independent SHA-256 implementation at padding/block boundaries.
for (const length of [0, 1, 55, 56, 63, 64, 65, 119, 120, 128, 1000000]) {
  const bytes = Uint8Array.from({ length }, (_, i) => (i * 31 + 7) & 255);
  assert.equal(sha256(bytes), createHash('sha256').update(bytes).digest('hex'));
}
const backing = new Uint8Array([0, 97, 98, 99, 0]);
assert.equal(sha256(backing.subarray(1, 4)), 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad');

// Reproduce the Figma iframe environment: crypto exists but subtle is absent.
const cryptoDescriptor = Object.getOwnPropertyDescriptor(globalThis, 'crypto');
Object.defineProperty(globalThis, 'crypto', { configurable: true, value: {} });
try {

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
assert.equal(path, 'Imgs/AltasImgs/a.png');
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
// Same names with different content must not overwrite, even across screens/folders.
const collisionStore = new AssetBundle();
async function addNamed(name, data) {
  const result = JSON.parse(await collisionStore.manifest(
    JSON.stringify({ assets: [{ file: name }], elements: [{ asset: name }] }),
    [{ name, data }], '../'));
  assert.equal(result.elements[0].asset, result.assets[0].file);
  return result.assets[0].file;
}
assert.equal(await addNamed('Icon.png', small), '../Imgs/AltasImgs/Icon.png');
assert.equal(await addNamed('Icon.png', png(511, 511, 1)), '../Imgs/AltasImgs/Icon_2.png');
assert.equal(await addNamed('icon.png', png(512, 1)), '../Imgs/RawImgs/icon_3.png');
assert.equal(await addNamed('other.png', small.slice()), '../Imgs/AltasImgs/Icon.png');
assert.equal(collisionStore.files.size, 3);
assert.deepEqual(collisionStore.files.get('Imgs/AltasImgs/Icon.png'), small);
assert.deepEqual(collisionStore.files.get('Imgs/AltasImgs/Icon_2.png'), png(511, 511, 1));
await assert.rejects(store.manifest(json, [{ name: 'bad.png', data: new Uint8Array(4) }]), /Invalid PNG/);
console.log('PASS: content deduplication, cross-screen paths, nested references, dimension boundaries, distinct content and invalid headers');
// Also support environments without a crypto global at all.
Object.defineProperty(globalThis, 'crypto', { configurable: true, value: undefined });
assert.equal(await new AssetBundle().manifest(json, assets), JSON.stringify(frame, null, 2));
console.log('PASS: SHA-256 reference vectors and packaging without crypto.subtle or crypto');
} finally {
  if (cryptoDescriptor) Object.defineProperty(globalThis, 'crypto', cryptoDescriptor);
  else delete globalThis.crypto;
}
