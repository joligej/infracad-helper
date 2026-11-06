# Updates en installatie

De opstartcontrole vraagt alleen de nieuwste stabiele release van
`joligej/infracad-helper` op. Geen tekening, projectpad, computer-id of accounttoken
wordt verstuurd. GitHub ontvangt een normale HTTPS-aanvraag en de vaste user-agent
`NlcsLegenda-Updates/1`. De controle draait buiten de CAD-thread en wordt bij
sluiten geannuleerd. In Core Console wordt hij niet gestart.

## Van controle naar installatie

1. Vergelijk drie numerieke versievelden; sla drafts, prereleases en oudere of
   gelijke versies over. GitHub ETags en een dagelijkse cache beperken aanvragen.
   De time-out is tien seconden. Rate limits worden ook handmatig gerespecteerd.
2. Accepteer uitsluitend complete release-assets met een SHA-256-digest en het
   verwachte adres onder dezelfde repository en tag. `update.json` vermeldt
   CAD-jaargangen en de minimale updater. Zonder passend manifest blijft alleen
   de handmatige route beschikbaar.
3. Na een klik: download naar een tijdelijk bestand, maximaal 64 MiB en vijf
   minuten. Controleer grootte en SHA-256 voordat het bestand wordt klaargezet.
   Alleen HTTPS-downloads via GitHub en de twee gebruikte assetdomeinen zijn geldig.
4. Controleer de daadwerkelijke bestandshandtekening via Windows
   `MsiGetFileSignatureInformationW` met `MSI_INVALID_HASH_IS_FATAL`. Vergelijk het
   SHA-256 van het uitgeverscertificaat met de ingebouwde waarde; controleer ook
   geldigheidsduur en code-signingdoel. Een certificaat alleen uitlezen bewijst
   niet dat het bestand ongewijzigd is. Er wordt geen certificaat geïmporteerd.
5. Lees de ondertekende MSI-eigenschappen: productnaam, UpgradeCode, versie,
   gebruikersinstallatie, `NLCSACADYEARS` en `NLCSUPDATEPROTOCOL`. Losse metadata
   kan daardoor geen ongeschikte installer geschikt verklaren.
6. Kopieer de ondertekende helper buiten de installatiebundel. Controleer de kopie
   opnieuw en geef een kleine, gevalideerde opdracht mee. Een exclusief bestandsslot
   voorkomt twee installaties tegelijk; een ander slot voorkomt dubbele meldingen.
7. Wacht op twee opeenvolgende controles zonder `acad` of `accoreconsole`.
   Na twee uur stopt het wachten met een melding. Sluit of beëindig nooit een
   CAD-proces. Controleer vlak voor installeren opnieuw de versie, het bestand
   en de actieve processen; houd het MSI-bestand gedurende installatie tegen
   schrijven en verwijderen vergrendeld.
8. Start Windows Installer zonder herstart en met Restart Manager uitgeschakeld.
   De MSI verwijdert de vorige installatie binnen de terugdraaibare transactie
   (`MajorUpgrade` na `InstallInitialize`). Controleer registratie, DLL-versies
   en persoonlijke instellingen na afloop. Exitcode 3010 wordt als noodzakelijke
   Windows-herstart getoond, zonder die uit te voeren. Bij 3010 volgt de controle
   van uitgestelde bestandsvervanging pas bij een CAD-start na de Windows-herstart.
   De bestandscontrole gebruikt de CAD-jaargangen uit de ondertekende MSI.
9. Meld na de volgende normale CAD-start eenmaal welke update werkelijk is geladen.

## Bewaarde gegevens

`%LOCALAPPDATA%\NlcsLegenda\Updates` bevat de updatevoorkeuren, releasecache,
installatielog, uitslag, downloads en tijdelijke helperkopieën. Deze gegevens
staan los van `%APPDATA%\NlcsLegenda` en van legenda-instellingen in DWG’s.
De persoonlijke JSON-bestanden worden vóór installatie gekopieerd en achteraf
met SHA-256 vergeleken. Afwijkingen worden gemeld, niet automatisch overschreven.

Voltooide downloads tot en met de geladen versie worden opgeruimd. Helperkopieën
komen na twee dagen voor opruimen in aanmerking, uitsluitend wanneer geen
installatie loopt. Onbekende bestanden, submappen en reparse-points worden
niet gevolgd of verwijderd. Instellingenback-ups blijven beschikbaar.

## Grenzen en onderhoud

Automatische installatie vereist de per-user MSI-installatie op de verwachte
locatie. ZIP’s, ontwikkelbuilds, versieverschillen tussen geladen en geregistreerde
software en onbekende hosts krijgen een handmatige route. Een gebruiker moet
altijd zelf kiezen om te installeren.

Het vastgelegde certificaat is self-signed; dit is een bewuste koppeling aan deze
uitgever, geen externe identiteitsgarantie. Een ander of verlopen certificaat
wordt geweigerd. Certificaatwisseling vereist dus een expliciet voorbereide
release of een handmatige installatie; downloadgegevens kunnen de vertrouwde
uitgever niet wijzigen. Het huidige certificaat verloopt op 22 september 2031.

De helper gebruikt .NET Windows Desktop 8 of een nieuwere hoofdversie, zoals .NET
10 bij Civil 3D 2027. Hij draait buiten AutoCAD en vervangt nooit geladen DLL’s.
Een programma dat na de laatste controle alsnog CAD opent, kan Windows Installer
nog een bestandsvergrendeling laten melden; de updater geeft geen toestemming om
dat programma te beëindigen. Laat CAD daarom gesloten totdat de update klaar is.

## Verificatie

De tests controleren versie- en bronbeleid, hashfouten, redirects, limieten,
annuleren, offlinegebruik, caching, voorkeuren en exclusieve toegang. De vensters
worden op vier schermschalen gecontroleerd. `Test-UpdatePackage.ps1` wijzigt een
ondertekende MSI-tabel en controleert dat validatie die wijziging weigert.
`Test-InstallerRollback.ps1` gebruikt een afzonderlijk testproduct, veroorzaakt
een fout na het schrijven van nieuwe bestanden en vergelijkt de herstelde oude
DLL met het origineel; daarna moet een normale upgrade slagen.

Een echte update wordt daarnaast met de publieke, ondertekende bestanden getest,
inclusief wachten op CAD, instellingenbehoud en opnieuw laden na herstart.
