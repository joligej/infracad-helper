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
| `NLCSLEGENDATEMPLATEMETEN` | Meet de legendageometrie tegen de referentietemplate. |
| `NLCSLEGENDAUNDOSETUP` / `NLCSLEGENDAUNDORENAME` | Zetten een laaghernoem-/undo-regressie klaar en voeren die uit. |
| `NLCSLEGENDAABCSETUP` / `NLCSLEGENDAABCVERIFY` | Bouwen drie legenda's met verschillende bronnen (twee selecties + hele tekening) en config; na QSAVE/heropenen controleren ze bronnen, instellingen, global-isolatie, inhoudelijke vingerafdrukken, export- en viewport-isolatie en update-isolatie. |
| `NLCSLEGENDAABCXREFSETUP` / `NLCSLEGENDAABCXREFVERIFY` | Koppelen twee synthetische xrefs en controleren na heropenen dat A alleen xref A meeneemt, B alleen xref B en C allebei. |
| `NLCSLEGENDACONSUMERTEST` | Controleert dat zichtbare schakelaars (kader, swatchkader, schaalbalk, titel, opmerkingen, hoeveelheden, symbolen) de getekende geometrie veranderen. |
| `NLCSLEGENDAVIEWPORTTEST` | Controleert viewport-plaatsing en -schaal rond de legenda. |
| `NLCSLEGENDAXREFSETUP` / `NLCSLEGENDAXREFVERIFY` | Bouwen een xref-scenario en verifiëren de opname ervan. |
| `NLCSLEGENDAXREFANALYSE` | Analyseert een tekening met echte xrefs en toont dat alleen de ingesloten xref zijn NLCS-elementen bijdraagt. |

De testcommando's zijn read-only of werken op tijdelijke testfixtures; ze wijzigen geen
gebruikersinstellingen en zetten `SECURELOAD`/`FILEDIA` netjes terug.
