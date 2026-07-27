using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using KentumArabic.Util;

namespace KentumArabic.Fonts
{
    /// <summary>
    /// Supplies Arabic glyphs to TextMeshPro.
    ///
    /// Kentum ships Montserrat, GeosansLight, LiberationSans and three CJK Noto families — none
    /// of which contain a single Arabic codepoint. Rather than replacing the UI font (which would
    /// change every Latin glyph's metrics and break tuned layouts), the Arabic face is registered
    /// as a *fallback*: TMP resolves Latin and digits from the original font and only falls
    /// through to ours for characters the original cannot render. Mixed strings then work with no
    /// extra logic and the game keeps its visual identity.
    /// </summary>
    public static class ArabicFont
    {
        public const string DefaultFontFile = "fonts/NotoNaskhArabic-Regular.ttf";
        public const string DefaultBundleName = "arabicfont";

        private static AssetBundle _bundle;
        private static TMP_FontAsset _font;

        public static TMP_FontAsset Font => _font;
        public static bool IsLoaded => _font != null;
        public static string LoadedFrom { get; private set; }

        /// <summary>
        /// Builds the Arabic font asset. Tries, in order:
        ///  1. a .ttf next to the plugin, turned into a TMP font asset at runtime (default), and
        ///  2. a pre-built AssetBundle, for anyone who wants a hand-tuned static atlas.
        ///
        /// Returns false with a logged reason rather than throwing, so text injection can still
        /// be exercised without a font present.
        /// </summary>
        public static bool Load(string pluginDir, string fontFile, string bundleName, string assetName)
        {
            if (_font != null) return true;

            // A supplied bundle wins, since choosing one is an explicit act.
            if (!string.IsNullOrEmpty(bundleName))
            {
                var bundlePath = Path.Combine(pluginDir, bundleName);
                if (File.Exists(bundlePath) && LoadFromBundle(bundlePath, assetName))
                    return true;
            }

            var ttfPath = Path.Combine(pluginDir, string.IsNullOrEmpty(fontFile) ? DefaultFontFile : fontFile);
            if (File.Exists(ttfPath))
                return LoadFromTtf(ttfPath);

            Log.Warn($"No Arabic font found. Expected a TrueType file at '{ttfPath}'. " +
                     "Arabic text will render as empty boxes until one is present.");
            return false;
        }

        /// <summary>
        /// Creates a TMP font asset directly from a TrueType file, using a dynamic atlas that
        /// rasterizes glyphs on demand.
        ///
        /// This is much better than shipping a pre-baked AssetBundle: the material is built from
        /// the player's own TMP shader (so text can never come out pink), there is no Unity
        /// version coupling to break on a game update, no atlas size or character range to guess,
        /// and the download is a 240 KB font file instead of a multi-megabyte bundle.
        /// </summary>
        private static bool LoadFromTtf(string path)
        {
            try
            {
                // 90pt sampling with 9px padding matches TMP's own defaults and gives clean SDF
                // edges at the sizes Kentum's UI uses. Multi-atlas support is implicit: the asset
                // grows extra atlas pages if 1024x1024 fills up.
                _font = TMP_FontAsset.CreateFontAsset(
                    path,
                    faceIndex: 0,
                    samplingPointSize: 90,
                    atlasPadding: 9,
                    renderMode: GlyphRenderMode.SDFAA,
                    atlasWidth: 1024,
                    atlasHeight: 1024);

                if (_font == null)
                {
                    Log.Error($"TextMeshPro could not load the font face from '{path}'. " +
                              "Is the file a valid TrueType/OpenType font?");
                    return false;
                }

                _font.name = Path.GetFileNameWithoutExtension(path) + " SDF (runtime)";
                _font.isMultiAtlasTexturesEnabled = true;
                Protect();

                LoadedFrom = path;
                Log.Info($"Arabic font built at runtime from '{Path.GetFileName(path)}' " +
                         $"({_font.faceInfo.familyName} {_font.faceInfo.styleName}, dynamic atlas " +
                         $"{_font.atlasWidth}x{_font.atlasHeight}).");
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Building the Arabic font from '{path}' failed.\n{e}");
                return false;
            }
        }

        private static bool LoadFromBundle(string path, string assetName)
        {
            try
            {
                _bundle = AssetBundle.LoadFromFile(path);
                if (_bundle == null)
                {
                    Log.Error($"Failed to load asset bundle '{path}'. It was most likely built with a different " +
                              "Unity version — it must match the game's Unity version exactly. " +
                              "Falling back to building the font from a .ttf instead.");
                    return false;
                }

                _font = FindFontAsset(_bundle, assetName);
                if (_font == null)
                {
                    Log.Error($"No TMP_FontAsset inside '{path}'. Contents: {string.Join(", ", _bundle.GetAllAssetNames())}");
                    return false;
                }

                RebindShader(_font);
                Protect();

                LoadedFrom = path;
                Log.Info($"Arabic font loaded from bundle: '{_font.name}' " +
                         $"({_font.characterTable?.Count ?? 0} characters, atlas {_font.atlasWidth}x{_font.atlasHeight}).");
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Arabic font bundle load failed.\n{e}");
                return false;
            }
        }

        /// <summary>
        /// Scene loads trigger Resources.UnloadUnusedAssets, which would otherwise collect a font
        /// that nothing in the scene hierarchy references yet.
        /// </summary>
        private static void Protect()
        {
            _font.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (_font.material != null) _font.material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (_font.atlasTextures != null)
                foreach (var tex in _font.atlasTextures)
                    if (tex != null) tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            // The bundle, when used, is deliberately never unloaded.
        }

        private static TMP_FontAsset FindFontAsset(AssetBundle bundle, string assetName)
        {
            if (!string.IsNullOrEmpty(assetName))
            {
                var byName = bundle.LoadAsset<TMP_FontAsset>(assetName);
                if (byName != null) return byName;
                Log.Warn($"Font asset '{assetName}' not found in bundle; falling back to the first one present.");
            }

            var all = bundle.LoadAllAssets<TMP_FontAsset>();
            return (all != null && all.Length > 0) ? all[0] : null;
        }

        /// <summary>
        /// Shaders travelling inside an asset bundle frequently fail to resolve against the
        /// player's compiled variants, which shows up as bright pink text. Rebinding to the
        /// shader the player already has avoids that entirely. (Runtime-created assets are
        /// immune: TMP builds their material from the player's own shader reference.)
        /// </summary>
        private static void RebindShader(TMP_FontAsset font)
        {
            if (font.material == null || font.material.shader == null) return;

            var name = font.material.shader.name;
            var playerShader = Shader.Find(name);
            if (playerShader != null)
            {
                font.material.shader = playerShader;
                Log.Verbose($"Rebound font material to player shader '{name}'.");
            }
            else
            {
                Log.Warn($"Shader '{name}' not found in the player; relying on the bundled shader. " +
                         "If Arabic text renders pink, this is why.");
            }
        }

        /// <summary>
        /// Registers the Arabic font in TMP's global fallback list and on every currently loaded
        /// font asset. Idempotent, so it can be re-run cheaply after each scene load to catch
        /// font assets that arrive with new scenes.
        /// </summary>
        public static void RegisterFallback()
        {
            if (_font == null) return;

            Log.Try("Registering global TMP fallback", () =>
            {
                var list = TMP_Settings.fallbackFontAssets;
                if (list == null) list = new List<TMP_FontAsset>();
                if (!list.Contains(_font))
                {
                    // Insert first so Arabic wins over the CJK Noto fonts, which contain no
                    // Arabic anyway but would otherwise be searched first.
                    list.Insert(0, _font);
                    TMP_Settings.fallbackFontAssets = list;
                    Log.Verbose("Added Arabic font to TMP_Settings.fallbackFontAssets.");
                }
            });

            Log.Try("Registering per-font fallbacks", () =>
            {
                int added = 0;
                foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (f == null || f == _font) continue;
                    if (f.fallbackFontAssetTable == null)
                        f.fallbackFontAssetTable = new List<TMP_FontAsset>();
                    if (!f.fallbackFontAssetTable.Contains(_font))
                    {
                        f.fallbackFontAssetTable.Add(_font);
                        added++;
                    }
                }
                if (added > 0) Log.Verbose($"Added Arabic fallback to {added} font asset(s).");
            });
        }

        /// <summary>
        /// Forces every live text object to re-evaluate its glyphs. TMP does not re-resolve
        /// characters when the fallback chain changes, so without this, text already on screen
        /// keeps showing empty boxes.
        /// </summary>
        public static void RefreshAllText()
        {
            Log.Try("Refreshing TMP text objects", () =>
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_Text>();
                foreach (var t in all)
                {
                    if (t == null) continue;
                    t.SetAllDirty();
                    t.ForceMeshUpdate(true, true);
                }
                Log.Verbose($"Refreshed {all.Length} TMP_Text object(s).");
            });
        }

        /// <summary>
        /// Reports which characters used by the translation the font cannot render. Catches the
        /// classic failure — a font without the Arabic Presentation Forms-B block (U+FE70–FEFF) —
        /// in seconds, instead of during play-testing.
        /// </summary>
        public static void AuditCoverage(IEnumerable<string> texts)
        {
            if (_font == null) return;

            var missing = new SortedDictionary<int, int>();
            int scanned = 0;

            foreach (var s in texts)
            {
                if (string.IsNullOrEmpty(s)) continue;
                scanned++;
                foreach (var ch in s)
                {
                    if (ch < 0x20) continue;
                    // tryAddCharacter matters for a dynamic atlas: a glyph the face can render
                    // is simply not rasterized yet, and would otherwise be reported missing.
                    if (_font.HasCharacter(ch, searchFallbacks: false, tryAddCharacter: true)) continue;
                    missing.TryGetValue(ch, out var n);
                    missing[ch] = n + 1;
                }
            }

            if (missing.Count == 0)
            {
                Log.Info($"Font coverage audit passed: all characters across {scanned} string(s) are renderable.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Font coverage audit: {missing.Count} character(s) missing from '{_font.name}'. " +
                          "These will render as empty boxes:");
            int shown = 0;
            foreach (var kv in missing)
            {
                if (shown++ >= 40) { sb.AppendLine($"  ... and {missing.Count - 40} more"); break; }
                sb.AppendLine($"  U+{kv.Key:X4} '{(char)kv.Key}' x{kv.Value}");
            }
            sb.Append("If these are in U+FE70–FEFF, the source font lacks Arabic Presentation Forms-B. " +
                      "Rebuild the font asset with that range included.");
            Log.Warn(sb.ToString());
        }
    }
}
