<#
.SYNOPSIS
    Draait een reeks NLCS Legenda-commando's headless via de AutoCAD Core Console en
    controleert de resultaten. Bedoeld als lokale integratietest (vereist AutoCAD).

.DESCRIPTION
    De opgegeven tekening wordt eerst naar een tijdelijke map gekopieerd, zodat alle
    resultaten (export-bestanden e.d.) daar terechtkomen en de bron ongemoeid blijft.
    Getest worden: NLCSLEGENDATEST (plaatsen), NLCSLEGENDAINFO (overzicht),
    NLCSLEGENDAEXPORT (CSV/JSON) en NLCSLEGENDAUPDATE (bijwerken).

.EXAMPLE
    ./Run-IntegrationTests.ps1 -Drawing "C:\pad\ontwerp.dwg" -AcadVersion 2027
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Drawing,
    [string]$AcadVersion = "2027",
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$pluginProj = Join-Path $repoRoot "src\NlcsLegenda.Plugin\NlcsLegenda.Plugin.csproj"
$accore = "C:\Program Files\Autodesk\AutoCAD $AcadVersion\accoreconsole.exe"
$tfm = if ($AcadVersion -eq "2027") { "net10.0-windows" } else { "net8.0-windows" }

if (-not (Test-Path $accore)) { throw "accoreconsole niet gevonden: $accore" }
if (-not (Test-Path $Drawing)) { throw "Tekening niet gevonden: $Drawing" }

Write-Host "==> Plugin bouwen ($Configuration, $AcadVersion)..." -ForegroundColor Cyan
dotnet build $pluginProj -c $Configuration -p:AcadVersion=$AcadVersion | Out-Null
$dll = Join-Path $repoRoot "src\NlcsLegenda.Plugin\bin\$Configuration\$tfm\NlcsLegenda.dll"
if (-not (Test-Path $dll)) { throw "Plugin-DLL niet gevonden: $dll" }
$dllFwd = (Resolve-Path $dll).Path -replace '\\', '/'

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("nlcs_it_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $work | Out-Null
$dwgCopy = Join-Path $work ([System.IO.Path]::GetFileName($Drawing))
Copy-Item $Drawing $dwgCopy
$base = [System.IO.Path]::GetFileNameWithoutExtension($Drawing)
$csv = Join-Path $work "$($base)_NLCS-legenda.csv"
$json = Join-Path $work "$($base)_NLCS-legenda.json"

$scr = Join-Path $work "it.scr"
@"
(setq __o (getvar "SECURELOAD"))(setvar "SECURELOAD" 0)
(command "_.NETLOAD" "$dllFwd")(setvar "SECURELOAD" __o)
(command "NLCSLEGENDATEST")
(command "NLCSLEGENDAINFO")
(command "NLCSLEGENDAEXPORT")
(command "NLCSLEGENDAUPDATE")
(setq g (tblobjname "GROUP" "*"))
(princ "\nIT|done|IT\n")
(princ)
"@ | Set-Content -Path $scr -Encoding ASCII

Write-Host "==> Commando's draaien op een kopie van: $([System.IO.Path]::GetFileName($Drawing))" -ForegroundColor Cyan
$log = Join-Path $work "it.log"
Push-Location $work
& $accore /i "$dwgCopy" /s "$scr" 2>&1 | Out-File -FilePath $log -Encoding UTF8
Pop-Location
$out = (Get-Content $log -Raw) -replace "`0", ""

$fail = 0
function Check($name, $cond) {
    if ($cond) { Write-Host "  [OK]   $name" -ForegroundColor Green }
    else { Write-Host "  [FOUT] $name" -ForegroundColor Red; $script:fail++ }
}

$placed = [regex]::Match($out, 'NLCSTEST placed rows=(\d+)')
Check "NLCSLEGENDATEST plaatst regels" ($placed.Success -and [int]$placed.Groups[1].Value -gt 0)
Check "NLCSLEGENDAINFO geeft een overzicht" ($out -match 'overzicht|regel\(s\)')
Check "NLCSLEGENDAEXPORT schrijft CSV" (Test-Path $csv)
Check "NLCSLEGENDAEXPORT schrijft JSON" (Test-Path $json)
Check "CSV heeft een kop en regels" ((Test-Path $csv) -and ((Get-Content $csv).Count -gt 1))
Check "NLCSLEGENDAUPDATE werkt bij" ($out -match 'bijgewerkt')
Check "Geen onafgevangen fout" (-not ($out -match 'Unhandled|FATAL ERROR'))

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

if ($fail -eq 0) { Write-Host "==> Alle integratiechecks geslaagd." -ForegroundColor Green; exit 0 }
Write-Host "==> $fail integratiecheck(s) mislukt." -ForegroundColor Red; exit 1
