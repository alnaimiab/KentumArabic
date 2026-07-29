using System;
using System.Collections.Generic;
using System.IO;
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
            ("save slot info","نسخة {0} - {1}"),
            ("composed day", "اليوم 18"),
            ("composed clone","نسخة 17 - عادي"),
            ("map word",     "الخريطة"),
            ("save day",     "اليوم {0}"),
            ("invalid save", "إصدار غير صالح ({0}.{1})"),
            ("multiline",    "هل تريد الخروج فعلًا؟\nسيُحفظ تقدّمك حتى هذه النقطة."),
            ("long para",    "هذه فقرة طويلة بما يكفي لتلتف على عدة أسطر داخل الصندوق، والغرض منها " +
                             "التحقق من أن ترتيب الأسطر يُقرأ من الأعلى إلى الأسفل بشكل صحيح."),
        };

        private static int Main(string[] args)
        {
            // These two run before anything else touches the shaper. Placed after the regression
            // cases they would read those cases' cached results, which were produced under a
            // different mode - the first version of this tool did exactly that and reported a
            // menu string as unreversed.
            //
            // "--glyphs <dir> <out>" writes every codepoint the shaped translation actually needs.
            // A font only has to cover that set, not the whole Presentation Forms-B block: most of
            // the block is Persian and Urdu letters Arabic never produces, so screening candidates
            // against the block rejects good fonts while screening against this file does not.
            if (args.Length == 3 && args[0] == "--glyphs")
                return DumpGlyphs(args[1], args[2]);

            // "--shape <in> <out>" shapes each line in visual order, for font previews: preview
            // tools lay out left to right and do no OpenType shaping, the same two properties
            // TextMeshPro has, so what they draw is what the game will draw.
            if (args.Length == 3 && args[0] == "--shape")
                return ShapeLines(args[1], args[2]);

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

                // Format placeholders must survive byte-for-byte. A reversed "{0}" makes
                // string.Format throw, and Kentum formats save-slot labels while building the
                // panel — so the whole Load screen dies. This suite checked rich text tags but
                // not placeholders, which is exactly why that shipped.
                foreach (var ph in ExtractPlaceholders(text))
                {
                    if (!shaped.Contains(ph))
                    {
                        Console.WriteLine($"   FAIL: format placeholder lost or reversed: {ph}");
                        failed = true;
                    }
                }

                if (!BracesBalanced(shaped))
                {
                    Console.WriteLine("   FAIL: unbalanced braces in output — string.Format would throw");
                    failed = true;
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

            // Hand-picked cases only cover the failure modes already known. The placeholder bug
            // that broke the Load screen shipped because no case happened to contain "{0}".
            // Running the real shipped corpus removes that gap: every string the player can
            // actually see is checked, so a new markup form in new content fails here first.
            foreach (var dir in args)
                failed |= !CheckCorpus(dir);

            Console.WriteLine(failed ? "RESULT: FAILURES ABOVE" : "RESULT: all cases OK.");
            return failed ? 1 : 0;
        }

        private static int ShapeLines(string inPath, string outPath)
        {
            ArabicShaper.Mode = ShapingMode.VisualOrder;
            ArabicShaper.PreserveNumbers = true;

            var shaped = new List<string>();
            foreach (var line in File.ReadAllLines(inPath))
                shaped.Add(line.Length == 0 ? line : ArabicShaper.Shape(line));

            File.WriteAllLines(outPath, shaped, new System.Text.UTF8Encoding(false));
            Console.WriteLine($"{shaped.Count} line(s) shaped -> {outPath}");
            return 0;
        }

        private static int DumpGlyphs(string stringsDir, string outPath)
        {
            ArabicShaper.Mode = ShapingMode.RtlLayout;
            ArabicShaper.PreserveNumbers = true;

            var needed = new SortedSet<int>();
            foreach (var file in Directory.GetFiles(stringsDir, "*.tsv", SearchOption.AllDirectories))
                foreach (var raw in File.ReadAllLines(file))
                {
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    var cols = raw.Split('\t');
                    if (cols.Length < 2 || string.IsNullOrWhiteSpace(cols[1])) continue;
                    if (cols[0] == "key" || cols[0] == "field" || cols[0] == "id") continue;

                    foreach (var ch in ArabicShaper.Shape(cols[1]))
                        needed.Add(ch);
                }

            using (var w = new StreamWriter(outPath, false, new System.Text.UTF8Encoding(false)))
                foreach (var cp in needed)
                    w.WriteLine($"U+{cp:X4}");

            Console.WriteLine($"{needed.Count} distinct codepoints required by the shaped translation -> {outPath}");
            return 0;
        }

        private static bool CheckCorpus(string stringsDir)
        {
            if (!Directory.Exists(stringsDir))
            {
                Console.WriteLine($"── corpus: {stringsDir} not found, skipped");
                return true;
            }

            int rows = 0, bad = 0;
            Console.WriteLine($"── corpus: {stringsDir}");

            foreach (var file in Directory.GetFiles(stringsDir, "*.tsv", SearchOption.AllDirectories))
            {
                foreach (var raw in File.ReadAllLines(file))
                {
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    var cols = raw.Split('\t');
                    if (cols.Length < 2) continue;
                    if (cols[0] == "key" || cols[0] == "field" || cols[0] == "id") continue;

                    var key = cols[0];
                    var text = cols[1];
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    rows++;
                    var shaped = ArabicShaper.Shape(text);
                    var name = Path.GetFileName(file);

                    foreach (var tag in ExtractTags(text))
                        if (!shaped.Contains(tag))
                            bad += Fail(name, key, $"rich text tag lost: {tag}");

                    foreach (var ph in ExtractPlaceholders(text))
                        if (!shaped.Contains(ph))
                            bad += Fail(name, key, $"format placeholder lost or reversed: {ph}");

                    if (!BracesBalanced(shaped))
                        bad += Fail(name, key, "unbalanced braces — string.Format would throw");

                    // Pixel Crushers consumes [emN] in FormattedText.Parse before TMP sees it,
                    // so the shaper should never meet one. If a path ever delivers it raw, the
                    // reversal would turn "[em2]" into "]2me[" and render as literal garbage.
                    foreach (var em in ExtractEmphasis(text))
                        if (!shaped.Contains(em))
                            bad += Fail(name, key, $"emphasis code reversed: {em} — check the Parse path");

                    // The shaper recognises already-shaped text by the presentation forms it
                    // emitted. A string of Latin plus Arabic punctuation only — "O.R.B.؟" — never
                    // produces one, so there is nothing to recognise and shaping it twice flips it
                    // back. That is undetectable by design, not a defect: TMP always hands the
                    // preprocessor the original m_text, never its own previous output.
                    if (HasPresentationForms(shaped) && ArabicShaper.Shape(shaped) != shaped)
                    {
                        bad += Fail(name, key, "not idempotent — double shaping changes it");
                        Console.WriteLine($"        in   : {text}");
                        Console.WriteLine($"        once : {shaped}");
                        Console.WriteLine($"        twice: {ArabicShaper.Shape(shaped)}");
                    }
                }
            }

            Console.WriteLine(bad == 0 ? $"   OK ({rows} strings)\n" : $"   {bad} failure(s) over {rows} strings\n");
            return bad == 0;
        }

        private static int Fail(string file, string key, string message)
        {
            Console.WriteLine($"   FAIL {file} [{key}]: {message}");
            return 1;
        }

        private static IEnumerable<string> ExtractEmphasis(string s)
        {
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(s, @"\[/?em[0-9]\]"))
                yield return m.Value;
        }

        private static bool HasPresentationForms(string s)
        {
            foreach (var c in s)
                if ((c >= 0xFE70 && c <= 0xFEFF) || (c >= 0xFB50 && c <= 0xFDFF)) return true;
            return false;
        }

        /// <summary>Both numbered ({0}) and named ({itemDef:Coal}) placeholders.</summary>
        private static IEnumerable<string> ExtractPlaceholders(string s)
        {
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(s, @"\{[^{}]*\}"))
                yield return m.Value;
        }

        /// <summary>
        /// Mirrors what string.Format requires: every '{' closed by a later '}'. Reversal
        /// produces "}0{", which is balanced by count but not by order — so order is what
        /// gets checked.
        /// </summary>
        private static bool BracesBalanced(string s)
        {
            int depth = 0;
            foreach (var c in s)
            {
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth < 0) return false;
                }
            }
            return depth == 0;
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
