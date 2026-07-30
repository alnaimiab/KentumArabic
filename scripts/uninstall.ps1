<#
.SYNOPSIS
    إزالة تعريب Kentum. Removes the Kentum Arabic translation.

.DESCRIPTION
    يزيل ملفات التعريب، ويزيل BepInEx فقط إن كان سكربت التثبيت هو من ثبّته ولم يبق أي تعديل آخر.

    The rule that matters: this removes what this mod put there, and nothing else. BepInEx is a
    shared loader - other mods live in the same folder - so deleting it wholesale would take
    someone else's mods with it. It is only removed when install-record.json says we installed it
    AND no other plugin remains. Anything ambiguous is left in place and reported.

.PARAMETER GameDir
    مجلد اللعبة. يُكتشف تلقائيًا إن لم يُذكر.

.PARAMETER KeepBepInEx
    أبقِ BepInEx حتى لو كنا من ثبّتناه.

.PARAMETER WhatIf
    اعرض ما سيُحذف دون حذف شيء.

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
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch {}

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

function Remove-Thing([string]$path, [string]$label) {
    if (-not (Test-Path $path)) { return $false }
    if ($PSCmdlet.ShouldProcess($path, 'Remove')) {
        Remove-Item $path -Recurse -Force
        Ok "حُذف: $label"
    }
    else {
        Ok "سيُحذف: $label"
    }
    return $true
}

Say ""
Say "  تعريب Kentum — الإزالة" 'Green'
Say "  =====================" 'Green'

Step "البحث عن اللعبة..."
if (-not $GameDir) { $GameDir = Find-KentumDir }
if (-not $GameDir) {
    Warn "لم أجد Kentum. مرّر المسار بـ -GameDir."
    exit 1
}
Ok $GameDir

$pluginDir = Join-Path $GameDir "BepInEx\plugins\$PluginFolder"
$configFile = Join-Path $GameDir 'BepInEx\config\com.kentum.arabic.cfg'

if (-not (Test-Path $pluginDir) -and -not (Test-Path $configFile)) {
    Say ""
    Ok "التعريب غير مثبت في هذا المجلد. لا شيء لإزالته."
    exit 0
}

if (-not $WhatIfPreference -and -not (Test-Writable $GameDir)) {
    if ($NoElevate) { Warn "لا صلاحية للكتابة في مجلد اللعبة."; exit 1 }
    Step "مجلد اللعبة يحتاج صلاحية مسؤول — سيُطلب منك التأكيد."
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
Step "إزالة ملفات التعريب..."
Remove-Thing $pluginDir "BepInEx\plugins\$PluginFolder\" | Out-Null
Remove-Thing $configFile "BepInEx\config\com.kentum.arabic.cfg" | Out-Null

# --- the shared loader ---------------------------------------------------------------------------
Step "فحص BepInEx..."

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
        Warn "توجد تعديلات أخرى في BepInEx\plugins — لن يُحذف BepInEx:"
        foreach ($l in $leftovers) { Warn "    $($l.Name)" }
    }
}

$weInstalledIt = $false
if ($record) { $weInstalledIt = [bool]$record.bepinexInstalledByUs }

if ($KeepBepInEx) {
    Ok "أُبقي على BepInEx بناءً على طلبك (-KeepBepInEx)."
}
elseif ($othersRemain) {
    Ok "أُبقي على BepInEx لأن تعديلات أخرى تعتمد عليه."
}
elseif (-not $record) {
    # No record means a manual unzip, or an install by an older version. We cannot tell whether
    # BepInEx predates us, and guessing wrong deletes something the player wanted.
    Warn "لا يوجد سجل تثبيت، فلا أعرف إن كنا من ثبّت BepInEx — تُرك كما هو."
    Warn "لإزالته يدويًا احذف من مجلد اللعبة: BepInEx\ و winhttp.dll و doorstop_config.ini"
}
elseif (-not $weInstalledIt) {
    Ok "كان BepInEx موجودًا قبل التعريب — تُرك كما هو."
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
    Say "  عرض فقط — لم يُحذف شيء. أعد التشغيل بلا -WhatIf للتنفيذ." 'Yellow'
}
else {
    Say "  تمت الإزالة." 'Green'
    Say ""
    Ok "ملفات حفظ اللعبة لم تُمس؛ التعريب لا يكتب فيها إطلاقًا."
    Ok "إن أردت التأكد من سلامة ملفات اللعبة: Steam > خصائص اللعبة > Verify integrity of game files"
}
Say ""
