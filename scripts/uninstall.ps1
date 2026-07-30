<#
.SYNOPSIS
    Removes the Kentum Arabic translation.

.DESCRIPTION
    Removes the translation, and removes BepInEx only when this installer put it there and no
    other mod still needs it.

    The rule that matters: this removes what this mod put there, and nothing else. BepInEx is a
    shared loader - other mods live in the same folder - so deleting it wholesale would take
    someone else's mods with it. It is only removed when install-record.json says we installed it
    AND no other plugin remains. Anything ambiguous is left in place and reported.

    Output is plain ASCII for the same reason as install.ps1: a Windows console is not guaranteed
    to be able to render Arabic, and an uninstaller that prints boxes while deleting files is
    alarming rather than helpful.

.PARAMETER GameDir
    Kentum install folder. Auto-detected if not given.

.PARAMETER KeepBepInEx
    Keep BepInEx even if this installer put it there.

.PARAMETER WhatIf
    Show what would be removed without removing anything.

.EXAMPLE
    .\uninstall.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$GameDir,
    [switch]$KeepBepInEx,
    [switch]$NoElevate
)

$ErrorActionPreference = 'Stop'

$PluginFolder = 'KentumArabic'
$RecordName = 'install-record.json'

# What a BepInEx install of ours leaves in the game root.
$LoaderItems = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version', 'changelog.txt')

function Say([string]$t, [string]$c = 'Gray') { Write-Host $t -ForegroundColor $c }
function Step([string]$t) { Write-Host ""; Write-Host $t -ForegroundColor Cyan }
function Ok([string]$t) { Write-Host "  $t" -ForegroundColor DarkGray }
function Warn([string]$t) { Write-Host "  $t" -ForegroundColor Yellow }

function Find-KentumDir {
    $candidates = @()
    if ($env:KENTUM_DIR) { $candidates += $env:KENTUM_DIR }
    try {
        $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
        if ($steam) { $candidates += (Join-Path $steam 'steamapps\common\Kentum') }
    }
    catch {}
    $steamRoots = @('C:\Program Files (x86)\Steam', 'C:\Program Files\Steam')
    foreach ($root in $steamRoots) {
        $candidates += (Join-Path $root 'steamapps\common\Kentum')
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $candidates += (Join-Path ($m.Groups[1].Value -replace '\\\\', '\') 'steamapps\common\Kentum')
            }
        }
    }
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'Kentum.exe'))) { return (Resolve-Path $c).Path }
    }
    return $null
}

function Test-Writable([string]$dir) {
    $probe = Join-Path $dir ".kentumarabic-write-test"
    try { [IO.File]::WriteAllText($probe, 'x'); Remove-Item $probe -Force; return $true }
    catch { return $false }
}

# Uses the *script's* $PSCmdlet, which is why -WhatIf works without this helper declaring
# SupportsShouldProcess itself. Adding the attribute here would open a second, separate
# ShouldProcess context and break that, so the analyser warning about it is expected.
function Remove-Thing([string]$path, [string]$label) {
    if (-not (Test-Path $path)) { return $false }
    if ($PSCmdlet.ShouldProcess($path, 'Remove')) {
        Remove-Item $path -Recurse -Force
        Ok "removed: $label"
    }
    else {
        Ok "would remove: $label"
    }
    return $true
}

Say ""
Say "  Kentum Arabic - Uninstall" 'Green'
Say "  =========================" 'Green'

Step "Looking for the game..."
if (-not $GameDir) { $GameDir = Find-KentumDir }
if (-not $GameDir) {
    Warn "Could not find Kentum. Pass the folder with -GameDir."
    exit 1
}
Ok $GameDir

$pluginDir = Join-Path $GameDir "BepInEx\plugins\$PluginFolder"
$configFile = Join-Path $GameDir 'BepInEx\config\com.kentum.arabic.cfg'

if (-not (Test-Path $pluginDir) -and -not (Test-Path $configFile)) {
    Say ""
    Ok "The translation is not installed here. Nothing to remove."
    exit 0
}

if (-not $WhatIfPreference -and -not (Test-Writable $GameDir)) {
    if ($NoElevate) { Warn "No write access to the game folder."; exit 1 }
    Step "The game folder needs administrator rights - you will be asked to confirm."
    $a = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($MyInvocation.MyCommand.Path)`"",
           '-NoElevate', '-GameDir', "`"$GameDir`"")
    if ($KeepBepInEx) { $a += '-KeepBepInEx' }
    Start-Process powershell -Verb RunAs -ArgumentList $a -Wait
    exit $LASTEXITCODE
}

# --- what did we install? ----------------------------------------------------------------------
$record = $null
$recordPath = Join-Path $pluginDir $RecordName
if (Test-Path $recordPath) {
    try { $record = Get-Content $recordPath -Raw | ConvertFrom-Json } catch { $record = $null }
}

# --- our own files -------------------------------------------------------------------------------
Step "Removing the translation..."
Remove-Thing $pluginDir "BepInEx\plugins\$PluginFolder\" | Out-Null
Remove-Thing $configFile "BepInEx\config\com.kentum.arabic.cfg" | Out-Null

# --- the shared loader ---------------------------------------------------------------------------
Step "Checking BepInEx..."

$pluginsDir = Join-Path $GameDir 'BepInEx\plugins'
$othersRemain = $false
if (Test-Path $pluginsDir) {
    # A leftover empty folder is not another mod; anything with content in it is.
    $leftovers = @(Get-ChildItem $pluginsDir -Force | Where-Object {
        $_.Name -ne $PluginFolder -and
        ($_.PSIsContainer -eq $false -or @(Get-ChildItem $_.FullName -Force -Recurse -File).Count -gt 0)
    })
    if ($leftovers.Count -gt 0) {
        $othersRemain = $true
        Warn "Other mods are present in BepInEx\plugins - BepInEx will be kept:"
        foreach ($l in $leftovers) { Warn "    $($l.Name)" }
    }
}

$weInstalledIt = $false
if ($record) { $weInstalledIt = [bool]$record.bepinexInstalledByUs }

if ($KeepBepInEx) {
    Ok "BepInEx kept, as requested (-KeepBepInEx)."
}
elseif ($othersRemain) {
    Ok "BepInEx kept because other mods depend on it."
}
elseif (-not $record) {
    # No record means a manual unzip, or an install by an older version. We cannot tell whether
    # BepInEx predates us, and guessing wrong deletes something the player wanted.
    Warn "No install record found, so it is unknown whether this mod installed BepInEx - it was left in place."
    Warn "To remove it by hand, delete from the game folder: BepInEx\, winhttp.dll, doorstop_config.ini"
}
elseif (-not $weInstalledIt) {
    Ok "BepInEx was already there before this mod - left in place."
}
else {
    Remove-Thing (Join-Path $GameDir 'BepInEx') 'BepInEx\' | Out-Null
    foreach ($item in $LoaderItems) {
        Remove-Thing (Join-Path $GameDir $item) $item | Out-Null
    }
}

# --- done ----------------------------------------------------------------------------------------
Say ""
if ($WhatIfPreference) {
    Say "  Preview only - nothing was removed. Run again without -WhatIf to apply." 'Yellow'
}
else {
    Say "  Uninstalled." 'Green'
    Say ""
    Ok "Save games are untouched; this mod never writes to them."
    Ok "To double-check the game files: Steam > game properties > Verify integrity of game files"
}
Say ""
