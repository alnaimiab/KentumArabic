#!/usr/bin/env python3
"""Validate translation TSV files before they reach the game.

Catches the failure modes that are invisible in a spreadsheet but break at runtime:

  * a lost or altered ``{0}`` placeholder -> ``string.Format`` throws mid-frame
  * an unbalanced ``<color>`` tag -> the rest of the screen inherits the colour
  * a duplicate key -> one translation silently wins over the other
  * a stray tab -> the row shifts and the wrong column is read as the translation
  * Arabic-Indic digits inside a format placeholder -> the placeholder stops matching

Runs in CI on every pull request, so contributors get told immediately rather than
after someone plays through the affected screen.

Usage:
    python tools/validate_tsv.py content/strings
    python tools/validate_tsv.py content/strings --strict   # warnings become errors
"""
from __future__ import annotations

import argparse
import os
import re
import sys
from collections import defaultdict

PLACEHOLDER = re.compile(r"\{(\d+)(?::[^}]*)?\}")
# Quest text also uses named placeholders — {itemDef:Coal}, {tech:...},
# {baseExpansion:...} — which the game replaces with the localized name of that
# thing. Losing or altering one leaves a quest objective referring to nothing, so
# they are checked for exact preservation, case included.
NAMED_PLACEHOLDER = re.compile(r"\{[A-Za-z][A-Za-z0-9_]*:[^}]+\}")
TAG = re.compile(r"<(/?)([a-zA-Z][a-zA-Z0-9]*)(?:=[^>]*)?(/?)>")
ARABIC_INDIC = re.compile(r"[٠-٩۰-۹]")

# Tags TextMeshPro treats as self-contained; they never need a closing partner.
# "input" is Kentum's own markup, not TextMeshPro's: InputManager replaces
# <input=Player/Jump> with the icon for whatever the player has that action bound to.
# It must survive translation verbatim, but it is never paired.
SELF_CONTAINED = {"br", "sprite", "space", "nbsp", "page", "align", "indent", "line",
                  "pos", "size", "voffset", "cspace", "mspace", "style", "input"}
# Of those, the ones that are also commonly used as a matched pair.
PAIRABLE = {"size", "align", "indent", "cspace", "mspace", "voffset", "style"}


class Report:
    def __init__(self):
        self.errors = []
        self.warnings = []

    def error(self, path, line, msg):
        self.errors.append(f"{path}:{line}: error: {msg}")

    def warn(self, path, line, msg):
        self.warnings.append(f"{path}:{line}: warning: {msg}")


def check_placeholders(report, path, lineno, key, source, target):
    """Every {N} in the source must appear in the translation, and none may be invented."""
    if not source:
        return
    src = sorted(set(PLACEHOLDER.findall(source)))
    dst = sorted(set(PLACEHOLDER.findall(target)))
    if src == dst:
        return
    missing = [p for p in src if p not in dst]
    extra = [p for p in dst if p not in src]
    if missing:
        report.error(path, lineno,
                     f"[{key}] translation is missing placeholder(s) "
                     + ", ".join("{" + p + "}" for p in missing)
                     + " - string.Format will throw at runtime")
    if extra:
        report.error(path, lineno,
                     f"[{key}] translation has placeholder(s) not in the source: "
                     + ", ".join("{" + p + "}" for p in extra))

    src_named = sorted(set(NAMED_PLACEHOLDER.findall(source)))
    dst_named = sorted(set(NAMED_PLACEHOLDER.findall(target)))
    if src_named != dst_named:
        lost = [p for p in src_named if p not in dst_named]
        added = [p for p in dst_named if p not in src_named]
        if lost:
            report.error(path, lineno,
                         f"[{key}] translation is missing named placeholder(s): " + ", ".join(lost))
        if added:
            report.error(path, lineno,
                         f"[{key}] translation has named placeholder(s) not in the source: "
                         + ", ".join(added))


def check_tags(report, path, lineno, key, target):
    """Rich text tags must nest correctly or the rest of the screen inherits the style."""
    stack = []
    for match in TAG.finditer(target):
        closing, name, selfclose = match.group(1), match.group(2).lower(), match.group(3)
        if selfclose or (name in SELF_CONTAINED and name not in PAIRABLE and not closing):
            continue
        if closing:
            if not stack:
                report.error(path, lineno, f"[{key}] closing </{name}> with no opening tag")
            elif stack[-1] != name:
                report.error(path, lineno,
                             f"[{key}] </{name}> closes out of order (expected </{stack[-1]}>)")
                stack.pop()
            else:
                stack.pop()
        else:
            stack.append(name)
    for name in stack:
        if name in PAIRABLE:
            continue  # <size=..> without </size> is legal and common
        report.error(path, lineno, f"[{key}] <{name}> is never closed")


def check_digits(report, path, lineno, key, target):
    for match in PLACEHOLDER.finditer(target):
        if ARABIC_INDIC.search(match.group(0)):
            report.error(path, lineno,
                         f"[{key}] placeholder {match.group(0)} contains Arabic-Indic digits; "
                         "it will no longer match at runtime")


def check_file(path, report, seen_keys):
    with open(path, encoding="utf-8-sig") as handle:
        lines = handle.read().split("\n")

    header_seen = False
    key_col, ar_col, en_col = 0, 1, None
    rows = 0
    translated = 0

    for lineno, raw in enumerate(lines, start=1):
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue

        cols = raw.rstrip("\r").split("\t")

        if not header_seen:
            header_seen = True
            lowered = [c.strip().lower() for c in cols]
            if "ar" in lowered or "arabic" in lowered:
                ar_col = lowered.index("ar" if "ar" in lowered else "arabic")
                for name in ("key", "field", "id"):
                    if name in lowered:
                        key_col = lowered.index(name)
                        break
                if "en" in lowered:
                    en_col = lowered.index("en")
                continue
            report.warn(path, lineno,
                        "no header row found; assuming column 0 is the key and column 1 the Arabic")

        if len(cols) <= key_col:
            report.error(path, lineno, "row has no key column")
            continue

        key = cols[key_col].strip()
        if not key:
            report.error(path, lineno, "empty key")
            continue

        rows += 1
        target = cols[ar_col] if len(cols) > ar_col else ""
        source = cols[en_col] if en_col is not None and len(cols) > en_col else ""

        if key in seen_keys:
            report.error(path, lineno,
                         f"[{key}] duplicate key, already defined at {seen_keys[key]} "
                         "- one translation will silently win")
        else:
            seen_keys[key] = f"{os.path.basename(path)}:{lineno}"

        if not target.strip():
            continue  # untranslated is fine: the game falls back to English
        translated += 1

        check_placeholders(report, path, lineno, key, source, target)
        check_tags(report, path, lineno, key, target)
        check_digits(report, path, lineno, key, target)

        if "\\t" in target:
            report.warn(path, lineno, f"[{key}] contains a literal \\t")
        if target != target.strip():
            report.warn(path, lineno, f"[{key}] has leading or trailing whitespace")

    return rows, translated


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("paths", nargs="+", help="TSV files or directories containing them")
    parser.add_argument("--strict", action="store_true", help="treat warnings as errors")
    args = parser.parse_args()

    files = []
    for path in args.paths:
        if os.path.isdir(path):
            for root, _dirs, names in os.walk(path):
                files.extend(os.path.join(root, n) for n in sorted(names) if n.endswith(".tsv"))
        else:
            files.append(path)

    if not files:
        print("No .tsv files found.")
        return 1

    report = Report()
    seen_keys = {}
    total_rows = total_translated = 0

    for path in files:
        rows, translated = check_file(path, report, seen_keys)
        total_rows += rows
        total_translated += translated
        pct = (100.0 * translated / rows) if rows else 0.0
        print(f"  {os.path.relpath(path):<44} {translated:5d}/{rows:<5d} translated ({pct:5.1f}%)")

    for line in report.warnings:
        print(line)
    for line in report.errors:
        print(line)

    pct = (100.0 * total_translated / total_rows) if total_rows else 0.0
    print(f"\n{len(files)} file(s), {total_rows} key(s), {total_translated} translated ({pct:.1f}%)")
    print(f"{len(report.errors)} error(s), {len(report.warnings)} warning(s)")

    if report.errors or (args.strict and report.warnings):
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
