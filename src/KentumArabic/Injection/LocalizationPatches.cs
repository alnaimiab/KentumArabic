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
            /// Safety net for keys the text table could not cover. It deliberately fires only
            /// when the lookup genuinely missed (result == key), which preserves the debug-mode
            /// semantics described above while still catching stragglers.
            /// </summary>
            public static void Postfix(string key, ref string __result)
            {
                if (!Plugin.ArabicActive) return;
                if (__result != key) return;

                Diagnostics.TextDiagnostics.NoteMissingKey(key);

                if (Plugin.Translations != null && Plugin.Translations.Ui.TryGetValue(key, out var arabic))
                    __result = arabic;
            }
        }

        /// <summary>
        /// Tracks the active language and drives the UI refresh when the player switches.
        /// </summary>
        [HarmonyPatch(typeof(Localization), nameof(Localization.ChangeLanguage))]
        public static class ChangeLanguage_Patch
        {
            public static void Prefix(string languageId)
            {
                Plugin.TryEnsureInjected();
                Log.Verbose($"ChangeLanguage -> {languageId}");
            }

            public static void Postfix(string languageId)
            {
                Plugin.OnLanguageChanged(languageId);
            }
        }
    }
}
