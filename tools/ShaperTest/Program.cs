using System;
using System.Collections.Generic;
using KentumArabic.Shaping;

namespace ShaperTest
{
    /// <summary>
    /// Regression suite for the shaping pipeline, running the real
    /// <see cref="ArabicShaper"/> outside the game.
    ///
    /// Two classes of bug this exists to catch:
    ///  - rich text tags surviving the reorder (RTLTMPro's own tag pass double-reverses them)
    ///  - text coming back unshaped, which on screen looks like disconnected isolated letters
    /// </summary>
    internal static class Program
    {
        private static readonly (string Label, string Text)[] Cases =
        {
            ("basic",        "مرحبا بك في كنتوم"),
            ("menu newgame", "لعبة جديدة"),
            ("menu load",    "تحميل"),
            ("menu options", "الخيارات"),
            ("menu quit",    "خروج"),
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
            ("tanween",      "جارٍ الاتصال..."),
            ("multiline",    "هل تريد الخروج فعلًا؟\nسيُحفظ تقدّمك حتى هذه النقطة."),
            ("long para",    "هذه فقرة طويلة بما يكفي لتلتف على عدة أسطر داخل الصندوق، والغرض منها " +
                             "التحقق من أن ترتيب الأسطر يُقرأ من الأعلى إلى الأسفل بشكل صحيح."),
        };

        private static int Main(string[] args)
        {
            bool failed = false;

            ArabicShaper.Mode = ShapingMode.RtlLayout;
            ArabicShaper.PreserveNumbers = true;

            Console.WriteLine($"mode = {ArabicShaper.Mode}\n");

            foreach (var (label, text) in Cases)
            {
                var shaped = ArabicShaper.Shape(text);
                Console.WriteLine($"── {label}");
                Console.WriteLine($"   in  : {text}");
                Console.WriteLine($"   out : {shaped}");

                // The whole point of shaping is to emit presentation forms. If none came out,
                // TextMeshPro will draw isolated, disconnected letters.
                if (!HasPresentationForms(shaped))
                {
                    Console.WriteLine("   FAIL: no presentation forms in output — text would render disconnected");
                    failed = true;
                }

                if (ReferenceEquals(shaped, text))
                {
                    Console.WriteLine("   FAIL: shaper returned the input unchanged");
                    failed = true;
                }

                foreach (var tag in ExtractTags(text))
                {
                    if (!shaped.Contains(tag))
                    {
                        Console.WriteLine($"   FAIL: rich text tag lost: {tag}");
                        failed = true;
                    }
                }

                Console.WriteLine();
            }

            // Shaping is cached and the buffer is reused, so repeated calls must stay stable.
            Console.WriteLine("── repeat/caching stability");
            foreach (var (label, text) in Cases)
            {
                var a = ArabicShaper.Shape(text);
                ArabicShaper.ClearCache();
                var b = ArabicShaper.Shape(text);
                var c = ArabicShaper.Shape(text);
                if (a != b || b != c)
                {
                    Console.WriteLine($"   FAIL: '{label}' is not stable across calls");
                    Console.WriteLine($"     first={a}");
                    Console.WriteLine($"     again={b}");
                    failed = true;
                }
            }
            Console.WriteLine(failed ? "" : "   OK\n");

            // Shaped text fed back in must not be shaped a second time.
            Console.WriteLine("── idempotence");
            foreach (var (label, text) in Cases)
            {
                var once = ArabicShaper.Shape(text);
                var twice = ArabicShaper.Shape(once);
                if (once != twice)
                {
                    Console.WriteLine($"   FAIL: '{label}' changes when shaped twice");
                    failed = true;
                }
            }
            Console.WriteLine(failed ? "" : "   OK\n");

            Console.WriteLine(failed ? "RESULT: FAILURES ABOVE" : "RESULT: all cases OK.");
            return failed ? 1 : 0;
        }

        private static bool HasPresentationForms(string s)
        {
            foreach (var c in s)
                if ((c >= 0xFE70 && c <= 0xFEFF) || (c >= 0xFB50 && c <= 0xFDFF)) return true;
            return false;
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
