# Návod na testovanie – Fáza 2.1 (Runtime Route + DCC Signal Logic)

> **Demo súbor:** `demo-faza-2-1-runtime.trackflow.json`  
> **Podmienky:** Nevyžaduje sa reálna DCC centrála. Všetky scenáre fungujú aj v offline/simulačnom
> móde – navestné aspekty sa menia v UI; DCC príkazy sú preskočené s `Debug` logom.

---

## Obsah layoutu

| Prvok            | ID                      | Popis                                               |
|------------------|-------------------------|-----------------------------------------------------|
| Blok A – start   | `blk_a`                 | Štartovací blok, má priradené návestidlo `S1 start` |
| Blok B – priamo  | `blk_b`                 | Cieľový blok, priama koľaj                          |
| Blok C – odbočka | `blk_c`                 | Cieľový blok, odbočka                               |
| Výhybka V1       | `sw_1`                  | DCC adresa 101, stav `Straight` / `Diverging`       |
| Návestidlo S1    | `sig_start`             | DCC adresa 120, chráni Blok B (`ProtectsBlockId`)   |
| Route marker A→B | `route_marker_straight` | Aktivuje route `r_straight`                         |
| Route marker A→C | `route_marker_diverge`  | Aktivuje route `r_diverge`                          |
| Demo lokomotíva  | `loco_demo_1`           | Adresa DCC 3, „Demo 754"                            |

### Cesty (RouteDefinition)

| ID           | Trasa | Vyhybka V1  | Aspekt návestidla S1                    |
|--------------|-------|-------------|-----------------------------------------|
| `r_straight` | A → B | Priamo (0)  | **Yellow** (Výstraha)                   |
| `r_diverge`  | A → C | Odbočka (1) | **LowerYellow** (Výstraha s obmedzením) |

---

## Predpoklady

1. Spusti aplikáciu (`dotnet run` alebo z IDE).
2. Otvor súbor `Samples/demo-faza-2-1-runtime.trackflow.json` cez **File → Open Project**.
3. Prejdi do záložky **Prevádzka** (Operation).
4. V ľavom paneli uvidíš lokomotívu **Demo 754**.

---

## Scénar T1: Priama cesta → Yellow (Výstraha)

### Postup

1. **Pretiahni** lokomotívu „Demo 754" zo zoznamu na canvas a pusti ju na blok **Blok A – start**.
    - Blok A sa zafarbí (obsadený lokomotívou).
2. **Klikni ľavým tlačidlom** na route marker **PRIAMO A→B**.

### Očakávané výsledky

| Čo sledovať                | Očakávaný stav                                                          |
|----------------------------|-------------------------------------------------------------------------|
| Návestidlo **S1** (canvas) | Zmení sa na **žltú hornu** (Yellow)                                     |
| Výhybka **V1** (canvas)    | Zostáva / prepne sa na **Straight**                                     |
| Správa v UI                | „route-activated" alebo prázdna                                         |
| Log (Info level)           | `Route signal apply: route=r_straight signal=sig_start … aspect=Yellow` |
| DCC (ak pripojené)         | Extended accessory `#2` na adresu 120                                   |

> **Prepis aspektu do DCC čísla:** `Yellow = 2` (viď `SignalController.MapAspectToExtendedNumber`)

---

## Scénar T2: Odbočková cesta → LowerYellow (Výstraha s obmedzením)

### Postup

1. Deaktivuj predchádzajúcu cestu (ak je aktívna) kliknutím na tlačidlo **Deactivate All** v UI,
   alebo zopakuj postup priradiť Demo 754 späť na Blok A.
2. **Klikni ľavým tlačidlom** na route marker **ODBOČKA A→C**.

### Očakávané výsledky

| Čo sledovať                | Očakávaný stav                             |
|----------------------------|--------------------------------------------|
| Návestidlo **S1** (canvas) | Zmení sa na **dolnú žltú** (LowerYellow)   |
| Výhybka **V1** (canvas)    | Prepne sa na **Diverging** (odbočka)       |
| Log (Info level)           | `Route signal apply: … aspect=LowerYellow` |
| DCC (ak pripojené)         | Extended accessory `#6` na adresu 120      |

---

## Scénar T3: Deaktivácia cesty → Red (Fall-back na bezpečnosť)

### Postup (varianta A – deaktivuj konkrétnu cestu)

1. Aktivuj route `r_straight` (scénar T1).
2. Klikni na route marker **PRIAMO A→B** znova → lokomotíva sa vráti na Blok A,
   route `r_straight` sa deaktivuje a aspekt S1 padne na `Red`.

### Postup (varianta B – Deactivate All)

1. Aktivuj ľubovoľnú cestu.
2. V Operation toolbar klikni na **Deactivate All Routes**.

### Očakávané výsledky

| Čo sledovať                | Očakávaný stav                                 |
|----------------------------|------------------------------------------------|
| Návestidlo **S1** (canvas) | Zmení sa na **Red** (červená)                  |
| UI správa                  | „route-deactivated" / „routes-deactivated-all" |
| Log (Info level)           | `Route signal apply: … aspect=Red`             |
| DCC (ak pripojené)         | Extended accessory `#1` na adresu 120          |

---

## Scénar T4: Obsadenie prvého bloku za návestidlom → okamžitý Red

Tento scénar simuluje vjazd vlaku do chráneného bloku.

### Postup

1. Aktivuj route `r_straight` (scénar T1) – S1 → Yellow.
2. Skontruj, že lokomotíva je na Blok A (S1 svieti Yellow).
3. **Pretiahni** lokomotívu zo Blok B na Blok A (alebo klikni pravým tlačidlom na Blok B → „Presunut vybranu lokomotivu
   sem"), aby si presunul lokomotívu do Blok B.
    - Blok B = prvý blok za návestidlom S1 na route `r_straight`.

### Očakávané výsledky

| Čo sledovať                | Očakávaný stav                                  |
|----------------------------|-------------------------------------------------|
| Návestidlo **S1** (canvas) | **Okamžite** zmení sa na **Red**                |
| Blok B (canvas)            | Zafarbí sa ako obsadený                         |
| Route `r_straight`         | Deaktivuje sa automaticky                       |
| Log (Info level)           | `OnBlockOccupied … signal=sig_start aspect=Red` |

> **Prečo sa to deje:** `OnBlockOccupiedAsync` v `OperationViewModel` detekuje obsadenie prvého
> bloku za navestidlom a okamžite nastaví aspekt na `Red` bez čakania na ďalšie udalosti.

---

## Scénar T5: Chýbajúce návestidlo → Warning + bezpečný fallback

### Postup

1. Otvor projekt v záložke **Editor** (Layout Editor).
2. Dvakrát klikni na **Blok A – start**, otvor jeho vlastnosti.
3. Vymaž referenciu na návestidlo pre smer `Right` (pole `SignalRightId` nastav na prázdne).
4. Uloži projekt (Ctrl+S) a prejdi do záložky **Prevádzka**.
5. Aktivuj route `r_straight` kliknutím na marker **PRIAMO A→B**.

### Očakávané výsledky

| Čo sledovať                | Očakávaný stav                                                                                          |
|----------------------------|---------------------------------------------------------------------------------------------------------|
| Návestidlo **S1** (canvas) | Zostáva na **Red** (nezmení sa)                                                                         |
| UI správa                  | Prázdna alebo „route-activated" (route ak. prebehla, ale signal nie)                                    |
| Log (**Warning** level)    | `Route signal resolve failed: no signal assigned on block blk_a for direction Right (route=r_straight)` |
| DCC                        | Žiaden príkaz sa neodošle                                                                               |

> **Bezpečné správanie:** Systém zaloguje Warning a zachová predchádzajúci aspekt návestidla
> bez zmeny – nikdy nevytvorí nebezpečný stav.

---

## Analýza logov

Logy sú zapisované do priečinka `logs/` v **projektovom koreňi** (formát `trackflow-YYYYMMDD.txt`).  
Súbor sa vytvára pri **spustení aplikácie** – unit testy do neho nepíšu.

### Kľúčové správy pre Fázu 2.1

```
# Úspešná aktivácia cesty + aspekt navestidla
[INF] Route signal apply: route=r_straight signal=sig_start direction=Right aspect=Yellow sent=False reason=route-activate

# Pri priamej ceste (T1)
aspect=Yellow   → Yellow (Výstraha)

# Pri odbočkovej ceste (T2)
aspect=LowerYellow → LowerYellow (Výstraha s obmedzením 40 km/h)

# Obsadenie bloku (T4) – okamžité zhadzovanie
[INF] ...OnBlockOccupied... signal=sig_start aspect=Red reason=block-occupied

# DCC preskočené (offline)
[DBG] Signal DCC skipped: disconnected central (signal=sig_start, reason=route-activate, ...)

# Chybajúce návestidlo (T5)
[WRN] Route signal resolve failed: no signal assigned on block blk_a for direction Right (route=r_straight)
```

### Mapovanie aspektov na DCC čísla

| Aspekt                | Extended aspect # | Basic mode             |
|-----------------------|-------------------|------------------------|
| `Off`                 | 0                 | –                      |
| `Red`                 | 1                 | addr+0, activate=true  |
| `Yellow`              | 2                 | addr+1, activate=true  |
| `Green`               | 3                 | addr+0, activate=false |
| `White`               | 4                 | addr+1, activate=false |
| `Blue`                | 5                 | addr+0, activate=true  |
| `LowerYellow`         | 6                 | addr+1, activate=true  |
| `UpperYellowBlinking` | 7                 | addr+1, activate=true  |

> Návestidlo `S1 start` má DCC adresu **120** a je v **extended móde** (`IsBasicMode=false`).

---

## Rozšírené scenáre (voliteľné)

### T6: Simultánne aktívne cesty (edge case)

1. Pretiahni jednu demo lokomotívu na Blok A, druhú na Blok B (ak je k dispozícii viac loko).
2. Aktivuj `r_straight` (A→B) – S1 → Yellow.
3. Aktivuj `r_diverge` (A→C) – overí sa, že sa S1 neprepíše nesprávne.

> Tento scenár je relevantný najmä pre Fázu 2.2 (look-ahead logika).

### T7: Rýchla reakácia po opätovnom pripojení DCC

1. Otvor **Settings** a nastav DCC centrál (napr. Z21 na localhost:21105 v simulátore).
2. Aktivuj route `r_straight`.
3. Odpoj centrál (simuluj výpadok) a znova pripoj.
4. Po auto-reconnect by mal TrackFlow odoslať aktuálny stav ALL signálov (force snapshot).

---

## Akceptačné kritériá (z FAZA_2_1_PLAN.md)

| # | Kritérium                                                           | Scenár |
|---|---------------------------------------------------------------------|--------|
| 1 | Aktivácia cesty spolahlivo nastavi aspekt podla stavu vyhybiek      | T1, T2 |
| 2 | DCC odoslanie prebehne na spravne navestidlo podla smeru cesty      | T1, T2 |
| 3 | Pri deaktivacii ciest sa navestidla vracaju na SafetyFallbackAspect | T3     |
| 4 | Occupancy okamzite prepina chranene navestidlo na Red               | T4     |
| 5 | Pri chybajucom navestidle je warning a system ostava fail-safe      | T5     |

Všetky kritériá sú pokryté automatickými testami v:

- `TrackFlow.Tests/SignalControllerTests.cs`
- `TrackFlow.Tests/OperationViewModelRouteActivationTests.cs`
- `TrackFlow.Tests/OperationViewModelSignalSafetyTests.cs`

Spusti testy príkazom:

```powershell
dotnet test TrackFlow.Tests
```

---

## Rýchly postup (TL;DR)

```
1. Otvor demo-faza-2-1-runtime.trackflow.json
2. Záložka Prevádzka
3. Drag Demo 754 → Blok A
4. Klik PRIAMO A→B → S1 zmení na Yellow ✓
5. Klik ODBOČKA A→C → S1 zmení na LowerYellow ✓
6. Klik PRIAMO A→B znova → S1 zmení na Red ✓
7. Aktivuj route, presuň lokomotívu na Blok B → S1 падне na Red okamžite ✓
```

