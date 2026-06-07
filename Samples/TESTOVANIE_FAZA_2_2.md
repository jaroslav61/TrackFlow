# Návod na testovanie – Fáza 2.2 (Look-ahead / UpperYellowBlinking)

> **Demo súbor:** `demo-faza-2-2-lookahead.trackflow.json`  
> **Podmienky:** Nevyžaduje sa reálna DCC centrála.  
> **Primárne overenie:** Automatizované unit testy (pozri sekciu „Unit testy").

---

## Čo je look-ahead a prečo je dôležitý

V reálnej prevádzke vlakdispečer **vopred nastaví celú trasu** (A→B aj B→C) predtým, než vlak
odíde. Vodiča vlaku blížiaceho sa k návestidlu S1 (pri Bloku A) musí S1 informovať nielen
o stave pred ním, ale aj o tom, čo ho čaká pri ďalšom návestidle S2 (pri Bloku B):

| Oba stavy onward cesty                         | Aspekt S1                  | Slovenská návestná sústava   |
|------------------------------------------------|----------------------------|------------------------------|
| Žiadna aktívna cesta z Bloku B                 | Yellow (Výstraha)          | Očakávaj zastavenie pri S2   |
| Aktívna priama cesta B→C (S2 = Yellow)         | **UpperYellowBlinking** 🟡 | Voľno s očakávaním Výstrahy  |
| Aktívna odbočková cesta B→C (S2 = LowerYellow) | **UpperYellowBlinking** 🟡 | Voľno s očakávaním Výstrahy  |
| S2 ukazuje Red (obsadený blok / bezpečnosť)    | Yellow (bez zmeny)         | Red nie je look-ahead signál |

### Čo sa NEupgraduje

- `LowerYellow` (odbočka) na S1 sa **nikdy nezmení na UpperYellowBlinking** – odbočkové
  obmedzenie má prednosť pred look-ahead informáciou.

---

## Obsah layoutu

```
[S1](sig_a) [Blok A] -- seg -- [V1] == seg == [S2](sig_b) [Blok B] -- seg -- [Blok C]
                                  \
                                  seg (diag + curve)
                                    \
                                    [Blok D]
```

| Prvok            | ID      | DCC adresa | Popis                                          |
|------------------|---------|------------|------------------------------------------------|
| Návestidlo S1    | `sig_a` | 120 (ext.) | Odchod z Bloku A smerom Right, chráni Blok B   |
| Blok A – štart   | `blk_a` | –          | `SignalRightId = sig_a`                        |
| Výhybka V1       | `sw_1`  | 101        | Priamo → Blok B, Odbočka → Blok D              |
| Návestidlo S2    | `sig_b` | 122 (ext.) | Odchod z Bloku B smerom Right, chráni Blok C   |
| Blok B – stred   | `blk_b` | –          | `SignalRightId = sig_b`                        |
| Blok C – cieľ    | `blk_c` | –          | Cieľový blok hlavnej linky (žiadne návestidlo) |
| Blok D – odbočka | `blk_d` | –          | Cieľový blok odbočky (žiadne návestidlo)       |

### Route definície

| ID     | Trasa | V1 stav    | S1 aspekt (samotná) | S1 aspekt (look-ahead)                    |
|--------|-------|------------|---------------------|-------------------------------------------|
| `r_ab` | A → B | Straight   | Yellow              | **UpperYellowBlinking** (ak r_bc aktívna) |
| `r_bc` | B → C | – (žiadna) | S2 = Yellow         | –                                         |
| `r_ad` | A → D | Diverge    | LowerYellow         | LowerYellow (bez zmeny)                   |

---

## ⚠️ Dôležité: Prečo sa look-ahead nedá priamo spustiť z UI kliknutím

Interaktívna demonštrácia look-ahead je **obmedzená bezpečnostným mechanizmom** systému:

**Konfliktná situácia:**

1. Demo 040E sa pohybuje z Bloku B do Bloku C (route `r_bc` aktívna)
2. Počas pohybu: `blk_b.IsOccupied = true` (Demo 040E stále tam), `blk_b.IsLocked = true`
3. Demo 754 chce aktivovať `r_ab` (cieľ: Blok B)
4. `CollisionDetectionService.EvaluateBlockEntry(blk_b)` → `target-block-locked` → **BLOCKED**

Inými slovami: kým `r_bc` drží Blok B obsadený/zamknutý, `r_ab` nemôže vstúpiť.
Po tom, ako Demo 040E dorazí do Bloku C, je `r_bc` ihneď deaktivovaná (pred ďalším
používateľom klikom), čím look-ahead prestáva byť relevantný.

**Look-ahead bol navrhnutý pre reálnu prevádzku**, kde:

- Vlak sa fyzicky pohybuje (senzory udávajú polohu)
- Dispečer nastaví trasy PRED pohybom vlaku
- Obe trasy (A→B, B→C) môžu byť aktívne **bez konfliktov** v tom istom čase,
  ak sú vlaky na odlišných blokoch

---

## Primárne overenie: Unit testy

### Spustenie

```powershell
cd C:\Users\Jaroslav\source\repos\TrackFlow
dotnet test TrackFlow.Tests --filter "DisplayName~LookAhead"
```

### Očakávaný výstup

```
Passed!  - Failed: 0, Passed: 21, Skipped: 0, Total: 21
```

### Prehľad testov a čo overujú

| Test                                                              | Čo overuje                                                           |
|-------------------------------------------------------------------|----------------------------------------------------------------------|
| `CalculateRouteAspect_AllStraight_NoRestrictedOnward`             | Priama bez onward → Yellow                                           |
| `CalculateRouteAspect_AllStraight_WithRestrictedOnward`           | Priama + restricted onward → **UpperYellowBlinking**                 |
| `CalculateRouteAspect_Diverge_EvenWithRestrictedOnward`           | Odbočka aj s onward → LowerYellow (bez zmeny)                        |
| `CalculateRouteAspect_NoTurnouts_WithRestrictedOnward`            | Bez vyhybiek + restricted → **UpperYellowBlinking**                  |
| `IsRestrictedAspect_ReturnsExpected` (8 prípadov)                 | Yellow/LowerYellow/UpperYellowBlinking = restricted, Red/Green = nie |
| `ApplyLookAheadAspectsAsync_TwoConsecutiveRoutes_YellowChain`     | **Hlavný scenár**: r_ab + r_bc → S1 upgradnutý                       |
| `ApplyLookAheadAspectsAsync_SingleRoute_NoChange`                 | Jedna cesta → žiadna zmena                                           |
| `ApplyLookAheadAspectsAsync_TwoIndependentRoutes_NoChange`        | Dve nezávislé cesty → žiadna zmena                                   |
| `ApplyLookAheadAspectsAsync_DivergeRoute_LowerYellowStays`        | Odbočková R2 + onward → LowerYellow zostáva                          |
| `ApplyLookAheadAspectsAsync_ThreeChainRoutes_UpgradesFirstTwo`    | Trojreťaz: S1 a S2 upgradnuté, S3 ostáva Yellow                      |
| `ApplyLookAheadAspectsAsync_OnwardSignalIsRed_NoUpgrade`          | Red na onward → žiadny upgrade                                       |
| `ApplyLookAheadAspectsAsync_MissingSignalOnOnwardRoute_NoUpgrade` | Chýbajúce návestidlo → bezpečné správanie                            |

---

## Interaktívne scenáre (UI + logy)

Hoci look-ahead nemôže byť súčasne spustený pre obidva pohyby, jednotlivé
kroky FÁZY 2.2 sú viditeľné interaktívne.

### Príprava

1. Otvor projekt `demo-faza-2-2-lookahead.trackflow.json`
2. Záložka **Prevádzka** (Operation)
3. Budú dostupné 2 lokomotívy: **Demo 754** a **Demo 040E**

---

### Scénar I2-A: Priama cesta A→B (Yellow – bez look-ahead)

**Demonštruje:** Keď existuje JUST jedna aktívna cesta, žiaden look-ahead nefire.

**Postup:**

1. Pretiahni **Demo 754** na **Blok A**
2. Klikni na route marker **A → B priamo**

**Očakávané výsledky:**

| Čo sledovať           | Stav                                                          |
|-----------------------|---------------------------------------------------------------|
| S1 (sig_a) na canvase | **Yellow** (horna žltá)                                       |
| V1 výhybka            | Straight (priamo)                                             |
| S2 (sig_b)            | Nezmení sa (Red/Off – bez aktívnej r_bc)                      |
| Log (Info)            | `Route signal apply: route=r_ab signal=sig_a … aspect=Yellow` |
| Log (Info look-ahead) | `ApplyLookAheadAspectsAsync … upgraded=0` – ŽIADEN upgrade    |

> **Záver:** S1 = Yellow (nie UpperYellowBlinking), pretože r_bc nie je aktívna.
> Look-ahead nefire pri jedinej aktívnej ceste.

---

### Scénar I2-B: Priama cesta B→C (Yellow na S2)

**Demonštruje:** Keď r_bc aktivuješ, S2 = Yellow – toto BY BOLO podmienkou pre UpperYellowBlinking na S1.

**Postup:**

1. Pretiahni **Demo 040E** na **Blok B**
2. Klikni na route marker **B → C priamo**

**Očakávané výsledky:**

| Čo sledovať           | Stav                                                          |
|-----------------------|---------------------------------------------------------------|
| S2 (sig_b) na canvase | **Yellow** (priama cesta B→C, Výstraha)                       |
| S1 (sig_a)            | Nezmení sa (Red – r_ab nie je aktívna)                        |
| V1 výhybka            | Straight (r_bc nemá vyhybku)                                  |
| Log (Info)            | `Route signal apply: route=r_bc signal=sig_b … aspect=Yellow` |

> **Záver:** S2 = Yellow (restricted aspekt). Look-ahead by mal upgradnúť S1 → UpperYellowBlinking,
> ale S1 je Red (žiadna aktívna r_ab). Look-ahead nevie reagovať bez druhej strany reťaze.

---

### Scénar I2-C: Odbočková cesta A→D (LowerYellow – neupgraduje sa)

**Demonštruje:** LowerYellow sa NIKDY neupgraduje na UpperYellowBlinking.

**Postup:**

1. Pretiahni **Demo 754** na **Blok A**
2. Klikni na route marker **A → D odbočka**

**Očakávané výsledky:**

| Čo sledovať           | Stav                                                               |
|-----------------------|--------------------------------------------------------------------|
| S1 (sig_a) na canvase | **LowerYellow** (dolná žltá, odbočka 40 km/h)                      |
| V1 výhybka            | Diverge (odbočka)                                                  |
| Log (Info)            | `Route signal apply: route=r_ad signal=sig_a … aspect=LowerYellow` |

> **Záver:** Aj keby bola r_bc SÚČASNE aktívna (čo safety mechanizmus blokuje z UI),
> S1 by zostalo LowerYellow. Overenie tohto pravidla je v unit teste
> `ApplyLookAheadAspectsAsync_DivergeRoute_LowerYellowStaysLowerYellow`.

---

### Scénar I2-D: Sledovanie logov počas bežiacej aplikácie

> ℹ️ **Log súbor existuje ONLY keď beží aplikácia** (`dotnet run` alebo spustenie z IDE).
> Unit testy (`dotnet test`) **NEPÍŠU** do `logs/trackflow-*.txt` – používajú vlastný sink.

**Postup:**

1. Spusti aplikáciu: `dotnet run` (alebo z Rider/VS)
2. Otvor demo projekt `demo-faza-2-2-lookahead.trackflow.json`
3. Aktivuj cesty podľa scenárov I2-A, I2-B, I2-C
4. Po zatvorení aplikácie alebo kedykoľvek počas behu otvor:
   ```
   C:\Users\Jaroslav\source\repos\TrackFlow\logs\trackflow-20260503.txt
   ```
5. Hľadaj záznamy z aktivácií ciest (pozri sekciu „Analýza logov" nižšie)

---

## Analýza logov pre look-ahead

> Log súbor: `logs/trackflow-YYYYMMDD.txt` (napr. `trackflow-20260503.txt`)  
> Vytvára sa automaticky pri **spustení aplikácie** (`dotnet run` / IDE).  
> Dnešný log **neexistuje pred prvým spustením** aplikácie.  
> Od verzie s `FindProjectLogsDir()` logy vždy idú do **projektového koreňa** `logs/`,  
> bez ohľadu na to, či spúšťaš z IDE alebo `dotnet run`.  
> Minimálna úroveň: `Debug` – obsahuje aj `[DBG]` záznamy.

### Čo hľadať v `logs/trackflow-YYYYMMDD.txt`

```
# Úspešný look-ahead upgrade (z unit testov / pri reálnom DCC):
[INF] Look-ahead upgrade: route=r_ab signal=sig_a → UpperYellowBlinking
      (onward restricted signal from block blk_b) reason=route-activate-look-ahead syncId=-

# Look-ahead bez akcie (menej ako 2 aktívne cesty):
[INF] ApplyLookAheadAspectsAsync: activeRoutes=1 → 0 upgrades

# DCC príkaz pre UpperYellowBlinking (extended mode, aspect=7):
[DBG] Signal DCC send: mode=extended signal=sig_a aspect=UpperYellowBlinking
      address=120 aspectNo=7 reason=route-activate-look-ahead

# Odbočka – LowerYellow – žiadny upgrade:
[INF] Route signal apply: route=r_ad signal=sig_a … aspect=LowerYellow
      (žiadny look-ahead log pre r_ad = správne)
```

### Mapovanie aspektov na DCC čísla

| Aspekt                | Extended `#` | Popis                                               |
|-----------------------|--------------|-----------------------------------------------------|
| `Yellow`              | 2            | S1 pri priamej ceste bez onward reťaze              |
| `LowerYellow`         | 6            | S1 pri odbočkovej ceste                             |
| `UpperYellowBlinking` | **7**        | S1 pri priamej ceste + obm. onward → **look-ahead** |
| `Red`                 | 1            | Fallback / obsadený chránený blok                   |

---

## Akceptačné kritériá (z FAZA_2_2_PLAN.md)

| # | Kritérium                                                                     | Overenie                      |
|---|-------------------------------------------------------------------------------|-------------------------------|
| 1 | r_ab + r_bc aktívne → S1 = UpperYellowBlinking                                | Unit test (hlavný scenár)     |
| 2 | LowerYellow (odbočka) sa neupgraduje                                          | Unit test + Scénar I2-C       |
| 3 | Red na onward signáli → žiadny upgrade                                        | Unit test                     |
| 4 | Chýbajúce návestidlo onward → bezpečné správanie (žiadna zmena, žiadna chyba) | Unit test                     |
| 5 | Look-ahead posiela DCC iba pre ZMENENÉ aspekty (nie celý snapshot)            | Unit test (DCC príkaz iba 1×) |
| 6 | Reťaz 3 cies (A→B→C→D): S1 a S2 upgradnuté, S3 zostáva Yellow                 | Unit test (ThreeChainRoutes)  |

---

## Spustenie všetkých testov (Fáza 2.1 + 2.2)

```powershell
cd C:\Users\Jaroslav\source\repos\TrackFlow
dotnet test TrackFlow.Tests
```

**Očakávaný výsledok:**

```
Total tests: 315   (294 z Fázy 2.1 a skôr + 21 nových z Fázy 2.2)
     Passed: 315
      Failed: 0
```

Spustenie iba testov Fázy 2.2:

```powershell
dotnet test TrackFlow.Tests --filter "DisplayName~LookAhead"
# Vysledok: 21/21 testov OK
```

---

## Zhrnutie: Čo demonštruje tento demo projekt

```
Interaktívne v UI:
  I2-A: Demo 754 na Blok A → klik "A→B priamo"     → S1 = Yellow ✓
  I2-B: Demo 040E na Blok B → klik "B→C priamo"    → S2 = Yellow ✓
  I2-C: Demo 754 na Blok A → klik "A→D odbočka"    → S1 = LowerYellow ✓

Automatizovane (unit testy):
  dotnet test --filter "DisplayName~LookAhead"   → 21/21 testov ✓
  → r_ab + r_bc súčasne aktívne → S1 = UpperYellowBlinking ✓
  → LowerYellow → žiadny upgrade ✓
  → Red onward → žiadny upgrade ✓
  → Trojreťaz → S1 aj S2 upgradnuté ✓
```



