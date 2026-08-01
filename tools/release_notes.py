#!/usr/bin/env python3
"""Render the release notes a player actually needs.

The release page is the only documentation most people will ever read, and almost none of them
are developers. Left to be written by hand each time it drifts into a changelog of internal
faults - accurate, and useless to someone who just wants Arabic in their game.

So the body is a fixed template covering install, uninstall, what is translated, which file to
download and where to report a problem. The only part that varies per release is one plain-language
line, which lives in content/manifest.json as changelogAr and is the same line the in-game update
notice shows. Writing it there once keeps the two from disagreeing.

    python tools/release_notes.py            # -> dist/RELEASE-NOTES.md
    python tools/release_notes.py --check    # verify the template renders, for CI
"""

import glob
import json
import os
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TEMPLATE = os.path.join(REPO, "docs", "release-notes-template.md")
MANIFEST = os.path.join(REPO, "content", "manifest.json")
STRINGS = os.path.join(REPO, "content", "strings")
OUT = os.path.join(REPO, "dist", "RELEASE-NOTES.md")

PLACEHOLDERS = ("version", "changelog", "strings", "dialogue")


def count_rows(path):
    """Translated rows: not the header, not a comment, and carrying an Arabic column.

    The header is not line 1 - every file opens with a block of Arabic comments - so it has to be
    recognised as the first non-comment row rather than by position.
    """
    total = 0
    seen_header = False
    with open(path, encoding="utf-8-sig") as handle:
        for line in handle:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            if not seen_header:
                seen_header = True
                if line.split("\t")[0].strip().lower() == "key":
                    continue
            parts = line.split("\t")
            if len(parts) >= 2 and parts[1].strip():
                total += 1
    return total


def main():
    with open(MANIFEST, encoding="utf-8") as handle:
        manifest = json.load(handle)

    changelog = manifest.get("changelogAr", "").strip()
    if not changelog:
        print("error: content/manifest.json has no changelogAr.")
        print("It is what the release page and the in-game update notice both show, so it")
        print("cannot be blank. One or two plain sentences, written for a player.")
        return 1

    files = sorted(glob.glob(os.path.join(STRINGS, "*.tsv")))
    dialogue = sum(count_rows(f) for f in files if os.path.basename(f) == "dialogue.tsv")
    total = sum(count_rows(f) for f in files)

    with open(TEMPLATE, encoding="utf-8") as handle:
        body = handle.read()

    values = {
        "version": manifest["pluginVersion"],
        "changelog": changelog,
        "strings": f"{total:,}",
        "dialogue": f"{dialogue:,}",
    }

    for name in PLACEHOLDERS:
        body = body.replace("{" + name + "}", values[name])

    leftover = [chunk for chunk in body.split("{")[1:] if "}" in chunk.split("\n")[0]]
    if leftover:
        print(f"error: unresolved placeholder(s) in the template: {leftover}")
        return 1

    if "--check" in sys.argv:
        print(f"Release notes render cleanly for v{values['version']} "
              f"({values['strings']} strings, {values['dialogue']} dialogue lines).")
        return 0

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as handle:
        handle.write(body)

    print(f"Wrote {os.path.relpath(OUT, REPO)} for v{values['version']}.")
    print(f"  {values['strings']} strings, {values['dialogue']} dialogue lines")
    return 0


if __name__ == "__main__":
    sys.exit(main())
