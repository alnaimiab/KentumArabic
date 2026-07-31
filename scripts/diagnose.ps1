<#
.SYNOPSIS
    Reports why the Kentum Arabic translation is not showing up.

.DESCRIPTION
    Run this on a machine where the translation does not work and share the output.

    "The game runs but there is no Arabic" has several possible causes that look identical from
    the outside: the loader never ran, the loader ran but the plugin threw, antivirus removed a
    file, or the language is registered and simply was not selected. Each leaves a different trace,
    and this reads all of them in one pass so the answer comes from evidence rather than guesses.

    Read-only. It changes nothing.

.PARAMETER GameDir
    Kentum install folder. Auto-detected if not given.

.EXAMPLE
    .\diagnose.ps1
#>
[CmdletBinding()]
param([string]$GameDir)

$ErrorActionPreference = 'Continue'

function Head([string]$t) { Write-Host ""; Write-Host "== $t" -ForegroundColor Cyan }
function Line([string]$t) { Write-Host "   $t" }
function Good([string]$t) { Write-Host "   [ok]   $t" -ForegroundColor DarkGray }
function Bad([string]$t)  { Write-Host "   [FAIL] $t" -ForegroundColor Yellow }

function Find-KentumDir {
    $c = @()
    if ($env:KENTUM_DIR) { $c += $env:KENTUM_DIR }
    try {
        $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
        if ($steam) { $c += (Join-Path $steam 'steamapps\common\Kentum') }
    } catch {}
    foreach ($root in @('C:\Program Files (x86)\Steam', 'C:\Program Files\Steam')) {
        $c += (Join-Path $root 'steamapps\common\Kentum')
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $c += (Join-Path ($m.Groups[1].Value -replace '\\\\', '\') 'steamapps\common\Kentum')
            }
        }
    }
    foreach ($p in $c) { if ($p -and (Test-Path (Join-Path $p 'Kentum.exe'))) { return (Resolve-Path $p).Path } }
    return $null
}

function IsZoned([string]$p) {
    try { $null = Get-Content $p -Stream Zone.Identifier -ErrorAction Stop; return $true } catch { return $false }
}

Write-Host ""
Write-Host "  Kentum Arabic - Diagnostics" -ForegroundColor Green
Write-Host "  ===========================" -ForegroundColor Green

Head "Environment"
Line "Windows       : $([Environment]::OSVersion.Version)"
Line "PowerShell    : $($PSVersionTable.PSVersion)"
Line "ANSI codepage : $([Text.Encoding]::Default.CodePage)"

Head "Game"
if (-not $GameDir) { $GameDir = Find-KentumDir }
if (-not $GameDir) { Bad "Kentum not found. Re-run with -GameDir ""<folder with Kentum.exe>"""; exit 1 }
Good $GameDir

Head "Loader files"
$loaderOk = $true
foreach ($f in @('winhttp.dll', 'doorstop_config.ini', 'BepInEx\core\BepInEx.dll', 'BepInEx\core\BepInEx.Preloader.dll')) {
    $p = Join-Path $GameDir $f
    if (-not (Test-Path $p)) { Bad "$f is MISSING"; $loaderOk = $false }
    else {
        $len = (Get-Item $p).Length
        $zone = if (IsZoned $p) { '  <- still tagged as downloaded from the Internet' } else { '' }
        if ($len -eq 0) { Bad "$f is 0 bytes (antivirus?)"; $loaderOk = $false }
        else { Good "$f  ($len bytes)$zone" }
    }
}
if (-not $loaderOk) {
    Line ""
    Line "A missing or empty winhttp.dll is almost always antivirus. Allow the game folder,"
    Line "then run install.bat again."
}

Head "Translation files"
$plug = Join-Path $GameDir 'BepInEx\plugins\KentumArabic'
if (-not (Test-Path $plug)) { Bad "plugin folder missing: $plug" }
else {
    foreach ($f in @('KentumArabic.dll', 'manifest.json')) {
        $p = Join-Path $plug $f
        if (Test-Path $p) { Good "$f  ($((Get-Item $p).Length) bytes)" } else { Bad "$f MISSING" }
    }
    $tsv = @(Get-ChildItem (Join-Path $plug 'strings') -Filter *.tsv -ErrorAction SilentlyContinue)
    $ttf = @(Get-ChildItem (Join-Path $plug 'fonts') -Filter *.ttf -ErrorAction SilentlyContinue)
    if ($tsv.Count -ge 15) { Good "strings: $($tsv.Count) files" } else { Bad "strings: only $($tsv.Count) files" }
    if ($ttf.Count -ge 1)  { Good "fonts:   $($ttf.Count) files" } else { Bad "fonts:   none" }
}

Head "Config"
$cfg = Join-Path $GameDir 'BepInEx\config\com.kentum.arabic.cfg'
if (-not (Test-Path $cfg)) {
    Bad "com.kentum.arabic.cfg does not exist - the plugin has never run"
    Line "That means the game has not been started since installing, or the loader is not loading."
}
else {
    Good "com.kentum.arabic.cfg exists - the plugin has run at least once"
    foreach ($k in @('FontFile', 'Mode')) {
        $m = Select-String -Path $cfg -Pattern "^\s*$k\s*=" | Select-Object -First 1
        if ($m) { Line ($m.Line.Trim()) }
    }
}

Head "BepInEx log"
$log = Join-Path $GameDir 'BepInEx\LogOutput.log'
if (-not (Test-Path $log)) {
    Bad "LogOutput.log does not exist"
    Line "The loader never ran. Either winhttp.dll is missing or blocked, the game has not been"
    Line "started since installing, or something is stopping the DLL from loading."
}
else {
    $age = [int]((Get-Date) - (Get-Item $log).LastWriteTime).TotalMinutes
    Good "LogOutput.log present (last written $age minute(s) ago)"

    # -SimpleMatch takes the pattern literally, so it must not be regex-escaped.
    $loaded = Select-String -Path $log -Pattern 'Loading [Kentum Arabic' -SimpleMatch | Select-Object -First 1
    if ($loaded) { Good $loaded.Line.Trim() } else { Bad "BepInEx never loaded the plugin" }

    $counts = Select-String -Path $log -Pattern 'Translation loaded:' | Select-Object -First 1
    if ($counts) { Good $counts.Line.Trim() } else { Bad "no translation files were read" }

    $font = Select-String -Path $log -Pattern 'Arabic font built at runtime' | Select-Object -First 1
    if ($font) { Good $font.Line.Trim() } else { Bad "the Arabic font was never built - text would be empty boxes" }

    $reg = Select-String -Path $log -Pattern 'Arabic registered as language id' | Select-Object -First 1
    if ($reg) { Good $reg.Line.Trim() } else { Bad "Arabic was never registered - it will not appear in the language list" }

    # The plugin prints one line saying whether the install can work at all.
    $verdict = Select-String -Path $log -Pattern 'STARTUP OK|STARTUP INCOMPLETE' | Select-Object -Last 1
    if ($verdict) {
        if ($verdict.Line -match 'STARTUP OK') { Good $verdict.Line.Trim() } else { Bad $verdict.Line.Trim() }
    }

    # Where the game actually is, according to the game itself - the one authority on it.
    foreach ($k in @('Game path', 'Game build-guid', 'strings/', 'fonts/', 'Font requested')) {
        $m = Select-String -Path $log -Pattern ([regex]::Escape($k)) | Select-Object -First 1
        if ($m) { Line $m.Line.Trim() }
    }

    $errors = @(Select-String -Path $log -Pattern '\[Error|\[Fatal|Exception' | Select-Object -Last 15)
    if ($errors.Count -gt 0) {
        Line ""
        Line "Last errors in the log:"
        foreach ($e in $errors) { Write-Host "     $($e.Line.Trim())" -ForegroundColor Yellow }
    }
}

Head "Installer log"
$ilog = Join-Path $PSScriptRoot 'install-log.txt'
if (Test-Path $ilog) {
    Good "install-log.txt found next to this script - include it in your report"
    Line "   $ilog"
    Line ""
    Line "   Which folder the installer chose, and why:"
    foreach ($l in Select-String -Path $ilog -Pattern 'Kentum.exe found|no Kentum.exe|candidate location') {
        Line "     $($l.Line.Trim())"
    }
}
else {
    Line "No install-log.txt next to this script."
    Line "It is written by install.ps1; if you installed by unzipping by hand there will not be one."
}

Head "What to do"
Write-Host "   Share everything above when reporting the problem." -ForegroundColor Green
Write-Host "   The full log is at: $log" -ForegroundColor Green
Write-Host ""
