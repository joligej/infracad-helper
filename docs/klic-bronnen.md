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

De 8925 symboolinserts dragen attributen met tags `TYPE`, `LABEL`, `OMSCHRIJVING`. Zijn alle drie
leeg, dan levert dat de generieke placeholdertekst `TYPE \ LABEL \ OMSCHRIJVING` op; die wordt in
de legenda onderdrukt (zie `PlaceholderText.IsGeneric`, instelbaar via *KLIC-placeholdertekst
weglaten*). Echte waarden (`afsluiter`, `344-4793`) blijven behouden.

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
