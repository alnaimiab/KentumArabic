using HarmonyLib;
using TMPro;
using KentumArabic.Shaping;
using KentumArabic.Util;

namespace KentumArabic.Injection
{
    /// <summary>
    /// Applies Arabic shaping at the last possible moment before text reaches TextMeshPro.
    ///
    /// This is deliberately not done in the translation files, and not in a postfix on
    /// <c>Localization.Localize</c>. Localize returns the *format template* — the string that
    /// later goes through string.Format with runtime arguments — so the composed text a player
    /// actually sees never passes through it. The text setter is the real last boundary: it
    /// catches string.Format output, runtime-composed strings, numbers, and every code path that
    /// was never enumerated.
    /// </summary>
    [HarmonyPatch]
    public static class TmpPatches
    {
        /// <summary>
        /// Set while we are writing back, to stop the hook re-entering itself. Also used by the
        /// test overlay, which needs to drive each shaping mode explicitly rather than have the
        /// global mode applied to it.
        /// </summary>
        public static bool Suppress;

        public static long ShapedCount;
        public static long PassthroughCount;

        [HarmonyPatch(typeof(TMP_Text), "text", MethodType.Setter)]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        public static void TextSetter_Prefix(TMP_Text __instance, ref string value)
        {
            Process(__instance, ref value);
        }

        /// <summary>
        /// SetText(string) writes m_text directly instead of going through the property, so it
        /// needs its own hook or a whole class of strings would slip past unshaped.
        /// </summary>
        [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string) })]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Low)]
        public static void SetText_Prefix(TMP_Text __instance, ref string sourceText)
        {
            Process(__instance, ref sourceText);
        }

        private static void Process(TMP_Text instance, ref string value)
        {
            if (Suppress) return;
            if (!Plugin.ArabicActive) return;
            if (string.IsNullOrEmpty(value)) return;

            // Single-pass rejection so non-Arabic strings — the overwhelming majority, including
            // per-frame counters — cost almost nothing.
            if (!ArabicShaper.ContainsArabic(value))
            {
                PassthroughCount++;
                Diagnostics.TextDiagnostics.NoteBypass(instance, value);
                return;
            }

            Suppress = true;
            try
            {
                Diagnostics.TextDiagnostics.NoteArabicText(instance, value);

                var shaped = ArabicShaper.Shape(value);
                if (!ReferenceEquals(shaped, value))
                {
                    value = shaped;
                    ShapedCount++;
                }

                TextDirector.ApplyRtl(instance);
            }
            catch (System.Exception e)
            {
                Log.WarnOnce("tmp-hook", $"Text hook failed; passing text through unchanged: {e.Message}");
            }
            finally
            {
                Suppress = false;
            }
        }
    }
}
