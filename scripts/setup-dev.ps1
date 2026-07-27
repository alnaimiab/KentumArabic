<#
.SYNOPSIS
    One-off developer setup: install BepInEx into the game and remember where the game is.

.DESCRIPTION
    Downloads BepInEx 5.4.23.2 (x64, Mono) and extracts it into the Kentum install folder, then
    persists KENTUM_DIR so the build can resolve the game's assemblies. Safe to re-run.

.PARAMETER GameDir
    Kentum install root. Auto-detected from the Steam library if not given.

.EXAMPLE
    .\scripts\setup-dev.ps1
#>
[CmdletBinding()]
param(
    [string]$GameDir
)

$ErrorActionPreference = 'Stop'

$BepInExVersion = '5.4.23.2'
$BepInExUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
# Verified against the published release; guards against a corrupted or substituted download.
$BepInExSha256 = 'F752CE4E838F4C305B9DA1404B6745F2CFF23B8BFD494F79F0C84D0A01F59B46'

function Find-KentumDir {
    $candidates = @()
    if ($env:KENTUM_DIR) { $candidates += $env:KENTUM_DIR }
    $candidates += 'C:\Program Files (x86)\Steam\steamapps\common\Kentum'

    # Follow Steam's extra library folders, if configured.
    $vdf = 'C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
            $candidates += (Join-Path ($m.Groups[1].Value -replace '\\\\', '\') 'steamapps\common\Kentum')
        }
    }

    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'Kentum.exe'))) { return $c }
    }
    return $null
}

if (-not $GameDir) { $GameDir = Find-KentumDir }
if (-not $GameDir) {
    throw "Could not find Kentum. Pass -GameDir with the folder containing Kentum.exe."
}
if (-not (Test-Path (Join-Path $GameDir 'Kentum.exe'))) {
    throw "Kentum.exe not found in '$GameDir'."
}

Write-Host "Game directory: $GameDir" -ForegroundColor Cyan

# --- BepInEx -----------------------------------------------------------------------------------
if (Test-Path (Join-Path $GameDir 'BepInEx\core\BepInEx.dll')) {
    Write-Host "BepInEx already installed - skipping." -ForegroundColor DarkGray
}
else {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "BepInEx_$BepInExVersion.zip"
    Write-Host "Downloading BepInEx $BepInExVersion..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $BepInExUrl -OutFile $tmp -UseBasicParsing

    $hash = (Get-FileHash $tmp -Algorithm SHA256).Hash
    if ($hash -ne $BepInExSha256) {
        Remove-Item $tmp -Force
        throw "BepInEx download failed verification.`n  expected $BepInExSha256`n  got      $hash"
    }
    Write-Host "  checksum verified" -ForegroundColor DarkGray

    Expand-Archive -Path $tmp -DestinationPath $GameDir -Force
    Remove-Item $tmp -Force
    Write-Host "  installed into $GameDir" -ForegroundColor DarkGray
}

# --- environment -------------------------------------------------------------------------------
# The build reads this instead of hardcoding a path, so the project works on any machine.
if ($env:KENTUM_DIR -ne $GameDir) {
    [Environment]::SetEnvironmentVariable('KENTUM_DIR', $GameDir, 'User')
    $env:KENTUM_DIR = $GameDir
    Write-Host "KENTUM_DIR set (user scope)." -ForegroundColor DarkGray
}

# --- checks ------------------------------------------------------------------------------------
Write-Host ""
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) { Write-Host "dotnet SDK : $(& dotnet --version)" -ForegroundColor DarkGray }
else { Write-Host "dotnet SDK : MISSING - install the .NET SDK to build the plugin" -ForegroundColor Yellow }

$python = Get-Command python -ErrorAction SilentlyContinue
if ($python) { Write-Host "python     : $(& python --version)" -ForegroundColor DarkGray }
else { Write-Host "python     : missing (only needed for tools/)" -ForegroundColor Yellow }

Write-Host ""
Write-Host "Setup complete. Next:" -ForegroundColor Green
Write-Host "  .\scripts\deploy.ps1        build and install the plugin" -ForegroundColor Green
Write-Host "  then launch Kentum and choose العربية in Options > Language" -ForegroundColor Green
