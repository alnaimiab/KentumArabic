@echo off
title Kentum Arabic - Install
rem Everything here is ASCII: the filename, this wrapper and the script's own output. A Windows
rem console is not guaranteed to render Arabic - the code page may not cover it, and the legacy
rem console host may be using a raster font with no Arabic glyphs - so an installer written in
rem Arabic prints boxes on a large share of machines. The docs are Arabic; the tooling is not.
rem
rem The wrapper exists because .ps1 files do not run on double-click: the default execution
rem policy blocks them and Windows opens them in Notepad instead.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
echo.
pause
