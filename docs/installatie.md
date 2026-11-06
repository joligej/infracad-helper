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

## Vanuit de plug-in

Vanaf 1.42.0 controleert de plug-in bij de eerste rustige gelegenheid na opstarten
of GitHub een nieuwere stabiele release heeft. De automatische netwerkcontrole
gaat maximaal eenmaal per dag; na een netwerkfout probeert hij het niet vaker
dan eenmaal per uur. Zonder internet blijft de huidige versie werken.

1. Open **Updates controleren** in het lint, of gebruik `NLCSLEGENDAUPDATES`.
2. Lees de wijzigingen en kies **Downloaden en bijwerken**.
3. Sla je werk op en sluit **alle** AutoCAD-, Civil 3D- en Core Console-sessies.
   Het aparte updateprogramma wacht hierop; het sluit geen programma’s voor je.
4. Wacht op **Bijwerken voltooid** en start Civil 3D opnieuw.

De geladen DLL’s worden niet vervangen terwijl CAD draait. Windows Installer
krijgt ook geen toestemming om CAD automatisch te sluiten. Als Windows toch
meldt dat een herstart nodig is, toont de updater dat; hij herstart zelf niets.
De update is pas in gebruik na een nieuwe CAD-start.

Vanaf 1.42.1 zijn ook alle DLL’s ondertekend met het vaste `joligej`-certificaat.
AutoCAD controleert daarnaast of jouw Windows-gebruiker deze uitgever vertrouwt.
Na eenmalig vertrouwen blijven nieuwere releases met hetzelfde certificaat
herkenbaar; er hoeft niet bij iedere nieuwe DLL opnieuw toestemming te worden gegeven.
Zie [Uitgever eenmalig vertrouwen](#uitgever-eenmalig-vertrouwen).

**Later** stelt de automatische melding een dag uit. **Deze versie overslaan**
onderdrukt die versie; een handmatige controle blijft hem tonen. Het vinkje
**Bij opstarten controleren** geldt voor jouw Windows-gebruiker,
los van de lokale legenda-instellingen en de globale legendastandaard.
Core Console doet geen netwerkcontrole en opent geen updatevensters.

![Updatevenster met demonstratiegegevens](updates.png)

De automatische installatie is beschikbaar voor de gewone MSI-installatie per
gebruiker. Een ZIP, ontwikkelbuild, afwijkende installatie of niet ondersteunde
CAD-versie krijgt een verwijzing naar de handmatige installer. De updater vraagt
geen beheerdersrechten en installeert geen certificaat. Een onvolledig,
gewijzigd of anders ondertekend bestand wordt geweigerd.

Persoonlijke JSON-instellingen worden vóór installatie gekopieerd naar
`%LOCALAPPDATA%\NlcsLegenda\Updates\SettingsBackup` en daarna vergeleken.
De installer heeft geen toegang nodig tot je tekeningen. Bij een mislukte
installatie toont het venster de fout en de locatie van het installatielog.
Met **Opnieuw proberen** kun je verder nadat de oorzaak is verholpen.

## Handmatig met een ZIP

Kies `NlcsLegenda-2025-vX.Y.Z.zip`, `2026` of `2027` voor jouw CAD-jaargang.
Pak `NlcsLegenda.bundle` uit in `%APPDATA%\Autodesk\ApplicationPlugins` en herstart
CAD. De drie DLL’s en het updateprogramma horen bij elkaar. Combineer geen bestanden uit verschillende
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


## Uitgever eenmalig vertrouwen

Het bestaande certificaat is self-signed: `CN=joligej`. De handtekening bewijst
herkomst van dezelfde sleutel en ongewijzigde inhoud; een externe certificaatautoriteit
heeft de identiteit niet gecontroleerd. Vertrouw dit certificaat alleen als je
software van deze uitgever wilt toestaan, of laat je CAD-beheerder dit regelen.

Download `joligej-codesign.cer` uit dezelfde GitHub-release en controleer de SHA-256
van het certificaat:

```text
E2B9FBE20EB6E4423BA7DC723116CD77584DE3224ADB0DD8E48EF8089ACC826B
```

Voor Windows-handtekeningcontrole heeft dit zelfondertekende certificaat een
vertrouwensanker nodig. Voor AutoCAD moet het daarnaast bij **Vertrouwde uitgevers**
staan. Dit kan voor alleen de **huidige gebruiker**, zonder beheerder:

1. Open `certmgr.msc` en importeer het gecontroleerde openbare certificaat in
   **Vertrouwde basiscertificeringsinstanties → Certificaten**.
2. Importeer hetzelfde certificaat in **Vertrouwde uitgevers → Certificaten**.
3. Start Civil 3D opnieuw. De ondertekende plug-in moet nu zonder laadbevestiging
   starten. Bij een afwijkend, ongeldig of verlopen certificaat blijft controle actief.

Dit certificaat heeft uitsluitend het doel **Code Signing** en de sleuteltoepassing
**Digital Signature**. Het vertrouwen geldt voor code van deze uitgever binnen jouw
Windows-profiel, niet alleen voor deze plug-in. Installer en updater voeren deze
vertrouwenskeuze niet stilzwijgend uit. Ze veranderen `SECURELOAD`, `TRUSTEDPATHS`
en certificaatopslag niet. Op een andere computer is de vertrouwenskeuze opnieuw
nodig; een toekomstige certificaatwisseling vereist eveneens een nieuwe beoordeling.

[Autodesk: ondertekende plug-ins en vertrouwde uitgevers](https://blog.autodesk.io/autocad-2016-trusted-paths-and-autoloader/).
