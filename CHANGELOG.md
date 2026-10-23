# Changelog

## 1.35.3 - 2026-10-23

- KLIC-broncontrole in Overzicht en exports groepeert dezelfde veldwaarden niet meer herhaaldelijk. Alle bronverschillen, vergelijkingsregels en verse wijzigingen blijven behouden. Zie [metingen](docs/prestaties.md) voor de afgebakende tijdwinst.
- De losse meettool kan ook een lokaal geopende DWG controleren en registreert processortijd. Een aanvullende vergelijking meet broncontrole en exports met identieke uitvoer.

## 1.35.2 - 2026-10-23

- KLIC-uitlezing maakt minder tijdelijke objecten aan. Ingebedde XML wordt rechtstreeks gelezen; toegang tot de Map-API en Property Sets wordt hergebruikt. Alle bronwaarden en bronverschillen blijven beschikbaar.
- Laagstijlen en ontlede laagnamen worden binnen één analyse hergebruikt. Een volgende analyse leest wijzigingen opnieuw.
- Verschillende lijnpatronen uit dezelfde xref delen tijdens het bouwen één bronkopie. Na de bouwactie wordt die kopie vrijgegeven.
- Een losse Civil 3D-meettool vergelijkt opbouwen en bijwerken, met controles op inhoud, hoeveelheden en getekende uitvoer. Zie [metingen](docs/prestaties.md).

## 1.35.1 - 2026-10-23

- Bijwerken laat ongewijzigde herkennings- en uitsluitingskeuzes ook in de opgeslagen instellingen gelijk.
- **Opmerkingen** volgt weer de templatekleur van de koppenlaag (ACI 130, ByLayer). Een eigen laagkleur per tekening blijft behouden bij het maken en bijwerken van legenda's.

## 1.35.0 - 2026-10-23

- Bijwerken behoudt lokale keuzes, ook bij een andere legendaschaal. Symbolen krijgen een schaalvaste herkenning; een volledig uitgevinkte legenda blijft beheerd.
- **Resetten** zet de gekozen legenda terug naar de globale standaard, met behoud van naam, bron en plaats.
- Aanklikken om uit te sluiten werkt op bronobjecten, geneste symbolen en legendaregels, ook na exploderen en heropenen. Een samengevoegde regel schakelt alle bijbehorende bronregels uit.
- **Samenvoegen** heeft een eigen instellingenpagina met KLIC en overige lagen naast elkaar. KLIC-bronnen worden ook aan hun bronnaam herkend. De diameterkeuze geldt voor laagnamen én objectgegevens; nieuwe KLIC-legenda's voegen diameters standaard samen.
- Samengevoegde waarden verschijnen achter de gemeenschappelijke omschrijving, bijvoorbeeld **Leiding (200, 300)**. Dit kan worden uitgezet.
- Na **Toepassen** zijn Opslaan en Toepassen uitgeschakeld totdat er weer iets wijzigt.

## 1.34.0 - 2026-10-22

- **Diameters samenvoegen** is apart instelbaar voor KLIC en overige lagen. De optie negeert de diameter in de laagnaam, met behoud van leidingsoort, kleur, lijnpatroon en de overige niet-samengevoegde kenmerken. Voor nieuwe KLIC-instellingen staat zij aan; bestaande keuzes blijven behouden.
- KLIC-rioollagen onder hoofdgroep RI gebruiken hun objectgegevens voor de netbeheerder en de KLIC-samenvoegkeuzes. Vrijvervalriool krijgt een herkenbare naam; een samengevoegde automatische omschrijving noemt niet uitsluitend de eerste diameter.
- Samengevoegde regels behouden alle bronobjecten voor selectie, ook bij een handmatige samenvoeging. Hoeveelheden en de afzonderlijke bronnen blijven beschikbaar.

- Een terugkerende GDI+-fout krijgt een begrensd lokaal diagnoselog met de volledige lopende aanroepketen. De start- en lagenvensterproeven slagen met en zonder de plug-in; de oorzaak van de oudere hostmelding is nog niet definitief vastgesteld.

## 1.33.4 - 2026-10-20

- Object Data uit een alleen-lezen xref blijft beschikbaar. Alleen de tijdelijke bronkopie krijgt een verwijderbaar bestandskenmerk; de originele xref blijft alleen-lezen en wordt niet gewijzigd. Zo mislukt de uitlezing niet meer bij het opruimen van de kopie.

## 1.33.3 - 2026-10-20

- KLIC Object Data wordt nu ook uit directe en geneste xrefs gelezen. Waarden blijven gekoppeld aan het oorspronkelijke bronobject; de hoofdtekening hoeft geen eigen Object Data-tabellen te bevatten.
- De eerste analyse leest veldnamen via een tijdelijke, alleen-lezen bronkopie. Volgende analyses hergebruiken de veldindeling. Herladen en wijzigingen aan de tabeldefinities maken die cache ongeldig. Bronbestanden worden niet gewijzigd of automatisch herladen.
- De Map-regressie controleert ook xrefs, behoud van de actieve tekening en het aantal geopende documenten. Ontbrekende of afwijkende KLIC-tabeldefinities geven een leesmelding.

## 1.33.2 - 2026-10-20

- Object Data blijft beschikbaar na het maken of bijwerken van een legenda. De plug-in ruimde de gedeelde Map-tabellenverzameling op, waardoor een volgende analyse of Map-bewerking kon mislukken. Alleen de eigen tijdelijke Map-objecten worden nu vrijgegeven.
- Een nieuwe Civil 3D/Map-proef leest dezelfde tekening drie keer, controleert de tabellen na iedere analyse en vergelijkt objectgegevens en hoeveelheden bij groeperen. Deze controle vereist de volledige CAD-host en wordt niet als Core Console-test meegeteld.

## 1.33.1 - 2026-10-20

- KLIC-samenvoeging herkent een bekende netbeheerder ook als die achter in de laagnaam staat. Spanning, druk en uitvoering blijven daardoor afzonderlijk instelbaar. De beheerder wordt niet dubbel in de legenda vermeld; bronlagen en hoeveelheden blijven behouden.

- Automatische bibliotheekkeuzes vervangen geen bemonsterde bronsymbolen meer. Vorm, maat en rotatie blijven behouden; een bewust gekozen voorbeeld blijft mogelijk.
- Bij automatische arceringsvoorbeelden wordt ook de achtergrondkleur vergeleken. Een gelijke lijnkleur is niet genoeg om een afwijkende vulling over te nemen.
- Bijwerken leest een gewijzigde voorbeeldenbibliotheek opnieuw. Geïmporteerde voorbeelden krijgen eigen blokken en stijlen, zodat gelijknamige hostobjecten de weergave niet veranderen. Andere geplaatste legenda's blijven gelijk.
- De laatst gebruikte voorbeelden blijven beschikbaar zonder bibliotheekbestand, ook na opslaan en heropenen. Een ongewijzigde bibliotheek wordt hergebruikt.
- De CAD-acceptatieronde controleert nu ook bronvormen, achtergrondkleuren, gewijzigde bibliotheken en bestaande kleurvarianten. Met een referentie-DWT worden de drie templateproeven automatisch meegenomen.

## 1.33.0 - 2026-10-20

- Nieuwe legenda's gebruiken de TAUW-maatvoering. Een globaal profiel met de oude standaardmaten wordt gericht bijgewerkt; eigen maatvoering en lokale instellingen blijven behouden.
- Legenda-viewports blijven gekoppeld aan hun legenda en volgen schaalwijzigingen. Met Viewport → Bestaand kan ook een eerder geplaatst venster worden gekoppeld.
- Automatische templatevoorbeelden mogen de bronkleuren niet vervangen. Verschillende kleurvarianten blijven afzonderlijke regels en zijn nu ook afzonderlijk te kiezen in Samenstellen. Expliciet gekozen voorbeelden blijven leidend.
- Opmerkingen gebruikt white (ACI 7). Tekeningnotities, de KLIC-waarschuwing, beide samen of eigen tekst zijn te kiezen en daarna vrij te bewerken.
- Samenvoegen betekent steeds: het aangevinkte kenmerk niet vergelijken. Voor nieuwe KLIC-legenda's staan spanning/druk, uitvoering, volgnummer en netbeheerder aan; soorten samenvoegen staat uit. Overige lagen hebben afzonderlijke keuzes, standaard allemaal uit.
- Overzicht kan geselecteerde regels handmatig samenvoegen. Het gekozen hoofdobject bepaalt titel en weergave; hoeveelheden worden opgeteld. Samenvoegen is omkeerbaar. Verschillende eenheden worden niet op één hoop gegooid.
- Samenstellen heeft aparte tabbladen voor tekeningregels en eigen regels. Zoeken ondersteunt losse woorden in willekeurige volgorde en `*` tussen tekstdelen. Opnemen en uitsluiten kunnen op alle zoekresultaten tegelijk.
- In tekening uitsluiten blijft actief voor meerdere objecten, ook binnen xrefs. Enter past de uitsluitingen toe; Escape annuleert. Bronobjecten blijven staan.


## 1.32.5 - 2026-10-18

- Samenstellen geeft de eigen aankruisafbeeldingen en afbeeldingslijst bij sluiten vrij, ook als het venster nog niet is getoond.
- Instellingen en Laagnaam bewerken ruimen hun eigen lettertypen op. Gedeelde Windows-lettertypen blijven behouden.
- Globale standaard heeft nu ook een teken in het ribbonpictogram. Alle 19 pictogrammen worden op beide afmetingen gecontroleerd.
- Nieuwe regressieproeven controleren daadwerkelijk vrijgegeven tekenresources. De GDI+-melding uit Autodesk LayerManager is daarmee niet als opgelost aangemerkt.

## 1.32.4 - 2026-10-18

- Opslag van de globale standaard herstelt bestanden via een tijdelijke kopie. Een opruimfout verbergt de oorspronkelijke fout niet meer; herstel- en opruimfouten worden samen gemeld.
- Tijdelijke CAD-objecten, xref-kopieën en Map-tabellen krijgen een afzonderlijk opruimbereik. Eén mislukte sluiting houdt de overige bronnen niet meer open; objecten van de CAD-transactie blijven behouden.
- CAD- en vensterproeven melden fouten op dezelfde manier, met testnaam en volledige foutinformatie. Een fouttekst kan niet meer als geslaagde CSV-export worden vergeleken. Vensterproeven lopen na een afzonderlijke mislukking door.
- De KLIC-koppeling meldt ontbrekende Map-methoden expliciet. Gerichte foutafhandeling voorkomt dat programmeerfouten worden behandeld als ontbrekende symbolen of onmeetbare objecten.
- Mislukte xrefinventarisatie levert geen onvolledige keuzelijst meer op. Alles bijwerken meldt welke legenda is mislukt. Ook eerder afgesloten meldingen over testobjecten, groepsobjecten en stille opruimfouten zijn hersteld.
- Lagen, stijlen, attributen en testvoorbeelden gebruiken expliciete selectie. Ongeldige testlagen laten de proef mislukken. Tekstafbreking houdt gecombineerde letters en symbolen heel.

## 1.32.3 - 2026-10-18

- Onleesbare instellingen- en omschrijvingenbestanden geven een foutmelding. Alleen een ontbrekend bestand gebruikt de beginwaarden; een leesfout wordt niet meer als een leeg profiel behandeld.
- Opruimfouten bij tijdelijke legendablokken worden gemeld. Tijdelijke tekenobjecten die niet kunnen worden verwijderd breken de opbouw af, zodat de transactie de vorige legenda kan behouden.
- Terugval voor niet-meetbare CAD-objecten en ontbrekende Windows-vormgeving vangt alleen de bijbehorende soorten fouten op. Diagnostische proeven controleren annuleren en terugdraaien expliciet en melden achtergebleven testbestanden.
- CodeQL-aandachtspunten zijn afzonderlijk beoordeeld. Padopbouw en enkele onnodige vertakkingen zijn vereenvoudigd; de redenen voor behouden meldingen staan in de onderhoudsdocumentatie.

## 1.32.2 - 2026-10-18

- Profielen kunnen weer afzonderlijk worden opgeslagen en geladen voor een bestaande legenda of de globale standaard, ook zonder geplaatste legenda. De melding dat een profiel is geladen verschijnt pas na geslaagd toepassen.
- Overzicht leest alleen de gekozen legenda en onthoudt de gelezen lijst zolang het venster openstaat. Vernieuwen leest de bron opnieuw. Een bronfout blokkeert de overige legenda’s niet; een onleesbare lijst kan niet worden geëxporteerd. Toepassen ververst ook de tekening direct. Lege informatiekolommen blijven verborgen en de lijst wordt in één keer gevuld.
- CSV bevat nu ook de volledige legenda-omschrijving en bronlagen. JSON bevat daarnaast een lijst met bronlagen. Xref-herkomst en de aangevulde KLIC-tekst blijven zo beschikbaar bij gewone export en batch-export; bestaande velden behouden hun betekenis.
- De GitHub-startpagina is ingekort met actuele vensterbeelden. Installatie, handleiding en probleemoplossing hebben eigen pagina’s. Help in de plugin en het bugformulier sluiten daarop aan.

## 1.32.1 - 2026-10-18

- Lokale instellingen en globale standaard hebben afzonderlijke knoppen. Lokale instellingen vallen niet meer terug op de globale standaard wanneer er geen legenda aanwezig is.
- Toepassen en Opslaan bouwen de gekozen legenda direct opnieuw op en verversen het scherm. Bij een fout blijft het venster open met behoud van de vorige legenda.
- Samenstellen zoekt op elk deel van de omschrijving en bronlaagnaam, ook bij xrefs. Groepsvinkjes veranderen tijdens zoeken alleen de zichtbare resultaten. Eigen regels kunnen vanuit een doorzoekbare lagenlijst worden toegevoegd.
- Een breed symbool vergroot de kaders van andere regels niet meer. Vullingen sluiten daardoor weer op hun eigen kader aan. Lange woorden breken binnen de tekstkolom af.
- Gegenereerde NLCS-laagbeschrijvingen worden als gewone namen weergegeven. BGT-symbolen op algemene meubellagen krijgen hun specifieke naam uit het blok; eigen teksten blijven behouden.
- Symbolen met dubbele sluitpunten in een polylijn blokkeren de legenda niet meer. Oude arceringen worden op een tijdelijke kopie opnieuw gemeten; een onmeetbare broncontour krijgt `? m²` en een bronmelding in Overzicht, ook na samenvoegen en in de export.

## 1.32.0 - 2026-10-16

- Overzicht laat per regel een volledig voorbeeld uit een aparte DWG-bibliotheek kiezen, inclusief codes, afdekvlakken en samengestelde lijnen. De bronmaat schaalt mee met de legenda.
- Gebruikte voorbeelden en keuzes blijven in de tekening bewaard, ook na heropenen zonder bibliotheekbestand. Bij meerdere varianten blijft de keuze aan de gebruiker.
- Arceringen behouden hun afzonderlijke voor- en achtergrondkleur. Tekenvolgorde blijft behouden bij het importeren en ontleden van geneste blokken.
- Vlakke symbolen verliezen geen cirkels of polylijnbreedtes meer door een afwijkende Z-schaal. Losse symboolcurven en oude laagsuffixen worden herkend.
- Associatieve bronarceringen worden veilig gekopieerd en losgekoppeld. Hulpcontouren op een arceerlaag leveren geen extra regel op; bronblokken worden niet dubbel geteld.
- Bedrijfseigen voorbeelden blijven buiten de openbare installer.

## 1.31.6 - 2026-10-16

- Scheidingsbanden 100/300, 115/345 en 220/440 krijgen de vier lijnen uit de TAUW-template, met de gemeten onderlinge afstanden, kantlijnen en oorspronkelijke binnenlijntypen.
- De samengestelde voorbeelden schalen mee met de legenda en werken ook vanuit xrefs. Eigen handmatige voorbeelden blijven behouden.

## 1.31.5 - 2026-10-16

- Arceringen behouden de ingesloten patroondefinitie uit de tekening, ook via xrefs en als een gelijknamig PAT-bestand afwijkt.
- Arceervakken hebben een gesloten rand; volle vullingen worden niet meer als een driehoek getekend.
- Symbolen behouden ingevulde blokattributen. Verschillende waarden krijgen afzonderlijke legendaregels.
- Anonieme symboolblokken uit xrefs worden via hun bronverwijzing opgehaald.
- Vullingen blijven achter de symbooltekst. Niet-plotbare hulplijnen in bronblokken komen niet meer als gewone lijnen in de legenda terecht.
- Vijf waarschuwingen over het vrijgeven van CAD-objecten verholpen, ook bij fouten vóór registratie in de transactie.

## 1.31.4 - 2026-10-15

- Legenda’s volgen hun eigen kleur, lijndikte, lijntype en transparantie, ook als de actieve tekeninstellingen afwijken.
- Eigen arceringen en symbolen behouden hun gekozen voorbeeld, ook op een gedeelde bronlaag. Samenstellen bewaart bloknamen, hoofdgroepen en status Overig.
- Een aangeklikte legendaregel verwijst bij tekstbewerking naar de juiste inhoud en legenda, ook na heropenen of exploderen. Oudere legenda’s eerst bijwerken.
- Batchexport leest xrefs; gewone export en viewports gebruiken de instellingen van de gekozen legenda zonder eerst algemene instellingen te laden.
- CSV volgt het gekozen aantal decimalen. KLIC-vensters bewaren de gekozen veldvolgorde.
- Toewijzen aan een andere eigen status verplaatst de regel. De maximale legendahoogte telt de titel, opmerkingen en schaalbalk mee. Annuleren van een bronselectie bewaart de eerdere selectie.

## 1.31.3 - 2026-10-15

- Handmatige regels volgen het statusfilter. Een eigen status blijft een expliciete uitzondering.
- Samenvoegen laat handmatige en blanco regels afzonderlijk staan; eigen voorbeelden verdwijnen niet meer.
- Eigen regels met dezelfde omschrijving maar verschillende lagen, symbolen of arceringen behouden elk hun handmatige positie.
- Een hoge regel of een groot symbool kan het gekozen vaste aantal kolommen niet meer verkleinen.
- Ontbrekende metadata op kabel-/leidingobjecten blijft zichtbaar in een gemengde KLIC-regel,
  bijvoorbeeld als “Niet opgegeven; PE”. Hoeveelheden en brongegevens blijven behouden.
- Codecomments ingekort tot enkele Nederlandse woorden in kleine letters; overbodige toelichtingen verwijderd.
- Gerichte foutafhandeling voor CAD-eigenschappen en vensterverdeling. Een niet te activeren viewport
  en een niet op te slaan lintcontrole worden nu gemeld.
- Vijf regressieproeven toegevoegd, plus een CAD-proef voor gedeeltelijke KLIC-metadata in lokale en xref-bronnen.

## 1.31.2 - 2026-10-15

- Meerregelige omschrijvingen volgen nu de gemeten regelafstand uit de referentietemplate
  (0,624). De eerdere factor 1,35 gaf te veel witruimte en hogere regels.
- Automatische sortering zet Bestaand vóór Nieuw, zoals in de voorbeelden.
- Templateopmaak herstellen zet ook kolomindeling, koppen en voorbeeldweergave terug.
  Eigen teksten, schaal, bronkeuzes en handmatige volgorde blijven behouden.
- Grote statuskoppen en kleine kopafstanden kunnen de titel of vorige groep niet
  meer overlappen. Ook opmerkingen blijven vrij van hun kop bij een kleine afstand.
- De standaardopmaak blijft NLCS-ISO gebruiken als een bestaande hoststijl met die
  naam afwijkt. Bestaande tekststijlen blijven intact; een expliciet gekozen, ontbrekende
  stijl wordt gemeld in plaats van stil vervangen.
- Metadata uit de actieve tekening wordt in de bestaande transactie gelezen. De
  eerdere OpenClose-transactie kon een nog niet opgeslagen arceercontour laten
  verdwijnen. Xref-metadata houdt zijn afzonderlijke, korte leestoegang.
- Extra CAD-acceptatie vergelijkt tekst, stijl, posities op drie schalen en een
  meerregelige omschrijving rechtstreeks met de lokale referentie-DWT.

Voor een bestaande legenda met oude opmaak: Instellingen → Legenda →
Templateopmaak herstellen. Persoonlijke instellingen worden niet automatisch vervangen.

## 1.31.1 - 2026-10-15

- Xrefs aan- of uitzetten overschrijft eerdere uitzonderingen. Een gedeeltelijke keuze
  is zichtbaar in Instellingen. Ook bij het maken van een nieuwe legenda is de xref-lijst beschikbaar.
- Een xref op een NLCS-symboollaag wordt niet meer als los symbool opgenomen wanneer
  die bron is uitgesloten. Een uitgeschakelde bovenliggende xref sluit ook de geneste inhoud uit.
- Symboolimport leest elke bron per koppeling eenmaal tijdens het opbouwen van een legenda.
  Op de lokale KLIC-proef met 306 regels daalde de tekentijd van 112 naar 2,6 seconden;
  de bronanalyse duurde ongeveer 4 seconden. Snelheid hangt af van tekening en computer.
  Bronversies, aparte laagoverrides en gedeelde symboolonderdelen blijven gecontroleerd.
- Technische laagomschrijvingen zoals `NLCS-basis`, herhaalde laagnamen en alleen `KLIC`
  verdringen de gewone benaming niet meer. Expliciete eigen omschrijvingen blijven leidend.
- Instellingen heeft een vaste onderdelennavigatie, één plek per optie en afzonderlijke
  pagina's voor schaalbalk en afmetingen. Overzicht scheidt filters van acties en toont
  verplaatsknoppen alleen bij handmatige volgorde. Filteren hergebruikt de geladen KLIC-waarden.
- De releaseproeven kunnen met `-Isolated` op afzonderlijke testtekeningen naast een
  andere CAD-opdracht draaien, zonder diens opgeslagen CAD-profiel terug te zetten.

## 1.31.0 - 2026-10-14

- Overzicht toont instelbare KLIC-kolommen, een zoekfilter en een filter voor bronmeldingen.
  De gekozen velden en gebruikte bronnen gaan mee in CSV- en JSON-export.
- KLIC-details toont veldwaarden per bron en tabel, bronobjecten, voorzorgsmaatregelen
  en bijlagen. PDF- en afbeeldingsbijlagen kunnen op eigen verzoek worden geopend.
- Materiaal, diameter en andere KLIC-eigenschappen kunnen aan de omschrijving worden
  toegevoegd of voor afzonderlijke regels worden gebruikt. Elke legenda bewaart haar
  eigen keuzes en handmatige volgorde. De bestaande opmaak blijft behouden.
- Ingebedde InfraCAD-gegevens, Property Sets en Object Data worden afzonderlijk gelezen.
  Bronverschillen zijn zichtbaar; de gebruiker kiest welke opslagvorm voorrang krijgt.
  Object Data vereist een geopende bron-DWG in Civil 3D/Map; xrefs gebruiken de overige
  leesbare bronnen. Niet-leesbare bronnen worden gemeld. Vergelijking gebeurt binnen dezelfde KLIC-tabel.
- Diameters worden in millimeters getoond. Niet-opgegeven waarden en standaarddieptes
  worden onderscheiden van opgegeven dieptes; nul wordt niet als gemeten diepte getoond.

## 1.30.1 - 2026-10-14

- De typekeuze onderscheidt VLAk en VULling ook bij bediening via het toetsenbord.
- Viewportproeven controleren echte CAD-objecten met onafhankelijke referentiematen
  en een gezamenlijke einduitslag; een dubbele berekening gold ten onrechte als bewijs.
- De teksthoogteproef controleert ook een bestaande NLCS-ISO-stijl met vaste hoogte.

## 1.30.0 - 2026-10-14

- Instellingen en andere vensters passen zich aan de beschikbare schermruimte aan.
  Lange teksten lopen door en de schaalbalkinstellingen zijn zichtbaar en bereikbaar.
- Overzicht opent een lijstvenster. Kies Handmatig om regels te verplaatsen; iedere
  legenda bewaart haar eigen volgorde, ook na bijwerken en opnieuw openen.
- KLIC-netbeheerders worden uit de ingebedde InfraCAD-metadatatabellen gelezen,
  ook via geneste xrefs. Apart tonen, samenvoegen en afzonderlijk uitsluiten behouden
  de juiste hoeveelheden. Overzicht en export vermelden de netbeheerders.
- De ribbon groepeert de knoppen per taak en gebruikt overal dezelfde icoongrootte.
- Samenstellen en Omschrijvingen ondersteunen Toepassen binnen Instellingen.
  Gedeelde instellingen blijven gelijk tussen tabbladen; eigen laagregels zijn bewerkbaar.
- Symboolonderdelen op laag 0 volgen de zichtbaarheid van hun invoeglaag.
  Hoogspanning wordt als afzonderlijke KLIC-eigenschap herkend.
- Core Console-tests herstellen ook profielwaarden die tijdens een test ontstaan.
  GitHub-controles omvatten nu de vensters en hun bediening.

## 1.29.11 - 2026-10-14

- Overzicht, export, elementcontrole en viewportkeuze tellen alleen aanwezige
  legenda's. Na het wissen van alle legenda's werkt de bronexport weer en meldt
  Bijwerken dat er geen legenda is. De opgeslagen gegevens blijven intact voor Undo.

## 1.29.10 - 2026-10-14

- Instellingen openen direct bij één aanwezige legenda. Gewiste legenda's tellen
  niet mee in de keuze; Undo herstelt hun bereikbaarheid. Standaard opent de
  instellingen voor nieuwe legenda's rechtstreeks.
- Titel, statuskoppen, regelafstand, tekstkolom, opmerkingen en schaalbalk volgen
  de opnieuw gemeten maatvoering. Hulpkaders blijven zichtbaar in modelruimte
  en worden niet geplot. Een lege hosttekening krijgt de juiste NLCS-tekststijl.
- Broninserts worden niet nogmaals geschaald of passend gemaakt in een vakje.
  Expliciete NLCS-schaalvarianten worden eenmaal omgerekend. Grote symbolen
  krijgen meer ruimte; ongelijke schaalverhoudingen blijven behouden.
- Samenvoegen vergelijkt symbolen en arceringen op hun uiterlijk bij de gekozen
  legendaschaal. Werkelijke MText-hoogte bepaalt de rijruimte en statuskoppen
  tellen mee bij de maximale kolomhoogte.
- Bestaande legenda's houden hun eigen instellingen. Opmaak → template zet
  maatvoering en lagen terug; Tekeningnotities herstelt desgewenst de standaardtekst.

## 1.29.9 - 2026-10-13

- Samenstellen gebruikt de bronselectie en omschrijvingen van de gekozen legenda.
  Geplaatste legenda's tellen niet mee; uitsluitingen van tijdelijk ontbrekende bronnen blijven bewaard.
- Globale profielen nemen alle omschrijvingen mee. Opnieuw bewerken vervangt oude
  tekstoverrides en een schrijffout laat de vorige bestanden intact.
  Beschrijvingen vóór het plaatsen gelden alleen voor de nieuwe legenda.
- Bij lagen samenvoegen verhuizen attribuutreferenties mee en blijft een behouden
  bronlaag vergrendeld. Alleen hoofdletters wijzigen wordt als hernoemen behandeld.
- Dialoogcommando's weigeren nu veilig in Core Console. De vensterbibliotheek kon
  bij onder meer Instellingen al vóór de hostcontrole laden en een native crash veroorzaken.
- De systeemvariabeletest controleert vijftien waarden na afzonderlijke bewerkingen,
  met FILEDIA=0 en 1. Een onleesbare waarde kan niet meer als geslaagd tellen.
- De testrunners controleren ook de opgeslagen profielwaarden, herstellen afwijkingen
  exact en stoppen bij fouten, ontbrekende resultaten, timeouts of een mislukte build.
  Tests gebruiken kopieën en starten alleen zonder andere CAD-processen.

## 1.29.8 - 2026-10-13

- Opgeslagen legenda's met ontbrekende kerngegevens worden geweigerd. Een ontbrekend
  ID, bronselectie of instellingenblok wordt niet meer stil met standaardwaarden
  ingevuld. De bestaande gegevens blijven behouden, ook bij batch-export.

## 1.29.7 - 2026-10-13

- Een ongeldige schaal in een laagnaam breekt de analyse niet meer af. Lagen met een
  substatus of een langere discipline- of hoofdgroepcode worden ook herkend.
- Omschrijvingen blijven na inlezen ongevoelig voor hoofdletters. Bij een variant krijgt
  de meest specifieke omschrijving voorrang.
- Onleesbare profielen worden geweigerd voordat ze een bestaand profiel kunnen vervangen.
  Beschadigde legendaopslag blijft behouden en wordt duidelijk als fout gemeld,
  ook tijdens batch-export.
- De uitleg over laagnamen, standaardmaten en eigen instellingen is bijgewerkt.

## 1.29.6 - 2026-10-13

- Gemengde eigen lagen en afzonderlijke blokfilters behouden alle legendaonderdelen.
- Symbolen worden vergeleken op hun zichtbare vorm en eigenschappen. Laag 0, ByLayer en
  ByBlock behouden hun eigen betekenis; verborgen onderdelen splitsen geen zichtbare regel.
- Gebruikersarceringen behouden afstand en kruispatroon. Rotatie en schaal van een
  bronblok werken ook door in het arceringsvoorbeeld.
- Een gesloten lijncontour verdubbelt het oppervlak van een bijbehorende arcering niet meer.
  Omschrijvingen op samengevoegde bronlagen blijven beschikbaar.
- Bij een gewijzigd xref-bestand vraagt de plugin eerst om herladen. Een mislukte update
  behoudt de bestaande legenda.
- INFO en export gebruiken de bron en instellingen van de gekozen legenda. Een verdwenen
  selectie valt niet terug op de hele tekening; latere globale omschrijvingen veranderen
  bestaande legenda's niet.
- De releasecontrole bevat extra regressietests en controleert isolatie en migratie na heropenen.

## 1.29.5 - 2026-10-13

- Legenda's behouden de effectieve bronkleur, lijndikte, transparantie en het lijntype,
  ook wanneer objecten op dezelfde laag verschillende eigenschappen hebben.
- Verschillende arceringen en symbolen op één bronlaag krijgen eigen regels en hoeveelheden.
  Arceringsgrenzen worden niet dubbel geteld; geschaalde oppervlakken worden in het juiste vlak gemeten.
- Xref-symbolen worden met hun database-afhankelijkheden naar de host gekopieerd.
  Herhaald bijwerken voegt geen nieuwe kopieën van dezelfde stijlen en materialen toe.
- Vlakke symbolen met een afwijkende Z-insertschaal behouden hun geometrie en arcering.
- Een tekenfout draait de plaatsing of update terug en behoudt de bestaande legenda.
- Meerregelige omschrijvingen staan op gelijke hoogte met hun voorbeeld; lege symboolblokken leveren geen lege regel op.
- De voorbeeldconfiguratie gebruikt dezelfde standaard statusteksten als nieuwe legenda's.

## 1.29.4 - 2026-10-12

- Dezelfde NLCS-laag uit host- en xref-bronnen wordt alleen samengevoegd wanneer de zichtbare
  weergave gelijk is. Verschillende kleuren, lijntypen, lijngewichten, arceringen en
  symboolrenderingen houden hun eigen regels, swatches en hoeveelheden.
- Xref-symbolen worden bij plaatsing in de hosttekening opgebouwd; tekststijlen worden op basis
  van hun naam in de hostdatabase opgelost.

## 1.29.3 - 2026-10-07

- Een legenda die alleen uit een externe referentie (xref) wordt opgebouwd, krijgt nu volledig
  gevulde sample-vakken in de juiste kleur. Eerder bleven die vakken soms leeg of wit, of kregen ze
  een verkeerde kleur.
- Het bijwerken van zo'n legenda na opslaan en opnieuw openen werkt betrouwbaar, ook bij geneste
  externe referenties.
- De standaard statusteksten ("Nieuwe situatie", "Bestaande situatie", en zo verder) en de
  meegeleverde voorbeeldconfiguratie sluiten weer aan op de rest van de opmaak. De teksthoogtes voor
  omschrijving, kop en titel blijven vrij instelbaar.

## 1.29.2 - 2026-10-07

- De opmaak van de gegenereerde legenda sluit nu beter aan op het referentiemateriaal: de titel is
  5 mm, de statuskoppen zijn 2,5 mm en onderstreept, en de ruimte tussen het sample-vak en de tekst
  is iets kleiner. Nieuwe legenda's en "terug naar template" gebruiken deze maten; bestaande
  legenda's houden hun eigen instellingen tot je ze terugzet naar het template.

## 1.29.1 - 2026-10-07

- De keuzes in de commandoregelmenu's zijn nu gewone woorden. Eerder stond er soms een hoofdletter
  midden in een woord om een sneltoets vrij te maken, wat op een schrijffout leek.
- Het lint is logischer ingedeeld: een apart paneel voor instellingen en een paneel Bronnen met
  alleen de tekening-bronnen.
- Kleine opschoning van zichtbare teksten en van de code achter de schermen, zonder dat er iets
  aan het resultaat verandert.

## 1.29.0 - 2026-10-07

- Nieuw helpvenster in het lint (en het commando NLCSLEGENDAHELP) met korte uitleg over de
  belangrijkste functies, zonder internet.
- Het lint is opnieuw ingedeeld: de hoofdacties staan groot vooraan en de rest is verdeeld
  over overzichtelijke panelen.
- Teksten, knoplabels en tooltips zijn opgeschoond en de sneltoetsen in de commandoprompts
  zijn nu allemaal uniek.

## 1.28.2 - 2026-10-07

- Tekst die uit een bronsymbool wordt overgenomen, behoudt nu ook de breedtefactor, schuinte
  en oriëntatie bij de omzetting naar MText, zodat zo'n symbool er in de legenda hetzelfde
  uitziet als in de tekening.

## 1.28.1 - 2026-10-06

- Tekst in bronsymbolen wordt bij het overnemen ook MText, zodat een gegenereerde legenda
  echt geen enkele DBText meer bevat.
- README opgeschoond en overzichtelijker ingedeeld.

## 1.28.0 - 2026-10-06

- De tekst in een gegenereerde legenda bestaat nu volledig uit MText-objecten. Je kunt die in
  AutoCAD net als gewone tekst selecteren en bewerken. De legenda ziet er hetzelfde uit.
- Een bestaande legenda werk je gewoon bij met `NLCSLEGENDAUPDATE`; de oude tekst wordt daarbij
  vervangen, met behoud van plek, bron en instellingen. Een apart conversiecommando is niet nodig.
- Bij het bijwerken worden ongebruikte legenda-blokken opgeruimd, zodat er geen oude tekst
  achterblijft in de tekening.
- In het lint zijn alleen de hoofdacties groot; de overige knoppen staan compacter bij elkaar.

## 1.27.1 - 2026-10-06

- Opgelost: de lint-tab *NLCS Legenda* verscheen niet altijd meteen bij het opstarten. De tab
  komt nu direct in beeld en blijft ook staan na het wisselen van werkruimte.
- Een blanco regel toont geen hoofdgroepkop meer en blijft leeg in de CSV/JSON-export; de
  overige regels veranderen niet.

## 1.27.0 - 2026-10-06

- Nieuw: blanco legendaregels. Via *Blanco regels* in het instellingenvenster reserveer je een
  of meer lege regels (leeg vakje, standaardtekst "[blanco]") om later met de hand iets toe te
  voegen. Ze hebben geen bron en geen hoeveelheid, blijven bij bijwerken/opslaan/heropenen
  bestaan en staan los van automatische NLCS-bronnen, gekoppelde eigen lagen en handmatige regels.
- Opgelost: bij herhaald bijwerken kon een legenda zichzelf voeden. De vorige legenda bleef als
  losse geometrie staan en werd bij een volgende update als bron meegeteld, waardoor lengtes en
  aantallen opliepen. Een update houdt de beheergroep nu in stand, zodat oude geometrie
  betrouwbaar wordt gewist.
- Een eigen bronlaag die in meerdere bronnen (hoofdtekening/xref) voorkomt met een zichtbaar
  andere weergave levert nu aparte legendaregels per bron in plaats van een willekeurige; bij
  gelijke weergave telt de hoeveelheid op tot één regel.
- De omschrijving van een eigen bronlaag kun je nu net als NLCS per legenda overschrijven.
- De viewport-meettest laat de actieve layout niet meer gewijzigd achter.

## 1.26.1 - 2026-10-05

- Eigen bronlagen in een xref werken nu echt: de bron *elke bron* en *specifieke xref* tellen
  de laag in de host en in xrefs correct op, ook als dezelfde laagnaam in host en xref voorkomt.
- Een bevroren of uitgezette eigen laag valt nu net als een NLCS-laag weg, tenzij je
  *onzichtbare lagen meenemen* aanzet.
- De omschrijving van een eigen laag toont in de export de herkomst *eigen koppeling* in plaats
  van *laagbeschrijving*, en `NLCSLEGENDAINFO` telt eigen lagen apart van de NLCS-lagen.
- De bronkeuze in het venster *Eigen lagen* staat nu in het Nederlands (lokaal, elke bron,
  specifieke xref).

## 1.26.0 - 2026-10-05

- Je kunt nu eigen (niet-NLCS) lagen als echte bron koppelen via *Eigen lagen* in het
  instellingenvenster. Zo'n laag telt gelijkwaardig mee: lengte, aantal of oppervlak uit de
  tekening, met eigen omschrijving, status en swatch, en werkt op de hele tekening, een selectie,
  xrefs en in de uittrekstaat. Anders dan een handmatige regel verschijnt een eigen laag alleen
  als er objecten op staan.

## 1.25.0 - 2026-10-05

- Eigen statussen met dezelfde naam worden nu geweigerd, zodat je geen dubbele kopregels krijgt.
- Het label voor het legendakader is overal gelijk, en bij het opslaan en laden van een profiel
  is duidelijk of je dat vanuit of naar de globale standaard of een legenda doet.
- Een instelling buiten het toegestane bereik wordt in het instellingenvenster meteen
  gelijkgetrokken met wat je ziet.

## 1.24.0 - 2026-10-04

- Het instellingenvenster is nu volledig met normale bedieningselementen op elk tabblad. De
  per-xref keuze, eigen statussen inclusief ledentoewijzing en de omschrijvingen stel je in
  vanuit datzelfde venster; de losse commando's blijven als snelkoppeling bestaan.
- NLCS-symbolen en arceringen in doorsnede en niet-zichtbare symbolen (aspectcodes AD, SD, SN,
  SV) worden nu als symbool of arcering herkend.

## 1.23.0 - 2026-10-03

- KLIC-symbolen gebruiken nu de echte omschrijving uit het attribuut OMSCHRIJVING (bijv.
  "Distributieleiding") in plaats van alleen de laagnaam.
- Het instellingenvenster heeft normale bedieningselementen voor Algemeen en Inhoud (schaal,
  titel, aankruisvakjes) in plaats van alleen een eigenschappenlijst.
- Een profiel opslaan bewaart nu de instellingen van de gekozen bron (globale standaard of een
  gekozen legenda), niet altijd de globale standaard.
- Bij het bijwerken wordt een waarschuwing getoond als een regel niet kon worden getekend.

## 1.22.0 - 2026-10-03

- Samenstellen (uitsluitingen en eigen regels) is nu ook bereikbaar vanuit het
  instellingenvenster, naast het losse commando.
- Omschrijvingen gebruiken intern één model; de tekeningopslag volgt één actueel schema.

## 1.21.1 - 2026-10-02

- Symbolen in de legenda worden nu op de NLCS-bronschaal (schaal/1000) getekend in plaats van
  een vaste vulgraad van het swatchvak; bij overloop wordt nog steeds naar het vak teruggeschaald.

## 1.21.0 - 2026-10-02

- Veiliger overnemen van oude tekeningconfiguratie: oude instellingen en omschrijvingen worden
  nu in dezelfde stap overgenomen en pas daarna opgeruimd (bij een afbreking blijft de oude
  configuratie dus volledig bestaan). Onleesbare oude configuratie wordt niet overgenomen en
  niet gewist.
- Omschrijvingen per legenda zijn nu een onafhankelijke momentopname: wijzig je later de
  globale omschrijvingen, dan verandert een bestaande legenda niet meer mee. Een nieuwe legenda
  krijgt wel de nieuwe globale tekst.
- Omschrijvingen en elementteksten gebruiken intern één model.
- Laagnaameditor toont bij een naamsbotsing de volledige eigenschappen van de bestaande laag.

## 1.20.0 - 2026-10-02

- Omschrijvingen kunnen nu per legenda verschillen: `NLCSLEGENDAOMSCHRIJVINGEN` vraagt waarop
  je het toepast (globale standaard of één legenda). Oude tekeningbrede omschrijvingen worden
  bij het openen netjes overgenomen in de betreffende legenda in plaats van gedeeld te blijven.
- Onleesbare oude configuratie wordt niet meer stil vervangen en gewist; de migratie slaat
  dan over zodat er niets verloren gaat.
- Gelijke statussen samenvoegen houdt nu ook rekening met de symboolschaal, -rotatie en
  -spiegeling.
- Laagnaameditor: vergrendelde bronlaag wordt gemeld en veilig behandeld, samenvoegen met een
  bestaande laag vraagt een expliciete bevestiging en toont de doellaag.

## 1.19.0 - 2026-10-02

- Instelcommando's werken nu per legenda of op de globale standaard: je kiest het doel en een
  bestaande legenda wordt meteen bijgewerkt. Teksten kunnen zo per legenda verschillen.
- Samenstellen heeft een groepsboom met drie-standen-vinkjes (groep in één klik aan/uit) en
  een filter.
- Instellingenvenster met tabbladen (Algemeen, Inhoud, Opmaak, Teksten, Hoeveelheden,
  Schaalbalk / Extra) in plaats van één lange lijst.
- Nieuw commando `NLCSLEGENDALAAGNAAM`: een NLCS-laagnaam component voor component bewerken
  met live preview en validatie, en de laag hernoemen in één (ongedaan te maken) stap.
- Gelijke statussen samenvoegen kijkt nu ook naar arcering (patroon/schaal/hoek) en
  transparantie.
- Template-maten staan op één centrale plek, afgestemd op de referentielegenda.

## 1.18.0 - 2026-10-01

- Gelijke statussen samenvoegen: voegt dezelfde regel uit verschillende statussen samen,
  maar alleen als lijn, vlak, arcering en symbool er precies hetzelfde uitzien. In te stellen
  onder *Groepering*.

## 1.17.0 - 2026-10-01

- KLIC-groepering: voeg kabels/leidingen in de legenda samen op soort, spanning/druk,
  uitvoering (mantelbuis/hulpstuk) of volgnummer. Zo worden bijvoorbeeld DATA, DATA2 en
  DATA3 één regel. Hoeveelheden tellen op en het is omkeerbaar. In te stellen onder
  *Groepering*, globaal of per legenda.
- Generieke placeholdertekst uit KLIC-symbolen (zoals "TYPE \ LABEL \ OMSCHRIJVING") wordt
  standaard weggelaten; echte omschrijvingen blijven staan.

## 1.16.0 - 2026-10-01

- Commando's die een venster nodig hebben (klikken, dialogen) weigeren nu netjes in de
  AutoCAD Core Console in plaats van te kunnen crashen. Headless blijven `NLCSLEGENDAINFO`,
  `NLCSLEGENDATEST`, `NLCSLEGENDAEXPORT`, `NLCSLEGENDAUPDATE` en `NLCSLEGENDABATCHTEST`
  werken.
- Bij meerdere legenda's kiezen `NLCSLEGENDAUPDATE`/`NLCSLEGENDAEXPORT` headless de doel-
  legenda via de omgevingsvariabele `NLCSLEGENDA_TARGET` (legenda-id of unieke naam); zonder
  die keuze weigeren ze veilig zonder iets te wijzigen.
- Het controlecommando heet nu `NLCSLEGENDAELEMENT` (ribbon: "Element controleren") met
  compacte uitvoer; `NLCSLEGENDAWAAROM` blijft als alias werken.
- Een regel die niet volledig getekend kan worden wordt nu gemeld in plaats van stil
  overgeslagen; een bijgewerkte legenda met zulke regels geldt niet meer als volledig
  geslaagd.
- De oude "Alleen deze tekening"-instellingenscope is vervallen. Instellingen zijn nu de
  globale standaard (voor nieuwe legenda's) of de eigen instellingen van één bestaande
  legenda (via `NLCSLEGENDABEHEER`). Oude tekeningspecifieke configuratie wordt nog gelezen
  en kan met `NLCSLEGENDACONFIG` worden gewist.
- Knoppen in de instellingenvensters groeien mee met hun tekst, zodat langere labels niet
  meer worden afgekapt.

## 1.15.0 - 2026-09-23

- Legenda-opmaak volgt de NLCS-template: tekststijl `NLCS-ISO`, standaard teksthoogtes
  (2,5 mm regels, 5 mm koppen, 7 mm titel) en de tekstlagen `-T25`/`-T50`.
- Een geometrieregel (G) blijft altijd een lijn, ook als hetzelfde element een arcering of
  vulling heeft. Vlakken, vlakvullingen, arceringen en symbolen renderen elk apart.
- `Kader per swatch` en `Symboolblokken invoegen` werken nu zoals verwacht: staat een optie
  uit, dan wordt het vakje niet omkaderd respectievelijk geen symbool of vervangende cirkel
  getekend.
- Nieuwe legenda's worden standaard als één blok geplaatst (niet meer geëxplodeerd).
- Instellingen per legenda: in `NLCSLEGENDABEHEER` pas je de opmaak van één gekozen legenda
  aan; de legenda wordt daarna in dezelfde bewerking opnieuw opgebouwd.
- Instellingen overnemen van een andere legenda, een legenda dupliceren, de bronselectie
  aanpassen (vervangen/toevoegen/verwijderen) en de opmaak van een legenda als globale
  standaard instellen.
- `NLCSLEGENDAWAAROM`: klik een object aan en zie of en waarom het wel of niet in een
  gekozen legenda komt.
- Meerregelige opmerkingen met een echte editor; regeleindes en opsommingen blijven behouden.
- `Opmaak → template` zet alleen de opmaak terug, niet de bron, filters of eigen regels.

## 1.14.0 - 2026-09-23

- Meerdere legenda's kunnen naast elkaar in één tekening staan, elk met een eigen bron
  en eigen instellingen.
- Een legenda wordt gemaakt van de hele tekening of van een opgeslagen selectie; bij
  bijwerken houdt hij die bron aan.
- Bij meerdere legenda's kies je bij het bijwerken welke wordt vernieuwd (of werk alles
  bij); een legenda die geen regels meer oplevert wordt niet stil verwijderd.
- `NLCSLEGENDABEHEER` beheert de legenda's: bekijken, bijwerken, hernoemen, opzoeken en
  verwijderen. De oude samenstel-dialoog heet nu `NLCSLEGENDASAMENSTELLEN`.
- Globale instellingen zijn het startpunt voor nieuwe legenda's; een bestaande legenda
  verandert niet mee als de globale standaard later wijzigt.
- Viewport en export richten zich op de gekozen legenda. Geplaatste legenda's tellen niet
  meer mee als brondata bij het analyseren, ook niet in de batch-uittrekstaat.
- Hoeveelheden kloppen nu ook bij een blok dat meerdere keren is ingevoegd en bij
  geschaalde of geroteerde blokken.

## 1.13.0 - 2026-09-22

Genereert uit de NLCS-lagen van een Civil 3D- of AutoCAD-tekening automatisch een
legenda in NLCS-stijl: swatches met lijnen, arceringen, vlakvullingen en symbolen, de
bijbehorende tekst, en optioneel hoeveelheden, een schaalbalk en een opmerkingenblok.

- Plaatsen (met de muis), bijwerken op dezelfde plek, en een viewport op schaal.
- Per elementsoort te kiezen wat meekomt: geometrie/lijnen, vlakken, arceringen,
  vlakvullingen en symbolen, elk apart aan of uit.
- Filteren op status, plus eigen statussen waaraan je regels toewijst.
- Regels uitvinken en eigen regels toevoegen; instellingen globaal of per tekening.
- Externe referenties per xref in- of uitschakelen (geneste xrefs volgen de bovenliggende).
- Instellingen als profiel opslaan, laden en im-/exporteren om te delen.
- Export naar CSV en JSON, en een gecombineerde uittrekstaat over een hele map tekeningen.
- Werkt met AutoCAD/Civil 3D 2025, 2026 en 2027; installatie via MSI of zip.
