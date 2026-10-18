# Installeren en bijwerken

[Terug naar het project](../README.md) · [Handleiding](handleiding.md)

## Met de installer

1. Sla je werk op en sluit AutoCAD, Civil 3D en Core Console.
2. Download `NlcsLegenda-X.Y.Z.msi` bij de [laatste release](https://github.com/joligej/infracad-helper/releases/latest).
3. Voer de MSI uit. Een oudere MSI-installatie wordt bijgewerkt.
4. Start CAD opnieuw. De tab **NLCS Legenda** verschijnt in het lint.
   **Help** toont de geladen pluginversie.

De installatie is per Windows-gebruiker en vereist geen beheerder. Eén MSI bevat
de bundles voor AutoCAD/Civil 3D 2025, 2026 en 2027. De plugin staat onder
`%APPDATA%\Autodesk\ApplicationPlugins\NlcsLegenda.bundle`.

Persoonlijke instellingen staan apart in `%APPDATA%\NlcsLegenda`.
Een nieuwe versie overschrijft geen instellingen van bestaande legenda’s in DWG’s.
Verwijderen kan via Windows **Instellingen → Apps**.

De MSI is zelfondertekend als `joligej`; Windows kan een onbekende uitgever melden.
Je hoeft geen certificaat te installeren. Vergelijk de SHA-256 van je download met
`SHA256SUMS.txt` bij dezelfde release, bijvoorbeeld met `Get-FileHash` in PowerShell.
Meer uitleg staat in [Beveiliging](../SECURITY.md).

## Handmatig met een ZIP

Kies `NlcsLegenda-2025-vX.Y.Z.zip`, `2026` of `2027` voor jouw CAD-jaargang.
Pak `NlcsLegenda.bundle` uit in `%APPDATA%\Autodesk\ApplicationPlugins` en herstart
CAD. De twee DLL’s horen bij elkaar. Combineer geen bestanden uit verschillende
versies en gebruik niet tegelijk een losse oude bundle en een MSI-installatie.

## Op een andere computer

De installer levert de plugin. Neem daarnaast zo nodig mee:

- een geëxporteerd profiel via **Profielen → Exporteren/Importeren**;
- je voorbeeldenbibliotheek; kies het nieuwe pad via **Overzicht → Voorbeeld → Bibliotheek**;
- het gebruikte lettertype, bijvoorbeeld `NLCS-ISO.ttf`, via je organisatie.

Gebruikte voorbeelden zijn ingebed in de DWG. Voor nog niet gebruikte voorbeelden
is de bibliotheek nodig. Meerdere passende varianten vereisen een eigen keuze.

## Ondersteunde hosts

De 2025- en 2026-builds gebruiken .NET 8; de 2027-build gebruikt .NET 10.
De benodigde AutoCAD-bibliotheken komen uit de host. Een aanwezige Civil 3D/Map-API
is nodig voor aanvullende KLIC-opslagvormen. Core Console is geen vervanging voor
een volledige Civil 3D-controle van AEC-proxyobjecten of Object Data.

Ontbreekt het lint of het commando? Zie [Problemen oplossen](problemen-oplossen.md).
