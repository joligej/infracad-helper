# Diagnostische commando's

Naast de commando's in de README registreert de plugin een aantal commando's die alleen
bedoeld zijn voor headless tests en acceptatiecontroles (CI en de installatiecontrole op een
echte AutoCAD-host). Ze staan bewust niet in de README en er is geen HELP-commando dat ze
toont, dus een normale gebruiker komt ze niet tegen. Ze blijven wél in de release-dll omdat de
acceptatietests ze tegen de geïnstalleerde bundle draaien; conditioneel compileren zou juist die
controle onmogelijk maken.

| Commando | Doel |
| --- | --- |
| `NLCSLEGENDATEST` | Bouwt headless een legenda en rapporteert eventuele renderissues. |
| `NLCSLEGENDABATCHTEST` | Draait de batch-uittrekstaat headless op een map. |
| `NLCSLEGENDADESCTEST` | Controleert de omschrijvingsbron-resolutie per regel. |
| `NLCSLEGENDAISOLATIETEST` | Controleert dat instellingen per legenda geïsoleerd blijven. |
| `NLCSLEGENDAKLICATTRTEST` | Diagnose van het KLIC-attribuut OMSCHRIJVING per symboollaag. |
| `NLCSLEGENDALAAGNAAMTEST` | Parset en hernoemt NLCS-laagnamen component voor component. |
| `NLCSLEGENDAPERFTEST` | Meet de analysetijd op een grote KLIC-tekening. |
| `NLCSLEGENDATEMPLATEMETEN` | Meet de maatvoering (swatch, rijafstand, teksthoogtes) van een bestaande legenda. |
| `NLCSLEGENDAUNDOSETUP` / `NLCSLEGENDAUNDORENAME` | Zetten een laaghernoem-/undo-regressie klaar en voeren die uit. |
| `NLCSLEGENDAABCSETUP` / `NLCSLEGENDAABCVERIFY` | Bouwen drie legenda's met verschillende bronnen (twee selecties + hele tekening) en config; na QSAVE/heropenen controleren ze bronnen, instellingen, global-isolatie, inhoudelijke vingerafdrukken, export- en viewport-isolatie en update-isolatie. |
| `NLCSLEGENDAABCXREFSETUP` / `NLCSLEGENDAABCXREFVERIFY` | Koppelen twee synthetische xrefs en controleren na heropenen dat A alleen xref A meeneemt, B alleen xref B en C allebei. |
| `NLCSLEGENDACONSUMERTEST` | Controleert dat zichtbare schakelaars (kader, swatchkader, schaalbalk, titel, opmerkingen, hoeveelheden, symbolen) de getekende geometrie veranderen. |
| `NLCSLEGENDACUSTOMTEST` | Maakt geometrie op niet-NLCS-lagen en controleert dat gekoppelde eigen lagen gelijkwaardig meetellen (lengte/aantal/oppervlak, selectie, blok-identiteit). |
| `NLCSLEGENDAEIGENANYTEST` | Controleert dat een *elke bron*-regel met zichtbaar verschillende weergave per bron aparte regels oplevert, en bij gelijke weergave één opgetelde regel. |
| `NLCSLEGENDAEIGENHATCHTEST` | Koppelt een echte arcering en een echt blok op eigen lagen en controleert dat de gebouwde legenda echt een Hatch-entiteit en een symboolinvoeging bevat. |
| `NLCSLEGENDAEIGENBATCHTEST` | Draait de batch op twee tijdelijke DWG's (met/zonder eigen laag) en controleert dat alleen de juiste een eigen regel geeft en de bron-DWG's qua hash onveranderd blijven. |
| `NLCSLEGENDAEIGENUPDATETEST` | Werkt een eigen-bronlegenda 10 en 50 keer bij en controleert dat rijtal, hoeveelheden en het aantal beheerde blokken stabiel blijven (geen zelfvoeding). |
| `NLCSLEGENDABLANCOTEST` | Controleert dat blanco regels als echte rijen worden gebouwd (leeg vakje, tekst "[blanco]"), de legenda hoger maken en geen renderissue geven. |
| `NLCSLEGENDAMTEXTTEST` | Controleert dat een complete legenda (alle tekstcategorieën) geen DBText maar alleen MText bevat, zowel behouden als geëxplodeerd, zonder verweesde tekstblokken. |
| `NLCSLEGENDASYMBOOLTEKSTTEST` | Controleert dat tekst binnen een bronsymbool bij het overnemen MText wordt, zodat de legenda ook dan geen DBText bevat. |
| `NLCSLEGENDAOPMAAKMEETTEST` | Meet de teksthoogtes en swatchbreedte van een gebouwde legenda en vergelijkt ze met de instellingen. |
| `NLCSLEGENDAVOLOPMAAKTEST` | Brede opmaakcontrole: meet per tekstsoort hoogte/uitlijning/laag, de swatch-afmetingen, lijnsamples, arceringen, symbolen en de tekstruimte tegen de instellingen. |
| `NLCSLEGENDASYMBOOLTEKST2TEST` | Controleert dat de symbooltekst-conversie uitlijning, breedtefactor, oblique, rotatie, normaal en Unicode behoudt. |
| `NLCSLEGENDAOLDUPDATETEST` | Bootst een oude legenda met DBText na en controleert dat bijwerken dezelfde legenda MText-only teruggeeft met behoud van id, bron, instellingen en positie. |
| `NLCSLEGENDAOLDUPDATEBROADTEST` | Hetzelfde maar voor WholeDrawing, Selection (met bronhandles), een eigen bronlaag, blanco regels en xref-instellingen naast elkaar. |
| `NLCSLEGENDAOLDREOPENSETUP` / `NLCSLEGENDAOLDREOPENVERIFY` | Oude DBText-Selection-legenda; na QSAVE/heropenen bijwerken en controleren dat tekst MText-only wordt met behoud van bronhandles en plaats. |
| `NLCSLEGENDASYSVARTEST` | Legt relevante systeemvariabelen vast, draait de hoofdcommando's en controleert dat elke waarde daarna exact gelijk is (geen state-lek). |
| `NLCSLEGENDAEIGENXREFTEST` | Koppelt dezelfde eigen laagnaam in host (3 m) en twee xrefs (5 m en 11 m) en controleert dat Lokaal alleen de host telt (3), SpecifiekeXref alleen die xref (5), ElkeBron alle bronnen (19) en dat xref-uitsluiting geen regel oplevert. |
| `NLCSLEGENDAEIGENZICHTBAARTEST` | Bevriest een eigen laag en een NLCS-laag en controleert dat beide gelijk reageren op "onzichtbare lagen meenemen". |
| `NLCSLEGENDAVIEWPORTTEST` | Controleert viewport-plaatsing en -schaal rond de legenda. |
| `NLCSLEGENDAXREFSETUP` / `NLCSLEGENDAXREFVERIFY` | Bouwen een xref-scenario en verifiëren de opname ervan. |
| `NLCSLEGENDAXREFANALYSE` | Analyseert een tekening met echte xrefs en toont dat alleen de ingesloten xref zijn NLCS-elementen bijdraagt. |

De testcommando's zijn read-only of werken op tijdelijke testfixtures; ze wijzigen geen
gebruikersinstellingen en zetten `SECURELOAD`/`FILEDIA` netjes terug.
