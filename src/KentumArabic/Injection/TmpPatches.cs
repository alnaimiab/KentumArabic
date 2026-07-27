using System;
using System.Collections.Generic;
using System.Reflection;
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
    ///
    /// These are applied manually rather than through attributes so that each target is resolved
    /// explicitly and a failure is reported loudly. A silently unpatched text setter looks exactly
    /// like a broken translation — Arabic appears on screen but every letter is disconnected —
    /// and that is far too expensive a failure to leave undiagnosed.
    /// </summary>
    public static class TmpPatches
    {
        /// <summary>
        /// Set while we are writing back, to stop the hook re-entering itself. Also used by the
        /// test overlay, which needs to drive each shaping mode explicitly rather than have the
        /// global mode applied to it.
        /// </summary>
        public static bool Suppress;

        /// <summary>Every invocation of the hook, before any filtering. Distinguishes "the patch
        /// never runs" from "the patch runs but declines to act".</summary>
        public static long RawCallCount;

        public static long ShapedCount;
        public static long PassthroughCount;

        /// <summary>Calls that arrived while Arabic was not the active language.</summary>
        public static long InactiveCount;

        public static int PatchedCount { get; private set; }

        /// <summary>
        /// Hooks every route by which a string can reach a TMP component. Returns the number of
        /// methods successfully patched.
        /// </summary>
        public static int ApplyTo(Harmony harmony)
        {
            // Each target gets a prefix whose parameter is named after that method's own
            // parameter. Harmony binds injected arguments by name, so a single shared prefix
            // cannot serve both "value" (the property setter) and "sourceText" (SetText).
            //
            // A generic `object[] __args` prefix would avoid the duplication, but this HarmonyX
            // build passes it as null instead of rejecting it, so the hook silently does nothing —
            // which presents as Arabic rendering with every letter disconnected. Named parameters
            // fail loudly at patch time instead.
            var targets = new (MethodBase Target, string PrefixName)[]
            {
                (AccessTools.PropertySetter(typeof(TMP_Text), "text"), nameof(TextSetterPrefix)),
                (AccessTools.Method(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string) }), nameof(SetTextPrefix)),
                (AccessTools.Method(typeof(TMP_Text), nameof(TMP_Text.SetText), new[] { typeof(string), typeof(bool) }), nameof(SetTextPrefix)),
            };

            int patched = 0;

            foreach (var (target, prefixName) in targets)
            {
                if (target == null)
                {
                    Log.Warn($"TextMeshPro entry point for {prefixName} could not be resolved.");
                    continue;
                }
                try
                {
                    var prefix = new HarmonyMethod(AccessTools.Method(typeof(TmpPatches), prefixName))
                    {
                        priority = Priority.Low,
                    };
                    harmony.Patch(target, prefix);
                    patched++;
                    Log.Verbose($"Hooked {target.DeclaringType?.Name}.{target.Name}" +
                                $"({string.Join(", ", Array.ConvertAll(target.GetParameters(), p => p.ParameterType.Name))})");
                }
                catch (Exception e)
                {
                    Log.Warn($"Could not hook {target.DeclaringType?.Name}.{target.Name}: {e.Message}");
                }
            }

            PatchedCount = patched;

            if (patched == 0)
                Log.Error("No TextMeshPro text entry point could be hooked. Arabic will render as " +
                          "disconnected letters because shaping can never run.");
            else
                Log.Info($"Hooked {patched} TextMeshPro text entry point(s).");

            return patched;
        }

        /// <summary>Prefix for the <c>text</c> property setter, whose parameter is named "value".</summary>
        public static void TextSetterPrefix(TMP_Text __instance, ref string value)
        {
            value = ProcessText(__instance, value);
        }

        /// <summary>
        /// Prefix for <c>SetText</c>, whose parameter is named "sourceText". SetText writes the
        /// backing field directly and never goes through the property, so it needs its own hook.
        /// </summary>
        public static void SetTextPrefix(TMP_Text __instance, ref string sourceText)
        {
            sourceText = ProcessText(__instance, sourceText);
        }

        /// <summary>Returns the string that should be displayed, shaped if it needs to be.</summary>
        private static string ProcessText(TMP_Text instance, string value)
        {
            RawCallCount++;
            if (Suppress) return value;
            if (!Plugin.ArabicActive) { InactiveCount++; return value; }
            if (string.IsNullOrEmpty(value)) return value;

            // Single-pass rejection so non-Arabic strings — the overwhelming majority, including
            // per-frame counters — cost almost nothing.
            if (!ArabicShaper.ContainsArabic(value))
            {
                PassthroughCount++;
                Diagnostics.TextDiagnostics.NoteBypass(instance, value);
                return value;
            }

            Suppress = true;
            try
            {
                var shaped = ArabicShaper.Shape(value);
                if (!ReferenceEquals(shaped, value)) ShapedCount++;
                TextDirector.ApplyRtl(instance);
                return shaped;
            }
            catch (Exception e)
            {
                Log.WarnOnce("tmp-hook", $"Text hook failed; passing text through unchanged: {e.Message}");
                return value;
            }
            finally
            {
                Suppress = false;
            }
        }
    }
}
