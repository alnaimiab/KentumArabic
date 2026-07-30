@echo off
chcp 65001 >nul
title Kentum Arabic - Install
rem ASCII filename on purpose. cmd.exe resolves a batch file's own path through the system ANSI
rem codepage, which on a Western-locale Windows cannot represent Arabic - so an Arabic filename
rem risks "is not recognized as an internal or external command" on exactly the machines we
rem cannot test. The script speaks Arabic; its filename does not have to.
rem
rem PowerShell scripts do not run on double-click: the default execution policy blocks them and
rem Windows opens them in Notepad instead. This wrapper is what makes the installer a
rem double-click for someone who has never opened a terminal.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" %*
echo.
pause
