# تعريب لعبة Kentum — Kentum Arabic

<div dir="rtl">

تعريب غير رسمي للعبة **Kentum** من استوديو Tlön Industries، يضيف **العربية كلغة حقيقية قابلة للاختيار** من قائمة إعدادات اللعبة — بحروف متصلة، ومن اليمين لليسار، ومحاذاة صحيحة.

لا يُعدَّل أي ملف من ملفات اللعبة الأصلية. تُسجَّل اللغة أثناء التشغيل، وتُحمَّل الحروف العربية عبر نظام الخطوط الاحتياطية في TextMeshPro، وتعيش الترجمة نفسها في ملفات نصية منفصلة يمكن تحديثها دون إعادة تثبيت أي شيء.

## التثبيت

1. نزّل `KentumArabic-Full-vX.Y.Z.zip` من [صفحة الإصدارات](../../releases/latest) وفك ضغطه في أي مكان.
2. انقر نقرًا مزدوجًا على **`install.bat`**.
3. شغّل اللعبة ← **Options** ← **Language** ← **العربية**.

يعثر السكربت على مجلد اللعبة بنفسه — حتى لو كانت على قرص آخر — ويثبّت BepInEx بعد التحقق من بصمته، ويطلب صلاحية المسؤول مرة واحدة إن لزم. وإن فضّلت اليدوي، فك ضغط الحزمة داخل مجلد اللعبة مباشرة؛ النتيجة واحدة عدا شيئًا واحدًا يشرحه القسم التالي.

### إلغاء التثبيت
انقر نقرًا مزدوجًا على **`uninstall.bat`**.

يزيل ما ثبّته التعريب فقط. و**لا يحذف BepInEx** إن كان موجودًا قبله أو كانت تعديلات أخرى تعتمد عليه — لأنه مُحمِّل مشترك، وحذفه دون تمييز يأخذ معه تعديلات غيرنا. ولهذا يكتب المثبّت `install-record.json` يسجّل فيه ما فعله؛ ومن ثبّت يدويًا بفك الضغط لن يجد السكربت سجلًا، فيمتنع عن التخمين ويخبرك بما تحذفه بنفسك.

جرّب `uninstall.bat -WhatIf` لترى ما سيُحذف قبل حذفه.

### تحديث الترجمة فقط
نزّل `KentumArabic-Content-*.zip` واستبدل به مجلد `BepInEx/plugins/KentumArabic/strings`. لا حاجة لإعادة التثبيت ولا لإعادة تنزيل الخط.

### ملاحظة عن تحذير Windows
يستخدم التعريب أداة التحميل المفتوحة المصدر **BepInEx**، وهي تعمل عبر ملف `winhttp.dll`. قد يعرض Windows SmartScreen أو بعض مضادات الفيروسات تحذيرًا لهذا السبب. بصمات التحقق (SHA-256) لكل ملف منشورة مع كل إصدار.

## الحالة

**الترجمة مكتملة: 3,308 نصًا، منها 732 سطر حوار.**

| | |
|---|---|
| اللغة تظهر في الإعدادات | ✅ |
| تشكيل الحروف ووصلها، والاتجاه، والمحاذاة | ✅ |
| التفاف الأسطر بترتيب صحيح | ✅ |
| الأرقام والنصوص اللاتينية داخل الجملة | ✅ |
| وسوم التنسيق `<color>` و`<size>` والعناصر النائبة `{0}` | ✅ |
| نصوص الواجهة والعناصر والتقنيات والمهام | ✅ |
| الحوارات وأسماء الشخصيات | ✅ |
| خمسة خطوط عربية، يبدَّل بينها داخل اللعبة | ✅ |

النصوص التي قد يضيفها الاستوديو في تحديث لاحق ستظهر بالإنجليزية إلى أن تُترجم — لا ينكسر شيء، لأن اللعبة ترتد تلقائيًا إلى الإنجليزية عند غياب المفتاح.

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
   translation into it. Every downstream consumer — `LocalizedStaticText`, `LocalizedStaticImage`,
   the quest log — then works with no further patching.
2. **Reconciles the options dropdown**, which is the one consumer that does *not* work for free.
   `OptionsPanel` builds the language row once, from a snapshot taken before this mod can
   register anything, into a fixed array it never rebuilds:

   ```csharp
   int num = GameHub.GetPlayerPrefsInt("KENTUM_PREFS_LANGUAGE", defaultValue);
   if (num < 0 || num >= optionNames.Length) num = defaultValue;   // silent reset
   ```

   So Arabic is absent from the list, and a saved index of 10 is out of range and quietly
   discarded — which is how the game ends up fully in Arabic while the language row reads
   ENGLISH. A watcher compares the dropdown against the live language list a few times a second
   until they agree, rebuilding the option list and correcting the shown value **by language
   name, never by index**: an index only means something relative to a list whose length is the
   thing in dispute. This deliberately does not depend on a Harmony hook — patches on all three
   relevant `OptionsPanel` methods report as applied and never execute.
3. **Supplies glyphs** by building a `TMP_FontAsset` at runtime from a bundled `.ttf` with a
   dynamic atlas, and registering it as a TMP *fallback*. Latin and digits keep coming from the
   game's own Montserrat, so the UI keeps its visual identity and mixed strings just work.
4. **Shapes the text** through `ITextPreprocessor`, TMP's own supported hook. TMP calls
   `PreprocessText` from `ParseInputText` on the *final composed* string, which is the property
   that matters: a save slot reading "Day {0}" is shaped after the number is substituted, not
   before. Harmony patches on `set_text` were tried first and are a dead end here — they were
   reported as applied and never once executed, and even had they fired they would have seen
   the template rather than the composed result.

Translation data stays as **plain logical-order Arabic** in TSV files. Shaping is a render-time
detail, which keeps the data reviewable, diffable and usable in CAT tools — and means improving
the shaper re-shapes everything for free.

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
493 of the 3,308 strings run past 60 characters — item and technology descriptions above all — so
wrap order is not an edge case.

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

### Quality gates

Four checks run on every push. What they have in common is that the damage is invisible in the
file and only appears on screen, often far from the string that caused it.

| Check | Catches | What it costs to miss |
|---|---|---|
| `validate_tsv.py` | broken `{0}`, unbalanced tags, duplicate keys | Kentum formats save-slot labels while building the panel, so one reversed `{0}` throws and takes the whole Load screen with it |
| `ShaperTest -- content/strings` | anything shaping mangles, over **every shipped string** | hand-picked cases only cover known failure modes; new content with unfamiliar markup goes unchecked |
| `check_terms.py` | one term rendered two ways | a player who meets both renderings of the same thing can tell nobody was minding the text |
| `strip_tashkeel.py --check` | diacritics TMP cannot position | they land off their letter and shift word to word, reading as text that will not sit still |
| `check_scripts_ascii.py` | non-ASCII in the .ps1/.bat scripts | PowerShell 5.1 decodes them with the system code page, so they fail to parse anywhere the code page is not UTF-8 |
| `check_version_sync.py` | version drift between code, manifest, csproj and tags | the log names a build that is not the one running, or the branch advertises a release nobody can download |

The last two are enforcement of `docs/glossary.md` rather than replacements for it: the glossary
holds the reasoning, `content/terms.tsv` holds the machine-checkable part, and they are meant to
be edited together.

Two constraints in there are worth stating plainly because they are unusual and both are
consequences of the same missing renderer feature:

- **No diacritics anywhere.** Not a style preference — see above.
- **Certain imperatives are banned.** Undiacritised, ابنِ (build) is ابن (son), سلّم (deliver)
  is سلم (peace), أنهِ (finish) is أنَّه. Use شيد، أودع، أكمل. And when swapping a verb, re-read
  what follows it: أودع governs في, not the إلى that belonged to the verb it replaced.

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
| `Ctrl+Alt+G` | Audit and repair the Options > Language dropdown, and log its full state |
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

# Version in the code, the manifest and the tags must agree
python tools/check_version_sync.py --check-tag

# Release notes for the download page — generated, not written by hand
python tools/release_notes.py

# Shaping regression suite over every shipped string — no game needed
dotnet run --project tools/ShaperTest -c Release -- content/strings
```

### Repository layout

```
src/KentumArabic/     plugin source (RTLTMPro 3.4.3 vendored under Shaping/)
content/strings/      the translation — plain logical Arabic, TSV
content/fonts/        five Arabic typefaces, all SIL OFL (Ctrl+Alt+N cycles them)
content/terms.tsv     terms the glossary fixes, enforced by check_terms.py in CI
content/manifest.json content version + supported game builds
tools/                Python utilities and the shaping test harness
scripts/              install.ps1 / uninstall.ps1 for players, setup-dev + deploy for developers
docs/                 install guide (Arabic) and glossary
unity/FontBuilder/    optional: bake a static font atlas instead of the runtime one
```

## Licensing

This project is MIT licensed. It redistributes **none** of the game's assets or assemblies.
Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Kentum is © Tlön Industries. This is an unofficial fan translation, not affiliated with or
endorsed by the developer.
