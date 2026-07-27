using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KentumArabic.Util;

namespace KentumArabic.Injection
{
    /// <summary>
    /// Loads the Arabic translation from loose TSV files shipped alongside the plugin.
    ///
    /// TSV rather than CSV: Kentum's strings are comma-dense and Arabic uses both ',' and '،',
    /// so CSV quoting becomes a reliability problem the moment a translator opens the file in a
    /// spreadsheet. Tabs never appear in game text, need no escaping, and diff cleanly in git —
    /// which is what makes community pull requests workable.
    ///
    /// Text is stored as plain logical-order Arabic. Shaping happens at render time
    /// (see <see cref="Shaping.ArabicShaper"/>), never here.
    /// </summary>
    public class TranslationStore
    {
        /// <summary>Text table entries: field name -> Arabic.</summary>
        public readonly Dictionary<string, string> Ui = new Dictionary<string, string>(4096, StringComparer.Ordinal);

        /// <summary>Dialogue entries: "convId:entryId:FieldName" -> Arabic.</summary>
        public readonly Dictionary<string, string> Dialogue = new Dictionary<string, string>(2048, StringComparer.Ordinal);

        /// <summary>Actor display names: actor name -> Arabic.</summary>
        public readonly Dictionary<string, string> Actors = new Dictionary<string, string>(32, StringComparer.Ordinal);

        public int FilesLoaded { get; private set; }
        public int RowsSkipped { get; private set; }

        public int TotalEntries => Ui.Count + Dialogue.Count + Actors.Count;

        /// <summary>
        /// Reads every *.tsv under <paramref name="stringsDir"/>. Which dictionary a file feeds
        /// is decided by its name, so translators can split files however they like as long as
        /// dialogue/actor files keep their prefix.
        /// </summary>
        public static TranslationStore LoadFrom(string stringsDir)
        {
            var store = new TranslationStore();

            if (!Directory.Exists(stringsDir))
            {
                Log.Warn($"Translation directory not found: {stringsDir}. The game will run in English.");
                return store;
            }

            var files = Directory.GetFiles(stringsDir, "*.tsv", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                Dictionary<string, string> target =
                    name.StartsWith("dialogue") ? store.Dialogue :
                    name.StartsWith("actors") ? store.Actors :
                    store.Ui;

                Log.Try($"Loading {Path.GetFileName(file)}", () => store.LoadFile(file, target));
            }

            Log.Info($"Translation loaded: {store.Ui.Count} UI, {store.Dialogue.Count} dialogue, " +
                     $"{store.Actors.Count} actors from {store.FilesLoaded} file(s)" +
                     (store.RowsSkipped > 0 ? $", {store.RowsSkipped} row(s) skipped" : ""));

            return store;
        }

        private void LoadFile(string path, Dictionary<string, string> target)
        {
            // UTF-8 with or without BOM; StreamReader auto-detects and strips it.
            using (var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
            {
                string line;
                int lineNo = 0;
                int keyCol = 0, arCol = -1;
                bool headerSeen = false;

                while ((line = reader.ReadLine()) != null)
                {
                    lineNo++;
                    if (line.Length == 0) continue;
                    if (line[0] == '#') continue;

                    var cols = line.Split('\t');

                    if (!headerSeen)
                    {
                        headerSeen = true;
                        // A header row is optional; detect it by looking for an "ar" column.
                        int foundKey = -1, foundAr = -1;
                        for (int i = 0; i < cols.Length; i++)
                        {
                            var h = cols[i].Trim().ToLowerInvariant();
                            if (h == "key" || h == "field" || h == "id") foundKey = i;
                            else if (h == "ar" || h == "arabic") foundAr = i;
                        }
                        if (foundAr >= 0)
                        {
                            keyCol = foundKey >= 0 ? foundKey : 0;
                            arCol = foundAr;
                            continue; // header consumed
                        }
                        // No header: assume "key<TAB>arabic".
                        arCol = 1;
                    }

                    if (cols.Length <= Math.Max(keyCol, arCol)) { RowsSkipped++; continue; }

                    var key = cols[keyCol].Trim();
                    var value = cols[arCol];

                    if (key.Length == 0) { RowsSkipped++; continue; }

                    // An empty Arabic cell means "not translated yet". Leaving it out lets
                    // TextTable fall through to English, which is why a partial translation is
                    // still perfectly playable.
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    // The game itself converts a literal "\n" into a newline when reading the
                    // text table, so keep that convention end to end.
                    target[key] = value;
                }
            }

            FilesLoaded++;
        }
    }
}
