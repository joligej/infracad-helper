[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('artifacts/installer-rollback/'+[Guid]::NewGuid().ToString('N'))}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Gebruik een nieuwe testmap.'}
$work=[IO.Path]::GetFullPath($OutputDirectory)
if(!$work.StartsWith((Join-Path $repo 'artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Testmap moet onder artifacts staan.'}
New-Item -ItemType Directory -Path $work -Force | Out-Null
[xml]$source=Get-Content -LiteralPath (Join-Path $repo 'deploy/installer/Package.wxs') -Raw
$upgrade=$source.Wix.Package.MajorUpgrade.OuterXml
if($source.Wix.Package.MajorUpgrade.Schedule -ne 'afterInstallInitialize'){throw 'De productie-installer mist herstel van de vorige versie.'}
$id=[Guid]::NewGuid().ToString('N');$upgradeCode=[Guid]::NewGuid().ToString('B');$component=[Guid]::NewGuid().ToString('B')
$products=@{old=[Guid]::NewGuid().ToString('B');failure=[Guid]::NewGuid().ToString('B');new=[Guid]::NewGuid().ToString('B')}
$target=Join-Path $env:LOCALAPPDATA "NlcsLegenda/InstallerTests/$id/value.dll"
function Build-TestPackage([string]$name,[string]$version,[bool]$fail){
 $payload=Join-Path $work "$name.dll"
 $project=Join-Path $work ($name+'-build');New-Item -ItemType Directory -Path $project | Out-Null
 $projectXml='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyName>Payload</AssemblyName><Version>'+$version+'</Version></PropertyGroup></Project>'
 Set-Content -LiteralPath (Join-Path $project 'Payload.csproj') -Value $projectXml
 Set-Content -LiteralPath (Join-Path $project 'Value.cs') -Value ('public static class Marker { public const string Value = "'+$name+'"; }')
 dotnet build (Join-Path $project 'Payload.csproj') -c Release --nologo -v quiet
 if($LASTEXITCODE){throw 'Proefbestand niet gebouwd.'}
 Copy-Item -LiteralPath (Join-Path $project 'bin/Release/net8.0/Payload.dll') -Destination $payload
 $extra=if($fail){'<CustomAction Id="FailAfterFiles" Directory="Target" ExeCommand="&quot;[SystemFolder]WindowsPowerShell\v1.0\powershell.exe&quot; -NoProfile -NonInteractive -WindowStyle Hidden -Command &quot;exit 1&quot;" Execute="deferred" Impersonate="yes" Return="check" /><InstallExecuteSequence><Custom Action="FailAfterFiles" After="InstallFiles" Condition="NLCS_TEST_FAIL = 1 AND NOT REMOVE" /></InstallExecuteSequence>'}else{''}
 $xml=@"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Package Name="NLCS installerproef $id" Manufacturer="NLCS test" Version="$version" Scope="perUser" ProductCode="$($products[$name])" UpgradeCode="$upgradeCode" Compressed="yes">
$upgrade<MediaTemplate EmbedCab="yes" />
<StandardDirectory Id="LocalAppDataFolder"><Directory Id="Root" Name="NlcsLegenda"><Directory Id="Tests" Name="InstallerTests"><Directory Id="Target" Name="$id" /></Directory></Directory></StandardDirectory>
<Feature Id="Main"><Component Directory="Target" Guid="$component"><RegistryValue Root="HKCU" Key="Software\NlcsLegendaInstallerTests\$id" Name="Installed" Type="integer" Value="1" KeyPath="yes" /><File Source="$([Security.SecurityElement]::Escape($payload))" Id="Payload" Name="value.dll" /></Component></Feature>
$extra</Package></Wix>
"@
 $file=Join-Path $work "$name.wxs"; Set-Content -LiteralPath $file -Value $xml
 & wix build $file -arch x64 -o (Join-Path $work "$name.msi")
 if($LASTEXITCODE){throw "Testinstaller $name niet gebouwd."}
}
function Install-TestPackage([string]$name){
 $arguments=@('/i',('"'+(Join-Path $work "$name.msi")+'"'),'/qn','/norestart','MSIRESTARTMANAGERCONTROL=Disable','/l*v',('"'+(Join-Path $work "$name.log")+'"'))
 if($name -eq 'failure'){$arguments += 'NLCS_TEST_FAIL=1'}
 $process=Start-Process (Join-Path $env:SystemRoot 'System32/msiexec.exe') -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
 return $process.ExitCode
}
try {
 Build-TestPackage old '1.0.0' $false
 Build-TestPackage failure '2.0.0' $true
 Build-TestPackage new '2.0.0' $false
 if((Install-TestPackage old) -ne 0 -or (Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath (Join-Path $work 'old.dll')).Hash){throw 'Oude proefversie ontbreekt.'}
 $failure=Install-TestPackage failure
 if($failure -ne 1603 -or !(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath (Join-Path $work 'old.dll')).Hash){throw 'Oude versie niet teruggezet na bewuste installatiefout.'}
 if((Install-TestPackage new) -ne 0 -or (Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath (Join-Path $work 'new.dll')).Hash){throw 'Gewone upgrade niet voltooid.'}
 @{passed=$true;rollbackExit=$failure;oldRestored=$true;upgradeSucceeded=$true;isolatedProduct=$id} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $work 'result.json')
 Write-Output 'INSTALLER PASS: mislukte upgrade zet oude versie terug; volgende upgrade slaagt.'
} finally {
 foreach($product in $products.Values){$process=Start-Process (Join-Path $env:SystemRoot 'System32/msiexec.exe') -ArgumentList @('/x',$product,'/qn','/norestart') -WindowStyle Hidden -Wait -PassThru;if($process.ExitCode -notin @(0,1605)){throw "Opruimen proefinstaller mislukt: $($process.ExitCode)"}}
}
