# Prestatiemetingen

## Aanvulling 1.35.3: broncontrole en exports

De vervolgmeting op 23 oktober 2026 laat een gerichte versnelling zien bij
KLIC-broncontrole en exports. Dit is geen versnelling van de gehele legenda-opbouw.
Dezelfde 178 legendaregels met 38.593 objecten en 230.112 bronrecords zijn gebruikt.
Beide versies draaiden afwisselend in één .NET 10-proces, met drie opwarmrondes en
tien metingen per versie. Onderstaande tijden zijn medianen; invoer laden en
uitvoer vergelijken vallen buiten de tijd.

| Handeling | 1.35.2 | 1.35.3 | Tijdwinst |
| --- | ---: | ---: | ---: |
| Alle KLIC-bronmeldingen bepalen | 449 ms | 208 ms | 54% |
| CSV met beheerder, diameter en materiaal | 718 ms | 459 ms | 36% |
| JSON met dezelfde velden | 715 ms | 463 ms | 35% |

Bronmeldingen en complete exports zijn exact gelijk. De spreiding was 439–513 ms
tegen 200–227 ms voor broncontrole, 685–800 tegen 434–519 ms voor CSV en 671–782
tegen 426–527 ms voor JSON. De broncontrole maakt ongeveer 117 MiB aan tijdelijke
objecten aan in plaats van 914 MiB. Dit is cumulatieve toewijzing, geen piek-RAM.

De controle vergelijkt waarden nog steeds binnen dezelfde tabel, ongeacht
hoofdletters in de tabelnaam. Verschillen tussen bronnen, lege waarden en de
bestaande normalisatie van getallen blijven behouden. Identieke ruwe waarden
hoeven niet opnieuw te worden omgezet. Er wordt niets tussen analyses bewaard.
De [CoreChecks-meettool](../dev/benchmarks/README.md#broncontrole-en-exports-vergelijken)
kan de vergelijking herhalen met eigen meetgegevens.

Twee andere proeven zijn verwijderd: extra voorbereide Map-veldlezers en een
andere XML-leeslus gaven geen overtuigende versnelling van de volledige handeling.
In de nieuwe Civil 3D-controle lag zowel de bestaande versie als de Map-proef
rond 3,4 seconden voor een herhaalde lokale KLIC-opbouw. Dit laat ook zien waarom
metingen van verschillende momenten niet zomaar met elkaar vergeleken mogen worden.

Bij de KLIC-xref kostte het openen van de symbolenbron in deze ronde circa twee
seconden per opbouw, plus enkele tienden voor kopiëren en opruimen. Die werkwijze
is behouden. Autodesk vereist volledig inlezen wanneer andere toepassingen de
bron mogen blijven beschrijven; gedeeltelijk lezen zonder bescherming kan
inconsistente gegevens opleveren ([FileOpenMode](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_FileOpenMode.html)).
Er is geen langdurige cache van CAD-objecten of brongegevens toegevoegd.

De kandidaat is gecontroleerd met 662 kerntests en acht native CAD-runs, inclusief
heropenen, KLIC, xref-keuzes en het templatecontract. De volledige Civil 3D-proef
maakte en vernieuwde de echte KLIC-xref-legenda viermaal. Bronrecords en getekende
uitvoer waren gelijk aan 1.35.2; de drie aanvullende Map-analyses slaagden. De
sessie sloot normaal af, bronnen bleven gelijk en 35 profielwaarden zijn hersteld.

## Eerdere vergelijking 1.35.1–1.35.2

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
