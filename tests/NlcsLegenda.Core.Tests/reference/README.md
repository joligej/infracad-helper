# Referentiecontract legenda-maatvoering

`template-contract.json` is de onafhankelijke meting van de TAUW-referentielegenda. De waarden
komen niet uit de code maar uit de tekening, zodat `TemplateContractTests` echt toetst of
`TemplateDefaults` met de referentie klopt en niet met zichzelf.

## Reproduceren

```powershell
$dll = (Resolve-Path src\NlcsLegenda.Plugin\bin\Release\net8.0-windows\NlcsLegenda.dll).Path -replace '\\','/'
$env:NLCS_CONTRACT_OUT = "$PWD\template-contract.json"
$env:NLCS_MEET_SCALE = "200"   # optioneel, default 200
$scr = @"
(setvar "SECURELOAD" 0)
(command "NETLOAD" "$dll")
NLCSLEGENDATEMPLATEMETEN
"@
# accoreconsole op een schrijfbare kopie van SIT-NW-LEGENDA.dwg met dit script.
```

`NLCSLEGENDATEMPLATEMETEN` meet in de model space:

- lijnsample-breedte uit de horizontale sample-lijnen (dominante breedte);
- swatchkader-breedte apart uit gesloten rechthoeken (sample en kader zijn niet hetzelfde);
- rijafstand uit de verticale afstand tussen de omschrijvingsteksten (T25);
- teksthoogtes uit alle tekst in de tekening;
- symbool-insertschaal + resulterende papiermaat uit de symboolblokken.

Model is in meters (INSUNITS=6); op 1:200 is 1 modelmeter 5 mm papier.

## Toleranties

- Rijafstand en teksthoogtes komen exact overeen met `TemplateDefaults`.
- De gemeten lijnsample (22,4 mm) zit binnen het swatchvak; `SwatchWidthMm` (24 mm) mag iets breder
  zijn. Het swatchkader (gesloten rechthoek) komt in deze referentie weinig voor (de legenda
  gebruikt vooral sample-lijnen).
- De symbool-insertschaal is schaal/1000 (0,2 bij 1:200); dit is de basis die de renderer gebruikt.
