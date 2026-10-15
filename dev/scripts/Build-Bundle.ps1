<#
.synopsis
    releasebundel voor de gekozen autocad-versie
#>
[CmdletBinding()]
param(
    [ValidateSet("2025", "2026", "2027")]
    [string]$AcadVersion = "2025"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$pluginProj = Join-Path $repoRoot "src\NlcsLegenda.Plugin\NlcsLegenda.Plugin.csproj"

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
dotnet build $pluginProj -c Release -p:AcadVersion=$AcadVersion
if ($LASTEXITCODE -ne 0) { throw "Pluginbuild mislukt; er wordt geen bundel gemaakt van oude DLLs." }

$binDir = Join-Path $repoRoot "src\NlcsLegenda.Plugin\bin\Release\$tfm"
$outBundle = Join-Path $repoRoot "artifacts\NlcsLegenda.bundle"
$outContents = Join-Path $outBundle "Contents"

if (Test-Path -LiteralPath $outBundle) {
    $resolvedBundle = (Resolve-Path -LiteralPath $outBundle).Path
    $expectedBundle = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\NlcsLegenda.bundle"))
    if ($resolvedBundle -ne $expectedBundle) { throw "Onverwacht bundelpad; opruimen gestopt." }
    Remove-Item -LiteralPath $resolvedBundle -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $outContents | Out-Null

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

