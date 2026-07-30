<#
.SYNOPSIS
    Installs the Kentum Arabic translation.

.DESCRIPTION
    Finds the game, installs BepInEx if it is missing, and copies the translation in.

    All output is plain ASCII on purpose. The audience is Arabic-speaking, but a Windows console
    is not guaranteed to render Arabic: the code page may not cover it, and the legacy console
    host may be using a raster font that has no Arabic glyphs at all. Anyone running install.ps1
    directly also skips the .bat wrapper's chcp. An installer that prints boxes is worse than one
    that prints English, so the scripts speak English and the documentation speaks Arabic.

    It records what it created in install-record.json next to the plugin. uninstall.ps1 reads
    that record, which is what lets it remove this mod without touching anything the player
    installed separately - the difference between a clean uninstall and deleting somebody's
    other mods along with ours.

.PARAMETER GameDir
    Kentum install folder. Auto-detected from the Steam library if not given.

.PARAMETER Font
    Arabic font to start with. Changeable in game with Ctrl+Alt+N.

.EXAMPLE
    .\install.ps1
    .\install.ps1 -Font NotoKufiArabic
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [ValidateSet('Vazirmatn', 'NotoKufiArabic', 'NotoSansArabic', 'IBMPlexSansArabic', 'NotoNaskhArabic')]
    [string]$Font = 'Vazirmatn',
    [switch]$NoElevate
)

$ErrorActionPreference = 'Stop'

$BepInExVersion = '5.4.23.2'
$BepInExUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
# Verified against the published release. A mod loader is exactly the kind of download worth
# checking, since it runs before the game does.
$BepInExSha256 = 'F752CE4E838F4C305B9DA1404B6745F2CFF23B8BFD494F79F0C84D0A01F59B46'

$PluginFolder = 'KentumArabic'
$RecordName = 'install-record.json'

function Say([string]$text, [string]$colour = 'Gray') { Write-Host $text -ForegroundColor $colour }
function Step([string]$text) { Write-Host ""; Write-Host $text -ForegroundColor Cyan }
function Ok([string]$text) { Write-Host "  $text" -ForegroundColor DarkGray }
function Warn([string]$text) { Write-Host "  $text" -ForegroundColor Yellow }

function Find-KentumDir {
    $candidates = @()
    if ($env:KENTUM_DIR) { $candidates += $env:KENTUM_DIR }

    # Steam's own registry key first; the default path is only a guess.
    try {
        $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
        if ($steam) { $candidates += (Join-Path $steam 'steamapps\common\Kentum') }
    }
    catch {}

    $steamRoots = @('C:\Program Files (x86)\Steam', 'C:\Program Files\Steam')
    foreach ($root in $steamRoots) { $candidates += (Join-Path $root 'steamapps\common\Kentum') }

    # Games are often on a second drive, which Steam records here.
    foreach ($root in $steamRoots) {
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $lib = $m.Groups[1].Value -replace '\\\\', '\'
                $candidates += (Join-Path $lib 'steamapps\common\Kentum')
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
    try {
        [IO.File]::WriteAllText($probe, 'x')
        Remove-Item $probe -Force
        return $true
    }
    catch { return $false }
}

function Get-PayloadRoot {
    # Works both from a checkout and from an extracted release, so there is one script to
    # document rather than two that drift apart.
    # $PSScriptRoot, not $MyInvocation: inside a function the latter describes the function,
    # and its Path is null.
    $here = $PSScriptRoot
    $repo = Split-Path -Parent $here

    $fromRepo = Join-Path $repo 'content'
    if (Test-Path (Join-Path $fromRepo 'strings')) {
        return [pscustomobject]@{
            Strings  = Join-Path $fromRepo 'strings'
            Fonts    = Join-Path $fromRepo 'fonts'
            Manifest = Join-Path $fromRepo 'manifest.json'
            Dll      = Join-Path $repo 'src\KentumArabic\bin\Release\KentumArabic.dll'
            Kind     = 'repo'
        }
    }

    foreach ($base in @($here, $repo)) {
        $p = Join-Path $base "BepInEx\plugins\$PluginFolder"
        if (Test-Path (Join-Path $p 'strings')) {
            return [pscustomobject]@{
                Strings  = Join-Path $p 'strings'
                Fonts    = Join-Path $p 'fonts'
                Manifest = Join-Path $p 'manifest.json'
                Dll      = Join-Path $p 'KentumArabic.dll'
                Kind     = 'release'
            }
        }
    }
    return $null
}

# --- elevation ---------------------------------------------------------------------------------
# The game normally lives under Program Files, which a standard user cannot write to. Better to
# ask for elevation once, up front, than to fail halfway through a copy.
function Invoke-Elevated {
    $relaunch = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"",
                  '-NoElevate', '-Font', $Font)
    if ($GameDir) { $relaunch += @('-GameDir', "`"$GameDir`"") }
    Start-Process powershell -Verb RunAs -ArgumentList $relaunch -Wait
}

Say ""
Say "  Kentum Arabic - Install" 'Green'
Say "  =======================" 'Green'

# --- locate the game ---------------------------------------------------------------------------
Step "Looking for the game..."
if (-not $GameDir) { $GameDir = Find-KentumDir }
if (-not $GameDir) {
    Say ""
    Warn "Could not find Kentum automatically."
    Warn "Run the script with the folder that contains Kentum.exe, for example:"
    Warn "  .\install.ps1 -GameDir ""D:\SteamLibrary\steamapps\common\Kentum"""
    exit 1
}
if (-not (Test-Path (Join-Path $GameDir 'Kentum.exe'))) {
    Warn "Kentum.exe is not in: $GameDir"
    exit 1
}
Ok $GameDir

# --- payload -----------------------------------------------------------------------------------
$payload = Get-PayloadRoot
if (-not $payload) {
    Warn "Could not find the translation files next to this script."
    Warn "Make sure the whole package was extracted before running it."
    exit 1
}
if (-not (Test-Path $payload.Dll)) {
    Warn "Plugin assembly missing: $($payload.Dll)"
    if ($payload.Kind -eq 'repo') { Warn "Build it first: dotnet build src\KentumArabic -c Release" }
    exit 1
}

# --- permissions -------------------------------------------------------------------------------
if (-not (Test-Writable $GameDir)) {
    if ($NoElevate) {
        Warn "No write access to the game folder, and running as administrator was declined."
        exit 1
    }
    Step "The game folder needs administrator rights - you will be asked to confirm."
    Invoke-Elevated
    exit $LASTEXITCODE
}

# --- BepInEx -----------------------------------------------------------------------------------
$bepinexWasInstalledByUs = $false
Step "Checking BepInEx..."
if (Test-Path (Join-Path $GameDir 'BepInEx\core\BepInEx.dll')) {
    Ok "already installed - left untouched"
}
elseif (Test-Path (Join-Path $PSScriptRoot 'BepInEx\core\BepInEx.dll')) {
    # The Full package already carries the loader, so downloading it again would be a pointless
    # round trip that also makes the installer fail on a machine with no internet.
    Ok "bundled with this package - copying, no download needed"
    foreach ($item in @('BepInEx', 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version')) {
        $src = Join-Path $PSScriptRoot $item
        if (Test-Path $src) { Copy-Item $src $GameDir -Recurse -Force }
    }
    $bepinexWasInstalledByUs = $true
    Ok "copied into the game folder"
}
else {
    Ok "not present - downloading version $BepInExVersion"
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "BepInEx_$BepInExVersion.zip"
    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $tmp -UseBasicParsing
    }
    catch {
        Warn "Download failed. Check your internet connection."
        Warn $_.Exception.Message
        exit 1
    }

    $hash = (Get-FileHash $tmp -Algorithm SHA256).Hash
    if ($hash -ne $BepInExSha256) {
        Remove-Item $tmp -Force
        Warn "The downloaded file does not match the expected checksum - install aborted."
        Warn "  expected: $BepInExSha256"
        Warn "  actual  : $hash"
        exit 1
    }
    Ok "checksum verified"

    Expand-Archive -Path $tmp -DestinationPath $GameDir -Force
    Remove-Item $tmp -Force
    $bepinexWasInstalledByUs = $true
    Ok "installed into the game folder"
}

# --- plugin ------------------------------------------------------------------------------------
Step "Copying the translation..."
$dest = Join-Path $GameDir "BepInEx\plugins\$PluginFolder"
New-Item -ItemType Directory -Path $dest -Force | Out-Null

Copy-Item $payload.Dll (Join-Path $dest 'KentumArabic.dll') -Force
Ok "KentumArabic.dll"

foreach ($sub in @('strings', 'fonts')) {
    $src = $payload.$(if ($sub -eq 'strings') { 'Strings' } else { 'Fonts' })
    $target = Join-Path $dest $sub
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    # Only the file types that belong. This is also what keeps steam_appid.txt, _dump/ and any
    # other local development leftovers out of a player's install.
    $filter = if ($sub -eq 'strings') { '*.tsv' } else { '*.ttf' }
    $files = @(Get-ChildItem $src -File | Where-Object { $_.Name -like $filter -or $_.Extension -eq '.txt' })
    foreach ($f in $files) { Copy-Item $f.FullName (Join-Path $target $f.Name) -Force }
    Ok "$sub\  ($($files.Count) files)"
}

if (Test-Path $payload.Manifest) {
    Copy-Item $payload.Manifest (Join-Path $dest 'manifest.json') -Force
    Ok "manifest.json"
}

# --- font choice -------------------------------------------------------------------------------
$cfgDir = Join-Path $GameDir 'BepInEx\config'
$cfg = Join-Path $cfgDir 'com.kentum.arabic.cfg'
$fontFile = "fonts/$Font-Regular.ttf"

if (Test-Path $cfg) {
    $text = Get-Content $cfg -Raw
    if ($text -match '(?m)^\s*FontFile\s*=') {
        $text = [regex]::Replace($text, '(?m)^\s*FontFile\s*=.*$', "FontFile = $fontFile")
        Set-Content $cfg $text -Encoding utf8 -NoNewline
        Ok "font set to $Font"
    }
}
elseif ($Font -ne 'Vazirmatn') {
    # On a first install the config does not exist yet - BepInEx writes it when the game first
    # runs - so there is nothing to edit and -Font would silently do nothing. Seeding a minimal
    # file works because BepInEx merges its defaults into whatever it finds rather than replacing
    # it. Only written when the choice differs from the built-in default, so a plain install
    # leaves the config entirely to BepInEx.
    New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null
    Set-Content $cfg "[Font]`r`nFontFile = $fontFile`r`n" -Encoding utf8
    Ok "font set to $Font"
}

# --- record ------------------------------------------------------------------------------------
# uninstall.ps1 depends on this to know what it may remove.
$record = [ordered]@{
    installedAtUtc          = (Get-Date).ToUniversalTime().ToString('o')
    gameDir                 = $GameDir
    pluginFolder            = $PluginFolder
    bepinexInstalledByUs    = $bepinexWasInstalledByUs
    bepinexVersion          = $BepInExVersion
    font                    = $Font
}
$record | ConvertTo-Json | Set-Content (Join-Path $dest $RecordName) -Encoding utf8

# --- done --------------------------------------------------------------------------------------
Say ""
Say "  Installed successfully." 'Green'
Say ""
Say "  Start the game from Steam, then:  Options > Language > Arabic" 'Green'
Say "  (it is the last entry in the language list)" 'Green'
Say ""
Ok "Ctrl+Alt+N   cycle the bundled Arabic fonts while playing"
Ok "Ctrl+Alt+R   reload the translation without restarting the game"
Say ""
if ($bepinexWasInstalledByUs) {
    Ok "BepInEx was installed as part of this, and uninstall.ps1 will remove it."
}
else {
    Ok "BepInEx was already present, so uninstall.ps1 will leave it alone."
}
Say ""
