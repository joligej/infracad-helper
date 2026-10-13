<#
.SYNOPSIS
  Controleert de echte plugin in AutoCAD Core Console op afzonderlijke testtekeningen.
.DESCRIPTION
  Slaagt alleen bij de verwachte testmeldingen, ongewijzigde systeemvariabelen,
  QSAVE met DBMOD=0 en afsluiting zonder fout. Bron-/templatebestanden blijven intact.
  Gebruik -PluginPath voor acceptatie van een publiek geïnstalleerde release.
#>
[CmdletBinding()]
param(
    [ValidateSet('2025','2026','2027')][string]$AcadVersion='2025',
    [string]$PluginPath,
    [string]$TemplatePath,
    [string]$OutputDirectory,
    [string[]]$Cases=@('metadata','review','snapshot','exportreview','abclocal','abcxref','oldreopen','eigenvis','eigenbatch','properties','entitystyle','dependencies','sampleconflict','placement','render','nested','host','split','style','symbol','hatch','visibility','states','reload','custom','customxref','customany','customhatch','blank','sysvars','mtext','heights','symboltext','oldupdate','isolation','viewport','consumer','layout','paperscale','symboledge','polyownership'),
    [switch]$SkipReopen
)
$ErrorActionPreference='Stop'
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$framework=if($AcadVersion -eq '2027'){'net10.0-windows'}else{'net8.0-windows'}
if(!$PluginPath){$PluginPath=Join-Path $repoRoot "src/NlcsLegenda.Plugin/bin/Release/$framework/NlcsLegenda.dll"}
$PluginPath=(Resolve-Path -LiteralPath $PluginPath).Path
$pluginHash=(Get-FileHash -LiteralPath $PluginPath -Algorithm SHA256).Hash
if(!$TemplatePath){$TemplatePath=Join-Path $env:APPDATA "Autodesk/C3D $AcadVersion/enu/Template/AutoCAD Template/acad.dwt"}
$TemplatePath=(Resolve-Path -LiteralPath $TemplatePath).Path
$templateHash=(Get-FileHash -LiteralPath $TemplatePath -Algorithm SHA256).Hash
if(!$OutputDirectory){$OutputDirectory=Join-Path $repoRoot ('artifacts/acceptance/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+$AcadVersion)}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Kies een nieuwe uitvoermap; bestaande resultaten worden niet overschreven.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$core="C:/Program Files/Autodesk/AutoCAD $AcadVersion/accoreconsole.exe"
if(!(Test-Path -LiteralPath $core)){throw "Core Console $AcadVersion niet geïnstalleerd."}
$specs=@{
 metadata=@('NLCSLEGENDAMETADATATEST','METADATA vier onleesbare tekeningen afgewezen; geldige tekening 7m; bronbestanden ongewijzigd -> OK')
 abclocal=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDAABCSETUP",'ABC: setup klaar','NLCSLEGENDAABCVERIFY','ABC: na A bewerken B/C-vingerafdruk ongewijzigd')
 abcxref=@('NLCSLEGENDAABCXREFSETUP','ABCX: setup klaar','NLCSLEGENDAABCXREFVERIFY','ABCX: xref-isolatie A=OK B=OK C=OK -> OK')
 oldreopen=@('NLCSLEGENDAOLDREOPENSETUP','HEROPEN: setup klaar','NLCSLEGENDAOLDREOPENVERIFY','handles=2 pos=True orphan=0 -> OK')
 eigenvis=@('NLCSLEGENDAEIGENZICHTBAARTEST','(verw true/true) -> OK')
 eigenbatch=@('NLCSLEGENDAEIGENBATCHTEST','bron-hashes ongewijzigd=True -> OK')
 exportreview=@('NLCSLEGENDAEXPORTREVIEWTEST','EXPORTREVIEW eigen omschrijvingen en lege bronselectie -> OK')
 snapshot=@('NLCSLEGENDASNAPSHOTTEST','SNAPSHOT gewijzigde bron afgewezen; bestaande legenda behouden -> OK')
 review=@('NLCSLEGENDAREVIEWTEST','REVIEW totaal: OK (0 fouten)')
 polyownership=@('NLCSLEGENDAPOLYOWNERSHIPTEST','POLYOWNERSHIP 2D/3D 14m en bronvertices ongewijzigd na drie analyses -> OK')
 symboledge=@('NLCSLEGENDASYMBOLEDGETEST','SYMBOLEDGE leeg/verborgen/lijn/verschillende arceringscontouren -> OK')
 paperscale=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDAPAPERSCALETEST",'PAPERSCALE 1:1000 vakken24x5/tekst2.5/kop2.5/titel5/gap5.7/centrering/kader -> OK')
 properties=@('NLCSLEGENDAPROPERTYTEST','PROPERTY mislukte tekenregel: vorige legenda en registratie behouden -> OK')
 entitystyle=@('NLCSLEGENDAENTITYSTYLETEST','ENTITYSTYLE echte rode/groene swatches blijven gelijk na laagwijziging -> OK')
 dependencies=@('NLCSLEGENDADEPENDENCYTEST','DEPENDENCY SaveAs -> OK')
 sampleconflict=@('NLCSLEGENDAXREFSAMPLECONFLICTTEST','SAMPLECONFLICT totaal: OK')
 placement=@("NLCSLEGENDAXREFPLACEMENTTEST`n",'PLACEMENT totaal: OK')
 render=@('NLCSLEGENDAXREFRENDERTEST','XREFRENDER: totaal -> OK')
 nested=@('NLCSLEGENDAXREFNESTEDTEST','XREFNESTED: totaal -> OK')
 host=@('NLCSLEGENDAXREFHOSTTEST','HOSTXREF totaal: OK')
 split=@('NLCSLEGENDAXREFSPLITTEST','SPLIT: gebouwd regels=2 fout=geen')
 style=@('NLCSLEGENDAXREFSTYLETEST','STYLE: totaal -> OK')
 symbol=@('NLCSLEGENDAXREFSYMBOLTEST','SYMBOL totaal: OK')
 hatch=@('NLCSLEGENDAXREFHATCHTEST','HATCH totaal: OK')
 visibility=@('NLCSLEGENDAXREFVISIBILITYTEST','XREF visibility totaal: OK')
 states=@('NLCSLEGENDAXREFSTATESTEST','XREF status totaal: OK')
 reload=@('NLCSLEGENDAXREFRELOADSETUP','XREFRELOAD setup: eerst rood 5 m; plaatsing=1 fout=geen -> OK')
 custom=@('NLCSLEGENDACUSTOMTEST','CUSTOM: selectie (1e lijn) kabel=5.0m (verw 5) -> OK')
 customxref=@('NLCSLEGENDAEIGENXREFTEST','EIGENXREF: matrix -> OK')
 customany=@('NLCSLEGENDAEIGENANYTEST','EIGENANY: totaal -> OK')
 customhatch=@('NLCSLEGENDAEIGENHATCHTEST','EIGENHATCH: gerenderde hatches=')
 blank=@('NLCSLEGENDABLANCOTEST','BLANCO: totaal -> OK')
 sysvars=@('NLCSLEGENDASYSVARTEST','SYSVAR: 11 variabele(n) gecontroleerd, 0 gewijzigd -> OK')
 mtext=@('NLCSLEGENDAMTEXTTEST','MTEXT: totaal -> OK')
 heights=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDAHOOGTETEST",'HOOGTE: body=0.6(True) kop=0.8(True) titel=1.2(True) geen-default(True) -> OK')
 symboltext=@('NLCSLEGENDASYMBOOLTEKST2TEST','SYMFID: totaal -> OK')
 oldupdate=@('NLCSLEGENDAOLDUPDATEBROADTEST','BREED: totaal -> OK')
 isolation=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDAISOLATIETEST",'ISO: A.manual=1/status=1 B.manual=0 C.manual=0 -> OK')
 viewport=@("NLCSLEGENDAMTEXTTEST`nNLCSLEGENDAVIEWPORTTEST",'VPTEST: opmerkingzwaar 1:200')
 consumer=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDACONSUMERTEST",'CONSUMER: exploderen=')
 layout=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDAOPMAAKMEETTEST`nNLCSLEGENDAVOLOPMAAKTEST",'VOLOPMAAK: totaal -> OK')
}
# Controleer testcommando's voor de eerste CAD-start; ongeldige scriptinvoer mag nooit doorlopen.
$sourceText=(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/NlcsLegenda.Plugin') -Filter 'Commands*.cs' | Get-Content -Raw) -join "`n"
foreach($case in $Cases){
 if(!$specs.ContainsKey($case)){throw "Onbekende test: $case"}
 foreach($command in ($specs[$case][0] -split "`n" | Where-Object {$_})){
  if(!$sourceText.Contains('[CommandMethod("'+$command+'"')){throw "Onbekend testcommando: $command"}
 }
}
$results=[Collections.Generic.List[object]]::new()
function Run-Case([string]$Name,[string]$Drawing,[string]$Commands,[string[]]$Expected){
 $dir=Split-Path $Drawing
 $script=Join-Path $dir "$Name.scr"
 $dll=$PluginPath.Replace('\','/')
 @"
(setq auditF (getvar "FILEDIA") auditS (getvar "SECURELOAD") auditC (getvar "CTAB"))
(setvar "SECURELOAD" 0)
(command "_.NETLOAD" "$dll")
(setvar "SECURELOAD" auditS)
NLCSLEGENDAAUDITVERSION
$Commands
_.QSAVE
(princ (strcat "\nAUDIT_DBMOD=" (itoa (getvar "DBMOD"))))
(princ (strcat "\nAUDIT_SYSVARS=" (if (and (= auditF (getvar "FILEDIA")) (= auditS (getvar "SECURELOAD")) (= auditC (getvar "CTAB"))) "OK" "FAIL")))
(princ "\nAUDIT_SCRIPT_END\n")
_.QUIT
_Y
"@ | Set-Content -LiteralPath $script -Encoding ascii
 $raw=Join-Path $dir "$Name.raw.log"
 $arguments=@('/i', ('"{0}"' -f $Drawing), '/s', ('"{0}"' -f $script), '/l', 'en-US')
 $process=Start-Process -FilePath $core -ArgumentList $arguments -WorkingDirectory $dir -PassThru -WindowStyle Hidden -RedirectStandardOutput $raw -RedirectStandardError "$raw.stderr"
 try {
  if(!$process.WaitForExit(300000)){
   $process.Kill()
   $process.WaitForExit()
   throw "Test $Name overschrijdt vijf minuten; uitsluitend dit testproces is gestopt. Log: $raw"
  }
  $exitCode=$process.ExitCode
 } finally { $process.Dispose() }
 $log=(Get-Content -LiteralPath $raw -Raw) -replace "`0",''
 $log | Set-Content -LiteralPath (Join-Path $dir "$Name.log") -Encoding utf8
 $actual=($log -split "`r?`n" | Where-Object {$_ -notmatch '^Command:'}) -join "`n"
 $failures=[Collections.Generic.List[string]]::new()
 if($exitCode -ne 0){$failures.Add("Procescode $exitCode")}
 foreach($marker in $Expected + @("SHA256=$pluginHash",'AUDIT_DBMOD=0','AUDIT_SYSVARS=OK',"`nAUDIT_SCRIPT_END")){
  if(!$actual.Contains($marker)){$failures.Add("Verwachte melding ontbreekt: $marker")}
 }
 if($actual -match '(?im)^.*(?:->\s*FAIL|totaal:?\s*(?:->\s*)?FAIL|\bFAIL:|fatale fout|\berror:|\bfout:|Unknown command|Invalid option keyword|An error occurred during save|overgeslagen|GEEN VERSCHIL|host zonder NLCS-content|geen papier-layout).*$'){
  $failures.Add($Matches[0])
 }
 $result=[pscustomobject]@{Case=$Name;Passed=($failures.Count -eq 0);ExitCode=$exitCode;Failures=@($failures);Log=(Join-Path $dir "$Name.log");PluginHash=$pluginHash}
 $results.Add($result)
 $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.json') -Encoding utf8
 Write-Output "$Name : $(if($result.Passed){'PASS'}else{'FAIL'})"
 if(!$result.Passed){throw ($failures -join "`n")}
}
foreach($case in $Cases){
 if(!$specs.ContainsKey($case)){throw "Onbekende test: $case"}
 $dir=Join-Path $OutputDirectory $case
 New-Item -ItemType Directory -Path $dir | Out-Null
 $drawing=Join-Path $dir 'host.dwg'
 Copy-Item -LiteralPath $TemplatePath -Destination $drawing
 Run-Case $case $drawing $specs[$case][0] @($specs[$case][1])
 if($case -in @('abclocal','abcxref','oldreopen')){
  $expected=@($specs[$case][3])
  if($case -eq 'abclocal'){$expected+='ABC: viewport A=OK B=OK C=OK'}
  Run-Case "$case-verify" $drawing $specs[$case][2] $expected
 }
 if($case -eq 'snapshot'){
  Run-Case 'snapshot-verify' $drawing "NLCSLEGENDASNAPSHOTVERIFY`nNLCSLEGENDASTABILITYTEST" @('SNAPSHOTVERIFY herladen bron; nieuwe symboolvorm -> OK','STABILITY updates=50','STABILITY orphan blocks=0 -> OK')
 }
 if($case -eq 'reload'){
  Copy-Item -LiteralPath (Join-Path $dir 'reload_replacement.dwg') -Destination (Join-Path $dir 'reload_source.dwg') -Force
  Run-Case 'reload-verify' $drawing 'NLCSLEGENDAXREFRELOADVERIFY' @('XREFRELOAD totaal: OK')
 }
 if(!$SkipReopen -and $case -in @('properties','entitystyle','dependencies','sampleconflict','placement','render','nested','hatch','oldupdate','mtext')){
  $commands='NLCSLEGENDASTABILITYTEST'
  $expected=@('STABILITY updates=10','STABILITY updates=50','STABILITY orphan blocks=0 -> OK')
  if($case -eq 'placement'){
   $commands="NLCSLEGENDAXREFPLACEMENTVERIFY`n$commands"
   $expected+= 'PLACEMENTVERIFY totaal: OK'
  }
  Run-Case "$case-reopen" $drawing $commands $expected
 }
}
if((Get-FileHash -LiteralPath $TemplatePath -Algorithm SHA256).Hash -ne $templateHash){throw 'Templatebron gewijzigd.'}
Write-Output "ACCEPTANCE PASS: $($results.Count) runs; bron ongewijzigd; resultaten in $OutputDirectory"
