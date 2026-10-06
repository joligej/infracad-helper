# KLIC-groepering

Voor KLIC-tekeningen (kabels en leidingen) kun je regels samenvoegen op een eigenschap. Dit
document legt uit welke eigenschappen de legenda kan groeperen en waar ze vandaan komen.

## Waar de eigenschappen vandaan komen

| Eigenschap | Bron | Voorbeeld | In de legenda |
|---|---|---|---|
| Soort | element-deel van de NLCS-laagnaam | `KL-DATA`, `KL-GAS` | `DATA`, `GAS` |
| Specificatie (spanning/druk) | achtervoegsel in het element | `ET_LS`, `GAS_HD` | `LS`, `HD` |
| Uitvoering | achtervoegsel in het element | `GAS_LD_MANTELBUIS` | `MANTELBUIS` |
| Volgnummer | cijfers achter de soort | `DATA2`, `DATA3` | `2`, `3` |

De symbolen dragen attributen `TYPE`, `LABEL` en `OMSCHRIJVING`:

- **`OMSCHRIJVING`** bevat waar gevuld de echte omschrijving (bijv. *Distributieleiding*). De
  analyse leest de meest voorkomende niet-lege waarde per symboollaag en gebruikt die als
  omschrijving, boven de laagbeschrijving.
- **`TYPE`** herhaalt meestal het laag-element en wordt al door de catalogus/laagnaam gedekt.
- **`LABEL`** is een identificatie per object, geen groepeersleutel.

Zijn ze alle drie leeg, dan ontstaat de placeholdertekst `TYPE \ LABEL \ OMSCHRIJVING`; die wordt
weggelaten (instelbaar via *KLIC-placeholdertekst weglaten*). KLIC-symbolen staan vaak op bevroren
lagen; met *onzichtbare lagen meenemen* komen ze toch in de legenda. De tekening wordt nooit
gewijzigd.

## Groeperen

De eigenschappen zijn gemodelleerd als `GroupDimension` (Soort, Specificatie, Uitvoering, Nummer).
Per dimensie kun je samenvoegen; niet-samengevoegde dimensies blijven onderscheidend. Het is
omkeerbaar: zet je het uit, dan komt de oude granulariteit exact terug.

Een leverancier- of netbeheerder-dimensie zit er bewust niet in: die eigenschap staat niet in de
NLCS-laagnaam, de blocknaam of de attributen. Komt zo'n eigenschap in de toekomst wel voor, dan is
het model uitbreidbaar met een extra dimensie zonder de bestaande te raken.
