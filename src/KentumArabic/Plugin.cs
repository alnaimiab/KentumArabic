using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PixelCrushers;
using UnityEngine;
using Tlon.Localization;
using KentumArabic.Diagnostics;
using KentumArabic.Fonts;
using KentumArabic.Injection;
using KentumArabic.Shaping;
using KentumArabic.Util;

namespace KentumArabic
{
    /// <summary>
    /// Arabic translation for Kentum (تعريب لعبة Kentum).
    ///
    /// Adds Arabic as a genuine, selectable language rather than overwriting an existing one.
    /// Nothing in the game's own files is modified: the language is registered into the live
    /// TextTable at runtime, Arabic glyphs are supplied through TMP's fallback chain, and the
    /// translation itself lives in loose TSV files that can be updated without rebuilding
    /// anything.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("Kentum.exe")]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.kentum.arabic";
        public const string PluginName = "Kentum Arabic";
        public const string PluginVersion = "0.1.0";

        public static Plugin Instance { get; private set; }
        public static TranslationStore Translations { get; private set; }
        public static string PluginDir { get; private set; }

        /// <summary>Hot path flag: true only while Arabic is the selected language.</summary>
        public static bool ArabicActive { get; private set; }

        // --- configuration -------------------------------------------------------------------
        public static ConfigEntry<ShapingMode> CfgShapingMode;
        public static ConfigEntry<bool> CfgPreserveNumbers;
        public static ConfigEntry<bool> CfgVerboseLogging;
        public static ConfigEntry<bool> CfgDiagnostics;
        public static ConfigEntry<bool> CfgSelfTest;
        public static ConfigEntry<bool> CfgCheckForUpdates;
        public static ConfigEntry<string> CfgFontFile;
        public static ConfigEntry<string> CfgFontBundle;
        public static ConfigEntry<string> CfgFontAssetName;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log.Init(Logger);

            PluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            BindConfig();

            Log.Info($"{PluginName} v{PluginVersion} starting. Plugin directory: {PluginDir}");

            // Load order matters: translations first (so injection has data to write), then the
            // font (so glyphs exist before anything renders), then patches.
            Log.Try("Loading translations", () =>
            {
                Translations = TranslationStore.LoadFrom(Path.Combine(PluginDir, "strings"));
            });
            Translations ??= new TranslationStore();

            Log.Try("Loading Arabic font", () =>
            {
                if (ArabicFont.Load(PluginDir, CfgFontFile.Value, CfgFontBundle.Value, CfgFontAssetName.Value))
                    ArabicFont.RegisterFallback();
            });

            Log.Try("Applying Harmony patches", () =>
            {
                _harmony = new Harmony(PluginGuid);
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Info($"Patched {_harmony.GetPatchedMethods().CountEnumerable()} method(s).");
            });

            // Hosts hotkeys, per-scene fallback re-registration and the shaping test overlay.
            var host = new GameObject("KentumArabic.Runner");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<KentumArabicRunner>();

            if (CfgCheckForUpdates.Value)
                Updates.UpdateChecker.CheckAsync(host);

            Log.Info("Startup complete. Select العربية in Options > Language.");
        }

        private void BindConfig()
        {
            CfgShapingMode = Config.Bind("Shaping", "Mode", ShapingMode.RtlLayout,
                "How Arabic is prepared for TextMeshPro.\n" +
                "RtlLayout  - shape, reverse, and put TMP in right-to-left mode. Correct word wrap\n" +
                "             and correct typewriter direction. Recommended.\n" +
                "VisualOrder- shape and reorder only, leaving TMP left-to-right. Single lines look\n" +
                "             right but multi-line text wraps bottom-to-top.\n" +
                "None       - no processing at all; for comparison while diagnosing.");

            CfgPreserveNumbers = Config.Bind("Shaping", "PreserveNumbers", true,
                "Keep Western digits (0-9) instead of converting to Arabic-Indic (٠-٩).");

            CfgFontFile = Config.Bind("Font", "FontFile", ArabicFont.DefaultFontFile,
                "TrueType/OpenType font used for Arabic, relative to this plugin's folder.\n" +
                "The font asset is built at runtime with a dynamic atlas, so any font works as\n" +
                "long as it contains the Arabic Presentation Forms-B block (U+FE70-FEFF).\n" +
                "Run tools/check_font_coverage.py against a font before switching to it.");

            CfgFontBundle = Config.Bind("Font", "BundleFileName", "",
                "Optional: a pre-built AssetBundle holding a hand-tuned TMP font asset, used\n" +
                "instead of FontFile. Must be built with the game's exact Unity version.\n" +
                "Leave empty to build the font from FontFile, which is the recommended path.");

            CfgFontAssetName = Config.Bind("Font", "AssetName", "",
                "Name of the TMP_FontAsset inside the bundle. Leave empty to use the first one found.");

            CfgVerboseLogging = Config.Bind("Logging", "Verbose", false,
                "Log detailed information about injection, fallbacks and language switching.");

            CfgDiagnostics = Config.Bind("Diagnostics", "Enabled", false,
                "Track untranslated keys and on-screen text that bypasses the localization system.\n" +
                "Press Ctrl+Alt+D while playing to write the reports.");

            CfgSelfTest = Config.Bind("Diagnostics", "SelfTestOnStartup", false,
                "Show the shaping test battery shortly after startup and save a screenshot to\n" +
                "_dump/. Used to verify Arabic rendering without navigating the menus by hand.");

            CfgCheckForUpdates = Config.Bind("Updates", "CheckOnStartup", true,
                "Check GitHub once at startup for a newer translation and show an in-game notice.\n" +
                "Nothing is ever downloaded automatically and no information about you is sent.");

            ArabicShaper.Mode = CfgShapingMode.Value;
            ArabicShaper.PreserveNumbers = CfgPreserveNumbers.Value;
            Log.VerboseEnabled = CfgVerboseLogging.Value;
            TextDiagnostics.Enabled = CfgDiagnostics.Value;
        }

        /// <summary>
        /// Idempotent injection entry point, called from several hooks. Whichever fires first
        /// performs the work; the rest cost a single bool check.
        /// </summary>
        public static void TryEnsureInjected()
        {
            if (ArabicLanguage.IsInjected) return;
            if (Translations == null) return;

            if (ArabicLanguage.EnsureInjected(Translations))
            {
                // Re-evaluate now that the language exists — the player may already have Arabic
                // saved as their preference from a previous session.
                RefreshArabicActive();
            }
        }

        public static void RefreshArabicActive()
        {
            bool wasActive = ArabicActive;
            ArabicActive = ArabicLanguage.IsArabicActive();
            if (wasActive != ArabicActive) ApplyActiveState();
        }

        public static void OnLanguageChanged(string languageId)
        {
            bool wasActive = ArabicActive;
            ArabicActive = string.Equals(languageId, ArabicLanguage.LanguageName, StringComparison.Ordinal);

            if (wasActive != ArabicActive) ApplyActiveState();

            // Text already on screen must be re-evaluated either way: entering Arabic needs
            // shaping applied, leaving it needs the original strings back.
            ArabicFont.RefreshAllText();
        }

        private static void ApplyActiveState()
        {
            TextDirector.Enabled = ArabicActive;

            if (ArabicActive)
            {
                Log.Info("Arabic activated.");
                ArabicFont.RegisterFallback();
                if (!ArabicFont.IsLoaded)
                    Log.Warn("Arabic is active but no Arabic font is loaded — text will render as empty boxes.");
            }
            else
            {
                Log.Info("Arabic deactivated; restoring original text direction and alignment.");
                TextDirector.RestoreAll();
            }
        }

        /// <summary>Re-reads the TSV files and re-applies them without restarting the game.</summary>
        public static void HotReload()
        {
            Log.Try("Hot reload", () =>
            {
                Translations = TranslationStore.LoadFrom(Path.Combine(PluginDir, "strings"));
                ArabicShaper.ClearCache();

                int applied = ArabicLanguage.Reapply(Translations);
                Log.Info($"Hot reload applied {applied} field(s).");

                // Force everything on screen through the pipeline again.
                UILocalizationManager.instance?.UpdateUIs(Localization.CurrentLanguage);
                ArabicFont.RefreshAllText();
            });
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }

    internal static class EnumerableCountExtensions
    {
        public static int CountEnumerable<T>(this System.Collections.Generic.IEnumerable<T> src)
        {
            int n = 0;
            if (src != null) foreach (var _ in src) n++;
            return n;
        }
    }
}
