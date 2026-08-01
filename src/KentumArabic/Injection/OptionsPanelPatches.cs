using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Tlon.Localization;
using KentumArabic.Util;

namespace KentumArabic.Injection
{
    /// <summary>
    /// Keeps the Options → Language dropdown honest.
    ///
    /// The dropdown is built once, from a snapshot, and then never reconciled with anything:
    ///
    ///     AddDropdownOptionItem("UI_Options_Language", "LANGUAGE",
    ///         Localization.GetAllLanguagesNames().Select(k => "UI_Language_" + k).ToArray(),
    ///         index => Localization.ChangeLanguage(Localization.GetAllLanguagesNames()[index]),
    ///         defaultValue);
    ///
    /// and inside AddDropdownOptionItem:
    ///
    ///     int num = GameHub.GetPlayerPrefsInt("KENTUM_PREFS_LANGUAGE", defaultValue);
    ///     if (num &lt; 0 || num >= optionNames.Length) num = defaultValue;   // silent reset
    ///     item.SetValue(num, triggerEvent: false);
    ///     onChangeCallback(num);
    ///
    /// Two independent things can therefore go wrong, and they look identical on screen:
    ///
    ///   1. The array was built before Arabic was registered. Arabic is missing from the list,
    ///      and a saved index of 10 is out of range, so the player's choice is silently reset to
    ///      English — losing it from the saved preference too, since the reset then calls
    ///      ChangeLanguage.
    ///   2. The array contains Arabic but the dropdown's value does not agree with the language
    ///      the game is actually running in, which is what produces the contradictory screen
    ///      where every menu is in Arabic and the language row reads ENGLISH.
    ///
    /// Rather than trying to win the ordering race — the previous attempt, which did not hold —
    /// this reconciles after the fact, on a signal that cannot be early: the panel finishing its
    /// initialisation, and again every time the player opens it. By then everything that could
    /// interfere has already run.
    ///
    /// Reconciliation is by language *name*, never by index. An index is only meaningful relative
    /// to a list whose length is exactly the thing in dispute.
    /// </summary>
    public static class OptionsPanelPatches
    {
        [HarmonyPatch(typeof(OptionsPanel), "Generate")]
        public static class Generate_Patch
        {
            public static void Prefix()
            {
                Log.InfoOnce("hook-generate", "OptionsPanel.Generate hook reached.");
                Plugin.TryEnsureInjected();
            }
        }

        [HarmonyPatch(typeof(OptionsPanel), "InitializeOptions")]
        public static class InitializeOptions_Patch
        {
            /// <summary>
            /// Injects if it has not happened yet, and records the language the player had chosen
            /// *before* the panel gets a chance to overwrite it.
            ///
            /// The recording is the important half. If the option array turns out to be missing
            /// Arabic, the panel's own range check resets the saved index and immediately calls
            /// ChangeLanguage("Default"), which overwrites the language name preference as well.
            /// After that there is nothing left to tell us what the player wanted. Reading both
            /// preferences here, before the body runs, preserves that evidence.
            /// </summary>
            public static void Prefix()
            {
                Log.InfoOnce("hook-init", "OptionsPanel.InitializeOptions hook reached.");
                Plugin.TryEnsureInjected();
                LanguageDropdown.RememberPreferenceBeforeInit();
            }

            public static void Postfix(OptionsPanel __instance)
            {
                LanguageDropdown.ScheduleReconcile(__instance, "options panel initialised");
            }
        }

        /// <summary>
        /// Every open is another chance to put things right, and it is the exact moment the player
        /// is looking at the row in question.
        /// </summary>
        [HarmonyPatch(typeof(OptionsPanel), "OnOpenPanel")]
        public static class OnOpenPanel_Patch
        {
            public static void Postfix(OptionsPanel __instance)
            {
                Log.InfoOnce("hook-open", "OptionsPanel.OnOpenPanel hook reached.");
                LanguageDropdown.ScheduleReconcile(__instance, "options panel opened");
            }
        }
    }

    /// <summary>
    /// Audits and repairs the language dropdown. Everything here is defensive: the game is
    /// perfectly playable with a wrong-looking dropdown, so no failure in this file may propagate.
    /// </summary>
    public static class LanguageDropdown
    {
        private const string KentumIndexPref = "KENTUM_PREFS_LANGUAGE";
        private const string PixelCrushersNamePref = "Language";

        private static OptionsPanel _panel;
        private static string _reason;
        private static int _runAtFrame = -1;
        private static bool _everReconciled;

        /// <summary>Language name in effect before OptionsPanel.InitializeOptions ran.</summary>
        private static string _preInitLanguage;

        /// <summary>
        /// True only between the panel's initialisation and the reconcile that follows it.
        ///
        /// Outside that window the recorded name is stale and must not be used: a player who
        /// switches from Arabic to English mid-session would otherwise be dragged back to Arabic
        /// the next time they opened the panel.
        /// </summary>
        private static bool _preInitIsFresh;

        public static void RememberPreferenceBeforeInit()
        {
            try
            {
                var byName = PlayerPrefs.GetString(PixelCrushersNamePref, string.Empty);
                _preInitLanguage = !string.IsNullOrEmpty(byName) ? byName : Localization.CurrentLanguage;
                _preInitIsFresh = true;
                Log.Verbose($"Language before the options panel initialised: '{_preInitLanguage}'.");
            }
            catch (Exception e)
            {
                Log.Verbose($"Could not read the saved language: {e.Message}");
            }
        }

        /// <summary>
        /// Defers the work by a frame. The dropdown is a freshly instantiated component, and
        /// TMP_Dropdown.Start — which calls RefreshShownValue — has not run yet at the point the
        /// panel finishes building it. Reconciling in the same frame would be overwritten.
        /// </summary>
        public static void ScheduleReconcile(OptionsPanel panel, string reason)
        {
            _panel = panel;
            _reason = reason;
            _runAtFrame = Time.frameCount + 2;
        }

        /// <summary>
        /// Driven from the runner's Update. Two things happen here: the reconcile the panel hooks
        /// asked for, and an independent watch that does not need them.
        ///
        /// The watch is not redundancy for its own sake. Harmony reported all three OptionsPanel
        /// methods as patched and then none of them ran — the same "patched successfully, never
        /// invoked" behaviour already seen on TMP_Text.set_text. A repair that only happens when a
        /// detour fires is a repair that cannot be relied on, so the hooks make it prompt and the
        /// watch makes it certain.
        /// </summary>
        public static void Tick()
        {
            if (_runAtFrame >= 0 && Time.frameCount >= _runAtFrame)
            {
                _runAtFrame = -1;
                try { Reconcile(_panel, _reason); }
                catch (Exception e) { Log.Warn($"Language dropdown reconcile failed: {e.Message}"); }
            }

            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + (_reconcileCount >= 3 ? 10f : 2f);

            try { Watch(); }
            catch (Exception e) { Log.WarnOnce("dropdown-watch", $"Language dropdown watch failed: {e.Message}"); }
        }

        private static OptionItem_Dropdown _watched;
        private static float _nextPoll;
        private static int _reconcileCount;

        /// <summary>
        /// Holds on to the dropdown once found, so the steady-state cost is comparing two small
        /// lists rather than sweeping every object in the scene.
        /// </summary>
        private static void Watch()
        {
            if (!ArabicLanguage.IsInjected) return;

            if (_watched == null)
            {
                var items = FindLanguageDropdowns(null);
                if (items.Count == 0) return;

                _watched = items[0];
                Reconcile(null, "language dropdown appeared");
                return;
            }

            var languages = Localization.GetAllLanguagesNames();
            if (languages == null || languages.Count == 0) return;

            var keys = GetKeys(_watched);
            bool listWrong = keys.Count != languages.Count ||
                             !keys.Contains(ArabicLanguage.LanguageLabelKey);

            bool valueWrong = false;
            var dropdown = _watched.dropdown;
            int expected = languages.IndexOf(Localization.CurrentLanguage);
            if (dropdown != null && expected >= 0 && expected < dropdown.options.Count)
                valueWrong = dropdown.value != expected;

            if (listWrong || valueWrong)
                Reconcile(null, listWrong ? "option list no longer matches the language list"
                                          : "shown value no longer matches the active language");
        }

        /// <summary>
        /// Forgets the dropdown being watched, so the next tick sweeps for it again.
        ///
        /// Called on scene loads: the steady-state check is deliberately narrowed to one known
        /// component, which would miss a second options panel arriving with a new scene.
        /// </summary>
        public static void Rescan()
        {
            _watched = null;
            _nextPoll = 0f;
        }

        /// <summary>On-demand audit, for when a report is needed but the game is behaving.</summary>
        public static void AuditNow()
        {
            try { Reconcile(null, "requested by hotkey"); }
            catch (Exception e) { Log.Warn($"Language dropdown audit failed: {e.Message}"); }
        }

        private static void Reconcile(OptionsPanel panel, string reason)
        {
            Plugin.TryEnsureInjected();

            var languages = Localization.GetAllLanguagesNames();
            if (languages == null || languages.Count == 0)
            {
                Log.Warn("Language dropdown: the game reports no languages at all; nothing to reconcile.");
                return;
            }

            var items = FindLanguageDropdowns(panel);
            _reconcileCount++;

            // The first few audits are the useful ones. Past that a repeating report would only
            // bury the rest of the log, so later passes speak up only when they change something.
            bool report = _reconcileCount <= 3;
            if (report)
                Log.Info($"--- Language dropdown audit ({reason}) ---\n" + DescribeState(languages, items));

            if (items.Count == 0)
            {
                // Before the panel is built there is nothing to repair, and that is normal.
                Log.Verbose("No language dropdown exists yet.");
                return;
            }

            if (items.Count > 0) _watched = items[0];

            // The label is a localized key like any other. If the field never made it into the
            // text table, every entry would read "UI_Language_Arabic" instead of العربية.
            if (Localization.Localize(ArabicLanguage.LanguageLabelKey) == ArabicLanguage.LanguageLabelKey)
            {
                Log.Warn($"{ArabicLanguage.LanguageLabelKey} is missing from the text table; re-adding it.");
                ArabicLanguage.EnsureLabel();
            }

            var target = ChooseLanguage(languages);
            int targetIndex = languages.IndexOf(target);

            if (!string.Equals(target, Localization.CurrentLanguage, StringComparison.Ordinal))
            {
                Log.Info($"Language dropdown: restoring '{target}' — the panel had left the game in " +
                         $"'{Localization.CurrentLanguage}'.");
                Localization.ChangeLanguage(target);
            }

            bool changed = false;
            foreach (var item in items)
                changed |= RepairItem(item, languages, targetIndex);

            SyncSavedIndex(targetIndex);

            if (changed)
                Log.Info($"--- after repair ({reason}) ---\n" +
                         DescribeState(Localization.GetAllLanguagesNames(), items));
            else if (!_everReconciled)
                Log.Info("Language dropdown needed no repair.");

            _everReconciled = true;
        }

        /// <summary>
        /// Decides which language the game should be in.
        ///
        /// Normally that is simply the active one — the player's choice is not ours to second
        /// guess, and this runs every time the panel opens. The one exception is the reconcile
        /// straight after initialisation, where the panel may have just discarded a saved choice
        /// it could not represent; there the name recorded beforehand is the only surviving
        /// evidence of what the player actually wanted.
        /// </summary>
        private static string ChooseLanguage(List<string> languages)
        {
            if (_preInitIsFresh && !string.IsNullOrEmpty(_preInitLanguage) && languages.Contains(_preInitLanguage))
            {
                _preInitIsFresh = false;
                return _preInitLanguage;
            }
            _preInitIsFresh = false;

            var current = Localization.CurrentLanguage;
            if (!string.IsNullOrEmpty(current) && languages.Contains(current))
                return current;

            var byName = PlayerPrefs.GetString(PixelCrushersNamePref, string.Empty);
            if (!string.IsNullOrEmpty(byName) && languages.Contains(byName))
                return byName;

            return languages[0];
        }

        private static bool RepairItem(OptionItem_Dropdown item, List<string> languages, int targetIndex)
        {
            bool changed = false;

            var wanted = new List<string>(languages.Count);
            foreach (var name in languages) wanted.Add("UI_Language_" + name);

            var keys = GetKeys(item);
            if (!SameSequence(keys, wanted))
            {
                Log.Info($"Language dropdown: rebuilding the option list — it had {keys.Count} " +
                         $"entr{(keys.Count == 1 ? "y" : "ies")}, the game has {wanted.Count} language(s).");
                item.SetOptionsLocalizationKeys(wanted);
                changed = true;
            }

            var dropdown = item.dropdown;
            if (dropdown == null)
            {
                Log.Warn("Language dropdown: the option item has no dropdown component.");
                return changed;
            }

            if (targetIndex >= 0 && targetIndex < dropdown.options.Count && dropdown.value != targetIndex)
            {
                Log.Info($"Language dropdown: correcting the shown value from {dropdown.value} to " +
                         $"{targetIndex} ('{languages[targetIndex]}').");
                // Without notify: the language has already been applied, and letting the dropdown
                // fire its callback here would re-enter ChangeLanguage for no reason.
                item.SetValue(targetIndex, triggerEvent: false);
                changed = true;
            }

            // Re-localizes every label in the language that is now active and re-applies the
            // value, which is exactly the work the panel does on a language change.
            item.OnChangeLanguage();
            dropdown.RefreshShownValue();

            return changed;
        }

        /// <summary>
        /// Writes the index back so the game agrees with itself on the next launch.
        ///
        /// Kentum keeps the chosen language twice: a name, written by PixelCrushers, and an index
        /// into the language list, written by the options panel. Only the index is read at
        /// startup. Leaving it stale means the repair has to happen again on every launch, and
        /// any launch where it does not run reverts the player to English.
        /// </summary>
        private static void SyncSavedIndex(int targetIndex)
        {
            if (targetIndex < 0) return;

            try
            {
                var hub = GameBootstrap.GameHub;
                if (hub == null) return;

                if (hub.GetPlayerPrefsInt(KentumIndexPref, -1) == targetIndex) return;

                hub.SetPlayerPrefsInt(KentumIndexPref, targetIndex);
                Log.Info($"Language dropdown: saved language index set to {targetIndex}.");
            }
            catch (Exception e)
            {
                Log.Verbose($"Could not write {KentumIndexPref}: {e.Message}");
            }
        }

        // --- discovery -------------------------------------------------------------------------

        private static readonly FieldInfo KeysField =
            AccessTools.Field(typeof(OptionItem_Dropdown), "optionsLocalizationKeys");

        /// <summary>
        /// The language dropdown is the one whose every option key is a language name. The voice
        /// dropdown also uses UI_Language_ keys but prepends UI_Options_VOLanguage_SameAsLanguage,
        /// which is what separates them.
        /// </summary>
        private static List<OptionItem_Dropdown> FindLanguageDropdowns(OptionsPanel panel)
        {
            var found = new List<OptionItem_Dropdown>();

            void Consider(OptionItem_Dropdown item)
            {
                if (item == null || found.Contains(item)) return;
                var keys = GetKeys(item);
                if (keys.Count < 2) return;
                foreach (var k in keys)
                    if (k == null || !k.StartsWith("UI_Language_", StringComparison.Ordinal)) return;
                found.Add(item);
            }

            if (panel != null && panel.itemsByCategory != null)
            {
                foreach (var kv in panel.itemsByCategory)
                    foreach (var item in kv.Value)
                        Consider(item as OptionItem_Dropdown);
            }

            // Also sweep globally: the panel reference may be stale, and if the panel was ever
            // built twice there is more than one live dropdown to keep in step.
            foreach (var item in Resources.FindObjectsOfTypeAll<OptionItem_Dropdown>())
                Consider(item);

            return found;
        }

        private static List<string> GetKeys(OptionItem_Dropdown item)
        {
            if (KeysField?.GetValue(item) is List<string> keys) return keys;
            return new List<string>();
        }

        private static bool SameSequence(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        // --- reporting -------------------------------------------------------------------------

        /// <summary>
        /// Writes down every value the two conflicting explanations disagree about, so a log from
        /// a machine nobody can sit at is enough to tell them apart.
        /// </summary>
        private static string DescribeState(List<string> languages, List<OptionItem_Dropdown> items)
        {
            var sb = new System.Text.StringBuilder();

            sb.Append("  current language : ").Append(Localization.CurrentLanguage)
              .Append("   (Arabic active: ").Append(Plugin.ArabicActive).Append(")\n");
            sb.Append("  injected         : ").Append(ArabicLanguage.IsInjected)
              .Append(", language id ").Append(ArabicLanguage.LanguageId).Append('\n');
            sb.Append("  language list    : ").Append(languages.Count).Append(" -> ")
              .Append(string.Join(", ", languages.ToArray())).Append('\n');
            sb.Append("  before the panel : ").Append(_preInitLanguage ?? "(not recorded)").Append('\n');

            AppendPrefs(sb);

            var label = Localization.Localize(ArabicLanguage.LanguageLabelKey);
            sb.Append("  Arabic label     : \"").Append(label).Append('"')
              .Append(label == ArabicLanguage.LanguageLabelKey ? "   <- MISSING from the text table" : "")
              .Append('\n');

            sb.Append("  dropdowns found  : ").Append(items.Count).Append('\n');

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var keys = GetKeys(item);
                sb.Append("   [").Append(i).Append("] ").Append(SafeName(item))
                  .Append("  keys=").Append(keys.Count)
                  .Append(keys.Contains(ArabicLanguage.LanguageLabelKey) ? " (Arabic present)" : " (ARABIC MISSING)")
                  .Append('\n');

                var dropdown = item.dropdown;
                if (dropdown == null) { sb.Append("        no dropdown component\n"); continue; }

                sb.Append("        value=").Append(dropdown.value)
                  .Append(" of ").Append(dropdown.options.Count).Append(" option(s)")
                  .Append("  caption=\"").Append(dropdown.captionText != null ? dropdown.captionText.text : "(none)")
                  .Append("\"\n");
                sb.Append("        options: ").Append(DescribeOptions(dropdown.options)).Append('\n');
            }

            return sb.ToString().TrimEnd('\n');
        }

        private static void AppendPrefs(System.Text.StringBuilder sb)
        {
            try
            {
                sb.Append("  saved by name    : \"")
                  .Append(PlayerPrefs.GetString(PixelCrushersNamePref, "(unset)")).Append("\"\n");
            }
            catch { /* reporting only */ }

            try
            {
                var hub = GameBootstrap.GameHub;
                sb.Append("  saved by index   : ")
                  .Append(hub != null ? hub.GetPlayerPrefsInt(KentumIndexPref, -1).ToString() : "(no game hub)")
                  .Append("   (raw PlayerPrefs: ").Append(PlayerPrefs.GetInt(KentumIndexPref, -1)).Append(")\n");
            }
            catch (Exception e)
            {
                sb.Append("  saved by index   : unavailable (").Append(e.Message).Append(")\n");
            }
        }

        private static string DescribeOptions(List<TMP_Dropdown.OptionData> options)
        {
            var parts = new List<string>(options.Count);
            for (int i = 0; i < options.Count; i++)
                parts.Add($"{i}:\"{options[i]?.text}\"");
            return string.Join("  ", parts.ToArray());
        }

        private static string SafeName(Component c)
        {
            try { return c.gameObject.name + (c.gameObject.activeInHierarchy ? "" : " (inactive)"); }
            catch { return "(destroyed)"; }
        }
    }
}
