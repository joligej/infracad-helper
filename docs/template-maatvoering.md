# Maatvoering

De legenda staat in modelruimte, in meters. Een papiermaat wordt omgerekend met
`papier-mm × schaal / 1000`. De onderstaande maten zijn op 14 oktober 2026
opnieuw gemeten in het lokale referentiemateriaal, bij de voorbeelden op 1:200,
1:500 en 1:1000. Ze vervangen de eerdere afgeronde opmaakwaarden.

| Onderdeel | Papiermaat of eigenschap |
| --- | --- |
| Lijn, vlak en arceringsvoorbeeld | 24 × 5 mm; een lijn beslaat de volle breedte |
| Afstand tussen opeenvolgende regelmiddens | 6,3099 mm |
| Tekstkolom vanaf de linker voorbeeldrand | 29,7188 mm |
| Ruimte tussen voorbeeld en tekst | 5,7188 mm |
| Titel | 5 mm, onderstreept, laag T50, ACI 130 |
| Statuskop en omschrijving | 2,5 mm, laag T25, ACI 7 |
| Titelbasislijn tot eerste statusbasislijn | 6,9257 mm |
| Omschrijvingbasislijn onder voorbeeldmidden | 0,7097243 mm |
| Kolombreedte | 143 mm |
| Lettertype | NLCS-ISO.ttf, breedtefactor 1 |
| Meerregelige omschrijvingen | MText-regelafstandfactor 0,624 (minimaal) |
| Hulpkaders | Aparte niet-plotbare laag, ACI 40, lijndikte 0,18 mm |
| Opmerkingenkop | 5 mm, onderstreept, dezelfde tekststijl als de titel, white (ACI 7) |
| Opmerkingentekst | 2,5 mm, tekstbreedte 128,8894 mm |
| Schaalbalk | 40 × 2 mm; vier segmenten, elk twee halve stroken |
| Schaalbalklabels | 1,8 mm, boven de balk, laatste label met eenheid |
| Schaaltekst | 1,8 mm, links onder de balk |

Het referentiemateriaal bevat DBText. De plugin gebruikt MText en verrekent de
linker zijruimte van het NLCS-lettertype met 0,1 maal de teksthoogte. De
regelbasislijnen blijven gelijk voor teksten met en zonder onderlengte. Voor
afbreken en rijhoogte wordt de werkelijke MText-hoogte gebruikt. Lange teksten en
grote symbolen vergroten hun rij; de volgende rij schuift mee.

De automatische volgorde begint, net als de voorbeelden, met Bestaand en daarna
Nieuw. Een handmatige volgorde blijft behouden. De standaard voor meerregelige
omschrijvingen is op 15 oktober rechtstreeks uit de gewone MText-voorbeelden
gemeten: 0,624. De eerdere waarde 1,35 gaf te veel witruimte. Het voorbeeld met
een eigen alinea-opmaak gebruikt 0,576 en is geen algemene standaard. Opmerkingen
hebben hun eigen alinea-opmaak en worden hierdoor niet veranderd.

Een broninsert zonder schaalvariant behoudt zijn modelmaat, rotatie en spiegeling.
Een expliciete NLCS-schaalvariant wordt eenmaal omgerekend met
`gekozen schaal / bronschaal`. Dat geldt ook voor een bemonsterd arceringspatroon.
Symbolen worden niet passend gemaakt in het voorbeeldvak. Een groter symbool
verbreedt de voorbeeldkolom zonder de beschikbare tekstruimte te verkleinen.
Ongelijk geschaalde symbolen blijven zo nodig als zelfstandig hostblok behouden;
de inhoud bevat geen DBText of verwijzingen naar de brondatabase.

Hoeveelheden, automatische kolomverdeling en een optioneel buitenkader zijn
productuitbreidingen. De instelbare uitgangspunten daarvoor zijn een
hoeveelhedenkolom van 18 mm, 10 mm tussen kolommen en 5 mm kadermarge. Dit zijn
geen gemeten templateafstanden. Een grote rechthoek rond een layoutvoorbeeld
is geen aanwijzing dat iedere legenda een zichtbaar buitenkader moet krijgen.

`NLCSLEGENDATEMPLATECONTRACTTEST` vergelijkt de geproduceerde geometrie met
afzonderlijk vastgelegde meetpunten op drie schalen, met een tolerantie van
0,002 mm op papier voor de afgeronde maten. `NLCSLEGENDAPAPERSCALETEST` controleert
ook 1:100. De schaalvariant- en grote-symbolenproeven controleren xrefs, de echte
bronmaat, samenvoeging, vrije tekstruimte en ongewijzigde bronbestanden. Deze
objectmetingen vullen de visuele controle van de geplotte uitvoer aan.

`NLCSLEGENDATEMPLATEFILETEST` leest daarnaast de originele DWT via de
omgevingsvariabele `NLCS_TEMPLATE_REFERENCE`. Deze proef vergelijkt de geproduceerde
teksthoogtes, stijl, posities en de hoogte van een meerregelige omschrijving
rechtstreeks met de bron en controleert de bronhash. De bron is niet opgenomen in
de repository. Gebruik met het acceptatiescript `-Cases templatefile` en
`-ReferenceTemplatePath <pad-naar-DWT>`.

Bestaande persoonlijke instellingen worden niet automatisch overschreven.
**Instellingen → Legenda → Templateopmaak herstellen** herstelt de maatvoering, lagen,
kolomindeling, koppen en voorbeeldweergave. De extra hoeveelheden en totaalregel
worden uitgezet. Teksten, schaal, handmatige volgorde en bronkeuzes blijven behouden;
de knop **Tekeningnotities** in het opmerkingenvenster herstelt desgewenst de standaardtekst.
De standaard gebruikt een correcte NLCS-ISO-stijl, ook als de host al een afwijkende
stijl met die naam heeft. Een expliciet gekozen eigen stijl blijft leidend; ontbreekt
die stijl, dan wordt dit gemeld en blijft de vorige legenda behouden.

## Scheidingsbanden

De drie scheidingsbanden zijn samengestelde voorbeelden: twee doorlopende
kantlijnen (ACI 210, 0,35 mm) en twee binnenlijnen met de stijl van de bronlaag.
De lijnrichting volgt de template. De afstanden zijn op 16 oktober 2026 uit de
1:200-voorbeelden gemeten en worden als papiermaten met de legendaschaal omgerekend.

| NLCS-element | Tussen kantlijnen | Tussen binnenlijnen |
| --- | --- | --- |
| KANTOPSLUITING_SCHEIDINGSBAND_100X300 | 1,50 mm | 0,50 mm |
| KANTOPSLUITING_SCHEIDINGSBAND_115X345 | 1,75 mm | 0,60 mm |
| KANTOPSLUITING_SCHEIDINGSBAND_220X440 | 2,20 mm | 1,10 mm |

De getekende breedte van het 115/345-voorbeeld is 350 mm op modelschaal;
de templategeometrie is hier leidend, niet de maattekst. De naam en omschrijving
worden niet aangepast. De samengestelde weergave geldt alleen voor deze drie
NLCS-elementen met lijngeometrie. Handmatige regels en andere elementen behouden
hun bestaande weergave. Extra voorbeeldlijnen tellen niet mee in hoeveelheden.

De native proef `NLCSLEGENDABANDTEST` vergelijkt de lijnmaten en stijlen
rechtstreeks met de originele DWT op 1:200, 1:500 en 1:1000, lokaal, gecombineerd
en uitsluitend via een xref. Met `-Cases band` voert het acceptatiescript ook
heropenen en vijftig keer bijwerken uit. De referentie blijft buiten git.

## Voorbeeldcontrole

Op 20 oktober 2026 zijn alle 266 voorbeelden uit de lokale referentie naast hun
geplotte uitvoer gezet. De lokale bibliotheek is gecorrigeerd voor ontbrekende
annotaties, extra randen en de tekenvolgorde van afdekvlakken. Deze bedrijfseigen
DWG blijft buiten de openbare repository en installer.

Een samengesteld templatevoorbeeld is via **Overzicht → Voorbeeld** als één geheel
te kiezen. Bij automatische bronanalyse blijven verschillende NLCS-elementen en
weergavevarianten afzonderlijke regels. Losse maatvoering en annotaties worden
niet vanzelf legenda-items; daarvoor is een gekozen voorbeeld of eigen regel nodig.
Een expliciete schaalvariant in de bron wordt omgerekend, ook wanneer een
templateblad zelf voorbeelden met verschillende schaalsuffixen bevat.

De proeven `NLCSLEGENDAEXAMPLEAPPEARANCETEST` en
`NLCSLEGENDAEXAMPLEREVISIONTEST` controleren bronvorm, achtergrondkleur,
bibliotheekupdates, gelijknamige blokken en behoud van andere legenda's.
De tweede proef heeft ook een controle na opslaan en heropenen.
