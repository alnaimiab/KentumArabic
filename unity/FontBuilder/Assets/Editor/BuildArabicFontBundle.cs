using System.IO;
using UnityEditor;
using UnityEngine;

namespace KentumArabic.FontBuilder
{
    /// <summary>
    /// Packs the Arabic TMP font asset into an AssetBundle the plugin can load at runtime.
    ///
    /// This must run in Unity 2022.3.62f3 — the exact version Kentum ships. A TMP_FontAsset
    /// serialized by a different Unity or TextMeshPro version can deserialize with missing
    /// fields, which shows up as invisible or corrupted text rather than a clean error.
    /// </summary>
    public static class BuildArabicFontBundle
    {
        private const string BundleName = "arabicfont";
        private const string ExpectedUnityVersion = "2022.3.62f3";

        [MenuItem("Kentum Arabic/Build Font Bundle")]
        public static void Build()
        {
            if (Application.unityVersion != ExpectedUnityVersion)
            {
                if (!EditorUtility.DisplayDialog(
                        "Unity version mismatch",
                        $"Kentum runs Unity {ExpectedUnityVersion} but this editor is {Application.unityVersion}.\n\n" +
                        "A bundle built with a different version will most likely fail to load or render " +
                        "incorrectly in the game.\n\nBuild anyway?",
                        "Build anyway", "Cancel"))
                {
                    return;
                }
            }

            // The output goes straight into content/ so scripts/deploy.ps1 picks it up, and so
            // the bundle is committed alongside the translation it belongs to.
            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../content"));
            Directory.CreateDirectory(outDir);

            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetNames = CollectFontAssetPaths(),
            };

            if (build.assetNames.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "No font asset found",
                    "No TMP_FontAsset was found under Assets/Fonts.\n\n" +
                    "Create one first with Window > TextMeshPro > Font Asset Creator. " +
                    "See docs/font-build.md for the exact settings, especially the character ranges — " +
                    "the Arabic Presentation Forms blocks are mandatory.",
                    "OK");
                return;
            }

            Debug.Log($"[KentumArabic] Packing {build.assetNames.Length} asset(s) into '{BundleName}':\n  " +
                      string.Join("\n  ", build.assetNames));

            var manifest = BuildPipeline.BuildAssetBundles(
                outDir,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression,
                BuildTarget.StandaloneWindows64);

            if (manifest == null)
            {
                Debug.LogError("[KentumArabic] Bundle build failed. See the console above for details.");
                return;
            }

            // BuildAssetBundles also emits .manifest files and a bundle named after the folder;
            // only the bundle itself ships.
            var bundlePath = Path.Combine(outDir, BundleName);
            var sizeMb = new FileInfo(bundlePath).Length / 1048576f;
            Debug.Log($"[KentumArabic] Built {bundlePath} ({sizeMb:F1} MB). " +
                      "Run scripts/deploy.ps1 to push it into the game.");

            EditorUtility.RevealInFinder(bundlePath);
        }

        private static string[] CollectFontAssetPaths()
        {
            var paths = new System.Collections.Generic.List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Fonts" }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            return paths.ToArray();
        }

        /// <summary>
        /// Reports whether the built font actually covers the characters Arabic rendering needs.
        /// The Presentation Forms-B block is the one that silently breaks everything when absent:
        /// shaping maps letters into it, and TMP does no substitution of its own.
        /// </summary>
        [MenuItem("Kentum Arabic/Verify Font Coverage")]
        public static void VerifyCoverage()
        {
            var paths = CollectFontAssetPaths();
            if (paths.Length == 0)
            {
                Debug.LogError("[KentumArabic] No TMP_FontAsset found under Assets/Fonts.");
                return;
            }

            foreach (var path in paths)
            {
                var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
                if (font == null) continue;

                int formsB = CountRange(font, 0xFE70, 0xFEFF);
                int formsA = CountRange(font, 0xFB50, 0xFDFF);
                int arabic = CountRange(font, 0x0600, 0x06FF);
                int latin = CountRange(font, 0x0020, 0x007E);

                var verdict = formsB >= 100 ? "OK" : "INSUFFICIENT";
                var log =
                    $"[KentumArabic] {font.name}: {verdict}\n" +
                    $"  Presentation Forms-B (U+FE70-FEFF): {formsB}   <- must be ~140+, this is what shaping produces\n" +
                    $"  Presentation Forms-A (U+FB50-FDFF): {formsA}\n" +
                    $"  Arabic              (U+0600-06FF): {arabic}\n" +
                    $"  Basic Latin         (U+0020-007E): {latin}\n" +
                    $"  atlas: {font.atlasWidth}x{font.atlasHeight}, {font.characterTable.Count} characters";

                if (formsB >= 100) Debug.Log(log);
                else Debug.LogError(log + "\n  The source font lacks Arabic Presentation Forms-B, or the " +
                                          "character range was omitted. Arabic will render as empty boxes.");
            }
        }

        private static int CountRange(TMPro.TMP_FontAsset font, int from, int to)
        {
            int n = 0;
            for (int c = from; c <= to; c++)
                if (font.HasCharacter((char)c)) n++;
            return n;
        }
    }
}
