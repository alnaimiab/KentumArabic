using System;
using System.Collections.Generic;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using KentumArabic.Shaping;
using KentumArabic.Util;

namespace KentumArabic.Injection
{
    /// <summary>
    /// Adds Arabic to the dialogue database.
    ///
    /// Dialogue does not live in the TextTable — it is a separate Pixel Crushers
    /// <c>DialogueDatabase</c> where each entry carries per-language fields. Kentum's Spanish is
    /// stored exactly this way, so this mirrors a mechanism the game already ships and tests,
    /// rather than inventing one.
    ///
    /// Field naming follows what the shipped data actually uses (confirmed by reading the raw
    /// database out of the asset bundle):
    ///   - the spoken subtitle localizes to a field named after the language alone: "Arabic"
    ///   - every other field takes a suffix: "Menu Text Arabic", "Display Name Arabic"
    /// <c>Field.LookupLocalizedValue</c> probes <c>title + " " + language</c> first, so
    /// "Dialogue Text Arabic" is written too — a few hundred extra fields costs nothing and
    /// covers the alternate lookup path.
    ///
    /// As with the text table, Arabic is shaped on the way in. Nothing in this build reliably
    /// intercepts text on its way to TextMeshPro, so the data itself carries the display form.
    /// </summary>
    public static class DialogueInjection
    {
        private const string SubtitleField = ArabicLanguage.LanguageName;                 // "Arabic"
        private const string SubtitleFieldAlt = "Dialogue Text " + ArabicLanguage.LanguageName;
        private const string MenuField = "Menu Text " + ArabicLanguage.LanguageName;
        private const string ActorField = "Display Name " + ArabicLanguage.LanguageName;

        public static int EntriesInjected { get; private set; }
        public static int ActorsInjected { get; private set; }

        private static DialogueDatabase _injectedInto;

        /// <summary>
        /// Writes the Arabic dialogue into the live database. Idempotent, and cheap to re-run:
        /// it returns immediately unless the database has been swapped or reset underneath us.
        /// </summary>
        public static bool Apply(TranslationStore store, bool force = false)
        {
            if (store == null || (store.Dialogue.Count == 0 && store.Actors.Count == 0)) return false;

            var db = Dump.StringDumper.FindDialogueDatabase();
            if (db == null) return false;

            if (!force && ReferenceEquals(db, _injectedInto) && StillInjected(db)) return false;

            try
            {
                int entries = 0, actors = 0;

                foreach (var conv in db.conversations)
                {
                    if (conv?.dialogueEntries == null) continue;

                    foreach (var entry in conv.dialogueEntries)
                    {
                        if (entry?.fields == null) continue;

                        if (TryGet(store, conv.id, entry.id, "Dialogue Text", out var subtitle))
                        {
                            SetField(entry.fields, SubtitleField, subtitle);
                            SetField(entry.fields, SubtitleFieldAlt, subtitle);
                            entries++;
                        }

                        if (TryGet(store, conv.id, entry.id, "Menu Text", out var menu))
                        {
                            SetField(entry.fields, MenuField, menu);
                            entries++;
                        }
                    }
                }

                foreach (var actor in db.actors)
                {
                    if (actor?.fields == null) continue;
                    var english = Field.LookupValue(actor.fields, "Display Name");
                    if (string.IsNullOrEmpty(english)) english = actor.Name;
                    if (string.IsNullOrEmpty(english)) continue;

                    if (store.Actors.TryGetValue(english, out var arabic))
                    {
                        SetField(actor.fields, ActorField, ArabicShaper.Shape(arabic.Replace("\\n", "\n")));
                        actors++;
                    }
                }

                EntriesInjected = entries;
                ActorsInjected = actors;
                _injectedInto = db;

                Log.Info($"Dialogue: {entries} field(s) and {actors} actor name(s) translated in '{db.name}'.");
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Dialogue injection failed — dialogue will stay in English.\n{e}");
                return false;
            }
        }

        private static bool TryGet(TranslationStore store, int convId, int entryId, string field, out string shaped)
        {
            shaped = null;
            var key = $"{convId}:{entryId}:{field}";
            if (!store.Dialogue.TryGetValue(key, out var arabic) || string.IsNullOrWhiteSpace(arabic))
                return false;

            // Same as the text table: resolve the literal "\n" before shaping, or the reorder
            // turns it into "n\" and the line break is lost.
            shaped = ArabicShaper.Shape(arabic.Replace("\\n", "\n"));
            return true;
        }

        private static void SetField(List<Field> fields, string title, string value)
        {
            Field.SetValue(fields, title, value, FieldType.Text);
        }

        /// <summary>
        /// Cheap canary. The database is a bundle-loaded asset and our writes are runtime-only,
        /// so a ResetDatabase or a scene change can wipe them; checking one known entry is much
        /// cheaper than re-walking thousands.
        /// </summary>
        private static bool StillInjected(DialogueDatabase db)
        {
            foreach (var conv in db.conversations)
            {
                if (conv?.dialogueEntries == null) continue;
                foreach (var entry in conv.dialogueEntries)
                {
                    if (entry?.fields == null) continue;
                    if (!string.IsNullOrEmpty(Field.LookupValue(entry.fields, SubtitleField)))
                        return true;
                    // Only the first entry that should have had a translation matters.
                    if (Plugin.Translations != null &&
                        Plugin.Translations.Dialogue.ContainsKey($"{conv.id}:{entry.id}:Dialogue Text"))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Resetting the database reverts it to the asset as shipped, discarding our fields.
        /// </summary>
        [HarmonyPatch(typeof(DialogueManager), nameof(DialogueManager.ResetDatabase), new[] { typeof(DatabaseResetOptions) })]
        public static class ResetDatabase_Patch
        {
            public static void Postfix()
            {
                if (!Plugin.ArabicActive) return;
                Log.Verbose("Dialogue database was reset; re-injecting Arabic.");
                Apply(Plugin.Translations, force: true);
            }
        }
    }
}
