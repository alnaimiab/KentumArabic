using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using PixelCrushers;
using Tlon.Localization;
using KentumArabic.Util;

namespace KentumArabic.Injection
{
    /// <summary>
    /// Registers Arabic as a real, selectable language in Kentum's own localization system.
    ///
    /// Kentum's language list is data-driven: <c>Localization.GetAllLanguagesNames()</c> simply
    /// enumerates <c>UILocalizationManager.instance.textTable.languages</c>. That means Arabic
    /// can be added by mutating the live TextTable — no IL patching, no hijacking an existing
    /// language slot, and every downstream consumer (LocalizedStaticText, the options dropdown,
    /// LocalizedStaticImage, ...) picks it up for free.
    /// </summary>
    public static class ArabicLanguage
    {
        /// <summary>The internal language key. Kentum uses English words, not ISO codes.</summary>
        public const string LanguageName = "Arabic";

        /// <summary>Endonym shown in the options dropdown.</summary>
        public const string LanguageEndonym = "العربية";

        /// <summary>Dropdown labels are resolved through this key pattern by OptionsPanel.</summary>
        public const string LanguageLabelKey = "UI_Language_" + LanguageName;

        private static bool _injected;
        private static List<string> _baselineOrder;

        public static int LanguageId { get; private set; } = -1;
        public static bool IsInjected => _injected;

        /// <summary>Number of text table fields that received Arabic text on the last apply.</summary>
        public static int AppliedCount { get; private set; }

        /// <summary>Translation keys that had no matching field in the text table.</summary>
        public static int OrphanKeyCount { get; private set; }

        public static void Reset()
        {
            _injected = false;
            LanguageId = -1;
        }

        /// <summary>
        /// Idempotent. Safe (and cheap) to call from many hooks — whichever one runs first wins.
        /// Returns true only on the call that actually performed the injection.
        /// </summary>
        public static bool EnsureInjected(TranslationStore store)
        {
            if (_injected) return false;

            var uilm = UILocalizationManager.instance;
            if (uilm == null) return false;

            var tt = uilm.textTable;
            if (tt == null) return false;

            try
            {
                InjectLanguage(tt);
                ApplyTranslations(tt, store);
                InvalidateLanguageCache();
                _injected = true;

                Log.Info($"Arabic registered as language id {LanguageId}. " +
                         $"{AppliedCount} field(s) translated" +
                         (OrphanKeyCount > 0 ? $", {OrphanKeyCount} unknown key(s) ignored" : "") + ".");
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"Arabic language injection failed — the game will run unmodified.\n{e}");
                return false;
            }
        }

        private static void InjectLanguage(TextTable tt)
        {
            // Capture the pre-injection order so we can prove we did not disturb it. The saved
            // preference KENTUM_PREFS_LANGUAGE is an *index* into this list, not a name, so any
            // reordering would silently switch existing players to the wrong language.
            if (_baselineOrder == null)
            {
                _baselineOrder = new List<string>();
                foreach (var kv in tt.languages) _baselineOrder.Add(kv.Key);
                Log.Verbose($"Baseline language order: {string.Join(", ", _baselineOrder.ToArray())}");
            }

            if (!tt.languages.ContainsKey(LanguageName))
            {
                // Appends with the next free id, so existing ids stay put.
                tt.AddLanguage(LanguageName);
            }

            LanguageId = tt.GetLanguageID(LanguageName);
            if (LanguageId == 0)
                throw new InvalidOperationException(
                    $"'{LanguageName}' resolved to language id 0, which is the Default (English) slot. Aborting to avoid overwriting English.");

            VerifyOrderUnchanged(tt);
        }

        /// <summary>
        /// Dictionary enumeration order is an implementation detail, not a contract. If Mono ever
        /// reorders after an insert, every saved language index would point at the wrong entry —
        /// so we check rather than assume, and say so loudly if it ever happens.
        /// </summary>
        private static void VerifyOrderUnchanged(TextTable tt)
        {
            var current = new List<string>();
            foreach (var kv in tt.languages) current.Add(kv.Key);

            for (int i = 0; i < _baselineOrder.Count; i++)
            {
                if (i >= current.Count || current[i] != _baselineOrder[i])
                {
                    Log.Error(
                        "Language order changed after adding Arabic. Saved language preferences are an " +
                        "index into this list, so existing players could be switched to the wrong language.\n" +
                        $"  before: {string.Join(", ", _baselineOrder.ToArray())}\n" +
                        $"  after:  {string.Join(", ", current.ToArray())}");
                    return;
                }
            }

            Log.Verbose($"Language order preserved; Arabic appended at index {current.Count - 1}.");
        }

        /// <summary>
        /// Writes the Arabic column into the live text table, already shaped for display.
        ///
        /// Shaping is applied here, at load time, rather than by intercepting text as it flows to
        /// TextMeshPro. Hooking proved unworkable on this build: Harmony reports patches on
        /// <c>TMP_Text.set_text</c>, <c>Localization.Localize</c> and
        /// <c>TextTable.GetFieldTextForLanguage</c> as applied, and every one of them recorded
        /// zero invocations at runtime — including against a component this plugin created
        /// itself. Baking the shaped form into the table removes Harmony from the path entirely,
        /// so the text is correct no matter which of the game's several localization routes reads
        /// it.
        ///
        /// The translation files stay plain logical Arabic; shaping happens on the way in, so
        /// they remain reviewable, diffable and re-shapeable whenever the shaper improves.
        ///
        /// Known limitation: strings containing <c>{0}</c> placeholders are shaped before their
        /// runtime values are substituted, so a number injected into an Arabic sentence can land
        /// at the wrong end. Those keys carry the "format" flag in the translation workbook.
        ///
        /// Goes through the fields dictionary directly rather than the string-keyed
        /// SetFieldTextForLanguage overloads: those call GetFieldID, which linear-scans all
        /// ~2,600 fields on every single call.
        /// </summary>
        public static int ApplyTranslations(TextTable tt, TranslationStore store)
        {
            if (LanguageId <= 0) return 0;

            int applied = 0;
            var matched = new HashSet<string>(StringComparer.Ordinal);

            foreach (var kv in tt.fields)
            {
                var field = kv.Value;
                if (field == null || string.IsNullOrEmpty(field.fieldName)) continue;

                if (store.Ui.TryGetValue(field.fieldName, out var arabic))
                {
                    // Resolve the literal "\n" escape before shaping. The table normally does this
                    // on read, but by then the reorder would have turned "\n" into "n\" and the
                    // line break would be lost — so it has to happen first.
                    field.texts[LanguageId] = Shaping.ArabicShaper.Shape(arabic.Replace("\\n", "\n"));
                    matched.Add(field.fieldName);
                    applied++;
                }
            }

            // The dropdown row for Arabic is itself a localized key, so without this the options
            // menu shows the raw string "UI_Language_Arabic".
            EnsureLabelField(tt);

            OrphanKeyCount = 0;
            foreach (var key in store.Ui.Keys)
                if (!matched.Contains(key)) OrphanKeyCount++;

            AppliedCount = applied;
            return applied;
        }

        /// <summary>
        /// Adds UI_Language_Arabic and sets it for *every* language id, not just Arabic. The
        /// dropdown resolves its labels in whatever language is currently active, so an English
        /// player browsing the language list must still see a readable entry.
        /// </summary>
        private static void EnsureLabelField(TextTable tt)
        {
            var field = tt.GetField(LanguageLabelKey);
            if (field == null)
            {
                tt.AddField(LanguageLabelKey);
                field = tt.GetField(LanguageLabelKey);
            }
            if (field == null)
            {
                Log.Warn($"Could not create field {LanguageLabelKey}; the options dropdown will show a raw key.");
                return;
            }

            var label = Shaping.ArabicShaper.Shape(LanguageEndonym);
            foreach (var lang in tt.languages)
                field.texts[lang.Value] = label;
        }

        /// <summary>
        /// Clears Kentum's cached language list.
        ///
        /// Verified against the decompiled source: GetAllLanguagesNames rebuilds only when the
        /// backing field is <c>null</c> (<c>if (supportedLanguageNames == null)</c>), so clearing
        /// the list in place would not be enough — it must be set to null.
        /// </summary>
        public static void InvalidateLanguageCache()
        {
            var field = AccessTools.Field(typeof(Localization), "supportedLanguageNames");
            if (field == null)
            {
                Log.Error("Could not find Localization.supportedLanguageNames; Arabic may not appear in Options.");
                return;
            }

            field.SetValue(null, null);

            // Prove it worked rather than hoping.
            var rebuilt = Localization.GetAllLanguagesNames();
            if (rebuilt == null || !rebuilt.Contains(LanguageName))
                Log.Error("Language cache invalidation did not take effect — Arabic will not appear in Options.");
            else
                Log.Verbose($"Language list now: {string.Join(", ", rebuilt.ToArray())}");
        }

        /// <summary>
        /// Re-applies the player's saved language once Arabic exists.
        ///
        /// This is not a convenience. <c>OptionsPanel.AddDropdownOptionItem</c> reads
        /// <c>KENTUM_PREFS_LANGUAGE</c> — an *index* into the language list — and range-checks it:
        ///
        ///     int num = GetPlayerPrefsInt("KENTUM_PREFS_" + prefKey, defaultValue);
        ///     if (num &lt; 0 || num >= optionNames.Length) num = defaultValue;
        ///     onChangeCallback(num);
        ///
        /// If the options panel builds its list before this plugin has injected, Arabic's index
        /// is out of range and the saved choice is silently discarded — a player who selected
        /// العربية would find the game back in English after restarting. Re-applying after
        /// injection closes that window regardless of initialisation order.
        ///
        /// The language *name* is preferred over the index. PixelCrushers persists it separately
        /// as a string, and a name cannot be invalidated by the list changing shape.
        /// </summary>
        public static void RestoreSavedLanguage()
        {
            try
            {
                var languages = Localization.GetAllLanguagesNames();
                if (languages == null || languages.Count == 0) return;

                string saved = null;

                // Preferred: the name PixelCrushers persisted.
                var byName = PlayerPrefs.GetString(PixelCrushersLanguageKey, string.Empty);
                if (!string.IsNullOrEmpty(byName) && languages.Contains(byName))
                    saved = byName;

                // Fallback: Kentum's own index, now that the list is complete.
                if (saved == null)
                {
                    int index = PlayerPrefs.GetInt(KentumLanguagePrefKey, -1);
                    if (index >= 0 && index < languages.Count) saved = languages[index];
                }

                if (string.IsNullOrEmpty(saved)) return;
                if (string.Equals(saved, UserSettings.currentLanguage, StringComparison.Ordinal)) return;

                Log.Info($"Restoring saved language '{saved}' (was '{UserSettings.currentLanguage}').");
                Localization.ChangeLanguage(saved);

                // Kentum keeps two independent records of the chosen language: this name, and an
                // index that drives the options dropdown. If only the name is restored they
                // disagree, and the dropdown reads "English" while the game is plainly in Arabic.
                // Writing the index back keeps both in step from here on.
                int savedIndex = languages.IndexOf(saved);
                if (savedIndex >= 0 && PlayerPrefs.GetInt(KentumLanguagePrefKey, -1) != savedIndex)
                {
                    PlayerPrefs.SetInt(KentumLanguagePrefKey, savedIndex);
                    Log.Verbose($"Synced {KentumLanguagePrefKey} to index {savedIndex} ('{saved}').");
                }
            }
            catch (Exception e)
            {
                Log.Warn($"Could not restore the saved language: {e.Message}");
            }
        }

        /// <summary>Kentum's own preference: an index into the language list.</summary>
        private const string KentumLanguagePrefKey = "KENTUM_PREFS_LANGUAGE";

        /// <summary>PixelCrushers' preference: the language name.</summary>
        private const string PixelCrushersLanguageKey = "Language";

        /// <summary>Re-reads translation files into the live table without a game restart.</summary>
        public static int Reapply(TranslationStore store)
        {
            var uilm = UILocalizationManager.instance;
            if (uilm == null || uilm.textTable == null) return 0;
            return ApplyTranslations(uilm.textTable, store);
        }

        public static bool IsArabicActive()
        {
            var lang = Localization.CurrentLanguage;
            return string.Equals(lang, LanguageName, StringComparison.Ordinal);
        }
    }
}
