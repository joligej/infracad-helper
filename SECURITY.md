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

De MSI-installer is self-signed ondertekend als `joligej`. Die handtekening laat zien
dat het bestand na ondertekening niet is gewijzigd; hij zegt niets over een door een
externe autoriteit geverifieerde identiteit. Windows toont pas een vertrouwde uitgever
nadat je `joligej-codesign.cer` zelf importeert. Doe dat alleen als je dit certificaat
vertrouwt: een certificaat in *Vertrouwde basiscertificeringsinstanties* geldt voor je
hele gebruikersprofiel.
