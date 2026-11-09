<#
.synopsis
    gebruikersinstaller voor 2025, 2026 en 2027, optioneel ondertekend
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$UpdaterPath,
    [string]$BundleDirectory,
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

# ondertekende bestanden uit de bundels ongewijzigd overnemen
$stage = Join-Path $repoRoot "artifacts\bundle-combined"
if ($BundleDirectory) {
    $inputDirectory = (Resolve-Path -LiteralPath $BundleDirectory).Path.TrimEnd('\')
    $stageDirectory = [IO.Path]::GetFullPath($stage).TrimEnd('\')
    if ($inputDirectory -eq $stageDirectory -or $inputDirectory.StartsWith($stageDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'De invoerbundels mogen niet in de tijdelijke installermap staan.'
    }
    $BundleDirectory = $inputDirectory
}
if (Test-Path -LiteralPath $stage) {
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    $expectedStage = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\bundle-combined"))
    if ($resolvedStage -ne $expectedStage) { throw "Onverwacht stagingpad; opruimen gestopt." }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
foreach ($year in 2025, 2026, 2027) {
    $framework = if ($year -eq 2027) { 'net10.0-windows' } else { 'net8.0-windows' }
    $folder = if ($year -eq 2027) { 'net10' } else { "net8-$year" }
    if ($BundleDirectory) {
        $bundle = Join-Path $BundleDirectory "$year/NlcsLegenda.bundle"
        [xml]$metadata = Get-Content -LiteralPath (Join-Path $bundle 'PackageContents.xml') -Raw
        $series = switch ($year) { 2025 { 'R25.0' } 2026 { 'R25.1' } 2027 { 'R26.0' } }
        $hostRequirements = $metadata.ApplicationPackage.Components.RuntimeRequirements
        if ($metadata.ApplicationPackage.AppVersion -ne $Version -or $hostRequirements.SeriesMin -ne $series -or $hostRequirements.SeriesMax -ne $series) {
            throw "Bundel voor $year heeft een andere versie of CAD-jaargang."
        }
        $source = Join-Path $bundle 'Contents'
    } else {
        dotnet build $pluginProj -c Release -p:AcadVersion=$year
        if ($LASTEXITCODE) { throw "Pluginbuild $year mislukt; MSI-bouw gestopt." }
        $source = Join-Path $repoRoot "src/NlcsLegenda.Plugin/bin/Release/$framework"
    }
    $destination = Join-Path $stage "Contents/$folder"
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($name in 'NlcsLegenda.dll', 'NlcsLegenda.Core.dll', 'NlcsLegenda.Update.dll') {
        $file = Join-Path $destination $name
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $file
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($file)
        if ("$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)" -ne $Version) { throw "Verkeerde bestandsversie: $file" }
        if ($PfxPath -and !$BundleDirectory) {
            & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path $file -PfxPath $PfxPath -PfxPassword $PfxPassword -TimestampUrl $TimestampUrl
        }
    }
}
if (!$UpdaterPath) {
    & (Join-Path $PSScriptRoot 'Build-Updater.ps1') -PfxPath $PfxPath -PfxPassword $PfxPassword -TimestampUrl $TimestampUrl
    $UpdaterPath = Join-Path $repoRoot 'artifacts/updater/NlcsLegenda.Updater.exe'
}
$updaterDirectory = Join-Path $stage 'Contents/Updater'
New-Item -ItemType Directory -Path $updaterDirectory -Force | Out-Null
Copy-Item -LiteralPath $UpdaterPath -Destination $updaterDirectory
$helper = Join-Path $updaterDirectory 'NlcsLegenda.Updater.exe'
$helperVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($helper)
if ("$($helperVersion.FileMajorPart).$($helperVersion.FileMinorPart).$($helperVersion.FileBuildPart)" -ne $Version) { throw 'Updateprogramma heeft een andere versie.' }
if ($BundleDirectory) {
    $helperHash = (Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash
    foreach ($year in 2025, 2026, 2027) {
        $bundledHelper = Join-Path $BundleDirectory "$year/NlcsLegenda.bundle/Contents/Updater/NlcsLegenda.Updater.exe"
        if ((Get-FileHash -LiteralPath $bundledHelper -Algorithm SHA256).Hash -ne $helperHash) { throw "Updateprogramma verschilt van de bundel voor $year." }
    }
}
if ($PfxPath -or $BundleDirectory) {
    dotnet build (Join-Path $repoRoot 'src/NlcsLegenda.Update/NlcsLegenda.Update.csproj') -c Release
    if ($LASTEXITCODE) { throw 'Handtekeningcontrole niet gebouwd.' }
    $validator = Join-Path $repoRoot 'src/NlcsLegenda.Update/bin/Release/net8.0/NlcsLegenda.Update.dll'
    [void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($validator))
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $stage 'Contents') -Recurse -File) {
        [NlcsLegenda.Update.WindowsPackage]::VerifySignature($file.FullName)
    }
}

$helpDirectory = Join-Path $stage 'Contents/Help'
& (Join-Path $PSScriptRoot 'Build-Help.ps1') -OutputDirectory $helpDirectory
if ($BundleDirectory) {
    $helpFiles = @(Get-ChildItem -LiteralPath $helpDirectory -File | Sort-Object Name)
    foreach ($year in 2025, 2026, 2027) {
        $bundledHelp = Join-Path $BundleDirectory "$year/NlcsLegenda.bundle/Contents/Help"
        if (@(Get-ChildItem -LiteralPath $bundledHelp -File).Count -ne $helpFiles.Count) { throw "Hulpbestanden ontbreken in bundel $year." }
        foreach ($file in $helpFiles) {
            if ((Get-FileHash -LiteralPath (Join-Path $bundledHelp $file.Name)).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Handleiding verschilt van de bundel voor $year." }
        }
    }
}
[xml]$src = Get-Content $manifestSrc -Raw
$commandsXml = $src.ApplicationPackage.Components.ComponentEntry.Commands.InnerXml
$productCode = $src.ApplicationPackage.ProductCode

function New-Component([string]$series, [string]$path, [string]$dotnet) {
    @"
  <Components Description="NLCS Legenda ($dotnet)">
    <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="$($series.Split('-')[0])" SeriesMax="$($series.Split('-')[1])" SupportPath="./Contents/Help" />
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
                    Author="infracad-helper" HelpFile="./Contents/Help/NlcsLegenda.html">
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
$helpComponents = foreach ($file in Get-ChildItem -LiteralPath $helpDirectory -File | Sort-Object Name) {
    $digest = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("NlcsLegenda/Help/" + $file.Name.ToLowerInvariant()))
    $id = 'Help_' + [Convert]::ToHexString($digest).Substring(0, 24)
    $guid = [Guid]::new([byte[]]$digest[0..15]).ToString('B')
    $sourcePath = [Security.SecurityElement]::Escape($file.FullName)
    '<Component Id="' + $id + '" Directory="HelpDir" Guid="' + $guid + '"><RegistryValue Root="HKCU" Key="Software\joligej\NlcsLegenda\HelpFiles" Name="' + $file.Name + '" Type="integer" Value="1" KeyPath="yes" /><File Source="' + $sourcePath + '" /></Component>'
}
$helpWxs = Join-Path $stage 'Help.wxs'
Set-Content -LiteralPath $helpWxs -Value ('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment><ComponentGroup Id="OfflineHelpFiles">' + ($helpComponents -join "`n") + '</ComponentGroup></Fragment></Wix>') -Encoding utf8
wix build $wxs $helpWxs -arch x64 -d "Version=$Version" -d "BundleStage=$stage" -o $msi
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $msi)) { throw "MSI is niet gebouwd." }

if ($PfxPath) {
    & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path $msi -PfxPath $PfxPath -PfxPassword $PfxPassword -TimestampUrl $TimestampUrl
}
Write-Host "==> Klaar: $msi" -ForegroundColor Green
