using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using KentumArabic.Diagnostics;
using KentumArabic.Fonts;
using KentumArabic.Injection;
using KentumArabic.Shaping;
using KentumArabic.Util;

namespace KentumArabic
{
    /// <summary>
    /// Lives for the whole session and owns everything that needs a Unity frame: hotkeys,
    /// re-registering the font fallback when new scenes bring their own font assets, and the
    /// shaping test overlay.
    /// </summary>
    public class KentumArabicRunner : MonoBehaviour
    {
        private ShapingTestOverlay _overlay;
        private float _nextEnsureCheck;
        private float _nextDirectionSweep;
        private float _nextDialogueCheck;

        private void Start()
        {
            StartCoroutine(InjectAsSoonAsPossible());

            if (Plugin.CfgSelfTest != null && Plugin.CfgSelfTest.Value)
                StartCoroutine(SelfTest());
        }

        /// <summary>
        /// Registers Arabic the instant the text table exists, rather than waiting to be asked.
        ///
        /// Injection used to happen lazily, on the first call to GetAllLanguagesNames. That call
        /// can arrive before UILocalizationManager has a table, in which case nothing can be
        /// injected and the game caches a language list without Arabic in it — and the options
        /// panel turns that list into a fixed array it never rebuilds. Whether the dropdown ends
        /// up containing Arabic then depends on component start order, which is exactly the
        /// "sometimes it is there, sometimes it is not" the lazy approach produced.
        ///
        /// Polling every frame is the right cost here: it stops the moment it succeeds, and the
        /// window it is racing is a handful of frames at startup.
        /// </summary>
        private IEnumerator InjectAsSoonAsPossible()
        {
            const float giveUpAfterSeconds = 120f;
            var deadline = Time.realtimeSinceStartup + giveUpAfterSeconds;
            int frames = 0;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (ArabicLanguage.IsInjected)
                {
                    Log.Verbose($"Arabic was already registered after {frames} frame(s).");
                    yield break;
                }

                if (Plugin.TryEnsureInjected())
                {
                    Log.Info($"Arabic registered eagerly, {frames} frame(s) after startup — " +
                             "before anything could cache a language list without it.");
                    yield break;
                }

                frames++;
                yield return null;
            }

            Log.Warn($"The text table never appeared within {giveUpAfterSeconds:0}s, so Arabic " +
                     "could not be registered eagerly. It will still be registered on the first " +
                     "language lookup, but a dropdown built before that may not list it.");
        }

        /// <summary>
        /// Brings up the test battery, dumps the translation workbook and saves a screenshot, so
        /// Arabic rendering can be verified without navigating the menus by hand. This is how the
        /// shaping mode decision gets made: the result has to be looked at, not reasoned about.
        /// </summary>
        private IEnumerator SelfTest()
        {
            yield return new WaitForSecondsRealtime(12f);

            Log.Info("Self-test: dumping the translation workbook.");
            Dump.StringDumper.DumpAll(System.IO.Path.Combine(Plugin.PluginDir, "_dump"));
            yield return null;

            // The overlay is for choosing the shaping mode. Skipping it captures the game's own
            // UI instead, which is what you want when checking real screens for layout problems.
            if (Plugin.CfgSelfTestOverlay.Value)
            {
                Log.Info("Self-test: showing the shaping test battery.");
                if (_overlay == null) _overlay = gameObject.AddComponent<ShapingTestOverlay>();
                _overlay.Toggle();
            }

            // Give the dynamic atlas a few frames to rasterize every glyph it just met.
            yield return new WaitForSecondsRealtime(3f);
            ArabicFont.RefreshAllText();
            yield return new WaitForSecondsRealtime(2f);

            var dir = System.IO.Path.Combine(Plugin.PluginDir, "_dump");
            System.IO.Directory.CreateDirectory(dir);
            var shot = System.IO.Path.Combine(dir, "selftest.png");
            ScreenCapture.CaptureScreenshot(shot);
            Log.Info($"Self-test: screenshot requested -> {shot}");

            yield return new WaitForSecondsRealtime(3f);

            var all = new List<string>();
            if (Plugin.Translations != null) all.AddRange(Plugin.Translations.Ui.Values);
            var shaped = new List<string>(all.Count);
            foreach (var s in all) shaped.Add(ArabicShaper.Shape(s));
            ArabicFont.AuditCoverage(shaped);

            ReportLiveText();

            yield return LanguageRoundTrip();

            Log.Info("Self-test complete.");

            // The dialogue database only exists once a game is loaded, so keep watching for it
            // and re-dump when it turns up. That way the workbook can be completed by simply
            // loading a save, without having to remember a hotkey at the right moment.
            yield return WatchForDialogueDatabase();
        }

        /// <summary>
        /// Switches away from Arabic and back, then reports what the text on screen actually is.
        ///
        /// This is the one case a restart hides. Starting the game with Arabic already saved takes
        /// a different path from choosing it in the options menu, and for a while only the first
        /// of those produced joined letters — the second left every letter isolated because the
        /// shaping flag was never raised. Reproducing it needs a real mid-session switch, which is
        /// exactly what this does, and the report says outright how many components are unshaped.
        /// </summary>
        private IEnumerator LanguageRoundTrip()
        {
            var original = Tlon.Localization.Localization.CurrentLanguage;

            Log.Info("Self-test: switching language mid-session, the way the options menu does.");

            Tlon.Localization.Localization.ChangeLanguage("Default");
            yield return new WaitForSecondsRealtime(2f);

            Tlon.Localization.Localization.ChangeLanguage(Injection.ArabicLanguage.LanguageName);
            yield return new WaitForSecondsRealtime(3f);

            Log.Info("Self-test: state after a mid-session switch into Arabic —");
            ReportLiveText();

            if (!string.Equals(original, Tlon.Localization.Localization.CurrentLanguage, System.StringComparison.Ordinal))
            {
                Tlon.Localization.Localization.ChangeLanguage(original);
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private IEnumerator WatchForDialogueDatabase()
        {
            const float TimeoutSeconds = 300f;
            float deadline = Time.unscaledTime + TimeoutSeconds;
            bool announced = false;

            while (Time.unscaledTime < deadline)
            {
                var db = Dump.StringDumper.FindDialogueDatabase();
                if (db != null && db.conversations != null && db.conversations.Count > 0)
                {
                    Log.Info($"Dialogue database appeared ({db.conversations.Count} conversations) — dumping.");
                    // Let the database finish populating before reading it.
                    yield return new WaitForSecondsRealtime(2f);
                    Dump.StringDumper.DumpAll(System.IO.Path.Combine(Plugin.PluginDir, "_dump"));
                    yield break;
                }

                if (!announced)
                {
                    announced = true;
                    Log.Info("Waiting for a game to load so the dialogue can be dumped...");
                }
                yield return new WaitForSecondsRealtime(2f);
            }

            Log.Info("Stopped waiting for the dialogue database.");
        }

        /// <summary>
        /// Reports what is actually being drawn, not what is stored.
        ///
        /// The distinction matters and this report used to get it wrong. Shaping happens in TMP's
        /// preprocessing hook, which leaves the component's own <c>text</c> as plain logical
        /// Arabic on purpose — so testing that string for presentation forms reported every
        /// component as unshaped even while the screen was perfectly correct. A metric that reads
        /// alarming when nothing is wrong is worse than no metric: it sends the next investigation
        /// after the wrong thing.
        ///
        /// <c>textInfo.characterInfo</c> holds the characters TMP resolved after preprocessing,
        /// which is the ground truth for what reaches the glyph atlas.
        /// </summary>
        private void ReportLiveText()
        {
            Log.Try("Reporting live text state", () =>
            {
                int arabic = 0, drawn = 0, shapedOk = 0, unshaped = 0, rtlSet = 0, hooked = 0;
                var samples = new List<string>();

                foreach (var t in Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>())
                {
                    if (t == null) continue;
                    var s = t.text;
                    if (string.IsNullOrEmpty(s) || !ArabicShaper.ContainsArabic(s)) continue;

                    arabic++;
                    if (t.isRightToLeftText) rtlSet++;
                    if (t.textPreprocessor is ArabicTextPreprocessor) hooked++;

                    var state = RenderedState(t);
                    if (state == Rendered.NotDrawn) { }
                    else if (state == Rendered.Shaped) { drawn++; shapedOk++; }
                    else { drawn++; unshaped++; }

                    if (samples.Count < 6 && state != Rendered.NotDrawn)
                        samples.Add($"    {(state == Rendered.Shaped ? "shaped  " : "UNSHAPED")} rtl={t.isRightToLeftText,-5} " +
                                    $"align={t.alignment,-14} \"{s}\"  [{t.name}]");
                }

                Log.Info(
                    $"Live text state: {arabic} component(s) contain Arabic, {drawn} of them drawn — " +
                    $"{shapedOk} shaped on screen, {unshaped} UNSHAPED, " +
                    $"{rtlSet} with RTL layout, {hooked} with the shaping hook attached.\n" +
                    $"  ArabicActive={Plugin.ArabicActive} language={Tlon.Localization.Localization.CurrentLanguage} " +
                    $"mode={ArabicShaper.Mode}\n" +
                    $"  Localize postfix: {LocalizationPatches.Localize_Patch.Calls} call(s), " +
                    $"{LocalizationPatches.Localize_Patch.WhileActive} while Arabic active, " +
                    $"{LocalizationPatches.Localize_Patch.Shaped} shaped; " +
                    $"directed={LocalizedTextPatches.DirectedCount}\n" +
                    $"  preprocessor: {Shaping.ArabicTextPreprocessor.Processed} call(s), {Shaping.ArabicTextPreprocessor.Shaped} shaped\n" +
                    $"  sanity: Shape(\"مرحبا\") -> \"{ArabicShaper.Shape("مرحبا")}\"\n" +
                    (samples.Count > 0 ? string.Join("\n", samples.ToArray()) : "    (nothing drawn)"));
            });
        }

        private enum Rendered { NotDrawn, Shaped, Unshaped }

        /// <summary>
        /// Looks at the characters TMP resolved for this component. A component that has never
        /// been laid out — most of the UI at any given moment — has no verdict to give, and
        /// counting it either way would be a guess.
        /// </summary>
        private static Rendered RenderedState(TMPro.TMP_Text t)
        {
            var info = t.textInfo;
            if (info == null || info.characterCount == 0 || info.characterInfo == null)
                return Rendered.NotDrawn;

            bool sawArabic = false;
            // characterCount is a logical count and can exceed the allocated array.
            int count = Mathf.Min(info.characterCount, info.characterInfo.Length);

            for (int i = 0; i < count; i++)
            {
                char c = info.characterInfo[i].character;
                // Presentation Forms-B: what the shaper emits and what the atlas renders.
                if (c >= (char)0xFE70 && c <= (char)0xFEFE) return Rendered.Shaped;
                // A base Arabic letter reaching layout means the shaper never saw this string.
                if (c >= (char)0x0621 && c <= (char)0x064A) sawArabic = true;
            }

            return sawArabic ? Rendered.Unshaped : Rendered.NotDrawn;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // New scenes can introduce font assets that were not present when we first
            // registered, and Resources.UnloadUnusedAssets runs around scene loads. Both are
            // cheap to re-apply and idempotent.
            ArabicFont.RegisterFallback();
            Plugin.TryEnsureInjected();
            LanguageDropdown.Rescan();
        }

        private string _observedLanguage;

        /// <summary>
        /// Notices a language change by looking, rather than by being told.
        ///
        /// Being told was the original design: a Harmony prefix on Localization.ChangeLanguage set
        /// the active flag before the game re-localized anything. On this build that patch is
        /// reported as applied and never executes — so picking العربية from the options menu
        /// switched the text to Arabic with shaping still switched off, and every letter rendered
        /// in its isolated form. Restarting the game hid it, because startup takes a different
        /// path that calls the same code directly.
        ///
        /// Localization.CurrentLanguage is a static field read, so checking it every frame costs
        /// a string comparison and removes the dependency on a hook that does not fire.
        /// </summary>
        private void ObserveLanguage()
        {
            string language;
            try { language = Tlon.Localization.Localization.CurrentLanguage; }
            catch { return; }

            if (string.Equals(language, _observedLanguage, System.StringComparison.Ordinal)) return;

            var previous = _observedLanguage;
            _observedLanguage = language;

            if (previous == null) return;   // first look: startup state, already handled

            Log.Info($"Language changed to '{language}' (was '{previous}').");
            Plugin.BeginLanguageChange(language);

            // The options row tracks the language by index and can be left pointing at the old
            // one. Ask for it now rather than let the slow watch get to it seconds later.
            LanguageDropdown.ScheduleReconcile(null, "language changed");

            // The switch has already happened by the time we see it, so the text on screen was
            // composed while the flag was still wrong. Re-run it rather than wait for whatever
            // redraws next.
            Plugin.ForceRelocalize();
        }

        private void Update()
        {
            // Before anything that reads Plugin.ArabicActive this frame.
            ObserveLanguage();

            // Injection normally happens through the Harmony hooks. This is a slow safety net for
            // the case where the game somehow reaches gameplay without ever calling them.
            if (!ArabicLanguage.IsInjected && Time.unscaledTime >= _nextEnsureCheck)
            {
                _nextEnsureCheck = Time.unscaledTime + 1f;
                Plugin.TryEnsureInjected();
            }

            // The dialogue database only exists once a game is loaded, and can be reset out from
            // under us on a new game or save load, so it is re-checked on the same slow cadence.
            if (Plugin.ArabicActive && Time.unscaledTime >= _nextDialogueCheck)
            {
                _nextDialogueCheck = Time.unscaledTime + 3f;
                DialogueInjection.Apply(Plugin.Translations);
            }

            // Text direction cannot be set where the text is produced — the table hook shapes
            // strings but has no idea which component will display them. A periodic sweep gives
            // right-to-left layout to anything holding Arabic, including UI that appears later.
            // TextDirector records each component's original state, so this is idempotent and
            // fully reversible when the player switches back.
            if (Plugin.ArabicActive && Time.unscaledTime >= _nextDirectionSweep)
            {
                _nextDirectionSweep = Time.unscaledTime + 0.5f;
                LocalizedTextPatches.DirectAllOnScreen();
            }

            // Deferred by a frame from the options panel hooks, so it lands after the dropdown's
            // own Start has run and cannot be overwritten by it.
            LanguageDropdown.Tick();

            HandleHotkeys();
        }

        private void HandleHotkeys()
        {
            if (!Hotkeys.ModifierHeld()) return;

            // Ctrl+Alt+L — toggle Kentum's own localization debug mode. With Arabic selected this
            // paints every untranslated string red, turning the game into a live coverage report.
            if (Hotkeys.Pressed(KeyCode.L))
            {
                UserSettings.LocalizationDebugMode = !UserSettings.LocalizationDebugMode;
                Log.Info($"Localization debug mode: {(UserSettings.LocalizationDebugMode ? "ON" : "OFF")} " +
                         "(green = localized, red = NOT localized, for the current language)");
                PixelCrushers.UILocalizationManager.instance?.UpdateUIs(Tlon.Localization.Localization.CurrentLanguage);
                ArabicFont.RefreshAllText();
            }

            // Ctrl+Alt+R — re-read the TSV files without restarting.
            if (Hotkeys.Pressed(KeyCode.R))
                Plugin.HotReload();

            // Ctrl+Alt+M — cycle the shaping mode to compare them on the same screen.
            if (Hotkeys.Pressed(KeyCode.M))
            {
                ArabicShaper.Mode = ArabicShaper.Mode switch
                {
                    ShapingMode.RtlLayout => ShapingMode.VisualOrder,
                    ShapingMode.VisualOrder => ShapingMode.None,
                    _ => ShapingMode.RtlLayout,
                };
                ArabicShaper.ClearCache();
                Log.Info($"Shaping mode: {ArabicShaper.Mode}");
                _overlay?.Rebuild();
                PixelCrushers.UILocalizationManager.instance?.UpdateUIs(Tlon.Localization.Localization.CurrentLanguage);
                ArabicFont.RefreshAllText();
            }

            // Ctrl+Alt+N — cycle the bundled Arabic fonts, live.
            if (Hotkeys.Pressed(KeyCode.N))
                Plugin.CycleFont();

            // Ctrl+Alt+T — the shaping test battery.
            if (Hotkeys.Pressed(KeyCode.T))
            {
                if (_overlay == null) _overlay = gameObject.AddComponent<ShapingTestOverlay>();
                _overlay.Toggle();
            }

            // Ctrl+Alt+D — write the diagnostic reports.
            if (Hotkeys.Pressed(KeyCode.D))
            {
                TextDiagnostics.WriteReports(System.IO.Path.Combine(Plugin.PluginDir, "_dump"));
            }

            // Ctrl+Alt+F — audit font coverage across the whole loaded translation.
            if (Hotkeys.Pressed(KeyCode.F))
            {
                var all = new List<string>();
                if (Plugin.Translations != null)
                {
                    all.AddRange(Plugin.Translations.Ui.Values);
                    all.AddRange(Plugin.Translations.Dialogue.Values);
                    all.AddRange(Plugin.Translations.Actors.Values);
                }
                // Audit the shaped forms — those are the codepoints TMP actually has to render.
                var shaped = new List<string>(all.Count);
                foreach (var s in all) shaped.Add(ArabicShaper.Shape(s));
                ArabicFont.AuditCoverage(shaped);
            }

            // Ctrl+Alt+G — audit and repair the Options > Language dropdown on demand.
            if (Hotkeys.Pressed(KeyCode.G))
                LanguageDropdown.AuditNow();

            // Ctrl+Alt+S — status summary.
            if (Hotkeys.Pressed(KeyCode.S))
                LogStatus();

            // Ctrl+F12 — dump every source string to a translation workbook.
            if (Hotkeys.Pressed(KeyCode.F12))
                Dump.StringDumper.DumpAll(System.IO.Path.Combine(Plugin.PluginDir, "_dump"));
        }

        private void LogStatus()
        {
            Log.Info(
                $"--- Kentum Arabic status ---\n" +
                $"  injected      : {ArabicLanguage.IsInjected} (language id {ArabicLanguage.LanguageId})\n" +
                $"  arabic active : {Plugin.ArabicActive}\n" +
                $"  shaping mode  : {ArabicShaper.Mode} (cache {ArabicShaper.CacheCount})\n" +
                $"  font loaded   : {ArabicFont.IsLoaded}{(ArabicFont.IsLoaded ? $" ({ArabicFont.Font.name})" : "")}\n" +
                $"  translations  : {Plugin.Translations?.TotalEntries ?? 0} entries, {ArabicLanguage.AppliedCount} applied, {ArabicLanguage.OrphanKeyCount} unknown\n" +
                $"  shaping       : {Shaping.ArabicTextPreprocessor.Processed} preprocessed, {Shaping.ArabicTextPreprocessor.Shaped} shaped\n" +
                $"  diagnostics   : {(TextDiagnostics.Enabled ? $"{TextDiagnostics.MissingKeyCount} missing keys, {TextDiagnostics.BypassCount} bypasses" : "disabled")}");
        }
    }

    /// <summary>
    /// Keyboard access that works whether the project uses the new Input System, the legacy
    /// input manager, or both. Kentum ships the new Input System but the legacy module is also
    /// present, so neither can be assumed.
    /// </summary>
    internal static class Hotkeys
    {
        private static bool _legacyBroken;

        public static bool ModifierHeld()
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
                    bool alt = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
                    return ctrl && alt;
                }
            }
            catch { /* fall through to legacy */ }

            if (_legacyBroken) return false;
            try
            {
                return (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                       (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
            }
            catch
            {
                _legacyBroken = true;
                return false;
            }
        }

        public static bool Pressed(KeyCode key)
        {
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null)
                {
                    var control = kb[ToInputSystemKey(key)];
                    return control != null && control.wasPressedThisFrame;
                }
            }
            catch { /* fall through to legacy */ }

            if (_legacyBroken) return false;
            try { return Input.GetKeyDown(key); }
            catch { _legacyBroken = true; return false; }
        }

        private static UnityEngine.InputSystem.Key ToInputSystemKey(KeyCode key) => key switch
        {
            KeyCode.L => UnityEngine.InputSystem.Key.L,
            KeyCode.R => UnityEngine.InputSystem.Key.R,
            KeyCode.M => UnityEngine.InputSystem.Key.M,
            KeyCode.T => UnityEngine.InputSystem.Key.T,
            KeyCode.D => UnityEngine.InputSystem.Key.D,
            KeyCode.F => UnityEngine.InputSystem.Key.F,
            KeyCode.G => UnityEngine.InputSystem.Key.G,
            KeyCode.S => UnityEngine.InputSystem.Key.S,
            KeyCode.F12 => UnityEngine.InputSystem.Key.F12,
            _ => UnityEngine.InputSystem.Key.None,
        };
    }
}
