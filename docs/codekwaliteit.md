# Codekwaliteit

De ronde na 1.32.3 begon met 142 CodeQL-meldingen: 111 over brede
foutafhandeling, 27 over lussen en 4 over het vrijgeven van objecten.
Ze hadden niveau `note`. De scanconfiguratie is niet versmald en meldingen
worden niet handmatig gesloten om het aantal te verlagen.

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
