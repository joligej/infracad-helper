# KLIC-gegevens en groepering

[Handleiding](handleiding.md#klic-gegevens-bekijken) · [Samenvoegen](handleiding.md#regels-samenvoegen) · [Problemen oplossen](problemen-oplossen.md#klic-gegevens-ontbreken)

KLIC-objecten kunnen extra gegevens bevatten, zoals diameter, materiaal en
netbeheerder. Op deze pagina lees je waar de plug-in die gegevens vindt en hoe
je kiest wat in de legenda verschijnt.

## Gegevens lezen

InfraCAD Map kan dezelfde objectgegevens op meerdere manieren in een DWG bewaren:
ingebedde XML-tabellen, Property Sets en Object Data. De legenda leest deze opslagvormen
afzonderlijk en koppelt ze aan het objectnummer, de handle, in de juiste
brontekening. Dat werkt ook bij geneste xrefs.
De bronbestanden worden niet gewijzigd.

In **Overzicht → KLIC-details** staan de waarden met bron, tabel en het aantal bronobjecten.
**Gebruikt** laat zien welke bron de legenda gebruikt. Onder **Bronobjecten** staan tekening,
xref en handle. Bij **Bijlagen** kun je een gekoppelde PDF of afbeelding openen. Ontbreekt
het bestand, dan krijg je een melding. De plug-in downloadt geen ontbrekende bijlagen.
Voorzorgsmaatregelen, toelichtingen en contactverwijzingen staan bij de veldwaarden.

Voor Property Sets is AEC-ondersteuning nodig. Object Data wordt via de Map-API gelezen.
Deze ondersteuning is in Civil 3D/Map beschikbaar en kan in gewone AutoCAD ontbreken.
Object Data wordt ook uit directe en geneste xrefs gelezen. De waarden komen van de
geladen bronobjecten. Voor de veldnamen opent de plug-in bij de eerste analyse een
tijdelijke, alleen-lezen kopie van de bron-DWG. Die wordt meteen gesloten en verwijderd.
De gelezen veldindeling blijft beschikbaar zolang de geladen xref ongewijzigd is.
De hoofdtekening blijft actief en de plug-in herlaadt geen xrefs. Een gewijzigde bronversie
wordt gemeld, zodat waarden niet aan een verkeerde veldindeling worden gekoppeld.
De eerste analyse kan daardoor langer duren dan opnieuw bijwerken.
In Core Console wordt Object Data niet gelezen.
Overzicht vermeldt wanneer een aanwezige opslagvorm niet kon worden gecontroleerd.
Die melding betekent niet dat de overige bronnen ook onleesbaar zijn.

## Kolommen en bronkeuze

Bij **KLIC-instellingen → Overzicht en export** kies je velden zoals thema, materiaal,
diameter, druk, spanning, KLIC-melding, aanvraagdatum en aanvraagsoort. De gekozen velden
verschijnen ook in CSV/JSON. De export vermeldt gebruikte bronnen en bronmeldingen.
**Exporteer lijst** in Overzicht exporteert alleen de regels die het huidige filter toont.
Het zoekfilter wijzigt de inhoud van de getekende legenda niet. CSV bevat naast de
oorspronkelijke omschrijving ook Legenda-omschrijving en Bronlagen. JSON bevat
`omschrijving`, `legendaOmschrijving` en `bronlagen`. Xref-voorvoegsels blijven zichtbaar.

Standaard krijgt de ingebedde InfraCAD-bron voorrang. Met **Gegevensbron** kies je desgewenst
Property Sets of Object Data, bijvoorbeeld na een handmatige wijziging. Per tabel en veld
wordt de voorkeursbron gebruikt wanneer het veld daar bestaat, ook als de waarde leeg is.
Ontbreekt het veld, dan geldt de eerste beschikbare bron in de volgorde InfraCAD, Property
Sets, Object Data. Conflicten binnen dezelfde tabel blijven zichtbaar. Gelijknamige velden
in verschillende tabellen zijn op zichzelf geen bronconflict.

Een groep met verschillende waarden toont alle varianten. Bevat een samengevoegde
leidingregel bijvoorbeeld objecten met materiaal PE én objecten zonder
materiaalgegevens, dan zie je zowel **PE** als **Niet opgegeven**. Dat geldt voor
Overzicht, de export en de gekozen legenda-omschrijving. De waarde PE wordt dus
niet aan de hele groep toegeschreven. Ontbreken de gegevens bij alle objecten, dan blijven
de extra velden leeg. DiameterM wordt van meter naar millimeter omgezet.
Nul of een negatieve waarde bij diameter, diepte, druk en spanning wordt als niet opgegeven
getoond. **Standaarddiepte (niet gemeten)** blijft onderscheiden van **Opgegeven diepte**.
De plug-in controleert daarmee niet de werkelijke ligging in het terrein.

## Regels samenvoegen of uitsplitsen

Onder **Lokale instellingen → Samenvoegen** betekent een vinkje dat het verschil
in die eigenschap wordt genegeerd. **Soorten samenvoegen** staat standaard uit.
Gas en water blijven daardoor verschillende soorten. Voor nieuwe KLIC-instellingen
staan spanning/druk, uitvoering, volgnummer, diameter, materiaal en netbeheerder
standaard aan. Overige lagen hebben hun eigen keuzes, standaard allemaal uit.

Soort, spanning/druk, uitvoering en volgnummer komen uit de NLCS-laagnaam.
Diameter en materiaal worden uit de laagnaam of objectgegevens gelezen.
De netbeheerder komt uit de objectgegevens. Een naam zoals DATA2 vertelt dus
niet wie de netbeheerder is. Een rioollaag onder hoofdgroep RI wordt als KLIC
behandeld als zij KLIC-objectgegevens heeft of uit een bron met KLIC in de naam komt.

Per voorbeeldtype kun je afwijken van de standaard. Je kunt bijvoorbeeld
diameters bij lijnen samenvoegen en bij symbolen apart houden. De afzonderlijke
diameters blijven terug te vinden in KLIC-details en de bronlagen. Er gaan geen
bronobjecten verloren. Hoeveelheden worden opgeteld. Verschillen in kleur en
voorbeeldvorm blijven bij automatisch samenvoegen onderscheiden.

De diameter- en materiaalkeuzes gelden voor zowel de laagnaam als objectgegevens.
Een bestaande keuze voor afzonderlijke materialen uit het KLIC-venster wordt
overgenomen bij **Samenvoegen**. Onder **KLIC-instellingen → Afzonderlijke regels**
kun je extra velden, zoals thema of druk, aanvinken. Verschillende combinaties
krijgen dan aparte legendaregels, ook als andere eigenschappen worden samengevoegd.
**Legenda-omschrijving** bepaalt welke veldwaarden achter de bestaande tekst
verschijnen. Deze twee lijsten zijn standaard leeg. Bestaande legenda's veranderen
daardoor niet zonder je keuze. De bestaande opmaak blijft gelden.

**Samengevoegde waarden tonen** voegt de verschillende waarden tussen haakjes toe,
bijvoorbeeld **Vrijvervalriool (200, 300)**. Zet dit uit voor alleen **Vrijvervalriool**.
De gegevens blijven dan beschikbaar in Overzicht en KLIC-details.

Elke legenda bewaart haar eigen keuzes. In Overzicht kun je de volgorde op **Handmatig**
zetten en regels verplaatsen. Bij een actief filter is verplaatsen uitgeschakeld. Bestaande
regels behouden hun plaats bij bijwerken. Nieuwe combinaties worden achteraan toegevoegd.
Eigen regels met dezelfde omschrijving maar verschillende voorbeelden houden elk hun positie.
Eigen en blanco regels vallen buiten automatisch samenvoegen. **Overzicht → Samenvoegen**
combineert zelf gekozen regels. Je kiest één hoofdobject voor het voorbeeld.
De titel wordt afgeleid van de omschrijvingen. Blanco regels en regels met
verschillende eenheden worden geweigerd. **Samenvoeging opheffen** maakt de
groep weer los. Eigen regels volgen het statusfilter. Een toewijzing aan een
eigen status heeft voorrang op dat filter.

## Symbolen en attributen

Een gevuld attribuut `OMSCHRIJVING` blijft de bron voor de beschrijving van een KLIC-symbool.
Per symboollaag wordt de meest voorkomende niet-lege omschrijving gebruikt. `TYPE` herhaalt
meestal het onderdeel uit de laagnaam. `LABEL` is doorgaans een objectnummer. Zijn deze attributen
leeg, dan kan de generieke tekst `TYPE \ LABEL \ OMSCHRIJVING` worden weggelaten via
**KLIC-placeholdertekst weglaten**. Werkelijke objectgegevens blijven beschikbaar in KLIC-details.

## Controle in Civil 3D/Map

Deze controle is bedoeld voor onderzoek naar een probleem. Gebruik voor het
dagelijks bekijken van gegevens **Overzicht → KLIC-details**.

Open een kopie van een KLIC-brontekening of een hoofdtekening met KLIC-xrefs en voer
`NLCSLEGENDAMAPDATATEST` uit. De proef analyseert drie keer, waarvan eenmaal met
materiaal en diameter als groepering. Object Data, hoeveelheden en de gedeelde
Map-tabellen moeten daarna nog gelijk en leesbaar zijn. Ook de actieve tekening en het
aantal geopende documenten worden gecontroleerd. De proef wijzigt de bron
niet. Core Console kan deze controle niet uitvoeren.
