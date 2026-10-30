<#
.synopsis
    cad-acceptatie met bronbehoud en optioneel een geïsoleerd profiel
#>
[CmdletBinding()]
param(
    [ValidateSet('2025','2026','2027')][string]$AcadVersion='2025',
    [string]$PluginPath,
    [string]$TemplatePath,
    [string]$ReferenceTemplatePath,
    [string]$OutputDirectory,
    [string[]]$Cases=@('linkedexamples','managed','projectlegends','linetypes','continuity','mergeregression','viewportregression','xrefchoiceregression','examplerevision','exampleappearance','colorcomposition','choices','resources','presettarget','settingregression','frameregression','hatcharearegression','templateregression','examples','sources','xrefchoices','klic','partialklic','heightlimit','drawingdefaults','exportisolation','batchxref','customization','overview','realsize','templatecontract','templatestyle','scalevariant','livetarget','liveempty','metadata','globalsettings','layermerge','composition','review','snapshot','exportreview','abclocal','abcxref','oldreopen','eigenvis','eigenbatch','properties','entitystyle','dependencies','sampleconflict','placement','render','nested','host','split','style','symbol','hatch','visibility','states','reload','custom','customxref','customany','customhatch','blank','sysvars','mtext','heights','symboltext','oldupdate','isolation','viewport','consumer','layout','paperscale','symboledge','polyownership'),
    [switch]$SkipReopen,
    [switch]$Isolated
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CoreConsole.ps1')
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$framework=if($AcadVersion -eq '2027'){'net10.0-windows'}else{'net8.0-windows'}
if(!$PluginPath){$PluginPath=Join-Path $repoRoot "src/NlcsLegenda.Plugin/bin/Release/$framework/NlcsLegenda.dll"}
$PluginPath=(Resolve-Path -LiteralPath $PluginPath).Path
$pluginHash=(Get-FileHash -LiteralPath $PluginPath -Algorithm SHA256).Hash
if(!$TemplatePath){$TemplatePath=Join-Path $env:APPDATA "Autodesk/C3D $AcadVersion/enu/Template/AutoCAD Template/acad.dwt"}
$TemplatePath=(Resolve-Path -LiteralPath $TemplatePath).Path
function Get-TemplateHash {
 $stream=[IO.File]::Open($TemplatePath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
 try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
 finally { $stream.Dispose() }
}
$templateHash=Get-TemplateHash
if(!$OutputDirectory){$OutputDirectory=Join-Path $repoRoot ('artifacts/acceptance/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+$AcadVersion)}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $OutputDirectory){throw 'Kies een nieuwe uitvoermap; bestaande resultaten worden niet overschreven.'}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$core="C:/Program Files/Autodesk/AutoCAD $AcadVersion/accoreconsole.exe"
if(!(Test-Path -LiteralPath $core)){throw "Core Console $AcadVersion niet geïnstalleerd."}
$specs=@{
 linkedexamples=@('NLCSLEGENDALINKEDXREFTEST','LINKEDXREF zelfstandige lijn en arcering, actuele kaderkleuren en bronbestand behouden -> OK')
 managed=@('NLCSLEGENDAMANAGEDTEST','MANAGED vaste samenstelling, bronkleuren, niet-plotten, model/papier en gekoppelde eigen regels -> OK','NLCSLEGENDAMANAGEDVERIFY','MANAGEDVERIFY vaste keuzes en broncontrole na heropenen -> OK')
 projectlegends=@('NLCSLEGENDAPROJECTTEST','PROJECTLEGENDS kaartgebied, bronredenen, laagweergave, leeg herstel en handmatige layoutregel -> OK','NLCSLEGENDAPROJECTVERIFY','PROJECTVERIFY kaartkeuze en handmatig voorbeeld na heropenen -> OK')
 linetypes=@('NLCSLEGENDALINETYPETEST','LINETYPES nieuw opgebouwde streep-, tekst- en shapepatronen zelfstandig -> OK',"_.AUDIT`n_Y`nNLCSLEGENDALINETYPEVERIFY`n_.AUDIT`n_Y`nNLCSLEGENDALINETYPEVERIFY",'LINETYPEVERIFY patroon, kleur en lijndikte na audit, heropenen en nieuwe varianten -> OK')
 continuityreal=@('NLCSLEGENDACONTINUITYREAL','CONTINUITYREAL herhaalde update, teksten, eigen regels en vensters -> OK')
 continuity=@('NLCSLEGENDACONTINUITYTEST','CONTINUITY kleurconvergentie, uitsluiting, eigen tekst, ontbrekende bron en eigen hoogte -> OK','NLCSLEGENDACONTINUITYVERIFY','CONTINUITYVERIFY opgeslagen regels en voorbeelden na heropenen -> OK')
 sourceinspection=@('NLCSLEGENDASOURCEINSPECTION','SOURCEINSPECTION -> OK')
 mergeregression=@('NLCSLEGENDAMERGEREGRESSION','MERGEREGRESSION lijnschaal, arceerschaal/hoek, kleur/patroon, hoeveelheden, uitsluiting en herstel -> OK','NLCSLEGENDAMERGEVERIFY','MERGEREGRESSION na heropenen: keuzes en bijwerken behouden -> OK')
 viewportregression=@('NLCSLEGENDAVIEWPORTREGRESSION','VIEWPORTREGRESSION ankers, lagen, handmatig kader en schaal op 200/500/1000 -> OK')
 xrefchoiceregression=@('NLCSLEGENDAXREFCHOICEREGRESSION','XREFCHOICEREGRESSION alleen-lezen/vergrendeld, eigen symbool, grootte en normalisatie -> OK')
 choices=@('NLCSLEGENDACHOICESTEST','CHOICES klikken op bron/symbool/rij/groep, schaalvast uitsluiten, leeg bijwerken en resetten -> OK','NLCSLEGENDACHOICESVERIFY','CHOICES na heropenen: instellingen, uitsluitingen en aanklikbare rijen behouden -> OK')
 examplerevision=@('NLCSLEGENDAEXAMPLEREVISIONTEST','EXAMPLEREVISION nieuwe bron toegepast, oude legenda gelijk en hergebruik zonder bron -> OK','NLCSLEGENDAEXAMPLEREVISIONVERIFY','EXAMPLEREVISION na heropenen: nieuwste voorbeeld en oude legenda behouden -> OK')
 exampleappearance=@('NLCSLEGENDAEXAMPLEAPPEARANCETEST','EXAMPLEAPPEARANCE bronvorm, maat en achtergrondkleur behouden; expliciete keuze toegepast -> OK')
 colorcomposition=@('NLCSLEGENDACOLORCOMPOSITIONTEST','COLORCOMPOSITION kleuren 1/3/5, kleurselectie, TAUW 24x5 mm, laagkleur en gekoppelde schaal -> OK')
 resources=@('NLCSLEGENDARESOURCETEST','RESOURCE losse objecten vrij, transactieobject behouden, dubbel opruimen veilig -> OK')
 presettarget=@("NLCSLEGENDAPRESETTARGETTEST`nStandaard`nLegenda`nStandaard",'PRESETTARGET globale en lokale profielen, zonder en met legenda, gescheiden -> OK')
 hatcharearegression=@('NLCSLEGENDAHATCHAREAREGRESSION','HATCHAREAREGRESSION eilanden en gaten, drie stijlen en bron behouden -> OK')
 settingregression=@('NLCSLEGENDASETTINGREGRESSION','SETTINGREGRESSION lokale A direct getekend, B en globale standaard gescheiden, fouten teruggedraaid -> OK')
 frameregression=@('NLCSLEGENDAFRAMEREGRESSION','FRAMEREGRESSION breed symbool, gevuld kader en lange tekst zonder botsing -> OK','NLCSLEGENDAFRAMEVERIFY','FRAMEREGRESSION na heropenen -> OK')
 templateregression=@('NLCSLEGENDATEMPLATEREGRESSIONTEST','TEMPLATEREGRESSION lijnbreedte, losse symboolcurve, cirkel en associatieve arcering; bron behouden -> OK')
 examples=@('NLCSLEGENDAEXAMPLETEST','EXAMPLE bibliotheekimport en bronmaat 1:200 naar 1:500 -> OK','NLCSLEGENDAEXAMPLEVERIFY','EXAMPLE na heropenen: gekozen voorbeeld zonder bronbibliotheek behouden -> OK')
 band=@('NLCSLEGENDABANDTEST','BAND drie DWT-varianten, vier lijnen, stijlen en drie schalen lokaal/xref -> OK','NLCSLEGENDABANDVERIFY','BAND na heropenen -> OK')
 appearance=@('NLCSLEGENDAAPPEARANCETEST','APPEARANCE ingesloten patronen, gesloten vulling, A01/A02 en tekenvolgorde lokaal/xref -> OK','NLCSLEGENDAAPPEARANCEVERIFY','APPEARANCE na heropenen -> OK')
 sources=@('NLCSLEGENDASOURCETEST','SOURCETEST gedeelde symbolen, xref-overrides en gewone omschrijvingen -> OK','NLCSLEGENDASOURCEVERIFY','SOURCETEST na heropenen -> OK')
 xrefchoices=@('NLCSLEGENDAXREFCHOICETEST','XREFCHOICE xref-inserts, opgeslagen keuzes en geneste KLIC-bronnen -> OK','NLCSLEGENDAXREFCHOICEVERIFY','XREFCHOICE na heropenen: bronkeuzes en KLIC-hoeveelheden -> OK')
 klic=@('NLCSLEGENDAKLICTEST','KLIC lokale en geneste xrefs: 4 netbeheerders, onbekend, 30m, samenvoegen en uitsluiten -> OK','NLCSLEGENDAKLICVERIFY','KLIC na heropenen: netbeheerders en 30m behouden -> OK')
 partialklic=@('NLCSLEGENDAPARTIALKLICTEST','PARTIALKLIC lokaal en xref: ontbrekende gegevens zichtbaar, 8/16m behouden -> OK')
 exportisolation=@('NLCSLEGENDAEXPORTISOLATIONTEST','EXPORTISOLATION opgeslagen legenda exporteert met onleesbare globale instellingen -> OK')
 heightlimit=@('NLCSLEGENDAHEIGHTLIMITTEST','HEIGHTLIMIT koppen, notities, schaalbalk en kader binnen 100/120mm op drie schalen -> OK')
 drawingdefaults=@('NLCSLEGENDADRAWINGDEFAULTSTEST','DRAWINGDEFAULTS legenda volgt eigen lagen; actieve tekeninstellingen behouden -> OK')
 batchxref=@('NLCSLEGENDABATCHXREFTEST','BATCHXREF relatieve geneste xref 30m, uitgesloten en ontladen 0m, bestanden ongewijzigd -> OK')
 customization=@('NLCSLEGENDACUSTOMIZATIONTEST','CUSTOMIZATION eigen arceringen en symbolen onafhankelijk van dezelfde bronlaag -> OK','NLCSLEGENDACUSTOMIZATIONVERIFY','CUSTOMIZATION na heropenen en bijwerken: eigen voorbeelden behouden -> OK')
 overview=@('NLCSLEGENDAOVERVIEWTEST','OVERVIEW handmatige volgorde, echte geometrie, B-isolatie en rollback -> OK','NLCSLEGENDAOVERVIEWVERIFY','OVERVIEW na heropenen en bijwerken: C/B/A, B gelijk -> OK')
 realsize=@('NLCSLEGENDAREALSIZETEST','REALSIZE grote xref-symbolen: modelmaat, schaalvariant, samenvoeging en vrije tekstruimte -> OK')
 templatestyle=@('NLCSLEGENDATEMPLATESTYLETEST','TEMPLATESTYLE standaardfont, bestaande stijl behouden, eigen keuze en ontbrekende stijl -> OK')
 templatefile=@('NLCSLEGENDATEMPLATEFILETEST','TEMPLATEFILE echte DWT: tekst, stijl, posities op drie schalen en meerregelige omschrijving -> OK')
 templatecontract=@('NLCSLEGENDATEMPLATECONTRACTTEST','TEMPLATECONTRACT DWT-meetpunten, lege hoststijl, notities en schaalbalk op 1:200/500/1000 -> OK')
 scalevariant=@('NLCSLEGENDASCALEVARIANTTEST','SCALEVARIANT xref 1:200/500 naar 1:200/500/1000: symbool 4mm, patroon 1mm, bron ongewijzigd -> OK')
 livetarget=@("NLCSLEGENDALIVETARGETTEST`nNLCSLEGENDALIVETARGETERASE`n_.U`nNLCSLEGENDALIVETARGETVERIFY",'LIVETARGET UNDO: A/B bereikbaar; metadata behouden -> OK')
 liveempty=@('NLCSLEGENDALIVEEMPTYTEST','LIVEEMPTY geen actieve legenda: bronexport 5m, metadata behouden -> OK')
 globalsettings=@('NLCSLEGENDAGLOBALSETTINGSTEST','GLOBALSETTINGS totaal: OK (0 fouten)')
 layermerge=@('NLCSLEGENDALAYERMERGETEST','LAYERMERGE totaal: OK (0 fouten)')
 composition=@('NLCSLEGENDACOMPOSITIONTEST','COMPOSITION totaal: OK (0 fouten)')
 metadata=@('NLCSLEGENDAMETADATATEST','METADATA 12 onleesbare tekeningen afgewezen; geldige tekening 7m; bronbestanden ongewijzigd -> OK')
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
 paperscale=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDAPAPERSCALETEST",'PAPERSCALE 1:1000 vakken24x5/tekst2.5/kop2.5/titel5/gap5.7188/basislijn/kader -> OK')
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
 sysvars=@('NLCSLEGENDASYSVARTEST','SYSVAR totaal: OK; 15 variabelen; 34 controles; FILEDIA=0/1')
 mtext=@('NLCSLEGENDAMTEXTTEST','MTEXT: totaal -> OK')
 heights=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDAHOOGTETEST",'HOOGTE: body=0.6(True) kop=0.8(True) titel=1.2(True) geen-default(True) -> OK')
 symboltext=@('NLCSLEGENDASYMBOOLTEKST2TEST','SYMFID: totaal -> OK')
 oldupdate=@('NLCSLEGENDAOLDUPDATEBROADTEST','BREED: totaal -> OK')
 isolation=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDAISOLATIETEST",'ISO: A.manual=1/status=1 B.manual=0 C.manual=0 -> OK')
 viewport=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDAMTEXTTEST`nNLCSLEGENDAVIEWPORTTEST",'VPTEST totaal: 14 controles -> OK')
 consumer=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDACONSUMERTEST",'CONSUMER: exploderen=')
 layout=@("NLCSLEGENDAAUDITFIXTURE`nNLCSLEGENDAOPMAAKMEETTEST`nNLCSLEGENDAVOLOPMAAKTEST",'VOLOPMAAK: totaal -> OK')
}
if($ReferenceTemplatePath -and !$PSBoundParameters.ContainsKey('Cases')){
 $Cases+=@('band','appearance','templatefile')
}
# testcommando controleren vóór de eerste cad-start
$sourceText=(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/NlcsLegenda.Plugin') -Filter 'Commands*.cs' | Get-Content -Raw) -join "`n"
foreach($case in $Cases){
 if(!$specs.ContainsKey($case)){throw "Onbekende test: $case"}
 foreach($command in ($specs[$case][0] -split "`n" | Where-Object {$_})){
  if($command.StartsWith('NLCS') -and !$sourceText.Contains('[CommandMethod("'+$command+'"')){throw "Onbekend testcommando: $command"}
 }
}
if('band' -in $Cases -or 'templatefile' -in $Cases -or 'appearance' -in $Cases){
 if(!$ReferenceTemplatePath -or !(Test-Path -LiteralPath $ReferenceTemplatePath -PathType Leaf)){throw 'templatefile vereist -ReferenceTemplatePath met de originele DWT.'}
 $ReferenceTemplatePath=(Resolve-Path -LiteralPath $ReferenceTemplatePath).Path
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
 $run=Invoke-NlcsCoreConsole -Executable $core -Drawing $Drawing -Script $script -Log $raw -Isolated:$Isolated -Environment @{NLCS_TEMPLATE_REFERENCE=$ReferenceTemplatePath}
 $exitCode=$run.ExitCode
 $log=(Get-Content -LiteralPath $raw -Raw -Encoding Unicode)
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
 if($Name.StartsWith('linetypes') -and $actual -match 'Total errors found [1-9]'){$failures.Add('AUDIT vindt fouten in een lijnvoorbeeld.')}
 $result=[pscustomobject]@{Case=$Name;Passed=($failures.Count -eq 0);ExitCode=$exitCode;Failures=@($failures);Log=(Join-Path $dir "$Name.log");PluginHash=$pluginHash;ProfileValuesChecked=$run.ProfileValuesChecked;Isolated=[bool]$Isolated}
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
 $expected=@($specs[$case][1])
 if($case -eq 'livetarget'){$expected+="1 legenda('s) in deze tekening:"}
 if($case -eq 'liveempty'){$expected+='Geen legenda om bij te werken. Plaats er eerst een met NLCSLEGENDA.'}
 Run-Case $case $drawing $specs[$case][0] $expected
 if($specs[$case].Count -eq 4){
  $expected=@($specs[$case][3])
  Run-Case "$case-verify" $drawing $specs[$case][2] $expected
 }
 if($case -eq 'linetypes'){
  Run-Case 'linetypes-reopen' $drawing "_.AUDIT`n_N`nNLCSLEGENDALINETYPECHECK" @('Total errors found 0 fixed 0','LINETYPECHECK opgeslagen varianten na tweede heropening -> OK')
 }
 if($case -eq 'snapshot'){
  Run-Case 'snapshot-verify' $drawing "NLCSLEGENDASNAPSHOTVERIFY`nNLCSLEGENDASTABILITYTEST" @('SNAPSHOTVERIFY herladen bron; nieuwe symboolvorm -> OK','STABILITY updates=50','STABILITY orphan blocks=0 -> OK')
 }
 if($case -eq 'reload'){
  Copy-Item -LiteralPath (Join-Path $dir 'reload_replacement.dwg') -Destination (Join-Path $dir 'reload_source.dwg') -Force
  Run-Case 'reload-verify' $drawing 'NLCSLEGENDAXREFRELOADVERIFY' @('XREFRELOAD totaal: OK')
 }
 if(!$SkipReopen -and $case -in @('band','appearance','realsize','properties','entitystyle','dependencies','sampleconflict','placement','render','nested','hatch','oldupdate','mtext')){
  $commands='NLCSLEGENDASTABILITYTEST'
  $expected=@('STABILITY updates=10','STABILITY updates=50','STABILITY orphan blocks=0 -> OK')
  if($case -eq 'placement'){
   $commands="NLCSLEGENDAXREFPLACEMENTVERIFY`n$commands"
   $expected+= 'PLACEMENTVERIFY totaal: OK'
  }
  Run-Case "$case-reopen" $drawing $commands $expected
 }
}
if((Get-TemplateHash) -ne $templateHash){throw 'Templatebron gewijzigd.'}
Write-Output "ACCEPTANCE PASS: $($results.Count) runs; bron ongewijzigd; resultaten in $OutputDirectory"
