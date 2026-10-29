import type { BinaryAsset } from './types';

const SHA256_K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
]);

/** SHA-256 without Web Crypto: the Figma UI may not expose crypto.subtle. */
export function sha256(bytes: Uint8Array): string {
  const state = new Uint32Array([
    0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
    0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19,
  ]);
  const words = new Uint32Array(64);
  const block = new Uint8Array(64);
  const view = new DataView(block.buffer);
  const paddedLength = Math.ceil((bytes.length + 9) / 64) * 64;
  const rotate = (x: number, n: number) => (x >>> n) | (x << (32 - n));
  for (let offset = 0; offset < paddedLength; offset += 64) {
    block.fill(0);
    block.set(bytes.subarray(offset, offset + 64));
    if (offset <= bytes.length && bytes.length < offset + 64) block[bytes.length - offset] = 0x80;
    if (offset + 64 === paddedLength) {
      view.setUint32(56, Math.floor(bytes.length / 0x20000000));
      view.setUint32(60, (bytes.length * 8) >>> 0);
    }
    for (let i = 0; i < 16; i++) words[i] = view.getUint32(i * 4);
    for (let i = 16; i < 64; i++) {
      const x = words[i - 15], y = words[i - 2];
      const s0 = rotate(x, 7) ^ rotate(x, 18) ^ (x >>> 3);
      const s1 = rotate(y, 17) ^ rotate(y, 19) ^ (y >>> 10);
      words[i] = words[i - 16] + s0 + words[i - 7] + s1;
    }
    let [a, b, c, d, e, f, g, h] = state;
    for (let i = 0; i < 64; i++) {
      const t1 = (h + (rotate(e, 6) ^ rotate(e, 11) ^ rotate(e, 25)) +
        ((e & f) ^ (~e & g)) + SHA256_K[i] + words[i]) >>> 0;
      const t2 = ((rotate(a, 2) ^ rotate(a, 13) ^ rotate(a, 22)) +
        ((a & b) ^ (a & c) ^ (b & c))) >>> 0;
      h = g; g = f; f = e; e = (d + t1) >>> 0;
      d = c; c = b; b = a; a = (t1 + t2) >>> 0;
    }
    const result = [a, b, c, d, e, f, g, h];
    for (let i = 0; i < 8; i++) state[i] += result[i];
  }
  return Array.from(state, word => word.toString(16).padStart(8, '0')).join('');
}

/** Hashes deduplicate images within a ZIP; filenames retain the first export name. */
export class AssetBundle {
  readonly files = new Map<string, Uint8Array>();
  private readonly hashToPath = new Map<string, string>();
  private readonly usedPaths = new Set<string>();

  async manifest(json: string, assets: BinaryAsset[], prefix = ''): Promise<string> {
    const paths = new Map<string, string>();
    for (const asset of assets) {
      // BinaryAsset already owns a Uint8Array; retaining it avoids duplicating every
      // PNG while the original frames, bundle and JSZip are alive simultaneously.
      const bytes = asset.data;
      const signature = [137, 80, 78, 71, 13, 10, 26, 10];
      if (bytes.length < 24 || !signature.every((b, i) => bytes[i] === b) ||
          String.fromCharCode(...bytes.slice(12, 16)) !== 'IHDR') {
        throw new Error(`Invalid PNG: ${asset.name}`);
      }
      const view = new DataView(bytes.buffer);
      const width = view.getUint32(16), height = view.getUint32(20);
      if (!width || !height) throw new Error(`Empty PNG: ${asset.name}`);
      const hash = sha256(bytes);
      const folder = width < 512 && height < 512 ? 'AltasImgs' : 'RawImgs';
      let path = this.hashToPath.get(hash);
      if (!path) {
        // The exporter already supplies a safe PNG filename. Strip any directory
        // component, but preserve its Unicode, case and extension. Only different
        // content claiming the same destination needs a numbered suffix.
        const name = asset.name.replace(/\\/g, '/').split('/').pop() || 'image.png';
        const extension = /\.png$/i.test(name) ? name.slice(-4) : '';
        const stem = extension ? name.slice(0, -4) : name;
        let file = name;
        let suffix = 2;
        while (this.usedPaths.has(`${folder}/${file}`.toLowerCase()))
          file = `${stem}_${suffix++}${extension}`;
        path = `${folder}/${file}`;
        this.usedPaths.add(path.toLowerCase());
        this.hashToPath.set(hash, path);
        this.files.set(path, bytes);
      }
      paths.set(asset.name, prefix + path);
    }
    const manifest = JSON.parse(json);
    // Rewrite references in nested canonical subtrees, fills and diagnostics too.
    const referenceKeys = new Set(['file', 'asset', 'iconAsset', 'arrowAsset', 'arrowRolloverAsset', 'arrowPressedAsset']);
    function rewrite(value: unknown, parent = ''): void {
      if (!value || typeof value !== 'object') return;
      for (const [key, child] of Object.entries(value)) {
        if (typeof child === 'string' && (referenceKeys.has(key) ||
            (parent === 'states' && ['normal', 'highlighted', 'pressed'].includes(key)))) {
          if (paths.has(child)) (value as Record<string, unknown>)[key] = paths.get(child);
        } else rewrite(child, key);
      }
    }
    rewrite(manifest);
    const seen = new Set<string>();
    manifest.assets = manifest.assets.filter((asset: { file: string }) => {
      if (seen.has(asset.file)) return false;
      seen.add(asset.file);
      return true;
    });
    return JSON.stringify(manifest, null, 2);
  }
}

export interface BundleFrame {
  manifest: string;
  assets: BinaryAsset[];
}

/** Validate the user-supplied directory as one portable path component. */
export function validateBundleName(name: string): string {
  if (!name || name !== name.trim() || /[<>:"/\\|?*\x00-\x1f]/.test(name) ||
      /[. ]$/.test(name) || /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(name)) {
    throw new Error('请输入有效的文件夹名：不能包含路径分隔符、特殊字符、首尾空格或系统保留名称。');
  }
  return name;
}

/** Build files before downloading: no partial ZIP on invalid/duplicate frame names. */
export async function createSharedBundle(name: string, frames: BundleFrame[]) {
  const root = validateBundleName(name);
  if (!frames.length) throw new Error('没有可导出的节点。');
  const used = new Set(['altasimgs', 'rawimgs']);
  const folders = frames.map(frame => {
    const screen = JSON.parse(frame.manifest).screen?.name;
    if (typeof screen !== 'string' || !screen) throw new Error('导出节点缺少名称。');
    const folder = screen.replace(/[<>:"/\\|?*\x00-\x1f]/g, '_').replace(/[. ]+$/g, '_').trim();
    validateBundleName(folder);
    if (used.has(folder.toLowerCase()))
      throw new Error(`预制体文件夹名重复或与资源目录冲突：${folder}。请重命名后重新导出。`);
    used.add(folder.toLowerCase());
    return folder;
  });
  const images = new AssetBundle();
  const files = new Map<string, string | Uint8Array>();
  for (const [i, frame] of frames.entries()) {
    files.set(`${root}/${folders[i]}/manifest.json`, await images.manifest(frame.manifest, frame.assets, '../'));
  }
  for (const [path, bytes] of images.files) files.set(`${root}/${path}`, bytes);
  return { files, directories: [`${root}/AltasImgs`, `${root}/RawImgs`], imageCount: images.files.size };
}
