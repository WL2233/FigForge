// =============================================================================
// FigForge — import a FigForge export .zip directly (no manual unzip).
//
// A FigForge export bundle contains JSON manifests and relative PNG paths.
// This preserves its directories in an Assets-relative folder, then the normal build
// pipeline (ManifestParser → TextureImportHelper → HierarchyBuilder) takes over.
//
// Uses System.IO.Compression.ZipArchive directly (stream-per-entry) so it does
// not depend on the System.IO.Compression.FileSystem extension assembly.
// =============================================================================

using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace FigForge
{
    public static class ZipImporter
    {
        internal static string NormalizeAssetsFolder(string folder)
        {
            var path = (folder ?? "").Replace('\\', '/').TrimEnd('/');
            if ((path != "Assets" && !path.StartsWith("Assets/", StringComparison.Ordinal)) ||
                path.Split('/').Any(p => string.IsNullOrWhiteSpace(p) || p == "." || p == ".." ||
                    p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new IOException("输出根目录必须位于 Assets 内：" + folder);
            return path;
        }

        internal static string BundleRoot(string sourceDir)
        {
            var source = Path.GetFullPath(sourceDir);
            var parent = Directory.GetParent(source);
            if (parent != null && (File.Exists(Path.Combine(parent.FullName, "project.json")) ||
                Directory.Exists(Path.Combine(parent.FullName, "AltasImgs")) ||
                Directory.Exists(Path.Combine(parent.FullName, "RawImgs"))))
                return parent.FullName;
            return source;
        }

        /// <summary>
        /// Extract a FigForge export zip into <paramref name="destAssetsFolder"/>
        /// (an "Assets/…"-relative path). Returns the extracted manifest's
        /// Assets-relative path, or null if the zip isn't a valid FigForge export.
        /// </summary>
        public static string ExtractToAssets(string zipPath, string destAssetsFolder)
        {
            if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
            {
                Debug.LogError($"[FigForge] zip not found: {zipPath}");
                return null;
            }

            try
            {
                destAssetsFolder = NormalizeAssetsFolder(destAssetsFolder);
                TextureImportHelper.EnsureFolder(destAssetsFolder);
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string destAbs = Path.Combine(projectRoot, destAssetsFolder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(destAbs);

                string manifestEntry = null;
                string projectEntry = null;
                var manifests = new List<string>();
                int files = 0;

                using (var fs = File.OpenRead(zipPath))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        var name = entry.FullName.Replace('\\', '/');
                        if (name.EndsWith("/")) continue;

                        try
                        {
                            var outPath = Path.GetFullPath(Path.Combine(destAbs, name));
                            if (Path.IsPathRooted(name) || name.Contains(":") ||
                                !outPath.StartsWith(Path.GetFullPath(destAbs) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                throw new IOException("ZIP entry escapes the import directory");
                            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                            using (var es = entry.Open())
                            using (var os = File.Create(outPath))
                                es.CopyTo(os);

                            files++;
                            if (Path.GetFileName(name).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) &&
                                !manifests.Contains(name))
                            {
                                manifests.Add(name);
                                if (manifestEntry == null || name == "manifest.json") manifestEntry = name;
                            }
                            if (Path.GetFileName(name).Equals("project.json", StringComparison.OrdinalIgnoreCase)) projectEntry = name;
                        }
                        catch (Exception entryEx)
                        {
                            // One bad entry shouldn't abort the whole extract —
                            // log it and keep going with the rest.
                            Debug.LogWarning($"[FigForge] skipped zip entry '{entry.FullName}': {entryEx.Message}");
                            continue;
                        }
                    }
                }

                if (manifestEntry == null)
                {
                    Debug.LogError("[FigForge] zip has no manifest.json — not a FigForge export.");
                    return null;
                }

                // Current exports omit project.json. Build a local index so all screens
                // can use the existing batch build flow and retain their bundle root.
                if (projectEntry == null)
                {
                    var roots = manifests.Select(p => p.Split('/')).ToList();
                    bool wrapped = roots.All(p => p.Length >= 3 && p[0] == roots[0][0]);
                    var prefix = wrapped ? roots[0][0] + "/" : "";
                    projectEntry = prefix + "project.json";
                    var project = new ProjectData
                    {
                        schema = "figforge/project", version = "2.0",
                        name = wrapped ? roots[0][0] : Path.GetFileName(destAssetsFolder),
                        screens = manifests.Select(p => new ProjectScreen
                        {
                            name = Path.GetFileName(Path.GetDirectoryName(p)),
                            manifest = p.Substring(prefix.Length),
                        }).ToList(),
                    };
                    File.WriteAllText(Path.Combine(destAbs, projectEntry), JsonConvert.SerializeObject(project, Formatting.Indented));
                }
                AssetDatabase.Refresh();
                Debug.Log($"[FigForge] extracted {files} file(s) → {destAssetsFolder}");
                return $"{destAssetsFolder}/{projectEntry}";
            }
            catch (Exception e)
            {
                Debug.LogError($"[FigForge] failed to extract zip: {e.Message}");
                return null;
            }
        }
    }
}
