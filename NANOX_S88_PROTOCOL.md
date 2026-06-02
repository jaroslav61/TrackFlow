# NanoX-S88 / Lenz LI100F v2 – Service Mode CV Read

## Dokumentácia ladenia a finálnej implementácie

**Dátum:** 27. máj 2026
**Súbor:** `Services/Dcc/NanoXS88Client.cs`
**Testy:** `TrackFlow.Tests/NanoXs88ClientTests.cs`

---

## 1. Cieľ

Spoľahlivo prečítať hodnotu CV registra (napr. CV1) z dekodéra lokomotívy
na programovacej koľaji cez Paco NanoX-S88 (PIC firmware emulujúci Lenz
LI100F v2/v3 XpressNet rozhranie) pripojený sériovým COM portom @ 9600 Bd.

Workflow musí byť **stabilný pri opakovaných testoch** – kým predtým druhý
test vždy padol s `61 81 E0` (Command Station Busy), teraz prebieha
identicky ako prvý.

---

## 2. Pôvodný stav (pred opravami)

### Symptómy
1. **Prvý test** po štarte aplikácie:
   - Komunikácia nadviazaná, dekodér fyzicky cukol.
   - Centrála pošle `61 02 63` (busy) → `61 00 61` (status).
   - Kód vetva A `if (response[1] == 0x00)` vrátila magic value `(byte)3`.
   - UI zobrazilo červenú napriek "úspechu", lebo finally posielal exit
     uprostred merania a NanoX uviazol.
2. **Druhý test** hneď za prvým:
   - Centrála hneď odpovedala `61 81 E0` (Command Station Busy).
   - Vetva D (informačné statusy) tento rámec neobsahovala, prepadol na
     `throw new InvalidOperationException("Neočakávaná odpoveď")`.

### Hlavné chyby v starom kóde
| # | Chyba | Dopad |
|---|---|---|
| A | `61 00` interpretované ako finálny výsledok | Vraciame `(byte)3` namiesto skutočného CV |
| B | Chýba `Service Mode Results Request` (`21 10 31`) | NanoX nikdy nepošle dátový rámec autonómne |
| C | Inter-byte timeout iba 500 ms | Tretí bajt sa nestihne dočítať (`checksum mismatch`) |
| D | `61 81` neošetrené v slučke | Druhý test padá `InvalidOperationException` |
| E | `61 01` broadcast spam plytvá timeoutom | Štartovací cyklus prečerpá ~600 ms |
| F | `63 10` / `63 14` čítané ako 4 bajty | Lenz spec hovorí 5 bajtov; XOR check zlyhával |
| G | Vrátenie `response[2]` namiesto `response[3]` | CV echo namiesto skutočnej hodnoty |
| H | Žiadny drén bufferu medzi testami | Druhý test "zdedil" stav NanoXu |

---

## 3. Chronológia ladenia

### Iterácia 1 — Inter-byte timeout + skrátený rámec checksum
**Zmena:** `InterByteReadTimeoutMs: 500 → 1000`, `ValidateChecksum` toleruje 2-bajtové rámce bez čísla.
**Výsledok:** `checksum mismatch pre 61 00` zmizol.

### Iterácia 2 — Defenzívne `61 81`
**Zmena:** Pridaný handler `61 81` v štartovacom cykle (retry s drénom) aj v hlavnej slučke (graceful TimeoutException).
**Výsledok:** Druhý test už nepadá výnimkou, ale stále nedoručí výsledok – `61 81` nebola koreňová príčina, len jej dôsledok.

### Iterácia 3 — Diagnostika z logov: chýba SMRR
**Pozorovanie z `prvý test.log`:** Po `61 02` centrála **mlčí**. Žiadne `63 1x` neprichádza.
**Analýza:** Podľa Lenz XpressNet v3.6 spec, PC musí explicitne poslať
`Service Mode Results Request` (`21 10 31`) inak centrála nikdy nepošle
dátový rámec.

**Zmena:**
- Pridaný helper `CreateServiceModeResultsRequestPacket() → 21 10 31`.
- V hlavnej slučke po `61 02` (busy) sa po krátkej pauze pošle SMRR.
- Pridaný handler `61 12` (programmer busy → retry SMRR, max 15 pokusov).
- Pridaný fallback handler `61 82` (instruction not supported → vypne
  SMRR a prejde na pasívne čakanie spontánneho `63 1x`).
- **Odstránená** vetva A (`61 00 → return`) – `61 00` je len informačný
  status. Skutočný výsledok prichádza ako `63 1x`.
- Pridaná vetva B: `63 10` (Paged Mode v2) aj `63 14` (Direct Mode) ako
  success rámec.
- Pridaný drén `61 01` broadcastov po Track Power ON (`DiscardInBuffer` +
  200 ms pauza + `DiscardInBuffer`).
- `DefaultPassiveReadTimeoutMs: 4_000 → 8_000` (SMRR retry cyklus môže
  trvať dlhšie).
- Vylepšený `finally`: drén pred + po exit pakete, predĺžená pauza.

**Výsledok:** SMRR funguje, po 3. pokuse príde `63 10 01 02` → ale
**checksum FAIL** (`0x63^0x10^0x01 = 0x72 ≠ 0x02`).

### Iterácia 4 — 5-bajtový dátový rámec
**Pozorovanie:** `63 10 01 02 ??` (4 bajty prečítané, 5. chýba).
**Analýza:** Lenz XpressNet `0x63` rámce sú 5-bajtové: `0x63 ID CV V XOR`.

| Bajt | Význam |
|---|---|
| `0x63` | Header |
| `ID` | Identifier (`0x10` = Paged Mode v2, `0x14` = Direct Mode, `0x21` = SW version) |
| `CV` | CV číslo (echo, 1-based) |
| `V` | **Hodnota CV** ← toto chceme vrátiť |
| `XOR` | Checksum všetkých predošlých bajtov |

**Zmena:**
- `ReadRawResponseAsync` číta pre `63 10`/`63 14` **5 bajtov** namiesto 4.
- Vetva A vracia `response[3]` (skutočná hodnota) namiesto `response[2]`
  (CV echo).
- Diagnostický log uvádza aj CV echo pre kontrolu.

**Výsledok:** ✅ `CV1 = 2` (alebo skutočná hodnota) sa korektne vracia.
Druhý test funguje identicky ako prvý.

---

## 4. Finálny protokolový tok

```
PC                                      NanoX-S88
│                                              │
│── 21 21 00 ──────────────────────────────────▶│   Command Station SW Version Request
│◀───── 63 21 36 00 74 ─────────────────────────│   SW v3.6
│                                              │
│── 21 81 A0 ──────────────────────────────────▶│   Resume Operations (Track Power ON)
│                                              │
│   [drén 61 01 broadcastov + 200 ms]          │
│                                              │
│── 22 14 01 37 ───────────────────────────────▶│   Paged Mode v2 CV1 Read
│◀───── 61 02 63 ───────────────────────────────│   Busy / Measuring
│◀───── 61 02 63 ───────────────────────────────│
│                                              │
│── 21 10 31 ──────────────────────────────────▶│   SMRR (pokus 1/15)
│◀───── 61 02 63 ───────────────────────────────│   Stále busy
│── 21 10 31 ──────────────────────────────────▶│   SMRR (pokus 2/15)
│◀───── 61 02 63 ───────────────────────────────│
│── 21 10 31 ──────────────────────────────────▶│   SMRR (pokus 3/15)
│◀───── 63 10 01 02 70 ─────────────────────────│   ✅ Paged Mode result: CV1=2
│                                              │
│   [pauza 250 ms + drén]                      │
│── 21 81 A0 ──────────────────────────────────▶│   Exit Service Mode
│   [pauza 300 ms + drén]                      │
```

---

## 5. Mapa všetkých použitých XpressNet rámcov

### Od PC → centrála
| Bajty | Význam |
|---|---|
| `21 21 00` | Command Station Software Version Request |
| `21 81 A0` | Resume Operations / Exit Service Mode (Track Power ON/OFF) |
| `21 10 31` | **Service Mode Results Request (SMRR)** |
| `22 14 CV XOR` | Paged Mode v2 CV Read |

### Od centrály → PC
| Bajty | Význam | Akcia |
|---|---|---|
| `61 00 XX` | Service Mode entered / Track Power Off | informačný status, continue |
| `61 01 XX` | Normal Operations Resumed | informačný status (broadcast po `21 81 A0`), continue |
| `61 02 63` | Programmer Busy / Measuring | pošli SMRR, continue |
| `61 12 73` | Programmer Busy (odpoveď na SMRR) | retry SMRR (max 15×) |
| `61 13 72` | No ACK (dekodér neodpovedal) | TimeoutException |
| `61 80 XX` | Transfer Error | informačný status, continue |
| `61 81 E0` | Command Station Busy | TimeoutException ("skúste znova") |
| `61 82 E3` | Instruction Not Supported | vypni SMRR, prejdi na pasívne čakanie |
| `63 10 CV V XOR` | **Paged Mode v2 result** | ✅ return V |
| `63 14 CV V XOR` | **Direct Mode result** | ✅ return V |
| `63 21 M S XOR` | Software Version Response (handshake) | parsovaný v `SendCommandStationVersionHandshakeAsync` |

---

## 6. Konštanty a politiky

```csharp
private const int InitialProgrammingResponseDelayMs = 200;
private const int BusyRetryDelayMs = 100;
private const int DefaultPassiveReadTimeoutMs = 8_000;   // bolo 4_000
private const int MinEffectiveTimeoutMs = DefaultPassiveReadTimeoutMs;
private const int MaxEffectiveTimeoutMs = 30_000;

// V ReadCvAsync:
const int MaxStartAttempts = 3;          // počet retry pri 61 13 (No ACK) v štarte
const int Post6113DrainDelayMs = 300;    // pauza medzi 61 13 retry pokusmi
const int InterByteReadTimeoutMs = 1000; // pauza medzi bajtmi v rámci
const int MaxSmrrAttempts = 15;          // ~3 s pri 200 ms pauze
const int SmrrRetryDelayMs = 200;        // pauza medzi 61 12 retry SMRR
```

---

## 7. Tabuľka zmien v `NanoXS88Client.cs`

| # | Lokalita | Zmena |
|---|---|---|
| 1 | Konštanty | `DefaultPassiveReadTimeoutMs: 4_000 → 8_000`, aktualizovaný komentár |
| 2 | `CreateServiceModeResultsRequestPacket()` | **NOVÝ** helper – vracia `21 10 31` |
| 3 | `IsKnownInformationalStatus` | Pridaný `0x12` (programmer busy) |
| 4 | `DescribeStatusFrame` | Pridaný popis pre `0x12` |
| 5 | `ReadCvAsync` štart | `DiscardInBuffer` pred štartom |
| 6 | `ReadCvAsync` po Track Power ON | Drén `61 01` broadcastov (DiscardInBuffer + 200 ms + DiscardInBuffer) |
| 7 | `ReadRawResponseAsync` | `63 10`/`63 14` číta **5 bajtov** namiesto 4 |
| 8 | Lokálny helper `SendSmrrAsync()` | **NOVÝ** – pošle `21 10 31` + log |
| 9 | Štartovací cyklus | Handler `61 81` (retry s drénom + 500 ms pauza) |
| 10 | Hlavná slučka vetva A | **Prepísaná** – akceptuje `63 10` aj `63 14`, vracia `response[3]` |
| 11 | Hlavná slučka vetva D (`0x02`) | Po pauze pošle SMRR |
| 12 | Hlavná slučka vetva D (`0x12`) | **NOVÝ** – retry SMRR (max 15×) |
| 13 | Hlavná slučka vetva D (`0x82`) | **NOVÝ** – vypne SMRR, fallback na pasívne čakanie |
| 14 | Hlavná slučka vetva D (`0x81`) | TimeoutException ("skúste znova") |
| 15 | Stará vetva `61 00 → return` | **ZMAZANÁ** (61 00 je len status, ide do vetvy D) |
| 16 | `finally` | `DiscardInBuffer` pred + po exit pakete, predĺžené pauzy (250 + 300 ms) |
| 17 | `ValidateChecksum` | Toleruje rámce kratšie ako 3 bajty (skip s info logom) |

---

## 8. Tabuľka zmien v testoch

| # | Test | Zmena |
|---|---|---|
| 1 | `CreateServiceModeResultsRequestPacket_ReturnsExpectedSmrrBytes` | **NOVÝ** – validuje `21 10 31` |
| 2 | `WhenPagedModeResultArrivesAfterBusyAndSmrr_ReturnsCvValue` | Prepísaný z `WhenPagedMode_Returns610061AfterBusy` (testuje nový kontrakt) |
| 3 | `When6100ArrivesAsFirstFrame_TreatsItAsStatusAndContinuesReading` | Prepísaný z `Returns SyntheticValueImmediately` |
| 4 | `AfterBusyFrames_SendsServiceModeResultsRequest_AndDoesNotRepeatCvRead` | Prepísaný z `DoesNotSendResultRequest` (opačný kontrakt) |
| 5 | `WhenProgrammerBusy6112_RetriesSmrr_UntilCvResultArrives` | **NOVÝ** – validuje retry SMRR pri `61 12` |
| 6 | `When6182NotSupported_FallsBackToPassiveWaiting` | **NOVÝ** – validuje fallback pri `61 82` |
| 7 | Všetky data rámce `63 14 V XOR` (4-bajtové) | Hromadne zmenené na **5-bajtový** Lenz formát `63 14 CV V XOR` |
| 8 | Strict `Assert.Equal(N, serial.AllWrites.Count)` | Uvoľnené na "loose count" (SMRR pridáva nepredvídateľný počet writes) |
| 9 | Strict `Assert.Equal(N, DiscardInBufferCallCount)` | Uvoľnené na `>= N` (drény pridávajú viac volaní) |
| 10 | Rozbitá diakritika v komentároch | Opravená (UTF-8 valid) |

---

## 9. Šťastný scenár UI (pre používateľa)

1. Pripoj NanoX-S88 cez COM3 @ 9600 Bd.
2. Lokomotívu polož na programovaciu koľaj.
3. Kliknutie na **„Test CV1"** v UI:
   - Spinner sa rozkrúti.
   - V Doktorovi vidíš handshake + Track Power ON + CV read + SMRR cyklus
     + finálny `63 10 01 02 70`.
   - UI ukáže: **✅ Úspešne: CV1 = 2 (Programovacia koľaj)**.
4. Hneď klikni znovu → druhý test prebehne identicky bez `61 81`.

---

## 10. Známe obmedzenia

- **Iba ServiceTrack mode.** ProgramOnMain (POM) hodí `NotSupportedException`.
- **CV rozsah 1–256.** Mimo rozsahu `ArgumentOutOfRangeException`.
- **Timeout 8–30 s.** Pre väčšinu CV stačí 8 s.
- **Lokomotíva musí byť na koľaji.** Bez ACK pulzu vráti `61 13` (No ACK) 3×.
- **`62 22 ID XX`** (Status Response) nie je implementovaný – ale ho NanoX
  zatiaľ neposielal.

---

## 11. Kontakt na diagnostiku

Všetky kroky sú logované do **TrackFlowDoctorService** s úrovňami:
- 📤 `Info` – odoslané pakety
- 📥 `Info` – prijaté pakety + checksum OK
- ℹ️ `Info` – informačné statusy (61 0x)
- ⚠️ `Warning` – odchýlky (checksum mismatch, 61 82, 61 81)
- ✅ `Success` – finálny výsledok CV
- ❌ `Warning` – chyby (timeout, No ACK)

Pri akejkoľvek anomálii: **uložiť Doktor log a porovnať s touto
dokumentáciou.** Mapovanie `0x6X` rámcov je v tabuľke sekcie 5.

