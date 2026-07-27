using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using KentumArabic.Util;

namespace KentumArabic.Updates
{
    /// <summary>
    /// Checks once at startup whether a newer translation has been published.
    ///
    /// Deliberately minimal: a single GET for a static JSON file, run asynchronously with a short
    /// timeout, failing silently. It never downloads or installs anything, never blocks startup,
    /// works fine offline, sends nothing about the user, and can be turned off in the config.
    ///
    /// The engine (this DLL) and the content (strings/) version independently, which is what
    /// makes a translation update a small file swap rather than a reinstall.
    /// </summary>
    public static class UpdateChecker
    {
        private const string ManifestUrl =
            "https://raw.githubusercontent.com/KENTUM_ARABIC_REPO/main/content/manifest.json";

        private const int TimeoutSeconds = 8;

        public static string LocalContentVersion { get; private set; } = "unknown";
        public static string LatestContentVersion { get; private set; }
        public static bool UpdateAvailable { get; private set; }

        public static void CheckAsync(GameObject host)
        {
            LocalContentVersion = ReadLocalContentVersion();
            VerifyGameBuild();

            var runner = host.GetComponent<MonoBehaviour>();
            if (runner == null) return;
            runner.StartCoroutine(Run());
        }

        private static IEnumerator Run()
        {
            // Nothing here is important enough to risk a startup hitch.
            yield return new WaitForSecondsRealtime(5f);

            if (ManifestUrl.Contains("KENTUM_ARABIC_REPO"))
            {
                Log.Verbose("Update check skipped: no release repository configured yet.");
                yield break;
            }

            UnityWebRequest req = null;
            try
            {
                req = UnityWebRequest.Get(ManifestUrl);
                req.timeout = TimeoutSeconds;
            }
            catch (Exception e)
            {
                Log.Verbose($"Update check could not start: {e.Message}");
                yield break;
            }

            yield return req.SendWebRequest();

            try
            {
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Log.Verbose($"Update check failed (this is harmless): {req.error}");
                    yield break;
                }

                var latest = ExtractJsonString(req.downloadHandler.text, "contentVersion");
                if (string.IsNullOrEmpty(latest)) yield break;

                LatestContentVersion = latest;
                UpdateAvailable = !string.Equals(latest, LocalContentVersion, StringComparison.Ordinal);

                if (UpdateAvailable)
                {
                    var changelog = ExtractJsonString(req.downloadHandler.text, "changelogAr");
                    var url = ExtractJsonString(req.downloadHandler.text, "downloadUrl");
                    Log.Info(
                        "─────────────────────────────────────────────\n" +
                        $" تتوفر نسخة أحدث من التعريب: {latest}\n" +
                        $" النسخة الحالية لديك: {LocalContentVersion}\n" +
                        (string.IsNullOrEmpty(changelog) ? "" : $" التغييرات: {changelog}\n") +
                        (string.IsNullOrEmpty(url) ? "" : $" التنزيل: {url}\n") +
                        " حدّث بنسخ مجلد strings الجديد فوق القديم — لا حاجة لإعادة التثبيت.\n" +
                        "─────────────────────────────────────────────");
                }
                else
                {
                    Log.Verbose($"Translation is up to date ({LocalContentVersion}).");
                }
            }
            finally
            {
                req?.Dispose();
            }
        }

        private static string ReadLocalContentVersion()
        {
            try
            {
                var path = Path.Combine(Plugin.PluginDir, "manifest.json");
                if (!File.Exists(path)) return "unknown";
                return ExtractJsonString(File.ReadAllText(path), "contentVersion") ?? "unknown";
            }
            catch { return "unknown"; }
        }

        /// <summary>
        /// Warns when Steam has patched the game to a build this translation has not been checked
        /// against. Purely informational — patches are bound by name, so the mod keeps working.
        /// </summary>
        private static void VerifyGameBuild()
        {
            try
            {
                var current = Dump.StringDumper.ReadBuildGuid();
                if (string.IsNullOrEmpty(current)) return;

                var path = Path.Combine(Plugin.PluginDir, "manifest.json");
                if (!File.Exists(path)) return;

                var manifest = File.ReadAllText(path);
                if (manifest.Contains(current))
                {
                    Log.Verbose($"Game build {current} is a known-good build for this translation.");
                    return;
                }

                Log.Warn(
                    $"This game build ({current}) is newer than the one this translation was tested against.\n" +
                    "The translation should keep working, but some newly added text may appear in English.\n" +
                    "Check for a translation update, or re-dump with Ctrl+F12 to see what changed.");
            }
            catch { /* informational only */ }
        }

        /// <summary>
        /// Pulls a single string value out of flat JSON. The manifest is a handful of fields we
        /// author ourselves, so this avoids taking a JSON dependency into the plugin.
        /// </summary>
        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            var needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return null;

            i = json.IndexOf(':', i + needle.Length);
            if (i < 0) return null;

            int start = json.IndexOf('"', i + 1);
            if (start < 0) return null;

            var sb = new System.Text.StringBuilder();
            for (int j = start + 1; j < json.Length; j++)
            {
                char c = json[j];
                if (c == '\\' && j + 1 < json.Length) { sb.Append(json[++j]); continue; }
                if (c == '"') break;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
