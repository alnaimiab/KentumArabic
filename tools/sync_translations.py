#!/usr/bin/env python3
"""Move translations between the private workbook and the publishable files.

Two different files exist for one good reason.

  _dump/*.tsv          The workbook. Carries the game's original English and Spanish text
                       so a translator can see the source and a professional reference
                       translation side by side. That text belongs to Tlön Industries, so
                       this directory is git-ignored and never published.

  content/strings/*.tsv What ships. Keys and the Arabic column only — our own work.

`publish` copies the Arabic across, dropping the reference columns. It is what makes the
"we redistribute none of the developer's content" claim actually true rather than aspirational.

The reverse direction needs no tool: the in-game dumper already merges the currently loaded
translation back into the `ar` column, so re-dumping after a game update preserves your work
and shows exactly which keys the developers changed.

Usage:
    python tools/sync_translations.py publish                      # dump -> content/strings
    python tools/sync_translations.py publish --dump-dir <path>
    python tools/sync_translations.py status                       # progress per file
"""
from __future__ import annotations

import argparse
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_DUMP = os.path.join(
    r"C:\Program Files (x86)\Steam\steamapps\common\Kentum",
    "BepInEx", "plugins", "KentumArabic", "_dump")
STRINGS_DIR = os.path.join(REPO, "content", "strings")

HEADER = "key\tar"

BANNER = (
    "# تعريب Kentum\n"
    "#\n"
    "# مُولَّد بواسطة tools/sync_translations.py — لا تحرره يدويًا إن كنت تستخدم\n"
    "# ملف العمل في _dump، لأن إعادة التوليد ستطمس تعديلاتك.\n"
    "#\n"
    "# الأعمدة: المفتاح <TAB> الترجمة العربية.\n"
    "# اكتب العربية عادية منطقية — الإضافة تتولى تشكيل الحروف والاتجاه.\n"
    "# السطر الجديد يُكتب \\n حرفيًا. حافظ على {0} والوسوم <...> كما هي.\n"
    "#\n"
    "# لا يحتوي هذا الملف على النص الإنجليزي أو الإسباني الأصلي عمدًا:\n"
    "# فهو ملك للاستوديو ومحمي بحقوق النشر. راجع THIRD-PARTY-NOTICES.md.\n"
)


def read_tsv(path):
    """Return (rows, ar_index, key_index). Rows are lists of columns; comments dropped."""
    rows = []
    key_col, ar_col = 0, 1
    header_seen = False

    with open(path, encoding="utf-8-sig") as handle:
        for raw in handle:
            line = raw.rstrip("\n").rstrip("\r")
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            cols = line.split("\t")
            if not header_seen:
                header_seen = True
                lowered = [c.strip().lower() for c in cols]
                if "ar" in lowered or "arabic" in lowered:
                    ar_col = lowered.index("ar" if "ar" in lowered else "arabic")
                    for name in ("key", "field", "id"):
                        if name in lowered:
                            key_col = lowered.index(name)
                            break
                    continue
            rows.append(cols)
    return rows, key_col, ar_col


def publish(dump_dir, out_dir, dry_run=False):
    if not os.path.isdir(dump_dir):
        sys.exit(f"Workbook directory not found: {dump_dir}\n"
                 "Run the game with the plugin installed and press Ctrl+F12 to produce it.")

    os.makedirs(out_dir, exist_ok=True)
    files = sorted(n for n in os.listdir(dump_dir) if n.endswith(".tsv"))
    if not files:
        sys.exit(f"No .tsv files in {dump_dir}")

    grand_total = grand_translated = 0

    for name in files:
        src = os.path.join(dump_dir, name)
        rows, key_col, ar_col = read_tsv(src)

        out_lines = [BANNER, HEADER]
        total = translated = 0

        for cols in rows:
            if len(cols) <= key_col:
                continue
            key = cols[key_col].strip()
            if not key:
                continue
            total += 1
            value = cols[ar_col] if len(cols) > ar_col else ""
            if not value.strip():
                continue          # untranslated: omit entirely, the game falls back to English
            translated += 1
            out_lines.append(f"{key}\t{value}")

        grand_total += total
        grand_translated += translated

        dst = os.path.join(out_dir, name)
        pct = (100.0 * translated / total) if total else 0.0
        print(f"  {name:<24} {translated:5d}/{total:<5d} ({pct:5.1f}%) -> {os.path.relpath(dst, REPO)}")

        if translated == 0:
            # Nothing to publish yet; don't create an empty file that looks like a regression.
            if os.path.exists(dst) and not dry_run:
                print(f"    (kept existing {name}: workbook has no translations for it yet)")
            continue

        if not dry_run:
            with open(dst, "w", encoding="utf-8-sig", newline="\n") as handle:
                handle.write("\n".join(out_lines) + "\n")

    pct = (100.0 * grand_translated / grand_total) if grand_total else 0.0
    print(f"\n{grand_translated}/{grand_total} strings translated ({pct:.1f}%)")
    if dry_run:
        print("(dry run - nothing written)")
    else:
        print(f"Published to {os.path.relpath(out_dir, REPO)}")
        print("Next: python tools/validate_tsv.py content/strings && .\\scripts\\deploy.ps1 -SkipBuild")
    return 0


def status(dump_dir):
    print(f"workbook : {dump_dir}")
    print(f"published: {STRINGS_DIR}\n")

    if os.path.isdir(dump_dir):
        total = translated = 0
        for name in sorted(n for n in os.listdir(dump_dir) if n.endswith(".tsv")):
            rows, key_col, ar_col = read_tsv(os.path.join(dump_dir, name))
            t = sum(1 for c in rows if len(c) > ar_col and c[ar_col].strip())
            n = sum(1 for c in rows if len(c) > key_col and c[key_col].strip())
            total += n
            translated += t
            pct = (100.0 * t / n) if n else 0.0
            bar = "#" * int(pct / 5) + "." * (20 - int(pct / 5))
            print(f"  {name:<24} [{bar}] {t:5d}/{n:<5d} {pct:5.1f}%")
        pct = (100.0 * translated / total) if total else 0.0
        print(f"\n  TOTAL{'':<19} {translated}/{total} ({pct:.1f}%)")
    else:
        print("  no workbook yet - run the game and press Ctrl+F12")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["publish", "status"])
    parser.add_argument("--dump-dir", default=DEFAULT_DUMP)
    parser.add_argument("--out", default=STRINGS_DIR)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    if args.command == "publish":
        return publish(args.dump_dir, args.out, args.dry_run)
    return status(args.dump_dir)


if __name__ == "__main__":
    raise SystemExit(main())
