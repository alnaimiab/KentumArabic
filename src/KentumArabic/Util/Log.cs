using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace KentumArabic.Util
{
    /// <summary>
    /// Logging front-end. Everything this mod does is optional by design, so failures are
    /// reported and swallowed rather than thrown — a translation must never be able to
    /// prevent the game from running.
    /// </summary>
    public static class Log
    {
        private static ManualLogSource _log;
        private static readonly HashSet<string> _onceKeys = new HashSet<string>(StringComparer.Ordinal);

        public static bool VerboseEnabled;

        public static void Init(ManualLogSource src) => _log = src;

        public static void Info(string msg) => _log?.LogInfo(msg);
        public static void Warn(string msg) => _log?.LogWarning(msg);
        public static void Error(string msg) => _log?.LogError(msg);

        public static void Verbose(string msg)
        {
            if (VerboseEnabled) _log?.LogInfo(msg);
        }

        /// <summary>
        /// Log once per key. Used for failures inside per-frame code paths, where an unbounded
        /// log would itself become the performance problem.
        /// </summary>
        public static void WarnOnce(string key, string msg)
        {
            if (_onceKeys.Add(key)) _log?.LogWarning(msg);
        }

        public static void ErrorOnce(string key, string msg)
        {
            if (_onceKeys.Add(key)) _log?.LogError(msg);
        }

        /// <summary>
        /// Records that a hook fired, without repeating it on every subsequent call. Whether a
        /// patch ever runs is the first thing a report needs to answer, and it cannot be answered
        /// by a log that only speaks up when something goes wrong.
        /// </summary>
        public static void InfoOnce(string key, string msg)
        {
            if (_onceKeys.Add(key)) _log?.LogInfo(msg);
        }

        /// <summary>
        /// Runs an action, reporting any failure without propagating it. Used to isolate each
        /// patch and each load step so one broken piece degrades instead of cascading.
        /// </summary>
        public static bool Try(string what, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                Error($"{what} failed — continuing in degraded state.\n{e}");
                return false;
            }
        }
    }
}
