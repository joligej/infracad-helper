# Bijdragen

Bijdragen zijn welkom. Een paar praktische zaken:

## Ontwikkelen

Je hebt de .NET SDK nodig: versie 8 voor AutoCAD 2025 en 2026, versie 10 voor 2027.
AutoCAD zelf hoeft niet geïnstalleerd te zijn; de build gebruikt standaard
de `AutoCAD.NET`-referenties van NuGet.

```powershell
dotnet build NlcsLegenda.slnx -c Release
dotnet test  NlcsLegenda.slnx
```

De logica die zonder AutoCAD werkt (laagnamen ontleden, groeperen, opmaak) zit in
`NlcsLegenda.Core` en is met unittests gedekt. Raak je die code aan, voeg dan een test
toe. Voor de plugin zelf staan er headless scripts in `dev/scripts/`.

## Stijl

Houd de bestaande stijl aan (zie `.editorconfig`): vier spaties, `var` waar het type
duidelijk is. Gebruik weinig commentaar: korte Nederlandse fragmenten in kleine letters,
alleen waar de reden voor de code anders onduidelijk blijft. Teksten in de UI en
documentatie zijn Nederlands.

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

## Issues

Meld een bug met de stappen om het te reproduceren, de AutoCAD-/Civil 3D-versie en zo
mogelijk een voorbeeldtekening of de betreffende laagnamen.

