# FigForge

Figma → ZIP → Unity uGUI Prefab.

## Workflow

1. Install the Figma plugin from `plugin/manifest.json` after building `plugin/`.
2. Select a frame and choose **Download Frame**, or choose **Download Page** for all page frames.
3. Install the Unity package from `unity/package.json`.
4. Open **Window → FigForge → Importer**, choose **Import a .zip...**, configure fonts and the prefab folder, then generate prefabs.

A frame ZIP contains `manifest.json` and PNG assets. A page ZIP contains `project.json` and a bundle for each frame. Each frame produces its own `.prefab`; page generation rejects duplicate sanitized frame filenames.

PNG files are stored once per SHA-256 content hash under `Imgs/AltasImgs/` (both dimensions below 512 pixels) or `Imgs/RawImgs/` (either dimension at least 512 pixels), using actual exported PNG dimensions. Frame manifests reference `Imgs/...`; page bundles share one root `Imgs/` beside `project.json`, with each screen manifest referencing `../Imgs/...`. References include nested control parts and state sprites. Deduplication applies within one downloaded ZIP; separately downloaded ZIPs remain self-contained.

The importer preserves anchors, text, fills, strokes, raster assets and canonical controls. Generated C# accessors are attached and wired to prefab assets after Unity imports the scripts. Existing output prefabs with the same filename are replaced; keep hand-authored changes in variants or separate assets.

Generation uses a temporary preview scene and closes it after saving. It does not assemble a scene, camera, event system or cross-page navigation. Place the resulting prefabs under your application's Canvas and configure navigation there. Blend compositing requires a suitable camera-backed Canvas at runtime.

## Components

- `plugin/`: Figma editing tools, export data and ZIP packaging.
- `unity/`: ZIP importer, uGUI hierarchy builder, prefab output and runtime controls.
- `docs/`: [Plugin guide](docs/plugin-guide.md), [canonical controls](docs/canonical-elements.md), [architecture](docs/architecture.md), [releases](docs/releasing.md).

The plugin uses JSZip from cdnjs to package downloads. Local bridge connections and UI Toolkit output are no longer provided.

## Development

In `plugin/`, use `npm ci`, `npm run typecheck` and `npm run build` to produce `dist/main.js` and `dist/ui.html`. The Unity package targets Unity 2022.3+; dependencies are declared in `unity/package.json`.

`node scripts/check-canonical-schema.mjs` checks the shared manifest contract without compiling.
