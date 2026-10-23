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
Object Data wordt ook uit directe en geneste xrefs gelezen. De waarden komen van de
geladen bronobjecten. Voor de veldnamen opent de plugin bij de eerste analyse een
tijdelijke, alleen-lezen kopie van de bron-DWG. Deze wordt meteen gesloten en verwijderd;
de veldindeling blijft beschikbaar zolang de geladen xref ongewijzigd is.
De hoofdtekening blijft actief en de plugin herlaadt geen xrefs. Een gewijzigde bronversie
wordt gemeld, zodat waarden niet aan een verkeerde veldindeling worden gekoppeld.
De eerste analyse kan daardoor langer duren dan opnieuw bijwerken.
In Core Console wordt Object Data niet gelezen. In gewone AutoCAD kunnen beide ontbreken.
Overzicht vermeldt wanneer een aanwezige opslagvorm niet kon worden gecontroleerd.
Die melding betekent niet dat de overige bronnen ook onleesbaar zijn.

## Kolommen en bronkeuze

Bij **KLIC-instellingen → Overzicht en export** kies je velden zoals thema, materiaal,
diameter, druk, spanning, KLIC-melding, aanvraagdatum en aanvraagsoort. De gekozen velden
verschijnen ook in CSV/JSON. De export vermeldt gebruikte bronnen en bronmeldingen.
**Exporteer lijst** in Overzicht exporteert alleen de regels die het huidige filter toont.
Het zoekfilter wijzigt de inhoud van de getekende legenda niet. CSV bevat naast de
oorspronkelijke omschrijving ook Legenda-omschrijving en Bronlagen; JSON bevat
`omschrijving`, `legendaOmschrijving` en `bronlagen`. Xref-voorvoegsels blijven zichtbaar.

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

In **Instellingen → Samenvoegen** betekent een aangevinkt samenvoegkenmerk dat dit kenmerk niet
wordt vergeleken. **Soorten samenvoegen** staat standaard uit: gas en water blijven
verschillende soorten. Voor nieuwe KLIC-legenda's staan specificatie (spanning/druk),
uitvoering, volgnummer, diameter en netbeheerder standaard aan. Overige lagen hebben aparte keuzes,
standaard allemaal uit. Soort, specificatie, uitvoering, diameter en volgnummer komen
uit de NLCS-laagnaam; de netbeheerder komt uit de objectgegevens. DATA2 bewijst dus niet wie
de netbeheerder is. Ook objectgegevens of een bronnaam met KLIC herkennen KLIC-riool onder hoofdgroep RI.
Bij **Diameters samenvoegen** verliezen de samengevoegde regels geen bronobjecten;
de afzonderlijke diameters blijven terug te vinden in KLIC-details en de bronlagen.
Hoeveelheden worden opgeteld; zichtbaar verschillende symbolen of
arceringen blijven gescheiden.

De diameterkeuze geldt voor zowel de laagnaam als objectgegevens. Onder
**KLIC-instellingen → Afzonderlijke regels** kun je andere extra velden, zoals materiaal,
aanvinken. Verschillende combinaties krijgen dan aparte legendaregels, ook als andere
eigenschappen worden samengevoegd. **Legenda-omschrijving** bepaalt welke veldwaarden achter
de bestaande omschrijving verschijnen. Standaard zijn beide keuzes leeg; zo blijven bestaande
legenda's gelijk. De maatvoering en vormgeving volgen de bestaande legenda-instellingen.

**Samengevoegde waarden tonen** voegt de verschillende waarden tussen haakjes toe,
bijvoorbeeld **Vrijvervalriool (200, 300)**. Zet dit uit voor alleen **Vrijvervalriool**.
De gegevens blijven dan beschikbaar in Overzicht en KLIC-details.

Elke legenda bewaart haar eigen keuzes. In Overzicht kun je de volgorde op **Handmatig**
zetten en regels verplaatsen. Bij een actief filter is verplaatsen uitgeschakeld. Bestaande
regels behouden hun plaats bij bijwerken; nieuwe combinaties worden achteraan toegevoegd.
Eigen regels met dezelfde omschrijving maar verschillende voorbeelden houden elk hun positie.
Eigen en blanco regels vallen buiten automatisch samenvoegen. **Overzicht → Samenvoegen**
kan zelf gekozen regels combineren met één hoofdobject voor de weergave en een gedeelde titel; blanco
regels en verschillende eenheden worden geweigerd. **Samenvoeging opheffen** maakt
de handmatige groep weer los. Eigen regels volgen het statusfilter;
een toewijzing aan een eigen status heeft voorrang op dat filter.

## Symbolen en attributen

Een gevuld attribuut `OMSCHRIJVING` blijft de bron voor de beschrijving van een KLIC-symbool.
Per symboollaag wordt de meest voorkomende niet-lege omschrijving gebruikt. `TYPE` herhaalt
meestal het laag-element; `LABEL` is doorgaans een objectidentificatie. Zijn deze attributen
leeg, dan kan de generieke tekst `TYPE \ LABEL \ OMSCHRIJVING` worden weggelaten via
**KLIC-placeholdertekst weglaten**. Werkelijke objectgegevens blijven beschikbaar in KLIC-details.

## Controle in Civil 3D/Map

Open een kopie van een KLIC-brontekening of een hoofdtekening met KLIC-xrefs en voer
`NLCSLEGENDAMAPDATATEST` uit. De proef analyseert drie keer, waarvan eenmaal met
materiaal en diameter als groepering. Object Data, hoeveelheden en de gedeelde
Map-tabellen moeten daarna nog gelijk en leesbaar zijn. Ook de actieve tekening en het
aantal geopende documenten worden gecontroleerd. De proef wijzigt de bron
niet. Core Console kan deze controle niet uitvoeren.
