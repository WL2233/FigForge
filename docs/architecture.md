# Architecture

`plugin/src/exporter.ts` produces the manifest and PNG bytes. `main.ts` owns Figma document access; `ui.ts` packages downloadable ZIP files with JSZip.

Unity `ZipImporter` extracts bundles into Assets. `ManifestParser` validates the shared contract. `FigForgeImporterWindow` imports textures/fonts, builds each frame through `HierarchyBuilder` in a temporary preview scene, and saves a prefab. The preview scene is always closed in a finally block.

Single-frame and page-bundle inputs use the same prefab builder. Page bundles preserve frame section metadata for code generation, but do not assemble navigation or a shared scene. Duplicate sanitized frame names are rejected before page generation.

`FrameCodeGenWire` queues saved prefab paths in Unity SessionState. Once generated types are available, it loads only those prefab contents, upgrades group/frame components, wires serialized references and saves the prefab. Unresolved types remain queued across script reloads during the editor session.

Runtime controls and rendering components remain available to the generated prefabs. Scene setup belongs to the consuming Unity project.
