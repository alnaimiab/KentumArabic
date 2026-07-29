#!/usr/bin/env python3
"""Remove Arabic diacritics from the translation.

Why this is not a style preference:

TextMeshPro has no GPOS mark-to-base positioning. It draws a combining mark as a
zero-advance glyph at the current pen position, so the mark lands wherever the base
letter's advance left it rather than anchored to that letter's actual shape. On narrow
non-joining letters - reh, dal, waw - the offset is large enough to see, and it changes
from word to word, which reads on screen as letters that will not sit still.

No font fixes this. Mark placement is the renderer's job and this renderer does not do it.
So diacritics in Kentum are not "elegant but optional", they are a visible defect.

Ambiguity that genuinely needed a mark is resolved by spelling instead, which costs
nothing at render time - see RESPELL below.

    python tools/strip_tashkeel.py content/strings          # apply
    python tools/strip_tashkeel.py content/strings --check  # report only, exit 1 if any
"""

import argparse
import glob
import io
import os
import sys

# U+064B-0652 tanween, harakat, shadda, sukun; U+0670 superscript alef;
# U+0653-0655 maddah and hamza above/below.
MARKS = set(range(0x064B, 0x0653)) | {0x0670, 0x0653, 0x0654, 0x0655}

# Words where dropping the mark would create a real reading ambiguity. Respelled with
# letters rather than marks, which is also the conventional Arabic transliteration.
RESPELL = {
    "كِنت": "كينت",   # otherwise reads as "kunt" (= I was), and the name is everywhere
    "نَتّي": "ناتي",   # the assistant's name
}


def strip(text):
    for before, after in RESPELL.items():
        text = text.replace(before, after)
    return "".join(ch for ch in text if ord(ch) not in MARKS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("strings_dir")
    ap.add_argument("--check", action="store_true",
                    help="report files that still contain diacritics, change nothing")
    args = ap.parse_args()

    files_changed = rows_changed = marks_removed = 0

    for path in sorted(glob.glob(os.path.join(args.strings_dir, "*.tsv"))):
        lines = io.open(path, encoding="utf-8").read().split("\n")
        out, changed, removed = [], 0, 0

        for line in lines:
            if line.startswith("#") or "\t" not in line:
                out.append(line)
                continue
            key, _, value = line.partition("\t")
            if key in ("key", "field", "id"):
                out.append(line)
                continue
            stripped = strip(value)
            if stripped != value:
                changed += 1
                removed += sum(1 for ch in value if ord(ch) in MARKS)
            out.append(key + "\t" + stripped)

        if changed:
            files_changed += 1
            rows_changed += changed
            marks_removed += removed
            print(f"  {os.path.basename(path):28} {changed:5} row(s), {removed:5} mark(s)")
            if not args.check:
                io.open(path, "w", encoding="utf-8", newline="\n").write("\n".join(out))

    if args.check:
        if rows_changed:
            print(f"\n{rows_changed} row(s) across {files_changed} file(s) still carry diacritics.")
            print("TextMeshPro cannot position them; run without --check to remove them.")
            return 1
        print("No diacritics found.")
        return 0

    print(f"\nRemoved {marks_removed} mark(s) from {rows_changed} row(s) in {files_changed} file(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
