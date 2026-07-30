@echo off
chcp 65001 >nul
title Kentum Arabic - Uninstall
rem ASCII filename on purpose: cmd.exe resolves a batch file's own path through the system ANSI
rem codepage, which on a Western-locale Windows cannot represent Arabic.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
echo.
pause
