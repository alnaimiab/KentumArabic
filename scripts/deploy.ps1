<#
.SYNOPSIS
    Builds the plugin and copies it, the translation files and the font bundle into the game.

.DESCRIPTION
    The project lives outside the game folder on purpose: the game directory sits under
    Program Files, gets rewritten by Steam updates, and cannot be version controlled. This script
    is the bridge between the two during development.

.PARAMETER GameDir
    Kentum install root. Defaults to $env:KENTUM_DIR, then to the usual Steam location.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER SkipBuild
    Copy content only. Useful when iterating on translations rather than code.

.EXAMPLE
    .\scripts\deploy.ps1
    .\scripts\deploy.ps1 -SkipBuild        # just push new strings
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (-not $GameDir) { $GameDir = $env:KENTUM_DIR }
if (-not $GameDir) { $GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Kentum' }

if (-not (Test-Path (Join-Path $GameDir 'Kentum.exe'))) {
    throw "Kentum.exe not found in '$GameDir'. Pass -GameDir or set the KENTUM_DIR environment variable."
}

$bepinex = Join-Path $GameDir 'BepInEx'
if (-not (Test-Path (Join-Path $bepinex 'core\BepInEx.dll'))) {
    throw "BepInEx is not installed in '$GameDir'. Run scripts\setup-dev.ps1 first."
}

$pluginDir = Join-Path $bepinex 'plugins\KentumArabic'
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

# --- build ------------------------------------------------------------------------------------
if (-not $SkipBuild) {
    Write-Host "Building ($Configuration)..." -ForegroundColor Cyan
    $env:KENTUM_DIR = $GameDir
    $proj = Join-Path $repo 'src\KentumArabic\KentumArabic.csproj'
    & dotnet build $proj -c $Configuration --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

    $dll = Join-Path $repo "src\KentumArabic\bin\$Configuration\KentumArabic.dll"
    if (-not (Test-Path $dll)) { throw "Build output not found at '$dll'." }
    Copy-Item $dll $pluginDir -Force
    $pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
    if (Test-Path $pdb) { Copy-Item $pdb $pluginDir -Force }
    Write-Host "  -> KentumArabic.dll" -ForegroundColor DarkGray
}

# --- translation content ------------------------------------------------------------------------
$stringsSrc = Join-Path $repo 'content\strings'
$stringsDst = Join-Path $pluginDir 'strings'
if (Test-Path $stringsSrc) {
    # Mirror rather than merge, so a file deleted in the repo also disappears from the game.
    if (Test-Path $stringsDst) { Remove-Item $stringsDst -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stringsDst | Out-Null
    $tsv = Get-ChildItem $stringsSrc -Filter *.tsv -Recurse -ErrorAction SilentlyContinue
    foreach ($f in $tsv) { Copy-Item $f.FullName $stringsDst -Force }
    Write-Host "  -> strings\ ($($tsv.Count) file(s))" -ForegroundColor DarkGray
}

$manifest = Join-Path $repo 'content\manifest.json'
if (Test-Path $manifest) {
    Copy-Item $manifest $pluginDir -Force
    Write-Host "  -> manifest.json" -ForegroundColor DarkGray
}

# --- fonts ---------------------------------------------------------------------------------------
# Plain .ttf files. TextMeshPro builds the font asset at runtime with a dynamic atlas, so there
# is no AssetBundle to bake and nothing tied to a specific Unity version.
$fontsSrc = Join-Path $repo 'content\fonts'
$fontsDst = Join-Path $pluginDir 'fonts'
if (Test-Path $fontsSrc) {
    New-Item -ItemType Directory -Force -Path $fontsDst | Out-Null
    $fonts = Get-ChildItem $fontsSrc -Include *.ttf, *.otf, *.txt -Recurse -ErrorAction SilentlyContinue
    foreach ($f in $fonts) { Copy-Item $f.FullName $fontsDst -Force }
    Write-Host "  -> fonts\ ($($fonts.Count) file(s))" -ForegroundColor DarkGray
}
else {
    Write-Host "  !  content\fonts missing - Arabic will render as empty boxes." -ForegroundColor Yellow
}

# Optional pre-built bundle, for anyone who prefers a hand-tuned static atlas.
$bundle = Join-Path $repo 'content\arabicfont'
if (Test-Path $bundle) {
    Copy-Item $bundle $pluginDir -Force
    $size = [math]::Round((Get-Item $bundle).Length / 1MB, 1)
    Write-Host "  -> arabicfont ($size MB, optional override)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Deployed to $pluginDir" -ForegroundColor Green
Write-Host "Launch Kentum, then Options > Language > العربية." -ForegroundColor Green
Write-Host "Log: $(Join-Path $bepinex 'LogOutput.log')" -ForegroundColor DarkGray
