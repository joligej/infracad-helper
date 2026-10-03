# KLIC-bronnen en groepeereigenschappen

Empirische inventarisatie van `relevante_bronnen/03 Voorbeeldtekening BE/ref/SIT-BS-Klic-melding.dwg`
(gemeten met accoreconsole). Deze tekening bepaalt welke eigenschappen de legenda kan groeperen.

## Waar de eigenschappen leven

| Eigenschap | Bronmechanisme | Echte voorbeelden | Genormaliseerd | Ontbrekend/onbekend |
|---|---|---|---|---|
| Soort | element-component van de NLCS-laagnaam | `KL-DATA`, `KL-ET`, `KL-GAS` | `DATA`, `ET`, `GAS` | overige hoofdgroepen blijven onder Soort |
| Specificatie (spanning/druk) | achtervoegsel in het element | `ET_LS`, `ET_MS`, `GAS_LD`, `GAS_HD` | `LS`/`MS`/`HD`/`LD` | leeg als niet aanwezig |
| Uitvoering | achtervoegsel in het element | `GAS_LD_MANTELBUIS`, `DATA_HULPSTUK` | `MANTELBUIS`/`HULPSTUK` | leeg als niet aanwezig |
| Volgnummer | cijfers achter de soort | `DATA2`, `DATA3`, `ET_LS2` | `2`, `3` | leeg als niet aanwezig |
| Symbooltype | blocknaam + attribuut `TYPE` | block `SKL-WTB_HULP_AFSLUITER-SO`, attr `TYPE=afsluiter` | zit al in de laag-/blocknaam | — |
| Identificatie | attribuut `LABEL` | `LABEL=344-4793` | per-instantie, geen groepeersleutel | vaak leeg |
| Omschrijving | attribuut `OMSCHRIJVING` | meestal leeg | valt terug op catalogus/laagnaam | vaak leeg |

De symboolinserts dragen attributen met tags `TYPE`, `LABEL`, `OMSCHRIJVING`. Gemeten over de hele
tekening (26.481 attribuutwaarden): `TYPE` is 8.621× gevuld, `OMSCHRIJVING` 128× en `LABEL` 116×.

- **`OMSCHRIJVING`** bevat waar gevuld een echte omschrijving (bijv. `Distributieleiding`,
  `Aansluitleiding`). De analyzer leest deze per symboollaag (meest voorkomende niet-lege waarde,
  `DrawingAnalyzer.ReadAttribute`) en gebruikt hem als omschrijving, boven de laagbeschrijving.
  Zo wordt `B-WE-KL-WATER_HULPSTUK-S` in de legenda `Distributieleiding` i.p.v. de nette laagnaam.
- **`TYPE`** (bijv. `afsluiter`) dupliceert doorgaans het laag-element en wordt al door de
  catalogus/laagnaam gedekt; het wordt niet apart als omschrijving gebruikt.
- **`LABEL`** (bijv. `344-4793`) is een per-instantie-identificatie, geen groepeersleutel of
  standaard legendatekst.

Zijn alle drie leeg, dan levert dat de generieke placeholdertekst `TYPE \ LABEL \ OMSCHRIJVING`
op; die wordt onderdrukt (`PlaceholderText.IsGeneric`, instelbaar via *KLIC-placeholdertekst
weglaten*). De KLIC-symbolen staan vaak op bevroren lagen; met *onzichtbare lagen meenemen* komen
ze (met hun attribuut-omschrijving) in de legenda. De bron-DWG wordt nooit gewijzigd.

## Groepeermodel

De groepeerbare eigenschappen zijn generiek gemodelleerd als `GroupDimension`
(`ElementProperties.From` ontleedt de elementnaam): **Soort, Specificatie, Uitvoering, Nummer**.
Per dimensie kan de gebruiker samenvoegen; niet-samengevoegde dimensies blijven onderscheidend
(`MergeKey`). Samenvoegen is omkeerbaar: zet je het uit, dan komt de oude granulariteit exact terug.

## Geen leverancier/netbeheerder-dimensie

In deze KLIC-bron zit **geen** leverancier- of netbeheerder-eigenschap: niet in de laagnaam, niet in
de blocknaam en niet in de attributen (`TYPE`/`LABEL`/`OMSCHRIJVING`). Een aparte leverancier-dimensie
of selectieve leverancier-groepering is daarom niet toegevoegd — er is geen brondata die dat zinvol
maakt. Komt die eigenschap in toekomstige bronnen wel voor (eigen attribuut of laagcomponent), dan is
het model uitbreidbaar met een extra `GroupDimension` zonder de bestaande dimensies te raken.

`GroupDimension` is dus bewust de **aantoonbaar gevonden** verzameling dimensies, niet een algemeen
productfeaturelijstje.

## Selectieve groepering

Per dimensie is "alles samenvoegen / alles onderscheiden" voldoende voor deze dataset:

- **Soort/Specificatie/Uitvoering**: een beheerder wil deze meestal óf per waarde tonen óf volledig
  samenvoegen; een tussenvorm (bijv. alleen leverancier A+B samen) vereist een eigenschap die hier
  niet bestaat (leverancier). Selectieve groepering per subset is daarom NOT APPLICABLE voor deze
  brondata.
- **Nummer**: volgnummers (DATA2/DATA3) horen juist óf samen óf uit elkaar; een subset-selectie is
  inhoudelijk niet zinvol.

Komt er brondata met een eigenschap waar subset-groepering wél logisch is, dan past dat in het
bestaande `MergedDimensions`-model zonder brondata te wijzigen.

## Performance (echte bron)

Gemeten met `NLCSLEGENDAPERFTEST` op SIT-BS-Klic-melding.dwg (accoreconsole, 5 runs, mediaan):

| Fase | Steekproef | Mediaan |
|---|---|---|
| parse + analyse (incl. groepering) | 28.873 entities / 74 lagen | ~138 ms |
| CompositionTree bouwen | 17 entries | < 0,1 ms |
| CSV genereren | 17 entries | ~0,1 ms |
| JSON genereren | 17 entries | ~1,2 ms |

De analyse van bijna 29.000 entities blijft ruim onder een seconde; er is geen bottleneck en dus
geen optimalisatie nodig. Export en tree zijn verwaarloosbaar.

