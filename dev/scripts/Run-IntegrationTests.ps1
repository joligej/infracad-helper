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

    Na elke run wordt de exitcode gecontroleerd en het Windows Application Event Log op
    nieuwe accoreconsole-crashes (1000/1001/1026) gecontroleerd. Runs zijn strikt
    sequentieel; nooit twee accoreconsole-processen tegelijk.

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

# Eén-procesregel: voorkom dat twee instanties van deze test tegelijk accoreconsole starten.
$mutex = New-Object System.Threading.Mutex($false, "Global\NlcsLegendaIntegrationTest")
if (-not $mutex.WaitOne(0)) { throw "Een andere integratietest draait al; draai sequentieel." }

try {
    Write-Host "==> Plugin bouwen ($Configuration, $AcadVersion)..." -ForegroundColor Cyan
    dotnet build $pluginProj -c $Configuration -p:AcadVersion=$AcadVersion | Out-Null
    $dll = Join-Path $repoRoot "src\NlcsLegenda.Plugin\bin\$Configuration\$tfm\NlcsLegenda.dll"
    if (-not (Test-Path $dll)) { throw "Plugin-DLL niet gevonden: $dll" }
    $dllFwd = (Resolve-Path $dll).Path -replace '\\', '/'

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("nlcs_it_" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $work | Out-Null

    $script:fail = 0
    function Check($name, $cond) {
        if ($cond) { Write-Host "  [OK]   $name" -ForegroundColor Green }
        else { Write-Host "  [FOUT] $name" -ForegroundColor Red; $script:fail++ }
    }

    # Draait één accoreconsole-run sequentieel (-Wait), geeft exitcode + schone output terug.
    function Invoke-Accore($dwg, $scriptBody, $tag, $envTarget) {
        $scr = Join-Path $work "$tag.scr"
        Set-Content -Path $scr -Value $scriptBody -Encoding ASCII
        $outFile = Join-Path $work "$tag.out"
        $startedAt = Get-Date
        $prev = $env:NLCSLEGENDA_TARGET
        if ($null -ne $envTarget) { $env:NLCSLEGENDA_TARGET = $envTarget }
        try {
            $p = Start-Process -FilePath $accore `
                -ArgumentList "/i `"$dwg`" /s `"$scr`" /l en-US" `
                -NoNewWindow -Wait -PassThru -RedirectStandardOutput $outFile
        }
        finally {
            if ($null -ne $envTarget) {
                if ($null -ne $prev) { $env:NLCSLEGENDA_TARGET = $prev } else { Remove-Item Env:\NLCSLEGENDA_TARGET -ErrorAction SilentlyContinue }
            }
        }
        $raw = if (Test-Path $outFile) { (Get-Content $outFile -Raw) -replace "`0", "" } else { "" }
        $crashes = @(Get-WinEvent -FilterHashtable @{LogName = 'Application'; StartTime = $startedAt } -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -match 'accoreconsole' -and ($_.Id -in 1000, 1001, 1026) })
        [pscustomobject]@{ Exit = $p.ExitCode; Out = $raw; Crashes = $crashes.Count }
    }

    $load = "(setq __o (getvar `"SECURELOAD`"))(setvar `"SECURELOAD`" 0)`n(command `"_.NETLOAD`" `"$dllFwd`")(setvar `"SECURELOAD`" __o)"

    # --- Scenario 1: één legenda ---
    Write-Host "==> Scenario 1: een legenda" -ForegroundColor Cyan
    $dwg1 = Join-Path $work "one.dwg"
    Copy-Item $Drawing $dwg1
    $base = [System.IO.Path]::GetFileNameWithoutExtension($dwg1)
    $csv = Join-Path $work "$($base)_Legenda-Legenda 1.csv"
    $json = Join-Path $work "$($base)_Legenda-Legenda 1.json"
    $s1 = "$load`nNLCSLEGENDATEST`nNLCSLEGENDAINFO`nNLCSLEGENDAEXPORT`nNLCSLEGENDAUPDATE`n_.QUIT`n"
    $r1 = Invoke-Accore $dwg1 $s1 "one" $null

    Check "Scenario 1 exitcode 0" ($r1.Exit -eq 0)
    Check "Scenario 1 geen crash-events" ($r1.Crashes -eq 0)
    $placed = [regex]::Match($r1.Out, 'NLCSTEST placed rows=(\d+)')
    Check "NLCSLEGENDATEST plaatst regels" ($placed.Success -and [int]$placed.Groups[1].Value -gt 0)
    Check "NLCSLEGENDAINFO geeft een overzicht" ($r1.Out -match 'overzicht|regel\(s\)|legenda')
    Check "NLCSLEGENDAEXPORT schrijft CSV" (Test-Path $csv)
    Check "NLCSLEGENDAEXPORT schrijft JSON" (Test-Path $json)
    Check "CSV heeft een kop en regels" ((Test-Path $csv) -and ((Get-Content $csv).Count -gt 1))
    Check "NLCSLEGENDAUPDATE werkt bij (1 legenda, auto)" ($r1.Out -match 'bijgewerkt')
    Check "Scenario 1 geen onafgevangen fout" (-not ($r1.Out -match 'Unhandled|FATAL ERROR'))

    # --- Scenario 2: crashregressie met twee legenda's ---
    Write-Host "==> Scenario 2: twee legenda's (crashregressie)" -ForegroundColor Cyan
    $dwg2 = Join-Path $work "two.dwg"
    Copy-Item $Drawing $dwg2
    $make = "$load`nNLCSLEGENDATEST`nNLCSLEGENDATEST`n_.QSAVE`n_.QUIT`n"
    $rMake = Invoke-Accore $dwg2 $make "make" $null
    Check "Twee legenda's geplaatst" (([regex]::Matches($rMake.Out, 'NLCSTEST placed rows=')).Count -ge 2)

    # Gewone update: moet headless veilig weigeren, geen crash, exit 0, geen mutatie.
    $upd = "$load`nNLCSLEGENDAUPDATE`n_.QUIT`n"
    $rRefuse = Invoke-Accore $dwg2 $upd "refuse" $null
    Check "Twee legenda's: exitcode 0" ($rRefuse.Exit -eq 0)
    Check "Twee legenda's: GEEN crash-event" ($rRefuse.Crashes -eq 0)
    Check "Twee legenda's: veilige weigering" ($rRefuse.Out -match 'Meerdere legenda' -and $rRefuse.Out -match 'NLCSLEGENDA_TARGET')
    Check "Twee legenda's: geen GetEntity-crash" (-not ($rRefuse.Out -match 'Unhandled|FATAL ERROR|AccessViolation'))

    # Niet-interactief: NLCSLEGENDA_TARGET wijst de juiste legenda aan.
    $rTarget = Invoke-Accore $dwg2 $upd "target" "Legenda 2"
    Check "Niet-interactief: exitcode 0" ($rTarget.Exit -eq 0)
    Check "Niet-interactief: geen crash-event" ($rTarget.Crashes -eq 0)
    Check "Niet-interactief: juiste legenda bijgewerkt" ($rTarget.Out -match 'Legenda 2.*bijgewerkt')

    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue

    if ($script:fail -eq 0) { Write-Host "==> Alle integratiechecks geslaagd." -ForegroundColor Green; exit 0 }
    Write-Host "==> $($script:fail) integratiecheck(s) mislukt." -ForegroundColor Red; exit 1
}
finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
