#!/usr/bin/env python3
"""Build the release archives.

Three packages, because the engine and the content version independently:

  Full         BepInEx + plugin + font + translation. The default download.
  Content      strings/ only. A translation update is then a ~100 KB file swap
               rather than a reinstall - which is the whole point of keeping the
               translation outside the DLL.
  PluginOnly   plugin + font, for people who already run BepInEx.

Also writes SHA256SUMS.txt. Users will see a SmartScreen warning about winhttp.dll
(unavoidable for any BepInEx mod), so publishing checksums is the least we can do.

Usage:
    python tools/package_release.py --bepinex path/to/BepInEx_win_x64_5.4.23.2.zip
    python tools/package_release.py --skip-full     # content update only
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sys
import tempfile
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PLUGIN_SUBDIR = "BepInEx/plugins/KentumArabic"


def read_manifest():
    with open(os.path.join(REPO, "content", "manifest.json"), encoding="utf-8") as handle:
        return json.load(handle)


def add_file(zf, src, arc):
    zf.write(src, arc)


def add_tree(zf, src_dir, arc_dir, exts=None):
    count = 0
    for root, _dirs, names in os.walk(src_dir):
        for name in sorted(names):
            if exts and not name.lower().endswith(exts):
                continue
            src = os.path.join(root, name)
            rel = os.path.relpath(src, src_dir).replace("\\", "/")
            add_file(zf, src, f"{arc_dir}/{rel}")
            count += 1
    return count


def plugin_files(zf):
    """Plugin assembly, fonts, translation and manifest."""
    dll = os.path.join(REPO, "src", "KentumArabic", "bin", "Release", "KentumArabic.dll")
    if not os.path.exists(dll):
        sys.exit(f"Plugin not built: {dll}\nRun: dotnet build src/KentumArabic -c Release")
    add_file(zf, dll, f"{PLUGIN_SUBDIR}/KentumArabic.dll")

    add_tree(zf, os.path.join(REPO, "content", "fonts"), f"{PLUGIN_SUBDIR}/fonts",
             exts=(".ttf", ".otf", ".txt"))
    add_tree(zf, os.path.join(REPO, "content", "strings"), f"{PLUGIN_SUBDIR}/strings",
             exts=(".tsv",))
    add_file(zf, os.path.join(REPO, "content", "manifest.json"), f"{PLUGIN_SUBDIR}/manifest.json")


# steam_appid.txt is deliberately NOT packaged. It is a local development file that
# lets the executable initialise Steamworks when launched directly instead of through
# the client; shipping it would change how players' games talk to Steam.


def installers(zf, prefix=""):
    """
    The install/uninstall scripts, at the root of the archive.

    They are worth shipping even though unzipping into the game folder also works, because
    they do two things a player cannot: install BepInEx with its checksum verified, and record
    whether BepInEx was already there. Without that record an uninstaller has to guess, and
    guessing wrong deletes somebody's other mods.

    The .bat wrappers matter as much as the .ps1 files: PowerShell scripts do not run on
    double-click, they open in Notepad. Their names are ASCII deliberately - cmd.exe resolves a
    batch file's own path through the system ANSI codepage, so an Arabic filename can fail to
    launch on a Western-locale Windows.
    """
    for name in ("install.ps1", "uninstall.ps1", "install.bat", "uninstall.bat"):
        src = os.path.join(REPO, "scripts", name)
        if os.path.exists(src):
            add_file(zf, src, prefix + name)


def docs(zf, prefix=""):
    for name, arc in [("README.md", "README.md"),
                      ("LICENSE", "LICENSE.txt"),
                      ("THIRD-PARTY-NOTICES.md", "THIRD-PARTY-NOTICES.md"),
                      ("docs/install-ar.md", "التثبيت.md")]:
        src = os.path.join(REPO, name)
        if os.path.exists(src):
            add_file(zf, src, prefix + arc)


def build_full(out_dir, version, bepinex_zip):
    """
    Everything needed, so installing is "unzip into the game folder".

    BepInEx is LGPL-2.1 and redistributing its binaries unmodified is permitted;
    THIRD-PARTY-NOTICES.md carries the attribution and source link.
    """
    path = os.path.join(out_dir, f"KentumArabic-Full-v{version}.zip")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        if bepinex_zip:
            with zipfile.ZipFile(bepinex_zip) as src:
                for info in src.infolist():
                    if info.is_dir():
                        continue
                    # Skip BepInEx's own changelog to avoid confusion with ours.
                    if info.filename == "changelog.txt":
                        continue
                    zf.writestr(info.filename, src.read(info.filename))
        plugin_files(zf)
        installers(zf)
        docs(zf)
    return path


def build_content(out_dir, content_version):
    path = os.path.join(out_dir, f"KentumArabic-Content-{content_version}.zip")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        n = add_tree(zf, os.path.join(REPO, "content", "strings"),
                     f"{PLUGIN_SUBDIR}/strings", exts=(".tsv",))
        add_file(zf, os.path.join(REPO, "content", "manifest.json"),
                 f"{PLUGIN_SUBDIR}/manifest.json")
        zf.writestr("اقرأني.txt",
                    "تحديث الترجمة فقط.\r\n\r\n"
                    "فك الضغط داخل مجلد اللعبة (المجلد الذي يحتوي Kentum.exe)،\r\n"
                    "واستبدل الملفات القديمة عند السؤال.\r\n\r\n"
                    "لا حاجة لإعادة تثبيت أي شيء آخر.\r\n")
        if n == 0:
            print("  warning: no .tsv files found in content/strings")
    return path


def build_plugin_only(out_dir, version):
    path = os.path.join(out_dir, f"KentumArabic-PluginOnly-v{version}.zip")
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as zf:
        plugin_files(zf)
        # Ships here too: the installer skips BepInEx when it is already present, which is
        # exactly this audience, and the uninstaller is worth having either way.
        installers(zf)
        docs(zf)
    return path


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--bepinex", help="path to BepInEx_win_x64_*.zip to bundle into Full")
    parser.add_argument("--out", default=os.path.join(REPO, "dist"))
    parser.add_argument("--skip-full", action="store_true")
    args = parser.parse_args()

    manifest = read_manifest()
    version = manifest["pluginVersion"]
    content_version = manifest["contentVersion"]

    os.makedirs(args.out, exist_ok=True)
    built = []

    if not args.skip_full:
        if not args.bepinex:
            print("note: --bepinex not given; the Full package will not include the loader,\n"
                  "      so users would have to install BepInEx themselves.")
        built.append(build_full(args.out, version, args.bepinex))
        built.append(build_plugin_only(args.out, version))

    built.append(build_content(args.out, content_version))

    sums = os.path.join(args.out, "SHA256SUMS.txt")
    with open(sums, "w", encoding="utf-8", newline="\n") as handle:
        for path in built:
            size = os.path.getsize(path) / 1048576
            digest = sha256(path)
            handle.write(f"{digest}  {os.path.basename(path)}\n")
            print(f"  {os.path.basename(path):<44} {size:6.2f} MB")
            print(f"    sha256 {digest}")

    print(f"\n{len(built)} package(s) in {args.out}")
    print(f"checksums: {sums}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
