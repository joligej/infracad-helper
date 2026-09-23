# Changelog

## 1.14.0 - 2026-09-23

- Meerdere legenda's kunnen naast elkaar in één tekening staan, elk met een eigen bron
  en eigen instellingen.
- Een legenda wordt gemaakt van de hele tekening of van een opgeslagen selectie; bij
  bijwerken houdt hij die bron aan.
- Bij meerdere legenda's kies je bij het bijwerken welke wordt vernieuwd (of werk alles
  bij); een legenda die geen regels meer oplevert wordt niet stil verwijderd.
- `NLCSLEGENDABEHEER` beheert de legenda's: bekijken, bijwerken, hernoemen, opzoeken en
  verwijderen. De oude samenstel-dialoog heet nu `NLCSLEGENDASAMENSTELLEN`.
- Globale instellingen zijn het startpunt voor nieuwe legenda's; een bestaande legenda
  verandert niet mee als de globale standaard later wijzigt.
- Viewport en export richten zich op de gekozen legenda. Geplaatste legenda's tellen niet
  meer mee als brondata bij het analyseren, ook niet in de batch-uittrekstaat.
- Hoeveelheden kloppen nu ook bij een blok dat meerdere keren is ingevoegd en bij
  geschaalde of geroteerde blokken.

## 1.13.0 - 2026-09-22

Genereert uit de NLCS-lagen van een Civil 3D- of AutoCAD-tekening automatisch een
legenda in NLCS-stijl: swatches met lijnen, arceringen, vlakvullingen en symbolen, de
bijbehorende tekst, en optioneel hoeveelheden, een schaalbalk en een opmerkingenblok.

- Plaatsen (met de muis), bijwerken op dezelfde plek, en een viewport op schaal.
- Per elementsoort te kiezen wat meekomt: geometrie/lijnen, vlakken, arceringen,
  vlakvullingen en symbolen, elk apart aan of uit.
- Filteren op status, plus eigen statussen waaraan je regels toewijst.
- Regels uitvinken en eigen regels toevoegen; instellingen globaal of per tekening.
- Externe referenties per xref in- of uitschakelen (geneste xrefs volgen de bovenliggende).
- Instellingen als profiel opslaan, laden en im-/exporteren om te delen.
- Export naar CSV en JSON, en een gecombineerde uittrekstaat over een hele map tekeningen.
- Werkt met AutoCAD/Civil 3D 2025, 2026 en 2027; installatie via MSI of zip.
