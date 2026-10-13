# NLCS-laagnaamconventie (InfraCAD)

Deze plugin herkent NLCS-lagen die door InfraCAD zijn aangemaakt aan hun
laagnaamopbouw:

```
F - DD - HH - ELEMENT[_SUB...] - TYPE [-SCHAAL]
```

| Veld | Betekenis | Voorbeeldwaarden |
|------|-----------|------------------|
| `F` | **Fase / status** (1 teken, eventueel met subnummer 01–99) | `N` nieuw, `N03` nieuw met substatus 03, `B` bestaand, `V` vervallen, `T` tijdelijk, `R` revisie, `X` algemeen |
| `DD` | **Discipline** | `WE` weg, `CO` constructie, `BH` beheer, `XX` algemeen |
| `HH` | **Hoofdgroep** | `VH` verharding, `RI` riolering, `GW` grondwerk, `IE` inrichting, `VW` verkeer/markering, `GR` groen, `KW` kunstwerk, `AL` algemeen |
| `ELEMENT` | **Elementomschrijving**, subdelen gescheiden met `_` | `OPENVERHARDING_BETONSTRAATSTEEN`, `KANTOPSLUITING_TROTTOIRBAND_180X200_250` |
| `TYPE` | **Tekentype** (suffix) | `G` geometrie/lijn, `A` arcering/hatch, `V` vlakvulling (hatch), `S` symbool, `T50`/`T25` tekst, `GV` gevuld vlak (contour), `GD` geometrie detail, `O` overig |
| `SCHAAL` | Optionele schaalvariant | `-200` (1:200), `-250`, `-500` |

## Voorbeelden

| Laagnaam | Status | Discipline | Hoofdgroep | Element | Type | Schaal |
|----------|--------|-----------|-----------|---------|------|--------|
| `N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A` | Nieuw | WE | VH | OPENVERHARDING_BETONSTRAATSTEEN | A (arcering) | – |
| `N-WE-VH-OPENVERHARDING_BETONSTRAATSTEEN-A-200` | Nieuw | WE | VH | OPENVERHARDING_BETONSTRAATSTEEN | A (arcering) | 200 |
| `V-WE-RI-HWA_RIOOLLEIDING_PVC_160-G` | Vervallen | WE | RI | HWA_RIOOLLEIDING_PVC_160 | G (lijn) | – |
| `B-WE-VH-KANTOPSLUITING_TROTTOIRBAND-G` | Bestaand | WE | VH | KANTOPSLUITING_TROTTOIRBAND | G (lijn) | – |
| `X-XX-AL-TEKENBLAD_KADER-G` | Algemeen | XX | AL | TEKENBLAD_KADER | G | – |

## Herkenningsregel

De analyse leest de naam na het laatste xref-voorvoegsel (`xref|...`). Status,
discipline, hoofdgroep, element en type moeten aanwezig zijn. Een status kan een
subnummer hebben, zoals `N03`. Discipline en hoofdgroep bevatten letters en cijfers.
Een optionele schaal moet een positief geheel getal zijn. De laagnaameditor
controleert daarnaast de formele status- en elementcodes en hun combinaties.

## Legenda-groepering

- Eén legenda-regel per **uniek** `(F, DD, HH, ELEMENT)`; het `TYPE`-suffix en de
  schaal worden samengevoegd als de zichtbare weergave gelijk is. Verschillende
  kleuren, arceringen of symbolen krijgen afzonderlijke regels. Zo horen `...-G` (lijn) en `...-A` (arcering) van
  hetzelfde element bij één swatch.
- Sortering op status: **Nieuw -> Bestaand -> Vervallen -> Tijdelijk -> Revisie**,
  daarbinnen op hoofdgroep en element.
- Lagen met status `X` en hoofdgroep `AL` (tekenblad/kader) worden standaard
  uitgesloten van de legenda (configureerbaar).

## Schaal en maatvoering

Model space staat in **meters** (1 tekeneenheid = 1 m). De legenda-maatvoering
wordt gedefinieerd in **papier-millimeters** en omgerekend naar modeleenheden:

```
model_eenheid = papier_mm * schaal / 1000
```

De standaardmaten zijn 2,5 mm voor regeltekst en koppen, 5 mm voor de titel,
24 × 5 mm voor het voorbeeldvak en 6,3 mm tussen opeenvolgende rijen.
De afstand tussen het voorbeeldvak en de tekst is 5,7 mm. Deze maten zijn instelbaar.
