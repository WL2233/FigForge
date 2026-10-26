# Unity 插件使用教程（小白版）

这篇教程带你把 Figma 导出的 ZIP 变成 Unity 中可以使用的 uGUI 界面预制体。示例使用包含“背包”和“商店”的 `CommonUI.zip`。

本文按当前项目 1.0.59 编写，包声明的最低 Unity 版本为 **2022.3**。如果还没有界面 ZIP，先看 [Figma 插件使用教程（小白版）](figma-beginner-guide.md)。

## 1. 先分清两个压缩包

| 文件 | 用途 |
| --- | --- |
| `figforge-unity-importer-1.0.59.zip` | 安装 Unity 插件，只需安装一次 |
| `CommonUI.zip` | Figma 导出的界面数据，设计更新后可以重新导入 |

**Prefab（预制体）**可以理解为保存好的界面模板。生成后，将它放到场景的 Canvas 下，才能在你的游戏界面中使用。

整体流程：**安装插件 → 导入界面 ZIP → 配置字体与目录 → 生成 Prefab → 放入场景检查**。

## 2. 安装 Unity 插件

1. 打开 Unity 工程。
2. 找到项目 `dist` 中的 `figforge-unity-importer-1.0.59.zip`，完整解压到长期保留的目录。
3. 在解压目录里找到 `package.json`，同级应有 `Editor`、`Runtime` 等文件夹。
4. 在 Unity 中打开 **Window → Package Manager**。
5. 点击 **＋ → Add package from disk…**，选择这个 `package.json`。
6. 等待 Unity 解析依赖、完成脚本处理；安装完成后，打开 **Window → FigForge → Importer**。

如果直接使用本项目源码，也可以选择项目内的 `unity/package.json`。本地包路径需要持续存在，安装后不要删除或移动它。

看到 FigForge 导入窗口就可以继续。若菜单没有出现，先看 **Window → General → Console** 是否存在红色脚本错误；工程中其他脚本出错也可能影响插件使用。

## 3. 导入 Figma 的界面 ZIP

1. 在导入窗口点击 **Import a .zip...**。
2. 选择 Figma 导出的 `CommonUI.zip`，不要选 Unity 插件安装包。
3. 等待导入，查看 **Manifest** 下拉框是否出现“背包”等页面。
4. 多页面包还会出现在 **Page bundle** 下拉框，供后续批量生成。

无需手动解压界面 ZIP。如果列表没有更新，点击 **Rescan** 重新扫描。

若弹出 **Replace existing import?**，表示同名导入目录已经有内容。确认这次是更新该包后选择 **Replace**；它会清理该导入目录的旧内容并重新解压。暂时不想替换时选 **Cancel**。

## 4. 配置文字字体

普通文字会使用 TextMeshPro。Figma 记录的字体名称，需要对应到 Unity 的 **TMP_FontAsset**，不能直接把普通 `.ttf` 文件当作这个资源填进去。

1. 在 **Manifest** 中选中“背包”。
2. 展开 **Fonts**，查看页面使用的字体和字重，例如某字体的 Regular、Bold。
3. 如果项目已经有合适的 TMP 字体资源，将它拖入对应字段，也可以点击字段右侧的资源选择按钮。
4. 没有字体资源时，将可使用的 `.ttf` 或 `.otf` 字体放入工程的 Assets 目录，然后打开 **Window → TextMeshPro → Font Asset Creator**。
5. 选择源字体，设置需要的字符范围，生成并保存字体资源，再回到导入窗口完成映射。
6. 如果 Unity 提示缺少 TMP Essential Resources，先按提示导入基础资源。

中文页面必须使用包含对应中文字形的字体，并让生成的字体资源覆盖实际文字。看到方块或缺字时，先检查字形覆盖；仅仅填入一个英文字体不能解决中文显示问题。

多页面导入时，逐个选择 Manifest 检查字体，尤其是不同页面使用了不同字体或字重的情况。Figma 中已转为 PNG 的文字不会作为可编辑文字出现。

## 5. 设置文件保存位置

窗口中的“导入根目录”有三项。第一次可以保留默认值：

| 字段 | 默认根目录 | 示例输出 |
| --- | --- | --- |
| 图集图 | `Assets/AssetRaw/UIRaw/Atlas` | `Assets/AssetRaw/UIRaw/Atlas/CommonUI/图片.png` |
| 原图 | `Assets/AssetRaw/UIRaw/Raw` | `Assets/AssetRaw/UIRaw/Raw/CommonUI/图片.png` |
| 预制体 | `Assets/AssetRaw/UI` | `Assets/AssetRaw/UI/CommonUI/背包.prefab` |

需要改位置时，直接输入路径或点击“选择…”，目标必须在当前工程的 **Assets 内**。点击 **Apply** 保存设置，**Reset** 恢复默认值。

这里的 `CommonUI` 来自 Figma 导出时填写的“文件夹名”，也就是 ZIP 内部根节点名。只把外面的 `CommonUI.zip` 改名，并不会改变这个输出子目录。

“图集图”是图片分类目录，插件不会自动创建 Sprite Atlas。修改输出目录也不会自动搬迁或删除旧输出。

## 6. 生成第一个预制体

1. 在 **Manifest** 选中“背包”。
2. 确认字体映射和输出目录。
3. 展开 **Prefab output**，第一次将 **Scale** 保持为 **MatchFigma**，其他选项先保持默认。
4. **Textures** 中先保留默认设置，之后再根据图片清晰度调整。
5. 第一次建议不勾选 **生成成功后删除导入资源**，方便返回检查或重新生成。
6. 点击 **Generate Prefab "背包"**。
7. 查看下方 **Forge log**；如果有错误，先按错误提示处理。
8. 在 Project 窗口打开 `Assets/AssetRaw/UI/CommonUI`，检查是否生成了“背包”预制体。

生成操作使用临时预览场景，保存后会关闭它；**它不会自动把界面放进你当前打开的场景**。

“生成成功后删除导入资源”会删除整个 `Assets/FigForge/Imports` 文件夹，包含其他导入包；批量生成时要求全部成功才执行。需要保留导入数据继续调整时，请保持关闭。图片会在生成时复制到配置的输出目录。

## 7. 一次生成多个页面

1. 先逐个选择 Manifest，检查各页面的字体映射。
2. 在 **Page bundle** 中选择这次导入的包。
3. 点击 **Generate Prefabs (all frames)**。
4. 检查输出目录中是否同时出现“背包”和“商店”的 `.prefab`。

每个导出根节点生成一个独立预制体。同名输出文件会被替换，因此不要把需要保留的手工修改只放在自动生成的预制体里；可以使用预制体变体或单独保存业务资源。

## 8. 把界面放进场景

1. 打开要展示 UI 的场景。
2. 如果场景没有 Canvas，通过 **GameObject → UI → Canvas** 创建。
3. 将生成的“背包”Prefab 从 Project 拖到 Hierarchy 中的 Canvas 下。
4. 确认场景中有 EventSystem，Canvas 上有 Graphic Raycaster，交互控件才能正常接收 UI 输入。
5. 在 Game 窗口选择与设计接近的宽高比，运行后检查位置、文字、图片和点击表现。

插件包使用旧 Input Manager。可在 **Edit → Project Settings → Player → Other Settings → Active Input Handling** 选择 **Input Manager (Old)**；原工程需要新旧输入同时工作时使用 **Both**。修改后按 Unity 提示重启编辑器。弹窗按 Escape 关闭需要启用旧 Input Manager。

需要 FigForge 运行时合成器的相机混合效果不会保留在纯原生预制体中，导入时会展平或降级。

插件负责生成界面结构和可映射的 Unity/TMP 原生控件。保存的预制体不包含 FigForge 运行时组件；FigForge 专用的导航、引用表、绑定、复杂状态和自定义图形会降级为最接近的静态原生 UI 表现。按钮要打开哪个页面、如何读取背包数据、购买逻辑等业务行为，仍需在工程中接入。当前导入器不会生成 C# 访问脚本，也不需要设置实时连接服务器或 token。

## 9. 设计更新后怎么操作

1. 在 Figma 中重新导出 ZIP，尽量保持内部文件夹名和页面名稳定。
2. 回到 Unity，重新点击 **Import a .zip...**。
3. 如果确实要替换这次导入数据，确认 **Replace**。
4. 检查字体和输出设置，重新生成对应页面或全部页面。
5. 回到场景，检查文字、图片引用、控件以及自己的业务接入是否正常。

## 10. 常见问题

| 现象 | 先这样检查 |
| --- | --- |
| 找不到 FigForge 菜单 | 确认本地包安装成功、路径还在，并先解决 Console 红色错误 |
| ZIP 导入后没有页面 | 确认是 Figma 导出的界面 ZIP；点击 Rescan，查看错误信息 |
| 提示 No TMP_FontAsset found | 先创建并保存 TMP 字体资源，再完成 Fonts 映射 |
| 中文显示方块 | 检查源字体和 TMP 字体资源是否包含这些中文字形 |
| 找不到生成的 Prefab | 查看“预制体”根目录下面的 ZIP 内部根节点文件夹，并检查 Forge log |
| Prefab 生成了但 Game 看不到 | 将它放到场景 Canvas 下，检查激活状态、尺寸与 Canvas 设置 |
| 按钮看得到但点不了 | 检查 EventSystem、输入设置、Graphic Raycaster、遮挡和业务事件绑定 |
| 图片不清楚 | 检查 Figma 导出比例，以及 Textures 的 Max size、Compression 是否降低了质量 |
| 改了 ZIP 文件名，输出目录却没变 | 输出子目录取自 ZIP 内部根节点名，需要在 Figma 的“文件夹名”处修改 |
| 重新生成后手工修改没了 | 同名生成文件会覆盖；将需要保留的修改放在变体或独立资源中 |

完成后至少检查：预制体存在、图片引用有效、中文显示正常、场景中可见，以及需要交互的控件能按预期工作。

本文经过当前源码和界面文案静态核对，未执行 Unity 编译或编辑器运行验收。
