<#
.synopsis
    gebruikersinstaller voor 2025, 2026 en 2027, optioneel ondertekend
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$PfxPath,
    [System.Security.SecureString]$PfxPassword,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$pluginProj = Join-Path $repoRoot "src\NlcsLegenda.Plugin\NlcsLegenda.Plugin.csproj"
$manifestSrc = Join-Path $repoRoot "deploy\NlcsLegenda.bundle\PackageContents.xml"
$wxs = Join-Path $repoRoot "deploy\installer\Package.wxs"

if (-not $Version) {
    $Version = ([regex]::Match((Get-Content (Join-Path $repoRoot "Directory.Build.props") -Raw),
        '<VersionPrefix>([^<]+)</VersionPrefix>')).Groups[1].Value
}
if (-not $Version) { throw "Kon de versie niet bepalen." }
Write-Host "==> Installer bouwen voor versie $Version" -ForegroundColor Cyan

if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw "wix (WiX Toolset) niet gevonden. Installeer het met: dotnet tool install --global wix --version 5.0.2"
}

# dezelfde versiebinaries in de installer en losse bundels
Write-Host "==> Plugin bouwen (net8, AutoCAD 2025)..." -ForegroundColor Cyan
dotnet build $pluginProj -c Release -p:AcadVersion=2025
if ($LASTEXITCODE -ne 0) { throw "Pluginbuild 2025 mislukt; MSI-bouw gestopt." }
$net8Bin2025 = Join-Path $repoRoot "src\NlcsLegenda.Plugin\bin\Release\net8.0-windows"
$stage = Join-Path $repoRoot "artifacts\bundle-combined"
if (Test-Path -LiteralPath $stage) {
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    $expectedStage = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\bundle-combined"))
    if ($resolvedStage -ne $expectedStage) { throw "Onverwacht stagingpad; opruimen gestopt." }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
$net82025Out = Join-Path $stage "Contents\net8-2025"
New-Item -ItemType Directory -Force -Path $net82025Out | Out-Null
Copy-Item (Join-Path $net8Bin2025 "NlcsLegenda.dll") $net82025Out
Copy-Item (Join-Path $net8Bin2025 "NlcsLegenda.Core.dll") $net82025Out

Write-Host "==> Plugin bouwen (net8, AutoCAD 2026)..." -ForegroundColor Cyan
dotnet build $pluginProj -c Release -p:AcadVersion=2026
if ($LASTEXITCODE -ne 0) { throw "Pluginbuild 2026 mislukt; MSI-bouw gestopt." }
$net8Bin2026 = Join-Path $repoRoot "src\NlcsLegenda.Plugin\bin\Release\net8.0-windows"
$net82026Out = Join-Path $stage "Contents\net8-2026"
New-Item -ItemType Directory -Force -Path $net82026Out | Out-Null
Copy-Item (Join-Path $net8Bin2026 "NlcsLegenda.dll") $net82026Out
Copy-Item (Join-Path $net8Bin2026 "NlcsLegenda.Core.dll") $net82026Out

Write-Host "==> Plugin bouwen (net10, AutoCAD 2027)..." -ForegroundColor Cyan
dotnet build $pluginProj -c Release -p:AcadVersion=2027
if ($LASTEXITCODE -ne 0) { throw "Pluginbuild 2027 mislukt; MSI-bouw gestopt." }
$net10Bin = Join-Path $repoRoot "src\NlcsLegenda.Plugin\bin\Release\net10.0-windows"
$net10Out = Join-Path $stage "Contents\net10"
New-Item -ItemType Directory -Force -Path $net10Out | Out-Null
Copy-Item (Join-Path $net10Bin "NlcsLegenda.dll") $net10Out
Copy-Item (Join-Path $net10Bin "NlcsLegenda.Core.dll") $net10Out

[xml]$src = Get-Content $manifestSrc -Raw
$commandsXml = $src.ApplicationPackage.Components.ComponentEntry.Commands.InnerXml
$productCode = $src.ApplicationPackage.ProductCode

function New-Component([string]$series, [string]$path, [string]$dotnet) {
    @"
  <Components Description="NLCS Legenda ($dotnet)">
    <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="$($series.Split('-')[0])" SeriesMax="$($series.Split('-')[1])" />
    <ComponentEntry AppName="NlcsLegenda" ModuleName="./Contents/$path/NlcsLegenda.dll" AppType=".NET"
                    LoadOnAutoCADStartup="True">
      <Commands GroupName="NLCS_LEGENDA">$commandsXml</Commands>
    </ComponentEntry>
  </Components>
"@
}

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0"
                    AppVersion="$Version"
                    ProductCode="$productCode"
                    Name="NLCS Legenda"
                    Description="Automatische NLCS-legendagenerator"
                    Author="infracad-helper">
  <CompanyDetails Name="infracad-helper" />
$(New-Component "R25.0-R25.0" "net8-2025" ".NET 8")
$(New-Component "R25.1-R25.1" "net8-2026" ".NET 8")
$(New-Component "R26.0-R26.0" "net10" ".NET 10")
</ApplicationPackage>
"@
Set-Content -Path (Join-Path $stage "PackageContents.xml") -Value $manifest -Encoding UTF8

$outDir = Join-Path $repoRoot "artifacts"
$msi = Join-Path $outDir "NlcsLegenda-$Version.msi"
Write-Host "==> MSI samenstellen met WiX..." -ForegroundColor Cyan
wix build $wxs -arch x64 -d "Version=$Version" -d "BundleStage=$stage" -o $msi
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $msi)) { throw "MSI is niet gebouwd." }

if ($PfxPath) {
    if (-not (Test-Path $PfxPath)) { throw "PFX niet gevonden: $PfxPath" }
    $signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName | Select-Object -Last 1
    if (-not $signtool) { throw "signtool.exe (x64) niet gevonden; installeer de Windows SDK." }
    $plainPw = ""
    if ($PfxPassword) {
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword)
        try { $plainPw = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
    }
    Write-Host "==> MSI ondertekenen..." -ForegroundColor Cyan
    & $signtool.FullName sign /fd SHA256 /f $PfxPath /p $plainPw /tr $TimestampUrl /td SHA256 $msi
    if ($LASTEXITCODE -ne 0) { throw "Ondertekenen mislukt." }

    # handtekening vergelijken met het meegeleverde openbare certificaat
    $cerPath = Join-Path $repoRoot "deploy\joligej-codesign.cer"
    if (-not (Test-Path $cerPath)) { throw "Publiek certificaat ontbreekt: $cerPath" }
    $expected = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)).Thumbprint
    $sig = Get-AuthenticodeSignature $msi
    if ($null -eq $sig.SignerCertificate) { throw "MSI is niet ondertekend." }
    if ($sig.Status -eq 'HashMismatch') { throw "MSI is na ondertekenen gewijzigd (HashMismatch)." }
    if ($sig.SignerCertificate.Thumbprint -ne $expected) {
        throw "Signer-thumbprint ($($sig.SignerCertificate.Thumbprint)) komt niet overeen met deploy/joligej-codesign.cer ($expected)."
    }
    Write-Host "==> Handtekening geverifieerd: $($sig.SignerCertificate.Subject), thumbprint $($sig.SignerCertificate.Thumbprint)" -ForegroundColor Green
}

Write-Host "==> Klaar: $msi" -ForegroundColor Green
