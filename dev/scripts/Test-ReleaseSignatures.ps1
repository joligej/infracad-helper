[CmdletBinding()]
param([Parameter(Mandatory)][string]$ContentsDirectory, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo ('artifacts/signatures/' + [Guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Gebruik een nieuwe testmap.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$validator = Join-Path $repo 'src/NlcsLegenda.Update/bin/Release/net8.0/NlcsLegenda.Update.dll'
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($validator))
$files = @('NlcsLegenda.dll', 'NlcsLegenda.Core.dll', 'NlcsLegenda.Update.dll', 'Updater/NlcsLegenda.Updater.exe')
foreach ($name in $files) {
    [NlcsLegenda.Update.WindowsPackage]::VerifySignature((Join-Path $ContentsDirectory $name))
}
$bytes = [IO.File]::ReadAllBytes((Join-Path $ContentsDirectory 'NlcsLegenda.dll'))
$metadata = -1
for ($index = 0; $index -le $bytes.Length - 4; $index++) {
    if ($bytes[$index] -eq 66 -and $bytes[$index+1] -eq 83 -and $bytes[$index+2] -eq 74 -and $bytes[$index+3] -eq 66) { $metadata = $index; break }
}
if ($metadata -lt 0) { throw 'Geen .NET-metadata gevonden voor de beschadigingsproef.' }
$bytes[$metadata] = $bytes[$metadata] -bxor 1
$changed = Join-Path $OutputDirectory 'changed.dll'
[IO.File]::WriteAllBytes($changed, $bytes)
$rejected = $false
try { [NlcsLegenda.Update.WindowsPackage]::VerifySignature($changed) } catch { $rejected = $true }
if (!$rejected) { throw 'Gewijzigde DLL geaccepteerd.' }
@{ signatures = $files.Count; changedDllRejected = $rejected } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'result.json')
Get-Content -LiteralPath (Join-Path $OutputDirectory 'result.json')
