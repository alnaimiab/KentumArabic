using System;
using HarmonyLib;
using PixelCrushers;
using KentumArabic.Shaping;
using KentumArabic.Util;

namespace KentumArabic.Injection
{
    /// <summary>
    /// Shapes Arabic at the point every localized string is born.
    ///
    /// <c>TextTable.GetFieldTextForLanguage</c> is the true chokepoint. Kentum reaches localized
    /// text by two independent routes — its own <c>Tlon.Localization.Localize</c>, and Pixel
    /// Crushers' <c>LocalizeUI</c> components, which read the table directly and never touch
    /// Kentum's wrapper. The main menu uses the latter, which is why hooks on Localize and on
    /// LocalizedStaticText both showed zero calls while Arabic text was plainly on screen.
    ///
    /// Hooking TextMeshPro's text setter would have covered both and more, but on this build
    /// Harmony reports the patch as applied and the detour never runs — confirmed with a probe
    /// that sets text on a component created by this plugin. The table is the next boundary down
    /// that provably works.
    ///
    /// Known limitation: shaping happens before <c>string.Format</c> substitutes runtime values,
    /// so a placeholder's argument is inserted into already-shaped text. Digits render correctly
    /// but sit at the wrong end of strings that mix them with Arabic. Those strings carry the
    /// "format" flag in the translation workbook.
    /// </summary>
    public static class TextTablePatches
    {
        public static long Calls, Shaped;

        public static int ApplyTo(Harmony harmony)
        {
            var postfix = new HarmonyMethod(AccessTools.Method(typeof(TextTablePatches), nameof(ShapeResult)));

            // The two leaf implementations; the string/string and int/string overloads delegate
            // to these after resolving the language name, so patching the leaves covers all four.
            var targets = new[]
            {
                AccessTools.Method(typeof(TextTable), nameof(TextTable.GetFieldTextForLanguage),
                                   new[] { typeof(int), typeof(int) }),
                AccessTools.Method(typeof(TextTable), nameof(TextTable.GetFieldTextForLanguage),
                                   new[] { typeof(string), typeof(int) }),
            };

            int patched = 0;
            foreach (var target in targets)
            {
                if (target == null) continue;
                try
                {
                    harmony.Patch(target, postfix: postfix);
                    patched++;
                }
                catch (Exception e)
                {
                    Log.Warn($"Could not hook TextTable.GetFieldTextForLanguage: {e.Message}");
                }
            }

            if (patched == 0)
                Log.Error("Could not hook the text table. Arabic will render as disconnected letters.");
            else
                Log.Info($"Hooked {patched} text table lookup(s) for shaping.");

            return patched;
        }

        /// <summary>
        /// Pass-through postfix: takes the original return value and returns the replacement.
        ///
        /// Written this way rather than as <c>ref string __result</c> because on this HarmonyX
        /// build a patch declaring a <c>ref string</c> parameter is reported as applied but never
        /// actually invoked — verified by counters that stayed at zero across every such hook
        /// while identical hooks without <c>ref</c> ran normally.
        /// </summary>
        public static string ShapeResult(string __result)
        {
            Calls++;
            if (!Plugin.ArabicActive) return __result;
            if (string.IsNullOrEmpty(__result)) return __result;

            var shaped = ArabicShaper.Shape(__result);
            if (!ReferenceEquals(shaped, __result)) Shaped++;
            return shaped;
        }
    }
}
