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
            /// Shapes the localized string, and acts as a safety net for keys the text table
            /// could not cover.
            ///
            /// This is where shaping happens because it is the one hook that demonstrably fires
            /// in this game — Harmony reports TMP_Text.set_text as patched, but the detour never
            /// actually runs, verified with a probe that sets text on a component we create
            /// ourselves. Every piece of Kentum UI text flows through Localize, so this covers
            /// the same ground.
            ///
            /// Shaping here does not disturb the LocalizationDebugMode coverage report: that mode
            /// returns its green/red marker from inside Localize, before this postfix sees it, and
            /// those markers contain no Arabic so shaping leaves them untouched. The missing-key
            /// substitution below is likewise gated on a genuine miss (result == key).
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

                var shaped = Shaping.ArabicShaper.Shape(__result);
                if (!ReferenceEquals(shaped, __result)) Shaped++;
                return shaped;
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
