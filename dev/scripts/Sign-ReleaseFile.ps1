[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][string]$PfxPath,[System.Security.SecureString]$PfxPassword,[string]$TimestampUrl='http://timestamp.digicert.com')
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$signtool=Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue | Where-Object FullName -Match '\\x64\\' | Sort-Object FullName | Select-Object -Last 1
if(!$signtool){throw 'SignTool ontbreekt.'}
$plain=''
try {
 if($PfxPassword){$pointer=[Runtime.InteropServices.Marshal]::SecureStringToBSTR($PfxPassword);try{$plain=[Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)}finally{[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)}}
 & $signtool.FullName sign /fd SHA256 /f $PfxPath /p $plain /tr $TimestampUrl /td SHA256 $Path
 if($LASTEXITCODE){throw 'Ondertekenen mislukt.'}
} finally {$plain=''}
$assembly=Join-Path $repo 'src/NlcsLegenda.Update/bin/Release/net8.0/NlcsLegenda.Update.dll'
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($assembly))
[NlcsLegenda.Update.WindowsPackage]::VerifySignature((Resolve-Path -LiteralPath $Path).Path)
Write-Output 'Bestandsinhoud en vaste ondertekenaar gecontroleerd.'
