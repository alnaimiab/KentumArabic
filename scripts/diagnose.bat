@echo off
title Kentum Arabic - Diagnostics
rem ASCII only, like the other scripts: Windows PowerShell 5.1 decodes .ps1 with the system ANSI
rem codepage unless the file has a UTF-8 BOM, so a non-ASCII byte can stop the script parsing.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0diagnose.ps1" %*
echo.
pause
