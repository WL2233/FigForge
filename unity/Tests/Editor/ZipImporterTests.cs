using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FigForge.Tests
{
    public class ZipImporterTests
    {
        [TestCase("Assets/AssetRaw/UIRaw/Atlas/", "Assets/AssetRaw/UIRaw/Atlas")]
        [TestCase("Assets\\Custom\\Raw", "Assets/Custom/Raw")]
        public void OutputRoot_NormalizesAssetsPaths(string input, string expected)
            => Assert.AreEqual(expected, ZipImporter.NormalizeAssetsFolder(input));

        [TestCase("Assets/../Outside")]
        [TestCase("AssetsOther/Raw")]
        [TestCase("")]
        [TestCase("C:/Outside")]
        public void OutputRoot_RejectsInvalidPaths(string input)
            => Assert.Throws<IOException>(() => ZipImporter.NormalizeAssetsFolder(input));

        [Test]
        public void Images_UseSeparateOutputsAndKeepManifestKeysAndGuids()
        {
            var testRoot = "Assets/FigForgeImageTest_" + Guid.NewGuid().ToString("N");
            var source = testRoot + "/Source/测试包";
            var atlas = testRoot + "/CustomAtlas/测试包";
            var raw = testRoot + "/CustomRaw/测试包";
            var texture = new Texture2D(2, 2);
            try
            {
                Directory.CreateDirectory(source + "/背包");
                Directory.CreateDirectory(source + "/AltasImgs");
                Directory.CreateDirectory(source + "/RawImgs");
                var png = texture.EncodeToPNG();
                // Same filename in both categories must remain distinct. Explicit
                // RawImgs wins even though this fixture is smaller than 512 pixels.
                File.WriteAllBytes(source + "/AltasImgs/image.png", png);
                File.WriteAllBytes(source + "/RawImgs/image.png", png);
                AssetDatabase.Refresh();
                var manifest = new Manifest();
                manifest.assets.Add(new AssetEntry { file = "../AltasImgs/image.png" });
                manifest.assets.Add(new AssetEntry { file = "../RawImgs/image.png" });
                var settings = new TextureImportSettings();
                var sprites = TextureImportHelper.Import(manifest, source + "/背包", atlas, raw, settings);
                Assert.AreEqual(atlas + "/image.png", AssetDatabase.GetAssetPath(sprites["../AltasImgs/image.png"]));
                Assert.AreEqual(raw + "/image.png", AssetDatabase.GetAssetPath(sprites["../RawImgs/image.png"]));
                var guid = AssetDatabase.AssetPathToGUID(atlas + "/image.png");
                TextureImportHelper.Import(manifest, source + "/背包", atlas, raw, settings);
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(atlas + "/image.png"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.DeleteAsset(testRoot);
            }
        }

        [Test]
        public void WrappedZipWithoutProject_IndexesEveryScreenAndKeepsInternalRootName()
        {
            var destination = "Assets/FigForgeZipTest_" + Guid.NewGuid().ToString("N");
            var zip = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using (var stream = File.Create(zip))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    foreach (var screen in new[] { "背包", "商店" })
                    {
                        var entry = archive.CreateEntry("输入的文件夹名/" + screen + "/manifest.json");
                        using (var writer = new StreamWriter(entry.Open()))
                            writer.Write("{\"schema\":\"figforge/manifest\",\"version\":\"2.0\",\"elements\":[]}");
                    }
                    archive.CreateEntry("输入的文件夹名/AltasImgs/");
                    archive.CreateEntry("输入的文件夹名/RawImgs/");
                }
                var path = ZipImporter.ExtractToAssets(zip, destination);
                Assert.AreEqual(destination + "/输入的文件夹名/project.json", path);
                var project = ManifestParser.LoadProject(path);
                Assert.AreEqual(2, project.screens.Count);
                CollectionAssert.AreEquivalent(new[] { "背包/manifest.json", "商店/manifest.json" },
                    project.screens.ConvertAll(s => s.manifest));
                Assert.AreEqual("输入的文件夹名", Path.GetFileName(
                    ZipImporter.BundleRoot(destination + "/输入的文件夹名/背包")));
            }
            finally
            {
                AssetDatabase.DeleteAsset(destination);
                if (File.Exists(zip)) File.Delete(zip);
            }
        }
    }
}
