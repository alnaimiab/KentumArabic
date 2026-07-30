using System;
using System.Collections.Generic;
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
        public static ConfigEntry<bool> CfgSelfTestOverlay;
        public static ConfigEntry<bool> CfgCheckForUpdates;
        public static ConfigEntry<string> CfgFontFile;
        public static ConfigEntry<int> CfgFontMigration;
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


                var patched = new List<string>();
                foreach (var m in _harmony.GetPatchedMethods())
                    patched.Add($"{m.DeclaringType?.Name}.{m.Name}");
                Log.Info($"Patched {patched.Count} method(s): {string.Join(", ", patched.ToArray())}");
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
                "Press Ctrl+Alt+N in game to try the bundled fonts, then name your pick here.\n" +
                "The font asset is built at runtime with a dynamic atlas, so any font works as\n" +
                "long as it covers the presentation forms this translation produces. Screen a\n" +
                "candidate with 'ShaperTest --glyphs' then tools/check_font_coverage.py; judging\n" +
                "by Presentation Forms-B block coverage alone rejects good fonts.");

            CfgFontMigration = Config.Bind("Font", "AppliedDefaultChanges", 0,
                "Internal. Tracks which changes of the built-in default font have been applied\n" +
                "to this config file. Do not edit.");

            MigrateFontDefault();

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
                "Shortly after startup, dump the translation workbook and save a screenshot to\n" +
                "_dump/. Used to verify Arabic rendering without navigating the menus by hand.");

            CfgSelfTestOverlay = Config.Bind("Diagnostics", "SelfTestShowOverlay", true,
                "Whether the self-test also shows the shaping test battery. Turn this off to\n" +
                "screenshot the game's own UI instead, which is what you want when checking real\n" +
                "screens for layout problems.");

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
                // The options panel may already have read the saved language index while Arabic
                // was still missing from the list, in which case it silently fell back to English.
                // Re-applying here makes the saved choice stick regardless of initialisation order.
                ArabicLanguage.RestoreSavedLanguage();

                // If Arabic ended up active, the menus were already built in English before the
                // shaping hook could do anything, so they have to be localized again from scratch.
                if (!ArabicActive && ArabicLanguage.IsArabicActive())
                    BeginLanguageChange(ArabicLanguage.LanguageName);
                if (ArabicActive)
                    ForceRelocalize();
            }
        }

        /// <summary>Re-syncs the active flag with whatever language is currently selected.</summary>
        public static void RefreshArabicActive()
        {
            BeginLanguageChange(Localization.CurrentLanguage);
        }

        /// <summary>
        /// Called *before* Localization.ChangeLanguage runs.
        ///
        /// This has to happen in a prefix, not a postfix. ChangeLanguage assigns
        /// <c>UILocalizationManager.instance.currentLanguage</c>, whose setter fires the
        /// <c>languageChanged</c> event — so every LocalizedStaticText in the scene re-localizes
        /// and assigns its new Arabic string *while ChangeLanguage is still executing*. If the
        /// active flag were only set afterwards, all of that text would pass through the shaping
        /// hook while it was still disabled and render as disconnected isolated letters.
        /// </summary>
        public static void BeginLanguageChange(string languageId)
        {
            bool willBeArabic = string.Equals(languageId, ArabicLanguage.LanguageName, StringComparison.Ordinal);
            if (willBeArabic == ArabicActive) return;

            if (ArabicActive && !willBeArabic)
            {
                // Put direction and alignment back before the English text arrives.
                Log.Info("Arabic deactivated; restoring original text direction and alignment.");
                TextDirector.RestoreAll();
            }

            ArabicActive = willBeArabic;
            TextDirector.Enabled = willBeArabic;

            if (willBeArabic)
            {
                Log.Info("Arabic activated.");
                ArabicFont.RegisterFallback();
                PrewarmGlyphs();
                if (!ArabicFont.IsLoaded)
                    Log.Warn("Arabic is active but no Arabic font is loaded — text will render as empty boxes.");
            }
        }

        /// <summary>
        /// Rasterizes every glyph the translation will ever need, before any of it is drawn.
        /// Runs once, on the switch into Arabic.
        /// </summary>
        private static bool _prewarmed;

        /// <summary>
        /// Moves an existing install onto a new default font, without overriding a real choice.
        ///
        /// BepInEx writes every default into the config file on first run, and from then on the
        /// stored value wins. So changing the default in code reaches new installs only — everyone
        /// who already played keeps the old font and sees nothing change, which looks like the
        /// update did not work.
        ///
        /// The distinction that matters is whether the stored value was ever chosen. If it still
        /// equals the default it replaced, nobody chose it and it is safe to move forward. If it
        /// is anything else the player picked it, and it is left alone.
        /// </summary>
        private static readonly string[] SupersededFontDefaults =
        {
            "fonts/NotoNaskhArabic-Regular.ttf",     // migration 1
            "fonts/IBMPlexSansArabic-Regular.ttf",   // migration 2
        };

        private static void MigrateFontDefault()
        {
            Log.Try("Migrating the font default", () =>
            {
                int applied = CfgFontMigration.Value;
                if (applied >= SupersededFontDefaults.Length) return;

                var current = (CfgFontFile.Value ?? string.Empty).Trim();
                bool wasNeverChosen = false;
                for (int i = applied; i < SupersededFontDefaults.Length; i++)
                    if (string.Equals(current, SupersededFontDefaults[i], StringComparison.OrdinalIgnoreCase))
                        wasNeverChosen = true;

                CfgFontMigration.Value = SupersededFontDefaults.Length;

                if (!wasNeverChosen)
                {
                    Log.Verbose($"Keeping the configured font '{current}'; it is not a superseded default.");
                    return;
                }

                CfgFontFile.Value = ArabicFont.DefaultFontFile;
                Log.Info($"Font default updated: '{current}' -> '{ArabicFont.DefaultFontFile}'. " +
                         "Set FontFile in the config, or press Ctrl+Alt+N in game, to choose another.");
            });
        }

        private static void PrewarmGlyphs()
        {
            if (_prewarmed || Translations == null || !ArabicFont.IsLoaded) return;
            _prewarmed = true;
            ArabicFont.Prewarm(AllShapedText());
        }

        /// <summary>
        /// Every string the player can see, shaped. A newly built font asset starts with an empty
        /// dynamic atlas, so this is what a font swap has to re-warm.
        /// </summary>
        public static List<string> AllShapedText()
        {
            var shaped = new List<string>(Translations?.TotalEntries ?? 0);
            if (Translations == null) return shaped;

            foreach (var s in Translations.Ui.Values) shaped.Add(ArabicShaper.Shape(s));
            foreach (var s in Translations.Dialogue.Values) shaped.Add(ArabicShaper.Shape(s));
            foreach (var s in Translations.Actors.Values) shaped.Add(ArabicShaper.Shape(s));
            return shaped;
        }

        /// <summary>
        /// Cycles to the next bundled font and puts it on screen. Bound to Ctrl+Alt+N so the
        /// typeface can be judged on the real menus and dialogue rather than from a sample sheet.
        /// The choice is not persisted — set FontFile in the config file to keep one.
        /// </summary>
        public static void CycleFont()
        {
            var fonts = ArabicFont.BundledFonts;
            var current = ArabicFont.LoadedFrom ?? string.Empty;

            int index = 0;
            for (int i = 0; i < fonts.Length; i++)
                if (current.EndsWith(Path.GetFileName(fonts[i]), StringComparison.OrdinalIgnoreCase))
                {
                    index = i + 1;
                    break;
                }

            var next = fonts[index % fonts.Length];
            if (ArabicFont.SwitchTo(PluginDir, next, AllShapedText()))
                Log.Info($"Font: {Path.GetFileNameWithoutExtension(next)}. " +
                         "Ctrl+Alt+N for the next one; set FontFile in the config to keep it.");
        }

        /// <summary>Called after the language change has propagated.</summary>
        public static void EndLanguageChange(string languageId)
        {
            LocalizedTextPatches.DirectAllOnScreen();
            ArabicFont.RefreshAllText();
        }

        /// <summary>
        /// Re-runs localization on every UI element that is already on screen.
        ///
        /// Needed when Arabic becomes active outside a ChangeLanguage call — for instance when
        /// injection completes after the menus have already built themselves in English. Setting
        /// the flag alone is not enough: the strings were assigned before the hook could act, and
        /// TMP will not re-evaluate them on its own.
        /// </summary>
        public static void ForceRelocalize()
        {
            Log.Try("Forcing re-localization", () =>
            {
                var uilm = UILocalizationManager.instance;
                if (uilm != null) uilm.UpdateUIs(Localization.CurrentLanguage);

                // Anything already holding Arabic still needs its direction set.
                int directed = LocalizedTextPatches.DirectAllOnScreen();
                ArabicFont.RefreshAllText();
                Log.Verbose($"Set right-to-left layout on {directed} component(s).");
            });
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
