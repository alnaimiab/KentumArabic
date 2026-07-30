#!/usr/bin/env python3
"""Refuse non-ASCII bytes in the player-facing scripts.

Windows PowerShell 5.1 - the version shipped with Windows and the one a .bat wrapper invokes -
decodes a .ps1 file using the system ANSI code page unless the file starts with a UTF-8 BOM.
A UTF-8 file without a BOM is therefore read as mojibake on any machine whose ANSI code page is
not UTF-8, which is nearly all of them. The damage is not cosmetic: the replacement characters
can include quotes and braces, so the script fails to parse and does not run at all.

This is invisible to a developer whose machine has the "Use Unicode UTF-8 for worldwide language
support" option enabled, because there the ANSI code page *is* UTF-8 and everything works.

A BOM would fix the decoding but not the display: the console still needs a code page and a font
that can draw the characters, and the legacy console host often has neither. So for these scripts
the rule is ASCII only, and the Arabic lives in the documentation, which browsers render fine.

    python tools/check_scripts_ascii.py scripts
"""

import argparse
import glob
import os
import sys

CHECKED_SUFFIXES = (".ps1", ".bat", ".cmd")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("directory")
    args = ap.parse_args()

    failures = 0
    checked = 0

    for path in sorted(glob.glob(os.path.join(args.directory, "*"))):
        if not path.lower().endswith(CHECKED_SUFFIXES):
            continue
        checked += 1
        data = open(path, "rb").read()

        offending = [(i, b) for i, b in enumerate(data) if b > 0x7F]
        if not offending:
            continue

        # Report by line so the message is actionable.
        lines = data.split(b"\n")
        offset = 0
        for lineno, line in enumerate(lines, start=1):
            bad = [b for b in line if b > 0x7F]
            if bad:
                shown = line.decode("utf-8", errors="replace").strip()
                print(f"{path}:{lineno}: {len(bad)} non-ASCII byte(s): {shown[:90]}")
                failures += 1
            offset += len(line) + 1

    if failures:
        print(f"\n{failures} line(s) contain non-ASCII bytes.")
        print("Windows PowerShell 5.1 decodes .ps1 with the system ANSI code page unless the file")
        print("has a UTF-8 BOM, so these become mojibake and the script fails to parse on most")
        print("machines. Keep the scripts ASCII; put the Arabic in docs/ instead.")
        return 1

    print(f"{checked} script(s) checked, all ASCII.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
