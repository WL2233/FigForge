# 批量替换字体的 Material Preset

这是一个独立的 Unity Editor Package，用于批量替换 Prefab 中 TextMeshPro 文本的 Material Preset。

## 安装

在 Unity Package Manager 中选择 **Add package from disk**，然后选择本包的 `package.json`。

## 使用

1. 打开 **Window/批量替换字体的Material Preset**。
2. 选择 `TMP_FontAsset`。
3. 选择该字体关联的旧、新 Material Preset。
4. 选择 `Assets` 下的 Prefab 文件夹。
5. 点击“批量替换”。

工具会递归扫描目标文件夹下的 Prefab，仅替换字体和旧 Material Preset 同时匹配的 `TMP_Text`，并显示扫描和替换统计。
