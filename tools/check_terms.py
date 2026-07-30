#!/usr/bin/env python3
"""Enforce the agreed Arabic rendering of recurring terms.

A glossary nobody checks is a glossary that drifts. One term rendered two ways is the
single clearest tell that text was produced in bulk rather than localised - a player
reads "سائل معرفي" on the item and "سائل إدراكي" in the dialogue about that same item
and knows immediately that nobody was minding it.

The list lives in content/terms.tsv so translators own it without touching code. Only
terms that have actually appeared more than one way belong there; article variation
(مخزون / المخزون) is ordinary grammar and is not checked.

Rejected forms are matched on word boundaries, so a rejected form that happens to be a
substring of a legitimate word does not fire.

    python tools/check_terms.py content/strings              # uses content/terms.tsv
    python tools/check_terms.py content/strings --terms X    # a different term list
"""

import argparse
import glob
import io
import os
import re
import sys

# Arabic has no case, and \b does not behave usefully around Arabic script in `re`,
# so word edges are defined explicitly: anything that is not an Arabic letter.
ARABIC_LETTER = r"ء-ي٠-٩"
EDGE = rf"(?:^|[^{ARABIC_LETTER}])"
EDGE_END = rf"(?:$|[^{ARABIC_LETTER}])"


def load_terms(path):
    terms = []
    for lineno, raw in enumerate(io.open(path, encoding="utf-8"), start=1):
        line = raw.rstrip("\n")
        if not line.strip() or line.startswith("#"):
            continue
        cols = line.split("\t")
        if len(cols) < 2:
            continue
        if cols[0].strip() == "canonical":
            continue
        canonical = cols[0].strip()
        rejected = [r.strip() for r in cols[1].split("|") if r.strip()]
        for bad in rejected:
            # A leading "^" means the form is only wrong at the start of a string. The
            # imperatives need this: أنه and سلم and ابن are ordinary words mid-sentence
            # ("that he", "peace", "son") and only become the wrong word when they lead a
            # line and the reader expects a command.
            if bad.startswith("^"):
                pattern = re.compile(r"^" + re.escape(bad[1:]) + EDGE_END)
                bad = bad[1:] + " (at the start of a string)"
            else:
                pattern = re.compile(EDGE + re.escape(bad) + EDGE_END)
            terms.append((canonical, bad, pattern))
    return terms


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("strings_dir")
    ap.add_argument("--terms", default=os.path.join("content", "terms.tsv"))
    args = ap.parse_args()

    if not os.path.exists(args.terms):
        print(f"term list not found: {args.terms}")
        return 1

    terms = load_terms(args.terms)
    failures = 0

    for path in sorted(glob.glob(os.path.join(args.strings_dir, "*.tsv"))):
        for lineno, raw in enumerate(io.open(path, encoding="utf-8"), start=1):
            line = raw.rstrip("\n")
            if line.startswith("#") or "\t" not in line:
                continue
            key, _, value = line.partition("\t")
            if key in ("key", "field", "id"):
                continue
            for canonical, bad, pattern in terms:
                if pattern.search(value):
                    print(f"{path}:{lineno}: [{key}] uses '{bad}'; "
                          f"the agreed rendering is '{canonical}'")
                    failures += 1

    if failures:
        print(f"\n{failures} inconsistent term use(s). Fix them, or if the rejected form "
              f"is right after all, change {args.terms} and docs/glossary.md together.")
        return 1

    print(f"Terminology consistent across {len(terms)} checked rendering(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
