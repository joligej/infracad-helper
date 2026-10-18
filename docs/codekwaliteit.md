# Codekwaliteit

Beoordeling van de 167 open CodeQL-meldingen op 18 oktober 2026, na release 1.32.2.
Alle meldingen hadden niveau `note`; er waren geen meldingen met niveau `warning`
of `error`. Een laag niveau is geen reden om een melding over te slaan.

| Regel | Aantal bij aanvang | Beoordeling en wijziging |
| --- | ---: | --- |
| Brede foutafhandeling | 120 | Stille terugval bij configuratiebestanden verwijderd. CAD-extents en Windows-tekenfuncties vangen gerichte uitzonderingen op. Testcommando's, vensters, rollback en optionele Map-koppelingen behouden hun foutgrens waar die nodig is. |
| Lege foutafhandeling | 9 | Alle gemelde plekken aangepast: leesfouten doorgeven, opruimfouten melden, mislukte verwijdering tijdens tekenen laten terugdraaien en verwachte testuitzonderingen expliciet controleren. |
| Suggestie voor `Where` | 23 | Uitsluitingen met `UnionWith` opgebouwd en een dubbele dictionary-lookup door `TryAdd` vervangen. De overige lussen bevatten onder meer `out`-waarden, CAD-transacties, volgordeafhankelijke wijzigingen of vroege afslagen; een filterlambda maakt die niet duidelijker. |
| Suggestie voor `Select` | 6 | Behouden. Het gaat onder meer om afzonderlijk vrij te geven vensters en CAD-objecten, en om een lokale tekstvariabele die tijdens het afbreken verandert. De levensduur blijft zichtbaar in de lus. |
| Suggestie voor `using` | 4 | Behouden. De bibliotheek wordt pas geopend als een voorbeeld nodig is. Andere plekken ruimen verzamelingen of alleen nog niet aan een transactie overgedragen objecten op. Een algemeen `using` zou de levensduur veranderen. |
| `Path.Combine` | 4 | `Path.Join` gebruikt voor het samenvoegen van een map met een bestandsnaam. Profielnamen worden daarnaast ontdaan van paden, ongeldige tekens en gereserveerde Windows-namen. |
| Geneste voorwaarde | 1 | Samengevoegd bij het toepassen van de bronstijl. |

## Waarom sommige brede catches blijven

- Een CAD-test moet iedere mislukking in zijn log herkennen. Een onverwachte fout
  mag niet ontsnappen zonder `FAIL` of een foutresultaat voor de testrunner.
- Een venster meldt een fout en blijft open. Een mislukte bronanalyse levert geen
  oude export op; een mislukte wijziging bewaart de voorgaande instellingen.
- Herstel probeert alle systeemvariabelen of bestanden terug te zetten en meldt
  de verzamelde fouten daarna. De eerste herstelmislukking mag de rest niet blokkeren.
- Object Data en Property Sets gebruiken optionele, per CAD-versie verschillende
  onderdelen. Een adapterfout wordt een zichtbare bronmelding, zonder te doen alsof
  alle KLIC-gegevens gelezen zijn.
- Een tekenfout krijgt context over de betreffende legendaregel en wordt daarna
  doorgegeven. De transactie bewaart de vorige legenda.

Deze redenen gelden voor de beoordeelde plekken, niet voor iedere nieuwe `catch`.
Een nieuwe stille terugval of lege catch moet opnieuw worden beoordeeld.
De scanconfiguratie is niet versmald en meldingen zijn niet in bulk afgesloten.

## Controle bij wijzigingen

Gebruik de controles in [CONTRIBUTING](../CONTRIBUTING.md). Bij wijzigingen aan
foutafhandeling horen ook foutgevallen: onleesbare configuratie, mislukte opbouw,
terugdraaien, annuleren en herstel van CAD-instellingen. Een dalend aantal meldingen
is pas nuttig als de plugin en de tests hun gedrag behouden.
