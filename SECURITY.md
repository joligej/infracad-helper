# Beveiliging

## Ondersteunde versies

Alleen de laatste release krijgt updates. Gebruik bij voorkeur de nieuwste versie
op de [releases-pagina](https://github.com/joligej/infracad-helper/releases).

## Een probleem melden

Meld een kwetsbaarheid via de [Security-tab](https://github.com/joligej/infracad-helper/security)
met "Report a vulnerability", of open een issue zonder gevoelige details en vraag om
een privécontact. Vermeld waar mogelijk de stappen om het te reproduceren en de
AutoCAD-/Civil 3D-versie.

## Wat de plugin wel en niet doet

De plugin leest de tekening en schrijft geometrie, instellingen (in de tekening of in
`%APPDATA%\NlcsLegenda\`) en desgevraagd een CSV/JSON-export. De tekeningverwerking
blijft lokaal. De optionele opstartcontrole haalt openbare releasegegevens op bij
GitHub; na jouw keuze downloadt hij de installer. Er worden geen tekeningen,
projectpaden of telemetrie verstuurd.

Automatische installatie controleert SHA-256, de ondertekende inhoud, de vaste
uitgever en de product- en CAD-gegevens. Een apart programma wacht totdat CAD
gesloten is. Er worden geen certificaten geïmporteerd en geen CAD-sessies beëindigd.
Zie [Updateprotocol](docs/update-protocol.md) voor de controles en beperkingen.

De DLL’s, updater en MSI zijn ondertekend met het vaste self-signed
codeondertekeningscertificaat `CN=joligej`. De releasebouw controleert de inhoud en
de vaste certificaatvingerafdruk. Dezelfde ondertekende bytes gaan naar ZIP’s en MSI;
er wordt niet na het verpakken nogmaals gebouwd of ondertekend.

De handtekening bewijst dat dezelfde sleutel is gebruikt en het bestand intact is,
geen extern geverifieerde identiteit. Windows en AutoCAD moeten deze uitgever
één keer expliciet vertrouwen. Het certificaat heeft alleen het doel Code Signing;
vertrouwen in de persoonlijke certificaatopslag geldt wel voor alle code van die
uitgever binnen dat Windows-profiel. Installer en updater importeren geen
certificaten. Zie [Uitgever eenmalig vertrouwen](docs/installatie.md#uitgever-eenmalig-vertrouwen).
