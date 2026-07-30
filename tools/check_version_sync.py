#!/usr/bin/env python3
"""Keep the plugin version in the code and in the manifest identical.

The number in Plugin.cs is what BepInEx prints to the log, and the log is what a bug report
quotes. The number in manifest.json is what the release is named and what the update check
compares against. When they drift, a report says 0.1.0 while the person is running 0.2.1, and the
investigation starts from the wrong build.

    python tools/check_version_sync.py
"""

import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main():
    manifest_path = os.path.join(REPO, "content", "manifest.json")
    plugin_path = os.path.join(REPO, "src", "KentumArabic", "Plugin.cs")

    with open(manifest_path, encoding="utf-8") as handle:
        manifest_version = json.load(handle)["pluginVersion"]

    with open(plugin_path, encoding="utf-8") as handle:
        match = re.search(r'PluginVersion\s*=\s*"([^"]+)"', handle.read())

    if not match:
        print(f"error: could not find PluginVersion in {plugin_path}")
        return 1

    code_version = match.group(1)

    if code_version != manifest_version:
        print(f"version mismatch:")
        print(f"  src/KentumArabic/Plugin.cs : {code_version}")
        print(f"  content/manifest.json      : {manifest_version}")
        print("\nThese must match. The first is what the log reports, the second is what the")
        print("release is called and what the update check compares.")
        return 1

    print(f"Plugin version {code_version} matches in both places.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
