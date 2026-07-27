using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using KentumArabic.Util;

namespace KentumArabic.Diagnostics
{
    /// <summary>
    /// Finds what is still missing from the translation.
    ///
    /// Two complementary views:
    ///  - <b>missing keys</b>: strings that went through Kentum's localization system but had no
    ///    Arabic entry. Tells you what to translate next, by key.
    ///  - <b>bypasses</b>: Latin text that reached a TMP component without passing through the
    ///    localization system at all. Tells you where the system itself does not reach.
    /// </summary>
    public static class TextDiagnostics
    {
        public static bool Enabled;

        private const int MaxTracked = 4000;

        private static readonly HashSet<string> _missingKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _bypasses = new HashSet<string>(StringComparer.Ordinal);

        public static int MissingKeyCount => _missingKeys.Count;
        public static int BypassCount => _bypasses.Count;

        public static void NoteMissingKey(string key)
        {
            if (!Enabled || string.IsNullOrEmpty(key)) return;
            if (_missingKeys.Count >= MaxTracked) return;
            _missingKeys.Add(key);
        }

        /// <summary>Called for text that did contain Arabic — used to exclude it from bypass reports.</summary>
        public static void NoteArabicText(TMP_Text component, string value)
        {
            // Reserved for future per-component statistics; kept as an explicit hook so the
            // text path has a single place to report from.
        }

        /// <summary>
        /// Records Latin-only text found on screen while Arabic is the active language. Skips
        /// things that must never be translated: pure numbers, and player/lobby names coming
        /// from Steam or EOS.
        /// </summary>
        public static void NoteBypass(TMP_Text component, string value)
        {
            if (!Enabled || string.IsNullOrEmpty(value)) return;
            if (_bypasses.Count >= MaxTracked) return;
            if (!LooksTranslatable(value)) return;

            var path = HierarchyPath(component);
            _bypasses.Add($"{path}\t{value.Replace("\n", "\\n").Replace("\t", " ")}");
        }

        private static bool LooksTranslatable(string s)
        {
            bool hasLetter = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) { hasLetter = true; break; }
            }
            if (!hasLetter) return false;              // pure numbers, separators, symbols
            if (s.Length > 400) return false;          // almost certainly generated content
            return true;
        }

        private static string HierarchyPath(Component c)
        {
            if (c == null) return "<null>";
            var t = c.transform;
            var sb = new StringBuilder(t.name);
            t = t.parent;
            int depth = 0;
            while (t != null && depth++ < 12)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }
            return sb.ToString();
        }

        public static void Clear()
        {
            _missingKeys.Clear();
            _bypasses.Clear();
        }

        /// <summary>Writes both reports next to the plugin for offline review.</summary>
        public static void WriteReports(string outDir)
        {
            Log.Try("Writing diagnostic reports", () =>
            {
                Directory.CreateDirectory(outDir);

                var missingPath = Path.Combine(outDir, "missing-keys.txt");
                var sorted = new List<string>(_missingKeys);
                sorted.Sort(StringComparer.Ordinal);
                File.WriteAllText(missingPath,
                    "# Localization keys with no Arabic translation, observed while playing.\n" +
                    "# Paste these into a strings/*.tsv file and translate them.\n" +
                    string.Join("\n", sorted.ToArray()) + "\n",
                    new UTF8Encoding(true));

                var bypassPath = Path.Combine(outDir, "bypass.tsv");
                var bypasses = new List<string>(_bypasses);
                bypasses.Sort(StringComparer.Ordinal);
                File.WriteAllText(bypassPath,
                    "# Latin text seen on screen while Arabic was active, that did NOT come\n" +
                    "# through Kentum's localization system. Each row is: hierarchy path <TAB> text.\n" +
                    "path\ttext\n" +
                    string.Join("\n", bypasses.ToArray()) + "\n",
                    new UTF8Encoding(true));

                Log.Info($"Diagnostics written: {sorted.Count} missing key(s) -> {missingPath}, " +
                         $"{bypasses.Count} bypass(es) -> {bypassPath}");
            });
        }
    }
}
