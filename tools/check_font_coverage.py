#!/usr/bin/env python3
"""Verify a font can actually render shaped Arabic before it ships.

TextMeshPro has no OpenType shaping engine, so the plugin converts Arabic letters into
Unicode *presentation forms* (U+FE70-FEFF) and hands those to TMP directly. A font that
only carries the base Arabic block (U+0600-06FF) and relies on OpenType — which is most
modern Arabic web fonts — renders every one of those as an empty box.

That failure looks identical to "the font didn't load", so it is worth ruling out in one
second here rather than an afternoon in game.

Usage:
    python tools/check_font_coverage.py content/fonts/*.ttf
    python tools/check_font_coverage.py --text content/strings/ui_UI.tsv content/fonts/x.ttf
"""
from __future__ import annotations

import argparse
import glob
import sys
import unicodedata

try:
    from fontTools.ttLib import TTFont
except ImportError:
    sys.exit("fontTools is required:  python -m pip install fonttools")

# (name, first, last, required_for_primary, note)
#
# "required_for_primary" only applies when the font is the sole font for the text. In this
# project the Arabic face is registered as a TMP *fallback*, so Latin and digits keep coming
# from the game's own Montserrat and the Arabic font is not expected to carry them. Arabic-only
# faces such as Noto Naskh Arabic legitimately ship with almost no Latin.
RANGES = [
    ("Basic Latin",            0x0020, 0x007E, True,
     "only needed if this font renders text on its own rather than as a fallback"),
    ("Latin-1 Supplement",     0x00A0, 0x00FF, False, ""),
    ("Arabic",                 0x0600, 0x06FF, True,
     "the logical-order source characters"),
    ("Arabic Supplement",      0x0750, 0x077F, False, ""),
    ("Arabic Extended-A",      0x08A0, 0x08FF, False, ""),
    ("Presentation Forms-A",   0xFB50, 0xFDFF, False,
     "ligatures, mostly Persian/Urdu"),
    ("Presentation Forms-B",   0xFE70, 0xFEFF, True,
     "THE critical range - contextual letter shapes"),
]

# Below this, shaped Arabic cannot render. The block holds ~141 assigned codepoints.
FORMS_B_MINIMUM = 100


def coverage(cmap, first, last):
    present = [c for c in range(first, last + 1) if c in cmap]
    assigned = []
    for c in range(first, last + 1):
        try:
            unicodedata.name(chr(c))
            assigned.append(c)
        except ValueError:
            pass
    return present, assigned


def check_font(path, extra_text=None, role="fallback"):
    try:
        font = TTFont(path, fontNumber=0, lazy=True)
        cmap = font.getBestCmap()
    except Exception as exc:  # noqa: BLE001
        print(f"  ERROR  could not read {path}: {exc}")
        return False

    print(f"\n{path}")
    try:
        name = font["name"].getDebugName(4) or "?"
        print(f"  face: {name}   glyphs: {font['maxp'].numGlyphs}")
    except Exception:  # noqa: BLE001
        pass

    ok = True
    for label, first, last, required, note in RANGES:
        present, assigned = coverage(cmap, first, last)
        pct = (100.0 * len(present) / len(assigned)) if assigned else 0.0

        latin_optional = role == "fallback" and first < 0x0600
        if label == "Presentation Forms-B":
            good = len(present) >= FORMS_B_MINIMUM
        elif required and not latin_optional:
            good = pct >= 90.0
        else:
            good = True

        if latin_optional and pct < 90.0:
            mark = "n/a "  # supplied by the primary font
        else:
            mark = "OK  " if good else "FAIL"
        if not good:
            ok = False
        suffix = f"   <- {note}" if note and (not good or required) else ""
        print(f"  [{mark}] {label:<22} U+{first:04X}-{last:04X}  "
              f"{len(present):4d}/{len(assigned):4d} ({pct:5.1f}%){suffix}")

    if extra_text:
        # Only Arabic-script characters matter here: Latin and digits in the translation are
        # rendered by the game's own font, not this one.
        missing = sorted({
            ch for ch in extra_text
            if ord(ch) not in cmap and is_arabic(ord(ch))
        })
        if missing:
            ok = False
            shown = "".join(missing[:40])
            print(f"  [FAIL] {len(missing)} Arabic character(s) used by the translation are missing: {shown}")
        else:
            print("  [OK  ] every Arabic character used by the supplied translation text is present")

    print(f"  => {'USABLE' if ok else 'NOT USABLE for shaped Arabic'}")
    return ok


def is_arabic(code):
    return (0x0600 <= code <= 0x06FF or 0x0750 <= code <= 0x077F
            or 0x08A0 <= code <= 0x08FF or 0xFB50 <= code <= 0xFDFF
            or 0xFE70 <= code <= 0xFEFF)


def read_translation_text(paths):
    """Collect the Arabic column from TSV translation files.

    Mirrors the plugin's own loader: comments are skipped and the header row is consumed
    rather than treated as data (otherwise the literal header word "ar" is read as text).
    """
    text = []
    for path in paths:
        for pattern in glob.glob(path):
            header_seen = False
            ar_col = 1
            with open(pattern, encoding="utf-8-sig") as handle:
                for line in handle:
                    if not line.strip() or line.startswith("#"):
                        continue
                    cols = line.rstrip("\n").split("\t")
                    if not header_seen:
                        header_seen = True
                        lowered = [c.strip().lower() for c in cols]
                        if "ar" in lowered or "arabic" in lowered:
                            ar_col = lowered.index("ar" if "ar" in lowered else "arabic")
                            continue  # header row, not data
                    if len(cols) > ar_col:
                        text.append(cols[ar_col])
    return "".join(text)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("fonts", nargs="+", help="font files to check (globs allowed)")
    parser.add_argument("--text", action="append", default=[],
                        help="TSV translation file whose Arabic column must be renderable")
    parser.add_argument("--role", choices=["fallback", "primary"], default="fallback",
                        help="'fallback' (default) expects Latin to come from the game's own "
                             "font; 'primary' requires this font to cover Latin too")
    args = parser.parse_args()

    extra = read_translation_text(args.text) if args.text else None

    paths = []
    for pattern in args.fonts:
        paths.extend(sorted(glob.glob(pattern)) or [pattern])

    results = [check_font(p, extra, args.role) for p in paths]
    print()
    if all(results):
        print(f"All {len(results)} font(s) can render shaped Arabic.")
        return 0
    print("At least one font cannot render shaped Arabic - see FAIL rows above.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
