using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Tlon.Localization;
using KentumArabic.Util;

namespace KentumArabic.Injection
{
    /// <summary>
    /// Hooks into Kentum's own localization system.
    ///
    /// The actual Arabic text is written into the live TextTable rather than returned from a
    /// patch on <c>Localize</c>. That matters for one specific reason: <c>Localize</c> returns
    /// <c>"&lt;color=red&gt; NOT LOCALIZED&lt;/color&gt;"</c> when the lookup result equals the key
    /// and debug mode is on. Substituting Arabic inside <c>Localize</c> would make that tool
    /// report "not localized" for strings we *have* translated, destroying the best coverage
    /// report the game hands us. Writing to the table instead turns debug mode into a direct,
    /// screen-by-screen coverage report for the translation.
    /// </summary>
    public static class LocalizationPatches
    {
        /// <summary>
        /// Primary injection point. This is the single choke point for "what languages exist",
        /// so making it self-healing means the ordering question stops mattering: whichever hook
        /// fires first wins, and EnsureInjected is a no-op after that.
        /// </summary>
        [HarmonyPatch(typeof(Localization), nameof(Localization.GetAllLanguagesNames))]
        public static class GetAllLanguagesNames_Patch
        {
            public static void Prefix()
            {
                Plugin.TryEnsureInjected();
            }

            /// <summary>
            /// Repairs a language list that was cached before Arabic existed.
            ///
            /// The prefix cannot always inject: until UILocalizationManager has a textTable there
            /// is nothing to inject into, so it returns quietly and the original method then
            /// caches the ten stock languages in its own static field. Every later call returns
            /// that cached list, and OptionsPanel.InitializeOptions materialises it into a fixed
            /// string[] for the dropdown — so Arabic is absent from that dropdown permanently,
            /// even though the language itself registers seconds later. That is the state where
            /// the menus are in Arabic while the dropdown reads ENGLISH: the saved index 10 is
            /// out of range for a ten-item array, so it falls back to item 0.
            ///
            /// The method returns its cache by reference rather than a copy, so appending here
            /// fixes the cache itself and not just this one caller.
            /// </summary>
            public static void Postfix(List<string> __result)
            {
                if (__result == null || !ArabicLanguage.IsInjected) return;
                if (__result.Contains(ArabicLanguage.LanguageName)) return;

                __result.Add(ArabicLanguage.LanguageName);
                Log.Info($"Language list had been cached before Arabic was registered; " +
                         $"appended it at index {__result.Count - 1}.");
            }
        }

        /// <summary>
        /// The options panel builds its language dropdown once, into a fixed array, and never
        /// consults the language list again. Injecting before that array is built is the
        /// difference between Arabic appearing in the dropdown and never appearing at all.
        /// </summary>
        [HarmonyPatch]
        public static class OptionsPanel_InitializeOptions_Patch
        {
            public static MethodBase TargetMethod()
            {
                // Private and resolved by name: it is the whole reason this patch exists, so a
                // rename in a game update should disable this patch rather than crash the mod.
                var type = AccessTools.TypeByName("OptionsPanel");
                return type == null ? null : AccessTools.Method(type, "InitializeOptions");
            }

            public static bool Prepare() => TargetMethod() != null;

            public static void Prefix()
            {
                Plugin.TryEnsureInjected();
            }
        }

        /// <summary>
        /// Belt and braces: Localize runs constantly and early, so this guarantees injection has
        /// happened even if the language list is never queried first.
        /// </summary>
        [HarmonyPatch(typeof(Localization), nameof(Localization.Localize))]
        public static class Localize_Patch
        {
            public static void Prefix()
            {
                Plugin.TryEnsureInjected();
            }

            /// <summary>
            /// Safety net for keys the text table could not cover. It deliberately fires only on
            /// a genuine miss (result == key), which preserves the LocalizationDebugMode coverage
            /// report while still catching stragglers.
            ///
            /// Shaping deliberately does NOT happen here. Localize returns the format *template*,
            /// before string.Format substitutes anything, so shaping it would reverse the template
            /// around values that are not yet present.
            /// </summary>
            /// <summary>Diagnostics: how often this postfix runs and what it does.</summary>
            public static long Calls, WhileActive, Shaped;

            /// <summary>
            /// Pass-through postfix — returns the replacement rather than taking
            /// <c>ref string __result</c>, which this HarmonyX build accepts at patch time and
            /// then never invokes.
            /// </summary>
            public static string Postfix(string __result, string key)
            {
                Calls++;
                if (!Plugin.ArabicActive) return __result;
                WhileActive++;

                if (__result == key)
                {
                    Diagnostics.TextDiagnostics.NoteMissingKey(key);
                    if (Plugin.Translations != null && Plugin.Translations.Ui.TryGetValue(key, out var arabic))
                        __result = arabic;
                }

                return __result;
            }
        }

        /// <summary>
        /// Tracks the active language and drives the UI refresh when the player switches.
        /// </summary>
        [HarmonyPatch(typeof(Localization), nameof(Localization.ChangeLanguage))]
        public static class ChangeLanguage_Patch
        {
            /// <summary>
            /// The shaping flag must be set here, before ChangeLanguage runs. Assigning
            /// UILocalizationManager.currentLanguage inside it fires languageChanged, which
            /// re-localizes every visible text component — so the flag has to already be true or
            /// that entire first wave of Arabic text renders unshaped.
            /// </summary>
            public static void Prefix(string languageId)
            {
                Plugin.TryEnsureInjected();
                Log.Verbose($"ChangeLanguage -> {languageId}");
                Plugin.BeginLanguageChange(languageId);
            }

            public static void Postfix(string languageId)
            {
                Plugin.EndLanguageChange(languageId);
            }
        }
    }
}
