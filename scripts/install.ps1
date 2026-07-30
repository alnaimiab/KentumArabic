<#
.SYNOPSIS
    تثبيت تعريب Kentum. Installs the Kentum Arabic translation.

.DESCRIPTION
    يعثر على مجلد اللعبة، ويثبّت BepInEx إن لم يكن موجودًا، وينسخ ملفات التعريب.

    Finds the game, installs BepInEx if it is missing, and copies the translation in.

    It records what it created in install-record.json next to the plugin. uninstall.ps1 reads
    that record, which is what lets it remove this mod without touching anything the player
    installed separately - the difference between a clean uninstall and deleting somebody's
    other mods along with ours.

.PARAMETER GameDir
    مجلد اللعبة. يُكتشف تلقائيًا من مكتبة Steam إن لم يُذكر.

.PARAMETER Font
    الخط المبدئي. يمكن تغييره لاحقًا بـ Ctrl+Alt+N داخل اللعبة.

.EXAMPLE
    .\install.ps1
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [ValidateSet('Vazirmatn', 'NotoKufiArabic', 'NotoSansArabic', 'IBMPlexSansArabic', 'NotoNaskhArabic')]
    [string]$Font = 'Vazirmatn',
    [switch]$NoElevate
)

$ErrorActionPreference = 'Stop'

# Arabic comes out as question marks in a legacy console otherwise.
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch {}

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
Say "  تعريب Kentum — التثبيت" 'Green'
Say "  ======================" 'Green'

# --- locate the game ---------------------------------------------------------------------------
Step "البحث عن اللعبة..."
if (-not $GameDir) { $GameDir = Find-KentumDir }
if (-not $GameDir) {
    Say ""
    Warn "لم أجد Kentum تلقائيًا."
    Warn "شغّل السكربت مع مسار المجلد الذي يحوي Kentum.exe، مثلًا:"
    Warn "  .\install.ps1 -GameDir ""D:\SteamLibrary\steamapps\common\Kentum"""
    exit 1
}
if (-not (Test-Path (Join-Path $GameDir 'Kentum.exe'))) {
    Warn "لا يوجد Kentum.exe في: $GameDir"
    exit 1
}
Ok $GameDir

# --- payload -----------------------------------------------------------------------------------
$payload = Get-PayloadRoot
if (-not $payload) {
    Warn "لم أجد ملفات التعريب بجوار هذا السكربت."
    Warn "تأكد أنك فككت ضغط الحزمة كاملة قبل التشغيل."
    exit 1
}
if (-not (Test-Path $payload.Dll)) {
    Warn "ملف الإضافة غير موجود: $($payload.Dll)"
    if ($payload.Kind -eq 'repo') { Warn "ابنِ المشروع أولًا: dotnet build src\KentumArabic -c Release" }
    exit 1
}

# --- permissions -------------------------------------------------------------------------------
if (-not (Test-Writable $GameDir)) {
    if ($NoElevate) {
        Warn "لا صلاحية للكتابة في مجلد اللعبة، وتشغيل السكربت كمسؤول فشل أو رُفض."
        exit 1
    }
    Step "مجلد اللعبة يحتاج صلاحية مسؤول — سيُطلب منك التأكيد."
    Invoke-Elevated
    exit $LASTEXITCODE
}

# --- BepInEx -----------------------------------------------------------------------------------
$bepinexWasInstalledByUs = $false
Step "التحقق من BepInEx..."
if (Test-Path (Join-Path $GameDir 'BepInEx\core\BepInEx.dll')) {
    Ok "مثبت مسبقًا — لن يُلمس."
}
else {
    Ok "غير موجود، سيُنزَّل الإصدار $BepInExVersion"
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "BepInEx_$BepInExVersion.zip"
    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $tmp -UseBasicParsing
    }
    catch {
        Warn "فشل التنزيل. تحقق من الاتصال بالإنترنت."
        Warn $_.Exception.Message
        exit 1
    }

    $hash = (Get-FileHash $tmp -Algorithm SHA256).Hash
    if ($hash -ne $BepInExSha256) {
        Remove-Item $tmp -Force
        Warn "الملف المُنزَّل لا يطابق البصمة المتوقعة — أُلغي التثبيت."
        Warn "  المتوقع: $BepInExSha256"
        Warn "  الفعلي : $hash"
        exit 1
    }
    Ok "البصمة مطابقة"

    Expand-Archive -Path $tmp -DestinationPath $GameDir -Force
    Remove-Item $tmp -Force
    $bepinexWasInstalledByUs = $true
    Ok "ثُبّت في مجلد اللعبة"
}

# --- plugin ------------------------------------------------------------------------------------
Step "نسخ ملفات التعريب..."
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
    Ok "$sub\  ($($files.Count) ملف)"
}

if (Test-Path $payload.Manifest) {
    Copy-Item $payload.Manifest (Join-Path $dest 'manifest.json') -Force
    Ok "manifest.json"
}

# --- font choice -------------------------------------------------------------------------------
# Written straight into the config so the requested font is live on first launch rather than
# after a restart.
$cfgDir = Join-Path $GameDir 'BepInEx\config'
$cfg = Join-Path $cfgDir 'com.kentum.arabic.cfg'
$fontFile = "fonts/$Font-Regular.ttf"
if (Test-Path $cfg) {
    $text = Get-Content $cfg -Raw
    if ($text -match '(?m)^\s*FontFile\s*=') {
        $text = [regex]::Replace($text, '(?m)^\s*FontFile\s*=.*$', "FontFile = $fontFile")
        Set-Content $cfg $text -Encoding utf8 -NoNewline
        Ok "الخط في الإعدادات: $Font"
    }
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
Say "  تم التثبيت بنجاح." 'Green'
Say ""
Say "  شغّل اللعبة من Steam، ثم:  Options > Language > العربية" 'Green'
Say ""
Ok "Ctrl+Alt+N  لتجريب الخطوط المرفقة أثناء اللعب"
Ok "Ctrl+Alt+R  لإعادة تحميل الترجمة دون إغلاق اللعبة"
Say ""
if ($bepinexWasInstalledByUs) {
    Ok "ثُبّت BepInEx كجزء من هذه العملية، وسيُزال مع uninstall.ps1."
}
else {
    Ok "كان BepInEx موجودًا مسبقًا، ولن يزيله uninstall.ps1."
}
Say ""
