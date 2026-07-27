using System;
using System.Collections.Generic;
using RTLTMPro;

namespace KentumArabic.Shaping
{
    /// <summary>
    /// How Arabic text is prepared before it reaches TextMeshPro.
    ///
    /// TextMeshPro is a glyph-atlas renderer: it has no OpenType shaping engine and no
    /// implementation of the Unicode Bidirectional Algorithm. Given logical-order Arabic it
    /// looks up isolated letter forms and lays them out left-to-right, producing disconnected,
    /// backwards text. Everything in this file exists to compensate for that.
    /// </summary>
    public enum ShapingMode
    {
        /// <summary>Pass text through untouched. For A/B comparison only.</summary>
        None = 0,

        /// <summary>
        /// Shape + reorder to visual order, leave TMP in normal left-to-right layout.
        /// Renders correctly on a single line, but multi-line text wraps bottom-to-top because
        /// TMP finds break points scanning a visual-order string from the left.
        /// </summary>
        VisualOrder = 1,

        /// <summary>
        /// Shape + reorder, then reverse, and put TMP into right-to-left layout mode.
        /// TMP then consumes the string front-to-back while advancing the pen leftward, which
        /// restores correct word wrap and correct typewriter reveal direction.
        /// This is the recommended mode.
        /// </summary>
        RtlLayout = 2,
    }

    /// <summary>
    /// Converts logical-order Arabic into something TextMeshPro can render.
    ///
    /// Translation data is authored and stored as plain logical Arabic so it stays reviewable,
    /// diffable and usable in CAT tools. All presentation-form conversion happens here, at the
    /// last possible moment before the string reaches TMP.
    /// </summary>
    public static class ArabicShaper
    {
        public static ShapingMode Mode = ShapingMode.RtlLayout;

        /// <summary>Keep Western digits as-is rather than converting to Arabic-Indic.</summary>
        public static bool PreserveNumbers = true;

        /// <summary>
        /// Protect rich text tags from being reordered. Kentum's UI is dense with
        /// &lt;color&gt;/&lt;size&gt;/&lt;b&gt; markup, so this stays on.
        ///
        /// Note this is handled here rather than by RTLTMPro. Its LigatureFixer already emits
        /// tags in readable forward order at the correct visual positions, and RichTextFixer then
        /// reverses each tag range a second time — leaving "&gt;roloc&lt;", which TMP prints as
        /// literal text instead of applying. So FixRTL is always called with fixTextTags:false
        /// and the tags are restored here, after the global reverse.
        /// </summary>
        public static bool FixTextTags = true;

        // RTLSupport's own buffers grow on demand, but starting large avoids repeated
        // reallocation on the long item/tech descriptions that dominate the corpus.
        private const int InitialBufferSize = 4096;

        [ThreadStatic] private static FastStringBuilder _buffer;

        // Shaping is deterministic, so the same input always maps to the same output.
        // set_text is a hot setter (animated counters hit it every frame), so results are cached.
        private const int MaxCacheEntries = 8192;
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(1024, StringComparer.Ordinal);

        /// <summary>True when the active mode needs TMP_Text.isRightToLeftText set.</summary>
        public static bool WantsRtlLayout => Mode == ShapingMode.RtlLayout;

        public static void ClearCache()
        {
            _cache.Clear();
        }

        public static int CacheCount => _cache.Count;

        /// <summary>
        /// Cheap rejection test. Runs on every single set_text call, so it must stay a single
        /// pass with no allocation. Covers Arabic, Arabic Supplement, Extended-A and both
        /// Presentation Forms blocks.
        /// </summary>
        public static bool ContainsArabic(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x0600) continue;
                if ((c >= 0x0600 && c <= 0x06FF) ||   // Arabic
                    (c >= 0x0750 && c <= 0x077F) ||   // Arabic Supplement
                    (c >= 0x08A0 && c <= 0x08FF) ||   // Arabic Extended-A
                    (c >= 0xFB50 && c <= 0xFDFF) ||   // Presentation Forms-A
                    (c >= 0xFE70 && c <= 0xFEFF))     // Presentation Forms-B
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True if the string already holds presentation forms, meaning it has been shaped once
        /// already. Guards against double-shaping when a component's text is read back and re-set.
        /// </summary>
        public static bool IsAlreadyShaped(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 0xFE70 && c <= 0xFEFF) || (c >= 0xFB50 && c <= 0xFDFF))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Shape a logical-order Arabic string for display. Returns the input unchanged when
        /// there is nothing to do, so callers can use this unconditionally.
        /// </summary>
        public static string Shape(string input)
        {
            if (Mode == ShapingMode.None) return input;
            if (string.IsNullOrEmpty(input)) return input;
            if (!ContainsArabic(input)) return input;
            if (IsAlreadyShaped(input)) return input;

            if (_cache.TryGetValue(input, out var cached))
                return cached;

            string result;
            try
            {
                result = ShapeUncached(input);
            }
            catch (Exception e)
            {
                // Never let a shaping failure take down the frame: show the raw text instead.
                Util.Log.WarnOnce("shape-fail", $"Arabic shaping failed, falling back to raw text: {e.Message}");
                result = input;
            }

            if (_cache.Count >= MaxCacheEntries) _cache.Clear();
            _cache[input] = result;
            return result;
        }

        /// <summary>
        /// Shapes each line independently.
        ///
        /// The RtlLayout pass reverses the whole string, which for multi-line text would also
        /// reverse the order of the lines themselves — a two-line message would read bottom-line
        /// first. Splitting first keeps lines in order while still reversing within each one.
        /// </summary>
        private static string ShapeUncached(string input)
        {
            if (input.IndexOf('\n') < 0) return ShapeSingleLine(input);

            var lines = input.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                lines[i] = ShapeSingleLine(lines[i]);
            return string.Join("\n", lines);
        }

        private static string ShapeSingleLine(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var buf = _buffer ??= new FastStringBuilder(InitialBufferSize);
            buf.Clear();

            // Produces presentation forms in visual order. fixTextTags stays false — see the
            // FixTextTags field for why RTLTMPro's own tag pass has to be skipped here.
            RTLSupport.FixRTL(input, buf, farsi: false, fixTextTags: false, preserveNumbers: PreserveNumbers);

            if (Mode == ShapingMode.VisualOrder)
                return buf.ToString();

            // RtlLayout: TMP will consume the string front-to-back while advancing leftward,
            // which reverses it on screen. Pre-reverse so the two cancel out. For pure Arabic
            // this lands back at logical order; embedded Latin and digit runs stay pre-flipped
            // so TMP's reversal renders them the right way round.
            buf.Reverse();

            // The whole-string reverse also flipped every tag into ">roloc<" form.
            if (FixTextTags) UnreverseTags(buf);

            return buf.ToString();
        }

        /// <summary>
        /// Mirror of RichTextFixer.Fix for a reversed buffer: finds tags that now read '>' first
        /// and flips each back. RichTextFixer itself cannot be reused here because it scans for
        /// '&lt;' as the tag opener and would pair delimiters across adjacent tags.
        /// </summary>
        private static void UnreverseTags(FastStringBuilder text)
        {
            // TMP's own parser gives up well before this; the bound stops a stray '>' in prose
            // from scanning the entire string.
            const int MaxTagLength = 128;

            for (int i = 0; i < text.Length; i++)
            {
                if (text.Get(i) != '>') continue;

                int end = -1;
                int limit = Math.Min(text.Length, i + MaxTagLength);
                for (int j = i + 1; j < limit; j++)
                {
                    int c = text.Get(j);
                    // A second '>' before any '<' means this was not a tag.
                    if (c == '>') break;
                    if (c == '<')
                    {
                        // Tags never open with a space; in reversed form that space sits just
                        // before the closing '<'.
                        if (j - 1 > i && text.Get(j - 1) == ' ') break;
                        end = j;
                        break;
                    }
                }

                if (end < 0) continue;

                text.Reverse(i, end - i + 1);
                i = end;
            }
        }
    }
}
