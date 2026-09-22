<#
.SYNOPSIS
    Bouwt de plugin in Release en zet een Autodesk-bundle klaar in artifacts/,
    klaar om te kopieren naar %APPDATA%\Autodesk\ApplicationPlugins\.

.DESCRIPTION
    AutoCAD 2025 en 2026 draaien op .NET 8, AutoCAD 2027 op .NET 10. Het script kiest
    de juiste doel-framework en de juiste versie-eis (R25.0/R25.1/R26.0) in het
    bundle-manifest.

.EXAMPLE
    ./Build-Bundle.ps1 -AcadVersion 2025

.EXAMPLE
    ./Build-Bundle.ps1 -AcadVersion 2026

.EXAMPLE
    ./Build-Bundle.ps1 -AcadVersion 2027
#>
[CmdletBinding()]
param(
    [ValidateSet("2025", "2026", "2027")]
    [string]$AcadVersion = "2025"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$pluginProj = Join-Path $repoRoot "src\NlcsLegenda.Plugin\NlcsLegenda.Plugin.csproj"

# Versie uit de centrale Directory.Build.props: die is leidend voor de DLL's en het manifest.
$version = ([regex]::Match((Get-Content (Join-Path $repoRoot "Directory.Build.props") -Raw),
    '<VersionPrefix>([^<]+)</VersionPrefix>')).Groups[1].Value
if (-not $version) { throw "Kon de versie niet uit Directory.Build.props lezen." }

$tfm = if ($AcadVersion -eq "2027") { "net10.0-windows" } else { "net8.0-windows" }
$dotnet = if ($AcadVersion -eq "2027") { ".NET 10" } else { ".NET 8" }
$series = switch ($AcadVersion) {
    "2027" { "R26.0" }
    "2026" { "R25.1" }
    default { "R25.0" }
}

Write-Host "==> Plugin bouwen (Release, AutoCAD $AcadVersion, $tfm)..." -ForegroundColor Cyan
dotnet build $pluginProj -c Release -p:AcadVersion=$AcadVersion | Out-Null

$binDir = Join-Path $repoRoot "src\NlcsLegenda.Plugin\bin\Release\$tfm"
$outBundle = Join-Path $repoRoot "artifacts\NlcsLegenda.bundle"
$outContents = Join-Path $outBundle "Contents"

if (Test-Path $outBundle) { Remove-Item $outBundle -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outContents | Out-Null

# Manifest overnemen en de versie-eis passend maken voor de doelversie.
$manifest = Get-Content (Join-Path $repoRoot "deploy\NlcsLegenda.bundle\PackageContents.xml") -Raw
$manifest = $manifest -replace 'AppVersion="[^"]*"', "AppVersion=`"$version`""
$manifest = $manifest -replace 'SeriesMin="[^"]*"', "SeriesMin=`"$series`""
$manifest = $manifest -replace 'SeriesMax="[^"]*"', "SeriesMax=`"$series`""
$manifest = $manifest -replace 'Description="NLCS Legenda \(\.NET[^"]*\)"', "Description=`"NLCS Legenda ($dotnet)`""
Set-Content -Path (Join-Path $outBundle "PackageContents.xml") -Value $manifest -Encoding UTF8

Copy-Item (Join-Path $binDir "NlcsLegenda.dll") $outContents
Copy-Item (Join-Path $binDir "NlcsLegenda.Core.dll") $outContents

Write-Host "==> Bundle klaar: $outBundle" -ForegroundColor Green
Write-Host "    Kopieer naar: $env:APPDATA\Autodesk\ApplicationPlugins\" -ForegroundColor Yellow

