using System;
using HarmonyLib;
using TMPro;
using Tlon.Localization;
using KentumArabic.Shaping;
using KentumArabic.Util;

namespace KentumArabic.Injection
{
    /// <summary>
    /// Applies right-to-left layout to Kentum's own localized text components.
    ///
    /// Shaping happens in the <c>Localization.Localize</c> postfix, but shaped text still has to
    /// be told which way to flow: TextMeshPro lays glyphs out left-to-right unless
    /// <c>isRightToLeftText</c> is set, and alignment has to flip with it.
    ///
    /// These hooks live on Kentum's own classes rather than on TextMeshPro because patches
    /// against the game's assemblies verifiably take effect, while a detour on
    /// <c>TMP_Text.set_text</c> reports success and then never runs.
    /// </summary>
    public static class LocalizedTextPatches
    {
        public static long DirectedCount;

        /// <summary>
        /// Runs right after LocalizeText has assigned the string, so the component already holds
        /// its final Arabic text and only needs its direction set.
        /// </summary>
        [HarmonyPatch(typeof(LocalizedStaticText), "LocalizeText")]
        public static class LocalizedStaticText_Patch
        {
            public static void Postfix(LocalizedStaticText __instance)
            {
                Direct(__instance, "txt");
            }
        }

        [HarmonyPatch(typeof(LocalizedPerPlatformStaticText), "LocalizeText")]
        public static class LocalizedPerPlatformStaticText_Patch
        {
            public static void Postfix(LocalizedPerPlatformStaticText __instance)
            {
                Direct(__instance, "txt");
            }
        }

        /// <summary>Reads the component's private TMP_Text field and applies direction to it.</summary>
        private static void Direct(object owner, string fieldName)
        {
            if (!Plugin.ArabicActive || owner == null) return;

            try
            {
                var field = AccessTools.Field(owner.GetType(), fieldName);
                if (field == null) return;

                if (field.GetValue(owner) is TMP_Text txt && txt != null)
                {
                    TextDirector.ApplyRtl(txt);
                    DirectedCount++;
                }
            }
            catch (Exception e)
            {
                Log.WarnOnce("direct-fail", $"Could not set text direction: {e.Message}");
            }
        }

        /// <summary>
        /// Sweeps every text component on screen and gives any that already holds Arabic the
        /// right direction and alignment.
        ///
        /// Needed because plenty of text never passes through LocalizedStaticText — runtime-
        /// composed strings, dialogue, tooltips. Cheap enough to run on a language change and
        /// after each scene load.
        /// </summary>
        public static int DirectAllOnScreen()
        {
            if (!Plugin.ArabicActive) return 0;

            int n = 0;
            foreach (var t in UnityEngine.Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (t == null) continue;
                var s = t.text;
                if (string.IsNullOrEmpty(s) || !ArabicShaper.ContainsArabic(s)) continue;

                TextDirector.ApplyRtl(t);
                n++;
            }
            return n;
        }
    }
}
