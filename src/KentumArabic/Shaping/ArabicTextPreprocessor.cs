using TMPro;
using KentumArabic.Util;

namespace KentumArabic.Shaping
{
    /// <summary>
    /// Shapes Arabic at display time, through TextMeshPro's own preprocessing hook.
    ///
    /// This is the correct place for it. <c>TMP_Text.ParseInputText</c> calls
    /// <c>m_TextPreprocessor.PreprocessText(m_text)</c> on the *final composed* string
    /// immediately before layout, so anything substituted at runtime is already in place.
    ///
    /// That distinction is not academic. Shaping the stored translation instead meant a template
    /// like "اليوم {0}" was reversed while the number substituted into it later was not, so day 18
    /// rendered as "81" — and before placeholders were protected, a reversed "{0}" made
    /// string.Format throw outright and took the whole save-slot panel with it.
    ///
    /// It is also an interface the engine asks for, rather than a detour installed over a method.
    /// Harmony reported patches on TMP_Text.set_text, SetText, Localization.Localize and
    /// TextTable.GetFieldTextForLanguage as applied on this build and none of them ever executed,
    /// verified with counters and an in-process probe. This runs because TMP itself calls it.
    /// </summary>
    public class ArabicTextPreprocessor : ITextPreprocessor
    {
        public static readonly ArabicTextPreprocessor Instance = new ArabicTextPreprocessor();

        public static long Processed;
        public static long Shaped;

        public string PreprocessText(string text)
        {
            Processed++;

            if (!Plugin.ArabicActive) return text;
            if (string.IsNullOrEmpty(text)) return text;
            if (!ArabicShaper.ContainsArabic(text)) return text;

            try
            {
                var shaped = ArabicShaper.Shape(text);
                if (!ReferenceEquals(shaped, text)) Shaped++;
                return shaped;
            }
            catch (System.Exception e)
            {
                Log.WarnOnce("preprocess", $"Shaping failed; showing raw text: {e.Message}");
                return text;
            }
        }
    }
}
