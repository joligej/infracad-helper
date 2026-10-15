<#
.synopsis
    legenda controleren op een bronkopie, inclusief profielherstel
#>
#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Drawing,
    [ValidateSet('2025','2026','2027')][string]$AcadVersion = '2025',
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [switch]$SaveResult
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CoreConsole.ps1')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$Drawing = (Resolve-Path -LiteralPath $Drawing).Path
$sourceHash = (Get-FileHash -LiteralPath $Drawing).Hash
$core = "C:/Program Files/Autodesk/AutoCAD $AcadVersion/accoreconsole.exe"
if (!(Test-Path -LiteralPath $core)) { throw "Core Console ontbreekt: $core" }
dotnet build (Join-Path $repoRoot 'src/NlcsLegenda.Plugin/NlcsLegenda.Plugin.csproj') -c $Configuration -p:AcadVersion=$AcadVersion
if ($LASTEXITCODE -ne 0) { throw 'Build mislukt; er wordt geen bestaande DLL getest.' }
$framework = if ($AcadVersion -eq '2027') { 'net10.0-windows' } else { 'net8.0-windows' }
$dll = (Resolve-Path -LiteralPath (Join-Path $repoRoot "src/NlcsLegenda.Plugin/bin/$Configuration/$framework/NlcsLegenda.dll")).Path
$dllHash = (Get-FileHash -LiteralPath $dll).Hash
$work = Join-Path $repoRoot ('artifacts/headless/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$copy = Join-Path $work 'test.dwg'
Copy-Item -LiteralPath $Drawing -Destination $copy
Set-ItemProperty -LiteralPath $copy -Name IsReadOnly -Value $false
$script = Join-Path $work 'run.scr'
$save = if ($SaveResult) { '_.QSAVE' } else { '' }
@"
(setq testF (getvar "FILEDIA") testS (getvar "SECURELOAD"))
(setvar "SECURELOAD" 0)
(command "_.NETLOAD" "$($dll.Replace('\','/'))")
(setvar "SECURELOAD" testS)
NLCSLEGENDAAUDITVERSION
NLCSLEGENDATEST
$save
(princ (strcat "\nHEADLESS_DBMOD=" (itoa (getvar "DBMOD"))))
(princ (strcat "\nHEADLESS_SYSVARS=" (if (and (= testF (getvar "FILEDIA")) (= testS (getvar "SECURELOAD"))) "OK" "FAIL")))
(princ "\nHEADLESS_END\n")
_.QUIT
_Y
"@ -split "`r?`n" | Where-Object { $_.Length -gt 0 } | Set-Content -LiteralPath $script -Encoding ascii
try {
    $run = Invoke-NlcsCoreConsole -Executable $core -Drawing $copy -Script $script -Log (Join-Path $work 'console.log')
    $log = ((Get-Content -LiteralPath $run.Log -Raw) -replace "`0", '')
    $actual = ($log -split "`r?`n" | Where-Object { $_ -notmatch '^Command:' }) -join "`n"
    foreach ($marker in @("SHA256=$dllHash", 'HEADLESS_SYSVARS=OK', "`nHEADLESS_END")) {
        if (!$actual.Contains($marker)) { throw "Resultaat ontbreekt: $marker. Log: $($run.Log)" }
    }
    if ($SaveResult -and !$actual.Contains("HEADLESS_DBMOD=0")) { throw "QSAVE heeft wijzigingen niet opgeslagen. Log: $($run.Log)" }
    if ($actual -notmatch 'NLCSTEST placed rows=[1-9]\d*' -or
        $actual -match '(?i)\bFAIL\b|\berror:|\bfout:|Unknown command|Invalid option|An error occurred during save') {
        throw "Legendatest mislukt. Log: $($run.Log)"
    }
    Write-Output "HEADLESS PASS; profielwaarden gecontroleerd: $($run.ProfileValuesChecked); uitvoer: $work"
}
finally {
    if ((Get-FileHash -LiteralPath $Drawing).Hash -ne $sourceHash) { throw 'Brontekening gewijzigd.' }
}
