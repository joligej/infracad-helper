<#
.SYNOPSIS
    Draait een reeks NLCS Legenda-commando's headless via de AutoCAD Core Console en
    controleert de resultaten. Bedoeld als lokale integratietest (vereist AutoCAD).

.DESCRIPTION
    De opgegeven tekening wordt eerst naar een tijdelijke map gekopieerd, zodat alle
    resultaten (export-bestanden e.d.) daar terechtkomen en de bron ongemoeid blijft.

    Twee scenario's:
      1. Eén legenda: NLCSLEGENDATEST, NLCSLEGENDAINFO, NLCSLEGENDAEXPORT, NLCSLEGENDAUPDATE.
      2. Crashregressie: twee beheerde legenda's. NLCSLEGENDAUPDATE moet headless veilig
         weigeren (geen GetEntity/native crash), en met NLCSLEGENDA_TARGET de juiste
         legenda bijwerken.

    Elke run controleert de procescode, het eindresultaat en de systeemvariabelen,
    inclusief de opgeslagen profielwaarden. Runs hebben een timeout en starten
    alleen zonder andere CAD-processen. Foutlogs blijven in artifacts/integration.

.EXAMPLE
    ./Run-IntegrationTests.ps1 -Drawing "C:\pad\ontwerp.dwg" -AcadVersion 2027
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Drawing,
    [ValidateSet("2025","2026","2027")][string]$AcadVersion = "2027",
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "CoreConsole.ps1")
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$pluginProj = Join-Path $repoRoot "src\NlcsLegenda.Plugin\NlcsLegenda.Plugin.csproj"
$accore = "C:\Program Files\Autodesk\AutoCAD $AcadVersion\accoreconsole.exe"
$Drawing = (Resolve-Path -LiteralPath $Drawing).Path
$sourceHash = (Get-FileHash -LiteralPath $Drawing).Hash
$tfm = if ($AcadVersion -eq "2027") { "net10.0-windows" } else { "net8.0-windows" }

if (-not (Test-Path $accore)) { throw "accoreconsole niet gevonden: $accore" }
if (-not (Test-Path $Drawing)) { throw "Tekening niet gevonden: $Drawing" }

try {
    Write-Host "==> Plugin bouwen ($Configuration, $AcadVersion)..." -ForegroundColor Cyan
    dotnet build $pluginProj -c $Configuration -p:AcadVersion=$AcadVersion
    if ($LASTEXITCODE -ne 0) { throw "Build mislukt; er wordt geen bestaande DLL getest." }
    $dll = Join-Path $repoRoot "src\NlcsLegenda.Plugin\bin\$Configuration\$tfm\NlcsLegenda.dll"
    if (-not (Test-Path $dll)) { throw "Plugin-DLL niet gevonden: $dll" }
    $dllFwd = (Resolve-Path $dll).Path -replace '\\', '/'

    $work = Join-Path $repoRoot ("artifacts/integration/" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $work | Out-Null

    $script:fail = 0
    function Check($name, $cond) {
        if ($cond) { Write-Host "  [OK]   $name" -ForegroundColor Green }
        else { Write-Host "  [FOUT] $name" -ForegroundColor Red; $script:fail++ }
    }

    # Elke run moet zijn eindmelding bereiken; een procescode 0 alleen is onvoldoende.
    function Invoke-Accore($dwg, $scriptBody, $tag, $envTarget) {
        $scr = Join-Path $work "$tag.scr"
        $end = '(princ (strcat "\nINTEGRATION_SYSVARS=" (if (and (= __f (getvar "FILEDIA")) (= __o (getvar "SECURELOAD"))) "OK" "FAIL")))' + "`n" + '(princ "\nINTEGRATION_END\n")' + "`n_.QUIT`n_Y"
        $scriptBody = $scriptBody.Replace('_.QUIT', $end)
        Set-Content -LiteralPath $scr -Value $scriptBody -Encoding ASCII
        $outFile = Join-Path $work "$tag.out"
        $childEnvironment = @{}
        if ($null -ne $envTarget) { $childEnvironment.NLCSLEGENDA_TARGET = $envTarget }
        $run = Invoke-NlcsCoreConsole -Executable $accore -Drawing $dwg -Script $scr -Log $outFile -Environment $childEnvironment
        $raw = (Get-Content -LiteralPath $outFile -Raw) -replace "`0", ""
        $actual = ($raw -split "`r?`n" | Where-Object { $_ -notmatch '^Command:' }) -join "`n"
        if (!$actual.Contains("`nINTEGRATION_END") -or !$actual.Contains('INTEGRATION_SYSVARS=OK')) {
            throw "Onvolledige run of gewijzigde systeemvariabelen: $outFile"
        }
        if ($actual -match '(?i)Unhandled|FATAL ERROR|AccessViolation|Unknown command|Invalid option|An error occurred during save|\berror:|\bfout:') {
            throw "Fout in Core Console: $outFile"
        }
        [pscustomobject]@{ Exit = $run.ExitCode; Out = $actual }
    }

    $load = "(setq __f (getvar `"FILEDIA`"))`n(setq __o (getvar `"SECURELOAD`"))(setvar `"SECURELOAD`" 0)`n(command `"_.NETLOAD`" `"$dllFwd`")(setvar `"SECURELOAD`" __o)"

    # --- Scenario 1: één legenda ---
    Write-Host "==> Scenario 1: een legenda" -ForegroundColor Cyan
    $dwg1 = Join-Path $work "one.dwg"
    Copy-Item $Drawing $dwg1
    Set-ItemProperty $dwg1 -Name IsReadOnly -Value $false
    $base = [System.IO.Path]::GetFileNameWithoutExtension($dwg1)
    $csv = Join-Path $work "$($base)_Legenda-Legenda 1.csv"
    $json = Join-Path $work "$($base)_Legenda-Legenda 1.json"
    $s1 = "$load`nNLCSLEGENDATEST`nNLCSLEGENDAINFO`nNLCSLEGENDAEXPORT`nNLCSLEGENDAUPDATE`n_.QUIT`n"
    $r1 = Invoke-Accore $dwg1 $s1 "one" $null

    Check "Scenario 1 exitcode 0" ($r1.Exit -eq 0)
    $placed = [regex]::Match($r1.Out, 'NLCSTEST placed rows=(\d+)')
    Check "NLCSLEGENDATEST plaatst regels" ($placed.Success -and [int]$placed.Groups[1].Value -gt 0)
    Check "NLCSLEGENDAINFO geeft een overzicht" ($r1.Out -match "1 legenda\('s\) in deze tekening:")
    Check "NLCSLEGENDAEXPORT schrijft CSV" (Test-Path $csv)
    Check "NLCSLEGENDAEXPORT schrijft JSON" (Test-Path $json)
    Check "CSV en JSON bevatten de geplaatste regels" ((Test-Path $csv) -and (Test-Path $json) -and $placed.Success -and
        @(Import-Csv -LiteralPath $csv -Delimiter ';').Count -eq [int]$placed.Groups[1].Value -and
        @(Get-Content -LiteralPath $json -Raw | ConvertFrom-Json).Count -eq [int]$placed.Groups[1].Value)
    Check "NLCSLEGENDAUPDATE werkt bij (1 legenda, auto)" ($r1.Out -match 'bijgewerkt')
    Check "Scenario 1 geen onafgevangen fout" (-not ($r1.Out -match 'Unhandled|FATAL ERROR'))

    # --- Scenario 2: crashregressie met twee legenda's ---
    Write-Host "==> Scenario 2: twee legenda's (crashregressie)" -ForegroundColor Cyan
    $dwg2 = Join-Path $work "two.dwg"
    Copy-Item $Drawing $dwg2
    Set-ItemProperty $dwg2 -Name IsReadOnly -Value $false
    $make = "$load`nNLCSLEGENDATEST`nNLCSLEGENDATEST`n_.QSAVE`n_.QUIT`n"
    $rMake = Invoke-Accore $dwg2 $make "make" $null
    Check "Twee legenda's geplaatst" (([regex]::Matches($rMake.Out, 'NLCSTEST placed rows=')).Count -ge 2)

    # Gewone update: moet headless veilig weigeren, geen crash, exit 0, geen mutatie.
    $upd = "$load`nNLCSLEGENDAUPDATE`n_.QUIT`n"
    $rRefuse = Invoke-Accore $dwg2 $upd "refuse" $null
    Check "Twee legenda's: exitcode 0" ($rRefuse.Exit -eq 0)
    Check "Twee legenda's: veilige weigering" ($rRefuse.Out -match 'Meerdere legenda' -and $rRefuse.Out -match 'NLCSLEGENDA_TARGET')
    Check "Twee legenda's: geen GetEntity-crash" (-not ($rRefuse.Out -match 'Unhandled|FATAL ERROR|AccessViolation'))

    # Niet-interactief: NLCSLEGENDA_TARGET wijst de juiste legenda aan.
    $rTarget = Invoke-Accore $dwg2 $upd "target" "Legenda 2"
    Check "Niet-interactief: exitcode 0" ($rTarget.Exit -eq 0)
    Check "Niet-interactief: juiste legenda bijgewerkt" ($rTarget.Out -match 'Legenda 2.*bijgewerkt')

    Write-Output "Integratielogs: $work"

    if ($script:fail -eq 0) { Write-Host "==> Alle integratiechecks geslaagd." -ForegroundColor Green; exit 0 }
    Write-Host "==> $($script:fail) integratiecheck(s) mislukt." -ForegroundColor Red; exit 1
}
finally {
    if ((Get-FileHash -LiteralPath $Drawing).Hash -ne $sourceHash) { throw "Brontekening gewijzigd." }
}
