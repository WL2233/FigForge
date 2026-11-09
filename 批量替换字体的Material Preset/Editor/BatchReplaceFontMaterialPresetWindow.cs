using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace BatchReplaceFontMaterialPreset
{
    public sealed class BatchReplaceFontMaterialPresetWindow : EditorWindow
    {
        TMP_FontAsset _font;
        readonly List<Material> _materialPresets = new List<Material>();
        int _oldPresetIndex = -1;
        int _newPresetIndex = -1;
        DefaultAsset _prefabFolder;
        string _result;
        MessageType _resultType = MessageType.Info;

        [MenuItem("Window/批量替换字体的Material Preset")]
        public static void Open()
        {
            var window = GetWindow<BatchReplaceFontMaterialPresetWindow>();
            window.titleContent = new GUIContent("批量替换Material Preset");
            window.minSize = new Vector2(620f, 250f);
            window.Show();
        }

        void OnGUI()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("批量替换字体的 Material Preset", EditorStyles.boldLabel);
            EditorGUILayout.Space(6f);

            EditorGUI.BeginChangeCheck();
            var nextFont = (TMP_FontAsset)EditorGUILayout.ObjectField("字体", _font, typeof(TMP_FontAsset), false);
            if (EditorGUI.EndChangeCheck()) SetFont(nextFont);

            using (new EditorGUI.DisabledScope(_font == null || _materialPresets.Count == 0))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                    _oldPresetIndex = PresetPopup("旧 Material Preset", _oldPresetIndex);
                GUILayout.Space(12f);
                using (new EditorGUILayout.VerticalScope())
                    _newPresetIndex = PresetPopup("新 Material Preset", _newPresetIndex);
            }

            if (_font != null && _materialPresets.Count == 0)
                EditorGUILayout.HelpBox("未找到与该字体 Atlas 关联的 Material Preset。", MessageType.Warning);

            EditorGUILayout.Space(6f);
            _prefabFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Prefab 文件夹", _prefabFolder, typeof(DefaultAsset), false);

            EditorGUILayout.Space(10f);
            if (GUILayout.Button("批量替换", GUILayout.Height(32f))) ReplaceAll();

            if (!string.IsNullOrEmpty(_result))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(_result, _resultType);
            }
        }

        int PresetPopup(string label, int index)
        {
            if (_materialPresets.Count == 0)
            {
                EditorGUILayout.Popup(label, 0, new[] { "请先选择字体" });
                return -1;
            }

            index = Mathf.Clamp(index, 0, _materialPresets.Count - 1);
            return EditorGUILayout.Popup(label, index, _materialPresets.Select(m => m.name).ToArray());
        }

        void SetFont(TMP_FontAsset font)
        {
            _font = font;
            _materialPresets.Clear();
            _oldPresetIndex = -1;
            _newPresetIndex = -1;
            _result = null;

            if (_font == null) return;

            var atlas = _font.atlasTexture;
            if (_font.material != null) _materialPresets.Add(_font.material);

            foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material == null || material == _font.material) continue;
                if (!material.HasProperty(ShaderUtilities.ID_MainTex)) continue;
                if (material.GetTexture(ShaderUtilities.ID_MainTex) == atlas) _materialPresets.Add(material);
            }

            _materialPresets.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            if (_materialPresets.Count > 0)
            {
                _oldPresetIndex = 0;
                _newPresetIndex = Mathf.Min(1, _materialPresets.Count - 1);
            }
        }

        void ReplaceAll()
        {
            _result = null;
            if (!TryGetSelection(out var oldPreset, out var newPreset, out var folderPath)) return;

            var prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .ToArray();

            int changedPrefabs = 0;
            int changedTexts = 0;

            try
            {
                for (int i = 0; i < prefabPaths.Length; i++)
                {
                    var path = prefabPaths[i];
                    EditorUtility.DisplayProgressBar("批量替换 Material Preset", path,
                        prefabPaths.Length == 0 ? 1f : (float)i / prefabPaths.Length);

                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        int prefabChanges = 0;
                        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                        {
                            if (text.font != _font || text.fontSharedMaterial != oldPreset) continue;
                            text.fontSharedMaterial = newPreset;
                            EditorUtility.SetDirty(text);
                            prefabChanges++;
                        }

                        if (prefabChanges == 0) continue;
                        if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                            throw new System.IO.IOException($"保存 Prefab 失败：{path}");
                        changedPrefabs++;
                        changedTexts += prefabChanges;
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }

                AssetDatabase.SaveAssets();
                _result = $"扫描 Prefab：{prefabPaths.Length}\n修改 Prefab：{changedPrefabs}\n替换文本组件：{changedTexts}";
                _resultType = MessageType.Info;
                Debug.Log($"[Material Preset 批量替换] {_result.Replace("\n", "，")}");
            }
            catch (System.Exception ex)
            {
                _result = "批量替换失败：" + ex.Message;
                _resultType = MessageType.Error;
                Debug.LogException(ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        bool TryGetSelection(out Material oldPreset, out Material newPreset, out string folderPath)
        {
            oldPreset = null;
            newPreset = null;
            folderPath = _prefabFolder != null ? AssetDatabase.GetAssetPath(_prefabFolder) : null;

            if (_font == null) return ValidationError("请先选择字体。");
            if (_oldPresetIndex < 0 || _oldPresetIndex >= _materialPresets.Count)
                return ValidationError("请选择旧 Material Preset。");
            if (_newPresetIndex < 0 || _newPresetIndex >= _materialPresets.Count)
                return ValidationError("请选择新 Material Preset。");
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return ValidationError("请选择 Assets 目录下的 Prefab 文件夹。");

            oldPreset = _materialPresets[_oldPresetIndex];
            newPreset = _materialPresets[_newPresetIndex];
            if (oldPreset == newPreset) return ValidationError("旧、新 Material Preset 不能相同。");
            return true;
        }

        bool ValidationError(string message)
        {
            _result = message;
            _resultType = MessageType.Warning;
            return false;
        }
    }
}
