# Problemen oplossen

[Handleiding](handleiding.md) · [Installatie](installatie.md)

Controleer eerst of je de juiste legenda bewerkt. Elke legenda bewaart haar
eigen bronnen en instellingen. Gebruik voor een bestaande legenda **Lokale
instellingen**. Een wijziging aan **Globale standaard** geldt voor nieuwe legenda's.

## Er zijn geen regels, maar ik wil wel instellingen aanpassen

Plaats de lege legenda met **Genereren**. Dat kan ook als er geen brongegevens
zijn gevonden. Kies daarna xrefs via **Lokale instellingen → Bronnen en inhoud**
en klik op **Toepassen**.

Heb je bij Genereren bewust **Blanco** gekozen, dan staat de samenstelling vast.
Keur bronregels goed via **Samenstelling vastzetten** of zet het vastzetten uit
als je de legenda automatisch wilt laten vullen.

## Een regel ontbreekt of hoort er niet bij

Gebruik **Controleren** (`NLCSLEGENDAELEMENT`) en wijs een bronobject aan.
De melding noemt onder meer status, hoofdgroep, elementsoort, zichtbaarheid,
xref-keuze en handmatige uitsluiting. Controleer dat je de juiste legenda bewerkt.

Onder **Lokale instellingen → Bronnen en inhoud** schakelt **Xrefs meenemen**
alle bronnen in of uit en vervangt het eerdere uitzonderingen. Kies **Xrefs…**
om koppelingen apart te kiezen. Een uitgeschakelde bovenliggende xref sluit ook
de geneste inhoud uit. Herlaad gewijzigde xrefs voordat je de legenda bijwerkt.

**Overzicht → Vernieuwen** leest de gekozen bron opnieuw. Het zoekfilter in dat
venster bepaalt alleen de lijst en de lijstexport. Het verandert de legenda niet.
**Samenstellen** bepaalt welke regels werkelijk worden opgenomen.

Bij een vaste samenstelling blijven nieuwe onderdelen eerst voorstellen.
Bekijk die via **Samenstelling vastzetten**. Bij gekozen kaartvensters telt
alleen de zichtbare inhoud van die kaarten mee.

## Er staan te veel regels in de legenda

Bekijk in **Overzicht** de bronlaag, status en kolom **Variant**. Controleer
daarna onder **Lokale instellingen → Samenvoegen** welk verschil de regels
gescheiden houdt. Een vinkje betekent dat dit verschil wordt genegeerd.

Wil je bijvoorbeeld riooldiameters combineren, zet dan **Diameters samenvoegen**
aan voor de juiste groep en het juiste voorbeeldtype. Laat **Soorten samenvoegen**
uit om verschillende leidingsoorten te behouden. Verschillen in kleur en
lijnpatroon blijven bij automatisch samenvoegen apart.

Gebruik **Samenstelling vastzetten** als alleen goedgekeurde regels op het blad
mogen staan. Nieuwe bronregels worden dan eerst als voorstel getoond.
Zie [Samenvoegen](handleiding.md#regels-samenvoegen) voor voorbeelden.

## De opmaak lijkt anders dan de voorbeelden

Controleer de eenheid (meters), legendaschaal en eventuele schaalvariant van het
bronsymbool. Een groot bronsymbool wordt niet automatisch verkleind tot het vakje. Schakel daarvoor
**Opmaak → Symbolen → Passend maken in het voorbeeldvak** in.
**Lokale instellingen → Legenda → Templateopmaak herstellen** herstelt de vaste
templateopmaak. Eigen teksten, bronkeuzes en schaal blijven bewaard.
Extra hoeveelheden en de totaalregel worden daarbij uitgezet.

Een samengesteld voorbeeld vraagt soms om de volledige bibliotheekvariant.
Kies die via **Overzicht → Voorbeeld…**. Bij meerdere passende varianten maakt
de plug-in geen willekeurige keuze. Controleer ook of het gebruikte lettertype
beschikbaar is. Een vervangend lettertype kan de tekstbreedte veranderen.

Rechtstreekse wijzigingen aan de getekende legenda verdwijnen bij bijwerken.
Gebruik de instellingen, **Elementtekst**, **Samenstellen** en **Overzicht**.

## De legenda past niet in het bestaande venster

Bij **Binnen het bestaande legendavenster blijven** houdt de plug-in de
venstergrootte, plotschaal en teksthoogte vast. Past de inhoud niet leesbaar,
dan blijft de vorige legenda staan en volgt een melding.

Controleer lange omschrijvingen, grote symbolen, kolommen en de ruimte voor
opmerkingen en schaalbalk. Pas de inhoud of beschikbare ruimte aan en werk
de legenda opnieuw bij. Verander de schaal niet alleen om tekst passend te
maken zonder ook de papiermaten te controleren.

## De kleur of het lijnpatroon wijkt af van de kaart

Controleer eerst of de legenda de algemene laagweergave of de weergave van
gekozen kaartvensters gebruikt. Een kleurafwijking binnen een viewport kan
verschillen van de algemene laagkleur.

Bekijk daarna de regel in **Overzicht**. Een bewust vastgezet voorbeeld of
een zelf gekozen bibliotheekvoorbeeld kan een eigen weergave hebben. Kies
**Bron volgen** bij een gekoppeld voorbeeld als dat de actuele bron moet volgen.
Zie [Voorbeelden kiezen en koppelen](handleiding.md#voorbeelden-kiezen-en-koppelen).

## Een streeplijn wordt doorgetrokken na AUDIT

Vanaf 1.38.1 maakt de plug-in zelfstandige lijntypen voor xref-voorbeelden, inclusief
bijbehorende tekst- en shapedefinities. Dit geldt ook voor nieuwe kleur- en
stijlvarianten. Werk een bestaande legenda eenmaal bij om de voorbeelden opnieuw
uit de bereikbare bron op te bouwen. De bron-xrefs worden daarbij niet aangepast.
Een bewust bewaard voorbeeld zonder bereikbare bron behoudt zijn opgeslagen vorm.
Een eerder door AUDIT verwijderd patroon kan dan niet uit de bron worden hersteld.

## Er staat `? m²`

Een bronarcering heeft geen betrouwbaar meetbaar oppervlak. **Overzicht** toont
onder **Broncontrole** de laag en het objectnummer (de handle).
Met **Alleen bronmeldingen** beperk je de lijst tot regels die aandacht vragen.
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

Herstart CAD na installatie en controleer de versie van de plug-in via **Help**.
Gebruik bij een ontbrekende tab eerst het getypte commando `NLCSLEGENDAHELP`.
Als ook dat onbekend is, controleer de gekozen ZIP-jaargang, eventuele dubbele
bundles en meldingen over het laden van applicaties. Laat een beheerder eventueel
de ingestelde applicatiebeveiliging controleren. Verlaag die niet om een melding
te omzeilen. Vermeld bij een bugmelding of de plug-in automatisch of via NETLOAD laadt.

## FILEDIA of een ErrorReports-map na afsluiten

`FILEDIA` bepaalt of AutoCAD bestandsvensters gebruikt. Een script of ander CAD-proces
kan dit in een gedeeld profiel veranderen. Noteer welke processen en scripts actief
waren wanneer de waarde omslaat. Alleen de aanwezigheid van de plug-in zegt
niet welk programma de waarde heeft gewijzigd.

Een `cer.log` met alleen informatieregels over de start van Autodesk CER bevat
geen crashoorzaak. Bewaar bij een herhaling het tijdstip, de volledige melding en
het bijbehorende rapport. Meld ook of er een foutvenster was of alleen een map
werd aangemaakt. Met alleen de mapnaam of dit log is de oorzaak niet vast te stellen.

## Fout in het lagenvenster

Een GDI+-melding in `LayerManager.MultiTreeView.OnDrawNode` is in Civil 3D 2027
ook gereproduceerd zonder dat NLCS Legenda was geladen. Deze melding kan dus
onafhankelijk van de plug-in ontstaan. Dat bewijst niet de oorzaak van iedere
GDI+-melding of afsluitfout.

De melding `NullReferenceException` in `AecLayerManagerEx.standardFilter_Update`
is nog niet gereproduceerd. De plug-in registreert deze specifieke Autodesk-melding en
GDI+-meldingen in `%LOCALAPPDATA%\NlcsLegenda\gdi.log`, met een begrensde
bestandsgrootte en maximaal acht meldingen per sessie. De oorspronkelijke fout
wordt niet onderdrukt. Vermeld bij een herhaling het tijdstip en welke handeling
in het lagenvenster eraan voorafging.

## Een probleem melden

Gebruik het [bugformulier](https://github.com/joligej/infracad-helper/issues/new?template=bug_report.yml).
Vermeld de versie van de plug-in, de exacte CAD-versie en update, de handeling, het verwachte resultaat en
zo nodig xrefs, schaal en schermschaal. Een kleine nagemaakte tekening zonder
projectgegevens is vaak voldoende. De [diagnostische commando’s](diagnostische-commandos.md)
zijn uitsluitend voor testtekeningen. Ze kunnen zelf objecten en xrefs aanmaken.
