using System;
using System.Collections.Generic;
using RTLTMPro;

namespace ShaperTest
{
    /// <summary>
    /// Exercises the shaping pipeline outside the game.
    ///
    /// Rich text is the fiddly part. RTLTMPro's LigatureFixer already emits tags in readable
    /// forward order while reversing everything else, and then RichTextFixer reverses each tag
    /// range again — which is correct for RTLTMPro's own component but leaves tags backwards for
    /// the way this plugin uses the library. This harness makes that visible in a second instead
    /// of a game launch.
    /// </summary>
    internal static class Program
    {
        private static readonly (string Label, string Text)[] Cases =
        {
            ("basic",        "مرحبا بك في كنتوم"),
            ("digits",       "الطاقة: 150 / 300"),
            ("latin inline", "تقنية Kentum المتقدمة"),
            ("rich text",    "<color=#ff4444>خطر</color> — المفاعل غير مستقر"),
            ("size tag",     "الكمية ( 12 <size=70%>/ 40</size> )"),
            ("nested tags",  "<b>تنبيه</b>: <color=red>الطاقة</color> منخفضة"),
            ("lam-alef",     "لا إله إلا الله"),
            ("punctuation",  "ما هذا؟ إنه (صندوق) كبير، جدًا!"),
            ("format",       string.Format("تم تحديث الإنجاز: {0} ({1}/{2})", "صائد النيازك", 3, 5)),
            ("tag only",     "<color=#00ff00>مرحبا</color>"),
            ("gt in prose",  "الطاقة > 100 وحدة"),
            ("tanween+space","جارٍ الاتصال..."),
            ("tanween2",     "جدًا كبير"),
        };

        private static int Main(string[] args)
        {
            bool failed = false;

            foreach (var (label, text) in Cases)
            {
                Console.WriteLine($"── {label}");
                Console.WriteLine($"   input      : {text}");

                var tagsOn = Run(text, fixTextTags: true, reverse: false);
                var tagsOff = Run(text, fixTextTags: false, reverse: false);
                Console.WriteLine($"   tags=true  : {tagsOn}");
                Console.WriteLine($"   tags=false : {tagsOff}");

                var rtl = ShapeForRtlLayout(text);
                Console.WriteLine($"   RtlLayout  : {rtl}");

                if (text.Contains("<"))
                {
                    // The tag must survive as something TMP's parser will still recognise.
                    bool ok = ContainsWellFormedTag(rtl, text);
                    Console.WriteLine($"   tag check  : {(ok ? "OK" : "BROKEN")}");
                    if (!ok) failed = true;
                }

                Console.WriteLine();
            }

            Console.WriteLine(failed ? "RESULT: at least one rich-text case is broken." : "RESULT: all cases OK.");
            return failed ? 1 : 0;
        }

        private static string Run(string input, bool fixTextTags, bool reverse)
        {
            var buf = new FastStringBuilder(Math.Max(4096, input.Length * 4));
            RTLSupport.FixRTL(input, buf, farsi: false, fixTextTags: fixTextTags, preserveNumbers: true);
            if (reverse) buf.Reverse();
            return buf.ToString();
        }

        /// <summary>
        /// Mirrors ArabicShaper's RtlLayout path so both stay in step.
        ///
        /// fixTextTags is deliberately false. LigatureFixer already emits rich text tags in
        /// readable forward order at their correct visual positions; RichTextFixer then reverses
        /// each tag range again, which leaves them as "&gt;roloc&lt;" and makes TMP print them as
        /// literal text. Skipping that stage and flipping the tags ourselves after the global
        /// reverse gives correct tags in both directions.
        /// </summary>
        private static string ShapeForRtlLayout(string input)
        {
            var buf = new FastStringBuilder(Math.Max(4096, input.Length * 4));
            RTLSupport.FixRTL(input, buf, farsi: false, fixTextTags: false, preserveNumbers: true);
            buf.Reverse();
            UnreverseTags(buf);
            return buf.ToString();
        }

        private static void UnreverseTags(FastStringBuilder text)
        {
            const int MaxTagLength = 128;
            for (int i = 0; i < text.Length; i++)
            {
                if (text.Get(i) != '>') continue;

                int end = -1;
                int limit = Math.Min(text.Length, i + MaxTagLength);
                for (int j = i + 1; j < limit; j++)
                {
                    int c = text.Get(j);
                    if (c == '>') break;
                    if (c == '<')
                    {
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

        /// <summary>
        /// Every tag present in the source must still appear verbatim in the output. Anything
        /// else means TMP will print it as literal text instead of applying it.
        /// </summary>
        private static bool ContainsWellFormedTag(string output, string source)
        {
            foreach (var tag in ExtractTags(source))
                if (!output.Contains(tag))
                {
                    Console.WriteLine($"   missing    : {tag}");
                    return false;
                }
            return true;
        }

        private static IEnumerable<string> ExtractTags(string s)
        {
            int i = 0;
            while (i < s.Length)
            {
                int open = s.IndexOf('<', i);
                if (open < 0) yield break;
                int close = s.IndexOf('>', open + 1);
                if (close < 0) yield break;
                yield return s.Substring(open, close - open + 1);
                i = close + 1;
            }
        }
    }
}
