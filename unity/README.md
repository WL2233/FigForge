# FigForge Unity Importer

Install this UPM package, then open **Window → FigForge → Importer**.

Input uses Unity's built-in Input Manager; this package does not install or reference the newer Input System package. Use **Active Input Handling → Input Manager (Old)** (or **Both** if your project already needs both backends). Modal Escape-to-close requires the legacy Input Manager to be enabled.

1. Import a ZIP exported by the Figma plugin.
2. Configure fonts, texture options and prefab output folder.
3. Generate a single frame prefab, or select a page bundle and generate all frame prefabs.

Outputs are uGUI `.prefab` assets. Matching output filenames are overwritten on reimport. The importer builds in a temporary preview scene and closes it after saving; it does not assemble or update your open scene.

The importer does not generate C# accessor scripts or a Canonical Library asset. Pages retain their base FigForgeFrame, container components, runtime element registry and navigation. Controls are built directly inside each page prefab. No Canonical folder, intermediate control prefabs, signature cache or library lookup is created. The exported control definitions are still consumed to preserve visuals, state transitions and bindings.

Sprites are copied to Assets/FigForge/Sprites/<screen>/AltasImgs or RawImgs, preserving the bundle category and export filename. Legacy bundles without categories use AltasImgs only when both PNG dimensions are below 512; other PNGs use RawImgs. Existing flat sprites are moved through AssetDatabase when possible to preserve their GUIDs.

When upgrading an existing Unity project, previously generated scripts and library assets are not deleted automatically. Rebuild affected page prefabs with this importer and migrate application calls to the runtime APIs before removing old Generated files and the old library asset. Old Canonical prefabs are not deleted automatically; rebuild every page that references them before removing those assets. Custom prefab mappings from the removed library are no longer supported.

Place prefabs under your own Canvas and supply your application's navigation and EventSystem. Camera-based blend compositing requires an appropriately configured Canvas/camera.

The importer uses ZIP bundles only; no live-import server or token is required. Sprite atlas generation is not supported. The Window → FigForge menu exposes only Importer. Popular-glyph pre-baking, orphan-sprite cleanup and live-page rebuilding are removed. The importer provides 检查页面引用 and 填充页面引用 for loaded pages; the same reference tools remain available in component inspectors.
