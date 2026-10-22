import type { BinaryAsset } from './types';

/** Shared image store: hashes deduplicate content, filenames retain export names. */
export class AssetBundle {
  readonly files = new Map<string, Uint8Array>();
  private readonly hashToPath = new Map<string, string>();
  // Unity imports into a flat folder; reserve names across both image folders.
  private readonly usedNames = new Set<string>();

  async manifest(json: string, assets: BinaryAsset[], prefix = ''): Promise<string> {
    const paths = new Map<string, string>();
    for (const asset of assets) {
      const bytes = new Uint8Array(asset.data);
      const signature = [137, 80, 78, 71, 13, 10, 26, 10];
      if (bytes.length < 24 || !signature.every((b, i) => bytes[i] === b) ||
          String.fromCharCode(...bytes.slice(12, 16)) !== 'IHDR') {
        throw new Error(`Invalid PNG: ${asset.name}`);
      }
      const view = new DataView(bytes.buffer);
      const width = view.getUint32(16), height = view.getUint32(20);
      if (!width || !height) throw new Error(`Empty PNG: ${asset.name}`);
      const hash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)))
        .map(b => b.toString(16).padStart(2, '0')).join('');
      const folder = width < 512 && height < 512 ? 'AltasImgs' : 'RawImgs';
      let path = this.hashToPath.get(hash);
      if (!path) {
        const name = asset.name.replace(/\\/g, '/').split('/').pop()!;
        const stem = name.replace(/\.png$/i, '') || 'image';
        let file = `${stem}.png`;
        let suffix = 2;
        while (this.usedNames.has(file.toLowerCase())) file = `${stem}_${suffix++}.png`;
        this.usedNames.add(file.toLowerCase());
        path = `Imgs/${folder}/${file}`;
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
