# Bijdragen

Bijdragen zijn welkom. Een paar praktische zaken:

## Ontwikkelen

Je hebt de .NET SDK nodig: versie 8 voor AutoCAD 2025 en 2026, versie 10 voor 2027.
AutoCAD zelf hoeft niet geïnstalleerd te zijn; de build gebruikt standaard
de `AutoCAD.NET`-referenties van NuGet.

```powershell
dotnet build NlcsLegenda.slnx -c Release
dotnet test tests/NlcsLegenda.Core.Tests -c Release
dotnet test tests/NlcsLegenda.Update.Tests -c Release
dotnet run --project tests/NlcsLegenda.Ui.Tests -c Release
pwsh -File dev/scripts/Test-CoreConsoleState.ps1
```

De logica die zonder AutoCAD werkt (laagnamen ontleden, groeperen, opmaak) zit in
`NlcsLegenda.Core` en is met unittests gedekt. Raak je die code aan, voeg dan een test
toe. De UI-controles zijn een apart uitvoerbaar testprogramma: `dotnet test` draait die
niet. Ze controleren vensters op 100%, 125%, 150% en 200% schaal, plus bediening en
opslaggedrag. Voor de plugin zelf staan er CAD-scripts in `dev/scripts/`.

```powershell
dotnet build src/NlcsLegenda.Plugin -c Release -p:AcadVersion=2027
pwsh -File dev/scripts/Run-ReleaseAcceptance.ps1 -AcadVersion 2027
```

De acceptatierunner vereist PowerShell 7.4+, een geïnstalleerde host en een nieuwe
uitvoermap. Met `-ReferenceTemplatePath <pad-naar-DWT>` bevat de volledige ronde
ook scheidingsbanden, arceringen/afdekvlakken en de rechtstreeks gemeten templateopmaak.
Bij een expliciete `-Cases`-selectie draait alleen die selectie. Zonder `-Isolated` moeten andere CAD-processen gesloten zijn. Met
`-Isolated` krijgt iedere proef een eigen profiel. De resultaten bevatten DLL-hash,
bronbehoud en opslaan/heropenen. Dit bewijst geen normale GUI-start of automatische
bundellading. Gebruik voor installed acceptance `-PluginPath <geïnstalleerde DLL>`.

### Documentatiebeelden

De vensterbeelden gebruiken de echte forms met synthetische gegevens. Genereer ze
opnieuw bij UI-wijzigingen en bekijk de PNG’s voordat je ze publiceert:

```powershell
dotnet run --project tests/NlcsLegenda.Ui.Tests -c Release -- --docs docs
```

Privé-DWG’s, bedrijfseigen voorbeelden en hun screenshots horen niet in git.

## Stijl

Houd de bestaande stijl aan (zie `.editorconfig`): vier spaties, `var` waar het type
duidelijk is. Gebruik weinig commentaar: korte Nederlandse fragmenten in kleine letters,
alleen waar de reden voor de code anders onduidelijk blijft. Teksten in de UI en
documentatie zijn Nederlands. Kies namen die de taak of inhoud beschrijven. Sluit aan
bij bestaande CAD-termen en vermijd onnodige afkortingen. Behoud namen die in
opgeslagen instellingen of openbare commando's worden gebruikt.

## Pull requests

Werk op een aparte branch, houd een PR klein en beschrijf kort wat er verandert. Zorg
dat `dotnet test` groen is; de CI draait dit ook.

## Releasen

De versie staat op één plek: `<VersionPrefix>` in `Directory.Build.props`. De DLL's,
de bundle-manifesten en de MSI lezen die waarde. Voor een release:

1. Werk `Directory.Build.props`, `deploy/NlcsLegenda.bundle/PackageContents.xml`
   (`AppVersion`) en `CHANGELOG.md` bij naar hetzelfde versienummer.
2. Zet een tag `vX.Y.Z`. De release-workflow controleert dat de tag overeenkomt met
   `Directory.Build.props`, het manifest en een CHANGELOG-sectie, draait de tests en
   bouwt daarna de bundels (2025/2026/2027) en de ondertekende MSI.
3. Download de publieke assets, controleer checksums en handtekening, installeer de
   publieke MSI en vergelijk de geïnstalleerde DLL’s met de ZIP’s. Draai daarna
   installed acceptance. Controleer normale automatische GUI-lading afzonderlijk.

Maak de tag pas nadat de PR gemerged is, de lokale main gelijk is aan origin/main
en de verplichte controles groen zijn. Controleer in PowerShell na iedere
Git/GitHub-opdracht ook `$LASTEXITCODE`; `$ErrorActionPreference` stopt native
programma’s niet automatisch bij een fout. Overschrijf geen bestaande releasetag.

## Issues

Meld een bug met de stappen om het te reproduceren, de AutoCAD-/Civil 3D-versie en zo
mogelijk een voorbeeldtekening of de betreffende laagnamen.


De beoordeling van CodeQL-meldingen staat in [Codekwaliteit](docs/codekwaliteit.md).


### Updateprogramma

`NlcsLegenda.Update` bevat de hostonafhankelijke controle, cache en validatie.
`NlcsLegenda.Updater` is een apart Windows-programma dat pas na het sluiten van
CAD de MSI start. Bouw het met `dev/scripts/Build-Updater.ps1`.

De release-workflow ondertekent eerst de updater en daarna de drie DLL’s per
CAD-jaargang. De bewaarde bundels in `artifacts/release-bundles` leveren exact
dezelfde bytes aan ZIP’s en MSI; `Build-Installer.ps1 -BundleDirectory <map>`
bouwt of ondertekent die DLL’s niet opnieuw. Pas daarna wordt de MSI ondertekend.
Een verkeerde bundelversie, CAD-jaargang, helperversie of handtekening stopt de bouw. `update.json` wordt afgeleid van
de ondertekende MSI-eigenschappen in `Package.wxs`. Alle bestanden worden eerst
als draft gepubliceerd; pas een complete release wordt de nieuwste versie.

Voor een wijziging aan deze route:

- draai de update- en UI-tests en `Test-InstallerRollback.ps1` (vereist WiX 5);
- voer `Test-UpdatePackage.ps1 -SignedMsi <publieke MSI>` uit: echte inhoudswijziging,
  verkeerde uitgever en verkeerde versie moeten worden geweigerd;
- controleer een ondertekende updater tijdens een actieve CAD-sessie, sluit die
  normaal en controleer de installatie, de instellingen en de volgende CAD-start;
- vergelijk alle negen DLL’s en de updater met de publieke ZIP’s;
- draai `Test-ReleaseSignatures.ps1 -ContentsDirectory <uitgepakte Contents>`;
- controleer de normale CAD-start met het expliciet vertrouwde certificaat,
  zonder wijzigingen aan `SECURELOAD` of `TRUSTEDPATHS`.

Zie [Updateprotocol](docs/update-protocol.md) voor de grenzen van automatische
updates, certificaatwisseling en het bewaren van gegevens.
