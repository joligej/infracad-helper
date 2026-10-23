# Legenda meten in Civil 3D 2027

Deze losse meet-DLL komt niet in de plug-inbundel. Gebruik een kopie van een
tekening; de proef plaatst en vernieuwt een legenda in het geopende document.
Voor Object Data is de volledige Civil 3D/Map-host nodig.

1. Bouw de gewenste plug-inversie voor 2027 en bouw dit project. Met
   `-p:PluginDirectory=<map>` kan de meet-DLL tegen een bewaarde versie worden gebouwd.
2. Maak een nieuwe uitvoermap. Stel `NLCS_BENCH_DIRECTORY` vóór het starten van
   Civil 3D in op die map. Zet er `cases.json` in:

   ```json
   {
     "Name": "KLIC lokaal",
     "SourceHash": "SHA256 VAN DE ONGEWIJZIGDE BRON",
     "AttachSource": "",
     "XrefName": "BRON",
     "Runs": 4
   }
   ```

3. Open de tekeningskopie. Voor een xref-proef: open een lege tekening en vul
   `AttachSource` met het absolute bronpad; de hash hoort dan bij die xref.
4. Laad eerst de bedoelde plug-in en daarna `LegendBench.dll` met NETLOAD.
   Voer `NLCSBENCH` uit. De proef gebruikt schaal 1:500, hoeveelheden, xrefs en
   de ingestelde voorbeeldbibliotheek. Controleer dat beide versies dezelfde
   instellingen en bronnen gebruiken.
5. Vergelijk `benchmark.json`, `rows.json.gz` en `legend-shape.txt`. De eerste
   opbouw bevat opstartkosten; vergelijk de overige opbouwen afzonderlijk.
   Bijwerken bevat ook het opnieuw tekenen van het beeld. Bestandsopslag,
   CAD-opstarten en het schrijven van de controlebestanden vallen buiten de tijd.

`NLCSBENCHPROFILE` meet de XML-, Map- en laagstappen afzonderlijk op een lokale
KLIC-tekening. Dit is een diagnose, geen volledige gebruikershandeling.

Tijdelijke .NET-toewijzingen zijn cumulatieve allocaties, geen piekgeheugen.
De geometriecontrole vergelijkt tekst, posities, vormen en stijlen; zij vervangt
geen aparte vergelijking met de oorspronkelijke TAUW-template. Bronrecords en
lokale paden in uitvoerbestanden kunnen vertrouwelijk zijn: publiceer alleen een
samenvatting. Sluit iedere CAD-proef normaal af en controleer bronbehoud en
profielherstel afzonderlijk.
