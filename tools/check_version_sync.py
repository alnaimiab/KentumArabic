#!/usr/bin/env python3
"""Keep the plugin version consistent across the code, the manifest and the published releases.

Four places have to agree, and each drifts differently:

  Plugin.cs      what BepInEx prints to the log, and the log is what a bug report quotes. Stale
                 here and the investigation starts from the wrong build.
  manifest.json  what the update check compares and what names the release.
  the .csproj    what Windows shows in the DLL's file properties. The quietest of the four - it
                 sat at 0.1.0 through five releases before anyone looked.
  the git tags   what a person can actually download.

The last is the one that bites hardest. Bumping the version on the branch while holding the tag
back leaves the repository advertising a version nobody can download - the update check reads the
manifest from the branch, so it points at a release that ships something older. The rule that
avoids it: the version is bumped as part of publishing, never before.

    python tools/check_version_sync.py
    python tools/check_version_sync.py --check-tag   # also require a matching git tag
"""

import json
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def tag_exists(version):
    import subprocess
    try:
        out = subprocess.run(["git", "tag", "--list", f"v{version}"],
                             cwd=REPO, capture_output=True, text=True, check=True)
        return out.stdout.strip() != ""
    except Exception:
        return None  # git unavailable; not this tool's problem to report


def grab(csproj, tag):
    match = re.search(rf"<{tag}>([^<]+)</{tag}>", csproj)
    return match.group(1) if match else f"(no <{tag}>)"


def strip_revision(value):
    """AssemblyVersion carries a fourth component the other three do not."""
    parts = value.split(".")
    return ".".join(parts[:3]) if len(parts) == 4 and parts[3] == "0" else value


def main():
    check_tag = "--check-tag" in sys.argv
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

    csproj_path = os.path.join(REPO, "src", "KentumArabic", "KentumArabic.csproj")
    with open(csproj_path, encoding="utf-8") as handle:
        csproj = handle.read()

    found = {
        "src/KentumArabic/Plugin.cs      ": code_version,
        "content/manifest.json           ": manifest_version,
        "KentumArabic.csproj <Version>   ": grab(csproj, "Version"),
        "KentumArabic.csproj <Assembly..>": strip_revision(grab(csproj, "AssemblyVersion")),
        "KentumArabic.csproj <FileVer..> ": strip_revision(grab(csproj, "FileVersion")),
    }

    if len(set(found.values())) != 1:
        print("version mismatch:")
        for where, value in found.items():
            print(f"  {where} : {value}")
        print("\nThese must all match. The first is what the log reports, the second is what the")
        print("release is called and what the update check compares, and the rest are what the")
        print("built DLL claims to be.")
        return 1

    if check_tag:
        exists = tag_exists(code_version)
        if exists is False:
            print(f"version {code_version} has no tag v{code_version}.")
            print("\nThe branch would advertise a version nobody can download: the update check")
            print("reads manifest.json from the branch and links to the latest release. Either")
            print("publish it, or move the bump into the commit that publishes.")
            return 1
        if exists:
            print(f"Plugin version {code_version} matches the code, the manifest and tag v{code_version}.")
            return 0

    print(f"Plugin version {code_version} is consistent across the code, the manifest and the csproj.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
