# Changelog

## 1.22.0 - 2026-10-03

- Samenstellen (uitsluitingen en eigen regels) is nu ook bereikbaar vanuit het
  instellingenvenster, naast het losse commando.
- Omschrijvingen gebruiken intern één model; de tekeningopslag volgt één actueel schema.

## 1.21.1 - 2026-10-02

- Symbolen in de legenda worden nu op de NLCS-bronschaal (schaal/1000) getekend in plaats van
  een vaste vulgraad van het swatchvak; bij overloop wordt nog steeds naar het vak teruggeschaald.

## 1.21.0 - 2026-10-02

- Veiliger overnemen van oude tekeningconfiguratie: oude instellingen en omschrijvingen worden
  nu in dezelfde stap overgenomen en pas daarna opgeruimd (bij een afbreking blijft de oude
  configuratie dus volledig bestaan). Onleesbare oude configuratie wordt niet overgenomen en
  niet gewist.
- Omschrijvingen per legenda zijn nu een onafhankelijke momentopname: wijzig je later de
  globale omschrijvingen, dan verandert een bestaande legenda niet meer mee. Een nieuwe legenda
  krijgt wel de nieuwe globale tekst.
- Omschrijvingen en elementteksten gebruiken intern één model.
- Laagnaameditor toont bij een naamsbotsing de volledige eigenschappen van de bestaande laag.

## 1.20.0 - 2026-10-02

- Omschrijvingen kunnen nu per legenda verschillen: `NLCSLEGENDAOMSCHRIJVINGEN` vraagt waarop
  je het toepast (globale standaard of één legenda). Oude tekeningbrede omschrijvingen worden
  bij het openen netjes overgenomen in de betreffende legenda in plaats van gedeeld te blijven.
- Onleesbare oude configuratie wordt niet meer stil vervangen en gewist; de migratie slaat
  dan over zodat er niets verloren gaat.
- Gelijke statussen samenvoegen houdt nu ook rekening met de symboolschaal, -rotatie en
  -spiegeling.
- Laagnaameditor: vergrendelde bronlaag wordt gemeld en veilig behandeld, samenvoegen met een
  bestaande laag vraagt een expliciete bevestiging en toont de doellaag.

## 1.19.0 - 2026-10-02

- Instelcommando's werken nu per legenda of op de globale standaard: je kiest het doel en een
  bestaande legenda wordt meteen bijgewerkt. Teksten kunnen zo per legenda verschillen.
- Samenstellen heeft een groepsboom met drie-standen-vinkjes (groep in één klik aan/uit) en
  een filter.
- Instellingenvenster met tabbladen (Algemeen, Inhoud, Opmaak, Teksten, Hoeveelheden,
  Schaalbalk / Extra) in plaats van één lange lijst.
- Nieuw commando `NLCSLEGENDALAAGNAAM`: een NLCS-laagnaam component voor component bewerken
  met live preview en validatie, en de laag hernoemen in één (ongedaan te maken) stap.
- Gelijke statussen samenvoegen kijkt nu ook naar arcering (patroon/schaal/hoek) en
  transparantie.
- Template-maten staan op één centrale plek, afgestemd op de referentielegenda.

## 1.18.0 - 2026-10-01

- Gelijke statussen samenvoegen: voegt dezelfde regel uit verschillende statussen samen,
  maar alleen als lijn, vlak, arcering en symbool er precies hetzelfde uitzien. In te stellen
  onder *Groepering*.

## 1.17.0 - 2026-10-01

- KLIC-groepering: voeg kabels/leidingen in de legenda samen op soort, spanning/druk,
  uitvoering (mantelbuis/hulpstuk) of volgnummer. Zo worden bijvoorbeeld DATA, DATA2 en
  DATA3 één regel. Hoeveelheden tellen op en het is omkeerbaar. In te stellen onder
  *Groepering*, globaal of per legenda.
- Generieke placeholdertekst uit KLIC-symbolen (zoals "TYPE \ LABEL \ OMSCHRIJVING") wordt
  standaard weggelaten; echte omschrijvingen blijven staan.

## 1.16.0 - 2026-10-01

- Commando's die een venster nodig hebben (klikken, dialogen) weigeren nu netjes in de
  AutoCAD Core Console in plaats van te kunnen crashen. Headless blijven `NLCSLEGENDAINFO`,
  `NLCSLEGENDATEST`, `NLCSLEGENDAEXPORT`, `NLCSLEGENDAUPDATE` en `NLCSLEGENDABATCHTEST`
  werken.
- Bij meerdere legenda's kiezen `NLCSLEGENDAUPDATE`/`NLCSLEGENDAEXPORT` headless de doel-
  legenda via de omgevingsvariabele `NLCSLEGENDA_TARGET` (legenda-id of unieke naam); zonder
  die keuze weigeren ze veilig zonder iets te wijzigen.
- Het controlecommando heet nu `NLCSLEGENDAELEMENT` (ribbon: "Element controleren") met
  compacte uitvoer; `NLCSLEGENDAWAAROM` blijft als alias werken.
- Een regel die niet volledig getekend kan worden wordt nu gemeld in plaats van stil
  overgeslagen; een bijgewerkte legenda met zulke regels geldt niet meer als volledig
  geslaagd.
- De oude "Alleen deze tekening"-instellingenscope is vervallen. Instellingen zijn nu de
  globale standaard (voor nieuwe legenda's) of de eigen instellingen van één bestaande
  legenda (via `NLCSLEGENDABEHEER`). Oude tekeningspecifieke configuratie wordt nog gelezen
  en kan met `NLCSLEGENDACONFIG` worden gewist.
- Knoppen in de instellingenvensters groeien mee met hun tekst, zodat langere labels niet
  meer worden afgekapt.

## 1.15.0 - 2026-09-23

- Legenda-opmaak volgt de NLCS-template: tekststijl `NLCS-ISO`, standaard teksthoogtes
  (2,5 mm regels, 5 mm koppen, 7 mm titel) en de tekstlagen `-T25`/`-T50`.
- Een geometrieregel (G) blijft altijd een lijn, ook als hetzelfde element een arcering of
  vulling heeft. Vlakken, vlakvullingen, arceringen en symbolen renderen elk apart.
- `Kader per swatch` en `Symboolblokken invoegen` werken nu zoals verwacht: staat een optie
  uit, dan wordt het vakje niet omkaderd respectievelijk geen symbool of vervangende cirkel
  getekend.
- Nieuwe legenda's worden standaard als één blok geplaatst (niet meer geëxplodeerd).
- Instellingen per legenda: in `NLCSLEGENDABEHEER` pas je de opmaak van één gekozen legenda
  aan; de legenda wordt daarna in dezelfde bewerking opnieuw opgebouwd.
- Instellingen overnemen van een andere legenda, een legenda dupliceren, de bronselectie
  aanpassen (vervangen/toevoegen/verwijderen) en de opmaak van een legenda als globale
  standaard instellen.
- `NLCSLEGENDAWAAROM`: klik een object aan en zie of en waarom het wel of niet in een
  gekozen legenda komt.
- Meerregelige opmerkingen met een echte editor; regeleindes en opsommingen blijven behouden.
- `Opmaak → template` zet alleen de opmaak terug, niet de bron, filters of eigen regels.

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
