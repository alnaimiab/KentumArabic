# تعريب لعبة Kentum — Kentum Arabic

<div dir="rtl">

تعريب غير رسمي للعبة **Kentum** من استوديو Tlön Industries، يضيف **العربية كلغة حقيقية قابلة للاختيار** من قائمة إعدادات اللعبة — بحروف متصلة، ومن اليمين لليسار، ومحاذاة صحيحة.

لا يُعدَّل أي ملف من ملفات اللعبة الأصلية. تُسجَّل اللغة أثناء التشغيل، وتُحمَّل الحروف العربية عبر نظام الخطوط الاحتياطية في TextMeshPro، وتعيش الترجمة نفسها في ملفات نصية منفصلة يمكن تحديثها دون إعادة تثبيت أي شيء.

## التثبيت

1. نزّل `KentumArabic-Full-vX.Y.Z.zip` من [صفحة الإصدارات](../../releases/latest).
2. فك الضغط داخل مجلد اللعبة (المجلد الذي يحتوي `Kentum.exe`).
3. شغّل اللعبة ← **Options** ← **Language** ← **العربية**.

لمعرفة مجلد اللعبة: في Steam، اضغط بزر الفأرة الأيمن على اللعبة ← Manage ← Browse local files.

### إلغاء التثبيت
احذف `winhttp.dll` و`doorstop_config.ini` و`.doorstop_version` ومجلد `BepInEx`.

### تحديث الترجمة فقط
نزّل `KentumArabic-Content-*.zip` واستبدل به مجلد `BepInEx/plugins/KentumArabic/strings`. لا حاجة لإعادة التثبيت ولا لإعادة تنزيل الخط.

### ملاحظة عن تحذير Windows
يستخدم التعريب أداة التحميل المفتوحة المصدر **BepInEx**، وهي تعمل عبر ملف `winhttp.dll`. قد يعرض Windows SmartScreen أو بعض مضادات الفيروسات تحذيرًا لهذا السبب. بصمات التحقق (SHA-256) لكل ملف منشورة مع كل إصدار.

## الحالة

| | |
|---|---|
| اللغة تظهر في الإعدادات | ✅ |
| تشكيل الحروف العربية ووصلها | ✅ |
| الاتجاه من اليمين لليسار والمحاذاة | ✅ |
| التفاف الأسطر بترتيب صحيح | ✅ |
| الأرقام والنصوص اللاتينية داخل الجملة | ✅ |
| وسوم التنسيق `<color>` و`<size>` | ✅ |
| نصوص الواجهة والعناصر والتقنيات | 🚧 قيد الترجمة |
| الحوارات | 🚧 قيد الترجمة |

</div>

---

## For developers

An unofficial Arabic (RTL) translation mod for **Kentum** (Steam AppID 2165140), built as a
BepInEx plugin. It adds Arabic as a genuine selectable language instead of overwriting an
existing one, and modifies none of the game's own files.

### How it works

Kentum's language list is **data-driven**: `Tlon.Localization.Localization.GetAllLanguagesNames()`
simply enumerates `UILocalizationManager.instance.textTable.languages`. There is no language enum
to patch. So the mod:

1. **Registers the language** by adding `"Arabic"` to the live `TextTable` and writing the
   translation into it. Every downstream consumer — `LocalizedStaticText`, the options dropdown,
   `LocalizedStaticImage` — then works with no further patching.
2. **Supplies glyphs** by building a `TMP_FontAsset` at runtime from a bundled `.ttf` with a
   dynamic atlas, and registering it as a TMP *fallback*. Latin and digits keep coming from the
   game's own Montserrat, so the UI keeps its visual identity and mixed strings just work.
3. **Shapes the text** in a Harmony prefix on `TMP_Text.set_text`, the last boundary before TMP.

Translation data stays as **plain logical-order Arabic** in TSV files. Shaping is a render-time
detail, which keeps the data reviewable, diffable and usable in CAT tools.

### Why the text is shaped at all

TextMeshPro is a glyph-atlas renderer, not a text shaper: no OpenType shaping, no Unicode
bidirectional algorithm. Given logical Arabic it renders isolated letter forms, left to right —
disconnected and backwards. The plugin converts letters to Unicode presentation forms
(U+FE70–FEFF) and reorders, then puts TMP into right-to-left layout mode.

Three shaping modes exist behind a config switch:

| Mode | Word wrap | Typewriter reveal | Verdict |
|---|---|---|---|
| `RtlLayout` | correct, top-to-bottom | right-to-left, correct | **default** |
| `VisualOrder` | **broken** — lines stack bottom-to-top | reveals the end of the sentence first | comparison only |
| `None` | n/a | n/a | diagnosis only |

`RtlLayout` was chosen by rendering all three side by side in game (`Ctrl+Alt+T`) rather than by
reasoning about the source. The multi-line paragraph is what decides it, and it matters because
item and technology descriptions — 1,521 of ~2,451 keys — are inherently multi-line.

### Why the translation carries no diacritics

The same missing layer that forces shaping also rules out tashkeel. TextMeshPro has no GPOS
mark-to-base positioning, so it draws a combining mark as a zero-advance glyph at the pen
position — wherever the base letter's advance happened to leave it, not anchored to that letter's
shape. On narrow non-joining letters, reh above all, the offset is visible and shifts from word to
word, which reads on screen as letters that will not sit still.

No font fixes this; placement is the renderer's job. So the 1,902 marks that were in the first
draft were removed, and the two words that genuinely needed one are spelled out instead —
كينت rather than كِنت, which is the conventional transliteration anyway and costs nothing at
render time. `tools/strip_tashkeel.py --check` keeps new contributions consistent.

### Choosing a typeface

The shaper emits presentation forms, so a font is only usable here if it carries the ones this
translation produces. That excludes most of the modern Arabic faces people reach for first:

| Font | Missing forms | Result on screen |
|---|---|---|
| Cairo, Tajawal, Almarai, Alexandria, El Messiri, Changa, Markazi Text | 36 — every **isolated** form | every isolated letter is an empty box |
| Readex Pro, Rubik, Reem Kufi, Scheherazade New, Mada, Harmattan | 125 | almost nothing renders |
| Vazirmatn, Noto Kufi Arabic, Noto Sans Arabic, Noto Naskh Arabic, IBM Plex Sans Arabic | 0 | usable |

The five that pass all ship, and `Ctrl+Alt+N` cycles them in game. Vazirmatn is the default; set
`FontFile` in `com.kentum.arabic.cfg` to pin another. Screen a new candidate with
`ShaperTest --glyphs` followed by `check_font_coverage.py` — the block-coverage number alone is
misleading, since most of Presentation Forms-B is Persian and Urdu letters Arabic never produces.

### Building

```powershell
# One-off: install BepInEx into the game and point the build at it
.\scripts\setup-dev.ps1

# Build + copy plugin, strings and fonts into the game
.\scripts\deploy.ps1

# Iterate on translations only
.\scripts\deploy.ps1 -SkipBuild
```

The build resolves the game's assemblies through the `KENTUM_DIR` environment variable (or
`src/KentumArabic/local.props`). Those assemblies are never redistributed.

### In-game hotkeys

| Keys | Action |
|---|---|
| `Ctrl+F12` | Dump every source string to `_dump/` as translation workbooks |
| `Ctrl+Alt+R` | Hot-reload the TSV files without restarting |
| `Ctrl+Alt+L` | Toggle Kentum's localization debug mode — with Arabic selected this is a live coverage report (red = untranslated) |
| `Ctrl+Alt+T` | Show the shaping test battery |
| `Ctrl+Alt+M` | Cycle shaping mode |
| `Ctrl+Alt+N` | Cycle the bundled Arabic fonts, live — judge a typeface on the real menus, not a sample sheet |
| `Ctrl+Alt+F` | Audit font coverage across the whole translation |
| `Ctrl+Alt+D` | Write missing-key and bypass diagnostics |
| `Ctrl+Alt+S` | Log a status summary |

### Tooling

```bash
# Which codepoints does the shaped translation actually need?
dotnet run --project tools/ShaperTest -c Release -- --glyphs content/strings needed.txt

# Will this font render them? (the Presentation Forms trap)
python tools/check_font_coverage.py "content/fonts/*.ttf" --text "content/strings/*.tsv"

# Check placeholders, tags and duplicate keys before committing
python tools/validate_tsv.py content/strings

# Diacritics TextMeshPro cannot position — reports, does not change
python tools/strip_tashkeel.py content/strings --check

# Shaping regression suite over every shipped string — no game needed
dotnet run --project tools/ShaperTest -c Release -- content/strings
```

### Repository layout

```
src/KentumArabic/     plugin source (RTLTMPro 3.4.3 vendored under Shaping/)
content/strings/      the translation — plain logical Arabic, TSV
content/fonts/        five Arabic typefaces, all SIL OFL (Ctrl+Alt+N cycles them)
content/manifest.json content version + supported game builds
tools/                Python utilities and the shaping test harness
scripts/              setup and deploy
docs/                 install guide (Arabic) and glossary
unity/FontBuilder/    optional: bake a static font atlas instead of the runtime one
```

## Licensing

This project is MIT licensed. It redistributes **none** of the game's assets or assemblies.
Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Kentum is © Tlön Industries. This is an unofficial fan translation, not affiliated with or
endorsed by the developer.
