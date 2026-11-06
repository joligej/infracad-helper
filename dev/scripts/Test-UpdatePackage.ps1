[CmdletBinding()]
param([Parameter(Mandatory)][string]$SignedMsi,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/package-check/'+[Guid]::NewGuid().ToString('N'))}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Gebruik een nieuwe testmap.'}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $repo 'src/NlcsLegenda.Update/bin/Release/net8.0/NlcsLegenda.Update.dll')))
$msi=(Resolve-Path -LiteralPath $SignedMsi).Path
[NlcsLegenda.Update.WindowsPackage]::VerifySignature($msi)
$properties=[NlcsLegenda.Update.WindowsPackage]::ReadProperties($msi)
$expected=[NlcsLegenda.Update.ReleaseVersion]::FromAssembly([Version]$properties['ProductVersion'])
[NlcsLegenda.Update.WindowsPackage]::VerifyInstaller($msi,$expected,$null)
$changed=Join-Path $OutputDirectory 'changed.msi'; Copy-Item -LiteralPath $msi -Destination $changed
$installer=New-Object -ComObject WindowsInstaller.Installer;$database=$null;$view=$null
try {
 $database=$installer.GetType().InvokeMember('OpenDatabase','InvokeMethod',$null,$installer,@([IO.Path]::GetFullPath($changed),1))
 $query="UPDATE ``Property`` SET ``Value`` = 'Changed NLCS Legenda' WHERE ``Property`` = 'ProductName'"
 $view=$database.GetType().InvokeMember('OpenView','InvokeMethod',$null,$database,@($query))
 $null=$view.GetType().InvokeMember('Execute','InvokeMethod',$null,$view,$null)
 $null=$database.GetType().InvokeMember('Commit','InvokeMethod',$null,$database,$null)
} finally { foreach($object in @($view,$database,$installer)){if($null -ne $object){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($object)}} }
$rejected=$false
try{[NlcsLegenda.Update.WindowsPackage]::VerifySignature($changed)}catch{$rejected=$true}
if(!$rejected){throw 'Beschadigde installer geaccepteerd.'}
$wrongSigner=$false
try{[NlcsLegenda.Update.WindowsPackage]::VerifySignature((Join-Path $env:SystemRoot 'System32/msiexec.exe'))}catch{$wrongSigner=$true}
if(!$wrongSigner){throw 'Andere uitgever geaccepteerd.'}
$wrongVersion=$false
try{[NlcsLegenda.Update.WindowsPackage]::VerifyInstaller($msi,[NlcsLegenda.Update.ReleaseVersion]::new(0,0,0),$null)}catch{$wrongVersion=$true}
if(!$wrongVersion){throw 'Verkeerde versie geaccepteerd.'}
@{signedContentAccepted=$true;tamperedRejected=$rejected;wrongPublisherRejected=$wrongSigner;wrongVersionRejected=$wrongVersion;installed=[NlcsLegenda.Update.WindowsPackage]::InstalledVersion()} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'result.json')
Get-Content -LiteralPath (Join-Path $OutputDirectory 'result.json')
