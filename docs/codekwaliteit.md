# Codekwaliteit

De ronde na 1.32.3 begon met 142 CodeQL-meldingen: 111 over brede
foutafhandeling, 27 over lussen en 4 over het vrijgeven van objecten.
Ze hadden niveau `note`. De scanconfiguratie is niet versmald en meldingen
worden niet handmatig gesloten om het aantal te verlagen.

## Grafische resources in 1.32.5

Een gesloten WinForms-control geeft een toegewezen `ImageList` of zelf gemaakt
`Font` niet automatisch vrij. Samenstellen beheert daarom zijn eigen afbeeldingen
en lijst; de vensters Instellingen en Laagnaam bewerken beheren hun eigen lettertypen.
Regressieproeven faalden op 1.32.4 en controleren na reparatie dat de resources
werkelijk worden vrijgegeven. Gedeelde Windows-resources blijven bruikbaar.

Een afzonderlijke pictogramproef controleert alle 19 ribbonknoppen op 16 en 32
pixels: maat, zichtbare tekening en hergebruik. Die proef draait in een eigen proces,
zodat het laden van WPF de nagebootste WinForms-schermschalen niet tussentijds wijzigt.

De eerder waargenomen GDI+-melding ontstond in
`Autodesk.AutoCAD.LayerManager.MultiTreeView.OnDrawNode`. Een verband met deze
resourcelekken is niet aangetoond; deze release claimt geen reparatie van die hostmelding.

## Controle voor 1.36.0

In Civil 3D 2027 is de volledige GDI+-aanroepketen opnieuw vastgelegd bij het
openen van het lagenvenster, zowel met als zonder geladen NLCS-assemblies:
`Graphics.FillRectangle → LayerManager.MultiTreeView.OnDrawNode → SyncTheme`.
Deze hostfout kan dus onafhankelijk van de plugin optreden. De afzonderlijke
null-reference in `AecLayerManagerEx.standardFilter_Update` is niet gereproduceerd;
1.36.0 registreert die gericht naast de GDI+-meldingen.

De Map-proef met een alleen-lezen xref slaagt in drie opeenvolgende analyses:
38.593 objecten en 76.704 records, met behoud van bronbestand en documentcontext.
De eigen Civil 3D-testsessie sluit daarbij normaal af. Dit is geen bewijs dat
iedere eerdere afsluitmelding dezelfde oorzaak had.

De nieuwe reviewmeldingen over testobjecten en ingesprongen lussen zijn nagelopen.
Nieuwe CAD-wrappers hebben een expliciete levensduur; geneste lussen hebben accolades.
Exacte vergelijkingen in de viewportproef controleren behoud van opgeslagen waarden.
De nulvergelijking na het afronden van symboolcoördinaten voorkomt verschillende
sleutels voor `0` en `-0`. Labels in de instellingen worden met hun bovenliggende
venster vrijgegeven; de vensterproeven controleren dat behoud van resources.

## GDI+-onderzoek op 22 oktober 2026

De oorspronkelijke melding betrof `Graphics.FillRectangle` vanuit
`Autodesk.AutoCAD.LayerManager.MultiTreeView.OnDrawNode`. De nieuwe proeven
registreren uitzonderingen vanaf de CAD-opstart, vóór de eerste idle-gebeurtenis.
Starten, vijftien keer het lagenvenster openen/sluiten en gewoon QSAVE/QUIT slagen
met en zonder de geïnstalleerde plug-in. Ook starten met een vooraf geopend
lagenvenster slaagt in beide gevallen; er is geen oplopend GDI-resourcegebruik.

In een eerdere proef zonder geladen NLCS-assemblies is één generieke GDI+-fout
vastgelegd. Die eerste registratie bevatte alleen `Graphics.CheckErrorStatus`.
Een andere opdracht nam daarna de testsessie over; die ronde is geen volledige
acceptatieproef en bewijst niet dat dit precies dezelfde LayerManager-fout was.
Een verbindingsprobleem in de testbesturing en een fout in de riooltest zijn
afzonderlijk hersteld en opnieuw gecontroleerd; deze waren geen GDI+-fouten.

De oorzaak van de oude melding is hiermee nog niet definitief vastgesteld.
Vanaf 1.34.0 bewaart de plug-in een volgende GDI+-uitzondering met de lopende
aanroepketen in `%LOCALAPPDATA%\NlcsLegenda\gdi.log`. Dat is lokale diagnose,
zonder verzending, en geen reparatie of onderdrukking van de oorspronkelijke fout.
Per CAD-sessie worden maximaal acht meldingen geschreven; het bestand blijft
maximaal 256 KiB. Afsluiten koppelt de registratie los. Tests controleren ook
een onschrijfbaar logbestand en dat de oorspronkelijke uitzondering doorgaat.

## Object Data uit xrefs in 1.33.3

De Map-lezer van de hoofdtekening leest de oorspronkelijke objecten uit directe
en geneste xrefs. De hoofdtekening hoeft zelf geen Object Data-tabellen te hebben.
Velddefinities worden eenmalig uit een alleen-lezen bronkopie gehaald en bij een
andere bronversie of gewijzigde tabeldefinitie opnieuw gelezen. De bronkopie wordt
gesloten en verwijderd; de actieve tekening en werkdatabase worden hersteld.

In Civil 3D 2027 zijn 76.704 records met 745.963 veldwaarden onafhankelijk
vergeleken met de rechtstreeks aangeroepen Map-API. Met dezelfde bron ook in een
geneste xref worden 153.408 records gevonden. Herhaald analyseren, materiaal- en
diametergroepering, uitsluiten en herladen behouden de verwachte hoeveelheden.
De vaste Map-proef controleert bovendien drie analyses achter elkaar en behoud
van de geopende documenten. Deze hostcontrole geldt voor Civil 3D 2027; de builds
voor 2025 en 2026 bewijzen op zichzelf geen Map-acceptatie in die hosts.

## Map-project in 1.33.2

Een proef in Civil 3D 2027 vond een fout in het Map-resourcebeheer: na de eerste
analyse was `ProjectModel.ODTables` vrijgegeven. Een volgende Map-uitlezing kon
daardoor falen. Deze verzameling hoort bij het gedeelde Map-project; de plugin
laat haar voortaan in leven en ruimt alleen eigen tijdelijke wrappers op.

Op dezelfde KLIC-bron zijn 76.704 Object Data-records en 745.963 veldwaarden
vergeleken met de rechtstreeks aangeroepen Map-API. Drie opeenvolgende analyses,
waaronder groeperen op materiaal en diameter, behouden records en hoeveelheden.
De afzonderlijke GUI-proeven met en zonder de plugin sloten normaal af, na tien
keer het lagenvenster openen en sluiten. De oude GDI+-melding trad daarbij niet
op; daarmee is haar oorzaak nog niet bewezen of definitief verholpen.

## Wijzigingen in 1.32.4

- Globale instellingen worden eerst volledig voorbereid. Bij een mislukte
  vervanging gebruikt herstel ook een tijdelijk bestand. De oorspronkelijke
  fout, eventuele herstelfouten en opruimfouten blijven samen beschikbaar.
- Een opruimbereik probeert alle geregistreerde bronnen te sluiten, in omgekeerde
  volgorde en hoogstens één keer. CAD-objecten die al in een transactie staan
  worden niet als losse objecten vrijgegeven. De voorbeeldenbibliotheek opent
  nog steeds pas zodra een voorbeeld nodig is.
- CAD-proeven gebruiken een gemeenschappelijke uitvoerder met een vaste
  `FAIL:`-melding, testnaam en volledige foutinformatie. Ernstige geheugen- en
  procesfouten worden doorgegeven. Een exportfout is geen CSV-resultaat meer.
- Vensterproeven voeren de overige controles uit als één controle mislukt.
  De uiteindelijke uitslag blijft dan mislukt.
- Map-reflectie controleert ontbrekende methoden expliciet. Bestands-, CAD- en
  Windows-foutpaden vangen hun toepasselijke fouttypen op.
- Lussen selecteren de benodigde lagen, stijlen en attributen expliciet.
  Testvoorbeelden wijzen ongeldige laagnamen af. Tekstafbreking splitst geen
  gecombineerde letters of Unicode-symbolen.

## Overblijvende foutgrenzen

Vensters melden gewone bewerkingsfouten en blijven open. Ernstige geheugen- en
procesfouten passeren deze foutgrens, ook wanneer ze in een andere uitzondering
zijn verpakt. Het opbouwen van een legendaregel voegt alleen aan gewone fouten
context toe en geeft ze daarna door, zodat de transactie terugdraait. Een
vensterproef controleert afzonderlijk dat geheugenuitputting wordt doorgegeven,
de wachtcursor wordt hersteld en de bestaande lijst behouden blijft.

## Eerder afgesloten meldingen

Ook de ruwe scan is gecontroleerd. Oudere handmatig afgesloten meldingen bleven
daarin terugkomen. Tijdelijke CAD-objecten krijgen alsnog een expliciete
levensduur, ontbrekende xrefinformatie wordt niet als een volledige lijst
gepresenteerd en fouten bij Alles bijwerken en het opruimen van testbestanden
worden zichtbaar gemeld. Verwijderde groepsleden worden vóór het openen gefilterd.

Drie soorten melding zijn inhoudelijk terecht als vals positief beoordeeld:
controls worden door hun bovenliggende venster vrijgegeven; exacte vergelijking
van opgeslagen schaalwaarden controleert behoud zonder afronding; een proef met
een ongeldig schaalformaat controleert juist de afhandeling van `FormatException`.
De vensterproeven controleren nu ook expliciet of alle onderliggende controls na
het sluiten zijn vrijgegeven. De bestaande herstel- en formaattests blijven gelden.

## Validatie

De tests dekken ook mislukte bestandsvervanging, herstel en opruiming tegelijk,
onverwachte testfouten, volledige foutmeldingen, dubbel opruimen en Unicode-tekst.
De native CAD-proef controleert dat een los object wordt vrijgegeven en een
geregistreerd object beschikbaar blijft voor de transactie. Zie
[CONTRIBUTING](../CONTRIBUTING.md) voor de volledige regressie- en releasecontroles.

## Prestatiecontrole 1.35.2

De nieuwe meettool geeft ernstige geheugen- en procesfouten door. De overige
fouten worden met volledige context als een mislukte proef vastgelegd. Een
overbodige tekstconversie uit de nieuwe review is verwijderd.

Drie LINQ-stijladviezen in KlicData blijven bewust onverwerkt: deze lussen
vermijden tijdelijke selecties bij tienduizenden bronobjecten. Bronkeuze, lege
waarden, tabelvolgorde en dubbele waarden worden afzonderlijk getest. Dit zijn
stijladviezen, geen vastgestelde functionele of beveiligingsfouten.

## Prestatiecontrole 1.35.3

Het nieuwe `cs/linq/missed-where`-advies in de broncontrole is inhoudelijk bekeken.
De lus gebruikt `TryGetValue` om een veld tegelijk te vinden en te lezen, en stopt
bij het eerste verschil. Een afzonderlijk filter zou extra iteratie of opnieuw
opzoeken introduceren. Daarom blijft deze meetbaar snellere lus staan. Tests dekken
tabelnamen, veldvolgorde, lege en numerieke waarden, verschillende woordenboek-
vergelijkingen en opnieuw gewijzigde records. Er wordt geen broncontrole overgeslagen.
