using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TMPro;
using KentumArabic.Util;

namespace KentumArabic.Shaping
{
    /// <summary>
    /// Manages per-component right-to-left layout state.
    ///
    /// Kentum's UI is not mirrored (a deliberate scope decision — flipping the whole canvas is
    /// high risk for little benefit, and Arabic players are used to LTR layouts with RTL text).
    /// Only text direction and horizontal alignment change.
    ///
    /// Every component's original state is recorded before it is touched so switching back to
    /// English fully restores the UI without a restart. Without that, QA needs a game restart per
    /// iteration and any player who switches away is left with a permanently mangled interface.
    /// </summary>
    public static class TextDirector
    {
        private class OriginalState
        {
            public TextAlignmentOptions Alignment;
            public bool IsRightToLeft;
            public bool Captured;
        }

        // Weak keys: entries disappear when the component is destroyed, so this cannot leak
        // across scene loads.
        private static readonly ConditionalWeakTable<TMP_Text, OriginalState> _originals =
            new ConditionalWeakTable<TMP_Text, OriginalState>();

        // Tracked separately so a language switch can walk exactly what we changed.
        private static readonly List<System.WeakReference> _touched = new List<System.WeakReference>(512);

        public static bool Enabled;

        private static readonly Dictionary<TextAlignmentOptions, TextAlignmentOptions> RtlAlignment =
            new Dictionary<TextAlignmentOptions, TextAlignmentOptions>
            {
                { TextAlignmentOptions.TopLeft,      TextAlignmentOptions.TopRight },
                { TextAlignmentOptions.Left,         TextAlignmentOptions.Right },
                { TextAlignmentOptions.BottomLeft,   TextAlignmentOptions.BottomRight },
                { TextAlignmentOptions.BaselineLeft, TextAlignmentOptions.BaselineRight },
                { TextAlignmentOptions.MidlineLeft,  TextAlignmentOptions.MidlineRight },
                { TextAlignmentOptions.CaplineLeft,  TextAlignmentOptions.CaplineRight },
                // Centered and justified alignments are intentionally left alone — they read
                // correctly in either direction and flipping them would move tuned layouts.
            };

        /// <summary>
        /// Applies right-to-left layout to a component that is about to display Arabic.
        /// Cheap and idempotent; called from the text setter hook.
        /// </summary>
        public static void ApplyRtl(TMP_Text t)
        {
            if (!Enabled || t == null) return;

            Capture(t);

            if (ArabicShaper.WantsRtlLayout && !t.isRightToLeftText)
                t.isRightToLeftText = true;

            if (RtlAlignment.TryGetValue(t.alignment, out var flipped))
                t.alignment = flipped;
        }

        private static void Capture(TMP_Text t)
        {
            if (_originals.TryGetValue(t, out var existing) && existing.Captured) return;

            var state = new OriginalState
            {
                Alignment = t.alignment,
                IsRightToLeft = t.isRightToLeftText,
                Captured = true,
            };

            _originals.Remove(t);
            _originals.Add(t, state);
            _touched.Add(new System.WeakReference(t));
        }

        /// <summary>
        /// Puts every component we modified back exactly as it was. Called when the player
        /// switches away from Arabic.
        /// </summary>
        public static void RestoreAll()
        {
            int restored = 0, dead = 0;

            for (int i = _touched.Count - 1; i >= 0; i--)
            {
                var target = _touched[i].Target as TMP_Text;
                if (target == null) { _touched.RemoveAt(i); dead++; continue; }

                if (_originals.TryGetValue(target, out var state) && state.Captured)
                {
                    target.isRightToLeftText = state.IsRightToLeft;
                    target.alignment = state.Alignment;
                    target.SetAllDirty();
                    restored++;
                }
            }

            Log.Verbose($"Restored {restored} text component(s) to their original direction/alignment ({dead} already destroyed).");
        }

        /// <summary>Drops tracking without restoring — used when tearing the plugin down.</summary>
        public static void Forget()
        {
            _touched.Clear();
        }
    }
}
