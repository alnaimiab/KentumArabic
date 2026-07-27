using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using PixelCrushers;
using PixelCrushers.DialogueSystem;
using KentumArabic.Util;

namespace KentumArabic.Dump
{
    /// <summary>
    /// Exports every translatable string in the game to TSV translation workbooks.
    ///
    /// This reads the live objects the game itself reads, which makes it authoritative by
    /// construction — no type-tree generation, no asset extraction, and no drift when the game
    /// updates. Re-running it after a patch and diffing the output shows exactly which strings
    /// the developers added, changed or removed.
    ///
    /// The output contains the developers' original English and Spanish text and is therefore
    /// their copyrighted content. It is written to _dump/, which is git-ignored, and must never
    /// be redistributed. Published translation files carry keys and the Arabic column only.
    /// </summary>
    public static class StringDumper
    {
        /// <summary>Reference language shown next to English to guide the translator.</summary>
        private const string ReferenceLanguage = "Spanish";

        private static readonly Regex PlaceholderRx = new Regex(@"\{\d+(?::[^}]*)?\}", RegexOptions.Compiled);
        private static readonly Regex RichTextRx = new Regex(@"<[^>]+>", RegexOptions.Compiled);

        // Kentum's own markup: replaced with a control-scheme icon by
        // InputManager.ReplaceInputControlStringsWithIcons before the text is displayed.
        // Translators must carry these through untouched, so they get their own flag.
        private static readonly Regex InputTagRx = new Regex(@"<input=[^>]+>", RegexOptions.Compiled);

        public static void DumpAll(string outDir)
        {
            Log.Try("Dumping translation workbook", () =>
            {
                Directory.CreateDirectory(outDir);

                int ui = DumpTextTable(outDir);
                int dlg = DumpDialogue(outDir, out int actors);

                WriteMeta(outDir, ui, dlg, actors);

                Log.Info($"Translation workbook written to {outDir}\n" +
                         $"  {ui} text table key(s), {dlg} dialogue entry field(s), {actors} actor name(s).\n" +
                         "  Translate the 'ar' column, then copy the files into strings/ and press Ctrl+Alt+R.");
            });
        }

        // ---------------------------------------------------------------------------------
        // Text table: UI, items, technologies, world objects, enemies, ...
        // ---------------------------------------------------------------------------------
        private static int DumpTextTable(string outDir)
        {
            var uilm = UILocalizationManager.instance;
            if (uilm == null || uilm.textTable == null)
            {
                Log.Warn("No UILocalizationManager/TextTable available yet — open the main menu first.");
                return 0;
            }

            var tt = uilm.textTable;
            int defaultId = tt.GetLanguageID("Default");
            int refId = tt.GetLanguageID(ReferenceLanguage);

            // Group by key prefix so translators get reviewable files instead of one huge sheet,
            // and so several people can work without merge conflicts.
            //
            // Buckets are derived from the keys themselves rather than a hardcoded list: the
            // game has ~30 distinct prefixes and adds more over time, and a stale whitelist would
            // quietly dump most of the corpus into one enormous "misc" file.
            var names = new List<string>();
            foreach (var kv in tt.fields)
            {
                var f = kv.Value;
                if (f != null && !string.IsNullOrEmpty(f.fieldName)) names.Add(f.fieldName);
            }

            var prefixCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                var p = PrefixOf(name);
                prefixCounts.TryGetValue(p, out var n);
                prefixCounts[p] = n + 1;
            }

            var buckets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            int total = 0;

            foreach (var kv in tt.fields)
            {
                var field = kv.Value;
                if (field == null || string.IsNullOrEmpty(field.fieldName)) continue;

                var name = field.fieldName;
                var en = field.GetTextForLanguage(defaultId) ?? "";
                var es = refId > 0 ? (field.GetTextForLanguage(refId) ?? "") : "";
                var ar = Plugin.Translations != null && Plugin.Translations.Ui.TryGetValue(name, out var existing) ? existing : "";

                // Prefixes with only a handful of keys are not worth their own file.
                var prefix = PrefixOf(name);
                var bucket = prefixCounts[prefix] >= MinBucketSize ? prefix : "misc";

                if (!buckets.TryGetValue(bucket, out var rows))
                {
                    rows = new List<string> { "key\tar\ten\tes\tflags" };
                    buckets[bucket] = rows;
                }

                rows.Add($"{Esc(name)}\t{Esc(ar)}\t{Esc(en)}\t{Esc(es)}\t{Flags(en)}");
                total++;
            }

            foreach (var kv in buckets)
            {
                var path = Path.Combine(outDir, $"ui_{kv.Key}.tsv");
                kv.Value.Sort(1, kv.Value.Count - 1, StringComparer.Ordinal);
                File.WriteAllText(path, string.Join("\n", kv.Value.ToArray()) + "\n", new UTF8Encoding(true));
            }

            return total;
        }

        private const int MinBucketSize = 20;

        private static string PrefixOf(string key)
        {
            int i = key.IndexOf('_');
            return i <= 0 ? "misc" : key.Substring(0, i);
        }

        // ---------------------------------------------------------------------------------
        // Dialogue database
        // ---------------------------------------------------------------------------------
        private static int DumpDialogue(string outDir, out int actorCount)
        {
            actorCount = 0;

            var db = DialogueManager.masterDatabase;
            if (db == null)
            {
                Log.Warn("Dialogue database not loaded yet — start or load a game, then dump again.");
                return 0;
            }

            var actorNames = new Dictionary<int, string>();
            foreach (var a in db.actors)
            {
                if (a == null) continue;
                actorNames[a.id] = Field.LookupValue(a.fields, "Display Name");
                if (string.IsNullOrEmpty(actorNames[a.id])) actorNames[a.id] = a.Name ?? $"actor{a.id}";
            }

            // Actors -------------------------------------------------------------------------
            var actorRows = new List<string> { "key\tar\ten\tes" };
            foreach (var a in db.actors)
            {
                if (a == null) continue;
                var en = Field.LookupValue(a.fields, "Display Name") ?? a.Name ?? "";
                if (string.IsNullOrEmpty(en)) continue;
                var es = Field.LookupValue(a.fields, "Display Name " + ReferenceLanguage) ?? "";
                var ar = Plugin.Translations != null && Plugin.Translations.Actors.TryGetValue(en, out var e) ? e : "";
                actorRows.Add($"{Esc(en)}\t{Esc(ar)}\t{Esc(en)}\t{Esc(es)}");
                actorCount++;
            }
            File.WriteAllText(Path.Combine(outDir, "actors.tsv"),
                string.Join("\n", actorRows.ToArray()) + "\n", new UTF8Encoding(true));

            // Dialogue -----------------------------------------------------------------------
            // Speaker and listener are not optional metadata: Arabic inflects verbs and
            // adjectives for the gender of the person addressed, so a translator cannot render
            // even "you found it" correctly without knowing who is talking to whom.
            var rows = new List<string> { "key\tar\ten\tes\tspeaker\tlistener\tconversation\torder\tflags" };
            int count = 0;

            foreach (var conv in db.conversations)
            {
                if (conv == null || conv.dialogueEntries == null) continue;
                var convTitle = conv.Title ?? $"conversation{conv.id}";

                int order = 0;
                foreach (var entry in conv.dialogueEntries)
                {
                    if (entry == null) continue;
                    order++;

                    actorNames.TryGetValue(entry.ActorID, out var speaker);
                    actorNames.TryGetValue(entry.ConversantID, out var listener);

                    // "Dialogue Text" localizes to a field named after the language alone
                    // ("Spanish"); every other field takes a " <Language>" suffix.
                    AddRow(rows, conv.id, entry.id, "Dialogue Text",
                           Field.LookupValue(entry.fields, "Dialogue Text"),
                           Field.LookupValue(entry.fields, ReferenceLanguage),
                           speaker, listener, convTitle, order, ref count);

                    AddRow(rows, conv.id, entry.id, "Menu Text",
                           Field.LookupValue(entry.fields, "Menu Text"),
                           Field.LookupValue(entry.fields, "Menu Text " + ReferenceLanguage),
                           speaker, listener, convTitle, order, ref count);
                }
            }

            File.WriteAllText(Path.Combine(outDir, "dialogue.tsv"),
                string.Join("\n", rows.ToArray()) + "\n", new UTF8Encoding(true));

            return count;
        }

        private static void AddRow(List<string> rows, int convId, int entryId, string fieldName,
                                   string en, string es, string speaker, string listener,
                                   string convTitle, int order, ref int count)
        {
            if (string.IsNullOrWhiteSpace(en)) return;

            var key = $"{convId}:{entryId}:{fieldName}";
            var ar = Plugin.Translations != null && Plugin.Translations.Dialogue.TryGetValue(key, out var e) ? e : "";

            rows.Add($"{Esc(key)}\t{Esc(ar)}\t{Esc(en)}\t{Esc(es)}\t{Esc(speaker)}\t{Esc(listener)}\t" +
                     $"{Esc(convTitle)}\t{order}\t{Flags(en)}");
            count++;
        }

        // ---------------------------------------------------------------------------------
        private static void WriteMeta(string outDir, int ui, int dialogue, int actors)
        {
            var buildGuid = ReadBuildGuid();
            var json =
                "{\n" +
                $"  \"pluginVersion\": \"{Plugin.PluginVersion}\",\n" +
                $"  \"gameVersion\": \"{UnityEngine.Application.version}\",\n" +
                $"  \"buildGuid\": \"{buildGuid}\",\n" +
                $"  \"unityVersion\": \"{UnityEngine.Application.unityVersion}\",\n" +
                $"  \"textTableKeys\": {ui},\n" +
                $"  \"dialogueFields\": {dialogue},\n" +
                $"  \"actors\": {actors}\n" +
                "}\n";
            File.WriteAllText(Path.Combine(outDir, "_meta.json"), json, new UTF8Encoding(false));
        }

        /// <summary>
        /// The build GUID identifies the exact game build. Comparing it on startup is how we
        /// detect that Steam has patched the game and the workbook should be re-dumped.
        /// </summary>
        public static string ReadBuildGuid()
        {
            try
            {
                var path = Path.Combine(UnityEngine.Application.dataPath, "boot.config");
                if (!File.Exists(path)) return "";
                foreach (var line in File.ReadAllLines(path))
                    if (line.StartsWith("build-guid=", StringComparison.Ordinal))
                        return line.Substring("build-guid=".Length).Trim();
            }
            catch { /* not important enough to report */ }
            return "";
        }

        /// <summary>
        /// Marks strings that need care: format placeholders must survive translation intact,
        /// and rich text tags must not be reordered into nonsense.
        /// </summary>
        private static string Flags(string en)
        {
            if (string.IsNullOrEmpty(en)) return "";
            var f = new List<string>(3);
            if (PlaceholderRx.IsMatch(en)) f.Add("format");
            if (InputTagRx.IsMatch(en)) f.Add("input");
            // "richtext" means TMP markup specifically, so ignore the input tags counted above.
            if (RichTextRx.IsMatch(InputTagRx.Replace(en, ""))) f.Add("richtext");
            return string.Join(",", f.ToArray());
        }

        /// <summary>
        /// Keeps every row on one physical line. The game's own text table already treats a
        /// literal "\n" as a newline, so this matches its convention exactly.
        /// </summary>
        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\t", "    ").Replace("\r", "").Replace("\n", "\\n");
        }
    }
}
