namespace KentumArabic.Util
{
    /// <summary>Stands in for the BepInEx logger so the shaper can be built outside the game.</summary>
    internal static class Log
    {
        public static void WarnOnce(string key, string msg) =>
            System.Console.WriteLine($"[warn:{key}] {msg}");
    }
}
