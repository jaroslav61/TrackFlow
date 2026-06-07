# Súhrn poslednej úlohy – kontaktný indikátor + S88/z21 feedback

Dátum: 2026-05-31

## Čo bolo cieľom

Opraviť dve chyby:

1. **Vizuálna chyba kontaktového indikátora**
    - kontaktový indikátor nemal byť predvolene farebný,
    - v neaktívnom stave sa mala zobrazovať desaturovaná ikona `cont_ind_d.png`,
    - farebná ikona `cont_ind.png` sa má zobraziť iba pri aktívnom/obsadenom stave.

2. **Funkčná chyba S88/R-Bus detekcie zo z21**
    - aplikácia nereagovala na zmeny spätnoväzbových vstupov,
    - bolo potrebné doplniť správny broadcast flag,
    - bolo potrebné doplniť parser `LAN_RMBUS_DATACHANGED (0x0040)`,
    - bolo potrebné správne mapovať 1-based `ModuleAddress` a `PortNumber` na indikátory a bloky.

---

## Čo som urobil

### 1. Pripravil som runtime stav aktivity indikátora

Do modelu `BlockIndicator` som doplnil runtime vlastnosť:

- `IsActive`

Táto vlastnosť je označená ako runtime-only (`[JsonIgnore]`) a slúži na živé zobrazenie aktívneho/obsadeného stavu
kontaktového indikátora bez zásahu do serializovaných projektových dát.

**Súbor:**

- `Models/Layout/BlockIndicator.cs`

---

### 2. Opravil som dynamickú ikonu kontaktového indikátora

Vo `BlockIndicatorViewModel` som upravil logiku `IconPath`:

- pre `Contact` indikátor:
    - `cont_ind_d.png` keď `IsActive == false`
    - `cont_ind.png` keď `IsActive == true`

Zároveň som doplnil helper:

- `RefreshVisualState()`

aby bolo možné v prípade potreby explicitne vyvolať refresh vizuálnych vlastností.

**Súbor:**

- `ViewModels/Editor/BlockIndicatorViewModel.cs`

---

### 3. Zaviedol som typy pre R-Bus / S88 feedback

Pridal som nové typy do DCC vrstvy:

- `IRBusFeedbackSource`
- `RBusFeedbackState`
- `DccFeedbackStateChange`

Tým vznikla jednotná vrstva, cez ktorú vie konkrétny klient (napr. `Z21Client`) publikovať zmeny spätnoväzbových vstupov
a vyššie vrstvy ich môžu spracovať bez priameho závisenia od implementácie klienta.

**Súbor:**

- `Services/Dcc/IRBusFeedbackSource.cs`

---

### 4. Opravil som `z21` broadcast flags

V `Z21Client` som upravil registráciu `LAN_SET_BROADCASTFLAGS` tak, aby sa k existujúcim flagom doplnil aj feedback flag
pre R-Bus / S88:

- predtým: `0x00000101`
- teraz: `0x00000111`

To znamená, že klient teraz od z21 odoberá:

- X-Bus broadcasty,
- systémové telemetrické správy,
- aj R-Bus / S88 feedback správy.

**Súbor:**

- `Services/Dcc/Z21Client.cs`

---

### 5. Doplnil som parser `LAN_RMBUS_DATACHANGED (0x0040)`

Do `Z21Client` som doplnil:

- spracovanie rámca `LAN_RMBUS_DATACHANGED`,
- publikovanie jednotlivých vstupov ako `RBusFeedbackState` udalostí.

Spracovanie je nastavené takto:

- `moduleAddress = data[4] + 1` (UI používa 1-based adresy modulov),
- bity v dátových bajtoch sa mapujú na 1-based porty,
- `bit0 = vstup 1`, `bit7 = vstup 8`.

To znamená, že napr. pre modul `1` a masku `0b11000000` sú aktívne vstupy:

- `7`
- `8`

**Súbor:**

- `Services/Dcc/Z21Client.cs`

---

### 6. Preposlal som feedback zo klientov do `DccConnectionService`

Do `DccConnectionService` som doplnil agregovaný event:

- `FeedbackStateChanged`

a pri vytváraní klientov som doplnil subscribe na `IRBusFeedbackSource`.

To platí pre:

- single-central path,
- multi-central path.

Vyššie vrstvy teda dostávajú jednotný stream feedback zmien bez ohľadu na to, či ide o jednu alebo viac DCC centrál.

**Súbor:**

- `Services/Dcc/DccConnectionService.cs`

---

### 7. Doplnil som mapovanie feedbacku na bloky a indikátory

Pridal som helper:

- `DccFeedbackLayoutApplier`

Ten:

- nájde zodpovedajúce kontaktné indikátory podľa:
    - `DccCentralProfileId`
    - `ModuleAddress`
    - `PortNumber`
- nastaví im `IsActive`,
- z kontaktových indikátorov prepočíta `BlockElement.IsOccupied`.

Blok je považovaný za obsadený, ak je aktívny aspoň jeden jeho kontaktný indikátor.

**Súbor:**

- `Services/Dcc/DccFeedbackLayoutApplier.cs`

---

### 8. Napojil som feedback na hlavný runtime tok aplikácie

V `MainWindowViewModel` som:

- subscribol `Dcc.FeedbackStateChanged`,
- pri prijatí feedbacku aktualizoval layout cez `DccFeedbackLayoutApplier`,
- pre zmenené bloky zavolal repaint editora,
- spustil reconciliáciu externej obsadenosti cez:
    - `Tabs.Operation.HandleExternalOccupancyUpdateAsync(Dcc.Client)`

To zabezpečuje, že sa spätná väzba z hardvéru premietne do:

- obsadenia bloku,
- logiky návestidiel,
- repaintu UI.

**Súbor:**

- `ViewModels/MainWindowViewModel.cs`

---

## Dotknuté súbory

### Produkčný kód

- `Models/Layout/BlockIndicator.cs`
- `ViewModels/Editor/BlockIndicatorViewModel.cs`
- `Services/Dcc/IRBusFeedbackSource.cs`
- `Services/Dcc/Z21Client.cs`
- `Services/Dcc/DccConnectionService.cs`
- `Services/Dcc/DccFeedbackLayoutApplier.cs`
- `ViewModels/MainWindowViewModel.cs`

### Testy

- `TrackFlow.Tests/Z21ClientRBusFeedbackTests.cs`
- `TrackFlow.Tests/DccFeedbackLayoutApplierTests.cs`
- `TrackFlow.Tests/BlockIndicatorViewModelTests.cs`

---

## Aké testy som doplnil

### `Z21ClientRBusFeedbackTests`

Overujú, že:

- parser `LAN_RMBUS_DATACHANGED` správne mapuje:
    - modul `0` -> UI modul `1`
    - bity na 1-based porty,
    - konkrétne aj vstupy `7` a `8`

### `DccFeedbackLayoutApplierTests`

Overujú, že:

- matching feedback aktivuje správny indikátor,
- blok sa nastaví na obsadený,
- uvoľnenie posledného aktívneho kontaktu zruší obsadenie bloku,
- feedback z iného profilu centrály sa ignoruje.

### `BlockIndicatorViewModelTests`

Overujú, že:

- kontaktový indikátor v neaktívnom stave používa `cont_ind_d.png`,
- kontaktový indikátor v aktívnom stave používa `cont_ind.png`.

---

## Overenie

Spustené cielené testy:

- `Z21ClientRBusFeedbackTests`
- `DccFeedbackLayoutApplierTests`
- `BlockIndicatorViewModelTests`

Výsledok:

- **6 / 6 testov úspešných**
- build prešiel úspešne

---

## Praktický výsledok opravy

Po tejto úprave:

- kontaktový indikátor už nie je predvolene farebný,
- v neaktívnom stave sa zobrazuje `cont_ind_d.png`,
- po aktivácii zo S88/R-Bus feedbacku sa prepne na `cont_ind.png`,
- `z21` klient už odoberá aj R-Bus / S88 spätnú väzbu,
- feedback sa mapuje na konkrétny modul a vstup,
- zmena sa premietne do `BlockIndicator.IsActive`,
- blok sa nastaví do `IsOccupied`,
- prevádzková logika dostane externý occupancy update.

