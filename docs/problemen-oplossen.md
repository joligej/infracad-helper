# Problemen oplossen

[Handleiding](handleiding.md) · [Installatie](installatie.md)

## Een regel ontbreekt of hoort er niet bij

Gebruik **Controleren** (`NLCSLEGENDAELEMENT`) en wijs een bronobject aan.
De melding noemt onder meer status, hoofdgroep, elementsoort, zichtbaarheid,
xref-keuze en handmatige uitsluiting. Controleer dat je de juiste legenda bewerkt.

Onder **Lokale instellingen → Bronnen en inhoud** schakelt **Xrefs meenemen**
alle bronnen in of uit en vervangt het eerdere uitzonderingen. Kies **Xrefs…**
om koppelingen apart te kiezen. Een uitgeschakelde bovenliggende xref sluit ook
de geneste inhoud uit. Herlaad gewijzigde xrefs voordat je de legenda bijwerkt.

**Overzicht → Vernieuwen** leest de gekozen bron opnieuw. Het zoekfilter in dat
venster bepaalt alleen de lijst en de lijstexport; het verandert de legenda niet.
**Samenstellen** bepaalt welke regels werkelijk worden opgenomen.

## De opmaak lijkt anders dan de voorbeelden

Controleer de eenheid (meters), legendaschaal en eventuele schaalvariant van het
bronsymbool. Een groot bronsymbool wordt niet automatisch verkleind tot het vakje.
**Lokale instellingen → Legenda → Templateopmaak herstellen** herstelt de vaste
maatvoering; eigen teksten, bronkeuzes en schaal blijven bewaard.

Een samengesteld voorbeeld vraagt soms om de volledige bibliotheekvariant.
Kies die via **Overzicht → Voorbeeld…**. Bij meerdere passende varianten maakt
de plugin geen willekeurige keuze. Controleer ook of het gebruikte lettertype
beschikbaar is; vervangende fonts kunnen tekstbreedtes veranderen.

Rechtstreekse edits aan de getekende legenda verdwijnen bij bijwerken.
Gebruik de instellingen, **Elementtekst**, **Samenstellen** en **Overzicht**.

## Er staat `? m²`

Een bronarcering heeft geen betrouwbaar meetbaar oppervlak. **Overzicht** toont
onder **Broncontrole** de laag en handle; **Alleen bronmeldingen** beperkt de lijst.
Herstel de broncontour en werk de legenda bij. Een totaal met een onbekend deel
wordt ook als onbekend getoond, zodat een gedeeltelijk totaal niet volledig lijkt.

## KLIC-gegevens ontbreken

Kijk bij **KLIC-details** welke gegevens daadwerkelijk aan het object zijn gekoppeld.
Een laagnaam zoals DATA2 bevat geen netbeheerder. Object Data vereist Civil 3D/Map en
werkt ook via xrefs. Controleer of de bron-DWG bereikbaar is. Bij een melding over een
gewijzigde bronversie moet de xref eerst opnieuw geladen worden.
Een aanwezige maar lege voorkeurswaarde wordt niet automatisch door een andere
bron vervangen. Zie [KLIC-gegevens](klic-bronnen.md).

## Lint of commando ontbreekt

Herstart CAD na installatie en controleer de pluginversie via **Help**.
Gebruik bij een ontbrekende tab eerst het getypte commando `NLCSLEGENDAHELP`.
Als ook dat onbekend is, controleer de gekozen ZIP-jaargang, eventuele dubbele
bundles en meldingen over het laden van applicaties. Laat een beheerder eventueel
de ingestelde applicatiebeveiliging controleren; verlaag die niet om een melding
te omzeilen. Vermeld bij een bugmelding of de plugin automatisch of via NETLOAD laadt.

## FILEDIA of een ErrorReports-map na afsluiten

`FILEDIA` bepaalt of AutoCAD bestandsvensters gebruikt. Een script of ander CAD-proces
kan dit in een gedeeld profiel veranderen. Noteer welke processen en scripts actief
waren wanneer de waarde omslaat; alleen de aanwezigheid van deze plugin bewijst
geen oorzaak.

Een `cer.log` met alleen informatieregels over de start van Autodesk CER bevat
geen crashoorzaak. Bewaar bij een herhaling het tijdstip, de volledige melding en
het bijbehorende rapport. Meld ook of er een foutvenster was of alleen een map
werd aangemaakt. De eerdere afsluitmelding is niet reproduceerbaar vastgesteld.

## Een probleem melden

Gebruik het [bugformulier](https://github.com/joligej/infracad-helper/issues/new?template=bug_report.yml).
Vermeld pluginversie, exacte CAD-versie/update, handeling, verwacht resultaat en
zo nodig xrefs, schaal en schermschaal. Een kleine nagemaakte tekening zonder
projectgegevens is vaak voldoende. De [diagnostische commando’s](diagnostische-commandos.md)
zijn uitsluitend voor testtekeningen; ze kunnen zelf objecten en xrefs aanmaken.
