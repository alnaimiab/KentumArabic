# Third-party notices

## What this project does NOT redistribute

Nothing belonging to Tlön Industries ships in a release. Specifically **never** redistributed:

- `data.unity3d` or anything else under `Kentum_Data/`
- `Tlon.*.dll`, `PixelCrushers.dll`, `DialogueSystem.dll`, `Unity.TextMeshPro.dll` or any other
  game assembly
- The contents of `_dump/`

`_dump/` deserves emphasis. The extractor writes the game's **original English and Spanish text**
into the workbook so translators have the source and a professional reference translation
side by side. That text is Tlön Industries' copyrighted content. It is git-ignored, must stay in
the working copy, and must never be published.

Published translation files therefore contain **keys and the Arabic column only**. Contributors
who need the source text run the extractor against their own copy of the game — the same
arrangement every reputable fan-translation project uses.

The build resolves the game's assemblies from a local install via `KENTUM_DIR`; they are
reference-only (`<Private>false</Private>`) and never copied into the output.

---

## Components redistributed in releases

### BepInEx — LGPL-2.1
Plugin loader for Unity games. Binaries (`winhttp.dll`, `doorstop_config.ini`, `BepInEx/core/*`)
are redistributed unmodified in the "Full" release package, as LGPL-2.1 permits.

- Source: https://github.com/BepInEx/BepInEx
- License: https://github.com/BepInEx/BepInEx/blob/master/LICENSE
- Version shipped: 5.4.23.2 (win_x64, Mono)
- SHA-256 of `BepInEx_win_x64_5.4.23.2.zip`:
  `f752ce4e838f4c305b9da1404b6745f2cff23b8bfd494f79f0c84d0a01f59b46`

Includes HarmonyX, MonoMod and Mono.Cecil, redistributed as part of BepInEx under their own
licenses (MIT / LGPL / MIT respectively).

### RTLTMPro — MIT
Arabic/Persian shaping and bidirectional reordering for TextMeshPro. The runtime sources of
v3.4.3 are **vendored** into `src/KentumArabic/Shaping/RTLTMPro/` and compiled into the plugin
assembly, rather than shipped as a separate DLL.

- Source: https://github.com/pnarimani/RTLTMPro
- License: MIT — full text at `src/KentumArabic/Shaping/RTLTMPro/LICENSE`
- Copyright (c) 2018 Mohamad Narimani

Only the runtime shaping files are vendored (`RTLSupport`, `GlyphFixer`, `LigatureFixer`,
`TashkeelFixer`, `RichTextFixer`, `FastStringBuilder`, `GlyphTable`, `TextUtils`, `Char32Utils`,
and the `Types/` enums). The `RTLTextMeshPro` components and editor scripts are not used: this
project calls the shaping stages directly as pure functions.

**Behavioural note.** `RTLSupport.FixRTL` is always called with `fixTextTags: false`. Its
`LigatureFixer` already emits rich text tags in readable forward order at their correct visual
positions, and `RichTextFixer` then reverses each tag range a second time, which leaves tags as
`>roloc<` and makes TextMeshPro print them as literal text. Tag handling is done in
`ArabicShaper` instead. No RTLTMPro source file has been modified.

### Noto Naskh Arabic — SIL Open Font License 1.1
The Arabic typeface. Shipped as unmodified `.ttf` files in `fonts/`, with `OFL.txt` alongside as
the license requires.

- Source: https://github.com/notofonts/arabic
- License: SIL OFL 1.1 — full text at `content/fonts/OFL.txt`
- Copyright: The Noto Project Authors

Chosen because it carries the complete Arabic Presentation Forms-B block (verified: 141/141
codepoints). Fonts lacking that block render shaped Arabic as empty boxes regardless of anything
else, which is why `tools/check_font_coverage.py` exists.

The font is **not** renamed or modified, so the OFL Reserved Font Name clause is not engaged.

---

## Components used only at build time

### fontTools — MIT
Used by `tools/check_font_coverage.py` to inspect font character maps. Not redistributed.

- Source: https://github.com/fonttools/fonttools

### ILSpy / ilspycmd — MIT
Used during development to read the game's localization code and confirm the exact API shapes
this plugin depends on. Not redistributed, and no decompiled output is committed.

- Source: https://github.com/icsharpcode/ILSpy

---

## This project

MIT licensed — see [LICENSE](LICENSE). The translation text itself is contributed by the project's
translators and released under the same terms.

Kentum is © Tlön Industries. This is an unofficial fan translation, not affiliated with or
endorsed by the developer.
