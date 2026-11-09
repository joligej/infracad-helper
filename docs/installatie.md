# Installeren en bijwerken

[Terug naar het project](../README.md) · [Handleiding](handleiding.md)

De plug-in werkt op Windows, met de 64-bits versie van AutoCAD of Civil 3D
2025, 2026 of 2027. Gebruik bij voorkeur de MSI-installer. Die kiest de juiste
bestanden voor de ondersteunde CAD-jaargangen.

## Met de installer

1. Sla je werk op en sluit AutoCAD, Civil 3D en Core Console.
2. Download `NlcsLegenda-X.Y.Z.msi` bij de [laatste release](https://github.com/joligej/infracad-helper/releases/latest).
3. Voer de MSI uit. Een oudere MSI-installatie wordt bijgewerkt.
4. Start CAD opnieuw. De tab **NLCS Legenda** verschijnt in het lint.
   **Help** toont welke versie van de plug-in is geladen.

De installatie is per Windows-gebruiker en vereist geen beheerder. Eén MSI bevat
de bestanden voor AutoCAD/Civil 3D 2025, 2026 en 2027. De plug-in staat onder
`%APPDATA%\Autodesk\ApplicationPlugins\NlcsLegenda.bundle`.

Persoonlijke instellingen staan apart in `%APPDATA%\NlcsLegenda`.
Een nieuwe versie overschrijft geen instellingen van bestaande legenda’s in DWG’s.
Verwijderen kan via Windows **Instellingen → Apps**.

De installer is ondertekend met het zelfondertekende certificaat van `joligej`.
Windows kan daarom een onbekende uitgever melden. Een certificaat vertrouwen is
een aparte keuze en is niet nodig om de installer uit te voeren. Wil je voorkomen
dat AutoCAD bij volgende updates opnieuw om laadtoestemming vraagt, lees dan
[Uitgever eenmalig vertrouwen](#uitgever-eenmalig-vertrouwen).

Je kunt de download controleren door zijn SHA-256 te vergelijken met
`SHA256SUMS.txt` bij dezelfde release. Gebruik daarvoor bijvoorbeeld
`Get-FileHash` in PowerShell. Meer uitleg staat in [Beveiliging](../SECURITY.md).

## Vanuit de plug-in

Vanaf 1.42.0 kan de plug-in na het opstarten melden dat er een nieuwere stabiele
versie op GitHub staat. De automatische controle gebeurt maximaal eenmaal per
dag. Na een netwerkfout wordt niet vaker dan eenmaal per uur opnieuw geprobeerd.
Zonder internet kun je de geïnstalleerde versie blijven gebruiken.

1. Open **Updates controleren** in het lint, of gebruik `NLCSLEGENDAUPDATES`.
2. Lees de wijzigingen en kies **Downloaden en bijwerken**.
3. Sla je werk op en sluit **alle** AutoCAD-, Civil 3D- en Core Console-sessies.
   Het aparte updateprogramma wacht hierop. Het sluit geen programma's voor je.
4. Wacht op **Bijwerken voltooid** en start Civil 3D opnieuw.

Je kunt de update downloaden terwijl je verder werkt. De plug-inbestanden
blijven tijdens die CAD-sessie in gebruik en worden pas na het sluiten vervangen.
Het updateprogramma sluit CAD niet automatisch en start de computer niet opnieuw.
Als Windows een herstart nodig heeft, krijg je daarvan een melding.
De nieuwe plug-inversie wordt geladen wanneer je CAD weer start.

Vanaf 1.42.1 zijn ook alle DLL’s ondertekend met het vaste `joligej`-certificaat.
AutoCAD controleert daarnaast of jouw Windows-gebruiker deze uitgever vertrouwt.
Na eenmalig vertrouwen blijven nieuwere releases met hetzelfde certificaat
herkenbaar. Je hoeft dan niet voor ieder nieuw plug-inbestand opnieuw toestemming te geven.
Zie [Uitgever eenmalig vertrouwen](#uitgever-eenmalig-vertrouwen).

**Later** stelt de automatische melding een dag uit. **Deze versie overslaan**
onderdrukt de automatische melding voor die versie. Bij een handmatige controle
blijft de versie wel zichtbaar. Het vinkje
**Bij opstarten controleren** geldt voor jouw Windows-gebruiker,
los van de lokale legenda-instellingen en de globale legendastandaard.
Core Console doet geen netwerkcontrole en opent geen updatevensters.

Is er geen nieuwe versie, dan sluit je het venster met **Sluiten**.
**Opnieuw controleren** doet een nieuwe aanvraag. Tijdens die aanvraag kun je
**Annuleren** kiezen. Daarmee stel je latere automatische meldingen niet uit.
Je kunt de handmatige controle ook openen terwijl de opstartcontrole nog bezig is.

![Updatevenster met demonstratiegegevens](updates.png)

Installeren vanuit het updatevenster werkt bij de gewone MSI-installatie per
gebruiker. Gebruik je een ZIP of een andere installatie, dan krijg je zo nodig
een verwijzing naar de handmatige installer. Het updateprogramma vraagt
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

De installer levert de plug-in. Neem daarnaast zo nodig het volgende mee:

- Een geëxporteerd profiel via **Profielen → Exporteren/Importeren**.
- Je voorbeeldenbibliotheek. Kies het nieuwe pad via **Overzicht → Voorbeeld → Bibliotheek**.
- Het gebruikte projectbestand met omschrijvingen. Kies het nieuwe pad onder **Teksten**.
- Het gebruikte lettertype, bijvoorbeeld `NLCS-ISO.ttf`, via je organisatie.

Gebruikte voorbeelden zijn ingebed in de DWG. Voor nog niet gebruikte voorbeelden
is de bibliotheek nodig. Meerdere passende varianten vereisen een eigen keuze.

## Aanvullende gegevens in Civil 3D

Voor aanvullende KLIC-gegevens, zoals Object Data, is Civil 3D/Map nodig.
Core Console kan niet alle gegevens lezen die in de volledige Civil 3D-versie
beschikbaar zijn. Zie [KLIC-gegevens](klic-bronnen.md) als er velden ontbreken.

Voor beheerders: de bestanden voor CAD 2025 en 2026 gebruiken .NET 8.
De bestanden voor CAD 2027 gebruiken .NET 10. De benodigde AutoCAD-bibliotheken
worden door AutoCAD zelf geleverd.

Ontbreekt het lint of het commando? Zie [Problemen oplossen](problemen-oplossen.md).


## Uitgever eenmalig vertrouwen

Het certificaat is zelfondertekend en heeft de naam `CN=joligej`. De handtekening
bevestigt dat de bestanden met dezelfde sleutel zijn ondertekend en daarna niet
zijn gewijzigd. Een externe certificaatautoriteit heeft de identiteit van de
uitgever niet gecontroleerd. Vertrouw dit certificaat alleen als je
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
nodig. Bij een toekomstig ander certificaat moet je die keuze opnieuw beoordelen.

[Autodesk: ondertekende plug-ins en vertrouwde uitgevers](https://blog.autodesk.io/autocad-2016-trusted-paths-and-autoloader/).
