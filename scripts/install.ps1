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

# --- logging -----------------------------------------------------------------------------------
# Everything printed is also written to a file next to the script. When an install works on one
# machine and not another, the only way to close the gap without sitting at the second machine is
# to have it record what it saw - which folder it chose and why it chose that one above the
# others, what was already present, what it copied, what it verified.
$script:LogLines = New-Object System.Collections.ArrayList
$script:LogPath = Join-Path $PSScriptRoot 'install-log.txt'

function Note([string]$text) {
    # File-only: detail worth having in a report but noise on screen.
    [void]$script:LogLines.Add($text)
}
function Say([string]$text, [string]$colour = 'Gray') {
    Write-Host $text -ForegroundColor $colour; Note $text
}
function Step([string]$text) {
    Write-Host ""; Write-Host $text -ForegroundColor Cyan; Note ""; Note "== $text"
}
function Ok([string]$text) { Write-Host "  $text" -ForegroundColor DarkGray; Note "   $text" }
function Warn([string]$text) { Write-Host "  $text" -ForegroundColor Yellow; Note "   !! $text" }

function Save-Log {
    try {
        $header = @(
            "Kentum Arabic install log",
            "when          : $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss')) local",
            "script        : $PSCommandPath",
            "windows       : $([Environment]::OSVersion.Version)",
            "powershell    : $($PSVersionTable.PSVersion)",
            "ANSI codepage : $([Text.Encoding]::Default.CodePage)",
            "elevated      : $((New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))",
            "parameters    : GameDir='$GameDir' Font='$Font'",
            ""
        )
        Set-Content -Path $script:LogPath -Value ($header + $script:LogLines) -Encoding utf8
        Write-Host "  log written to: $script:LogPath" -ForegroundColor DarkGray
    }
    catch {
        Write-Host "  (could not write the log: $($_.Exception.Message))" -ForegroundColor DarkGray
    }
}

function Find-KentumDir {
    # Every candidate is recorded with where it came from, so a wrong choice - or no choice at
    # all - can be understood from the log alone rather than guessed at.
    $candidates = New-Object System.Collections.ArrayList
    function Add-Candidate([string]$path, [string]$source) {
        if ($path) { [void]$candidates.Add([pscustomobject]@{ Path = $path; Source = $source }) }
    }

    if ($env:KENTUM_DIR) { Add-Candidate $env:KENTUM_DIR 'KENTUM_DIR environment variable' }

    # Steam's own registry key first; the default path is only a guess.
    try {
        $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
        if ($steam) {
            Note "   Steam registry SteamPath = $steam"
            Add-Candidate (Join-Path $steam 'steamapps\common\Kentum') 'Steam registry key'
        }
    }
    catch { Note "   Steam registry key not readable: $($_.Exception.Message)" }

    $steamRoots = @('C:\Program Files (x86)\Steam', 'C:\Program Files\Steam')
    foreach ($root in $steamRoots) {
        Add-Candidate (Join-Path $root 'steamapps\common\Kentum') 'default Steam location'
    }

    # Games are often on a second drive, which Steam records here.
    foreach ($root in $steamRoots) {
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            Note "   reading Steam libraries from $vdf"
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $lib = $m.Groups[1].Value -replace '\\\\', '\'
                Add-Candidate (Join-Path $lib 'steamapps\common\Kentum') "Steam library at $lib"
            }
        }
        else { Note "   no libraryfolders.vdf at $vdf" }
    }

    Note "   $($candidates.Count) candidate location(s) considered:"
    $found = $null
    foreach ($c in $candidates) {
        $hasExe = Test-Path (Join-Path $c.Path 'Kentum.exe')
        Note "     [$(if ($hasExe) { 'Kentum.exe found' } else { 'no Kentum.exe   ' })] $($c.Path)   ($($c.Source))"
        if ($hasExe -and -not $found) { $found = (Resolve-Path $c.Path).Path }
    }
    return $found
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
    Save-Log
    exit 1
}
if (-not (Test-Path (Join-Path $GameDir 'Kentum.exe'))) {
    Warn "Kentum.exe is not in: $GameDir"
    Save-Log
    exit 1
}
Ok $GameDir
Note "   game folder contents before install:"
foreach ($e in Get-ChildItem $GameDir -Force -ErrorAction SilentlyContinue) {
    Note "     $(if ($e.PSIsContainer) { '[dir] ' } else { '      ' })$($e.Name)"
}

# --- payload -----------------------------------------------------------------------------------
$payload = Get-PayloadRoot
if ($payload) {
    Note "   payload kind : $($payload.Kind)"
    Note "   plugin dll   : $($payload.Dll)"
    Note "   strings from : $($payload.Strings)"
    Note "   fonts from   : $($payload.Fonts)"
}
if (-not $payload) {
    Warn "Could not find the translation files next to this script."
    Warn "Make sure the whole package was extracted before running it."
    Save-Log
    exit 1
}
if (-not (Test-Path $payload.Dll)) {
    Warn "Plugin assembly missing: $($payload.Dll)"
    if ($payload.Kind -eq 'repo') { Warn "Build it first: dotnet build src\KentumArabic -c Release" }
    Save-Log
    exit 1
}

# --- permissions -------------------------------------------------------------------------------
if (-not (Test-Writable $GameDir)) {
    if ($NoElevate) {
        Warn "No write access to the game folder, and running as administrator was declined."
        Save-Log
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
        Save-Log
        exit 1
    }

    $hash = (Get-FileHash $tmp -Algorithm SHA256).Hash
    if ($hash -ne $BepInExSha256) {
        Remove-Item $tmp -Force
        Warn "The downloaded file does not match the expected checksum - install aborted."
        Warn "  expected: $BepInExSha256"
        Warn "  actual  : $hash"
        Save-Log
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
    foreach ($f in $files) {
        $to = Join-Path $target $f.Name
        Copy-Item $f.FullName $to -Force
        Note "     $($f.Name)  ($((Get-Item $to).Length) bytes)"
    }
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

# --- mark of the web ---------------------------------------------------------------------------
# A zip downloaded in a browser is tagged with the Internet zone, and Explorer and Expand-Archive
# both propagate that tag to every extracted file. Windows can then refuse to load the tagged
# native DLL into the game process, which looks exactly like "the mod does nothing" - the game
# starts normally and no log is ever written. Clearing it costs nothing and removes the whole
# class of problem. Never seen when installing from a local build, which is why it only bites
# people who downloaded the release.
Step "Clearing the Internet zone tag..."
$unblocked = 0
foreach ($p in @(
    (Join-Path $GameDir 'winhttp.dll'),
    (Join-Path $GameDir 'doorstop_config.ini'),
    (Join-Path $GameDir '.doorstop_version'))) {
    if (Test-Path $p) {
        try { Unblock-File -Path $p -ErrorAction Stop; $unblocked++ } catch {}
    }
}
foreach ($dir in @((Join-Path $GameDir 'BepInEx'))) {
    if (Test-Path $dir) {
        foreach ($f in Get-ChildItem $dir -Recurse -File -ErrorAction SilentlyContinue) {
            try { Unblock-File -Path $f.FullName -ErrorAction Stop; $unblocked++ } catch {}
        }
    }
}
Ok "$unblocked file(s) cleared"

# --- verify ------------------------------------------------------------------------------------
# Copying files is not the same as having a working install, and the difference only shows up
# later as "the game runs but there is no Arabic". Check now, while the person is still here.
Step "Verifying..."
$problems = @()
$mustExist = @(
    @{ Path = (Join-Path $GameDir 'winhttp.dll'); What = 'BepInEx loader (winhttp.dll)' },
    @{ Path = (Join-Path $GameDir 'doorstop_config.ini'); What = 'doorstop_config.ini' },
    @{ Path = (Join-Path $GameDir 'BepInEx\core\BepInEx.dll'); What = 'BepInEx core' },
    @{ Path = (Join-Path $dest 'KentumArabic.dll'); What = 'the translation plugin' },
    @{ Path = (Join-Path $dest 'manifest.json'); What = 'manifest.json' }
)
foreach ($item in $mustExist) {
    if (-not (Test-Path $item.Path)) { $problems += "missing: $($item.What)" }
    elseif ((Get-Item $item.Path).Length -eq 0) { $problems += "empty (antivirus may have removed it): $($item.What)" }
}
$tsv = @(Get-ChildItem (Join-Path $dest 'strings') -Filter *.tsv -ErrorAction SilentlyContinue)
if ($tsv.Count -lt 15) { $problems += "only $($tsv.Count) translation file(s) copied" }
$ttf = @(Get-ChildItem (Join-Path $dest 'fonts') -Filter *.ttf -ErrorAction SilentlyContinue)
if ($ttf.Count -lt 1) { $problems += "no font files copied" }

if ($problems.Count -gt 0) {
    Say ""
    Warn "The install is incomplete:"
    foreach ($p in $problems) { Warn "  - $p" }
    Warn ""
    Warn "Antivirus removing winhttp.dll is the usual cause. Allow the game folder in your"
    Warn "antivirus and run this again. If that is not it, run diagnose.bat and share its output."
    Save-Log
    exit 1
}
Ok "all expected files present"

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
Note ""
Note "== final state"
foreach ($f in @('winhttp.dll', 'doorstop_config.ini', 'BepInEx\core\BepInEx.dll',
                 "BepInEx\plugins\$PluginFolder\KentumArabic.dll")) {
    $p = Join-Path $GameDir $f
    Note "   $(if (Test-Path $p) { '{0,10} bytes' -f (Get-Item $p).Length } else { '    MISSING' })  $f"
}

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
Say "  If the game shows no Arabic, run diagnose.bat and send its output" 'Green'
Say "  together with the log file named below." 'Green'
Save-Log
Say ""
