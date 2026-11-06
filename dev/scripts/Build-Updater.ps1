[CmdletBinding()]
param([string]$PfxPath, [System.Security.SecureString]$PfxPassword, [string]$TimestampUrl='http://timestamp.digicert.com')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$destination=Join-Path $repo 'artifacts/updater'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
dotnet publish (Join-Path $repo 'src/NlcsLegenda.Updater/NlcsLegenda.Updater.csproj') -c Release -o $destination
if($LASTEXITCODE){throw 'Updateprogramma niet gebouwd.'}
$exe=Join-Path $destination 'NlcsLegenda.Updater.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Updateprogramma ontbreekt.'}
if($PfxPath){ & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path $exe -PfxPath $PfxPath -PfxPassword $PfxPassword -TimestampUrl $TimestampUrl }
Write-Output $exe
