# FigForge Unity Importer

Install this UPM package, then open **Window → FigForge → Importer**.

1. Import a ZIP exported by the Figma plugin.
2. Configure fonts, texture options, canonical library and prefab output folder.
3. Generate a single frame prefab, or select a page bundle and generate all frame prefabs.

Outputs are uGUI `.prefab` assets. Matching output filenames are overwritten on reimport. The importer builds in a temporary preview scene and closes it after saving; it does not assemble or update your open scene.

Generated accessor scripts require Unity's normal script import before typed prefab references are populated. The post-reload hook updates queued prefab assets directly.

Place prefabs under your own Canvas and supply your application's navigation and EventSystem. Camera-based blend compositing requires an appropriately configured Canvas/camera.
