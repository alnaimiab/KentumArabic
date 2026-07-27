using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KentumArabic.Fonts;
using KentumArabic.Injection;
using KentumArabic.Shaping;
using KentumArabic.Util;

namespace KentumArabic.Diagnostics
{
    /// <summary>
    /// Renders the same Arabic test strings through every shaping mode, side by side, using real
    /// TextMeshPro components.
    ///
    /// This exists because the shaping decision cannot be made by reading source. TMP's behaviour
    /// with rich text, auto-sizing and word wrap has to be looked at. Every string here targets
    /// one specific failure mode, and the multi-line paragraph is the one that actually decides
    /// between the modes.
    /// </summary>
    public class ShapingTestOverlay : MonoBehaviour
    {
        private struct TestCase
        {
            public string Label;
            public string Text;
            public TestCase(string label, string text) { Label = label; Text = text; }
        }

        private static readonly TestCase[] Cases =
        {
            new TestCase("basic", "مرحبا بك في كنتوم"),

            // THE deciding test: in VisualOrder mode these lines stack bottom-to-top.
            new TestCase("wrap (4 lines)",
                "هذه فقرة طويلة بما يكفي لتلتف على عدة أسطر داخل الصندوق، والغرض منها التحقق " +
                "من أن ترتيب الأسطر يُقرأ من الأعلى إلى الأسفل بشكل صحيح وليس معكوسًا، لأن هذا " +
                "هو الفارق الحقيقي بين أوضاع التشكيل المختلفة عند عرض أوصاف العناصر والتقنيات."),

            new TestCase("digits",        "الطاقة: 150 / 300"),
            new TestCase("latin inline",  "تقنية Kentum المتقدمة"),
            new TestCase("rich text",     "<color=#ff4444>خطر</color> — المفاعل غير مستقر"),
            new TestCase("size tag",      "الكمية ( 12 <size=70%>/ 40</size> )"),
            new TestCase("lam-alef",      "لا إله إلا الله — الآن، الأمر لا يحتمل"),
            new TestCase("forms",         "ععع ببب سسس مـمـم"),
            new TestCase("tashkeel",      "بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ"),
            new TestCase("punctuation",   "ما هذا؟ إنه (صندوق) كبير، جدًا!"),
            new TestCase("format result", null), // filled at build time via string.Format
        };

        private const string FormatTemplate = "تم تحديث الإنجاز: {0} ({1}/{2})";

        private Canvas _canvas;
        private GameObject _root;
        private bool _visible;

        public void Toggle()
        {
            _visible = !_visible;
            if (_visible) Rebuild();
            if (_root != null) _root.SetActive(_visible);
            Log.Info($"Shaping test overlay: {(_visible ? "shown" : "hidden")} " +
                     "(Ctrl+Alt+M cycles the mode, Ctrl+Alt+T hides this)");
        }

        public void Rebuild()
        {
            if (!_visible) return;

            if (_root != null) Destroy(_root);

            _root = new GameObject("KentumArabic.TestOverlay");
            _root.transform.SetParent(transform, false);

            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32760; // above the game's own UI
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            AddBackdrop();

            float y = -20f;
            y = AddHeader(y);

            foreach (var mode in new[] { ShapingMode.RtlLayout, ShapingMode.VisualOrder, ShapingMode.None })
                y = AddModeBlock(mode, y);

            Log.Info("Test overlay rebuilt. Compare the 'wrap (4 lines)' block between modes: " +
                     "in a correct mode the lines read top-to-bottom.");
        }

        private void AddBackdrop()
        {
            var go = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);
        }

        private float AddHeader(float y)
        {
            var status =
                $"KENTUM ARABIC — shaping test battery\n" +
                $"active mode: {ArabicShaper.Mode}   |   font: {(ArabicFont.IsLoaded ? ArabicFont.Font.name : "NOT LOADED")}   " +
                $"|   language: {Tlon.Localization.Localization.CurrentLanguage}\n" +
                $"Ctrl+Alt+M cycle mode   Ctrl+Alt+T close   Ctrl+Alt+F font coverage audit";

            MakeText("Header", status, y, 22, 1860, TextAlignmentOptions.TopLeft,
                     Color.yellow, rtl: false, shaped: false, out float h);
            return y - h - 14f;
        }

        private float AddModeBlock(ShapingMode mode, float y)
        {
            bool isActive = mode == ArabicShaper.Mode;
            var title = $"── {mode}{(isActive ? "   ← ACTIVE" : "")} ──";
            MakeText($"Title_{mode}", title, y, 20, 1860, TextAlignmentOptions.TopLeft,
                     isActive ? Color.green : Color.gray, rtl: false, shaped: false, out float th);
            y -= th + 4f;

            foreach (var c in Cases)
            {
                var raw = c.Text ?? string.Format(FormatTemplate, "صائد النيازك", 3, 5);

                // Shape explicitly for this row's mode rather than the global one, so all three
                // can be compared on screen at once.
                var previous = ArabicShaper.Mode;
                ArabicShaper.Mode = mode;
                ArabicShaper.ClearCache();
                var shaped = ArabicShaper.Shape(raw);
                ArabicShaper.Mode = previous;
                ArabicShaper.ClearCache();

                MakeText($"Label_{mode}_{c.Label}", c.Label, y, 15, 220,
                         TextAlignmentOptions.TopLeft, new Color(0.6f, 0.6f, 0.6f),
                         rtl: false, shaped: false, out _, x: 20f);

                MakeText($"Case_{mode}_{c.Label}", shaped, y, 24, 1580,
                         TextAlignmentOptions.TopRight, Color.white,
                         rtl: mode == ShapingMode.RtlLayout, shaped: true, out float ch, x: 260f);

                y -= Mathf.Max(ch, 26f) + 6f;
            }

            return y - 10f;
        }

        private TextMeshProUGUI MakeText(string name, string text, float y, float size, float width,
                                         TextAlignmentOptions align, Color color,
                                         bool rtl, bool shaped, out float height, float x = 20f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, 0f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;

            // Write past our own hook: the string is already shaped for this row's mode, and the
            // direction flag is set explicitly below.
            TmpPatches.Suppress = true;
            try
            {
                tmp.isRightToLeftText = rtl;
                tmp.text = text;
                tmp.ForceMeshUpdate();
            }
            finally
            {
                TmpPatches.Suppress = false;
            }

            height = tmp.preferredHeight;
            rt.sizeDelta = new Vector2(width, height);
            return tmp;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root);
        }
    }
}
