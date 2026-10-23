# FigForge Unity Importer

Install this UPM package, then open **Window → FigForge → Importer**.

Input uses Unity's built-in Input Manager; this package does not install or reference the newer Input System package. Use **Active Input Handling → Input Manager (Old)** (or **Both** if your project already needs both backends). Modal Escape-to-close requires the legacy Input Manager to be enabled.

1. Import a ZIP exported by the Figma plugin.
2. Configure fonts, texture options and prefab output folder.
3. Generate a single frame prefab, or select a page bundle and generate all frame prefabs.

Outputs are uGUI `.prefab` assets. Matching output filenames are overwritten on reimport. The importer builds in a temporary preview scene and closes it after saving; it does not assemble or update your open scene.

The importer does not generate C# accessor scripts or a Canonical Library asset. Pages retain their base FigForgeFrame, container components, runtime element registry and navigation. Controls are built directly inside each page prefab. No Canonical folder, intermediate control prefabs, signature cache or library lookup is created. The exported control definitions are still consumed to preserve visuals, state transitions and bindings.

导入窗口提供三个独立的根目录，可输入路径或点击“选择…”（须位于 Assets 内），点击 Apply 保存设置，Reset 恢复默认值：

| 产物 | 默认根目录 | 实际输出 |
| --- | --- | --- |
| AltasImgs 图集图 | `Assets/AssetRaw/UIRaw/Atlas` | `<根目录>/<ZIP 内根节点名>/<图片文件名>` |
| RawImgs 原图 | `Assets/AssetRaw/UIRaw/Raw` | `<根目录>/<ZIP 内根节点名>/<图片文件名>` |
| 预制体 | `Assets/AssetRaw/UI` | `<根目录>/<ZIP 内根节点名>/<页面名>.prefab` |

支持 `<根节点>/AltasImgs/`、`<根节点>/RawImgs/` 与 `<根节点>/<页面>/manifest.json` 的 ZIP 格式，不要求包含 project.json；导入时会生成本地页面索引，以便批量生成。包裹文件夹使用 ZIP 内的根节点名，不使用 ZIP 文件名或页面名。图片在生成预制体时复制到对应输出目录，预制体引用这些输出图片。多个页面共享同一包的图片目录。旧版无分类图片仍以宽高均小于 512 为 AltasImgs，否则归入 RawImgs。已有旧输出不会自动迁移或删除，重新生成后请检查引用。

手动验收：导入含“背包”和“商店”的 ZIP（将 ZIP 文件改名，内部根节点保持不变），分别验证默认目录和三个自定义目录；执行全部生成，确认三类输出均只追加内部根节点名，且两份预制体的普通图片与状态图片引用来自对应 Atlas/Raw 输出目录。重复生成应保留图片 GUID；开启导入资源清理后，预制体图片引用仍应有效。EditMode 的 ZipImporterTests 覆盖路径校验和无 project.json 的多页面 ZIP 索引。

When upgrading an existing Unity project, previously generated scripts and library assets are not deleted automatically. Rebuild affected page prefabs with this importer and migrate application calls to the runtime APIs before removing old Generated files and the old library asset. Old Canonical prefabs are not deleted automatically; rebuild every page that references them before removing those assets. Custom prefab mappings from the removed library are no longer supported.

Place prefabs under your own Canvas and supply your application's navigation and EventSystem. Camera-based blend compositing requires an appropriately configured Canvas/camera.

The importer uses ZIP bundles only; no live-import server or token is required. Sprite atlas generation is not supported. The Window → FigForge menu exposes only Importer. Popular-glyph pre-baking, orphan-sprite cleanup and live-page rebuilding are removed. The importer provides 检查页面引用 and 填充页面引用 for loaded pages; the same reference tools remain available in component inspectors.
