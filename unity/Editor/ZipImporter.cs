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
using UnityEditor;
using UnityEngine;

namespace FigForge
{
    public static class ZipImporter
    {
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
                TextureImportHelper.EnsureFolder(destAssetsFolder);
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string destAbs = Path.Combine(projectRoot, destAssetsFolder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(destAbs);

                string manifestEntry = null;
                bool hasProject = false;
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
                                (manifestEntry == null || name == "manifest.json")) manifestEntry = name;
                            if (name.Equals("project.json", StringComparison.OrdinalIgnoreCase)) hasProject = true;
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

                AssetDatabase.Refresh();
                Debug.Log($"[FigForge] extracted {files} file(s) → {destAssetsFolder}");
                return $"{destAssetsFolder}/{(hasProject ? "project.json" : manifestEntry)}";
            }
            catch (Exception e)
            {
                Debug.LogError($"[FigForge] failed to extract zip: {e.Message}");
                return null;
            }
        }
    }
}
