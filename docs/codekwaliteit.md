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

Vensters moeten een mislukte bewerking melden en open blijven. Het opbouwen van
een legendaregel voegt context toe en geeft de fout daarna door, zodat de
transactie terugdraait. Deze foutgrenzen krijgen een andere behandeling dan een
stille terugval die een fout als een geldig resultaat presenteert.

## Validatie

De tests dekken ook mislukte bestandsvervanging, herstel en opruiming tegelijk,
onverwachte testfouten, volledige foutmeldingen, dubbel opruimen en Unicode-tekst.
De native CAD-proef controleert dat een los object wordt vrijgegeven en een
geregistreerd object beschikbaar blijft voor de transactie. Zie
[CONTRIBUTING](../CONTRIBUTING.md) voor de volledige regressie- en releasecontroles.
