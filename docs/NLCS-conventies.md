# NLCS-laagnaamconventie (InfraCAD)

Deze plugin herkent NLCS-lagen die door InfraCAD zijn aangemaakt aan hun
laagnaamopbouw. De conventie die uit de voorbeeldtekeningen is afgeleid:

```
F - DD - HH - ELEMENT[_SUB...] - TYPE [-SCHAAL]
```

| Veld | Betekenis | Voorbeeldwaarden |
|------|-----------|------------------|
| `F` | **Fase / status** (1 teken) | `N` nieuw, `B` bestaand, `V` vervallen, `T` tijdelijk, `R` revisie, `X` algemeen |
| `DD` | **Discipline** (2 tekens) | `WE` weg, `CO` constructie, `BH` beheer, `XX` algemeen |
| `HH` | **Hoofdgroep** (2 tekens) | `VH` verharding, `RI` riolering, `GW` grondwerk, `IE` inrichting, `VW` verkeer/markering, `GR` groen, `KW` kunstwerk, `AL` algemeen |
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

Een laag geldt als NLCS-laag wanneer, na het strippen van een eventueel
xref-voorvoegsel (`xref|...`), de naam met `-` gesplitst ten minste 5 delen
heeft en geldt: `len(F)==1 && len(DD)==2 && len(HH)==2`. Dit onderscheidt
NLCS-lagen betrouwbaar van bijv. `SIT-BS-INMETING-2D` of `SIT-NW-ONTWERP`.

## Legenda-groepering

- Eén legenda-regel per **uniek** `(F, DD, HH, ELEMENT)`; het `TYPE`-suffix en de
  schaal worden samengevoegd. Zo horen `...-G` (lijn) en `...-A` (arcering) van
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

Afgeleid uit een NLCS-voorbeeldlegenda op schaal 1:200:
teksthoogte ~ 0,5 m (~ 2,5 mm papier), swatch ~ 4,0 x 0,8 m
(~ 20 x 4 mm papier). Deze waarden zijn de standaardinstellingen en zijn
volledig configureerbaar.
