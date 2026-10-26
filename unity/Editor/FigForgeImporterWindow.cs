// =============================================================================
// FigForge — importer editor window. Window ▸ FigForge ▸ Importer.
//
// Detects FigForge manifests in the project, lets you configure canvas / fonts /
// textures / canonical library, and saves uGUI prefab assets.
// =============================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FigForge
{
    public class FigForgeImporterWindow : EditorWindow
    {
        internal const int DefaultWarmUpBatchSize = 256;
        const int DefaultEditorColumns = 5;
        const string PrefWarmUpBatchSize = "FigForge.ImportWarmUpBatchSize";
        const string PrefEditorColumns = "FigForge.EditorColumns";
        internal static int WarmUpBatchSizePref
            => Mathf.Clamp(EditorPrefs.GetInt(PrefWarmUpBatchSize, DefaultWarmUpBatchSize), 1, 8192);
        internal static int EditorColumnsPref
            => Mathf.Clamp(EditorPrefs.GetInt(PrefEditorColumns, DefaultEditorColumns), 1, 50);
        internal static void SetEditorColumnsPref(int columns)
            => EditorPrefs.SetInt(PrefEditorColumns, Mathf.Clamp(columns, 1, 50));

        // ---- discovered manifests ----
        List<string> _manifestPaths = new List<string>();
        int _selected = 0;
        Manifest _manifest;
        List<string> _projectPaths = new List<string>();
        int _selectedProject = 0;

        // ---- config ----
        enum ScalePreset { MatchFigma, P720, P1080, Custom }


        ScalePreset _scalePreset = ScalePreset.MatchFigma;
        float _customRefHeight = 1080f;
        bool _disableRaycasts = true;
        bool _deleteImportsAfterBuild;
        const string ImportsFolder = "Assets/FigForge/Imports";
        int _warmUpBatchSize = DefaultWarmUpBatchSize;
        int _editorColumns = DefaultEditorColumns;
        const string DefaultAtlasFolder = "Assets/AssetRaw/UIRaw/Atlas";
        const string DefaultRawFolder = "Assets/AssetRaw/UIRaw/Raw";
        const string DefaultPrefabFolder = "Assets/AssetRaw/UI";
        string _atlasFolder = DefaultAtlasFolder;
        string _rawFolder = DefaultRawFolder;
        string _prefabFolder = DefaultPrefabFolder;

        TextureImportSettings _tex = new TextureImportSettings();

        // font mapping: "family|style" → TMP asset
        readonly Dictionary<string, TMP_FontAsset> _fontMap = new Dictionary<string, TMP_FontAsset>();
        List<TMP_FontAsset> _projectFonts = new List<TMP_FontAsset>();

        // ---- ui state ----
        Vector2 _scroll, _logScroll;
        readonly List<(string msg, MessageType kind)> _log = new List<(string, MessageType)>();
        bool _showCanvas, _showFonts, _showTextures;

        WindowStyles _styles;

        static readonly Color Background = new Color(0.055f, 0.065f, 0.075f);
        static readonly Color Panel = new Color(0.105f, 0.115f, 0.125f);
        static readonly Color PanelSoft = new Color(0.13f, 0.145f, 0.155f);
        static readonly Color Border = new Color(0.24f, 0.27f, 0.28f);
        static readonly Color Accent = new Color(0.17f, 0.86f, 0.33f);
        static readonly Color AccentDim = new Color(0.1f, 0.44f, 0.22f);

        [MenuItem("Window/FigForge/Importer")]
        public static void Open()
        {
            var w = GetWindow<FigForgeImporterWindow>();
            w.titleContent = new GUIContent("FigForge");
            w.minSize = new Vector2(760, 560);
            w.Show();
        }

        // A saved snapshot is local to this user and Unity project.
        string SettingsKey => "FigForge.Importer.Settings.v1." + Application.dataPath;

        sealed class SavedSettings
        {
            public ScalePreset scalePreset = ScalePreset.MatchFigma;
            public float customRefHeight = 1080f;
            public bool disableRaycasts = true;
            public bool deleteImportsAfterBuild;
            public int warmUpBatchSize = DefaultWarmUpBatchSize;
            public int editorColumns = DefaultEditorColumns;
            public string atlasFolder = DefaultAtlasFolder;
            public string rawFolder = DefaultRawFolder;
            public string prefabFolder = DefaultPrefabFolder;
            public TextureImportSettings tex = new TextureImportSettings();
            public Dictionary<string, string> fonts = new Dictionary<string, string>();
        }

        static string ObjectId(Object value) => value == null ? null : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        static T ResolveObject<T>(string value) where T : Object
            => !string.IsNullOrEmpty(value) && GlobalObjectId.TryParse(value, out var id) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as T : null;

        void SaveSettings(bool notify = true)
        {
            try
            {
                var settings = new SavedSettings
                {
                    scalePreset = _scalePreset,
                    customRefHeight = _customRefHeight,
                    disableRaycasts = _disableRaycasts,
                    deleteImportsAfterBuild = _deleteImportsAfterBuild,
                    warmUpBatchSize = _warmUpBatchSize,
                    editorColumns = _editorColumns,
                    atlasFolder = _atlasFolder,
                    rawFolder = _rawFolder,
                    prefabFolder = _prefabFolder,
                    tex = _tex,
                    fonts = _fontMap.ToDictionary(pair => pair.Key, pair => ObjectId(pair.Value)),
                };
                EditorPrefs.SetString(SettingsKey, JsonConvert.SerializeObject(settings));
                if (notify) ShowNotification(new GUIContent("当前设置已保存"));
            }
            catch (System.Exception ex) { Log("保存设置失败：" + ex.Message, MessageType.Error); }
        }

        void LoadSettings()
        {
            if (!EditorPrefs.HasKey(SettingsKey))
            {
                // Capture legacy preferences once so an unsaved reset cannot
                // become the next startup configuration via shared EditorPrefs.
                SaveSettings(false);
                return;
            }
            try
            {
                var settings = JsonConvert.DeserializeObject<SavedSettings>(EditorPrefs.GetString(SettingsKey));
                if (settings == null) throw new System.InvalidOperationException("设置内容为空");
                ApplySettings(settings);
            }
            catch (System.Exception ex) { Log("读取设置失败：" + ex.Message, MessageType.Warning); }
        }

        void ApplySettings(SavedSettings settings)
        {
            _scalePreset = settings.scalePreset;
            _customRefHeight = settings.customRefHeight;
            _disableRaycasts = settings.disableRaycasts;
            _deleteImportsAfterBuild = settings.deleteImportsAfterBuild;
            _warmUpBatchSize = settings.warmUpBatchSize;
            _editorColumns = settings.editorColumns;
            _atlasFolder = string.IsNullOrWhiteSpace(settings.atlasFolder) ? DefaultAtlasFolder : settings.atlasFolder;
            _rawFolder = string.IsNullOrWhiteSpace(settings.rawFolder) ? DefaultRawFolder : settings.rawFolder;
            _prefabFolder = string.IsNullOrWhiteSpace(settings.prefabFolder) || settings.prefabFolder == "Assets/FigForge/Prefabs"
                ? DefaultPrefabFolder : settings.prefabFolder;
            _tex = settings.tex ?? new TextureImportSettings();
            _warmUpBatchSize = Mathf.Clamp(_warmUpBatchSize, 1, 8192);
            _editorColumns = Mathf.Clamp(_editorColumns, 1, 50);
            EditorPrefs.SetInt(PrefWarmUpBatchSize, _warmUpBatchSize);
            SetEditorColumnsPref(_editorColumns);
            _fontMap.Clear();
            if (settings.fonts != null)
                foreach (var pair in settings.fonts)
                {
                    var font = ResolveObject<TMP_FontAsset>(pair.Value);
                    if (font != null) _fontMap[pair.Key] = font;
                }
            if (_manifest != null) BuildFontKeys();
        }

        void SettingsBar()
        {
            using (new EditorGUILayout.HorizontalScope(_styles.card))
            {
                if (GUILayout.Button("Reset", _styles.button, GUILayout.Height(26)))
                {
                    ApplySettings(new SavedSettings());
                    ShowNotification(new GUIContent("已恢复默认设置；点击 Apply 以供下次使用"));
                }
                if (GUILayout.Button("Apply", _styles.primaryButton, GUILayout.Height(26))) SaveSettings();
            }
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            _warmUpBatchSize = WarmUpBatchSizePref;
            _editorColumns = EditorColumnsPref;
            LoadSettings();
            RefreshManifests();
            RefreshFonts();
        }

        // Destroy the GUIStyle backing textures WindowStyles created. _styles is rebuilt
        // by EnsureStyles after each domain reload, so without this the prior instance's
        // HideAndDontSave textures leak as orphaned native objects. Null it so a later
        // OnGUI rebuilds fresh styles rather than touching disposed textures.
        void OnDisable()
        {
            _styles?.Dispose();
            _styles = null;
        }

        void RefreshManifests()
        {
            _manifestPaths = Directory
                .GetFiles(Application.dataPath, "manifest.json", SearchOption.AllDirectories)
                .Select(p => "Assets" + p.Substring(Application.dataPath.Length).Replace('\\', '/'))
                .Where(IsFigForgeManifest)
                .ToList();
            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _manifestPaths.Count - 1));

            _projectPaths = Directory
                .GetFiles(Application.dataPath, "project.json", SearchOption.AllDirectories)
                .Select(p => "Assets" + p.Substring(Application.dataPath.Length).Replace('\\', '/'))
                .Where(p => HasSchemaMarker(p, "figforge/project"))
                .ToList();
            _selectedProject = Mathf.Clamp(_selectedProject, 0, Mathf.Max(0, _projectPaths.Count - 1));

            RefreshFonts(); // keep _projectFonts current so BuildFontKeys/GuessFont never hit stale refs
            LoadSelected();
        }

        // Only FigForge manifests carry the "figforge/manifest" schema marker.
        // Requiring it keeps the scan from trying to parse foreign/old-schema
        // manifest.json files in the project (which throw and spam the log).
        static bool IsFigForgeManifest(string assetPath) => HasSchemaMarker(assetPath, "figforge/manifest");

        // The schema marker sits in the file head (it's the first/second JSON key the
        // plugin emits), so read only a bounded prefix instead of slurping the whole
        // file. RefreshManifests runs on every OnEnable/ImportZip and
        // scans the entire Assets tree — full ReadAllText on every manifest.json /
        // project.json (some carry large inlined data) made that needlessly heavy.
        const int SchemaProbeBytes = 8 * 1024;
        static bool HasSchemaMarker(string assetPath, string marker)
        {
            try
            {
                var buffer = new char[SchemaProbeBytes];
                using (var reader = new StreamReader(assetPath))
                {
                    int read = reader.Read(buffer, 0, buffer.Length);
                    return read > 0 && new string(buffer, 0, read).Contains(marker);
                }
            }
            catch { return false; }
        }

        void LoadSelected()
        {
            if (_manifestPaths.Count == 0) { _manifest = null; return; }
            _manifest = ManifestParser.Load(_manifestPaths[_selected]);
            if (_manifest != null) BuildFontKeys();
        }

        void FocusFontsFoldout()
        {
            _showCanvas = false;
            _showFonts = true;
            _showTextures = false;
        }

        void RefreshFonts()
        {
            _projectFonts = AssetDatabase.FindAssets("t:TMP_FontAsset")
                .Select(g => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(f => f != null).ToList();
        }

        void BuildFontKeys()
        {
            foreach (var f in _manifest.fonts)
                foreach (var s in f.styles)
                {
                    var key = $"{f.family}|{s}";
                    if (!_fontMap.ContainsKey(key)) _fontMap[key] = GuessFont(f.family, s);
                }
        }

        static void ApplyManifestSettings(Manifest manifest)
        {
            var dilate = manifest != null && manifest.settings != null
                ? manifest.settings.fontFaceDilate
                : FontAutoImporter.DefaultFontFaceDilate;
            FontAutoImporter.FaceDilate = Mathf.Clamp(dilate, 0f, 1f);
        }

        // Explicit assignment in the Fonts section wins; otherwise auto-import a
        // matching font (project → OS) and build a TMP asset for it.
        TMP_FontAsset ResolveFontAsset(string family, string style)
        {
            var key = $"{family}|{style}";
            if (_fontMap.TryGetValue(key, out var a) && a != null) return a;

            var resolved = FontAutoImporter.Resolve(family, style, m => Log(m, MessageType.Info));
            if (resolved != null) _fontMap[key] = resolved;
            return resolved;
        }

        TMP_FontAsset GuessFont(string family, string style)
        {
            // Only a confident family+weight match prefills the Fonts section; a
            // miss returns null so it falls through to FontAutoImporter (tiered
            // matching + can generate the exact weight). The old family-only /
            // first-available fallbacks were harmful — they cached e.g. a Regular
            // face for an "Extra Bold" request and shadowed the smart resolver.
            var fam = FontKey(family);
            var sty = FontKey(style);
            if (fam == "") return null;
            return _projectFonts.FirstOrDefault(f =>
            {
                if (f == null) return false; // guard stale/destroyed refs (fonts deleted since the last refresh)
                var n = FontKey(f.name);
                if (!n.Contains(fam)) return false;
                return sty == "" || sty == "regular" ? n.Contains("regular") : n.Contains(sty);
            });
        }

        static string FontKey(string s) =>
            new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        // -----------------------------------------------------------------------
        void OnGUI()
        {
            EnsureStyles();
            if (Event.current != null && Event.current.type == EventType.MouseMove) Repaint();
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), Background);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            Header();
            SettingsBar();
            OutputFoldersSection();
            ManifestPicker();
            if (_manifest != null)
            {
                CanvasSection();
                FontSection();
                TextureSection();
                BuildBar();
            }
            LogSection();

            EditorGUILayout.EndScrollView();
        }

        // of the loaded manifest, so it's available even without a manifest selected.
        void Header()
        {
            using (new EditorGUILayout.VerticalScope(_styles.hero))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope())
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label("FIGFORGE", _styles.heroTitle);
                            GUILayout.Label("->", _styles.versionArrow, GUILayout.Width(18), GUILayout.Height(20));
                            GUILayout.Label($"v{PackageVersion()}", _styles.versionPill, GUILayout.Width(60), GUILayout.Height(18));
                            GUILayout.FlexibleSpace();
                        }
                        GUILayout.Label("Forge Figma frames into Unity UI, export ZIP bundles and generate prefab assets.", _styles.heroSubtitle);
                    }

                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Rescan", _styles.primaryButton, GUILayout.Width(92), GUILayout.Height(30)))
                    { RefreshManifests(); RefreshFonts(); }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    SummaryCard("Page bundles", _projectPaths.Count.ToString(), "project.json", new Color(0.45f, 0.55f, 1f));
                    SummaryCard("Manifests", _manifestPaths.Count.ToString(), "single screens", new Color(0.82f, 0.54f, 1f));
                    SummaryCard("Elements", (_manifest?.elements.Count ?? 0).ToString(), _manifest?.screen?.name ?? "none loaded", Accent);
                    SummaryCard("Sprites", (_manifest?.assets.Count ?? 0).ToString(), "texture assets", new Color(0.28f, 0.75f, 1f));
                }
            }
        }

        string PackageVersion()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(FigForgeImporterWindow).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.version)) return info.version;

            var packageJson = FindPackageJsonPath();
            if (!string.IsNullOrEmpty(packageJson))
            {
                try
                {
                    var meta = JsonConvert.DeserializeObject<PackageJsonMeta>(File.ReadAllText(packageJson));
                    if (meta != null && !string.IsNullOrEmpty(meta.version)) return meta.version;
                }
                catch { /* version label is cosmetic; keep the importer usable */ }
            }

            return "dev";
        }

        static string FindPackageJsonPath()
        {
            foreach (var guid in AssetDatabase.FindAssets("FigForge.Editor"))
            {
                var asmdefPath = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(asmdefPath) != "FigForge.Editor.asmdef") continue;
                var dir = Path.GetDirectoryName(asmdefPath);
                if (string.IsNullOrEmpty(dir)) continue;
                var candidate = Path.Combine(dir, "..", "package.json").Replace('\\', '/');
                if (File.Exists(candidate)) return candidate;
            }

            const string packagePath = "Packages/com.figforge.unity-importer/package.json";
            return File.Exists(packagePath) ? packagePath : null;
        }

        sealed class PackageJsonMeta { public string version; }

        void ManifestPicker()
        {
            using (new EditorGUILayout.VerticalScope(_styles.card))
            {
                EditorGUILayout.LabelField("Import source", _styles.sectionTitle);

                // Whole-page project bundles (project.json) -> Forge Page.
                if (_projectPaths.Count > 0)
                {
                    _selectedProject = EditorGUILayout.Popup("Page bundle", _selectedProject,
                        _projectPaths.Select(p => Path.GetFileName(Path.GetDirectoryName(p)) + " / project.json").ToArray());
                    if (ForgePageButton("Generate Prefabs (all frames)",
                        _styles.pageForgeNormal, _styles.pageForgeHover, _styles.pageForgeActive, 38f))
                        BuildPageProject(_projectPaths[_selectedProject]);
                    EditorGUILayout.Space(4);
                    EditorGUILayout.Space(5);
                }

                // Import straight from a FigForge export .zip - no manual unzip.
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Import a .zip...", _styles.button, GUILayout.Height(26)))
                        ImportZip();
                    if (_manifestPaths.Count > 0 &&
                        GUILayout.Button("Reveal folder", _styles.button, GUILayout.Width(110), GUILayout.Height(26)))
                        EditorUtility.RevealInFinder(_manifestPaths[_selected]);
                }

                if (_manifestPaths.Count == 0)
                {
                    EditorGUILayout.HelpBox("Import a FigForge export .zip above, drop an extracted folder under Assets/, then press Rescan.", MessageType.Info);
                    return;
                }

                EditorGUI.BeginChangeCheck();
                _selected = EditorGUILayout.Popup("Manifest", _selected,
                    _manifestPaths.Select(p => Path.GetFileName(Path.GetDirectoryName(p)) + " / manifest.json").ToArray());
                if (EditorGUI.EndChangeCheck()) LoadSelected();

                if (_manifest != null)
                    EditorGUILayout.LabelField(
                        $"{_manifest.screen?.name}  |  {_manifest.elements.Count} elements  |  {_manifest.assets.Count} sprites  |  scale {_manifest.screen?.exportScale}x",
                        _styles.subtleLabel);
            }
        }

        void ImportZip()
        {
            var zip = EditorUtility.OpenFilePanel("Select a FigForge export (.zip)", "", "zip");
            if (string.IsNullOrEmpty(zip)) return;

            var dest = $"{ImportsFolder}/{SafeName(Path.GetFileNameWithoutExtension(zip))}";

            // A prior import into the same folder is merged into, not replaced — same-named
            // files are overwritten but stale assets from the old import linger silently.
            // If the destination already holds an import, confirm before clearing it so the
            // result is exactly the zip's contents. Fresh/empty folder → no prompt.
            if (AssetDatabase.IsValidFolder(dest) &&
                Directory.EnumerateFileSystemEntries(
                    Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                        dest.Replace('/', Path.DirectorySeparatorChar))).Any())
            {
                if (!EditorUtility.DisplayDialog("Replace existing import?",
                    $"Folder \"{dest}\" already has an import — replace its contents?\n\nStale files from the previous import will be removed so the result matches this zip.",
                    "Replace", "Cancel"))
                    return;
                AssetDatabase.DeleteAsset(dest);
            }

            var manifestPath = ZipImporter.ExtractToAssets(zip, dest);
            if (string.IsNullOrEmpty(manifestPath)) return;

            RefreshManifests();
            var projectIdx = _projectPaths.IndexOf(manifestPath);
            if (projectIdx >= 0) _selectedProject = projectIdx;
            var idx = _manifestPaths.IndexOf(manifestPath);
            if (projectIdx >= 0)
            {
                var project = ManifestParser.LoadProject(manifestPath);
                var first = project?.screens?.FirstOrDefault();
                if (first != null)
                {
                    var firstPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath), first.manifest));
                    idx = _manifestPaths.FindIndex(p => string.Equals(Path.GetFullPath(p), firstPath,
                        System.StringComparison.OrdinalIgnoreCase));
                }
            }
            if (idx >= 0) { _selected = idx; LoadSelected(); }
            FocusFontsFoldout();
        }

        void OutputFoldersSection()
        {
            using (new EditorGUILayout.VerticalScope(_styles.card))
            {
                EditorGUILayout.LabelField("导入根目录", _styles.sectionTitle);
                _atlasFolder = FolderField("图集图", _atlasFolder);
                _rawFolder = FolderField("原图", _rawFolder);
                _prefabFolder = FolderField("预制体", _prefabFolder);
                EditorGUILayout.LabelField("实际输出：所选根目录 / ZIP 内根节点名称 / 文件", _styles.subtleLabel);
            }
        }

        string FolderField(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                value = EditorGUILayout.TextField(label, value);
                if (GUILayout.Button("选择…", _styles.button, GUILayout.Width(60)))
                {
                    var selected = EditorUtility.OpenFolderPanel(label + "根目录", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        var relative = FileUtil.GetProjectRelativePath(selected);
                        try { value = ZipImporter.NormalizeAssetsFolder(relative); }
                        catch (System.Exception ex) { Log(ex.Message, MessageType.Error); }
                    }
                }
            }
            return value;
        }

        void CanvasSection()
        {
            using (new EditorGUILayout.VerticalScope(_styles.card))
            {
                _showCanvas = Foldout(_showCanvas, "Prefab output");
                if (!_showCanvas) return;

                _scalePreset = (ScalePreset)EditorGUILayout.EnumPopup("Scale", _scalePreset);
                if (_scalePreset == ScalePreset.Custom) _customRefHeight = EditorGUILayout.FloatField("Reference height", _customRefHeight);
                _disableRaycasts = EditorGUILayout.ToggleLeft("Disable decorative raycasts", _disableRaycasts);
                _warmUpBatchSize = Mathf.Clamp(EditorGUILayout.IntField("Warmup batch size", _warmUpBatchSize), 1, 8192);
            }
        }

        void FontSection()
        {
            using (new EditorGUILayout.VerticalScope(_styles.card))
            {
                _showFonts = Foldout(_showFonts, $"Fonts ({_manifest.fonts.Count})");
                if (!_showFonts) return;
                using (new EditorGUI.IndentLevelScope())
                {
                    if (_projectFonts.Count == 0)
                        EditorGUILayout.HelpBox("No TMP_FontAsset found. Create font assets (Window > TextMeshPro > Font Asset Creator).", MessageType.Warning);
                    foreach (var f in _manifest.fonts)
                        foreach (var s in f.styles)
                        {
                            var key = $"{f.family}|{s}";
                            _fontMap.TryGetValue(key, out var cur);
                            var next = (TMP_FontAsset)EditorGUILayout.ObjectField(key, cur, typeof(TMP_FontAsset), false);
                            _fontMap[key] = next;
                        }
                }
            }
        }

        void TextureSection()
        {
            using (new EditorGUILayout.VerticalScope(_styles.card))
            {
                _showTextures = Foldout(_showTextures, "Textures");
                if (!_showTextures) return;
                using (new EditorGUI.IndentLevelScope())
                {

                    _tex.autoMaxSize = EditorGUILayout.ToggleLeft("Auto max size", _tex.autoMaxSize, _styles.toggle);
                    if (!_tex.autoMaxSize) _tex.maxSize = EditorGUILayout.IntField("Max size", _tex.maxSize);
                    _tex.compression = (TextureImporterCompression)EditorGUILayout.EnumPopup("Compression", _tex.compression);
                }
            }
        }

        void BuildBar()
        {
            using (new EditorGUILayout.VerticalScope(_styles.buildCard))
            {
                if (GUILayout.Button($"Generate Prefab \"{_manifest.screen?.name}\"", _styles.primaryButton, GUILayout.Height(34)))
                    Build();
                _deleteImportsAfterBuild = EditorGUILayout.ToggleLeft(new GUIContent(
                    "生成成功后删除导入资源",
                    "生成成功后删除整个 Assets/FigForge/Imports 文件夹（含其他导入包）；批量生成须全部成功。"),
                    _deleteImportsAfterBuild, _styles.toggle);
            }
        }

        void LogSection()
        {
            if (_log.Count == 0) return;
            using (new EditorGUILayout.VerticalScope(_styles.card))
            {
                EditorGUILayout.LabelField("Forge log", _styles.sectionTitle);
                _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(120));
                foreach (var (msg, kind) in _log) EditorGUILayout.HelpBox(msg, kind);
                EditorGUILayout.EndScrollView();
            }
        }

        // -----------------------------------------------------------------------
        void Build()
        {
            _log.Clear();
            if (_manifest?.screen == null) return;
            try
            {
                var path = BuildPrefab(_manifest, Path.GetDirectoryName(_manifestPaths[_selected]), "");
                AssetDatabase.SaveAssets();
                DeleteImportsAfterSuccess(new[] { path });
            }
            catch (System.Exception ex) { Debug.LogException(ex); Log(ex.Message, MessageType.Error); }
            finally { EditorUtility.ClearProgressBar(); AssetDatabase.SaveAssets(); }
        }

        string BuildPrefab(Manifest manifest, string sourceDir, string section, string role = "screen")
        {
            FontAutoImporter.ClearCache();
            var bundleName = Path.GetFileName(ZipImporter.BundleRoot(sourceDir));
            var atlasFolder = ZipImporter.NormalizeAssetsFolder(_atlasFolder) + "/" + bundleName;
            var rawFolder = ZipImporter.NormalizeAssetsFolder(_rawFolder) + "/" + bundleName;
            var prefabFolder = ZipImporter.NormalizeAssetsFolder(_prefabFolder) + "/" + bundleName;
            var sprites = TextureImportHelper.Import(manifest, sourceDir, atlasFolder, rawFolder, _tex);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            try
            {
                var host = new GameObject("FigForge Prefab Build", typeof(RectTransform), typeof(Canvas));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
                var ctx = MakeContext(manifest, sprites);
                host.GetComponent<RectTransform>().sizeDelta = new Vector2(
                    manifest.screen.figmaSize.w * ctx.scaleFactor,
                    manifest.screen.figmaSize.h * ctx.scaleFactor);
                var page = HierarchyBuilder.BuildPage(manifest, host.transform, ctx);
                if (page == null) throw new System.InvalidOperationException("No prefab hierarchy was produced.");
                var frame = page.GetComponent<FigForgeFrame>() ?? page.AddComponent<FigForgeFrame>();
                frame.isShell = FrameRoles.IsShell(role);
                frame.shellKey = frame.isShell ? section ?? "" : "";
                bool isOverlay = FrameRoles.IsOverlay(role);
                if (isOverlay && page.GetComponent<FigForgeOverlayLayer>() == null)
                    page.AddComponent<FigForgeOverlayLayer>();
                RegisterFrameElements(page, manifest, ctx, isOverlay);
                TextureImportHelper.EnsureFolder(prefabFolder);
                var path = $"{prefabFolder}/{SafeName(manifest.screen.name)}.prefab";
                var saved = PrefabUtility.SaveAsPrefabAsset(page, path);
                if (saved == null) throw new System.IO.IOException($"Could not save prefab: {path}");
                Log($"saved prefab → {path}", MessageType.Info);
                return path;
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene); }
        }

        // ---- Whole-page (project bundle) build ---------------------------------
        struct LoadedScreen { public Manifest m; public string srcDir; public ProjectScreen ps;  }

        BuildContext MakeContext(Manifest m, Dictionary<string, Sprite> sprites)
        {
            ApplyManifestSettings(m);
            float fh = m.screen != null && m.screen.figmaSize != null ? m.screen.figmaSize.h : 1080f;
            float sf = fh > 0 ? ReferenceHeight(fh) / fh : 1f;
            return new BuildContext
            {
                scaleFactor = sf, sprites = sprites, disableRaycasts = _disableRaycasts,
                warmUpBatchSize = _warmUpBatchSize,
                exportScale = m.screen != null ? m.screen.exportScale : 1f,
                vanilla = m.vanilla,
                resolveFont = ResolveFontAsset,
                log = mm => Log(mm, MessageType.Warning),
            };
        }

        // Keep runtime lookup keys and overlay discovery without generating scripts.
        static void RegisterFrameElements(GameObject page, Manifest manifest, BuildContext ctx, bool isOverlay)
        {
            var registry = page.GetComponent<FigForgeScreen>() ?? page.AddComponent<FigForgeScreen>();
            string rootId = manifest.elements.Find(e => e != null && string.IsNullOrEmpty(e.parentId))?.id;
            var elements = new List<ElementData>();
            var names = new List<string>();
            foreach (var e in manifest.elements)
            {
                if (e == null || e.id == rootId) continue;
                bool member = IdentifierUtil.HasSerializeMarker(e.displayName)
                    || (e.canonical != null && !string.IsNullOrEmpty(e.canonical.kind))
                    || e.type == "GROUP" || e.type == "FRAME";
                if (!member) continue;
                elements.Add(e);
                names.Add(IdentifierUtil.ToIdentifier(IdentifierUtil.StripSerializeMarker(
                    !string.IsNullOrEmpty(e.displayName) ? e.displayName : e.name)));
            }
            var keys = IdentifierUtil.Dedupe(names, null, IdentifierUtil.ReservedAccessorNames);
            for (int i = 0; i < elements.Count; i++)
            {
                var e = elements[i];
                if (string.IsNullOrEmpty(e.id) || !ctx.byElementId.TryGetValue(e.id, out var go) || go == null) continue;
                registry.Register(keys[i].TrimStart('@'), go);
                if (isOverlay)
                {
                    var modal = go.GetComponent<FigForgeModal>();
                    if (modal != null) modal.dialogKey = e.name ?? "";
                }
            }
        }

        bool BuildPageProject(string projectPath)
        {
            _log.Clear();
            FontAutoImporter.ClearCache();
            _editorColumns = EditorColumnsPref;
            var proj = ManifestParser.LoadProject(projectPath);
            if (proj == null || proj.screens.Count == 0) { Log("project.json is empty or invalid", MessageType.Error); return false; }
            var baseDir = Path.GetDirectoryName(projectPath).Replace('\\', '/');

            var loaded = new List<LoadedScreen>();
            // baseDir is trusted (the folder we were pointed at); ps.manifest comes from
            // project.json and is untrusted. Resolve to a full path and require it to stay
            // under baseDir so a crafted "../../.." value can't read an arbitrary file.
            var baseDirFull = Path.GetFullPath(baseDir);
            foreach (var ps in proj.screens)
            {
                var mp = $"{baseDir}/{ps.manifest}".Replace('\\', '/');
                var mpFull = Path.GetFullPath(mp);
                var fence = baseDirFull.EndsWith(Path.DirectorySeparatorChar.ToString())
                    ? baseDirFull : baseDirFull + Path.DirectorySeparatorChar;
                if (!mpFull.Equals(baseDirFull, System.StringComparison.Ordinal) &&
                    !mpFull.StartsWith(fence, System.StringComparison.Ordinal))
                {
                    Log($"skip '{ps.name}': manifest path escapes bundle folder — rejected ({ps.manifest})", MessageType.Warning);
                    continue;
                }
                var m = ManifestParser.Load(mp);
                if (m == null) { Log($"skip '{ps.name}': manifest missing or rejected — see Console ({mp})", MessageType.Warning); continue; }
                loaded.Add(new LoadedScreen
                {
                    m = m,
                    srcDir = Path.GetDirectoryName(mp),
                    ps = ps,
                });
            }
            if (loaded.Count == 0) { Log("no buildable screens in bundle", MessageType.Error); return false; }

            var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var screen in loaded)
                if (!names.Add(SafeName(screen.m.screen.name)))
                { Log("Duplicate prefab filename in page bundle; rename the frames before exporting.", MessageType.Error); return false; }
            bool ok = loaded.Count == proj.screens.Count;
            var prefabPaths = new List<string>();
            foreach (var screen in loaded)
            {
                try { prefabPaths.Add(BuildPrefab(screen.m, screen.srcDir, screen.ps.section, screen.ps.role)); }
                catch (System.Exception ex) { ok = false; Debug.LogException(ex); Log(ex.Message, MessageType.Error); }
            }
            AssetDatabase.SaveAssets();
            if (ok) DeleteImportsAfterSuccess(prefabPaths.ToArray());
            return ok;
        }

        static bool IsInsideImports(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var root = Path.GetFullPath(ImportsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full.Equals(root, System.StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(root + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase);
        }

        void DeleteImportsAfterSuccess(string[] prefabPaths)
        {
            if (!_deleteImportsAfterBuild || !AssetDatabase.IsValidFolder(ImportsFolder)) return;
            try
            {
                // A configurable output or a manually selected font may still live in Imports.
                // Include recursive dependencies so deleting sources cannot break the saved prefabs.
                if (IsInsideImports(_prefabFolder) || IsInsideImports(_atlasFolder) || IsInsideImports(_rawFolder) ||
                    AssetDatabase.GetDependencies(prefabPaths, true).Any(IsInsideImports))
                {
                    Log("预制体已生成，但输出目录或资源依赖仍位于 Imports 内，已保留导入资源。", MessageType.Warning);
                    return;
                }
                if (!AssetDatabase.DeleteAsset(ImportsFolder))
                {
                    Log("预制体已生成，但删除 Imports 失败，请检查文件占用或权限。", MessageType.Warning);
                    return;
                }
                Log("已删除导入资源：" + ImportsFolder, MessageType.Info);
                RefreshManifests();
                Repaint();
            }
            catch (System.Exception ex)
            {
                Log("预制体已生成，但清理导入资源失败：" + ex.Message, MessageType.Warning);
            }
        }

        float ReferenceHeight(float figmaH)
        {
            switch (_scalePreset)
            {
                case ScalePreset.P720: return 720f;
                case ScalePreset.P1080: return 1080f;
                case ScalePreset.Custom: return _customRefHeight;
                default: return figmaH;
            }
        }

        void Log(string msg, MessageType kind) => _log.Add((msg, kind));
        static string SafeName(string s) => new string((s ?? "Screen").Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        void SummaryCard(string title, string value, string caption, Color color)
        {
            var rect = GUILayoutUtility.GetRect(120, 68, GUILayout.ExpandWidth(true), GUILayout.Height(68));
            EditorGUI.DrawRect(rect, Panel);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Border);

            GUI.Label(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, 16), title, _styles.metricLabel);
            GUI.Label(new Rect(rect.x + 10, rect.y + 27, rect.width - 20, 22), value, _styles.metricValue);
            GUI.Label(new Rect(rect.x + 10, rect.y + 50, rect.width - 20, 14), caption, _styles.metricCaption);
        }

        void EnsureStyles()
        {
            _styles ??= new WindowStyles();
        }
        bool Foldout(bool state, string label) => EditorGUILayout.Foldout(state, label, true, _styles.foldout);
        bool ForgePageButton(string label, GUIStyle normal, GUIStyle hover, GUIStyle active, float height)
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.Height(height));
            int id = GUIUtility.GetControlID("FigForgeForgePageButton".GetHashCode(), FocusType.Passive, rect);
            bool enabled = GUI.enabled;
            bool hot = enabled && rect.Contains(Event.current.mousePosition);
            bool held = enabled && GUIUtility.hotControl == id && hot;
            var style = enabled ? (held ? active : hot ? hover : normal) : _styles.forgeButtonDisabled;
            if (Event.current.type == EventType.Repaint)
            {
                if (!held) GUI.Box(new Rect(rect.x, rect.y + 2f, rect.width, rect.height), GUIContent.none, _styles.forgeButtonShadow);
                GUI.Box(rect, GUIContent.none, style);
                var labelRect = held ? new Rect(rect.x, rect.y + 1f, rect.width, rect.height) : rect;
                using (new EditorGUI.DisabledScope(!enabled))
                    GUI.Label(labelRect, label, enabled ? _styles.forgeButtonLabel : _styles.forgeButtonDisabledLabel);
            }

            var e = Event.current;
            if (!enabled || e == null) return false;
            if (e.type == EventType.MouseDown && e.button == 0 && hot)
            {
                GUIUtility.hotControl = id;
                Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                Repaint();
                e.Use();
                return hot;
            }
            return false;
        }

        void Divider()
        {
            var r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(1, 1, 1, 0.1f));
            EditorGUILayout.Space(2);
        }

        sealed class WindowStyles
        {
            // Every Texture2D MakeTexture/MakeButtonTexture/MakeForgeButtonTexture builds is
            // collected here so Dispose() can DestroyImmediate them. They use
            // HideFlags.HideAndDontSave (never serialized), so without an explicit destroy they
            // leak as orphaned native textures each time _styles is rebuilt (e.g. domain reload).
            readonly List<Texture2D> _textures = new List<Texture2D>();
            // Instance field initializers below can't call instance methods (CS0236), so the
            // static Make* helpers stash each created texture here; the constructor (which runs
            // after all field initializers) takes ownership into _textures so Dispose() can
            // destroy them. The editor is single-threaded, so this scratch list is safe.
            static readonly List<Texture2D> s_pending = new List<Texture2D>();

            public WindowStyles()
            {
                _textures.AddRange(s_pending);
                s_pending.Clear();
            }

            public readonly GUIStyle hero = new GUIStyle
            {
                padding = new RectOffset(16, 16, 14, 12),
                margin = new RectOffset(8, 8, 8, 8),
                normal = { background = MakeTexture(new Color(0.075f, 0.087f, 0.095f)) }
            };

            public readonly GUIStyle heroTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                normal = { textColor = Color.white }
            };

            public readonly GUIStyle heroSubtitle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.68f, 0.74f, 0.76f) },
                wordWrap = true
            };

            public readonly GUIStyle card = new GUIStyle
            {
                padding = new RectOffset(14, 14, 10, 12),
                margin = new RectOffset(8, 8, 0, 8),
                normal = { background = MakeTexture(Panel) }
            };

            public readonly GUIStyle buildCard = new GUIStyle
            {
                padding = new RectOffset(14, 14, 12, 12),
                margin = new RectOffset(8, 8, 0, 8),
                normal = { background = MakeTexture(new Color(0.075f, 0.087f, 0.095f)) }
            };

            public readonly GUIStyle foldout = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                onNormal = { textColor = Color.white },
                focused = { textColor = Color.white },
                onFocused = { textColor = Color.white }
            };

            public readonly GUIStyle sectionTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.88f, 0.93f, 0.94f) }
            };

            public readonly GUIStyle subtleLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.55f, 0.62f, 0.64f) },
                wordWrap = true
            };

            public readonly GUIStyle toggle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0.76f, 0.82f, 0.84f) },
                onNormal = { textColor = Color.white }
            };

            public readonly GUIStyle button = new GUIStyle(EditorStyles.miniButton)
            {
                border = new RectOffset(7, 7, 7, 7),
                padding = new RectOffset(10, 10, 4, 4),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.82f, 0.9f, 0.9f), background = MakeButtonTexture(new Color(0.15f, 0.17f, 0.18f), new Color(0.12f, 0.14f, 0.15f), new Color(0.25f, 0.29f, 0.3f)) },
                hover = { textColor = Color.white, background = MakeButtonTexture(new Color(0.23f, 0.27f, 0.28f), new Color(0.16f, 0.19f, 0.2f), new Color(0.34f, 0.39f, 0.4f)) },
                active = { textColor = Color.white, background = MakeButtonTexture(new Color(0.1f, 0.12f, 0.13f), new Color(0.16f, 0.18f, 0.19f), new Color(0.19f, 0.23f, 0.24f)) }
            };

            public readonly GUIStyle primaryButton = new GUIStyle(EditorStyles.miniButton)
            {
                border = new RectOffset(7, 7, 7, 7),
                padding = new RectOffset(12, 12, 4, 4),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white, background = MakeButtonTexture(new Color(0.12f, 0.5f, 0.25f), AccentDim, new Color(0.2f, 0.72f, 0.36f)) },
                hover = { textColor = Color.white, background = MakeButtonTexture(new Color(0.2f, 0.76f, 0.38f), new Color(0.1f, 0.5f, 0.24f), new Color(0.28f, 0.9f, 0.46f)) },
                active = { textColor = Color.white, background = MakeButtonTexture(new Color(0.07f, 0.32f, 0.16f), new Color(0.12f, 0.5f, 0.25f), new Color(0.16f, 0.58f, 0.28f)) }
            };

            public readonly GUIStyle warningButton = new GUIStyle(EditorStyles.miniButton)
            {
                border = new RectOffset(7, 7, 7, 7),
                padding = new RectOffset(12, 12, 4, 4),
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white, background = MakeButtonTexture(new Color(0.5f, 0.22f, 0.14f), new Color(0.32f, 0.13f, 0.08f), new Color(0.7f, 0.32f, 0.2f)) },
                hover = { textColor = Color.white, background = MakeButtonTexture(new Color(0.74f, 0.31f, 0.2f), new Color(0.48f, 0.18f, 0.12f), new Color(0.94f, 0.43f, 0.27f)) },
                active = { textColor = Color.white, background = MakeButtonTexture(new Color(0.25f, 0.1f, 0.07f), new Color(0.42f, 0.16f, 0.1f), new Color(0.55f, 0.22f, 0.14f)) }
            };

            public readonly GUIStyle forgeButtonLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white }
            };

            public readonly GUIStyle forgeButtonDisabledLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(1f, 1f, 1f, 0.48f) }
            };

            public readonly GUIStyle pageForgeNormal = ForgeButtonStyle(new Color(0.28f, 0.6f, 0.38f), new Color(0.18f, 0.24f, 0.21f), new Color(0.46f, 0.5f, 0.48f));
            public readonly GUIStyle pageForgeHover = ForgeButtonStyle(new Color(0.36f, 0.72f, 0.48f), new Color(0.22f, 0.3f, 0.26f), new Color(0.56f, 0.62f, 0.58f));
            public readonly GUIStyle pageForgeActive = ForgeButtonStyle(new Color(0.13f, 0.24f, 0.18f), new Color(0.27f, 0.46f, 0.33f), new Color(0.34f, 0.38f, 0.36f));
            public readonly GUIStyle customForgeNormal = ForgeButtonStyle(new Color(0.46f, 0.43f, 0.68f), new Color(0.22f, 0.22f, 0.31f), new Color(0.48f, 0.49f, 0.56f));
            public readonly GUIStyle customForgeHover = ForgeButtonStyle(new Color(0.56f, 0.52f, 0.82f), new Color(0.27f, 0.26f, 0.39f), new Color(0.58f, 0.58f, 0.68f));
            public readonly GUIStyle customForgeActive = ForgeButtonStyle(new Color(0.18f, 0.17f, 0.28f), new Color(0.35f, 0.32f, 0.52f), new Color(0.36f, 0.36f, 0.43f));
            public readonly GUIStyle forgeButtonDisabled = ForgeButtonStyle(new Color(0.25f, 0.26f, 0.27f), new Color(0.16f, 0.17f, 0.18f), new Color(0.38f, 0.4f, 0.41f));
            public readonly GUIStyle forgeButtonShadow = ForgeButtonStyle(new Color(0f, 0f, 0f, 0.2f), new Color(0f, 0f, 0f, 0.28f), new Color(0f, 0f, 0f, 0f));

            public readonly GUIStyle miniButton = new GUIStyle(EditorStyles.miniButton)
            {
                border = new RectOffset(6, 6, 6, 6),
                normal = { textColor = new Color(0.78f, 0.84f, 0.86f), background = MakeButtonTexture(new Color(0.15f, 0.17f, 0.18f), new Color(0.11f, 0.13f, 0.14f), new Color(0.24f, 0.27f, 0.28f), 6) },
                hover = { textColor = Color.white, background = MakeButtonTexture(new Color(0.22f, 0.25f, 0.26f), new Color(0.15f, 0.18f, 0.19f), new Color(0.32f, 0.36f, 0.37f), 6) }
            };

            public readonly GUIStyle versionPill = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                border = new RectOffset(7, 7, 7, 7),
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(7, 7, 1, 1),
                normal =
                {
                    textColor = new Color(0.9f, 0.98f, 0.92f),
                    background = MakeButtonTexture(new Color(0.13f, 0.3f, 0.19f), new Color(0.08f, 0.18f, 0.12f), new Color(0.22f, 0.72f, 0.36f), 7)
                }
            };

            public readonly GUIStyle versionArrow = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 5, 0),
                normal = { textColor = Accent }
            };

            public readonly GUIStyle metricLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.62f, 0.7f, 0.72f) },
                clipping = TextClipping.Clip
            };

            public readonly GUIStyle metricValue = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 17,
                normal = { textColor = Color.white },
                clipping = TextClipping.Clip
            };

            public readonly GUIStyle metricCaption = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.45f, 0.52f, 0.54f) },
                clipping = TextClipping.Clip
            };

            static Texture2D MakeTexture(Color color)
            {
                var texture = new Texture2D(1, 1)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.SetPixel(0, 0, color);
                texture.Apply();
                s_pending.Add(texture);
                return texture;
            }

            static Texture2D MakeButtonTexture(Color top, Color bottom, Color border, int radius = 7)
            {
                const int size = 32;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };

                float innerRadius = Mathf.Max(0, radius - 1);
                for (int y = 0; y < size; y++)
                {
                    float t = 1f - y / (float)(size - 1);
                    Color fill = Color.Lerp(top, bottom, t);
                    for (int x = 0; x < size; x++)
                    {
                        float d = RoundedRectDistance(x + 0.5f, y + 0.5f, size, size, radius);
                        if (d > 1f)
                        {
                            texture.SetPixel(x, y, Color.clear);
                            continue;
                        }

                        Color c = d > 0f || RoundedRectDistance(x + 0.5f, y + 0.5f, size, size, innerRadius) > 0f
                            ? border
                            : fill;
                        c.a = Mathf.Clamp01(1f - Mathf.Max(0f, d));
                        texture.SetPixel(x, y, c);
                    }
                }

                texture.Apply();
                s_pending.Add(texture);
                return texture;
            }

            static GUIStyle ForgeButtonStyle(Color top, Color bottom, Color border)
            {
                return new GUIStyle
                {
                    border = new RectOffset(8, 8, 8, 8),
                    normal = { background = MakeForgeButtonTexture(top, bottom, border, 7) }
                };
            }

            static Texture2D MakeForgeButtonTexture(Color top, Color bottom, Color bevel, int radius = 7)
            {
                const int size = 32;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };

                float innerRadius = Mathf.Max(0, radius - 1);
                for (int y = 0; y < size; y++)
                {
                    float t = 1f - y / (float)(size - 1);
                    Color fill = Color.Lerp(top, bottom, t);
                    for (int x = 0; x < size; x++)
                    {
                        float d = RoundedRectDistance(x + 0.5f, y + 0.5f, size, size, radius);
                        if (d > 1f)
                        {
                            texture.SetPixel(x, y, Color.clear);
                            continue;
                        }

                        float innerD = RoundedRectDistance(x + 0.5f, y + 0.5f, size, size, innerRadius);
                        float edge = Mathf.Clamp01(innerD + 1f);
                        Color c = Color.Lerp(fill, bevel, edge * 0.28f);
                        c.a = Mathf.Clamp01(1f - Mathf.Max(0f, d));
                        texture.SetPixel(x, y, c);
                    }
                }

                texture.Apply();
                s_pending.Add(texture);
                return texture;
            }

            static float RoundedRectDistance(float x, float y, float width, float height, float radius)
            {
                float px = Mathf.Abs(x - width * 0.5f) - (width * 0.5f - radius);
                float py = Mathf.Abs(y - height * 0.5f) - (height * 0.5f - radius);
                float ax = Mathf.Max(px, 0f);
                float ay = Mathf.Max(py, 0f);
                return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(px, py), 0f) - radius;
            }

            // Destroy the native textures this instance created. Only our own
            // HideAndDontSave textures are tracked here, so this never touches shared
            // or asset textures. Guarded + cleared so a double-call is a no-op.
            public void Dispose()
            {
                foreach (var t in _textures)
                    if (t != null) DestroyImmediate(t);
                _textures.Clear();
            }
        }
    }
}
