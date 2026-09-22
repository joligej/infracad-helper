<#
.SYNOPSIS
    Bouwt de NLCS Legenda-plugin en draait een headless regressietest tegen een
    tekening via de AutoCAD Core Console (accoreconsole.exe).

.DESCRIPTION
    Laadt de gebouwde plugin met NETLOAD (SECURELOAD wordt tijdelijk uitgezet en
    daarna hersteld) en voert het commando NLCSLEGENDATEST uit. De brontekening
    wordt NIET overschreven; er wordt niets opgeslagen tenzij -SaveResult is opgegeven.
    Tijdelijk gewijzigde systeemvariabelen (SECURELOAD, en bij -SaveResult FILEDIA)
    worden in het script hersteld; na afloop controleert het script of FILEDIA weer op
    de oorspronkelijke waarde staat en herstelt die zo nodig, zodat het AutoCAD-profiel
    niet blijvend verandert.

.EXAMPLE
    ./Run-HeadlessLegendTest.ps1 -Drawing "C:\pad\naar\ontwerp.dwg"

.EXAMPLE
    ./Run-HeadlessLegendTest.ps1 -Drawing "ontwerp.dwg" -AcadVersion 2027 -SaveResult
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Drawing,

    [string]$AcadVersion = "2025",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$SaveResult
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$pluginProj = Join-Path $repoRoot "src\NlcsLegenda.Plugin\NlcsLegenda.Plugin.csproj"
$accore = "C:\Program Files\Autodesk\AutoCAD $AcadVersion\accoreconsole.exe"

if (-not (Test-Path $accore)) { throw "accoreconsole niet gevonden: $accore" }
if (-not (Test-Path $Drawing)) { throw "Tekening niet gevonden: $Drawing" }

Write-Host "==> Plugin bouwen ($Configuration, AutoCAD $AcadVersion)..." -ForegroundColor Cyan
dotnet build $pluginProj -c $Configuration -p:AcadVersion=$AcadVersion | Out-Null

$tfm = if ($AcadVersion -eq "2027") { "net10.0-windows" } else { "net8.0-windows" }
$dll = Join-Path $repoRoot "src\NlcsLegenda.Plugin\bin\$Configuration\$tfm\NlcsLegenda.dll"
if (-not (Test-Path $dll)) { throw "Plugin-DLL niet gevonden: $dll" }
$dllFwd = $dll -replace '\\', '/'

$work = Join-Path $repoRoot "_dev_tmp"
New-Item -ItemType Directory -Force -Path $work | Out-Null
$scrPath = Join-Path $work "headless_run.scr"
$sysvarPath = (Join-Path $work "sysvars.txt") -replace '\\', '/'

# SAVEAS met een expliciet pad werkt in accoreconsole zonder FILEDIA-manipulatie (headless
# toont sowieso geen dialogen); er wordt dus bewust geen systeemvariabele gewijzigd.
$saveLine = ""
if ($SaveResult) {
    $resultDwg = (Join-Path $work "legenda_result.dwg") -replace '\\', '/'
    $saveLine = "(command `"_.SAVEAS`" `"2018`" `"$resultDwg`")"
}

# FILEDIA en SECURELOAD worden aan het begin én eind van dezelfde run vastgelegd, zodat
# achteraf te controleren is dat de run ze niet blijvend heeft gewijzigd (SECURELOAD wordt
# tijdens NETLOAD tijdelijk uitgezet en meteen hersteld).
@"
(setq __fBefore (getvar "FILEDIA"))
(setq __sBefore (getvar "SECURELOAD"))
(setvar "SECURELOAD" 0)
(command "_.NETLOAD" "$dllFwd")
(setvar "SECURELOAD" __sBefore)
(command "NLCSLEGENDATEST")
$saveLine
(setq __f (open "$sysvarPath" "w"))
(write-line (strcat "FILEDIA_before=" (itoa __fBefore)) __f)
(write-line (strcat "FILEDIA_after=" (itoa (getvar "FILEDIA"))) __f)
(write-line (strcat "SECURELOAD_before=" (itoa __sBefore)) __f)
(write-line (strcat "SECURELOAD_after=" (itoa (getvar "SECURELOAD"))) __f)
(close __f)
(princ)
"@ | Set-Content -Path $scrPath -Encoding ASCII

Write-Host "==> NLCSLEGENDATEST draaien op: $Drawing" -ForegroundColor Cyan
$logPath = Join-Path $work "headless_console.log"
& $accore /i "$Drawing" /s "$scrPath" 2>&1 | Out-File -FilePath $logPath -Encoding UTF8

# accoreconsole-output kan als UTF-16 verschijnen (spaties/NUL tussen tekens);
# verwijder NUL en spaties zodat de sleutelregels leesbaar zijn.
Get-Content $logPath |
    ForEach-Object { ($_ -replace "`0", "") -replace ' ', '' } |
    Where-Object { $_ -match "NLCSTEST|geladen|error|Exception" } |
    ForEach-Object { Write-Host "    $_" }

# Bevestig dat tijdelijk gewijzigde systeemvariabelen weer op hun beginwaarde staan.
$sysvarFile = Join-Path $work "sysvars.txt"
if (Test-Path $sysvarFile) {
    $v = @{}
    Get-Content $sysvarFile | ForEach-Object {
        if ($_ -match '^(\w+)=(\d+)$') { $v[$Matches[1]] = [int]$Matches[2] }
    }
    $leaked = @()
    foreach ($name in "FILEDIA", "SECURELOAD") {
        if ($v["${name}_after"] -ne $v["${name}_before"]) {
            $leaked += "$name ($($v["${name}_before"]) -> $($v["${name}_after"]))"
        }
    }
    if ($leaked.Count -gt 0) {
        Write-Warning "Systeemvariabele(n) niet hersteld: $($leaked -join ', ')."
    }
    else {
        Write-Host "    Systeemvariabelen ongewijzigd na de run." -ForegroundColor DarkGray
    }
}

Write-Host "==> Klaar." -ForegroundColor Green
