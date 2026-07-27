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
            if (Plugin.CfgSelfTest != null && Plugin.CfgSelfTest.Value)
                StartCoroutine(SelfTest());
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

            Log.Info("Self-test complete.");

            // The dialogue database only exists once a game is loaded, so keep watching for it
            // and re-dump when it turns up. That way the workbook can be completed by simply
            // loading a save, without having to remember a hotkey at the right moment.
            yield return WatchForDialogueDatabase();
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
        /// Reports what Arabic-bearing text components actually hold right now.
        ///
        /// Distinguishes the two ways Arabic can look wrong on screen: text that never reached
        /// the shaping hook (still base letters, renders disconnected) versus text that was
        /// shaped but is being drawn without right-to-left layout.
        /// </summary>
        private void ReportLiveText()
        {
            Log.Try("Reporting live text state", () =>
            {
                int arabic = 0, shapedOk = 0, unshaped = 0, rtlSet = 0;
                var samples = new List<string>();

                foreach (var t in Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>())
                {
                    if (t == null) continue;
                    var s = t.text;
                    if (string.IsNullOrEmpty(s) || !ArabicShaper.ContainsArabic(s)) continue;

                    arabic++;
                    bool isShaped = ArabicShaper.IsAlreadyShaped(s);
                    if (isShaped) shapedOk++; else unshaped++;
                    if (t.isRightToLeftText) rtlSet++;

                    if (samples.Count < 6)
                        samples.Add($"    {(isShaped ? "shaped  " : "UNSHAPED")} rtl={t.isRightToLeftText,-5} " +
                                    $"align={t.alignment,-14} \"{s}\"  [{t.name}]");
                }

                Log.Info(
                    $"Live text state: {arabic} component(s) contain Arabic — " +
                    $"{shapedOk} shaped, {unshaped} UNSHAPED, {rtlSet} with RTL layout.\n" +
                    $"  ArabicActive={Plugin.ArabicActive} language={Tlon.Localization.Localization.CurrentLanguage} " +
                    $"mode={ArabicShaper.Mode}\n" +
                    $"  Localize postfix: {LocalizationPatches.Localize_Patch.Calls} call(s), " +
                    $"{LocalizationPatches.Localize_Patch.WhileActive} while Arabic active, " +
                    $"{LocalizationPatches.Localize_Patch.Shaped} shaped; " +
                    $"directed={LocalizedTextPatches.DirectedCount}\n" +
                    $"  TextTable postfix: {TextTablePatches.Calls} call(s), {TextTablePatches.Shaped} shaped\n" +
                    $"  sanity: Shape(\"مرحبا\") -> \"{ArabicShaper.Shape("مرحبا")}\"\n" +
                    (samples.Count > 0 ? string.Join("\n", samples.ToArray()) : "    (no samples)"));
            });
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
        }

        private void Update()
        {
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
                $"  shaping       : {TextTablePatches.Calls} table lookup(s), {TextTablePatches.Shaped} shaped at runtime\n" +
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
            KeyCode.S => UnityEngine.InputSystem.Key.S,
            KeyCode.F12 => UnityEngine.InputSystem.Key.F12,
            _ => UnityEngine.InputSystem.Key.None,
        };
    }
}
