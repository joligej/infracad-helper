<#
.SYNOPSIS
    Maakt een self-signed codesign-certificaat (standaard CN=joligej) en exporteert het.

.DESCRIPTION
    Een self-signed certificaat geeft de installer een vaste, controleerbare uitgever en
    bewijst dat het bestand niet is gewijzigd. Windows/SmartScreen "vertrouwt" het pas als
    iemand het publieke .cer-bestand importeert in "Vertrouwde basiscertificeringsinstanties"
    en "Vertrouwde uitgevers"; anders blijft de melding "onbekende uitgever" staan. Voor een
    volledig vertrouwde handtekening is een certificaat van een echte CA (betaald) nodig.

    Er worden drie bestanden weggeschreven:
      - <naam>.pfx  : certificaat MET privésleutel (geheim; nooit committen)
      - <naam>.cer  : alleen het publieke certificaat (mag je delen/committen)
      - <naam>.pfx.b64 : de pfx als base64 voor een GitHub Actions-secret

    Voor het ondertekenen in CI: zet de inhoud van .pfx.b64 als secret CODESIGN_PFX_BASE64
    en het wachtwoord als CODESIGN_PFX_PASSWORD.

.EXAMPLE
    ./New-CodeSigningCertificate.ps1 -Password (Read-Host "Wachtwoord" -AsSecureString)
#>
[CmdletBinding()]
param(
    [string]$Subject = "CN=joligej",
    [Parameter(Mandatory = $true)][System.Security.SecureString]$Password,
    [string]$OutDir = (Join-Path $PSScriptRoot "..\signing"),
    [string]$Name = "joligej-codesign",
    [int]$Years = 5
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$pfx = Join-Path $OutDir "$Name.pfx"
$cer = Join-Path $OutDir "$Name.cer"
$b64 = Join-Path $OutDir "$Name.pfx.b64"

Write-Host "==> Self-signed codesign-certificaat maken ($Subject)..." -ForegroundColor Cyan
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation Cert:\CurrentUser\My `
    -KeyExportPolicy Exportable `
    -KeyUsage DigitalSignature `
    -KeySpec Signature `
    -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears($Years)

Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $Password | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
[IO.File]::WriteAllText($b64, [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx)))

# De cert weer uit de gebruikersstore halen; we werken verder met de bestanden.
Remove-Item ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -ErrorAction SilentlyContinue

Write-Host "==> Klaar." -ForegroundColor Green
Write-Host "    PFX (privé, niet committen): $pfx"
Write-Host "    CER (publiek, mag je delen):  $cer"
Write-Host "    Base64 voor CI-secret:        $b64"
Write-Host "    Vingerafdruk (SHA1):          $($cert.Thumbprint)"
Write-Host ""
Write-Host "    Bij rotatie: werk de CI-secrets CODESIGN_PFX_BASE64 en" -ForegroundColor Yellow
Write-Host "    CODESIGN_PFX_PASSWORD bij, en vervang deploy\joligej-codesign.cer" -ForegroundColor Yellow
Write-Host "    door dit nieuwe .cer, zodat het publieke certificaat blijft kloppen." -ForegroundColor Yellow
