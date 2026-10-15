# KLIC-gegevens en groepering

## Gegevens lezen

InfraCAD Map kan dezelfde objectgegevens op meerdere manieren in een DWG bewaren:
ingebedde XML-tabellen, Property Sets en Object Data. De legenda leest deze opslagvormen
afzonderlijk en koppelt ze aan de handle in de juiste brontekening, ook bij geneste xrefs.
De bronbestanden worden niet gewijzigd.

In **Overzicht → KLIC-details** staan de waarden met bron, tabel en het aantal bronobjecten.
**Gebruikt** laat zien welke bron de legenda gebruikt. Onder **Bronobjecten** staan tekening,
xref en handle. Bij **Bijlagen** kun je een gekoppelde PDF of afbeelding openen. Ontbreekt
het bestand, dan volgt een melding; de plugin downloadt geen ontbrekende bijlagen.
Voorzorgsmaatregelen, toelichtingen en contactverwijzingen staan bij de veldwaarden.

Property Sets vereisen AEC-ondersteuning; Object Data vereist een beschikbare Map-API.
Object Data wordt gelezen in de geopende bron-DWG. De Map-API levert geen eigen project
voor een xref of losse achtergrond-database; daar blijven ingebedde gegevens en leesbare
Property Sets beschikbaar. Open de bron-DWG in Civil 3D/Map om ook Object Data te vergelijken.
In Core Console wordt Object Data niet gelezen. In gewone AutoCAD kunnen beide ontbreken.
Overzicht vermeldt wanneer een aanwezige opslagvorm niet kon worden gecontroleerd.
Die melding betekent niet dat de overige bronnen ook onleesbaar zijn.

## Kolommen en bronkeuze

Bij **KLIC-instellingen → Overzicht en export** kies je velden zoals thema, materiaal,
diameter, druk, spanning, KLIC-melding, aanvraagdatum en aanvraagsoort. De gekozen velden
verschijnen ook in CSV/JSON. De export vermeldt gebruikte bronnen en bronmeldingen.
**Exporteer lijst** in Overzicht exporteert alleen de regels die het huidige filter toont.
Het zoekfilter wijzigt de inhoud van de getekende legenda niet.

Standaard krijgt de ingebedde InfraCAD-bron voorrang. Met **Gegevensbron** kies je desgewenst
Property Sets of Object Data, bijvoorbeeld na een handmatige wijziging. Per tabel en veld
wordt de voorkeursbron gebruikt wanneer het veld daar bestaat, ook als de waarde leeg is.
Ontbreekt het veld, dan geldt de eerste beschikbare bron in de volgorde InfraCAD, Property
Sets, Object Data. Conflicten binnen dezelfde tabel blijven zichtbaar. Gelijknamige velden
in verschillende tabellen zijn op zichzelf geen bronconflict.

Een groep met verschillende waarden toont alle varianten. Een ontbrekende waarde wordt
niet aan andere objecten toegeschreven. Bevat een samengevoegde kabel-/leidingregel ook
objecten zonder metadata, dan staat bijvoorbeeld **Niet opgegeven; PE** in het overzicht,
de export en de gekozen legenda-omschrijving. Zonder metadata op alle objecten blijven
de extra velden leeg. DiameterM wordt van meter naar millimeter omgezet.
Nul of een negatieve waarde bij diameter, diepte, druk en spanning wordt als niet opgegeven
getoond. **Standaarddiepte (niet gemeten)** blijft onderscheiden van **Opgegeven diepte**.
De plugin controleert daarmee niet de werkelijke ligging in het terrein.

## Regels samenvoegen of uitsplitsen

Onder **Groepering** in Instellingen kun je samenvoegen op soort, specificatie (spanning/druk),
uitvoering, volgnummer en netbeheerder. Soort, specificatie, uitvoering en volgnummer komen
uit de NLCS-laagnaam; de netbeheerder komt uit de objectgegevens. DATA2 bewijst dus niet wie
de netbeheerder is. Hoeveelheden worden opgeteld; zichtbaar verschillende symbolen of
arceringen blijven gescheiden.

Onder **KLIC-instellingen → Afzonderlijke regels** kun je bijvoorbeeld materiaal en diameter
aanvinken. Verschillende combinaties krijgen dan aparte legendaregels, ook als andere
eigenschappen worden samengevoegd. **Legenda-omschrijving** bepaalt welke veldwaarden achter
de bestaande omschrijving verschijnen. Standaard zijn beide keuzes leeg; zo blijven bestaande
legenda's gelijk. De maatvoering en vormgeving volgen de bestaande legenda-instellingen.

Elke legenda bewaart haar eigen keuzes. In Overzicht kun je de volgorde op **Handmatig**
zetten en regels verplaatsen. Bij een actief filter is verplaatsen uitgeschakeld. Bestaande
regels behouden hun plaats bij bijwerken; nieuwe combinaties worden achteraan toegevoegd.
Eigen regels met dezelfde omschrijving maar verschillende voorbeelden houden elk hun positie.
Handmatige en blanco regels worden niet samengevoegd. Handmatige regels volgen het statusfilter;
een toewijzing aan een eigen status heeft voorrang op dat filter.

## Symbolen en attributen

Een gevuld attribuut `OMSCHRIJVING` blijft de bron voor de beschrijving van een KLIC-symbool.
Per symboollaag wordt de meest voorkomende niet-lege omschrijving gebruikt. `TYPE` herhaalt
meestal het laag-element; `LABEL` is doorgaans een objectidentificatie. Zijn deze attributen
leeg, dan kan de generieke tekst `TYPE \ LABEL \ OMSCHRIJVING` worden weggelaten via
**KLIC-placeholdertekst weglaten**. Werkelijke objectgegevens blijven beschikbaar in KLIC-details.
