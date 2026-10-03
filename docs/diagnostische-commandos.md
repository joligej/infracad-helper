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
| `NLCSLEGENDAABCSETUP` / `NLCSLEGENDAABCVERIFY` | Bouwen drie legenda's en verifiëren na QSAVE/heropenen instellingen, geometrie en update-isolatie. |
| `NLCSLEGENDAVIEWPORTTEST` | Controleert viewport-plaatsing en -schaal rond de legenda. |
| `NLCSLEGENDAXREFSETUP` / `NLCSLEGENDAXREFVERIFY` | Bouwen een xref-scenario en verifiëren de opname ervan. |

De testcommando's zijn read-only of werken op tijdelijke testfixtures; ze wijzigen geen
gebruikersinstellingen en zetten `SECURELOAD`/`FILEDIA` netjes terug.
