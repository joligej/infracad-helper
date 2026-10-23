# Prestatiemetingen

Op 23 oktober 2026 zijn versie 1.35.1 en de kandidaat voor 1.35.2 vergeleken in
Civil 3D 2027, op dezelfde computer. De duidelijkste verbetering is minder
tijdelijke .NET-toewijzing: circa 34–35% bij de grote KLIC-tekening en 8–10% bij
de ontwerp-xref. Een algemene versnelling is met deze metingen niet aangetoond.
Sommige handelingen zijn sneller, andere iets langzamer; de computer voerde ook
achtergrondtaken uit. De tijdsverschillen zijn daarom geen snelheidsbelofte.

## Uitkomsten

| Proef | 1.35.1 | 1.35.2 | Tijdelijke toewijzing |
| --- | ---: | ---: | ---: |
| KLIC lokaal — opbouwen | 4.44 s | 4.65 s | 1487 → 959 MB |
| KLIC lokaal — bijwerken | 5.68 s | 5.51 s | 1503 → 974 MB |
| KLIC via xref — opbouwen | 7.89 s | 7.57 s | 1573 → 1031 MB |
| KLIC via xref — bijwerken | 7.66 s | 8.00 s | 1588 → 1048 MB |
| Ontwerp via xref — opbouwen | 1.19 s | 1.15 s | 85 → 77 MB |
| Ontwerp via xref — bijwerken | 1.10 s | 1.27 s | 94 → 87 MB |

Dit zijn medianen van drie herhaalde opbouwen en vier updates per proef.
De eerste opbouw is apart gehouden vanwege het laden van API's en bronkopieën.
Toewijzing telt alle tijdens de handeling aangemaakte .NET-objecten op; dit is
geen piekgeheugen of blijvend geheugengebruik. Het werkelijke effect op een andere
computer, waaronder een werklaptop, is hiermee niet gemeten.

De KLIC-proef bevatte 38.593 meegetelde objecten, 230.112 bronrecords en 178 regels;
de ontwerp-proef 1.368 objecten en 242 regels. Beide versies gebruikten schaal
1:500, hoeveelheden, dezelfde bronbestanden en dezelfde voorbeeldbibliotheek.
Inhoud, bronrecords, hoeveelheden en de geometrie- en tekstvingerafdrukken waren
gelijk. De oorspronkelijke tekeningen zijn ongewijzigd gebleven.

## Waar de tijd naartoe gaat

Bij de lokale KLIC-tekening kostte de afzonderlijke nulmeting ongeveer 0,86 s
voor ingesloten XML en 2,85 s voor Object Data en Property Sets. Bij xrefs kost
ook het overnemen van echte symbolen en stijlen tijd. De Map-API en het lezen
van bronkopieën blijven nodig om actuele waarden en de bronweergave te bewaren.

In 1.35.2 worden XML-records zonder tijdelijke XML-boom gelezen. De Map- en
Property Set-toegang wordt voorbereid en hergebruikt; waarden worden opnieuw
gelezen. Laagstijlen en ontlede laagnamen worden alleen tijdens één analyse
bewaard. Lijnbreedtes hebben geen tijdelijke lijst per vertex meer nodig.
Lijnpatronen uit dezelfde xref kunnen binnen één bouwactie dezelfde bronkopie
gebruiken; daarna wordt die vrijgegeven. Er is geen langdurige cache van
tekeninginhoud toegevoegd.

## Herhalen en grenzen

De [losse meettool](../dev/benchmarks/README.md) is beschikbaar voor een eigen
vergelijking. Opstarten, bestandsopslag en het schrijven van controlebestanden
vallen buiten de gemeten tijd; bijwerken bevat wel beeldregeneratie. Privébronnen
en hun inhoud zijn niet in deze repository opgenomen.

Twee vroege proefruns sloten niet tijdig af, waaronder één met ongewijzigd
1.35.1. De meetopstelling stuurt QUIT inmiddels pas nadat de opdrachten zijn
afgerond en CAD weer vrij is. De drie uiteindelijke kandidaatproeven sloten
normaal af en herstelden alle 35 vastgelegde profielwaarden. Dit is geen bewijs
dat de oudere GDI+-melding is opgelost; daarvoor is de oorzaak nog niet bewezen.
