# TrackFlow - Súkromný changelog

> Tento súbor je určený len pre súkromné poznámky o zmenách v projekte.

## Šablóna zápisu (skopírujte a vyplňte)

<!--
## RRRR-MM-DD HH:MM
===================
  **Oblasť:** 
  **Zmena:** 
  **Dôvod:** 
  **Riešenie:** 
  **Výsledok:**
-->

<!-- Poznámka: Každá zmena má byť samostatný záznam, aj keď je v ten istý deň.
     Je OK mať viac blokov s rovnakým dátumom (napr. viac × 2026-05-09).
      Do hlavičky odteraz vždy doplňte aj čas (napr. "## 2026-05-09 14:35"). -->

<!-- Poznámka 2: Najnovšie zápisy zapisujte hore (hneď pod "## Záznamy"). -->

---

## Záznamy

> Konvencia: **🟩** = položka z auditu / follow-upu je už opravená a zapracovaná v kóde.

## 2026-06-03 22:45
===================
**Oblasť:** TrackFlow.Views.Library.LocomotivesWindow (XAML)
**Zmena:** 🟩 Kompletná reorganizácia a očistenie záložky Dekodér (CV). Úplné odstránenie duplicitného bloku pre
kompenzáciu bŕzd a roztiahnutie panela pre CV29 (Smer a BEMF regulácia) cez celú šírku spodného riadku pomocou
Grid.ColumnSpan="3".
**Dôvod:** Kompenzácia bŕzd bola pôvodne umiestnená v záložke DCC konfigurácie, čo nedávalo zmysel, pretože ide o čisto
softvérovú korekciu aplikácie TrackFlow nezávislú od toho, či dekodér podporuje DCC programovanie alebo beží v analógu.
Duplicita prvkov naviac hrozila prepisovaním hodnôt.
**Riešenie:** Starý panel kompenzácie bŕzd bol zo záložky dekodéra kompletne vymazaný. Uvoľnené miesto v mriežke bolo
kompenzované úpravou zostávajúceho panela Smer a BEMF regulácia, ktorý teraz využíva celú šírku spodného riadku, čím sa
zachovala vizuálna symetria layoutu.
**Výsledok:** Čisté hardvérové prostredie v záložke dekodéra. Všetky CV registre (CV1-6, CV3, CV4, CV29) sú teraz
logicky pokope a správne reagujú na spoločné zamknutie cez vrchný checkbox podpory programovania.

## 2026-06-03 22:40
===================
**Oblasť:** TrackFlow.Views.Library.LocomotivesWindow (XAML), Styles (Slider.dccCompactSlider)
**Zmena:** 🟩 Prerobenie spodnej časti záložky Rýchlosť – presun panelov, zmena jednotiek z cm na mm, integrácia priamych
vstupných polí (TextBox) a úprava mierky tickov na slideri.
**Dôvod:** Kompenzácia bŕzd musela byť dostupná vždy, preto bola presunutá k nameraným profilom rýchlosti. Pôvodné
zobrazenie dvoch stĺpcov vedľa seba v odhade brzdnej dráhy orezávalo texty a pretekalo vertikálne von z Borderu, pričom
orientačné rysky (ticks) pod sliderom viseli vo vzduchu. Prechod z centimetrov na milimetre vyžadoval zmenu krokovania
po 1 mm.
**Riešenie:**
• Nadpis "Kompenzácia bŕzd" bol odstránený, čím sa uvoľnilo cca 20px vertikálneho miesta.
• Box Odhad brzdnej dráhy bol upravený do jedného čistého stĺpca pod seba s nastavením Width="240" a hodnotami v
kurzíve (FontStyle="Italic"), čím sa uvoľnil priestor pre vizuálny separátor.
• Box Kompenzácia bŕzd bol presunutý vedľa neho do stĺpca s pevnou šírkou, čím obidva panely dokonale lícujú s
diagnostikou nad nimi.
• Do oboch riadkov korekcií (vpred/vzad) boli pridané kompaktné TextBoxy so štýlom speedCompactTextBox viazané cez
obojsmerný binding (TwoWay) priamo na hodnoty sliderov.
• V šablóne Slider.dccCompactSlider bola výška mriežky upravená na 16,10 a pomocou záporného marginu Margin="6,-4,6,0"
boli ticky vytiahnuté priamo k osi slidera. Orientačné dieliky boli zväčšené (bežné na 3px, štvrťové na 5px a stredová
nula na 7px). Slidery boli nastavené na rozsah -500 až 500 s krokovaním SmallChange="1" a IsSnapToTickEnabled="True".
**Výsledok:** Stopercentne zarovnaný, kompaktný a moderný vzhľad celej záložky. Užívateľ má možnosť buď pohodlne ťahať
slider presne po 1 mm, alebo hodnotu rýchlo a bezpečne vpísať z klávesnice bez otravného triafania desatinných čiarok.

## 2026-06-01 13:02
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – `OnFlipOrientationMenuClick(...)` používa jednorazovo
vyhodnotený lokálny alias `loco` namiesto opakovaného čítania `_currentLoco`.
**Dôvod:** Handler pre otočenie orientácie lokomotívy najprv podmienečne inicializoval `_currentLoco` z `DataContext` a
potom s tým istým fieldom znovu pracoval v ďalších riadkoch. Išlo o malú lokálnu redundanciu, ktorú bolo možné
zjednodušiť bez zmeny správania.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) handler bol upravený na `var loco = _currentLoco ??= DataContext as Locomotive;` a
následne používa lokálnu premennú `loco`.
• Vlastné správanie ostalo nezmenené: pri `null` lokomotíve sa handler stále hneď ukončí, inak sa invertuje `IsFlipped`
a obnoví sa UI cez `UpdateDisplayNameInUi()`, `UpdateIconFlipTransform()` a
`NotifyPropertyChanged(nameof(DisplayName))`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test pre nový aliasový tvar; po prvom behu bolo potrebné
test spresniť, aby negatívna asercia nechytala inú vetvu so zhodným snippetom `if (_currentLoco == null)`.
**Výsledok:**
• Po spresnení regresného testu cielené testy `LocomotiveSpeedEditorMarkupTests` → **105 / 105 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **116 / 116
passed, 0 regresií**.

## 2026-06-01 12:48
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránené druhé zbytočné nulovanie `_pendingWagon` v závere
wagon-drag vetvy `OnPointerMoved(...)`.
**Dôvod:** Po `var wagon = _pendingWagon;` sa `_pendingWagon` v `OnPointerMoved(...)` vynuluje okamžite a počas zvyšku
wagon-drag flow sa už znova nenastavuje. Záverečné `_pendingWagon = null;` v sekcii „obnoviť stav“ tak bolo len
duplicitné nulovanie tej istej field hodnoty bez prínosu pre správanie.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z koncovej cleanup sekcie wagon-drag vetvy v `OnPointerMoved(...)` bol odstránený riadok
`_pendingWagon = null;`.
• Ostatný reset ostal nezmenený: vetva ďalej po dokončení dragu resetuje `_isPointerDown`, `_dragStarted` a nastavuje
`e.Handled = true`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že po úvodnom `_pendingWagon = null;` už na konci
wagon-drag vetvy nezostáva druhé zbytočné nulovanie.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **104 / 104 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **115 / 115
passed, 0 regresií**.


## 2026-06-01 12:44
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – `AttachedOverflowText` bol prevedený na expression-bodied
property.
**Dôvod:** Getter `AttachedOverflowText` už po predchádzajúcich clean-upoch obsahoval len jediný null-safe overflow
výraz a návratovú vetvu, takže blokový `get { ... }` tvar bol zbytočne rozvláčny. Išlo o čisto syntaktický cleanup bez
zmeny správania.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) property bola zjednodušená na
`public string AttachedOverflowText => ((_currentLoco?.AttachedWagons.Count ?? 0) - AttachedPreviewLimit) > 0 ? $"+{(_currentLoco?.AttachedWagons.Count ?? 0) - AttachedPreviewLimit}" : string.Empty;`.
• `LocomotiveSpeedEditorMarkupTests` boli upravené tak, aby očakávali expression-bodied tvar namiesto blokového getteru
s lokálnou premennou `extra`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **102 / 102 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **113 / 113
passed, 0 regresií**.


## 2026-06-01 12:40
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – `ShowOverflowIndicator` bol prevedený na expression-bodied
property.
**Dôvod:** Getter `ShowOverflowIndicator` už po predchádzajúcich clean-upoch obsahoval len jediný null-safe návratový
výraz, takže blokový `get { return ...; }` tvar bol zbytočne rozvláčny. Išlo o čisto syntaktický cleanup bez zmeny
správania.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) property bola zjednodušená na
`public bool ShowOverflowIndicator => (_currentLoco?.AttachedWagons.Count ?? 0) > AttachedPreviewLimit && AttachedPreviewLimit < int.MaxValue;`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o nový regresný test pre expression-bodied tvar.
• Prvý cielený beh odhalil pád staršieho string-based testu, ktorý ešte očakával pôvodný `return ...;` zápis v blokovom
getteri; test bol zosúladený s novým ekvivalentným tvarom a následný rerun prebehol úspešne.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **102 / 102 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **113 / 113
passed, 0 regresií**.


## 2026-06-01 12:36
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – property `AttachedWagons` používa null-coalescing výraz
namiesto ternárneho cast tvaru.
**Dôvod:** `AttachedWagons` ešte používala starší tvar
`_currentLoco != null ? (System.Collections.IEnumerable)_currentLoco.AttachedWagons : Array.Empty<Wagon>()`, hoci
rovnakú logiku bolo možné vyjadriť stručnejšie. Išlo o čisto lokálny cleanup bez zásahu do väzieb alebo behavioru UI.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) property bola zjednodušená na
`public System.Collections.IEnumerable AttachedWagons => (System.Collections.IEnumerable?)_currentLoco?.AttachedWagons ?? Array.Empty<Wagon>();`.
• Prvý pokus o zjednodušenie odhalil, že `??` medzi `ObservableCollection<Wagon>?` a `Wagon[]` potrebuje spoločný
cieľový typ; finálne riešenie preto explicitne kotví ľavú stranu na `System.Collections.IEnumerable`, ale správanie
ostáva rovnaké.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že nový null-coalescing tvar je prítomný a starý
ternárny cast zápis už v `VehicleStripItem` neexistuje.
**Výsledok:**
• Po malej korekcii cieľového typu výraz kompiluje a cielené testy `LocomotiveSpeedEditorMarkupTests` → **101 / 101
passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **112 / 112
passed, 0 regresií**.


## 2026-06-01 12:32
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – property `DisplayName` používa null-coalescing výraz
namiesto ternárneho tvaru.
**Dôvod:** `DisplayName` ešte používal starší tvar
`_currentLoco != null ? _currentLoco.DisplayName : (Title ?? string.Empty)`, hoci jeho fallback logika sa dala bez zmeny
správania vyjadriť jednoduchšie cez null-coalescing operátory. Išlo o čisto lokálny cleanup bez vplyvu na UI flow.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) property bola zjednodušená na
`public string DisplayName => _currentLoco?.DisplayName ?? Title ?? string.Empty;`.
• Fallback poradie ostalo rovnaké: najprv `DisplayName` aktuálnej lokomotívy, potom `Title`, napokon prázdny reťazec.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že nový null-coalescing tvar je prítomný a starý
ternárny zápis už v `VehicleStripItem` neexistuje.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **100 / 100 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **111 / 111
passed, 0 regresií**.


## 2026-06-01 12:27
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – helper `UpdateDisplayNameInUi()` používa null-conditional
aktualizáciu `TextBlock` namiesto explicitného `if` bloku.
**Dôvod:** `UpdateDisplayNameInUi()` mal len malý guard
`if (this.FindControl<TextBlock>("DisplayNameTextBlock") is TextBlock tb) tb.Text = DisplayName;`, ktorý bolo možné bez
zmeny správania zjednodušiť do jedného riadku. Keďže helper je privátny a slúži len na internú synchronizáciu
zobrazeného mena, išlo o bezpečný lokálny cleanup.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) helper bol zjednodušený na
`this.FindControl<TextBlock>("DisplayNameTextBlock")?.SetCurrentValue(TextBlock.TextProperty, DisplayName);`.
• Zmizla tým lokálna premenná `tb` aj explicitný `if` blok, pričom aktualizácia `DisplayNameTextBlock` ostala
behaviorálne rovnaká.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že nový null-conditional tvar je prítomný a starý
`if (... is TextBlock tb)` / `tb.Text = DisplayName;` tvar už v `VehicleStripItem` neexistuje.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **99 / 99 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **110 / 110
passed, 0 regresií**.


## 2026-06-01 12:22
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – getter `AttachedOverflowText` zjednotený na null-safe
výpočet a `ShowPropertiesAsync()` používa jeden spoločný `mainVm` guard.
**Dôvod:** `AttachedOverflowText` ešte používal viacriadkový guard flow podobný už predtým zjednodušenému
`ShowOverflowIndicator`. Zároveň `ShowPropertiesAsync()` obsahoval duplicitné `if (mainVm == null) return;` guardy v
oboch vetvách pre `Locomotive` a `Wagon`, čo bolo možné bezpečne zjednotiť do jedného spoločného guardu bez zmeny
správania.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) getter `AttachedOverflowText` bol zjednodušený na
`var extra = (_currentLoco?.AttachedWagons.Count ?? 0) - AttachedPreviewLimit;` a
`return extra > 0 ? $"+{extra}" : string.Empty;`.
• V `ShowPropertiesAsync()` bol pridaný spoločný guard
`if ((param is Locomotive || param is Wagon) && mainVm == null) return;` a duplicitné vetvové guardy boli odstránené.
• Po hoistnutí guardu bol zavedený lokálny alias `var resolvedMainVm = mainVm!;`, aby zostal zachovaný jeden guard a
zároveň bol spokojný nullable flow pre tvorbu oboch dialógových viewmodelov.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o nové regresné testy pre `AttachedOverflowText` aj spoločný
`ShowPropertiesAsync` guard; zároveň bol upravený starší test
`VehicleStripItem_OtvorenieLokomotivTiezNerobiLayoutSyncPredDialogom`, aby očakával nový ekvivalentný tvar s
`resolvedMainVm`.
**Výsledok:**
• Prvý cielený beh odhalil pád staršieho string-based testu po refaktore `ShowPropertiesAsync`; test bol zosúladený s
novým, správaním ekvivalentným tvarom a následný rerun prebehol úspešne.
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **98 / 98 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **109 / 109
passed, 0 regresií**.


## 2026-06-01 12:16
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – getter `AttachedOverflowText` používa jediný null-safe
výpočet namiesto viacriadkového guard flow.
**Dôvod:** Vlastnosť `AttachedOverflowText` najprv testovala `_currentLoco != null`, následne lokálne počítala `extra` a
vracala `+X` len ak bol počet skrytých vagónov kladný. Išlo o rovnaký typ redundantného viacriadkového guardu, aký už
bol predtým zjednotený pri `ShowOverflowIndicator`.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) getter bol zjednodušený na
`var extra = (_currentLoco?.AttachedWagons.Count ?? 0) - AttachedPreviewLimit;` a
`return extra > 0 ? $"+{extra}" : string.Empty;`.
• Správanie ostalo rovnaké: pri `null` lokomotíve aj pri nekladnom prebytku sa vracia prázdny reťazec, pri kladnom
prebytku sa naďalej zobrazuje `+X`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že starý guard `if (_currentLoco != null)` už v
`AttachedOverflowText` neexistuje a getter používa nový null-safe výpočet.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **96 / 96 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **107 / 107
passed, 0 regresií**.


## 2026-06-01 12:15
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – `DetachLastWagon()` už nepoužíva redundantný
`removed != null` guard pred vrátením vagóna do depa.
**Dôvod:** Lokálna premenná `removed` sa v `DetachLastWagon()` číta priamo z `_currentLoco.AttachedWagons[idx]` po
predchádzajúcom overení platného indexu a neprázdnej kolekcie. Podmienka `removed != null` v následnom
`if (svm != null && removed != null)` preto zostala iba ako redundantný guard bez zmeny reálneho správania.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) podmienka bola zjednodušená z `if (svm != null && removed != null)` na
`if (svm != null)`.
• Návrat odpojeného vagóna do depa ostal nezmenený: vetva ďalej vykonáva `svm.DepotWagons.Add(removed);`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že helper už neobsahuje redundantný
`removed != null` guard a že návrat vagóna do depa cez `svm.DepotWagons.Add(removed);` zostáva zachovaný.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **96 / 96 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **107 / 107
passed, 0 regresií**.


## 2026-06-01 12:06
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – getter `ShowOverflowIndicator` používa jediný null-safe
výraz namiesto dvojkrokového guard-return tvaru.
**Dôvod:** Vlastnosť `ShowOverflowIndicator` najprv explicitne testovala `if (_currentLoco == null) return false;` a
potom v ďalšom riadku vracala porovnanie nad `_currentLoco.AttachedWagons.Count`. Išlo o jednoduchý prípad, ktorý bolo
možné bez zmeny správania zjednotiť do jedného null-safe výrazu.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) getter `ShowOverflowIndicator` bol zjednodušený na
`return (_currentLoco?.AttachedWagons.Count ?? 0) > AttachedPreviewLimit && AttachedPreviewLimit < int.MaxValue;`.
• Správanie ostalo rovnaké: pri `null` lokomotíve sa vlastnosť stále vyhodnotí na `false`, inak naďalej sleduje počet
pripojených vagónov a aktivovaný preview limit.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že starý dvojkrokový guard-return tvar už v
`VehicleStripItem` neexistuje a getter používa nový null-safe výraz.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **95 / 95 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **106 / 106
passed, 0 regresií**.


## 2026-06-01 11:59
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránené duplicitné vynulovanie `_pendingWagon` v
early-return vetve `OnPointerMoved(...)`.
**Dôvod:** Vo vetve pre drag vagóna sa `_pendingWagon` vynuloval hneď po prečítaní do lokálnej premennej `wagon`, no v
následnom `if (wagon == null)` bloku sa znova nastavoval na `null`. Išlo teda len o druhé, redundantné vynulovanie tej
istej field hodnoty bez vplyvu na správanie.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z early-return vetvy `if (wagon == null)` v `OnPointerMoved(...)` bol odstránený druhý
riadok `_pendingWagon = null;`.
• Ostatný guard flow ostal nezmenený: vetva stále resetuje `_isPointerDown`, `_dragStarted`, nastaví `e.Handled = true`
a okamžite končí.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že po `var wagon = _pendingWagon;` a prvom
`_pendingWagon = null;` už v null vetve neostalo druhé nulovanie.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **93 / 93 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **104 / 104
passed, 0 regresií**.


## 2026-06-01 11:51
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránený redundantný `null` guard z privátneho helpera
`DetachSpecificWagon(Wagon wagon)`.
**Dôvod:** Metóda `DetachSpecificWagon` prijíma parameter typu `Wagon`, teda nenulovateľný typ, a všetky jej call-site v
`VehicleStripItem` posielajú hodnotu až po predchádzajúcom `null` overení alebo ju berú priamo z kolekcie pripojených
vagónov. Podmienka `if (wagon == null) return;` preto zostala iba ako redundantná obrana bez reálneho prínosu.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z helpera `DetachSpecificWagon(Wagon wagon)` bol odstránený riadok
`if (wagon == null) return;`.
• Ostatný flow odpojenia ostal nezmenený: naďalej sa hľadá index vagóna, prípadne upravuje `LocoPosition`, vagón sa
vracia do depa a UI sa obnovuje cez `NotifyAllProperties()`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že helper už neobsahuje redundantný `null` guard a
že zostáva používaný cez existujúce volania `DetachSpecificWagon(wagon);`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **93 / 93 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **104 / 104
passed, 0 regresií**.


## 2026-06-01 11:45
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – fallback parameter pre `DropCommand` v `OnDrop(...)` používa
implicitné vytvorenie poľa.
**Dôvod:** Vo fallback vetve `OnDrop(...)` sa parameter pre `DropCommand` vytváral ako
`new object?[] { target, wagon }`, hoci explicitný typ poľa bol pri tomto mixe hodnôt zbytočný. Išlo o čisto syntaktickú
redundantnosť bez zmeny významu príkazového parametra.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `var param = new object?[] { target, wagon };` bol zmenený na
`var param = new[] { (object?)target, wagon };`.
• Fallback vetva ostala správaním rovnaká: stále používa `cmd.CanExecute(param)` a následne `cmd.Execute(param)`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že nový implicitný tvar poľa je prítomný a že
`DropCommand` fallback vetva zostáva zachovaná.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **92 / 92 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **103 / 103
passed, 0 regresií**.


## 2026-06-01 11:42
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránený redundantný alias `leftCount` vo výpočte
`WagonsRight`.
**Dôvod:** V proporčnom limite pre pravú stranu súpravy sa v `WagonsRight` najprv vytvoril lokálny alias
`var leftCount = locoPos;`, ktorý iba kopíroval už existujúcu hodnotu `locoPos` a hneď v ďalšom riadku sa použil len
raz. Išlo teda o čisto internú redundantnosť bez významu pre správanie.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z výpočtu `WagonsRight` bol odstránený riadok `var leftCount = locoPos;`.
• Výpočet `leftLimit` teraz používa existujúcu hodnotu `locoPos` priamo: `var leftLimit = locoPos > 0 ? ... : 0;`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že alias `leftCount` už v `VehicleStripItem`
neexistuje a proporcionálny výpočet `leftLimit/rightLimit` ostáva zachovaný.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **90 / 90 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **101 / 101
passed, 0 regresií**.


## 2026-06-01 11:36
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránené redundantné `null` guardy z animačných helperov
`AnimateActivateAsync` a `AnimateDeactivateAsync`.
**Dôvod:** Oba helpery prijímajú parameter typu `Border`, teda nenulovateľný referenčný typ, a sú volané len z miest,
ktoré si `Border` overujú ešte pred zavolaním. Podmienka `if (border == null) return;` preto zostala iba ako interná
redundantná obrana bez reálneho významu.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z `AnimateActivateAsync(Border border)` a `AnimateDeactivateAsync(Border border)` bol
odstránený guard `if (border == null) return;`.
• Správanie animačných helperov ostalo nezmenené; ďalej iba zabezpečujú `ScaleTransform` a prehrávajú krátku scale
animáciu.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že oba helpery v `VehicleStripItem` zostávajú
zachované, ale už neobsahujú redundantný `null` guard.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **89 / 89 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **100 / 100
passed, 0 regresií**.


## 2026-06-01 11:34
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – lokálny helper `UpdateDisplayNameInUI` premenovaný na
štýlovo správne `UpdateDisplayNameInUi`.
**Dôvod:** V `VehicleStripItem` zostal po predchádzajúcich clean-upoch už len čisto namingový analyzer warning, že názov
helper metódy `UpdateDisplayNameInUI` nevyhovuje pravidlu pre metódy. Keďže ide o privátny interný helper bez XAML
napojenia a bez externých usage mimo súboru, šlo o bezpečný lokálny refaktor bez zmeny správania.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) helper bol premenovaný na `UpdateDisplayNameInUi()`.
• Aktualizované boli všetky interné volania v `VehicleStripItem` (`flip orientácie`, detach/clear flow, reakcia na
`IsFlipped`, rename flow a `DetachSpecificWagon(...)`).
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že staré meno `UpdateDisplayNameInUI` už v súbore
neexistuje a zostáva používané nové `UpdateDisplayNameInUi`.
**Výsledok:**
• Prvý build/test beh zlyhal na externom locku `TrackFlow.dll` od Avalonia Designer hosta; lock bol uvoľnený cez
`kill-trackflow-locks.ps1` a následný rerun prebehol úspešne.
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **88 / 88 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **99 / 99
passed, 0 regresií**.


## 2026-06-01 11:28
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránený nepoužitý `ObjectModel` using a mŕtva lokálna
premenná `rightCount`.
**Dôvod:** Po predchádzajúcom čistení zostali v code-behind ešte dva čisto mŕtve zvyšky bez vplyvu na runtime správanie:
nepoužitý `using System.Collections.ObjectModel;` a lokálna premenná `rightCount`, ktorá sa vypočítala v `WagonsLeft`,
ale ďalej sa už vôbec nepoužila. Zároveň bolo dôležité nepotknúť živý `DropCommand` fallback, ktorý sa stále používa v
`OnDrop(...)`.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z `VehicleStripItem` bol odstránený nepoužitý `using System.Collections.ObjectModel;`.
• Z getteru `WagonsLeft` zmizla nepoužitá premenná `var rightCount = totalWagons - locoPos;`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že oba mŕtve zvyšky už neexistujú a že
`DropCommandProperty` / `DropCommand` / `GetValue(DropCommandProperty)` vetva zostáva zachovaná.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **87 / 87 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **98 / 98
passed, 0 regresií**.


## 2026-06-01 11:20
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránený nepoužívaný `PointerPressedCommand` styled
property wrapper.
**Dôvod:** Fulltext search ukázal, že `PointerPressedCommandProperty` aj CLR wrapper `PointerPressedCommand` sa už nikde
v projekte nepoužívajú. Reálne spracovanie pointer stlačenia v `VehicleStripItem` ide priamo cez
`PointerPressed="OnPointerPressed"`, takže starý property wrapper zostal len ako mŕtvy kód.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z `VehicleStripItem` bola odstránená deklarácia `PointerPressedCommandProperty`.
• Odstránený bol aj zodpovedajúci CLR wrapper
`public System.Windows.Input.ICommand? PointerPressedCommand { get; set; }` postavený na `GetValue/SetValue(...)`.
• Reálne pointer správanie ostalo nezmenené, pretože control aj doteraz používal priamy handler `OnPointerPressed` z
XAML.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresné asercie, že `PointerPressedCommandProperty` ani
`PointerPressedCommand` už v `VehicleStripItem` neexistujú.
**Výsledok:**
• Prvý build/test beh zlyhal na externom locku `TrackFlow.dll` od Avalonia Designer hosta; lock bol uvoľnený cez
`kill-trackflow-locks.ps1` a následný rerun prebehol úspešne.
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **86 / 86 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **97 / 97
passed, 0 regresií**.


## 2026-06-01 11:04
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránený mŕtvy legacy rename click flow (
`RenameTrain_Click`, `RenameTrainAsync`, `ShowFallbackRenameDialogAsync`).
**Dôvod:** Aktuálne context-menu premenovanie vlaku už dlhšie používa flow `OnRenameMenuClick` →
`OpenRenameMenuAsync()`. Starý flow cez `RenameTrain_Click` a fallback jednoduchého vstupného okna zostal v code-behind
iba ako nepoužívaný legacy kód bez napojenia v XAML aj bez volania z iných miest.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z `VehicleStripItem` boli odstránené nepoužívané metódy `RenameTrain_Click(...)`,
`RenameTrainAsync()` a `ShowFallbackRenameDialogAsync(...)`.
• Zmizla tým aj už neaktuálna diagnostická vetva `VehicleStripItem.RenameTrain_Click`, ktorá sa v reálnom UI už vôbec
nespúšťala.
• Reálne rename správanie ostalo nezmenené, pretože aktívny flow stále ide cez `OnRenameMenuClick` /
`OpenRenameMenuAsync()`.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` boli upravené z pôvodných asercií na legacy rename handler na regresný test, že
mŕtvy `RenameTrain_Click` / `RenameTrainAsync` / `ShowFallbackRenameDialogAsync` flow už v `VehicleStripItem` neexistuje
a že zostáva aktívny `OpenRenameMenuAsync()` flow.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **86 / 86 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **97 / 97
passed, 0 regresií**.


## 2026-06-01 10:59
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránený nepoužívaný placeholder `DetachThisWagonCommand`.
**Dôvod:** Kontextové menu pre „Odpojiť tento vagón“ bolo už reálne napojené cez
`Click="HandleDetachThisWagonMenuClick"`, nie cez command binding. `DetachThisWagonCommand` preto zostal len ako
nevyužitá property s pomocným helperom bez použitia v XAML aj mimo súboru.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z `VehicleStripItem` bola odstránená property `DetachThisWagonCommand`.
• Z konštruktora zmizlo aj priradenie `DetachThisWagonCommand = new RelayCommand<Wagon>(HandleDetachThisWagonCommand);`.
• Odstránený bol aj už nepotrebný helper `HandleDetachThisWagonCommand(Wagon? wagon)`.
• Reálne správanie ostalo nezmenené, pretože odpojenie konkrétneho vagóna už doteraz bežalo cez existujúci click handler
`HandleDetachThisWagonMenuClick(...)` a `DetachSpecificWagon(...)`.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresné asercie, že `DetachThisWagonCommand` už v `VehicleStripItem`
neexistuje a nezostal po ňom konštruktorový ani helper wiring.
**Výsledok:**
• Prvý build/test beh zlyhal na externom locku `TrackFlow.dll` od Avalonia Designer hosta; lock bol uvoľnený cez
`kill-trackflow-locks.ps1` a následný rerun prebehol úspešne.
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **87 / 87 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **98 / 98
passed, 0 regresií**.


## 2026-06-01 10:53
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránený nepoužívaný placeholder `RenameTrainCommand`.
**Dôvod:** Rename flow v `VehicleStripItem` sa už dlhšie reálne vykonáva cez `OnRenameMenuClick` / `RenameTrainAsync()`
a nie cez command binding. `RenameTrainCommand` preto zostal len ako nevyužitá property s placeholder `RelayCommand`,
bez použitia v XAML aj mimo súboru.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z `VehicleStripItem` bola odstránená property `RenameTrainCommand`.
• Z konštruktora zmizol aj placeholder `var rename = new RelayCommand(() => { /* placeholder */ });` a príslušné
priradenie `RenameTrainCommand = rename;`.
• Cleanup sa dotkol aj nadväzujúceho wiring-u: odstránené bolo `NotifyPropertyChanged(nameof(RenameTrainCommand));` aj
zbytočné `NotifyCanExecuteChanged()` pre neexistujúci rename command.
• Reálne rename správanie ostalo nezmenené, pretože menu aj doteraz používalo click-based flow (`OnRenameMenuClick`) a
nie command binding.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresné asercie, že `RenameTrainCommand` už v `VehicleStripItem`
neexistuje a nezostal po ňom konštruktorový ani refresh wiring.
**Výsledok:**
• Prvý build/test beh zlyhal na externom locku `TrackFlow.dll` od Avalonia Designer hosta; lock bol uvoľnený cez
`kill-trackflow-locks.ps1` a následný rerun prebehol úspešne.
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **87 / 87 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **98 / 98
passed, 0 regresií**.


## 2026-06-01 10:47
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml`, `Views/Shared/VehicleStripItem.axaml.cs`,
`TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup drag event wiring v `VehicleStripItem` – `DragEnter` je teraz naviazaný priamo na
`OnDragOver` bez pomocného forwarding handlera.
**Dôvod:** `VehicleStripItem` mal ešte drobný helper `OnDragEnter(...)`, ktorý len okamžite delegoval na
`OnDragOver(...)` bez vlastnej logiky. To je zbytočná medzivrstva v event wiring-u, ktorú sa dalo bezpečne odstrániť bez
zmeny správania drag&drop.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) v `VehicleStripItem.axaml` bolo `DragDrop.DragEnter="OnDragEnter"` nahradené priamym
`DragDrop.DragEnter="OnDragOver"`.
• Redundantný forwarding handler `private void OnDragEnter(object? sender, DragEventArgs e)` bol odstránený z
code-behind.
• Správanie ostalo rovnaké, pretože `DragEnter` už predtým robil len okamžité delegovanie do `OnDragOver`.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresné asercie, že XAML už viaže `DragEnter` priamo na `OnDragOver` a
forwarding metóda `OnDragEnter(...)` v code-behind neexistuje.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **86 / 86 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **97 / 97
passed, 0 regresií**.


## 2026-06-01 10:44
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup settings-refresh helpera v `VehicleStripItem` – odstránenie zvyšnej anonymnej
dispatcher lambda v `OnAppSettingsChanged`.
**Dôvod:** Po predchádzajúcich clean-upoch už v `VehicleStripItem` ostával len jeden drobný anonymný
`Dispatcher.UIThread.Post(() => ...)` helper v `OnAppSettingsChanged`. Správanie bolo správne, ale pre konzistentnosť s
ostatnými pomenovanými helpermi dávalo zmysel dotiahnuť aj toto miesto na priamy method-group call bez zbytočnej lambda
vrstvy.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `OnAppSettingsChanged` už nepoužíva blokovú lambda pre refresh limitu zobrazených
vagónov.
• Handler teraz plánuje refresh priamo cez `Dispatcher.UIThread.Post(RefreshVisibleWagonsLimit);`, takže behavior ostáva
rovnaký, ale wiring je kratší a konzistentnejší s ostatnými helper clean-upmi v `VehicleStripItem`.
• Bez zmeny ostal spôsob odpojenia/pripojenia `AppSettingsChanged` aj samotná logika `RefreshVisibleWagonsLimit()`.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresnú aserciu, že `VehicleStripItem` obsahuje
`Dispatcher.UIThread.Post(RefreshVisibleWagonsLimit);` v rámci lifecycle/settings cleanupu.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **86 / 86 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **97 / 97
passed, 0 regresií**.


## 2026-06-01 10:38
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup command helperov pre manipuláciu s vagónmi v `VehicleStripItem`.
**Dôvod:** Konštruktor `VehicleStripItem` stále obsahoval väčšie inline `RelayCommand` lambdy pre odpojenie posledného
vagóna, rozpustenie súpravy a odpojenie konkrétneho vagóna. Správanie už bolo stabilné, takže sa dalo bezpečne upratať
do pomenovaných helperov bez zásahu do business logiky.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `DetachLastWagonCommand` teraz používa pomenovaný helper `DetachLastWagon` namiesto
veľkej inline lambda v konštruktore.
• 🟨 (audit 4.5, priebežný krok) `ClearWagonsCommand` bol presunutý do helpera `ClearAttachedWagons`, čím sa správanie
zachovalo, ale konštruktor je čitateľnejší.
• 🟨 (audit 4.5, priebežný krok) `DetachThisWagonCommand` už používa helper `HandleDetachThisWagonCommand(Wagon? wagon)`
namiesto inline delegátu.
• Existujúca logika návratu vagónov do depa, resetu `TrainName`, prepočtu UI a `NotifyProjectChanged()` ostala
nezmenená.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresné asercie, že wagon commandy používajú pomenované helpery
`DetachLastWagon`, `ClearAttachedWagons` a `HandleDetachThisWagonCommand`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **86 / 86 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **97 / 97
passed, 0 regresií**.


## 2026-06-01 10:28
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup rename click flow v `VehicleStripItem` – odstránenie `async void` z
`RenameTrain_Click` a presun dialógovej logiky do pomenovaného `Task` helpera.
**Dôvod:** Po predchádzajúcich krokoch mal fallback rename flow už vyriešené TCS zavesenie, ale samotný XAML click
handler `RenameTrain_Click` bol stále `async void`. To je v UI vrstve zbytočne krehké miesto, lebo fire-and-forget
logika sa číta horšie než pri pomenovanom helperi a ťažšie sa ďalej rozširuje bez návratu k neprehľadnému event
handleru.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `RenameTrain_Click` už nie je `async void`; je to len tenký sync handler, ktorý plánuje
`_ = RenameTrainAsync();`.
• Nový helper `RenameTrainAsync()` teraz zastrešuje celý rename flow: pokus o otvorenie dedikovaného
`RenameTrainWindow`, fallback `ShowFallbackRenameDialogAsync(...)`, aplikovanie nového mena do modelu a UI refresh.
• Existujúci exception reporting ostal zachovaný v jednom mieste cez
`Program.ReportUnhandledException("VehicleStripItem.RenameTrain_Click", ...)` a Doctor warning
`Premenovanie vlaku zlyhalo: ...`, takže sa nemenila diagnostická stopa.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresné asercie, že sa už nepoužíva
`private async void RenameTrain_Click`, existuje sync handler s `_ = RenameTrainAsync();` a dialógová logika je
presunutá do `private async Task RenameTrainAsync()`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **85 / 85 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **96 / 96
passed, 0 regresií**.


## 2026-06-01 10:13
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový bugfix vo fallback rename flow `VehicleStripItem` – zatvorenie jednoduchého vstupného
okna už nenechá visieť `TaskCompletionSource`.
**Dôvod:** V náhradnom rename dialógu sa po `input.ShowDialog(owner)` alebo `input.Show()` čakalo na `tcs.Task`, ale
`TaskCompletionSource` sa dokončil iba cez klik na `OK`. Ak používateľ zavrel okno cez `X`, flow mohol zostať visieť bez
výsledku. Ide o izolovaný UI dialog helper bez zásahu do business logiky, preto bol vhodný na ďalší low-risk follow-up.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) fallback rename flow bol presunutý do pomenovaného helpera
`ShowFallbackRenameDialogAsync(...)`.
• Helper teraz používa `TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously)` a na
`input.Closed` explicitne volá `tcs.TrySetResult(null)`, takže zatvorenie okna cez `X` bezpečne vráti `null` namiesto
zablokovania flow.
• Event handlery `OK`, `Opened` a `Closed` sa po dokončení vždy odpoja vo `finally`, takže pomocné okno po sebe
nenecháva subscriby.
• `RenameTrain_Click` zachováva pôvodné správanie: najprv skúsi dedikovaný `RenameTrainWindow`, a až potom použije
fallback helper.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresnú aserciu, že fallback rename flow používa
`ShowFallbackRenameDialogAsync(...)`, registruje `input.Closed += OnInputClosed`, dokončuje `tcs` aj pri zatvorení okna
a handlery sa vo `finally` odpoja.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **85 / 85 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **96 / 96
passed, 0 regresií**.


## 2026-06-01 10:03
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový lifecycle cleanup v `VehicleStripItem` – nahradenie inline lambd za pomenované handlery a
deterministické odpojovanie/subscribovanie eventov pre lokomotívu a `SettingsManager`.
**Dôvod:** Po predchádzajúcich low-risk clean-upoch zostávali v konštruktore `VehicleStripItem` ešte anonymné handlery
pre `DataContextChanged`, `AttachedToVisualTree` a `DetachedFromVisualTree`. Tie síce fungovali, ale sťažovali čitateľný
cleanup a zvyšovali riziko, že pri detach/reattach scenároch zostanú pripojené eventy `Locomotive.AttachedWagons`,
`INotifyPropertyChanged` alebo `AppSettingsChanged` dlhšie než treba.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) inline lambdy boli nahradené pomenovanými handlermi `OnDataContextChanged`,
`OnAttachedToVisualTree` a `OnDetachedFromVisualTree`.
• Pribudli helpery `AttachToCurrentLoco()` a `DetachFromCurrentLoco()`, ktoré konzistentne spravujú
subscribe/unsubscribe pre `AttachedWagons.CollectionChanged` a `Locomotive_PropertyChanged`.
• `VehicleStripItem` teraz sleduje `_isInVisualTree` a v `RefreshVisibleWagonsLimit()` deterministicky odpája starý
`SettingsManager.AppSettingsChanged` a pri pripojenom visual tree znovu pripája aktuálny manager, takže pri výmene
owner/DataContext nevzniká reťazenie starých subscribov.
• Pôvodné správanie UI ostalo zachované: refresh limitu vagónov, opacity/icon refresh aj prepočet `CanExecute` sa volajú
rovnako ako doteraz.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresný test, že `VehicleStripItem` už nepoužíva inline lifecycle
lambdy, má pomenované handlery a explicitný cleanup pre loko/settings eventy.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **84 / 84 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **95 / 95
passed, 0 regresií**.


## 2026-06-01 02:17
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový hardening posledného tichého failure path v `VehicleStripItem` pri odpojení konkrétneho
vagóna z context menu.
**Dôvod:** V `HandleDetachThisWagonMenuClick(...)` ešte zostával holý `catch`, ktorý pri chybe v lookup flow (
`MenuItem.DataContext`, `ContextMenu.PlacementTarget`, fallback `DataContext`) potichu pohltil výnimku bez akejkoľvek
centrálnej diagnostiky. Keďže ide o izolovaný UI handler bez zásahu do business logiky, bol to vhodný ďalší low-risk
follow-up.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `VehicleStripItem.HandleDetachThisWagonMenuClick` už nepohlcuje chyby potichu; pri
zlyhaní zapisuje `Program.ReportUnhandledException("VehicleStripItem.HandleDetachThisWagonMenuClick", ...)`.
• Do `TrackFlowDoctorService` pribudol warning `Odpojenie konkrétneho vagóna zlyhalo: ...`, takže aj tento context-menu
flow má konzistentnú diagnostickú stopu ako ostatné pomocné UI handlery v `VehicleStripItem`.
• Samotné správanie odpojenia vagóna, fallback lookup poradie aj zápis zmeny do projektu ostali nezmenené.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o string-based regresnú aserciu, že handler už obsahuje centrálny
reporting a že pôvodný tichý `catch` v kóde nezostal.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **83 / 83 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **94 / 94
passed, 0 regresií**.


## 2026-06-01 02:00
===================
**Oblasť:** `ViewModels/Operation/OperationViewModel.cs`, `TrackFlow.Tests/OperationViewModelRouteActivationTests.cs`
**Zmena:** Opravené falošné okamžité obsadenie bloku po manuálnom priradení lokomotívy pri vypnutej / nepripojenej DCC
centrále.
**Dôvod:** Pri drag&drop priradení lokomotívy do bloku sa v `AssignLocomotiveToBlockAsync(...)` bezpodmienečne
nastavovalo `targetBlock.IsOccupied = true`. To spôsobovalo, že pri vypnutej centrále blok okamžite sčervenal ako
obsadený, hoci neprišlo žiadne potvrdenie externej occupancy zo senzora ani z centrálnej telemetrie.
**Riešenie:**
• `AssignLocomotiveToBlockAsync(...)` teraz rozlišuje stav pripojenia cez `dccClient?.IsConnected == true`.
• Pri pripojenej centrále ostáva doterajšie správanie zachované: cieľový blok sa po manuálnom priradení označí ako
obsadený.
• Pri nepripojenej / vypnutej centrále sa lokomotíva do bloku iba priradí (`AssignedLocoId`, `AssignedBlockId`, smer),
ale `IsOccupied` sa na cieľovom bloku nenastaví na `true`.
• Diagnostika bola rozlíšená na dva stavy: pri pripojenej centrále ostáva log `OBSADENÝ`, pri odpojenej sa zapisuje
`PRIRADENÝ ... – bez potvrdenej obsadenosti z centrály.`
**Testy:**
• Existujúci happy-path test `AssignLocomotiveToBlockAsync_SafePresun_AktualizujeBlokyALoko` bol upravený na explicitne
pripojený `TestDccCentralClient { IsConnected = true }`.
• Nový regresný test `AssignLocomotiveToBlockAsync_PriOdpojenejCentralneLenPriradiLokoBezObsadeniaBloku` overuje, že pri
`IsConnected = false` sa lokomotíva priradí, ale cieľový blok neprejde do `IsOccupied`.
**Výsledok:**
• Cielené testy `OperationViewModelRouteActivationTests` → **57 / 57 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez `FullyQualifiedName~Operation` → **125 / 125 passed, 0 regresií**.


## 2026-06-01 01:47
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový hardening `showProps` flow v `VehicleStripItem` – odstránenie `async` lambda z
`RelayCommand<object?>` a presun dialógovej logiky do pomenovaného helpera s centrálnym reportingom.
**Dôvod:** V `VehicleStripItem` už po posledných clean-upoch nezostávali tiché `catch { }`, ale stále tam bola krehká
`async` lambda v `showProps`, na ktorú upozorňovali aj editorové hlásenia. Ide o čistý UI helper flow pre otvorenie okna
vlastností lokomotívy alebo vagóna, preto je to vhodný ďalší low-risk krok bez zásahu do business logiky.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `var showProps = new RelayCommand<object?>(async param => ...)` bolo nahradené
bezpečnejším `param => _ = ShowPropertiesAsync(param)`.
• Nový helper `ShowPropertiesAsync(object? param)` zachováva pôvodné správanie: pre lokomotívu otvorí
`LocomotivesWindow`, pre vagón `VagonsWindow`, s rovnakým napojením na `MainWindowViewModel` a existujúce viewmodely.
• Pri neočakávanej chybe sa teraz zapisuje
`Program.ReportUnhandledException("VehicleStripItem.ShowPropertiesAsync", ...)` a warning do Doctora (
`Otvorenie vlastností vozidla zlyhalo: ...`).
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresné asercie, že sa už nepoužíva `async` `RelayCommand` lambda,
existuje `ShowPropertiesAsync()` a failure path je napojený na centrálnu diagnostiku.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **81 / 81 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **92 / 92
passed, 0 regresií**.


## 2026-06-01 01:43
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový hardening rename menu flow v `VehicleStripItem` – odstránenie `async` lambda z
`Dispatcher.UIThread.Post(...)` a doplnenie centrálnej diagnostiky pre celý dialógový flow.
**Dôvod:** Po dočistení väčšiny tichých catch blokov ostávala v `OnRenameMenuClick` ešte krehká
`Dispatcher.UIThread.Post(async () => ...)` lambda. Tá síce fungovala, ale výnimka po `await` sa diagnostikovala horšie
než pri pomenovanom helperi s explicitným reportingom.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `OnRenameMenuClick` už nepoužíva `async` dispatcher lambda; namiesto toho plánuje
`_ = OpenRenameMenuAsync()` na `DispatcherPriority.ApplicationIdle`, takže behavior ostal rovnaký a menu sa stále stihne
zavrieť pred otvorením rename okna.
• `OpenRenameMenuAsync()` teraz zastrešuje celý rename dialog flow vrátane existujúcej aktivácie už otvoreného
`RenameTrainWindow`, vytvorenia nového dialógu, predvyplnenia `NameTextBox` a aplikovania výsledku do modelu/UI.
• Pri neočakávanej chybe sa teraz zapisuje
`Program.ReportUnhandledException("VehicleStripItem.OpenRenameMenuAsync", ...)` a warning do Doctora (
`Otvorenie rename okna zlyhalo: ...`).
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresné asercie, že sa už nepoužíva
`Dispatcher.UIThread.Post(async () => ...)`, existuje `OpenRenameMenuAsync()` a failure path je napojený na centrálnu
diagnostiku.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **81 / 81 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **92 / 92
passed, 0 regresií**.


## 2026-06-01 01:38
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový hardening zvyšných silent-catch blokov v `OnDrop` a v aktivácii existujúceho rename okna
pre `VehicleStripItem`.
**Dôvod:** Po predchádzajúcich drobných krokoch zostávali v tom istom UI flow ešte malé tiché failure path pri
resetovaní kurzora po drop-e, pri skrytí drop indikátorov a pri `existing.Activate()` pre už otvorené rename okno. Sú to
pomocné UI kroky bez dopadu na business logiku, preto sú vhodné na ďalší low-risk follow-up.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `VehicleStripItem.OnDrop.CursorReset` teraz pri zlyhaní resetu kurzora zapisuje
`Program.ReportUnhandledException(...)` a warning do Doctora.
• 🟨 (audit 4.5, priebežný krok) `VehicleStripItem.OnDrop.IndicatorsReset` už nepohlcuje chybu pri schovávaní
ľavého/pravého drop indikátora potichu.
• 🟨 (audit 4.5, priebežný krok) `VehicleStripItem.OnRenameMenuClick.ActivateExistingWindow` má explicitný exception
reporting, takže zlyhanie `existing.Activate()` nezostane bez centrálnej diagnostiky.
• Zvyšok drop/rename flow ostal nezmenený; doplnená je len diagnostika failure path.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o string-based regresné asercie pre `OnDrop.CursorReset`,
`OnDrop.IndicatorsReset` a `OnRenameMenuClick.ActivateExistingWindow`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **79 / 79 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **90 / 90
passed, 0 regresií**.


## 2026-06-01
==============
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový hardening pointer-capture a cursor-helper catch blokov v drag lifecycle
`VehicleStripItem`.
**Dôvod:** Po predošlom kroku ostávali v tom istom drag flow ešte malé silent-catch vetvy pre `e.Pointer.Capture(this)`
a pomocné nastavovanie/reset kurzora pri ťahaní lokomotívy alebo vagóna. Sú to čisto UI pomocné operácie, takže išlo o
bezpečný follow-up bez zmeny správania drag&drop.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) pri zlyhaní `e.Pointer.Capture(this)` sa teraz zapisuje centrálna diagnostika pre oba
scenáre: `VehicleStripItem.OnPointerPressed.LocoPointerCapture` a
`VehicleStripItem.OnPointerPressed.WagonPointerCapture`.
• 🟨 (audit 4.5, priebežný krok) helper catch bloky pre nastavenie a reset drag kurzora v `OnPointerMoved` už nepohlcujú
výnimky potichu; každý z nich je napojený na `Program.ReportUnhandledException(...)` a warning do Doctora.
• Drag&drop flow, reset interného state a existujúce failure vetvy `LocoDrag` / `WagonDrag` ostali bezo zmeny; doplnená
je len diagnostika pre pomocné UI kroky.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o string-based regresné asercie pre `LocoPointerCapture`,
`WagonPointerCapture`, `LocoDrag.CursorSet`, `LocoDrag.CursorReset`, `WagonDrag.CursorSet` a `WagonDrag.CursorReset`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **79 / 79 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **90 / 90
passed, 0 regresií**.


## 2026-06-01
==============
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový hardening zostávajúcich silent-catch blokov v drag lifecycle `VehicleStripItem`.
**Dôvod:** Po doplnení diagnostiky do failure vetiev `OnPointerMoved` ešte stále ostávali bez centrálnej stopy menšie
chyby pri aktualizácii drag indikátorov, resetovaní kurzora a uvoľnení pointer capture. Sú to čisté UI pomocné kroky,
takže išlo o bezpečný follow-up bez zásahu do business logiky.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `OnDragOver` už pri zlyhaní aktualizácie ľavého/pravého drag indikátora zapisuje
`Program.ReportUnhandledException("VehicleStripItem.OnDragOver.Indicators", ...)` a warning do Doctora.
• 🟨 (audit 4.5, priebežný krok) `OnDragLeave` dostal explicitnú diagnostiku pre reset kurzora aj pre reset drag
indikátorov (`CursorReset`, `IndicatorsReset`).
• 🟨 (audit 4.5, priebežný krok) `OnPointerReleased` už pri zlyhaní `e.Pointer.Capture(null)` alebo resetu kurzora
nezostane ticho; oba prípady sú napojené na globálnu diagnostiku a Doctor warningy.
• Správanie drag&drop, reset interného pointer state a vizuálny cleanup ostali nezmenené; doplnená je len diagnostika
failure path.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o string-based regresné asercie pre
`VehicleStripItem.OnDragOver.Indicators`, `OnDragLeave.CursorReset`, `OnDragLeave.IndicatorsReset`,
`OnPointerReleased.PointerCaptureRelease` a `OnPointerReleased.CursorReset`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **78 / 78 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **89 / 89
passed, 0 regresií**.


## 2026-06-01
==============
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový hardening drag failure vetiev v `VehicleStripItem.OnPointerMoved`.
**Dôvod:** `VehicleStripItem` patrí medzi dlhšie code-behind súbory s viacerými legacy silent-catch blokmi. Ako bezpečný
prvý krok v drag flow dávalo zmysel doplniť diagnostiku len tam, kde `DoDragDropAsync(...)` mohlo zlyhať bez akejkoľvek
centrálnej stopy.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) v lokomotívnej drag vetve `OnPointerMoved` už `catch` neostáva prázdny; zapisuje
`Program.ReportUnhandledException("VehicleStripItem.OnPointerMoved.LocoDrag", ...)` a warning do Doctora so source
`Súprava`.
• 🟨 (audit 4.5, priebežný krok) rovnaký reporting bol doplnený aj do vagónovej drag vetvy (
`VehicleStripItem.OnPointerMoved.WagonDrag`).
• Správanie drag&drop, cleanup kurzora a reset interného pointer/drag stavu ostali nezmenené; doplnená je len
diagnostika pri zlyhaní.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o string-based regresné asercie pre
`VehicleStripItem.OnPointerMoved.LocoDrag` a `VehicleStripItem.OnPointerMoved.WagonDrag`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **77 / 77 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **88 / 88
passed, 0 regresií**.


## 2026-06-01
==============
**Oblasť:** `Views/Library/LocomotivesWindow.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový hardening štartu progres-dialógu pre čítanie CV v okne lokomotív.
**Dôvod:** V `ReadCvButton_Click` zostávala `async` event lambda na `dialog.Opened`, čo je v UI vrstve zbytočne krehké
miesto: výnimka po `await` by sa diagnostikovala horšie než pri pomenovanom helperi s explicitným reportingom.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `dialog.Opened += async ...` bolo nahradené pomenovaným lokálnym handlerom
`OnDialogOpened(...)`, ktorý len bezpečne odpojí sám seba a deleguje štart čítania do
`StartReadDecoderValuesDialogAsync(...)`.
• `StartReadDecoderValuesDialogAsync(...)` zachováva pôvodné priebežné aplikovanie načítaných CV hodnôt do UI, ale pri
neočakávanej chybe teraz zapisuje `Program.ReportUnhandledException(...)`, warning do `TrackFlowDoctorService` a dialóg
bezpečne zatvorí.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o regresné asercie, že sa už nepoužíva `async` event lambda na `Opened`,
existuje `StartReadDecoderValuesDialogAsync(...)` a failure path je napojený na centrálnu diagnostiku.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **76 / 76 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **87 / 87
passed, 0 regresií**.


## 2026-06-01
==============
**Oblasť:** `Views/Editor/BlockPropertiesWindow.axaml.cs`, `Views/Shared/VehicleStripItem.axaml.cs`,
`TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový pod-batch hardeningu `async void` / dialog handlerov v editorovom a strip UI flow.
**Dôvod:** Audit bod `4.5` je stále len priebežne rozpracovaný. Po pomocných oknách zostávali ešte drobné UI handlery,
kde chyba v dialógovom flow nemala zostať iba ako silent fail bez centrálnej diagnostiky.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `BlockPropertiesWindow.OpenIndicatorPropertiesWindow` je teraz obalený v `try/catch`;
pri zlyhaní zapisuje `Program.ReportUnhandledException(...)` a warning do `TrackFlowDoctorService` so source `Editor`.
• 🟨 (audit 4.5, priebežný krok) `VehicleStripItem.RenameTrain_Click` už nepohlcuje `Exception` potichu; chyba ide do
`Program.ReportUnhandledException(...)` a zároveň do Doctora ako warning so source `Súprava`.
**Testy:**
• `LocomotiveSpeedEditorMarkupTests` doplnené o string-based regresné asercie pre
`BlockPropertiesWindow.OpenIndicatorPropertiesWindow` a `VehicleStripItem.RenameTrain_Click`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **75 / 75 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **86 / 86
passed, 0 regresií**.


## 2026-06-01
==============
**Oblasť:** `Views/DoctorWindow.axaml.cs`, `Views/Backstage/FileBackstageView.axaml.cs`,
`Views/Dialogs/ReadDecoderValuesWindow.axaml.cs`, `TrackFlow.Tests/UtilityWindowAsyncVoidMarkupTests.cs` (nový)
**Zmena:** Ďalší nízkorizikový pod-batch hardeningu `async void` UI handlerov v pomocných oknách a utility view.
**Dôvod:** Audit bod `4.5` ostáva priebežne otvorený. Po predošlom hardeningu hlavných okien dávalo zmysel dorobiť aj
malé utility flow, kde chyba po `await` nemá dôvod skončiť tichým failom bez centrálnej diagnostiky.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `DoctorWindow.OnSaveLogClick`, `OnSaveFilteredLogClick`, `OnCopyFilteredLogClick` a
`OnCopyFullLogClick` sú nově obalené v `try/catch`; pri zlyhaní zapisujú `Program.ReportUnhandledException(...)` a
zároveň warning do Doctora s čitateľným textom.
• 🟨 (audit 4.5, priebežný krok) `FileBackstageView.OnViewLoaded` už pri zlyhaní inicializačného
`await Task.Delay(...)` / focus flow nenechá exception uniknúť z `async void`; chyba ide do globálnej diagnostiky aj do
`TrackFlowDoctorService`.
• 🟨 (audit 4.5, priebežný krok) `ReadDecoderValuesWindow.OnOpenedStartCompatReading` teraz pri neočakávanej chybe
kompatibilitného štartu nastaví `Error`, zaloguje výnimku do centrálnej diagnostiky a okno bezpečne zavrie.
**Testy:**
• Nový súbor `UtilityWindowAsyncVoidMarkupTests` kontroluje exception reporting pre `DoctorWindow`, `FileBackstageView`
a `ReadDecoderValuesWindow`.
**Výsledok:**
• Cielené testy `UtilityWindowAsyncVoidMarkupTests` → **3 / 3 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez `DoctorWindow|TrackFlowDoctorService|UtilityWindowAsyncVoidMarkupTests` → **22 / 22 passed, 0 regresií
**.


## 2026-06-01
==============
**Oblasť:** `Views/Operation/OperationView.axaml.cs`, `TrackFlow.Tests/OperationViewInteractionMarkupTests.cs`
**Zmena:** Doplnený lifecycle cleanup `OperationView` pri zmene `DataContext` a pri odpojení z visual tree.
**Dôvod:** Audit bod `1.4` upozorňoval, že `OperationView.OnDataContextChanged` pripájal nový `OperationViewModel`,
subscriboval `LayoutRefreshRequested` aj eventy lokomotív, ale starý VM nikdy neodpojil. Pri reopen/projekt swap
scenároch to mohlo držať starý VM nažive a multiplikovať refresh handlery.
**Riešenie:**
• 🟩 (audit 1.4) `OperationView` teraz drží explicitné pole `_vmCurrent` a pred každým novým bindom volá
`DetachFromVm()`.
• `DetachFromVm()` odpojí `LayoutRefreshRequested`, `Locomotives.CollectionChanged` a pre všetky aktuálne lokomotívy aj
`AttachedWagons.CollectionChanged` + `PropertyChanged`.
• Inline lambda na `Locomotives.CollectionChanged` bola nahradená pomenovaným handlerom
`OnLocomotivesCollectionChanged`, takže cleanup je deterministický a reverzibilný.
• View sa teraz pripája aj na `DetachedFromVisualTree`, takže rovnaký cleanup prebehne aj pri odpojení view z UI stromu,
nielen pri výmene `DataContext`.
**Testy:**
• `OperationViewInteractionMarkupTests` doplnené o regresné asercie pre `_vmCurrent`, `DetachFromVm()`, unsubscribe
logiku a `DetachedFromVisualTree` cleanup.
**Výsledok:**
• Cielené testy `OperationViewInteractionMarkupTests` → **8 / 8 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez `FullyQualifiedName~Operation` → **124 / 124 passed, 0 regresií**.


## 2026-06-01
==============
**Oblasť:** `Views/MainWindow.axaml.cs`, `Views/Operation/OperationView.axaml.cs`,
`Views/Library/LocomotivesWindow.axaml.cs`, `Views/Editor/LayoutEditorView.axaml.cs`,
`TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`, `TrackFlow.Tests/OperationViewInteractionMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový hardening `async void` UI handlerov v code-behind súboroch.
**Dôvod:** Audit bod `4.5` stále eviduje viacero `async void` handlerov, pri ktorých by výnimka po `await` mohla spadnúť
mimo lokálnej callstack diagnostiky alebo zostať len ako tichý fail v UI flow. Bolo vhodné začať od handlerov s
najnižším regresným rizikom a bez zásahu do business logiky.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) `MainWindow.OnWindowClosing` je nově obalený v `try/catch`; pri chybe sa zatvorenie
explicitne zablokuje cez `e.Cancel = true`, výnimka sa pošle do `Program.ReportUnhandledException(...)` a zároveň do
`TrackFlowDoctorService`.
• 🟨 (audit 4.5, priebežný krok) `OperationView.OnCanvasLocoDrop` teraz pri zlyhaní drag&drop nenechá exception utiecť z
`async void`; chyba sa zaloguje do globálnej diagnostiky a event sa bezpečne označí ako handled.
• 🟨 (audit 4.5, priebežný krok) `LocomotivesWindow.OpenCalibrationWindow_Click`, `OpenProgrammingTrackSettings_Click` a
`OnBrowseSoundFileClick` už nepohlcujú chyby potichu; každá výnimka ide do centrálnej diagnostiky s pomenovaným source
stringom.
• 🟨 (audit 4.5, priebežný krok) `LayoutEditorView` property dialóg handlery (`OnBlockPropertiesRequested`,
`OnTurnoutPropertiesRequested`, `OnSignalPropertiesRequested`, `OnRoutePropertiesRequested`,
`OnTextPropertiesRequested`) majú explicitný exception reporting namiesto silent catch blokov alebo neobaleného
`async void`.
**Testy:**
• Doplnené string-based regresné asercie v `LocomotiveSpeedEditorMarkupTests` pre `MainWindow.OnWindowClosing` a vybrané
`LocomotivesWindow` handlery.
• Doplnené string-based regresné asercie v `OperationViewInteractionMarkupTests` pre `OperationView.OnCanvasLocoDrop` a
`LayoutEditorView` property handlery.
**Výsledok:**
• 🟩 Tento dnešný pod-scope hardeningu `async void` UI handlerov je overený ako opravený.
• Cielené testy `LocomotiveSpeedEditorMarkupTests` + `OperationViewInteractionMarkupTests` → **79 / 79 passed, 0
regresií**.
• Build/test beh potvrdil, že v tomto rozsahu nevznikli nové chyby; ostali iba predchádzajúce warningy a stylistické
hlásenia.


## 2026-06-01
==============
**Oblasť:** `Services/Dcc/Z21Client.cs`, `Services/Dcc/Z21BroadcastFlags.cs` (nový),
`Services/Dcc/DccConnectionService.cs`, `Services/Dcc/DccFeedbackLayoutApplier.cs`, `ViewModels/MainWindowViewModel.cs`,
`TrackFlow.Tests/Z21ClientCvPacketTests.cs`, `TrackFlow.Tests/DccConnectionServiceTests.cs`,
`TrackFlow.Tests/DccFeedbackLayoutApplierTests.cs`
**Zmena:** Dokončené ďalšie nízkorizikové follow-up opravy po audite a následnom bugfixe spätnej väzby.
**Riešenie:**
• 🟩 (audit 5.2) Zavedený nový typovaný enum `Z21BroadcastFlags` a helper `Z21Client.CreateSetBroadcastFlagsPacket(...)`,
takže prevádzkové `LAN_SET_BROADCASTFLAGS` už nepoužívajú magic number `0x00000111` priamo v kóde. Správanie ostalo
rovnaké (`XBus | RBus | SystemState`), len je výrazne čitateľnejšie a bezpečnejšie pre budúce rozširovanie.
• 🟩 (post-audit bugfix) Opravené prežívanie starej obsadenosti bloku po `Disconnect → fyzický presun loko → Connect`.
Pri odpojení poslednej centrály sa teraz vynuluje celý runtime feedback stav kontaktov; pri odpojení iba jednej z
viacerých centrál sa čistí len príslušný profil. Tým už blok z predošlej jazdy (napr. `YY`) nezostane falošne červený po
ďalšom pripojení a novom obsadení iného bloku (`XX`).
• 🟩 (post-audit bugfix) `DccConnectionService` teraz propaguje `ProfileId` aj v single-central `Connected` /
`Disconnected` / `Reconnecting` / `ConnectFailed` eventoch, takže cleanup runtime feedbacku cieli presne na ten istý
profil, ktorý feedback publikuje.
**Testy:**
• `Z21ClientCvPacketTests` doplnené o regresné testy pre `CreateSetBroadcastFlagsPacket` (`XBus` a kombinácia
`XBus | RBus | SystemState`).
• `DccFeedbackLayoutApplierTests` doplnené o testy pre čistenie runtime feedback obsadenosti po disconnecte vrátane
`clearAll` scenára.
• `DccConnectionServiceTests` doplnené o regresný test, že single-central `Disconnected` event nesie správny
`ProfileId`.
**Výsledok:**
• Cielené testy pre `Z21ClientCvPacketTests`, `Z21ClientDisconnectTests`, `Z21ClientRBus*`, `DccConnectionServiceTests`,
`DccFeedbackLayoutApplierTests` → úspešné.
• `dotnet test TrackFlow.Tests --no-build` → **729 / 729 passed, 0 regresií**.


## 2026-06-01
==============
**Oblasť:** `Services/Dcc/Z21Client.cs`, `Services/Dcc/DccAddressCodec.cs`, `Services/Dcc/PerCentralConnection.cs`,
`Services/Dcc/SerialDccClient.cs`, `Views/Settings/SettingsWindow.axaml.cs`, `Views/Library/LocomotivesWindow.axaml.cs`,
`TrackFlow.Tests/Z21ClientRBusFeedbackTests.cs`, `TrackFlow.Tests/Z21ClientRBusGroupTests.cs` (nový)
**Zmena:** Implementácia opráv z `TECH_AUDIT_2026-05-31.md` (pôvodná priorita 🟥/🟧, dnes už hotové položky označené `🟩`).
**Dôvod:** Audit identifikoval kritický bug v R-BUS group mapovaní, blokujúci shutdown UI, race conditions v monitor
lifecycle, duplikovaný kód a chýbajúce diagnostiky.
**Riešenie:**
• 🟩 (4.1) `Z21Client.TryParseRBusDataChanged` – formula `data[4] * 10 + 1` (group 0 → moduly 1..10, group 1 → moduly
11..20, podľa Z21 LAN špec. v1.13 sekcia 7.1.2).
• 🟩 (3.2) `Z21Client.Dispose` / `StopTelemetry` – nahradené `.GetAwaiter().GetResult()` za
`Wait(TimeSpan.FromSeconds(2))` – UI thread sa už pri zatváraní okna nezablokuje na stuck sokete.
• 🟩 (4.2) `Z21Client.PublishRBusModuleState` – initial mask `0xFF` → `mask`: pri prvom rámci z modulu sa publikujú iba
reálne aktívne bity, nie 8 false-positive eventov.
• 🟩 (3.5) `Z21Client.StartTelemetry` – fire-and-forget registračné a poll-init Tasky sú teraz viazané na
`_telemetryCts.Token`, ktorý sa vytvára PRED ich spustením. Pri rýchlom Connect/Disconnect/Connect cyklus z
predchádzajúceho cyklu už nezasiahne nový socket.
• 🟩 (5.1) `Z21Client.PingAsync` – nahradený "heavy ping" (nový UDP socket každú iteráciu) ľahkou verziou: vracia `true`
ak telemetria dostala rámec v posledných 8 s, inak pošle `LAN_GET_SERIALNUMBER` cez existujúci `_sendUdp`. Šetrí
ephemeral porty a zachováva NAT mapovanie.
• 🟩 (5.6) `Z21Client.SendAsync` – pri nesietovej výnimke pred `IsConnected=false` zapisuje Warning do Doctora (
`Z21 send chyba: {Type}: {Message}`).
• 🟩 (4.4) `Z21Client.SetLocomotiveSpeedAsync` – throttle prešiel na `Interlocked.CompareExchange` (read-modify-write je
teraz thread-safe; eliminuje "škubavé rozbiehanie" pri súbežných UI eventoch).
• 🟩 (2.1) `DccAddressCodec.EncodeLocoAddress` – nový helper; refaktor 4× duplikovaného encodingu v `Z21Client` (drive,
function, POM read, POM write).
• 🟩 (5.5) `Z21Client.SetExtendedAccessoryAspectAsync` – pridaná validácia 11-bit adresy (0..2047) a 5-bit aspectu (
0..31). Zlikvidovaný komentár `// OPRAVENÉ:`.
• 🟩 (5.3) `PerCentralConnection.StopMonitorAsync` – nová verzia ktorá najprv `Cancel()` a počká (max 2 s) na dobehnutie
monitor loopu, až POTOM disposne CTS. Zlikvidovaný race condition Disconnect → Connect a `ObjectDisposedException` v
`Task.Delay(...,ct)`. Pôvodný sync `StopMonitor` ostáva ako wrapper pre `Dispose`/`Disconnect`.
• 🟩 (1.1) `SettingsWindow` – `_tabIndexSubscription.Dispose()` doplnené do `Closing` (predtým ostal subscribed na
observable pri každom opätovnom otvorení → memory leak rastúci s počtom otvorení).
• 🟩 (2.2) `SerialDccClient` – odstránené 3 mŕtve metódy (`ReadResponseFrameAsync`, `ReadByteWithTimeoutAsync`,
`BuildStatusSummary`) – ~120 LOC.
• 🟩 (1.6) `LocomotivesWindow.HookTickRebuild` – inline lambdy slider handlerov uložené do fieldov, `UnhookTickRebuild`
zavolaný v `Closed` (predtým držali referenciu na window).
**Testy:**
• `Z21ClientRBusGroupTests` (nový, 4 testy): `Parses_Group0_StartsAtModule1`, `Parses_Group1_StartsAtModule11`,
`Parses_Group1_DoesNotCollideWith_Group0`, `InitialFrame_PublishesOnlyActiveBits`.
• `Z21ClientRBusFeedbackTests` aktualizované pre novú initial-mask semantiku (publikujú sa iba aktívne bity).
**Výsledok:**
• Build `TrackFlow.csproj` (Debug) → 0 errors.
• `dotnet test TrackFlow.Tests` → **725 / 725 passed, 0 regresií** (vrátane existujúcich `Z21ClientRBusFeedbackTests`,
`DccFeedbackLayoutApplierTests`, `PerCentralConnection`-súvisiacich a 4 nových group testov).
**Nezahrnuté z auditu (vyžadujú väčšiu architektonickú diskusiu, navrhnuté ako follow-up):**
• 🟧 (1.3) Rozdelenie `OperationViewModel` (5090 LOC) na `OperationRoutingService` / `OperationSignalCoordinator` /
`OperationDccBridge`.
• 🟩 (1.4) `OperationView.OnDataContextChanged` cleanup starého VM (vyžaduje súčinnosť s VM lifecycle redesignom).
• 🟧 (1.2) Refactor `Func<…>` callbackov v `MainWindowViewModel` na `IDialogService` cez DI.
• 🟧 (3.4) `LayoutEditorView.RefreshLayout` – per-element diff namiesto full canvas rebuild.
• 🟧 (3.3) `DccConnectionService.EnsureClientFromEffective` → `EnsureClientFromEffectiveAsync`.
• 🟩 (4.7) `SetTurnoutAsync(bool branch, bool activate)` – breaking change pre `IDccCentralClient` (potrebné zladiť s
`SignalController`).
• 🟩 (5.2) `Z21BroadcastFlags [Flags]` enum (eliminácia magic `0x00000111`) – hotové, viď novší záznam z `2026-06-01`
vyššie.
• 🟩 (4.5) Obalenie `async void` UI handlerov (20 výskytov) do try/catch + diagnostiky.
• 🟩 (4.6) Default `int locoAddress = 3` v POM cestách je **zámerne ponechaný** a bod sa uzatvára rozhodnutím. Aktuálne
správanie je považované za správne pre tento projekt, preto sa do signatúr `ReadCvAsync` / `WriteCvAsync` nesiahlo.
• 🟦 (1.7) Odstránenie WinForms dependency – v csproj `<UseWindowsForms>true</UseWindowsForms>` a používané dialógy (
FontDialog/ColorDialog) by sa museli nahradiť.
• 🟦 (2.6) Konsolidácia `Converters/` (19 konverterov).
• 🟨 (3.1) Sync wrapper `OperationViewModel.AdvanceReservationWindow` / `RefreshSignalStatus` – v produkčnej ceste sa už
používa async verzia; sync metóda existuje len kvôli `OperationViewModelDoctorDiagnosticsTests`, ktoré ju volajú cez
reflection. Refactor by si vyžiadal úpravu reflection-based testov.


## 2026-05-31
==============
**Oblasť:** `Services/Dcc/Z21Client.cs`, `Services/Dcc/DccConnectionService.cs`,
`Services/Dcc/DccFeedbackLayoutApplier.cs`, `Services/ProjectMigrationService.cs`,
`ViewModels/Editor/IndicatorPropertiesViewModel.cs`, `Views/Editor/IndicatorPropertiesWindow.axaml`,
`ViewModels/Editor/BlockIndicatorViewModel.cs`, `Views/Editor/BlockPropertiesWindow.axaml.cs`,
`TrackFlow.Tests/Z21ClientRBusFeedbackTests.cs`, `TrackFlow.Tests/DccFeedbackLayoutApplierTests.cs`,
`TrackFlow.Tests/ProjectMigrationServiceTests.cs`, `TrackFlow.Tests/IndicatorPropertiesViewModelTests.cs`,
`TrackFlow.Tests/BlockIndicatorViewModelTests.cs`
**Zmena:** Dokončené oživenie detekcie obsadenia cez `z21` / `R-BUS` a súvisiace UI pre kontaktné indikátory. Feedback
sa teraz berie iba z priamych `LAN_RMBUS_DATACHANGED (0x80 0x00)` rámcov, duplicate väzby jedného fyzického vstupu na
viac blokov sa už nerozlievajú do nesprávneho obsadenia, polling `R-BUS` bol zrýchlený pre citeľne rýchlejšiu reakciu a
vrátená bola aj testovacia ikonka v záložke `Pripojenie`, ktorá opäť zobrazuje aktívny/neaktívny stav zadaného kontaktu.
**Dôvod:** Pôvodný stav sa skladal z viacerých navrstvených problémov: zmätočné/parazitné `0x43` rámce, faulted UDP
receive tasky pri shutdown-e, preťaženie logmi pri opakovaných maskách, staré projekty s neúplným `DccCentralProfileId`
alebo neplatným `0/0` kontaktom, omylom duplicitne priradený `modul/vstup` viacerým blokom a zároveň neviditeľná
testovacia ikonka vo vlastnostiach indikátora. Výsledkom boli stavy od `RBUS no-match`, cez falošné obsadenie dvoch
blokov naraz, až po zdanlivé oneskorenie obsadenia a chýbajúcu vizuálnu spätnú väzbu v UI.
**Riešenie:**

1. `Z21Client` dostal bezpečné rušenie UDP receive slučiek cez fault-observing helper nad
   `ReceiveAsync().WaitAsync(ct)`, aby pri odpojení/zatváraní nezostávali neodpozorované výnimky
   `TaskScheduler.UnobservedTaskException` / `SocketException (995)`.
2. Parsovanie obsadenia bolo zúžené len na spoľahlivé priame `LAN_RMBUS_DATACHANGED (0x80 0x00)` rámce; pôvodná
   interpretácia `LAN_X 0x43` pre obsadenie bola odstránená, keďže v reálnej prevádzke kolísala aj pri iných udalostiach
   než fyzickom senzore.
3. Telemetria/polling bola rozdelená: systémový stav zostal na `2000 ms`, ale `LAN_RMBUS_GETDATA(group=0)` sa teraz pýta
   každých `250 ms`, takže reakcia obsadenia je citeľne rýchlejšia bez zbytočného zahltenia zvyšku telemetrie.
4. `Z21Client` si cache-uje posledné masky po moduloch a publikuje len zmenené bity; tým sa odstránilo zahltenie
   UI/logov pri opakovanom rovnakom stave a následné zamŕzanie po pripojení centrály.
5. `DccFeedbackLayoutApplier` bol doplnený o ochranu proti duplicitnému bindovaniu jedného fyzického vstupu na viac
   blokov. Ak je vstup nejednoznačný, feedback sa zámerne neaplikuje a do Doctora sa zapíše varovanie
   `RBUS duplicate-binding ambiguous...`, namiesto toho, aby sa obsadili nesprávne bloky.
6. `ProjectMigrationService` dostal repair helper pre legacy projekty: kontaktný indikátor bez `DccCentralProfileId` si
   vie prevziať jednoznačne zvolený aktívny profil a zároveň sa diagnostikujú neplatné konfigurácie
   `ModuleAddress/PortNumber` typu `0/0`.
7. V editore indikátorov boli zjednotené asset cesty na `avares://TrackFlow/...` a pre okno `IndicatorPropertiesWindow`
   bol testovací obrázok prepojený cez priamo načítaný `IImage` (`TestIconImage`), takže ikonka `Test:` je opäť
   spoľahlivo viditeľná a mení sa medzi šedou a farebnou podľa `indicator.IsActive`.
8. `BlockIndicatorViewModel` a kreslenie v `BlockPropertiesWindow` boli zosúladené na explicitné Avalonia asset URI, aby
   aj ikony kontaktných indikátorov v block editore/canvase korektne reflektovali aktivitu.
9. Reálny pracovný projekt bol zároveň vyčistený od chybne duplicitného priradenia `modul=1, vstup=8`, takže po oprave
   už platí jednoznačné mapovanie `Blok 1 -> vstup 7` a `Blok 4 -> vstup 8`.
   **Výsledok:** Obsadenie blokov cez `z21` / `R-BUS` teraz v reálnom testovaní funguje korektne a bez falošného
   rozsvietenia ďalších blokov. Reakcia na obsadenie je vďaka `250 ms` polling intervalu citeľne svižnejšia, Doctor
   logika jasne hlási nejednoznačné bindy namiesto tichých chýb a vo `Vlastnostiach indikátora -> Pripojenie` je
   obnovená testovacia ikonka so správnym šedým/farebným stavom podľa aktuálnej aktivity kontaktu. Zmeny boli priebežne
   overené cielenými testami (`Z21ClientRBusFeedbackTests`, `DccFeedbackLayoutApplierTests`,
   `ProjectMigrationServiceTests`, `IndicatorPropertiesViewModelTests`, `BlockIndicatorViewModelTests`) aj úspešnými
   buildmi riešenia.

## 2026-05-31
==============
**Oblasť:** `Services/SettingsManager.cs`, `ViewModels/Editor/IndicatorPropertiesViewModel.cs`,
`ViewModels/Editor/TurnoutPropertiesViewModel.cs`, `ViewModels/Library/LocomotivesWindowViewModel.cs`,
`TrackFlow.Tests/IndicatorPropertiesViewModelTests.cs`, `TrackFlow.Tests/TurnoutPropertiesViewModelTests.cs`,
`TrackFlow.Tests/LocomotivesWindowViewModelDccCentralFilteringTests.cs`
**Zmena:** Globálne zjednotené filtrovanie DCC centrál v ComboBoxoch pre výber centrál. Vo všetkých dotknutých editoroch
a formulároch sa už používateľovi zobrazujú iba aktívne/zaškrtnuté profily (`IsEnabled = true`); vypnuté profily
zostávajú uložené v konfigurácii, ale už sa neponúkajú na priradenie k prvkom layoutu ani k lokomotívam.
**Dôvod:** Pred opravou sa do výberových zoznamov DCC centrál načítavali všetky definované profily bez ohľadu na to, či
ich používateľ v nastaveniach nechal aktívne. To bolo nekonzistentné voči runtime správaniu aplikácie, kde sa vypnuté
centrály už ignorovali pri pripájaní a reconnect logike, no UI ich stále ponúkalo pri konfigurácii indikátorov, výhybiek
a lokomotív.
**Riešenie:**

1. `SettingsManager` bol rozšírený o centralizovaný helper `GetEffectiveEnabledDccCentralProfiles()`, ktorý vracia iba
   efektívne a zároveň povolené DCC profily.
2. `IndicatorPropertiesViewModel.LoadDccSystems()` bol prepnutý z `GetEffectiveDccCentralProfiles()` na nový filtrovaný
   helper, takže vo `Vlastnostiach indikátora` zostávajú v ComboBoxe iba aktívne centrály plus položka `Bez pripojenia`.
3. Rovnaké pravidlo bolo aplikované aj v `TurnoutPropertiesViewModel.LoadDccSystems()`, aby výber centrály pre výhybky
   používal identický zoznam ako ostatné editory.
4. `LocomotivesWindowViewModel` teraz plní `DigitalSystems` iba z aktívnych profilov a vlastnosť
   `HasConfiguredDigitalSystems` berie do úvahy už len povolené centrály. Ak existujú iba vypnuté profily, editor
   lokomotív korektne ponechá len možnosť `Bez pripojenia`.
5. Doplnené/regresne upravené testy: `IndicatorPropertiesViewModelTests` overujú, že vypnutý profil sa už do
   `DccSystems` nenačíta; `TurnoutPropertiesViewModelTests` pokrývajú rovnaké pravidlo pre výhybky; nový súbor
   `LocomotivesWindowViewModelDccCentralFilteringTests` kontroluje filtrovanie aj správne vyhodnotenie dostupnosti
   digitálnych systémov v okne lokomotív.
   **Výsledok:** Používateľ teraz vidí v ComboBoxoch pre výber DCC centrály iba tie profily, ktoré sú v nastaveniach
   reálne aktívne. UI je tým konzistentné s runtime logikou aplikácie a zabraňuje priraďovaniu prvkov k vypnutým
   centrálam. Zmena bola overená cielenými regresnými testami pre indikátory, výhybky a lokomotívy (36/36 úspešných
   testov v cieľovej sade).

## 2026-05-31
==============
**Oblasť:** `Services/Dcc/Z21Client.cs`, `ViewModels/Settings/ConfiguredDccCentralItem.cs`,
`ViewModels/Settings/DccCommunicationTestHandler.cs`, `ViewModels/Settings/SettingsViewModel.cs`,
`ViewModels/MainWindowViewModel.cs`, `Views/MainWindow.axaml.cs`, `Views/Library/LocomotivesWindow.axaml.cs`,
`Views/Settings/SettingsWindow.axaml.cs`, `TrackFlow.Tests/Z21ClientDisconnectTests.cs`,
`TrackFlow.Tests/SettingsReopenLifecycleTests.cs`, `TrackFlow.Tests/DccCommunicationTestHandlerTests.cs`
**Zmena:** Opravené zamŕzanie aplikácie pri práci s oknom `Nastavenia` po úspešnom z21 komunikačnom teste a následnom
odpojení centrál. Súčasne bol odstránený aj naväzujúci bug, pri ktorom sa po druhom otvorení `Nastavení` bez pripojenej
centrály už neprepínal hint v paneli „Testovanie komunikácie“ medzi textami `DCC centrála musí byť pripojená.` a
`Režim POM nepodporuje čítanie CV.`.
**Dôvod:** Po sekvencii „otvoriť Nastavenia → prepínať programming mode → otestovať komunikáciu so z21 → zavrieť
Nastavenia → odpojiť centrály → znovu otvoriť Nastavenia“ zostával v aplikácii rozbitý lifecycle kombinácie
`SettingsWindow` / `SettingsViewModel` / `DccCommunicationTestHandler` / `Z21Client`. Prvá časť chyby spôsobovala
zamrznutie UI pri ďalšom otvorení okna, druhá časť ponechávala medzi otvoreniami starý stav komunikačného handlera,
takže POM radiobutton už nepretlačil správny odvodený hint text.
**Riešenie:**

1. `Z21Client` dostal koordinovaný neblokujúci shutdown: `Disconnect()` už nečaká synchronicky na `_sendLock`,
   receive/poll slučky sa pri lokálnom shutdown-e korektne ukončia a `SocketException`/`ObjectDisposedException` počas
   zatvárania socketu sa už nespracovávajú ako bežný reconnect-worthy fail.
2. `ConfiguredDccCentralItem` bol doplnený o bezpečné odpojovanie telemetrie (`Dispose()`) a marshaling
   `PropertyChanged` notifikácií na Avalonia UI thread, aby po reload/reopen cykloch nezostávali visieť staré background
   callbacky do už nepoužívaných itemov.
3. `SettingsViewModel.Load()` teraz pred znovunaplnením `ConfiguredCentrals` explicitne odpojí telemetriu starých
   položiek; zároveň bol doplnený riadený `Dispose()` pre per-dialog inštancie viewmodelu vrátane odhlásenia z
   `DccConnectionService` a cleanupu `TestHandler`.
4. `SettingsWindow.axaml.cs` pri zatvorení okna odpojí starý viewmodel cez `AttachToVm(null)`, takže sa nehromadia
   zavesené `CloseRequested` subscriptiony na už zavretých oknách.
5. Architektúra otvorenia `Nastavení` bola prepracovaná: namiesto jednej dlhodobo zdieľanej inštancie
   `MainWindowViewModel.Settings` sa teraz pre každé otvorenie dialógu vytvára nový `SettingsViewModel` cez
   `CreateSettingsDialogViewModel(...)`. Rovnaký model sa použije aj pri otvorení DCC tabu z okna lokomotív. Po
   zatvorení sa per-open viewmodel korektne dispose-ne.
6. Tým sa odstránil aj second-open bug v `DccCommunicationTestHandler`: prepínanie `IsServiceTrackProgrammingMode` po
   ďalšom otvorení okna opäť korektne prepočítava `DisabledConnectionHint`, takže pri nepripojenej centrále POM voľba
   zobrazuje `Režim POM nepodporuje čítanie CV.` a Service Track voľba `DCC centrála musí byť pripojená.`.
7. Pridané regresné testy: `Z21ClientDisconnectTests` overujú, že `Disconnect()` sa neblokuje ani pri držanom internom
   semafore; `SettingsReopenLifecycleTests` kontrolujú odpojenie starej telemetrie pri reload/reopen cykle;
   `DccCommunicationTestHandlerTests` pokrývajú scenár druhého otvorenia Nastavení bez pripojenia a správne prepínanie
   POM hintu.
   **Výsledok:** Scenár `z21 test OK -> Zrušiť -> Odpojiť centrály -> znovu otvoriť Nastavenia` už aplikáciu nezamrzí a
   panel `Testovanie komunikácie` sa správa konzistentne aj pri opakovanom otvorení okna. Overené cielenými regresnými
   testami (`Z21ClientDisconnectTests`, `SettingsReopenLifecycleTests`, `DccCommunicationTestHandlerTests`, súvisiace
   DCC lifecycle testy) aj úspešným buildom riešenia.

## 2026-05-31
==============
**Oblasť:** `Models/DccCentralProfile.cs`, `ViewModels/Settings/ConfiguredDccCentralItem.cs`,
`Views/Settings/SettingsWindow.axaml`, `Services/Dcc/DccConnectionService.cs`, `ViewModels/MainWindowViewModel.cs`,
`ViewModels/StatusBarViewModel.cs`
**Zmena:** DCC profily dostali novú voľbu `IsEnabled`, ktorou môže používateľ v okne Nastavení rozhodnúť, ktoré centrály
sa majú reálne brať do úvahy. Vypnuté centrály sa už nepripájajú, nespúšťajú reconnect, neovplyvňujú stav Ribbon
tlačidla a po poslednej úprave sa už vôbec nezobrazujú ani v spodnom stavovom riadku.
**Dôvod:** Používateľ potreboval mať možnosť ponechať profily v konfigurácii, ale dočasne ich úplne vyradiť z runtime
logiky bez ich zmazania. Pred opravou sa neaktívna centrála síce vedela prestať pripájať, no stále sa zobrazovala v
`StatusBar`, mýlila používateľa a ovplyvňovala vizuálny dojem multi-central režimu.
**Riešenie:**

1. `DccCentralProfile` rozšírený o `bool IsEnabled = true`; vlastnosť sa automaticky serializuje/deserializuje v app
   konfigurácii.
2. `ConfiguredDccCentralItem` dostal delegovanú `IsEnabled` property a `SettingsWindow.axaml` bol doplnený o `CheckBox`
   v prvom stĺpci zoznamu centrál (`TwoWay` binding), takže používateľ môže profily zapínať/vypínať priamo v zozname.
3. `DccConnectionService.ConnectAllAsync()` a `ConnectMissingAsync()` teraz filtrujú profily cez
   `.Where(p => p.IsEnabled)`, takže sa pripájajú iba aktívne centrály.
4. `DccConnectionService.ApplyDccAfterSettingsSavedAsync()` doplnený o cleanup `DisconnectDisabledMultiConnections()`,
   ktorý pri vypnutí už bežiaceho profilu okamžite odpojí a dispose-ne jeho runtime connection, odstráni ho z
   `_multiConnections` aj `_reconnectingProfileIds` a zabráni ďalšiemu reconnect loopu.
5. `MainWindowViewModel` – helpery `AreAllProfilesConnected()` a `ShouldRibbonShowDisconnectState()` boli upravené tak,
   aby počítali iba aktívne profily; Ribbon teda ignoruje neaktívne centrály pri rozhodovaní `Pripojiť/Odpojiť`.
6. `StatusBarViewModel.UpdateCentrals()` teraz najprv vytvorí
   `enabledProfiles = profiles.Where(p => p.IsEnabled).ToList()` a až z tohto zoznamu generuje `StatusBarCentralItem`.
   Vďaka tomu sa vypnuté centrály v spodnom stavovom riadku vôbec nevykreslia.
7. Opravené aj `IsLast` – posledná aktívna centrála je určovaná po filtrovaní, takže zvislý oddeľovač `|` v lište mizne
   správne aj v prípade, že za ňou v konfigurácii nasledujú neaktívne profily.
8. Refresh po uložení nastavení bol potvrdený cez existujúce `MainWindowViewModel.HandleSettingsDialogClosedAsync()` →
   `RefreshStatusBarCentrals()`, takže po odškrtnutí profilu centrála zo status baru zmizne okamžite bez reštartu
   aplikácie.
   **Výsledok:** Používateľ môže mať v konfigurácii viac profilov, no runtime sa riadi iba zaškrtnutými centrálami.
   Vypnuté centrály sa nepripájajú, nereconnectujú, nezobrazujú sa v stavovom riadku a Ribbon tlačidlo ich už neberie do
   úvahy. Hlavný projekt build 0 chýb; test projekt build 0 chýb (ostali len staršie nesúvisiace warningy v iných
   testoch).

## 2026-05-31
==============
**Oblasť:** `Models/AppSettingsData.cs`, `Views/Settings/SettingsWindow.axaml`,
`ViewModels/Settings/SettingsViewModel.cs`, `Services/Dcc/ITelemetryPreferenceAwareClient.cs`,
`Services/Dcc/Z21Client.cs`, `Services/Dcc/DccConnectionService.cs`, `ViewModels/StatusBarCentralItem.cs`,
`ViewModels/StatusBarViewModel.cs`, `ViewModels/MainWindowViewModel.cs`,
`TrackFlow.Tests/SettingsProjectDccProfilesTests.cs`
**Zmena:** Doplnená používateľská voľba pre zobrazenie telemetrie v stavovom riadku a opravená logika Ribbon tlačidla
`Pripojiť/Odpojiť` v multi-central režime. Súčasne boli zaktualizované zastarané testy okolo DCC profilov, aby
zodpovedali súčasnej architektúre nastavení.
**Dôvod:** Nie každý používateľ chce v stavovom riadku vidieť napätie a prúd; pri jednoduchších centrálach alebo NanoX
táto informácia nemá pridanú hodnotu. Zároveň bolo tlačidlo v Ribbone chybne naviazané na stav „všetky centrály
pripojené“, takže pri scenári „jedna z dvoch centrál stále beží“ zostávalo v stave `Pripojiť` a neumožnilo manuálne
odpojiť už pripojenú centrálu. Testy `SettingsProjectDccProfilesTests.cs` medzitým stále referovali na historické
projektové DCC profily a rozbitú migračnú logiku, ktorá už v produkte neexistuje.
**Riešenie:**

1. `AppSettingsData` rozšírené o globálny boolean `ShowTelemetryInStatusBar` (predvolene `true`).
2. `SettingsWindow.axaml` + `SettingsViewModel` – na záložku `Všeobecné` pridaný nový `CheckBox` „Zobrazovať
   telemetrické údaje v stavovom riadku“, ktorý sa načíta a uloží do app settings.
3. `StatusBarCentralItem` dostal provider `SetTelemetryVisibilityProvider(Func<bool>)`; `TelemetryText` pri vypnutej
   voľbe vracia prázdny reťazec, takže sa bez zmeny wiring-u skryje telemetrický text v UI.
4. `StatusBarViewModel.UpdateCentrals()` a `MainWindowViewModel.RefreshStatusBarCentrals()` boli rozšírené o
   forwardovanie globálneho telemetry-visible flagu do každej položky stavového riadku.
5. Vytvorené rozhranie `ITelemetryPreferenceAwareClient`; `Z21Client` ho implementuje cez `IsTelemetryEnabled`.
   `TelemetryPollLoopAsync()` pri vypnutej voľbe preskočí UDP polling, takže sa zbytočne nezaťažuje sieť ani centrála.
6. `DccConnectionService` pri vytváraní/obnove klientov aplikuje runtime telemetry preference (
   `ApplyTelemetryPreference(...)`) a pri `ApplyDccAfterSettingsSavedAsync(...)` ich okamžite pretláča aj do už
   existujúcich klientov, aj keď sa DCC signatúra vôbec nezmenila.
7. `MainWindowViewModel` – zavedený helper `ShouldRibbonShowDisconnectState()`. Ribbon teraz ukazuje `Odpojiť`, ak je
   pripojená aspoň jedna centrála alebo prebieha reconnect aspoň jednej centrály; do `Pripojiť` sa vráti až keď nie je
   pripojená ani reconnectujúca žiadna centrála.
8. `SettingsProjectDccProfilesTests.cs` kompletne prepísaný na aktuálny model: DCC profily sú app-scoped (
   `AppSettingsData.DccCentralProfiles`), projekt nesie iba legacy scalar override polia. Nové testy overujú
   load/save/reload správanie a výber profilu bez neexistujúcich API (`TryMigrateLegacyProjectDccToProfiles`,
   `Project.UseProjectDcc`, `Project.DccCentralProfiles`, `DccProfilesScopeLabel`, ...).
   **Výsledok:** Používateľ môže telemetriu v stavovom riadku kedykoľvek vypnúť/zapnúť priamo z nastavení; pri vypnutí
   sa text skryje a z21 prestane pollovať telemetrické dáta. Ribbon tlačidlo už v multi-central režime korektne ostáva v
   stave `Odpojiť`, ak beží aspoň jedna centrála alebo reconnect loop. Hlavný projekt aj test projekt boli po zmene
   overené buildom bez chýb.

## 2026-05-31
==============
**Oblasť:** `Services/Dcc/IDccTelemetry.cs`, `Services/Dcc/Z21Client.cs`, `Services/Dcc/SerialDccClient.cs`,
`ViewModels/MainWindowViewModel.cs`, `ViewModels/StatusBarViewModel.cs`, `ViewModels/StatusBarCentralItem.cs`,
`ViewModels/Settings/ConfiguredDccCentralItem.cs`, `ViewModels/Settings/SettingsViewModel.cs`,
`Views/StatusBarView.axaml`
**Zmena:** Zavedené živé vyčítanie a zobrazenie telemetrie DCC centrál. z21 teraz poskytuje napätie, prúd a teplotu do
stavového riadku hlavného okna aj do zoznamu centrál v Nastaveniach; NanoX/XpressNet klient telemetriu explicitne
nepodporuje a vracia `null` hodnoty. Súčasne bol hotfixnutý nízkoúrovňový z21 polling: telemetria už beží cez správny
opcode `LAN_SYSTEMSTATE_GETDATA (0x85)` a registráciu `LAN_SET_BROADCASTFLAGS (0x00000101)` na existujúcom zdieľanom UDP
sockete.
**Dôvod:** Aplikácia doteraz vôbec nezobrazovala elektrické veličiny centrály. Počas implementácie sa odhalili dve
kritické pasce: (1) status bar položky sa nevešali na živý klient po neskoršom pripojení centrály, takže UI ostávalo
„hluché“, a (2) pôvodný dopytovací paket pre z21 používal nesprávny opcode `0x40`, ktorý centrála odmietala rámcom
`LAN_X_UNKNOWN_COMMAND (61 82 E3)`.
**Riešenie:**

1. Vytvorené rozhranie `IDccTelemetry` (`INotifyPropertyChanged`) s vlastnosťami `IsTelemetrySupported`, `MainVoltage`,
   `ProgVoltage`, `TrackCurrent`, `CentralTemperature`.
2. `Z21Client` implementuje `IDccTelemetry`; po úspešnom connecte spúšťa telemetrický subsystém na EXISTUJÚCOM
   `_sendUdp` sokete (bez druhého paralelného socketu). Pri štarte odošle `LAN_SET_BROADCASTFLAGS(0x00000101)` a každé 2
   s polling `LAN_SYSTEMSTATE_GETDATA` (`04 00 85 00`). Odpoveď `LAN_SYSTEMSTATE_DATACHANGED` (`14 00 84 00`) sa parsuje
   v zdieľanom `MainReceiveLoopAsync`.
3. Parsovanie z21 upravené podľa oficiálnej Z21 LAN špecifikácie v1.13: `FilteredMainCurrent` → `TrackCurrent`,
   `Temperature` → `CentralTemperature`, `SupplyVoltage` → `ProgVoltage` (proxy), `VCCVoltage` → `MainVoltage`.
4. `SerialDccClient` implementuje `IDccTelemetry` ako nepodporovanú funkcionalitu (`IsTelemetrySupported = false`,
   všetky hodnoty `null`), aby UI mohlo bindovať jednotne bez špeciálnych vetiev.
5. `StatusBarCentralItem` prepracovaný na `ObservableObject` + `IDisposable`; položka sa cez
   `Bind(DccConnectionService, resolver)` prihlási na globálny `ConnectionStateChanged`, pri každom `Connected` si znova
   vytiahne živý klient a attachne `IDccTelemetry`. Tým sa odstránil problém, že položka vznikla ešte pred connectom a
   telemetria ostala navždy `null`.
6. `MainWindowViewModel.RefreshStatusBarCentrals()` doplnený o resolver živého `IDccTelemetry` klienta pre multi-central
   aj legacy single-central režim; `StatusBarViewModel.UpdateCentrals()` teraz staré položky korektne dispose-ne a nové
   naviaže na connection service.
7. `StatusBarView.axaml` rozšírený o samostatný `TextBlock` pre telemetriu (`17.8 V • 0.01 A`) a po dokončení
   diagnostiky bol obnovený filter `IsVisible="{Binding HasTelemetryText}"`.
8. `ConfiguredDccCentralItem` + `SettingsViewModel` dostali rovnaké delegované telemetrické properties a attachovanie
   živého klienta, aby boli hodnoty dostupné aj v zozname centrály v okne Nastavenia.
9. Po úspešnom oživení telemetrie boli odstránené všetky dočasné diagnostické výpisy (`Debug.WriteLine`, placeholder
   „telemetria: čakám…“, testovacie natvrdo vložené UI hodnoty) a vyčistené zvyšky implementácie (nepoužívané
   `_lastHost` / `_lastPort`, zastarané komentáre o jednosmernom `_sendUdp`).
   **Výsledok:** Po pripojení z21 sa v stavovom riadku hlavného okna aj v Nastaveniach zobrazujú živé údaje typu
   `17.8 V • 0.01 A`. NanoX ostáva korektne bez telemetrie. Implementácia je bez paralelného socketu, naviazaná na
   existujúcu connection infraštruktúru a produkčný build prešiel s 0 warningmi a 0 error-mi.

## 2026-05-30
==============
**Oblasť:** `ViewModels/Settings/SettingsViewModel.cs`, `ViewModels/Settings/DccCommunicationTestHandler.cs`,
`Views/Settings/SettingsWindow.axaml`, `Views/Settings/SettingsWindow.axaml.cs`, `Services/Dcc/DccConnectionService.cs`
**Zmena:** Oprava výberu centrál v okne Nastavenia a presmerovanie testovania komunikácie striktne na práve označenú
centrálu. Žlté z21 varovanie sa teraz zobrazuje iba pre reálne vybraný z21 profil, zoznam centrál zostáva klikateľný aj
počas komunikácie a tlačidlo testu už neberie „prvú centrálu naslepo“.
**Dôvod:** Po pripojení centrál sa výber v zozname správal zamrznuto a test CV1 často skončil na nesprávnom zariadení (
typicky na prvej / primárnej centrále). Žlté varovanie pre z21 nebolo striktne viazané na aktuálny výber v zozname.
**Riešenie:**

1. `SettingsWindow.axaml.cs` – odstránený chybný listener na bubbled `SelectionChanged` z vnoreného `ListBox`/
   `TabControl`; prepnuté na sledovanie `SelectedIndex` tabu cez observable, aby sa pri klikaní do zoznamu centrál
   neresetoval výber späť na prvú položku.
2. `SettingsViewModel.SelectedConfiguredCentral` – setter teraz okamžite prepočítava `IsZ21Selected`, aktualizuje
   `TestHandler.SelectedCentralProfileId` a tým prepína celý testovací panel na dáta z práve zvoleného riadku.
3. `DccCommunicationTestHandler` + `DccConnectionService` – pridané profile-id routovanie testu v multi-central režime (
   `TryGetConnectedClient(profileId, out client)`), takže `ReadCvAsync(1, ...)` ide presne do označenej centrály.
4. `SettingsWindow.axaml` – zoznam centrál ostáva plne aktívny; žlté varovanie je viazané na `IsZ21Selected`, nie na
   globálny stav inej pripojenej centrály.
   **Výsledok:** Používateľ môže aj počas pripojených centrál slobodne prepínať medzi riadkami v Nastaveniach. Test CV1
   sa vykoná vždy na modro označenej centrále a z21 upozornenie okamžite zmizne pri prechode na iný typ centrály.

## 2026-05-30
==============
**Oblasť:** `ViewModels/MainWindowViewModel.cs`, `Services/Dcc/DccConnectionService.cs`,
`Services/Dcc/PerCentralConnection.cs`
**Zmena:** Obnovená správna politika autoconnectu – po štarte aplikácie sa centrály už automaticky nepripájajú vôbec.
AutoConnect slúži výlučne ako auto-reconnect po strate spojenia z predtým úspešne pripojeného stavu; po neúspešnom prvom
connecte sa monitor/reconnect slučka nespúšťa.
**Dôvod:** Pri štarte sa prvá centrála pripájala automaticky, čo bolo neakceptovateľné. Zároveň bolo potrebné zachovať
pôvodné správanie: až po manuálnom pripojení a následnej strate spojenia sa môže na pozadí spustiť auto-reconnect, ktorý
sa po limite ukončí a nechá centrálu odpojenú.
**Riešenie:**

1. `MainWindowViewModel` – odstránené štartovacie auto-pripájanie centrál.
2. `PerCentralConnection.ConnectAsync` – monitor/autoreconnect sa štartuje iba po úspešnom manuálnom connecte; pri
   neúspechu sa monitor explicitne zastaví.
3. `DccConnectionService.ConnectAsync` – rovnaké pravidlo aj pre legacy/single-central cestu; po zlyhaní prvého connectu
   sa nevyvoláva reconnect slučka.
   **Výsledok:** Po štarte aplikácie sú všetky centrály odpojené. Po kliknutí na „Pripojiť“ sa pripoja manuálne a až
   následná strata spojenia môže spustiť auto-reconnect podľa nastavenia profilu.

## 2026-05-30
==============
**Oblasť:** `ViewModels/Settings/DccCommunicationTestHandler.cs`, `ViewModels/Settings/SettingsViewModel.cs`,
`Views/Settings/SettingsWindow.axaml`, `Views/Settings/SettingsWindow.axaml.cs`
**Zmena:** Izolácia výsledkov testovania komunikácie po jednotlivých centrálach + reset panelu pri zatvorení okna
Nastavenia. Každá centrála má vlastný posledný výsledok testu; prepnutie výberu v zozname okamžite prepne aj obsah
testovacieho panela. Pri zatvorení okna sa všetky testovacie hlášky vynulujú.
**Dôvod:** Chybové/úspešné správy z testovania boli globálne pre celé okno, takže pri prepnutí z jednej centrály na
druhú sa zobrazoval cudzí výsledok testu. Po opätovnom otvorení Nastavení zostával v paneli starý error/success stav.
**Riešenie:**

1. `DccCommunicationTestHandler` – pridaná cache posledných výsledkov per `SelectedCentralProfileId`; pri zmene výberu
   sa aktuálny stav uloží a pre novú centrálu sa obnoví jej vlastný výsledok alebo prázdny panel.
2. `SettingsViewModel.ResetCommunicationTestPanels()` – centralizované vynulovanie legacy `ConnectionTestResult` aj
   handler stavu cez `ClearAllTestResults()`.
3. `SettingsWindow.axaml.cs` – reset testovacieho panela naviazaný aj na zatvorenie okna cez `Closing`, nie iba na
   Save/Cancel.
   **Výsledok:** Panel „Testovanie komunikácie“ zobrazuje iba stav vybranej centrály. Po zatvorení a ďalšom otvorení
   Nastavení je panel čistý bez starých chýb a úspechov.

## 2026-05-30
==============
**Oblasť:** `ViewModels/Settings/DccCommunicationTestHandler.cs`, `Views/Settings/SettingsWindow.axaml`
**Zmena:** Zjednodušené používateľské chybové hlášky pri teste komunikácie a odstránené natvrdo zapísané názvy
konkrétnych modelov z textov. Výnimky už neukazujú interné technické detaily (kódy rámcov, názvy exception typov) a
názov centrály sa do správ vkladá dynamicky podľa práve testovaného profilu.
**Dôvod:** Pôvodné chyby boli príliš technické a pevne viazané na konkrétne modely typu „z21“, hoci používateľ mohol
testovať inú centrálu. V UI bolo potrebné zachovať stručné, zrozumiteľné hlášky a pri menších monitoroch zapnúť
zalamovanie.
**Riešenie:**

1. `DccCommunicationTestHandler.ExecuteTestAsync` – catch vetvy pre `TimeoutException`, `SocketException`,
   `UnauthorizedAccessException`, `NotSupportedException`, `InvalidOperationException`, `OperationCanceledException` a
   generic `Exception` boli prepísané na krátke používateľské správy bez interných detailov.
2. `BuildTimeoutMessage(...)` + `ResolveCentralDisplayName(...)` – názov testovanej centrály sa skladá dynamicky z
   aktuálneho profilu/typu; texty správ ostali významovo zachované, len bez natvrdo napísaných modelov.
3. `SettingsWindow.axaml` – červený hint text v testovacom paneli má zapnuté `TextWrapping="Wrap"` a obmedzenú šírku,
   aby sa správy korektne zalamovali.
   **Výsledok:** Používateľ vidí kratšie a zrozumiteľnejšie chybové hlášky, ktoré používajú správny názov aktuálne
   testovanej centrály. UI panel testovania zostáva čitateľný aj na menších obrazovkách.

## 2026-05-30
==============
**Oblasť:** `ViewModels/Settings/SettingsViewModel.cs`, `ViewModels/Settings/DccCommunicationTestHandler.cs`,
`ViewModels/Settings/ConfiguredDccCentralItem.cs`, `Views/Settings/SettingsWindow.axaml`
**Zmena:** Oprava zamŕzania stavu komunikácie/autoconnectu výhradne pri otvorenom okne Nastavenia. `SettingsViewModel` a
`DccCommunicationTestHandler` teraz prijímajú stavové zmeny z `DccConnectionService` v reálnom čase aj počas otvoreného
okna; tlačidlo „Test komunikácie“ a hint „DCC centrála musí byť pripojená“ sa okamžite prepnú po úspešnom
auto-reconnecte. Zároveň bol do zoznamu centrál pridaný vizuálny stavový indikátor (bodka) pre `Connected` /
`Reconnecting` / `Disconnected`.
**Dôvod:** Po výpadku a následnom auto-reconnecte na pozadí sa stav v Settings UI neaktualizoval – Test tlačidlo ostalo
zablokované a UI ostalo v stave „automaticky pripájam…“, hoci `DccConnectionService` už centrálou úspešne prešiel na
`Connected`. Problém sa prejavoval len vtedy, keď bolo okno Nastavenia otvorené.
**Riešenie:**

1. `SettingsViewModel` sa pri konštrukcii prihlási na `IDccConnectionService.IsConnectedChanged` a (ak ide o konkrétny
   `DccConnectionService`) aj na `ConnectionStateChanged`. Všetky reakcie sú odoslané na UI thread cez zachytený
   `SynchronizationContext.Post`, aby sa neblokovali background vlákna (keepalive/auto-reconnect).
2. `DccCommunicationTestHandler` bol upravený tak, aby pri udalostiach spojenia robil refresh odvodených vlastností (
   `IsConnected`, `SupportsProgrammingTest`, `IsTestButtonEnabled`, hinty/tooltipy) cez UI `SynchronizationContext` a
   aby v multi-central režime reagoval aj na detailné `ConnectionStateChanged` (nie iba na agregované
   `IsConnectedChanged`).
3. `ConfiguredDccCentralItem` rozšírený o stavové property `IsConnected`/`IsReconnecting` + odvodené `StateDotColor`/
   `StateTooltip` a metódu `UpdateConnectionState(...)`. `SettingsWindow.axaml` zobrazuje tieto stavy ako farebnú bodku
   v zozname centrál (zelená/oranžová/červená).
   **Výsledok:** Po úspešnom auto-reconnecte sa Settings UI okamžite prepne na „pripojené“ – Test tlačidlo sa odomkne (
   ak je podporované), varovný text o nepripojenej centrále zmizne a zoznam centrál zobrazuje aktuálny stav pre každú
   centrálu (vrátane oranžového „automaticky pripájam…“). Build overený (`dotnet build TrackFlow.csproj -c Debug`).

## 2026-05-30
==============
**Oblasť:** `Services/Dcc/DccConnectionService.cs`, `Services/Dcc/PerCentralConnection.cs`,
`TrackFlow.Tests/DccConnectionServiceTests.cs`
**Zmena:** Kritický low-level hotfix monitorovania pripojených centrál – detekcia výpadku linky už nie je naviazaná na
`AutoConnect`. Systém teraz sleduje stratu spojenia pre každú úspešne pripojenú centrálu a až následne sa samostatne
rozhodne, či spustiť reconnect.
**Dôvod:** Keď profil nemal povolený autoconnect, monitor sa vôbec nespustil alebo sa po strate linky správal ako „nič
nerob“. Po fyzickom odpojení kábla tak v UI mohol visieť falošný zelený stav „Pripojené“, pretože strata spojenia nebola
doručená do stavového riadku ani do otvorených okien.
**Riešenie:**

1. `PerCentralConnection` – monitor sa po úspešnom connecte spúšťa vždy, nielen pri `Profile.AutoConnect == true`. Vetva
   „connection lost“ vždy vyšle `Disconnected("connection-lost")`; reconnect vetva sa aktivuje len ak je autoconnect
   povolený. Pri `AutoConnect == false` zostane centrála po výpadku v čistom červenom stave bez oranžovej slučky.
2. `DccConnectionService` – rovnaké oddelenie detekcie výpadku a reconnectu aj pre legacy/single-central monitor.
   Monitoring už nepadá len preto, že klient nemá keepalive; pre ne-keepalive klientov sa priebežne polluje
   `Client.IsConnected` a výpadok sa pošle do UI ako `Disconnected("connection-lost")`.
3. `DccConnectionServiceTests` – doplnené regresné testy pre scénar odpojenia linky pri vypnutom autoconnecte (legacy aj
   multi-central), ktoré overujú: `Disconnected` sa odošle vždy, `Reconnecting` sa nespustí a profil sa nedostane do
   `ReconnectingProfileIds`.
   **Výsledok:** Fyzické odpojenie kábla/soketu sa korektne deteguje aj pri profile s vypnutým autoconnectom. UI
   okamžite prejde do červeného „odpojené“ stavu a na manuálne znovupripojenie čaká až na stlačenie tlačidla „Pripojiť“.
   Produkčný build overený; test projekt ostáva mimo tejto zmeny blokovaný staršími nesúvisiacimi compile chybami v
   `SettingsProjectDccProfilesTests`.

## 2026-05-29
==============
**Oblasť:** `ViewModels/Editor/TurnoutPropertiesViewModel.cs`, `ViewModels/Library/LocomotivesWindowViewModel.cs`
**Zmena:** Hotfix predchádzajúcich úprav – (1) `TurnoutPropertiesViewModel.LoadDccSystems` napriek deklarovanej zmene
stále používal legacy `SettingsManager.GetEffective()` (zobrazoval iba jednu centrálu); (2) v editore lokomotívy (
DCC/Dekodér → ComboBox „DCC systém") chýbala prvá položka „Bez pripojenia".
**Dôvod:** Refaktor multi-pripojenia bol v predchádzajúcom kroku označený ako hotový, ale `LoadDccSystems` zostal v
starom stave a v `LocomotivesWindowViewModel.LoadDigitalSystems` „Bez pripojenia" nebolo zaradené do kolekcie. UX bola
nekonzistentná medzi oknami.
**Riešenie:**

1. `TurnoutPropertiesViewModel.LoadDccSystems` skutočne prepísaný na enumeráciu
   `_settingsManager.App.DccCentralProfiles` (poradie + typ + adresa) so „Bez pripojenia" ako prvou položkou.
2. `LocomotivesWindowViewModel.LoadDigitalSystems` pridáva `DccCentralProfileItem(Guid.Empty, "Bez pripojenia")` na prvú
   pozíciu kolekcie. `ResolveDigitalSystemItem` vracia túto položku ako fallback (namiesto null). Pri uložení lokomotívy
   sa `Guid.Empty` mapuje na `AssignedCentralProfileId = null` a `DccSystemName = null` (sentinel sa neukladá do
   modelu).
   **Výsledok:** Vo vlastnostiach výhybky sú teraz viditeľné všetky nakonfigurované DCC centrály (nielen jedna). V
   editore lokomotívy je „Bez pripojenia" prvá položka v ComboBoxe „DCC systém". Build 0 chýb.

---



**Zmena:** ComboBox „Digitálny systém“ vo vlastnostiach výhybky a kontaktného indikátora prerobený na dynamický zoznam
všetkých DCC profilov z globálnych nastavení. Prvou položkou je vždy „Bez pripojenia“ (null profil). Pre indikátor
pôvodne plochý zoznam stringov nahradený `DccSystemItem` s `ItemTemplate`.
**Dôvod:** Po prechode na multi-pripojenie (`AppSettingsData.DccCentralProfiles`) ComboBox vo vlastnostiach výhybky
stále ukazoval iba jednu (legacy) centrálu cez `SettingsManager.GetEffective().DccCentralType`. Vlastnosti indikátora
mali navyše natvrdo iba placeholder „--- Bez pripojenia ---“. Nebolo možné priradiť konkrétny profil ani odpojiť prvok (
„Bez pripojenia“ ako pevná prvá voľba chýbala).
**Riešenie:**

1. `DccSystemItem` rozšírený o `Guid? ProfileId` – jednoznačná identifikácia profilu (dva profily môžu mať rovnaký
   `DccCentralType`).
2. `TurnoutElement` a `BlockIndicator` rozšírené o `Guid? DccCentralProfileId`. `TurnoutElement.DccSystemType` ostáva
   ako legacy fallback pre staré projekty.
3. `TurnoutPropertiesViewModel.LoadDccSystems()` – pridáva pevnú prvú položku „Bez pripojenia“ (`ProfileId = null`,
   `Type = null`), potom enumeruje `_settingsManager.App.DccCentralProfiles`. Display name =
   `{poradie}: {Type} ({host:port | COM})`. Pri otvorení okna sa zoznam vždy načíta čerstvo, takže zmena profilov v
   globálnych nastaveniach sa okamžite premietne. Pre-select: 1) `DccCentralProfileId`, 2) legacy `DccSystemType`, 3)
   „Bez pripojenia“.
4. `TurnoutPropertiesViewModel.OnSave()` ukladá `DccCentralProfileId` aj `DccSystemType` (spätná kompatibilita).
5. `IndicatorPropertiesViewModel` prepracovaný: `DccSystems: ObservableCollection<DccSystemItem>` namiesto
   `ObservableCollection<string>`; nový ctor `(BlockIndicator, SettingsManager?)`; pre-select podľa
   `BlockIndicator.DccCentralProfileId` (fallback na „Bez pripojenia“); save ukladá `ProfileId` do modelu.
6. `IndicatorPropertiesWindow.axaml` – ComboBox dostal `ItemTemplate` s `TextBlock Text="{Binding Name}"`.
7. `BlockPropertiesViewModel` – nová public property `SettingsManager` (nastavuje sa zvonku),
   `BlockPropertiesWindow.OpenIndicatorPropertiesWindow` ju predáva do `IndicatorPropertiesViewModel`.
8. `LayoutEditorView.OnBlockPropertiesRequested` – nastavuje `dialogVm.SettingsManager = _vm?.SettingsManager` pred
   otvorením okna.
   **Výsledok:** Vo vlastnostiach výhybky aj kontaktného indikátora je vždy viditeľná položka „Bez pripojenia“ na prvej
   pozícii, pod ňou kompletný zoznam nakonfigurovaných DCC centrál s ich poradím, typom a adresou (IP:port alebo COM).
   Výber sa korektne zobrazí pri otvorení (vrátane legacy projektov bez `DccCentralProfileId`) a pri uložení zapíše
   `DccCentralProfileId` (alebo `null`) do modelu. Pridanie/odstránenie centrály v globálnych nastaveniach sa premietne
   pri ďalšom otvorení okna vlastností. Build 0 chýb; `TurnoutPropertiesViewModelTests` 31/31 OK.

---



**Zmena:** Plochý `ComboBox` „Typ centrály" nahradený hierarchickým rozbaľovacím výberom – skupiny výrobcov (Maerklin,
Lenz, Roco, Fleischmann, ESU, ...) → vnorené modely centrál. Skupiny sú rozbaľovateľné šípkou, modely klikateľné.
Neimplementované modely sú vizuálne zosivené (Opacity 0.5), výrobcovia majú SemiBold písmo.
**Dôvod:** Pôvodný plochý zoznam centrál bol dlhý a vizuálne neprehľadný – používateľ sa v ňom nevedel rýchlo
orientovať. Bolo potrebné zaviesť logické skupiny podľa výrobcu.
**Riešenie:** Vytvorený single-type model `DccCentralTreeNode` (skupiny majú `Type=null` + naplnené `Children`, listy
`Type!=null` + prázdne `Children`). Vo VM kolekcia `TreeNodes`, `SelectedNode`, `IsTypeDropDownOpen` a metóda
`SelectCentralItemFromTree`. V AXAML použitý `ToggleButton` + **in-window overlay** (Border s
`IsVisible={Binding IsTypeDropDownOpen}` v rovnakom vizuálnom strome ako formulár) – NIE `Popup`, pretože ten beží v
separátnom `PopupRoot` a compiled bindings v ňom tichom zlyhávajú (prázdny render bez chyby). `TreeView` s jediným
`TreeDataTemplate` (`x:DataType=DccCentralTreeNode`, `ItemsSource={Binding Children}`). Okno má `SizeToContent="Height"`
aby sa dynamicky zväčšilo po otvorení stromu. Kliknutie na uzol obsluhuje code-behind handler, ktorý prechádza vizuálny
strom hore a hľadá `DataContext` typu `DccCentralTreeNode`.
**Výsledok:** Hierarchický výber funguje, strom sa korektne renderuje, výber listu zatvorí menu a aktualizuje pole
`SelectedNode`. Build 0 chýb.

---


## 2026-05-29
==============
**Oblasť:** `Views/Settings/DccCentralEditWindow.axaml`
**Zmena:** Oprava prázdneho renderu hierarchického `TreeView` – nahradenie `TreeView.ItemContainerTheme` +
`ControlTheme` za `TreeView.Styles` + `Style Selector="TreeViewItem"`.
**Dôvod:** `ControlTheme` bez `BasedOn` úplne nahrádza default vizuálnu šablónu `TreeViewItem`. Bez šablóny sa žiadne
uzly nevykreslia – strom vyzeral prázdny napriek tomu, že `TreeNodes` boli korektne naplnené dátami.
**Riešenie:** Prechod na `Style Selector="TreeViewItem"` – settery (`IsExpanded=True`, `Padding`) sa aplikujú **nad**
predvolený theme namiesto jeho nahradenia. Default vizuálna šablóna `TreeViewItem` ostáva zachovaná.
**Výsledok:** Strom sa renderuje korektne – skupiny výrobcov sú viditeľné s rozbaľovacou šípkou, modely vnorené pod
nimi. Build 0 chýb.

---


## 2026-05-28
==============
**Oblasť:** `Services/Dcc/SerialDccClient.cs` (premenovaný z `NanoXS88Client.cs`), `Services/Dcc/DccClientFactory.cs`,
`Services/Dcc/DccConnectionService.cs`, `ViewModels/Library/LocomotivesWindowViewModel.cs`,
`TrackFlow.Tests/SerialDccClientTests.cs`, `TrackFlow.Tests/DccCvWritePacketTests.cs`,
`TrackFlow.Tests/DccClientFactoryTests.cs`
**Zmena:** Refaktorizácia – premenovanie triedy a súboru seriového DCC klienta na generické, hardvérovo-neutrálne názvy.
`NanoXS88Client.cs` → `SerialDccClient.cs`, trieda `NanoXs88Client` → `SerialDccClient`, interný interface
`INanoXs88SerialPort` → `ISerialDccPort`.
**Dôvod:** Trieda zapuzdruje generický XpressNet protokol cez ľubovoľný COM port a nesie názov konkrétneho komerčného
produktu (Paco NanoX-S88). Na sériovom porte môže byť pripojená akákoľvek XpressNet kompatibilná centrála. Pevne
zadrôtovaný produktový názov na úrovni triedy je neprípustný – porušuje pravidlo generickej vrstvy pre COM komunikáciu.
**Riešenie:** Iba premenovanie identifikátorov (súbor, trieda, interface) bez akejkoľvek zmeny funkčnej logiky. Všetky
referencie v produkcii aj testoch aktualizované. Build overený – 0 chýb.
**Výsledok:** Vrstva sériového rozhrania je hardvérovo-neutrálna. Kód je pripravený na pripojenie akéhokoľvek
sériového (COM) DCC zariadenia bez úpravy názvov tried.

---


## 2026-05-28
==============
**Oblasť:** `Services/Dcc/DccConnectionService.cs`, `Services/Dcc/PerCentralConnection.cs`,
`ViewModels/MainWindowViewModel.cs`, `ViewModels/StatusBarViewModel.cs`, `ViewModels/StatusBarCentralItem.cs`
**Zmena:** Oprava logiky tlačidla Pripojiť/Odpojiť pri zmiešanom stave centrál (jedna pripojená, druhá odpojená po
timeoutu auto-reconnectu). Pridaná metóda `ConnectMissingAsync` – pripájá iba chýbajúce centrály bez odpojenia
existujúcich. `Ribbon.IsConnected = true` iba keď sú VŠETKY profily pripojené; pri akomkoľvek výpadku tlačidlo sa prepne
na „Pripojiť" a umožní pripojiť chýbajúcu centrálu. Súčasne oranžový LED stav v stavovom riadku pri auto-reconnecte (bol
iba červený).
**Dôvod:** Pri zmiešanom stave (napr. z21 pripojená, NanoX po timeoutu odpojená) tlačidlo ukazovalo „Odpojiť". Stlačením
sa odpojila aj fungujúca z21, nie pripojila chýbajúca NanoX. Neexistoval spôsob ako pripojiť jednu centrálu bez
odpojenia druhej.
**Riešenie:**

1. `DccConnectionService.ConnectMissingAsync(allProfiles)` – nová metóda: zistí, ktoré profily nie sú v
   `ConnectedProfileIds`, pre každý timed-out profil zastaví starý `PerCentralConnection` (a jeho expirovaný monitor) a
   vytvorí nový s čerstvým 60-sekundovým reconnect oknom. Už-pripojené centrály zostanú nedotknuté.
2. `MainWindowViewModel.AreAllProfilesConnected()` – helper: `true` iba keď každý nakonfigurovaný profil má aktívne
   spojenie (pre legacy cestu bez profilov vracia `Dcc.IsConnected`).
3. `MainWindowViewModel.ConnectCoreAsync()` – pre multi-central path nahradené `ConnectAllAsync` →
   `ConnectMissingAsync`; stlačenie „Pripojiť" pripojí iba chýbajúce.
4. `OnDccConnectionStateChanged` – `Ribbon.IsConnected` nastavené cez `AreAllProfilesConnected()` namiesto
   `anyConnected`; pri `Disconnected` a `Reconnecting` vždy `false` (aspoň jedna chýba).
   **Výsledok:** Stlačenie „Pripojiť" po timeoutu jednej centrály pripojí iba tú chýbajúcu; ostatné centrály ostanú
   pripojené. Tlačidlo sa prepne na „Odpojiť" až keď sú VŠETKY centrály znovu pripojené.

---


## 2026-05-28
==============
**Oblasť:** `Services/Dcc/DccConnectionService.cs`, `Services/Dcc/PerCentralConnection.cs`,
`ViewModels/MainWindowViewModel.cs`, `ViewModels/StatusBarViewModel.cs`, `ViewModels/StatusBarCentralItem.cs`
**Zmena:** Zavedenie tretieho stavu stavového riadku pre DCC centrály – „automaticky pripájam…" (oranžová LED). Predtým
sa po výpadku zobrazovalo iba červené „odpojená" bez akejkoľvek indikácie prebiehajúceho auto-reconnectu.
**Dôvod:** Po fyzickom výpadku centrály sa v stavovom riadku zobrazovalo len „X odpojená" (červená LED), ale žiadna
správa o automatickom pripájaní. Auto-reconnect bežal na pozadí, ale používateľ to nevedel. Navyše `Reconnecting` event
sa odosielal až v nasledujúcej iterácii monitora (2,5 s meškanie oproti záložnému súboru).
**Riešenie:**

1. `StatusBarCentralItem.IsReconnecting` – nová property; `LedColor` = oranžová `#FF9800`; `StatusText` = „{Názov} –
   automaticky pripájam…".
2. `DccConnectionService._reconnectingProfileIds` – `HashSet<Guid>` sledujúci profily v stave reconnect; aktualizovaný v
   `OnPerCentralStateChanged` pri eventoch `Reconnecting` / `Connected` / `Disconnected` / timeout; čistený v
   `DisconnectAll`.
3. `DccConnectionService.ReconnectingProfileIds` – nová verejná property pre čítanie stavu.
4. `StatusBarViewModel.UpdateCentrals()` – rozšírené o voliteľný parameter `reconnectingProfileIds`; `IsReconnecting` sa
   nastaví iba pre odpojené profily s aktívnym reconnectom.
5. `MainWindowViewModel.RefreshStatusBarCentrals()` – predáva `Dcc.ReconnectingProfileIds` do `UpdateCentrals`.
6. `PerCentralConnection.MonitorLoopAsync` sekcia 3 – `Reconnecting` event sa odosiela IHNEĎ po detegovaní výpadku cez
   keepalive (nie až v ďalšej iterácii); `reconnectStartNotified = true` pred odoslaním, aby sekcia 1 neopakovala
   notifikáciu.
   **Výsledok:** Stavový riadok zobrazuje oranžovú LED a text „– automaticky pripájam…" počas celého 60-sekundového
   reconnect okna. Po timeout LED zostane červená „odpojená". Správanie zodpovedá záložnému súboru, kde auto-reconnect
   bol viditeľný pre používateľa.

---


## 2026-05-28
==============
**Oblasť:** `Views/Settings/SettingsWindow.axaml`
**Zmena:** Kompletná vizuálna prestavba záložky „DCC pripojenie" v okne Nastavenia – spodný panel „Konfigurácia
programovacej koľaje" bol prepracovaný na dva symetrické boxy (ľavý: Spôsob programovania, pravý: Testovanie
komunikácie), kompatibilné so štýlom horného panela. Pravý box dostal sivé záhlavie s nápisom „Testovanie komunikácie",
tlačidlo testu je vertikálne centrované a stavová oblasť (červený hint + výsledok testu) má vyhradenú zónu pod
tlačidlom.
**Dôvod:** Predchádzajúci stav mal sekciu testovania vizuálne odfláknutú – bez záhlavia, bez rámca, nespojitú so zvyškom
dizajnu. Boxy nemali rovnakú výšku ani jednotný štýl, červený text bol natlačený na spodný okraj.
**Riešenie:** Oba boxy sú zabalené do identickej vnornej
`Border (BorderBrush="#C8C8C8", CornerRadius="4", Background="#F8FBFF")` s rovnakým sivým záhlavím
`(Background="#E8E8E8", Padding="8,4", FontWeight="Bold", FontSize="13")`. Sú umiestnené v spoločnom `Grid` s
`ColumnDefinitions="*,12,*"`, čo zaručuje rovnakú výšku oboch. Vnútro pravého boxu delí
`Grid RowDefinitions="*,Auto,*"` – tlačidlo v strede, stavová oblasť s `Margin="0,8,0,0"` dole.
**Výsledok:** Záložka „DCC pripojenie" má vizuálne jednotný a symetrický layout. Oba spodné boxy majú identický štýl,
rovnakú výšku a záhlavie. Testy 679/679.

---


## 2026-05-28
==============
**Oblasť:** `Views/Settings/SettingsWindow.axaml`, `Views/Settings/DccCentralEditWindow.axaml`,
`ViewModels/Settings/DccCentralEditViewModel.cs`, `ViewModels/Settings/ConfiguredDccCentralItem.cs`,
`ViewModels/Settings/SettingsViewModel.cs`, `Models/DccCentralProfile.cs`, `Models/StartupFunctionBehavior.cs`,
`Models/AppSettingsData.cs`
**Zmena:** Kompletná prestavba záložky „DCC centrála" → „DCC pripojenie" v okne Nastavenia. Statický box pripojenia a
sekcia „Pokročilé" (timeout) nahradené správcom profilov centrál s ListBoxom, CRUD tlačidlami a modálnym dialógom.
Záložka obsahuje aj checkbox „Použiť pre tento projekt" a „Automatické pripojenie". Dolný panel „Konfigurácia
programovacej koľaje" je zamknutý, kým nie je vybraná žiadna centrála (`IsEnabled="{Binding HasSelectedCentral}"`). V
editačnom dialógu: Port a Baudrate sú skryté z UI (hardcódované na pozadí), pridaný ComboBox „Správanie po štarte" (4
možnosti). COM porty sa automaticky obnovia pri otvorení dropdownu.
**Dôvod:** Pôvodná záložka podporovala len jednu statickú konfiguráciu centrály bez možnosti správy viacerých profilov.
Chýbala možnosť prepínania medzi centrálami, ukladania profilov a viazania centrály na konkrétny projekt.
**Riešenie:**

1. `DccCentralProfile` – nový model s `Id`, `Type`, `Host`, `Port`, `SerialPort`, `BaudRate`, `StartupBehavior`.
2. `StartupFunctionBehavior` – nový enum (4 hodnoty: SendAllFunctions, SendActivatedFunctions, KeepPreviousState,
   AssumeOffState).
3. `AppSettingsData` rozšírené o `List<DccCentralProfile> DccCentralProfiles` a `Guid? SelectedDccCentralProfileId`.
4. `ConfiguredDccCentralItem` – ObservableObject wrapper generujúci `DisplayText` (napr. „1: Roco/Fleischmann z21
   192.168.0.111").
5. `DccCentralEditViewModel` – všetky vlastnosti explicitné (bez `[ObservableProperty]`) kvôli spoľahlivosti XAML
   analyzátora; `IsComPortDropDownOpen` setter volá `RefreshPorts()` pri otvorení dropdownu.
6. `SettingsViewModel` – pridané `ConfiguredCentrals`, `SelectedConfiguredCentral`, `HasSelectedCentral`,
   `AddCentralCommand`, `EditCentralCommand`, `DeleteCentralCommand`; dialog factory pattern pre testovateľnosť bez
   reálneho okna; `Load()`/`Save()` prečítajú a zapíšu profily do `AppSettingsData`.
7. ListBox riadky kompaktné: `MinHeight="24"`, `Height="26"`, `Padding="4,0"`, `VerticalContentAlignment="Center"`.
   **Výsledok:** Záložka podporuje správu ľubovoľného počtu DCC centrál s perzistentným ukladaním profilov. Kompaktný
   ListBox, modálny dialóg pre pridanie/úpravu, automatická obnova COM portov. Testy 679/679.

---


## 2026-05-28
==============
**Oblasť:** `Views/Library/LocomotivesWindow.axaml`, `ViewModels/Library/LocomotivesWindowViewModel.cs`,
`Models/LocoRecord.cs`, `Services/Dcc/IDccProgrammingClient.cs`, `Services/Dcc/Z21Client.cs`,
`Services/Dcc/NanoXS88Client.cs`, `Services/Dcc/DccAddressCodec.cs`,
`TrackFlow.Tests/LocomotiveAddressProgrammingTests.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`,
`TrackFlow.Tests/DccCvWritePacketTests.cs`
**Zmena:** Dokončené rozšírenie DCC editora lokomotívy o čítanie a zápis adresy dekodéra, bezpečné povoľovanie
globálnych DCC akcií len pri reálne dostupnom programovaní a o automatický reset celej DCC konfigurácie pri vypnutí
podpory DCC programovania na lokomotíve. Súčasne bol viackrát upravený layout boxu `DCC / Dekodér`, aby pole adresy,
helper text aj dvojica tlačidlí sedeli v boxe bez orezávania textu.
**Dôvod:** V editore chýbalo priame čítanie a zápis DCC adresy z/na dekodér cez service-track programovanie. Spodné
globálne tlačidlá zostávali aktívne aj v stavoch, keď lokomotíva DCC programovanie nemala povolené alebo nebola
pripojená centrála, čo predstavovalo bezpečnostný a UX problém. Pri vypnutí checkboxu
`Tento dekodér podporuje DCC programovanie` navyše ostávali v modeli staré CV hodnoty a CV29 stav, ktoré už neodrážali
skutočný stav profilu. Popritom bol horný box `DCC / Dekodér` vizuálne nestabilný – tlačidlá sa rezali, neutekali
správne na spodok boxu alebo nevyužívali dostupnú šírku.
**Riešenie:**

1. Rozhranie `IDccProgrammingClient` bolo rozšírené o `WriteCvAsync(...)` a jeho implementácia bola doplnená do klientov
   `Z21Client` aj `NanoXS88Client` vrátane verifikácie zápisu čítaním späť.
2. Do editora lokomotívy boli pridané tlačidlá `ReadAddressButton` a `WriteAddressButton` vedľa DCC adresy s kompletnou
   asynchrónnou logikou vo `LocomotivesWindowViewModel`.
3. Čítanie adresy používa `CV29` na rozlíšenie short/long address režimu a následne číta buď `CV1`, alebo dvojicu
   `CV17` + `CV18`, pričom dlhá adresa sa dekóduje cez `DccAddressCodec` podľa NMRA formátu.
4. Zápis adresy zapisuje buď `CV1` a vynuluje long-address bit v `CV29`, alebo pri dlhej adrese zakóduje `CV17` + `CV18`
   a zapne long-address bit v `CV29`.
5. Source-generator commandy pre adresné operácie boli nahradené explicitnými `IAsyncRelayCommand` vlastnosťami (
   `ReadAddressCommand`, `WriteAddressCommand`), aby XAML analyzátor spoľahlivo rozpoznal bindingy a prestal hlásiť
   `Cannot resolve symbol 'ReadAddressCommand'`.
6. Box `DCC / Dekodér` bol prepracovaný na stabilný grid layout: adresa ostala na samostatnom riadku s helper textom
   formátu adresy, tlačidlá boli presunuté do spodnej časti boxu, roztiahnuté na plnú šírku cez `UniformGrid` a oddelené
   vizuálnym separátorom.
7. Spodná globálna komunikačná lišta (`ReadCvButton`, `WriteCvButton`, `FactoryCvButton`) už nie je aktívna len podľa
   checkboxu lokomotívy – bola zavedená kombinovaná vlastnosť `IsGlobalDccProgrammingAvailable`, ktorá vyžaduje súčasne
   `SelectedLocomotive.IsDccProgrammingEnabled == true` aj `_dccConnectionService.IsConnected == true`.
8. `LocoRecord` už defaultne štartuje s `IsDccProgrammingEnabled = false` a pri prepnutí tejto voľby na `false` vykoná
   tvrdý reset celej DCC konfigurácie (`MinSpeedCv`, `MidSpeedCv`, `MaxSpeedCv`, `AccelerationCv`, `BrakingCv`,
   `Cv29Value`, `IsInvertDirectionEnabled`, `IsAnalogOperationEnabled`, `IsBemfEnabled`,
   `IsDisableDynamicsForMeasurement`, `BrakeCorrection`).
9. `LocomotivesWindowViewModel.Add()` už pri novej lokomotíve explicitne nezapína DCC programovanie; nový draft
   lokomotívy rešpektuje default modelu.
   **Výsledok:** Editor lokomotív teraz umožňuje bezpečne čítať a zapisovať DCC adresu dekodéra priamo z UI, vrátane
   short/long address režimu. DCC tlačidlá sa aktivujú iba vtedy, keď je lokomotíva označená ako programovateľná a
   zároveň je pripojená kompatibilná centrála. Vypnutie podpory DCC programovania spoľahlivo vynuluje celý DCC stav
   lokomotívy a nový profil lokomotívy štartuje s DCC programovaním vypnutým. Layout boxu `DCC / Dekodér` je vizuálne
   stabilný, tlačidlá sa nerežú a sedí helper text formátu adresy. Overené cielenými testami (
   `LocomotiveSpeedEditorMarkupTests`, `LocomotiveAddressProgrammingTests`, `DccCvWritePacketTests`, protokolové testy
   NanoX/z21) aj buildom riešenia.

---


## 2026-05-27
==============
**Oblasť:** `Services/Dcc/NanoXS88Client.cs` + `TrackFlow.Tests/NanoXs88ClientTests.cs` + nová dokumentácia
`Services/Dcc/NANOX_S88_PROTOCOL.md`
**Zmena:** Kompletné prepracovanie XpressNet service-mode CV read flow pre Paco NanoX-S88 (Lenz LI100F v2 emulácia).
**Dôvod:** Prvý test vracal magic value `3` namiesto skutočnej hodnoty CV (interpretoval `61 00` ako finál); druhý test
okamžite padol s `InvalidOperationException("Neočakávaná odpoveď NanoX-S88: 61 81 E0")` lebo centrála zostala uviaznutá
v service mode. Centrála po `61 02` (busy) nikdy autonómne neposielala dátový rámec – chýbal Service Mode Results
Request (`21 10 31`).
**Riešenie:**

1. Pridaný `CreateServiceModeResultsRequestPacket()` (SMRR `21 10 31`) – posiela sa po každom `61 02` aj ako retry pri
   `61 12` (programmer busy, max 15 pokusov).
2. Pridaný fallback handler `61 82` (instruction not supported) → vypne SMRR a prejde na pasívne čakanie spontánneho
   `63 1x`.
3. Pridaný handler `61 81` (Command Station Busy) v štartovacom cykle (retry s drénom + 500 ms pauza) aj v hlavnej
   slučke (graceful `TimeoutException`).
4. Odstránená chybná vetva `61 00 → return (byte)3`; `61 00` je len informačný status "Service Mode entered".
5. Skutočný úspech je až `63 10 CV V XOR` (Paged Mode v2) alebo `63 14 CV V XOR` (Direct Mode) – **5-bajtové** rámce
   podľa Lenz XpressNet v3.6 spec. Hodnota CV je `response[3]`, nie `response[2]`.
6. `ReadRawResponseAsync` číta pre `63 10`/`63 14` 5 bajtov (predtým 4 → checksum mismatch).
7. Pridaný drén `61 01` broadcast spamu po Track Power ON (`DiscardInBuffer` + 200 ms + `DiscardInBuffer`).
8. `finally` vylepšený: `DiscardInBuffer` pred + po exit pakete, predĺžené pauzy (250 + 300 ms) → zaručuje čistý stav
   pre ďalší test.
9. `InterByteReadTimeoutMs: 500 → 1000` ms (NanoX občas posiela 3. bajt s oneskorením).
10. `DefaultPassiveReadTimeoutMs: 4_000 → 8_000` ms (SMRR retry cyklus vyžaduje viac času).
11. `ValidateChecksum` toleruje rámce kratšie ako 3 bajty (skip s info logom).
12. Testy prepísané/doplnené na nový kontrakt (16 testov vrátane 3 nových: `CreateServiceModeResultsRequestPacket`,
    `WhenProgrammerBusy6112_RetriesSmrr`, `When6182NotSupported_FallsBackToPassiveWaiting`). 4-bajtové data rámce v
    testoch hromadne konvertované na 5-bajtový Lenz formát.
13. Nová dokumentácia `NANOX_S88_PROTOCOL.md` s kompletnou mapou protokolu, chronológiou ladenia, tabuľkou rámcov a
    šťastným scenárom.
    **Výsledok:** Oba testy (prvý aj druhý hneď za sebou) spoľahlivo vracajú skutočnú hodnotu CV (overené na CV1=2). UI
    zobrazuje zelený úspech bez vizuálnej regresie. `61 81` post-test problém vyriešený. Komunikácia s NanoX-S88 je
    stabilná a opakovateľná.

---



## 2026-05-25 (Editor lokomotív – priebežná zmena vzhľadu formulára)
==============
**Oblasť:** `Views\Library\LocomotivesWindow.axaml`, `ViewModels\Library\LocomotiveSpeedEditorViewModel.cs`

**Zmena:** Do changelogu bol doplnený explicitný záznam, že v tejto etape priebežne meníme a vizuálne dolaďujeme vzhľad
formulára `Editor lokomotív`, najmä v oblasti grafov, osí, popisov, podkladových zón a čitateľnosti diagnostických
prvkov.

**Dôvod:** Pri postupnom ladení detailov bolo potrebné mať priamo v changelogu jasne zdokumentované, že aktuálne zásahy
nie sú len izolované technické opravy, ale sú súčasťou širšej úpravy vzhľadu formulára `Editor lokomotív`.

**Riešenie:**

- Doplnený samostatný záznam do changelogu pre vizuálne úpravy formulára `Editor lokomotív`.
- Záznam explicitne pokrýva priebežné zmeny vzhľadu a čitateľnosti UI v tomto formulári.

**Výsledok:**

- Changelog teraz jednoznačne uvádza, že prebieha úprava vzhľadu formulára `Editor lokomotív`.
- Budúce vizuálne zmeny v tejto oblasti majú jasný kontext aj pri neskoršom spätnom dohľadávaní.

## 2026-05-24 (Asymetria smerov – stabilizácia minigrafu, čistejšia os Y a zosúladenie podkladu)
==============
**Oblasť:** `Views\Library\LocomotivesWindow.axaml`, `ViewModels\Library\LocomotiveSpeedEditorViewModel.cs`,
`TrackFlow.Tests\LocomotiveSpeedEditorMarkupTests.cs`, `TrackFlow.Tests\OperationViewModelDoctorDiagnosticsTests.cs`,
`TrackFlow.Tests\OperationViewModelRouteActivationTests.cs`

**Zmena:** Minigraf `Asymetria smerov` bol vizuálne stabilizovaný a zjednotený s novou šírkou grafu. Opravené bolo
percentuálne vyjadrenie asymetrie voči `Vmax`, zafixovaná Y os v rozsahu `0–20 %`, upratané duplicitné číselné popisy
prahov a rozšírený farebný podklad tak, aby presne siahal až po pravý okraj grafu. Súčasne boli opravené dva zastarané
reflexívne test helpery mimo tejto oblasti, aby opäť zodpovedali aktuálnej internej signatúre
`ApplyBoundaryEntryState(...)`.

**Dôvod:**

- Dynamické škálovanie a lokálne percentá na nízkych krokoch robili graf nečitateľným a opticky nadsadzovali odchýlky.
- Na osi Y sa prekrývali hlavné štítky so sekundárnymi popismi prahových pásiem.
- Po rozšírení minigrafu z `208` na `228` px ostali farebné pásma a časť podkladových čiar useknuté na pôvodnej šírke.
- Plná testovacia sada odhalila dva staršie testy, ktoré cez reflexiu volali už zmenenú internú signatúru
  `ApplyBoundaryEntryState(...)`.

**Riešenie:**

- `CalculateDirectionAsymmetryPercent(...)` bol vrátený na výpočet voči `referenceMaxSpeed` (`Vmax`).
- `MechanicalChartAxisMaximum` bol zafixovaný na `20.0`, čím sa minigraf prestal auto-scaleovať.
- Farebné pásma boli zjednotené na semafor:
    - zelená `0–5 %`,
    - oranžová `5–12 %`,
    - červená `12–20 %`.
- Z osi Y boli odstránené duplicitné farebné textové štítky prahov, ale prerušované prahové čiary ostali zachované.
- Formátovanie `FormatMechanicalAxisLabel(...)` bolo upravené tak, aby celé hodnoty osi (`20`, `15`, `10`, `5`) už
  nezobrazovali zbytočné desatinné miesta.
- V `LocomotivesWindow.axaml` boli podkladové `Rectangle` zóny a obrys minigrafu rozšírené z `208` na `228` px a
  horizontálne/prahové vodiace čiary na pravú hranu `260`.
- Vnútorné vertikálne mriežkové čiary minigrafu boli prepočítané na novú šírku (`89`, `146`, `203`).
- `LocomotiveSpeedEditorMarkupTests` boli priebežne aktualizované o nové očakávané axis labely, nové X súradnice krivky
  a regresné kontroly proti návratu starých šírok alebo duplicitných štítkov.
- `OperationViewModelDoctorDiagnosticsTests` a `OperationViewModelRouteActivationTests` boli opravené tak, aby
  reflexívne volanie `ApplyBoundaryEntryState(...)` zodpovedalo aktuálnej signatúre vrátane trailing `params object?[]`
  argumentu.

**Výsledok:**

- Minigraf `Asymetria smerov` má stabilnú a zrozumiteľnú stupnicu `0 / 5 / 10 / 15 / 20 %`.
- Farebný semafor aj podkladové čiary pokrývajú celú aktuálnu šírku grafu bez useknutia.
- Osa Y je čistejšia, bez duplicitných farebných čísel a bez osamelého `5,0`.
- Text `max. asymetria` a krivka používajú konzistentný vzťah k `Vmax`.
- Overená plná testovacia sada:
    - `dotnet test .\TrackFlow.Tests\TrackFlow.Tests.csproj --nologo --verbosity quiet`
    - výsledok: `Passed: 590, Failed: 0`

## 2026-05-20 (Editor rýchlostného profilu – spoločná RAW tabuľka krokov pre oba smery)
==============
**Oblasť:** `Views\Library\LocomotivesWindow.axaml`, `ViewModels\Library\LocomotiveSpeedEditorViewModel.cs`,
`ViewModels\Library\SpeedProfileTableRowViewModel.cs`, `TrackFlow.Tests\LocomotiveSpeedEditorMarkupTests.cs`

**Zmena:** Tabuľka rýchlostného profilu bola zjednotená do jednej spoločnej RAW tabuľky, ktorá zobrazuje jeden riadok na
jeden `Step` a dve hodnoty naraz: `FwdRawSpeed` a `BwdRawSpeed`. Z tabuľky boli odstránené stĺpce `Čas` a `Calculated` a
pôvodné smerové tabuľky boli nahradené jedným spoločným zdrojom dát.

**Dôvod:** Pôvodné smerové záložky a doplnkové vypočítané údaje v tabuľke boli zbytočne zložité pre bežné zadávanie a
údržbu RAW meraní.

**Riešenie:**

- Pridaný nový row viewmodel `SpeedProfileTableRowViewModel`.
- `LocomotiveSpeedEditorViewModel` teraz udržiava spoločnú kolekciu `SpeedProfileRows` a synchronizuje ju s existujúcimi
  smerovými bodmi profilu.
- `LocomotivesWindow.axaml` používa novú RAW tabuľku s tromi stĺpcami: `Krok`, `RAW dopredu`, `RAW dozadu`.
- Testy boli upravené na nový binding tabuľky a na zapisovanie RAW hodnôt cez spoločný riadok.

**Výsledok:** Tabuľka je prehľadnejšia, priamočaře zapisuje raw merania pre oba smery a už nezobrazuje čas ani
vypočítanú rýchlosť.

## 2026-05-19 (Editor rýchlostného profilu – dynamické osi grafu, cleanup artefaktov a balenie zdrojákov)
==============
**Oblasť:** `Views\Library\LocomotivesWindow.axaml`, `ViewModels\Library\LocomotiveSpeedEditorViewModel.cs`,
`ViewModels\Library\LocomotivesWindowViewModel.cs`, `TrackFlow.Tests\LocomotiveSpeedEditorMarkupTests.cs`, build/output
hygiena, ZIP archivácia projektu

**Zmena:** Dnešná práca sa sústredila na editor rýchlostného profilu lokomotívy, najmä na graf kalibrácie: postupne sa
ladila výplň pod krivkou, následne sa prepracovalo správanie X-ovej a Y-ovej osi tak, aby sa dynamicky riadili údajmi
aktuálne kalibrovanej lokomotívy. Súbežne bol vyčistený obrovský lokálny `artifacts\` adresár, doplnená ochrana proti
ďalšiemu vytváraniu build artefaktov v projekte a pridaný skript na vytvorenie malého ZIP archívu iba zo zdrojových
súborov.

**Dôvod:**

- ZIP archivácia celého adresára `TrackFlow` mala približne 2,2–2,8 GB, najmä kvôli starým build/test artefaktom v
  `artifacts\`.
- Graf rýchlostnej kalibrácie mal pôvodne pevný rozsah X-osi `0–126` a Y-osi `0–120`, čo nezodpovedalo lokomotívam s
  iným počtom dekóderových krokov alebo inou maximálnou rýchlosťou.
- Po prechode na dynamické osi bolo potrebné opraviť samotné vykresľovanie stupníc a grid čiar, pretože
  `ItemsControl + Canvas` vyžaduje správne pozicionovanie item kontajnerov.
- Výplň pod krivkou sa viackrát vizuálne ladila, pričom cieľom bolo priblížiť sa referenčnému obrázku a zachovať
  jednoduchý princíp výplne bez zbytočne komplikovaného renderovania.

**Riešenie:**

- **Cleanup artefaktov a build výstupov:**
    - Odstránený lokálny adresár `artifacts\` s veľkosťou približne 2,8 GB.
    - Odstránené staré koreňové diagnostické súbory (`doctor-log-*.log`, `build_out.txt`).
    - `TrackFlow.csproj` bol uprataný: redundantné `Remove="artifacts\**"`, `Artifacts\**`, `TrackFlow.Tests\**` a
      neexistujúce `Shell\**` výnimky boli nahradené globálnym `DefaultItemExcludes` pre nepotrebné stromy (
      `artifacts\**`, `Artifacts\**`, `TrackFlow.Tests\**`, `_archived\**`, `logs\**`).
    - Pridaný nový `Directory.Build.props`, ktorý:
        - nastavuje štandardné výstupy na `bin\` a `obj\`,
        - tvrdým guardom blokuje `dotnet build/test/publish -o ...` alebo `OutDir/OutputPath`, ak smeruje mimo `bin\`,
          najmä do `artifacts\...`.
    - Otestované, že `dotnet build -o artifacts\X` skončí chybou s jasnou hláškou a adresár `artifacts\` sa nevytvorí.

- **ZIP archív iba zo zdrojákov:**
    - Pridaný nový skript `pack-source.ps1`, ktorý vytvorí ZIP iba zo zdrojových súborov.
    - Skript vynecháva najmä:
        - `bin\`, `obj\`, `artifacts\`, `Artifacts\`, `.git\`, `.vs\`, `.idea\`, `logs\`, `_archived\`, `.test-out\`,
          `TestResults\`, `route-test-isolation\`,
        - `*.user`, `*.suo`, `*.cache`, `*.pdb`, `apphost.exe`, `doctor-log-*.log`, `build_*.txt`, `.build_errors.txt`,
          `.berr.txt`, `build_out.txt`.
    - Smoke test skriptu vytvoril archív približne `17 MB` z približne `21,5 MB` zdrojových dát.

- **Výplň/gradient pod krivkou rýchlostného profilu:**
    - Opakovane sa ladil vizuál `ForwardAreaPathData` výplne v `LocomotivesWindow.axaml` podľa referenčných
      screenshotov.
    - Vyskúšané boli viaceré varianty:
        - jednoduchý diagonálny `LinearGradientBrush`,
        - svetlejšie štartovacie farby,
        - rýchlejšie miznutie pred pravým dolným rohom,
        - pokus o pásové renderovanie pozdĺž krivky cez `ForwardAreaGradientBands`.
    - Pásové riešenie bolo následne odstránené ako zbytočne komplikované a potenciálne problematické.
    - Finálne ostalo jednoduchšie riešenie výplne v XAML: výplň je viazaná na `ForwardAreaPathData` a kreslená
      štandardným XAML renderom bez dodatočnej kolekcie pásov vo viewmodeli.
    - Z `LocomotiveSpeedEditorViewModel.cs` boli odstránené dočasné experimentálne časti:
        - `ForwardAreaGradientBands`,
        - `GradientBandViewModel`,
        - `BuildCurveGradientBands(...)`,
        - `BuildCurveRibbonPath(...)`.

- **Dynamická X-os podľa kroku dekodéra:**
    - Pôvodný pevný rozsah X-osi `0–126` bol nahradený dynamickým `CurrentChartMaxStep`.
    - `CurrentChartMaxStep` sa nastavuje podľa `LocoRecord.DecoderType`:
        - `DCC 14` → `0–14`,
        - `DCC 27` → `0–27`,
        - `DCC 28` → `0–28`,
        - `DCC 126` → `0–126`,
        - prázdna/neplatná hodnota → fallback `126`.
    - Pridané `DecoderStepAxisTitle`, aby title osi zobrazoval aktuálny rozsah napr.
      `Rýchlostný stupeň dekodéra (0-28)`.
    - Všetky výpočty X súradníc boli prepojené z pevného `126` na aktuálny rozsah:
        - krivky,
        - fill path,
        - markery,
        - variance area,
        - klik do grafu,
        - drag/edit bodov,
        - clamp hodnoty kroku.
    - `LocomotivesWindowViewModel.SelectedDecoderType` teraz okamžite volá `SpeedEditor.SetDecoderStepRange(value)`,
      takže zmena v comboboxe `Krok dekodéra` prekreslí graf hneď.
    - Hardcoded X labely `0,20,40,60,80,100,120` v XAML boli nahradené bindingom `XAxisLabels`.
    - Opravené vykresľovanie X labelov cez `ItemsControl + Canvas`:
        - `Canvas.Left` a `Canvas.Top` sa nastavujú na `ContentPresenter` kontajner,
        - nie na vnorený `TextBlock` v šablóne.
    - Opravený bug vo výpočte `CalculateHorizontalAxisLabelLeft(...)`, kde chýbajúce zátvorky okolo `switch` výrazu
      spôsobovali nesprávne pozície labelov.
    - Pri `DCC 14` sa teraz zobrazujú všetky stupne `0..14`.

- **Dynamická Y-os podľa maximálnej rýchlosti lokomotívy:**
    - Pôvodný pevný rozsah Y-osi `0–120 km/h` bol nahradený dynamickým `CurrentChartMaxSpeed`.
    - `CurrentChartMaxSpeed` sa nastavuje podľa `LocoRecord.MaxSpeed` aktuálnej lokomotívy.
    - Ak lokomotíva nemá zadanú max. rýchlosť (`MaxSpeed <= 0`) alebo nie je vybraná žiadna lokomotíva, používa sa
      fallback `120 km/h`.
    - Ak používateľ vymaže hodnotu v poli max. rýchlosti (`MaxSpeedText`), interné `MaxSpeed` sa nastaví na `0`, ale
      graf sa vráti na fallback `120 km/h`.
    - Pridané dynamické `YAxisLabels` a `HorizontalGridLines`.
    - Hardcoded Y labely `120,100,80,60,40,20,0` boli nahradené bindingom `YAxisLabels`.
    - Hardcoded horizontálne grid čiary boli nahradené bindingom `HorizontalGridLines`.
    - Opravené vykresľovanie dynamických vodorovných grid čiar:
        - presenter má pevný rozmer `900 × 620`,
        - item kontajnery majú tiež rozmer `900 × 620`,
        - každá čiara sa kreslí vo vlastnom canvase, aby sa spoľahlivo zobrazila v chart ploche.
    - Na dynamický Y rozsah boli prepojené:
        - krivky,
        - fill path,
        - markery,
        - variance area,
        - klik do grafu / mapovanie súradníc,
        - hit-test bodov,
        - gauge angle,
        - clamp rýchlosti pri editácii.
    - `LocomotivesWindowViewModel.MaxSpeed` aj `MaxSpeedText` teraz okamžite volajú `SpeedEditor.SetChartMaxSpeed(...)`,
      takže zmena maximálnej rýchlosti prekreslí Y-os hneď.

- **Testy:**
    - Aktualizovaný a rozšírený `TrackFlow.Tests\LocomotiveSpeedEditorMarkupTests.cs`.
    - Doplnené regresné testy pre:
        - dynamický rozsah X-osi podľa `DCC 14`, `DCC 28`, `DCC 126`,
        - okamžitú reakciu grafu na zmenu `SelectedDecoderType`,
        - zobrazovanie všetkých stupňov `0..14` pri `DCC 14`,
        - rastúce `Left` pozície X labelov,
        - dynamický rozsah Y-osi podľa `MaxSpeed`,
        - okamžitú reakciu grafu na zmenu `MaxSpeed`,
        - fallback `120 km/h` pri nezadanej alebo vymazanej max. rýchlosti,
        - render bindingy pre `YAxisLabels`, `XAxisLabels` a `HorizontalGridLines`.

**Výsledok:**

- Projekt už neobsahuje lokálny `artifacts\` strom s gigabajtmi build/test výstupov.
- Budúce buildy sú chránené pred náhodným výstupom do `artifacts\` alebo iných ciest mimo `bin\`.
- Pre archiváciu zdrojákov je dostupný `pack-source.ps1`, ktorý vytvára rádovo menší ZIP archív.
- Graf rýchlostného profilu má dynamickú X-os podľa kroku dekodéra a dynamickú Y-os podľa maximálnej rýchlosti
  lokomotívy.
- Pri chýbajúcej maximálnej rýchlosti je fallback grafu `120 km/h`.
- X labely aj Y labely sú renderované cez bindingy namiesto hardcoded textov.
- Vodorovné grid čiary sú renderované dynamicky podľa aktuálnej Y stupnice.
- Relevantná testovacia sada bola priebežne spúšťaná izolovane s dočasným `BaseOutputPath` do `.test-out\bin\`, aby
  neprekážali locknuté bežiace procesy aplikácie.
- Finálne overená cielená sada:
    -
    `dotnet test .\TrackFlow.Tests\TrackFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~LocomotiveSpeedEditorMarkupTests" -p:BaseOutputPath=...\.test-out\bin\ -nologo -v minimal`
    - výsledok: `Passed: 33, Failed: 0`
- Dočasný `.test-out\` bol po testoch odstránený.

## 2026-05-17 (Doctor a logy – centralizácia hlášok a veľké upratanie diagnostiky)
==============
**Oblasť:** Doctor diagnostika, `TrackFlowDoctorService`, `TrackFlowDoctorMessages`, `OperationViewModel`, signal/DCC
logovanie, UI diagnostika

**Zmena:** Dokončené komplexné upratanie Doctor hlášok a technických logov tak, aby v aplikácii ostali len operátorsky
užitočné správy, pričom texty Doctora sú teraz centralizované na jednom mieste pre jednoduché budúce prispôsobenie.

**Dôvod:** Doctor okno aj súvisiace runtime/UI/DCC logovanie obsahovali priveľa interných alebo technických správ, ktoré
používateľovi nepomáhali. Zároveň bolo potrebné odstrániť nejasné formulácie (najmä pri výhybkách), prestať zobrazovať
interné ID ciest a vytvoriť jedno miesto, kde sa dajú Doctor texty neskôr pohodlne upraviť.

**Riešenie:**

- Vytvorený nový centralizačný súbor `Services\TrackFlowDoctorMessages.cs` pre texty Doctor správ a ich formátovanie.
- `TrackFlowDoctorService` bol prepojený na túto message vrstvu, aby už nemal roztrúsené hardcoded texty priamo vo
  formatteri.
- Doctor route názvy boli zhumanizované:
    - namiesto technických route ID alebo dlhých interných názvov sa používa čitateľný formát štartový blok `→` cieľový
      blok,
    - ak čitateľný názov nie je dostupný, technický reťazec sa nepotvrdzuje ako „názov cesty“, ale spadne na bezpečné
      `neznáma cesta`.
- Formulácie turnout správ boli opravené tak, aby už nehlásili iba neurčité „uvoľnená“, ale uvádzali aj výslednú fyzickú
  polohu (`Rovno` / `do odbočky`).
- `OperationViewModel` bol upravený tak, aby Doctor-facing route texty používali endpoint labely a aby turnout release
  diagnostika zapisovala výslednú polohu výhybky.
- Z Doctor UI boli odstránené alebo úplne zrušené zbytočné interné multi-tag diagnostiky bez operátorskej hodnoty,
  vrátane rodín ako:
    - `[MULTI][UI-HIGHLIGHT]`,
    - `[MULTI][REZ-ENGINE]`,
    - `[MULTI][ORCHESTRACIA]`,
    - technické `[MULTI][CESTA]` lifecycle trace,
    - `[MULTI][UVOLNENIE]`,
    - `[MULTI][MOVEMENT]`,
    - `[MULTI][FRONTIER]`,
    - `[MULTI][CAKANIE][DUPLIKAT]`.
- `Views\DoctorWindow.axaml` a `Views\DoctorWindow.axaml.cs` boli zosúladené s novým stavom diagnostiky:
    - odstránený zastaraný filter `Uvoľnenie`,
    - Doctor UI filtruje iba správy, ktoré majú ešte zmysel pre obsluhu.
- `Services\UI\ActiveRouteVisualScopeResolver.cs` prestal generovať `UI-HIGHLIGHT` diagnostiku, pričom samotné
  visual-scope správanie ostalo zachované.
- `Views\Operation\OperationView.axaml.cs` bol vyčistený od posledných multi-tag UI/kurzor/refresher logov (
  `[MULTI][KURZOR]`, `[MULTI][UI]`, `[MULTI][UI-REFRESH]`).
- Následne boli odstránené aj zvyšné netagované technické logy mimo Doctor vrstvy, najmä v:
    - `Services\SignalController.cs`,
    - `Services\Dcc\Z21Client.cs`.
- Pri tomto poslednom cleanup kroku boli zachované warning/error logy s reálnou diagnostickou hodnotou, ale odstránené
  debug/info šumy typu skip/cache-hit/attempt trace a podobné interné výpisy.
- Súbežne boli upravené a doplnené regresné testy pre:
    - nové turnout formulácie,
    - čitateľné route názvy v Doctorovi,
    - skrytie/odstránenie technických multi-tag správ,
    - neprítomnosť zrušených UI multi-tag logov,
    - signal profile validačné API po premenovaní helperov.

**Výsledok:**

- Doctor okno je výrazne čistejšie a zobrazuje primárne len udalosti, ktoré majú hodnotu pre obsluhu.
- Texty Doctor správ sú centralizované a pripravené na ďalšie ručné prispôsobenie z jedného miesta.
- Hlášky o výhybkách a cestách používajú čitateľnejšie a prevádzkovo zmysluplnejšie formulácie.
- `Z21Client.cs` už neobsahuje žiadne `Log.` výpisy; `SignalController.cs` bol zredukovaný na podstatné warning/error
  logy.
- Overená cielená regresná sada:
    - `SignalControllerTests`
    - `SignalSystemRegistryTests`
    - `TrackFlowDoctorServiceTests`
    - `OperationViewModelDoctorDiagnosticsTests`
    - `RuntimeStateRegistryFrontierTests`
    - `OperationViewInteractionMarkupTests`
    - výsledok: `Passed: 170, Failed: 0`
- Overený build celého riešenia:
    - `dotnet build TrackFlow.sln --no-restore` → `Build succeeded`, `0 Warning(s)`, `0 Error(s)`

## 2026-05-16 (Multi-route rezervácie – oprava blokových rezervácií a upresnenie shared-block scenára)
==============
**Oblasť:** Operation runtime, multi-route rezervácie, shared-block správanie, diagnostika testovacieho scenára

**Zmena:** Opravené chybné rezervovanie blokov pri route logike tak, aby bloky už nezostávali nesprávne rezervované, a
zároveň bol spresnený referenčný shared-block scenár používaný pri ďalšom ladení.

**Dôvod:** Pri testovaní súbežných ciest sa ukázalo, že rezervácie blokov sa síce po oprave prestali zasekávať, ale
pôvodný slovný popis testovacieho usporiadania bol nepresný. Nebolo správne hovoriť o sekvencii „A a X (štart + zdieľaný
1)“, pretože blok `X` nie je shared blok; shared blok je až nasledujúci blok `Y`.

**Riešenie:**

- Runtime oprava rezervácií bola potvrdená ako funkčná v tom zmysle, že bloky sa už nezasekávajú v rezervovanom stave.
- Testovací scenár bol terminologicky zosúladený na správne poradie `A -> X -> Y`.
- Explicitne bolo upresnené, že:
    - `A` je štartovací blok,
    - `X` je bežný nasledujúci blok,
    - `Y` je shared blok, na ktorom sa stretáva konflikt/synchronizácia ciest.
- Záznam bol doplnený do changelogu, aby ďalšia analýza aktivácie oboch ciest vychádzala zo správneho modelu trate.

**Výsledok:**

- Rezervácie blokov sa už nezobrazujú ani nesprávne rezervované.
- Ďalšie ladenie multi-route aktivácie bude vychádzať zo správne pomenovaného scenára `A -> X -> Y`, kde shared blok je
  až `Y`.
- Changelog opäť obsahuje dnešný záznam aj po obnovení pôvodného súboru.

## 2026-05-15 (Milestone backup – Phase 1 substrate: authoritative frontier runtime foundation)
==============
**Oblasť:** `RuntimeStateRegistry`, frontier substrate, runtime ownership foundation, diagnostika, validačné helpery

**Zmena:** Vytvorený nový milestone checkpoint po implementácii Phase 1 substrate pre authoritative frontier runtime
foundation bez migrácie readerov a bez zmeny existujúceho runtime behavior.

**Dôvod:** Po dokončení audit/RFC fázy bolo potrebné uložiť samostatný návratový bod po zavedení samotného substrate:
nových frontier typov, registry storage vrstvy, atomic publish/read API, validačných helperov a diagnostiky. Tento
checkpoint oddeľuje foundation vrstvu od neskoršieho wiring kroku, kde sa publisherom stane `TraversalEngine`.

**Riešenie:**

- Do `Runtime\RouteRuntimeState.cs` boli pridané nové typy:
    - `RouteFrontierPublisherKind`,
    - `TraversalFrontierSnapshot`,
    - `RouteFrontierState`.
- `Runtime\RuntimeStateRegistry.cs` bol rozšírený o nový thread-safe frontier substrate:
    - `_frontierSync`,
    - `_routeFrontiers`,
    - `GetRouteFrontierState(...)`,
    - `TryGetRouteFrontierState(...)`,
    - `EnumerateActiveFrontiers()`,
    - `SetRouteFrontierState(...)`,
    - `PublishTraversalFrontier(...)`,
    - `ClearRouteFrontier(...)`,
    - `ClearAllRouteFrontiers()`,
    - `ValidateFrontierSnapshot(...)`.
- Publish kontrakt je v tejto fáze striktne substrate-only:
    - atomic replace snapshotu,
    - centrálne `Version` increment,
    - centrálne `UpdatedAtUtc`,
    - normalizácia a validácia snapshotu pred publish.
- Validation zatiaľ iba loguje a `Debug.Assert`-uje, ale nemení runtime flow a nehádže runtime exceptions.
- Doplnené frontier diagnostiky:
    - `frontier-publish-snapshot`,
    - `frontier-snapshot-invalid`,
    - `frontier-clear`.
- Pridané cielené testy v `TrackFlow.Tests\RuntimeStateRegistryFrontierTests.cs` pre publish/versioning/validation/clear
  scenáre.
- Doplnený stručný implementačný status dokument `PHASE1_STATUS.md` a auditný forward-link v
  `RUNTIME_SAFETY_AUDIT_2026-05-15.md`, aby bola auditná línia aj substrate checkpoint textovo previazaná.
- Žiadny existujúci runtime consumer zatiaľ nový frontier substrate nečíta a `TraversalEngine` ešte frontier snapshoty
  nepublikuje.

**Výsledok:**

- Authoritative frontier runtime foundation už existuje v kóde ako paralelný neaktívny substrate.
- Existujúci runtime behavior ostal nezmenený.
- Overený build projektu:
    -
    `dotnet build .\TrackFlow.csproj /p:OutputPath=artifacts\temp-build\ | Tee-Object -FilePath .\artifacts\build_log.txt` →
    OK
- Overené cielené testy substrate vrstvy:
    -
    `dotnet test .\TrackFlow.Tests\TrackFlow.Tests.csproj --no-restore /p:OutputPath=..\artifacts\temp-test-build\ --filter "RuntimeStateRegistryFrontierTests"` →
    `Passed: 4, Failed: 0`
- Vytvorený nový milestone backup súbor `MILESTONE_BACKUP_2026-05-15.md` ako návratový checkpoint pre ďalší frontier
  wiring krok.

## 2026-05-14 (TrackFlow_runtime_refactor_phase5_stable – WAIT orchestration ownership extraction)
==============
**Oblasť:** `OperationViewModel`, WAIT lifecycle, retry orchestration, sticky winner arbitration, deadlock/yield flow,
runtime diagnostika

**Zmena:** Vytvorený nový stabilný checkpoint po behavior-preserving extrakcii WAIT orchestration ownership z
`OperationViewModel` do `Services/Runtime/TraversalWaitCoordinator.cs` bez redesignu WAIT modelu, retry heuristík,
deadlock systému, arbitration algoritmu, traversal flow alebo reservation/signal ownershipu.

**Dôvod:** Po extrakcii reservation, traversal a signal safety ownershipu zostával najcitlivejší orchestration blok
stále v `OperationViewModel` – WAIT lifecycle, retry loop timing, sticky winner handover a deadlock/yield orchestration.
Pred ďalšou fázou bolo potrebné uložiť samostatný stabilný návratový bod pre túto behavior vrstvu.

**Riešenie:**

- Vytvorený `Services/Runtime/TraversalWaitCoordinator.cs`, ktorý teraz vlastní:
    - `WaitForNextBlockReservationAsync(...)`,
    - WAIT lifecycle enter/exit orchestration,
    - retry timing a retry loop sequencing,
    - sticky winner orchestration pre bloky aj výhybky,
    - circular wait deadlock detection,
    - deadlock/yield orchestration,
    - WAIT diagnostics a duplicate orchestration diagnostics.
- `OperationViewModel` bol prepojený na delegáciu do `_waitCoordinator`, pričom traversal continuation ostáva v
  `TraversalEngine`, reservation ownership v `ReservationEngine` a signal ownership v `SignalSafetyEngine`.
- Zachované bolo kritické poradie callov:
    - `WAIT -> STOP enforce -> retry reservation -> signal restore -> traversal continue`,
    - `boundary-entry -> signal recompute -> reservation advance`,
    - `tail-clear -> occupancy reconcile -> signal recompute`.
- Do diagnostiky boli doplnené explicitné stavy:
    - `wait-enter`,
    - `retry-start`,
    - `retry-denied`,
    - `retry-success`,
    - `timeout`,
    - `yield`,
    - `winner-assign`,
    - `winner-consume`,
    - plus auditné značky pre duplicate reservation apply / signal recompute / visual refresh trigger.
- Legacy interné test helpery boli zosúladené s aktuálnym runtime ownership modelom cez `RuntimeStateRegistry` a
  `TraversalWaitCoordinator`, bez návratu ownershipu späť do `OperationViewModel`.

**Výsledok:**

- WAIT orchestration ownership už nie je držaný inline v `OperationViewModel`, ale v `TraversalWaitCoordinator`.
- Build projektu bol overený úspešne:
    -
    `dotnet build .\TrackFlow.csproj --configuration Debug --no-restore -p:OutDir=C:\Users\Jaroslav\source\repos\TrackFlow\artifacts\temp-build\` →
    OK
- Diagnostická regresná sada bola overená úspešne:
    -
    `dotnet test .\TrackFlow.Tests\TrackFlow.Tests.csproj --no-restore -p:OutDir=C:\Users\Jaroslav\source\repos\TrackFlow\artifacts\temp-test-build\ --filter "FullyQualifiedName~TrackFlow.Tests.OperationViewModelDoctorDiagnosticsTests"` →
    `Passed: 40, Failed: 0`
- Vytvorený nový stabilný backup checkpoint `TrackFlow_runtime_refactor_phase5_stable.md` pre návrat k tejto fáze
  refaktoru.

## 2026-05-14 (Milestone backup – runtime ownership extraction: reservation + traversal + signal safety)
==============
**Oblasť:** `OperationViewModel`, runtime ownership refaktor, reservation lifecycle, traversal continuation, signal
safety orchestration

**Zmena:** Vytvorený nový milestone checkpoint po behavior-preserving extrakcii kľúčových runtime ownership vrstiev z
`OperationViewModel` do samostatných engine tried bez redesignu WAIT, traversal alebo signal algoritmov.

**Dôvod:** `OperationViewModel` dlhodobo držal rezervácie, traversal continuation aj signal recompute orchestration v
jednom vysoko rizikovom orchestration súbore. Pred ďalšími fázami bolo potrebné oddeliť ownership zodpovedností a uložiť
stabilný návratový bod.

**Riešenie:**

- Do `Runtime/` už bol predtým stabilizovaný route runtime ownership cez `RouteRuntimeState` a `RuntimeStateRegistry`
  ako jediný zdroj runtime pravdy.
- Vytvorený `Services/Runtime/ReservationEngine.cs`, ktorý vlastní reservation lifecycle orchestration:
    - initial reservation window,
    - reservation advance,
    - boundary-entry ownership transition,
    - tail-clear release flow,
    - shadow reservation handling,
    - reservation ownership validation.
- Vytvorený `Services/Runtime/TraversalEngine.cs`, ktorý vlastní traversal runtime ownership:
    - `traversalBlockIds`,
    - current traversal index/state v `RuntimeStateRegistry`,
    - route-local continuation order,
    - next block resolution,
    - traversal completion determination,
    - traversal window state.
- Vytvorený `Services/Signals/SignalSafetyEngine.cs`, ktorý vlastní signal safety orchestration:
    - `UpdateTraversalSignalWindowAsync(...)`,
    - look-ahead signal validation,
    - next-main-signal resolution,
    - STOP/Proceed arbitration,
    - released-block signal STOP recompute,
    - all-red / route fallback signal reset helpery.
- `OperationViewModel` bol postupne prepojený na delegáciu do `_reservationEngine`, `_traversalEngine` a
  `_signalSafetyEngine`, pričom timing a poradie callov ostali zachované.
- Topology-driven a reservation-driven legacy helpery neboli v tejto fáze prerábané; boli len identifikované alebo
  komentované ako kandidáti na budúci cleanup.

**Výsledok:**

- Ownership rezervácií, traversal continuation a signal recompute už nie je centralizovaný priamo v
  `OperationViewModel`.
- Runtime správanie ostalo zachované bez nového WAIT modelu, nového signal systému alebo traversal redesignu.
- Overený build projektu:
  `dotnet build .\\TrackFlow.csproj --configuration Debug --no-restore -p:OutDir=C:\\Users\\Jaroslav\\source\\repos\\TrackFlow\\artifacts\\temp-build\\` →
  OK.
- Vytvorený nový milestone backup súbor `MILESTONE_BACKUP_2026-05-14.md` ako návratový checkpoint pre ďalšie refaktor
  fázy.

## 2026-05-14 (Dnešná práca – audit a príprava refaktorizácie `OperationViewModel.cs`)
==============
**Oblasť:** `OperationViewModel`, operation runtime, interná diagnostika a stabilizácia

**Zmena:** Dnešná práca bola zameraná na audit, diagnostiku a prípravu refaktoringového checkpointu pre
`OperationViewModel.cs`.

**Dôvod:** `OperationViewModel.cs` zostáva veľký a rizikový orchestration súbor, v ktorom sa riešili problematické
oblasti okolo runtime správania, traversal/occupancy toku, signal window logiky a kompatibility interných helperov. Pred
ďalšou refaktorizáciou bolo potrebné pomenovať a zdokumentovať problémové miesta.

**Riešenie:**

- Prebehol audit aktuálneho stavu `OperationViewModel.cs` a nadväzných testov so zameraním na rizikové runtime vetvy.
- Boli analyzované problematické scenáre okolo shared-block / multi-route správania, traversal diagnostiky, signal
  safety a boundary-entry / tail-clear kompatibility.
- Prebehli cielené diagnostické a regresné kontroly na potvrdenie problémových miest pred ďalšou refaktorizáciou.

**Výsledok:**

- Dnešná etapa je v changelogu zapísaná ako analytický a stabilizácia checkpoint pred ďalším zásahom do štruktúry
  `OperationViewModel.cs`.
- Sú jasne oddelené dnešné pracovné zistenia od samostatného milestone backup záznamu.

## 2026-05-14 (Miľniková záloha pred refaktorizáciou `OperationViewModel.cs`)
==============
**Oblasť:** `OperationViewModel`, operation runtime, interná údržba kódu

**Zmena:** Do changelogu bol doplnený explicitný záznam, že aktuálny stav projektu predstavuje miľnikovú zálohu pred
plánovanou refaktorizáciou súboru `OperationViewModel.cs`.

**Dôvod:** Pred rozsiahlejším zásahom do `OperationViewModel.cs` je potrebné mať jasne označený checkpoint pracovného
stavu, ku ktorému sa dá v prípade potreby bezpečne vrátiť.

**Riešenie:**

- Záznam explicitne označuje tento stav ako milestone backup pred refaktorizáciou `OperationViewModel.cs`.

**Výsledok:**

- Changelog jednoznačne dokumentuje, že ide o miľnikovú zálohu pred refaktorizáciou `OperationViewModel.cs`.

## 2026-05-13 (Milestone backup – multi-route runtime validácia + WAIT recovery + cancel cleanup)
==============
**Oblasť:** Operation runtime, `OperationViewModel`, multi-route rezervácie, WAIT flow, route-local cleanup, runtime
diagnostické testy

**Zmena:** Vytvorený nový milestone backup pre dokončený stabilný checkpoint multi-route runtime správania bez zmeny
architektúry. Backup zahŕňa segment-local ownership alignment, runtime validačné scenáre, opravu shared-block WAIT
recovery a opravu route-local cleanup pri cancel počas WAIT.

**Dôvod:** Bolo potrebné potvrdiť reálne runtime správania pri súbežných cestách, odstrániť potvrdené leaks/WAIT
recovery chyby a uložiť stabilný checkpoint pred ďalšou samostatnou fázou diagnostických logov a sample layoutov.

**Riešenie:**

- `ClearRouteReservations(...)`, `ResolvePrimaryRouteLocoId(...)` a `GetRouteActiveBlockIds(...)` už nepredpokladajú
  route-global vlastníctvo cez celé `route.BlockIds`, ale pracujú s runtime-owned blokmi cez
  `ResolveRouteRuntimeOwnedBlockIds(...)`.
- Do `TrackFlow.Tests\OperationViewModelDoctorDiagnosticsTests.cs` boli doplnené runtime scenáre pre:
    - shared block WAIT,
    - shared turnout WAIT,
    - parallel non-conflicting routes,
    - cancel jednej route počas WAIT,
    - WAIT timeout cleanup,
    - tail-clear turnout release.
- Potvrdený shared-block WAIT bug bol opravený izolovane v runtime flow:
    - WAIT retry rezervácie používa interný bypass dočasného protecting-signal STOP gate iba pre retry vetvu,
    - po úspešnej retry rezervácii sa explicitne obnoví traversal segment window a signal window,
    - movement continuation po release shared bloku už nekončí falošným wait-timeoutom.
- Potvrdený cancel leak počas WAIT bol opravený route-local cleanup helperom:
    - cancel už nepropaguje nekontrolovaný `TaskCanceledException` mimo route lifecycle,
    - cleanup spoľahlivo odstráni `_activeRouteIds`, `_routeRuntime`, `_routeActiveWindows`, WAIT stav a route-local
      turnout ownership pre rušenú cestu,
    - ostatné aktívne route zostávajú nedotknuté.
- Do runtime flow boli doplnené cielené logy pre WAIT retry recovery a WAIT cancel cleanup cez existujúce logovanie.
- Následná požiadavka na nové slovenské tagged Doctor logy a multi-route Samples bola analyzovaná, ale do tohto
  milestone backupu ešte nie je zahrnutá.

**Výsledok:**

- Segment-local cleanup a ownership introspekcia sú zosúladené s runtime-local traversal modelom.
- Shared-block WAIT sa po uvoľnení konfliktu korektne obnoví a pokračuje v jazde.
- Cancel jednej route počas WAIT už nenecháva stale route ownership state a nepoškodzuje blokujúcu route.
- Runtime validačné scenáre pre multi-route WAIT/cleanup/tail-clear sú pokryté regresnými testami.
- Posledný overený stav pred ďalšou fázou: `dotnet test -c Release` = `462/462` OK.

## 2026-05-12 (Operation runtime – Dispatcher diagnostika a redukcia Doctor forensic logov)
==============
**Oblasť:** Operation runtime, `OperationViewModel`, Doctor diagnostika, reservation/shadow dispatcher

**Zmena:** Do operation runtime bol doplnený samostatný Doctor diagnostický kanál `Dispatcher` pre stručné sledovanie
rezervácií, čakania, uvoľňovania a obsadenia blokov počas jazdy.

**Dôvod:** Doctor okno bolo zahltené starými forensic/debug multiline výpismi (`DirectionAnalysis`, route compare dumpy,
terminal compare, post-activation debug), takže nové runtime reservation udalosti neboli počas reálnej prevádzky dobre
čitateľné.

**Riešenie:**

- `Dispatcher` teraz loguje úspešné rezervácie blokov (`RESERVED`), zlyhané rezervácie (`FAILED reservation`), čakanie
  na blok (`WAITING`), uvoľnenie rezervácie (`RELEASED`), posun reservation window (`ADVANCE reservation window`) a nové
  runtime obsadenie bloku (`OCCUPIED`).
- Logy sú zapojené v miestach skutočnej runtime mutácie stavu: `ReserveBlock(...)`, `ReserveNextBlock(...)`,
  `ClearShadowReservation(...)`, `AdvanceReservationWindow(...)`, `WaitForTargetReservationAsync(...)` a
  `HandleOccupiedBlocks(...)`.
- Staré direction/terminal compare forensic helpery a multiline Doctor dumpy boli odstránené alebo demotované mimo
  Doctor okna; ponechané sú najmä stručné runtime orientované záznamy pre `Dispatcher`, `RouteActivation`, `Safety` a
  `Senzor`.
- Reservation, signal, conflict, traversal window ani DCC logika neboli zámerne menené.

**Výsledok:**

- Dispatcher smoke log z testu obsahuje:
    - `ADVANCE reservation window: train=[754], current=[B2]`
    - `TRAIN [754] RESERVED block [B3]`
    - `OCCUPIED block [B3] by train [754]`
    - `TRAIN [754] RELEASED block [B3]`
    - `TRAIN [754] FAILED reservation for block [B3]`
    - `TRAIN [754] WAITING for block [B3]`
- Fokusované testy `OperationViewModelDoctorDiagnosticsTests` + `OperationViewModelRouteActivationTests`: `56/56` OK.
- `dotnet build TrackFlow.sln` s temp outputom a vypnutými debug symbolmi: OK.

## 2026-05-11 (Route generation – canonical ordering pre auto-generated routes)
==============
**Oblasť:** Editor Routes Manager, `RoutePathfinder`, auto-generated `RouteDefinition`, route activation direction

**Zmena:** Auto-generated routes sa pri generovaní ukladajú v deterministickom canonical poradí podľa blokov, namiesto
toho, aby prvý smer nájdený DFS/pathfinderom implicitne určil `FromBlockId`, `ToBlockId` a `BlockIds`.

**Dôvod:** Krátke 2-block route ako `B1 → B4`, `B1 → B5`, `B1 → B6` sa v niektorých layoutoch generovali opačne, napr.
ako `Blok 5 → Blok 1`. Pri aktivácii z `B1` potom runtime správne vytvoril reverznú aktivačnú route (`Blok 1 → Blok 5`),
ale diagnostika ukazovala `reversed=True` a následná direction chain mohla skončiť dashboard reverse. Dlhšie routes
`B1 → B7/B8/B9` mali `reversed=False`, pretože ich canonical smer vyšiel z pathfinder enumerácie správne.

**Root cause:** `RoutesManagerViewModel.GenerateRoutes()` používal prvý `FoundRoute` z
`RoutePathfinder.FindAllRoutes()`. Keďže duplicate check považoval opačné smery tej istej dvojice blokov za rovnakú
route, prvý nájdený smer vyhral a opačný smer sa zahodil. Tým sa canonical route ordering stal závislý od DFS/enumerácie
elementov layoutu.

**Riešenie:**

- Do `RoutesManagerViewModel.GenerateRoutes()` bola doplnená canonical orientácia nájdenej route pred vytvorením
  `RouteDefinition`.
- Nový helper `OrientFoundRouteForCanonicalGeneration(...)`:
    - porovnáva display názvy blokov z `AvailableBlocks`, fallback na ID,
    - ak je nájdený smer opačný voči canonical poradiu, otočí `FoundRoute`,
    - prehodí `FromBlockId` / `ToBlockId`, porty `FromBlockExitPort` / `ToBlockEntryPort`, otočí `PathElementIds` a
      zachová `TurnoutStates`.
- Nepribudol žiadny hardcoded fix pre `B5`, `B4/B5/B6` ani špeciálna vetva podľa počtu blokov.
- Nebola menená traversal logika, DCC, signal/look-ahead logika ani dashboard/direction heuristiky.

**Výsledok:**

- Auto-generated 2-block route `Blok 1 → Blok 5` sa ukladajú ako `FromBlockId=Blok 1`, `ToBlockId=Blok 5`,
  `BlockIds=[Blok 1, Blok 5]` aj vtedy, keď pathfinder najprv nájde opačný smer.
- Pre `B1 → B4/B5/B6` má byť po regenerácii route `reversed=False` namiesto runtime reverzácie canonical route.
- Direction layer zostáva robustná voči legitímnemu `reversed=True`; táto oprava len odstraňuje nesprávne canonical
  poradie pri generovaní.
- Pridaný regresný test `GenerateRoutes_CanonicalizesTwoBlockRouteDirection_WhenPathfinderFindsReverseFirst`.
- Relevantné testy: `18/18` OK (`RoutesManagerViewModelRouteMetadataTests`, `RoutesManagerViewModelDirectionTests`,
  `RoutePathfinderManualRouteTests`, `MoveLocomotiveByRouteElementAsync`, `CollisionDetectionServiceTests`).
- `dotnet build TrackFlow.sln` s temp outputom a vypnutými debug symbolmi: OK.

## 2026-05-11 (Runtime safety – route-scoped topologická safety-distance)
==============
**Oblasť:** Operation runtime safety, `CollisionDetectionService`, Doctor diagnostika, route activation

**Zmena:** Safety-distance kontrola pre kliknutie na route marker bola prepracovaná z globálneho route adjacency grafu
na route-scoped topológiu viazanú na práve aktivovanú `candidateRoute`.

**Dôvod:** Pri jazde napr. `B1 → B5` vznikal falošný `neighbor-block-occupied`, pretože safety graf najprv používal
globálnu topológiu zo všetkých `RouteDefinition`. Alternatívne/paralelné route definície tak vytvárali falošné susedstvá
typu `Blok 5 ↔ Blok 3`, hoci nepatrili do kandidátnej jazdnej cesty.

**Riešenie:**

- `CollisionDetectionService` dostal route-aware overload cez `TrackLayout` + `RouteDefinition? candidateRoute`.
- BFS seed handling bol opravený na target-only:
    - `GetRouteSafetySeedBlockIds(...)` vracia iba `targetBlockId`,
    - route bloky už nie sú BFS starting points,
    - odstránené bolo aj automatické occupied-precheck správanie nad seed blokmi.
- `BuildTopologicalBlockAdjacency(...)` je pri `candidateRoute != null` route-scoped:
    - používa iba `candidateRoute.BlockIds`,
    - nepoužíva globálne `layout.Routes`,
    - globálny route graph ostáva iba fallback pre volania bez `candidateRoute`.
- Pridaná diagnostika adjacency do Doctor/logu:
    - `Adjacency for Blok X: ...`,
    - vypisuje susedov target bloku aj route definíciu, z ktorej edge vznikol.
- `OperationViewModel` pri runtime route safety volá route-aware kontrolu s matched route.
- Doctor výpisy v route/safety toku boli skrátené:
    - bloky sa vypisujú cez `BlockDisplayName`,
    - route cez názov alebo `B1 → B5`,
    - lokomotíva/vlak cez display názov namiesto interného ID/kódu.

**Výsledok:**

- Paralelné alebo alternatívne route definície už nevytvárajú falošné safety susedov pre aktuálnu kandidátnu cestu.
- `Blok 3` môže blokovať iba vtedy, ak je skutočne susedom targetu v `candidateRoute.BlockIds`, nie len v inej route
  definícii layoutu.
- Cielené testy `CollisionDetectionServiceTests`: `9/9` OK.
- Route marker testy `MoveLocomotiveByRouteElementAsync`: `2/2` OK.
- `dotnet build TrackFlow.sln`: OK.

## 2026-05-11 (OperationViewModel – refactoring: direction/orientation extraction + stabilizácia)
==============
**Oblasť:** OperationViewModel, Services/Operation/, refactoring infrastructure

**Zmena:**

- **Variant A**: Mechanická 1:1 extrakcia direction/orientation cluster do
  `Services/Operation/RouteDirectionAnalyzer.cs`
- **Variant S**: Stabilizácia – odstránenie 7 dead forwarderov z OVM

**Dôvod:**

- Zmenšiť OVM (~3 351 riadkov) a zlepšiť údržbu extraction-vhodných častí pred zásahom do HIGH-RISK orchestration
  vrstiev
- Vytvoriť stabilný checkpoint pred dlhším smoke testingom v reálnej prevádzke
- Odstrániť forwarder noise po predchádzajúcich extrakciách

**Riešenie:**

- **Variant A (direction/orientation extraction)**:
    - Vytvorený `Services/Operation/RouteDirectionAnalyzer.cs` (public static class, 96 riadkov)
    - Presunuté metódy: `AnalyzeDirectionForMove`, `TryApplyAutomaticDirectionIfStopped`,
      `AnalyzeOrientationSyncForRoute`
    - Presunuté record structs: `DirectionAnalysis`, `OrientationSyncAnalysis`
    - `OperationViewModel`: pridaný `using static TrackFlow.Services.Operation.RouteDirectionAnalyzer;`
    - Odstránené lokálne duplikáty z OVM (5 členov)
    - Zachované: `AnalyzeSelectedLocoDirectionForRoute` (instance metóda, iný scope)
    - Protokol: mechanical 1:1, žiadne reinterpretácie logiky, branch/fallback/nullable/enum/direction/mutation ordering
      preserved
- **Variant S (stabilization cleanup)**:
    - Verified 8 forwarder candidates: call-sites + reflection + nameof + test refs + delegate captures
    - Discovered audit correction: `IsForwardRouteDirection` má 1 LIVE call-site (line 3713) → kept
    - Mechanicky odstránených 7 confirmed dead forwarderov:
        - `InvertRouteDirection`, `ResolveLocoCurrentBlockId`, `BlockDisplayName`, `SignalDisplayName`
        - `ResolveRouteStartBlockId`, `ResolveRouteEndBlockId`, `ResolveBlockDisplayName`
    - Každý nahradený 2-line navigation komentárom k helper triede
    - Kept 3 LIVE forwarders: `InvokeLayoutRefreshAsync` (4 call-sites), `ResolveSelectedLocoPhysicalOrientation` (1
      call-site), `IsForwardRouteDirection` (1 call-site)
- **Post-stabilization audit**:
    - Helper fragmentation: 7 acyklických helperov (RouteDirectionAnalyzer, RouteDirectionUtilities,
      RouteActivationOrder, BlockLookAheadHelper, LocoStateResolver, OperationDisplayHelpers, UiDispatcherHelper)
    - Dependency graph: acyklický, jediná intra-helper hrana: RouteActivationOrder → LocoStateResolver
    - Largest remaining clusters (HIGH-RISK, zámerne nedotknuté): heartbeat (~470 lines), move orchestration (~300),
      occupancy ingestion (~250), signal sequencing (~200), route activation (~127)
    - **Oblasti permanentne odložené** (až po smoke testingu): `WaitForTargetReservationAsync`, heartbeat cluster,
      signal sequencing, DCC orchestration, marker cluster, traversal mutation orchestration

**Výsledok:**

- `dotnet build -c Release`: 0 errors, 0 warnings, Time Elapsed 00:00:57.16
- `dotnet test -c Debug`: **424/424 passed**, Duration: 958 ms
- **OVM metrics**: 3 344 riadkov (−7), 78 private methods (−7), 19 public (bez zmeny), 25 async (bez zmeny), 3
  forwarders (−7, všetky LIVE)
- **Status**: STABLE CHECKPOINT — pripravené na produkčný smoke testing
- **Stratégia**: Controlled stabilization, žiadna ďalšia extrakcia v tomto momente; priorita = reálne runtime
  používanie, route activation/deactivation, signal sequencing, traversal windows, DCC dispatch ordering, occupancy
  ingest, ghost handling, teleport, panic stop, simulation heartbeat drift, reservation release timing
- **Refactor Summary Document**: vytvorené dokumentačné summary checkpoint (helper inventár, HIGH-RISK area map, future
  refactor candidates, operational recommendations)

## 2026-05-11 (Návestidlá – plný odchodový SR profil + fallback pre legacy 4-znakové)
==============
**Oblasť:** Návestidlá, renderer markerov, návestná sústava

**Zmena:** Odchodové návestidlo už má plnohodnotný profil `5-aspect-departure` a starý profil `4-aspect-departure` bol
ponechaný len ako legacy/obmedzený variant bez predstierania neexistujúcej hornej žltej.

**Dôvod:** Profil `4-aspect-departure` fyzicky obsahuje iba zelenú, červenú, bielu a dolnú žltú. Nevie teda korektne
zobraziť aspekty `Caution`, `SlowCaution` ani `SlowExpect40`, ktoré v SR sústave vyžadujú hornú žltú.

**Riešenie:**

- `SignalSystemRegistry`: pridaný nový profil `5-aspect-departure` s lampami
  `[horná žltá, zelená, červená, biela, dolná žltá]`.
- Nový profil podporuje kompletné SR aspekty: `Stop`, `Proceed`, `Caution`, `SlowProceed`, `SlowCaution`,
  `SlowExpect40`, `ShuntingPermitted`.
- `LayoutEditorViewModel.CreateSignalElement(...)`: marker `Signal4` teraz pri vkladaní vytvára profil
  `5-aspect-departure` namiesto starého `4-aspect-departure`.
- `Ribbon/MainRibbonView.axaml`: odchodové tlačidlo v ribbone bolo aktualizované na 5-znakový preview a tooltip
  „Odchodové návestidlo (5-znakové, plné SR)“.
- `MarkerSignal.axaml.cs`: pre legacy `4-aspect-departure` boli doplnené explicitné fallbacky a warning logy, aby
  renderer nikdy nekreslil neexistujúcu hornú žltú.
- Legacy fallbacky:
    - `Caution` → fallback na `Stop` + warning,
    - `SlowCaution` → fallback na `SlowProceed` + warning,
    - `SlowExpect40` → fallback na `SlowProceed` + warning.
- Doplnené a upravené testy `SignalSystemRegistryTests`, `SignalSystemRegistryRuntimeTests`, `SignalControllerTests` a
  `LayoutEditorRegressionTests`.

**Výsledok:**

- Nové odchodové návestidlá vložené z UI už používajú fyzicky správny 5-znakový SR profil.
- Legacy 4-znakové odchodové návestidlá už nepredstierajú hornú žltú lampu.
- `dotnet build TrackFlow.csproj -c Release`: OK.
- Cielené testy (`SignalSystemRegistryTests`, `SignalSystemRegistryRuntimeTests`, `SignalControllerTests`,
  `LayoutEditorRegressionTests`): `135/135` OK.

## 2026-05-11 (Návestidlá – oprava runtime renderovania SlowProceed / SlowCaution)
==============
**Oblasť:** Návestidlá, runtime renderovanie, marker `MarkerSignal`

**Zmena:** Opravené mapovanie runtime aspektov `SlowProceed` a `SlowCaution` na fyzické lampy v `MarkerSignal.axaml.cs`.

**Dôvod:** Pri `SlowProceed` sa v niektorých vetvách renderera rozsvecovala nesprávna horná žltá. Pri `SlowCaution` bolo
potrebné zabezpečiť korektné správanie podľa fyzických možností konkrétneho profilu.

**Riešenie:**

- `SlowProceed` už v rendereri nerozsvecuje hornú žltú.
- Pre plné profily sa `SlowProceed` vykresľuje ako zelená + dolná žltá.
- `SlowCaution` ostáva horná žltá + dolná žltá tam, kde profil fyzicky obe lampy má.
- Pre `2-aspect` predzvesť bol ponechaný bezpečný fallback bez hornej žltej pre `SlowProceed`, keďže profil nemá dolnú
  žltú.
- Pre legacy `4-aspect-departure` bol runtime rendering zosúladený s fyzickými limitmi profilu cez explicitné fallbacky
  a warningy.
- V tom istom súbore bol zároveň odstránený jeden analyzer warning pri nullable indexe blikajúceho svetla.

**Výsledok:**

- `SlowProceed` = zelená + dolná žltá.
- `SlowCaution` = horná žltá + dolná žltá (iba na kompatibilných profiloch).
- `MarkerSignal.axaml.cs`: editorová kontrola bez chýb.

## 2026-05-10 (ClockView – Neon/Dark dashboard štýl)
==============
**Oblasť:** UI, Fast Clock

**Zmena:** `ClockView` bol graficky prerobený do tmavého neónového štýlu podľa lokomotívneho dashboardu.

**Dôvod:** Predchádzajúci biely analógový ciferník vizuálne nezapadal do aplikácie a nepôsobil ako súčasť dashboardového
dizajnu.

**Riešenie:**

- Fixná kompaktná veľkosť okna `150x170`.
- Tmavé/čierne pozadie s jemným gradientom a transparentnými rohmi.
- Tmavý ciferník s radiálnym gradientom namiesto bieleho pozadia.
- Zelený neónový halo/glow efekt okolo ciferníka, čísiel, rysiek a ovládacieho panelu.
- Moderný bezpätkový font pre čísla `1–12`.
- Hodinová a minútová ručička majú strieborný ostrý vzhľad so zeleným glow podkladom.
- Sekundová ručička je tenká neónovo žltá s vlastným glow efektom.
- Pause/Play a koeficient zrýchlenia sú prerobené do tmavého dashboardového štýlu so zeleným ohraničením.
- Nevyhovujúca karta/položka `Zobraziť` bola odstránená z ribbonu; hodiny sa otvárajú cez viditeľné horné tlačidlo `🕒`.

**Výsledok:**

- Cielené testy `TimeServiceTests`: `3/3` OK.
- Plná sada testov: `411/411` OK.

## 2026-05-10 (Redizajn modelových hodín)
==============
**Oblasť:** UI, Fast Clock

**Zmena:** Okno modelových hodín bolo prerobené z digitálného panelu na kompaktné analógové hodiny.

**Dôvod:** Digitálny vzhľad bol príliš strohý, tlačidlo Pause splývalo s pozadím a nefunkčná/nevhodne vložená položka
`Zobraziť` v ribbone bola mätúca.

**Riešenie:**

- Odstránená nesprávne vložená karta/položka `Zobraziť` z `Ribbon/MainRibbonView.axaml`.
- `ClockView` má teraz analógový biely ciferník s čiernymi ryskami a číslami.
- Ručičky hodín/minút/sekúnd sa vykresľujú cez `Canvas` a súradnice z `ClockViewModel` podľa `CurrentModelTime`.
- Okno je kompaktné (`250x260`), bez klasických dekorácií, s priesvitným pozadím v rohoch a `Topmost="True"`.
- Tlačidlo Pause/Play je svetlé a kontrastné, koeficient zrýchlenia je zobrazený vedľa neho.
- Okno je možné ťahať myšou a zavrieť malým tlačidlom `×`.

**Výsledok:**

- Cielené testy `TimeServiceTests`: `3/3` OK.
- Plná sada testov: `411/411` OK.

## 2026-05-10 (Globálne modelové hodiny / Fast Clock)
==============
**Oblasť:** Simulátor, Nastavenia, UI, Prevádzka

**Zmena:** Pridané globálne modelové hodiny nad `SimulationSpeedFactor` ako systémová služba pre budúce cestovné
poriadky.

**Dôvod:** Simulačný koeficient už nemá zrýchľovať iba pohyb vlakov izolovane; aplikácia potrebuje jednotný modelový
čas, ktorý bude spoločný pre prevádzku, simulátor a plánovanie.

**Riešenie:**

- `TimeService`: singleton služba s `CurrentModelTime`, `SimulationSpeedFactor`, `Pause()`, `Resume()` a
  `TogglePause()`.
- `ClockView` + `ClockViewModel`: nové samostatné okno s veľkým digitálnym modelovým časom, tlačidlom Play/Pause a
  zobrazením aktuálneho koeficientu.
- Ribbon: pridaná karta **Zobraziť** s tlačidlami **Modelové hodiny** a **Doktor**.
- `SettingsViewModel`: zmena `SimulationSpeedFactor` okamžite aktualizuje globálne hodiny.
- `OperationViewModel.SimulateMoveHeartbeatAsync(...)`: časový krok `dtSec` je odvodený z progresu `TimeService`; pri
  pauze hodín simulačný engine nedostáva časový prírastok, takže pohyb stojí.
- Smerová logika v `ReserveNextBlock` a `ResolveSegmentStartSignal` nebola menená.

**Výsledok:**

- Cielené testy `TimeServiceTests`, `SettingsScalePersistenceTests` a dotknutý route ramp test: `19/19` OK.
- Plná sada testov: `411/411` OK.
- Editorová kontrola nových XAML súborov bez chýb.

## 2026-05-10 (Doktor diagnostika + zrýchlenie simulácie)
==============
**Oblasť:** Prevádzka, Doktor, Nastavenia, Simulátor

**Zmena:**

- Obnovené záznamy v okne Doktor pre reálne runtime rezervovanie blokov.
- Obnovené záznamy v okne Doktor pre „nahadzovanie“ návestidiel na voľnejšie/permisívne aspekty.
- Hláška zakázaného smeru jazdy v bloku už zobrazuje názov bloku (`Label`) namiesto interného ID.
- Pridané projektové nastavenie `SimulationSpeedFactor` pre zrýchlenie simulačného času pohybu vlakov.
- Stabilizovaná interná kolekcia udalostí Doktora pre paralelné testy/runtime zápisy.

**Dôvod:**

- Diagnostika v Doktorovi musí ukazovať reálne udalosti z aktívnych runtime ciest, nie iba staré/pomocné code pathy.
- Používateľské hlášky nemajú zobrazovať technické ID blokov, ak je dostupný čitateľný názov.
- Simulátor potrebuje nastaviteľný časový koeficient podobne ako zrýchlené hodiny v TrainControlleri.
- Paralelné zápisy do diagnostiky nesmú spôsobovať pády `ObservableCollection`.

**Riešenie:**

- `OperationViewModel.ReserveNextBlock(...)`: doplnený Doctor log `Blok <názov> REZERVOVANÝ` v reálnej rezervácií.
- `OperationViewModel.UpdateTraversalSignalWindowAsync(...)`: doplnený Doctor log pre segmentové nahodenie návestidla.
- `SignalController.ApplySignalAspectsForRouteAsync(...)`: doplnený Doctor log pre štartové návestidlo cesty aj bez live
  DCC odoslania.
- `OperationViewModel.ActivateRouteAsync(...)`: hláška „Smer jazdy v bloku ... nie je povolený“ používa
  `BlockDisplayName(...)`.
- `TrackFlowDoctorService`: `Events` kolekcia používa lockovaný thread-safe wrapper nad
  `ObservableCollection<DiagnosticEvent>`.
- `ProjectSettingsData`: pridané `SimulationSpeedFactor` s defaultom `3.0`, rozsahom `1.0–5.0` a normalizáciou
  neplatných hodnôt.
- `SettingsViewModel` + `SettingsWindow.axaml`: doplnené UI nastavenie na záložku **Mierka**; property je explicitná,
  aby ju spoľahlivo videl aj editor/XAML analyzátor.
- `OperationViewModel.SimulateMoveHeartbeatAsync(...)`: `dtSec` používa projektový `SimulationSpeedFactor` namiesto
  natvrdo zadaného koeficientu.
- Doplnené/regresne upravené testy pre Doctor diagnostiku, názvy blokov v hláškach, persistenciu `SimulationSpeedFactor`
  a nový defaultný rampovací krok.

**Výsledok:**

- Cielené testy `SettingsScalePersistenceTests` + dotknutý route ramp test: `16/16` OK.
- Plná sada testov: `408/408` OK.
- Editorová kontrola: `SettingsWindow.axaml` bez chýb; `SimulationSpeedFactor` už nie je závislý od source generatora.

## 2026-05-09 (Doktor – farebné ikonky začiatku/konca cesty)
==============
**Oblasť:** Doktor (diagnostika)

**Zmena:** Symbol pred „Začiatok cesty“ je zelený (▶), symbol pred „Koniec cesty“ je medovo-tmavo-žltý (■).

**Dôvod:** Rýchlejšia vizuálna orientácia v diagnostike.

**Riešenie:**

- `DiagnosticEvent`: odvodené vlastnosti `MessageIcon` / `MessageText` + flagy pre typ ikonky.
- `DoctorWindow.axaml`: `DataGridTemplateColumn` pre „Správa“, kde sa ikonka renderuje samostatne a štýluje cez CSS
  classes.

**Výsledok:** `dotnet test -c Debug`: `395/395` OK

## 2026-05-09 (Ribbon – ikonky Undo/Redo)
==============
**Oblasť:** Ribbon

**Zmena:** Undo/Redo v ribbone používa ikony `Assets\\Appicons\\32\\prev.png` (Undo) a
`Assets\\Appicons\\32\\next.png` (Redo) konzistentne.

**Dôvod:** Požiadavka na jednotné ikonky podľa existujúcich assetov.

**Riešenie:** `Ribbon/MainRibbonView.axaml` – nahradené vektorové šípky / text-only tlačidlá za `Image` s uvedenými PNG.

**Výsledok:** Ribbon Undo/Redo je vizuálne konzistentné a čitateľné.

## 2026-05-09 (Prevádzka/Simulátor – globálny Emergency Stop)
==============
**Oblasť:** Prevádzka/Simulátor

**Zmena:** STOP tlačidlo (Ribbon aj Dashboard) zastaví okamžite prevádzku v simulátore aj v live režime; v live režime
odošle E‑STOP aj do DCC centrály.

**Dôvod:** Núdzové zastavenie musí byť „globálne“ a spoľahlivo prerušiť aj prebiehajúce simulácie/pohybové tasky.

**Riešenie:**

- `OperationViewModel.EmergencyStopAsync(...)`: zastavenie všetkých lokomotív v modeli, deaktivácia ciest, návestidlá do
  STOJ + okamžité prerušenie pohybových simulácií cez panic cancellation token; v live režime aj DCC
  `EmergencyStopAsync` + speed=0 + push návestidiel.
- `MainWindowViewModel.Stop()`: fire-and-forget volanie globálneho E‑STOP.
- `LocoDashboardView.axaml.cs`: E‑STOP tlačidlo používa `StopCommand` (globálny STOP).

**Výsledok:** STOP reaguje okamžite; simulácie sa ukončia promptne a v live režime sa odošle bezpečnostný stav na
centrálu.

## 2026-05-09 (Layout Editor – Undo/Redo + Ctrl+Z/Ctrl+Y)
==============
**Oblasť:** Layout Editor

**Zmena:** Pridané Undo/Redo pre editáciu schémy (snapshot-based história) + globálne skratky `Ctrl+Z` / `Ctrl+Y` v UI.

**Dôvod:** Obnoviť plnohodnotnú prácu v editore bez rizika „nevratných“ úprav.

**Riešenie:**

- `LayoutEditorViewModel`: undo/redo stacky snapshotov layoutu, `CaptureUndoCheckpoint(...)`, `Undo()`, `Redo()`,
  `CanUndo/CanRedo` + event `UndoRedoStateChanged`.
- `LayoutEditorView.axaml.cs`: doplnené undo checkpointy pre view-driven operácie (drag-move, resize, draw-track).
- `MainWindowViewModel`: príkazy `UndoCommand` / `RedoCommand` napojené na editor + aktualizácia `CanExecute` po zmene
  režimu a po `UndoRedoStateChanged`.
- `MainWindow.axaml`: `Window.KeyBindings` pre `Ctrl+Z` → `UndoCommand` a `Ctrl+Y` → `RedoCommand`.

**Výsledok:** Undo/Redo funguje pre zásadné operácie editora; skratky sú dostupné priamo v aplikácii.

## 2026-05-09 (Editor – priradenie lokomotívy do bloku)
==============
**Oblasť:** Layout Editor – drag&drop priradenie lokomotívy do bloku

**Zmena:** Pri drag&drop umiestňovanie lokomotívy v Editore sa kolízna kontrola robí len na cieľovom bloku (nie aj na
susedných), aby sa lokomotíva korektne priradila.

**Dôvod:** V Editore ide o manuálne „stavebné“ umiestňovanie. Kontrola susedných blokov (bezpečnostná rezerva) je
vhodnejšia pre runtime jazdu v režime Prevádzka, ale v Editore spôsobovala, že lokomotíva sa niekedy nepriradila.

**Riešenie:** `LayoutEditorViewModel.AssignLocomotiveToBlock(...)` používa
`_collisionService.EvaluateEntry(..., safetyDistanceBlocks: 0)` (predtým `1`).

**Výsledok:** `dotnet test -c Debug`: `395/395` OK

## 2026-05-08 (Mierka simulácie & stabilizácia testov)
==============
**Oblasť:** Scale-aware simulácia pohybu lokomotív a stabilizácia route activation testov

**Zmena:** Simulácia pohybu už nepoužíva pevný deliteľ vzdialenosti, ale rešpektuje mierku projektu; testy pohybu majú
injektovateľné oneskorenie pre rýchle a deterministické spúšťanie

**Dôvod:**

- Odstrániť fixný `SimulationDistanceScale = 8.0` a naviazať prejdenú simulačnú vzdialenosť na reálnu mierku projektu
- Zachovať rýchle unit/integration testy aj po zapojení heartbeat simulácie pohybu
- Zúžiť príčinu timeoutu v `OperationViewModelRouteActivationTests` bez spoliehania sa na dlhé interaktívne PowerShell
  príkazy

**Riešenie:**

- `SimulationScaleResolver` sa používa pri spúšťaní pohybu na výpočet deliteľa podľa `EffectiveSettings.Scale`
- `LocomotiveSimulationEngine` používa `distanceScale`, takže prejdená vzdialenosť zodpovedá mierke layoutu namiesto
  pevnej konštanty
- Opravené viazanie `ComboBox` pre záložku **Mierka**: pôvodné položky `ComboBoxItem` boli nahradené `ItemsSource` zo
  string kolekcie `ScaleItems`, takže `SelectedItem` aj vlastnosť `Scale` majú rovnaký typ (`string`) a výber už
  nezostáva prázdny
- Doplnená normalizácia mierky pri načítaní aj ukladaní: prázdna alebo neplatná hodnota sa bezpečne zmení na `H0`; `HO`
  sa mapuje na `H0`
- V simulátore sa používateľské markery prepočítavajú pomerovo: `markerCm / lengthMm` sa aplikuje na virtuálnu dĺžku
  `SimBlockLengthMm = 2000.0`
- Ak blok nemá platnú `lengthMm`, alebo markery nie sú zadané, simulátor použije vlastné fallback markery: 60 % pre
  brzdenie a 90 % pre zastavenie
- `OperationViewModel` dostal testovací seam `movementDelayAsync`, predvolene `Task.Delay`, v route testoch nahradený
  `(_, _) => Task.CompletedTask`
- Route activation testy používajú spoločnú factory metódu `CreateOperationViewModel(...)`, ktorá nastavuje
  `SelectedLoco` a nulové oneskorenie pohybu
- Pridaný diagnostický skript `_isolate_route_tests.ps1` na neinteraktívne izolovanie pomalých alebo visiacich route
  testov

**Výsledok:**

- `SimulationScaleTests` a `SettingsScalePersistenceTests` prešli úspešne (`18/18`)
- `OperationViewModelRouteActivationTests` prešli úspešne (`36/36`), vrátane pôvodne visiaceho testu
  `MoveLocomotiveByRouteElementAsync_SpustiPresunPodlaVybratejCesty`
- Build `TrackFlow.Tests` bol overený ako úspešný; zostáva len existujúce upozornenie analyzátora `xUnit1025` na
  duplicitné `InlineData`

**Oblasť:** Návestidlá (markery) – nové typy

**Zmena:** Doplnené 2 nové varianty návestidiel do palety markerov:

- **Cestové**: červená + biela (profil `2-aspect-route`)
- **Vchodové (3-znakové)**: zelená + červená + biela (profil `3-aspect-entry`)

**Dôvod:** Potrebné doplniť ďalšie bežne používané typy návestidiel a mať ich priamo v ribbon páse pre rýchle vkladanie.

**Riešenie:**

- `SignalSystemRegistry` rozšírený o profily `2-aspect-route` a `3-aspect-entry` + doplnené pravidlá v
  `ResolveRuntimeAspect()`
- `MarkerSignal` doplnený o správne „prirodzené“ farby (preview) aj runtime mapovanie aspektov pre nové profily
- Ribbon: pridané nové tlačidlá `Signal2Route` a `Signal3Entry` + nové mini preview kontroly `MarkerSignal2Route` a
  `MarkerSignal3Entry`
- Editor: mapovanie `SelectedMarkerKey` → `SignalProfile` pri vkladaní signálu (
  `LayoutEditorViewModel.CreateSignalElement()`)
- Aktualizované render factory pre editor/operation/route preview, aby poznali aj nové marker keys (kvôli kompatibilite)
- Aktualizované testy `SignalSystemRegistryTests` a `SignalSystemRegistryRuntimeTests`

**Výsledok:**

- Nové návestidlá sú dostupné v palete a ukladajú sa ako štandardný `Signal` marker s príslušným `SignalProfile`
- Testy prešli úspešne (`dotnet test`: `387/387`)

**Oblasť:** Build hygiene (warnings)

**Zmena:** Upratané a stabilizované kompilátorové warnings; projekt teraz prejde čistým buildom bez warnings.

**Dôvod:**

- Zjednotiť CI/locálny build výstup (warnings „zabíjajú“ signál pri reálnych problémoch)
- Odstrániť zbytočné nullability/unused warnings v UI kóde
- Udržať kompatibilitu s aktuálnou Avaloniou 11.3.9 aj keď drag&drop API je označené ako obsolete

**Riešenie:**

- Opravené drobné nullability a analyzátorové prípady v UI (napr. bezpečnejšie casty pri `Pointer.Capture`, iterácia
  `ListBox.Items` cez `OfType<ListBoxItem>()`)
- Odstránené nepoužité polia (napr. `_draggedIndicator` v `BlockPropertiesWindow`)
- Geometria markerov: pridané null-forgiving `!` pre `PathFigure.Segments`/`PathGeometry.Figures` (CS8602)
- Drag&Drop (Avalonia): ponechané existujúce správanie cez `DragEventArgs.Data`/`DataObject`/`DragDrop.DoDragDrop(...)`,
  ale obsolete použitia sú izolované do jedného wrappera `TrackFlow\Helpers\DragDropCompat.cs`.
    - Jediné miesto s `#pragma warning disable CS0618` je `DragDropCompat`.
    - UI/behavior kód používa `DragDropCompat.Contains/Get/TryGet/DoDragDropAsync`, takže projekt ostáva warning-free.
- Poznámka/TODO: pri upgrade na Avalonia 12+ bude potrebná skutočná migrácia drag&drop na nový DataTransfer model (nie
  je drop-in zmena).

**Výsledok:**

- `dotnet clean; dotnet build -c Debug` bez warnings
- `dotnet test -c Debug`: `395/395` OK

## 2026-05-07 (Operation Mode & Simulator Switch)
==============
**Oblasť:** Globálny systém prepínania medzi Simulátorom (Trenažér) a Ostrou prevádzkou (Live)

**Zmena:** Implementácia režimov Simulation a Live s kompletnou DCC izoláciou a simuláciou senzorov

**Dôvod:**

- Umožniť testovanie logiky ciest a pohybu vlakov bez fyzickej DCC centrály (Trenažér)
- Oddeliť tréningový režim od ostrej prevádzky
- Simulovať senzory v trenažéri automaticky na základe matematickej simulácie
- Predísť konfliktom stavov pri prepínaní režimov

**Riešenie:**

- **OperationMode Property**:
    - `[ObservableProperty] private bool isSimulationMode = true;`
    - true = Simulátor (Trenažér bez centrály)
    - false = Live (Ostrá prevádzka s reálnou centrálou)

- **Odclonenie DCC vrstvy**:
    - `ActivateRouteAsync`: `var effectiveDccClient = IsSimulationMode ? null : dccClient;`
    - `UpdateTraversalSignalWindowAsync`: Podmienené DCC volania `if (!IsSimulationMode)`
    - `ApplyTailClearStateAsync`: Všetky `signalController.SendCurrentStateToCentral` len v Live režime
    - `SignalController`: V Simulation režime sa DCC príkazy nevolajú, ale model sa aktualizuje

- **Simulácia Senzorov (Kľúčová funkcia Trenažéra)**:
    - `SimulateMoveHeartbeatAsync`: Pridané parametre `isSimulationMode`, `layout`, `onSimulatedSensorOccupied`
    - Pri `boundaryEntryTriggered` v Simulation režime → automaticky `onSimulatedSensorOccupied(targetBlock.Id)`
    - Callback volá `OnBlockOccupiedAsync(layout, blockId, dccClient, ct, sendDcc: false)`
    - Diagnostika "🎮 SIMULOVANÝ SENZOR" pre monitoring
    - Fire-and-forget pattern pre simulované senzory

- **Live Sync Logic**:
    - V Live režime (`IsSimulationMode == false`) zostáva Real-time Sync (teleport) aktívny
    - Simulácia čaká na reálne senzory z DCC
    - Pri predčasnom fyzickom obsadení → teleport engine.CurrentDistanceMm

- **Bezpečnostný Reset pri prepnutí**:
    - `OnIsSimulationModeChanged` partial metóda (automaticky volaná CommunityToolkit.Mvvm)
    - Pri zmene režimu:
        1. Zastav všetky lokomotívy (TargetSpeed = 0, CurrentDisplaySpeed = 0)
        2. Zhoď všetky aktívne cesty (`DeactivateAllRoutes()`)
        3. Cleanup `_activeSimulations`
        4. Reset layout elementov (bloky → IsOccupied/IsLocked/IsShadowSet = false, signály → Red)
    - Diagnostika "🔄 Prepnutie režimu" + "✅ Režim aktivovaný"

**Výsledok:**

- Kompletné oddelenie Trenažéra od Ostrej prevádzky
- V Simulation režime: Žiadne DCC príkazy, simulované senzory, plná logika ciest
- V Live režime: Všetky DCC príkazy aktívne, reálne senzory, real-time sync
- Bezpečný switch medzi režimami bez konfliktu stavov
- Model sa aktualizuje v oboch režimoch identicky (len DCC vrstva je odclonená)

## 2026-05-07 (Fáza 3 - Real-time Sync & Safety)
==============
**Oblasť:** Real-time synchronizácia simulácie s fyzickými senzormi + bezpečnostné brzdenie

**Zmena:** Implementácia spätnej väzby z reálnych senzorov (DCC/S88) a inteligentného brzdenia

**Dôvod:**

- Zosúladiť simuláciu s realitou - senzor má prednosť pred matematickým modelom
- Umožniť plynulú jazdu (Flying Switch) bez brzdenia pri voľnej ceste
- Zabezpečiť bezpečné brzdenie pri nedostatočnej brzdnej dráhe
- Eliminácia vizuálnych "skokov" pri real-time korekcii polohy

**Riešenie:**

- **Úloha 1 - Real-time Korekcia (Teleport/Sync)**:
    - `ActiveSimulationContext` drží referenciu na aktívny engine pre každú lokomotívu
    - `OnBlockOccupiedAsync`: Ak reálny senzor nahlási obsadenie targetBlock pred simuláciou → teleport:
      `engine.CurrentDistanceMm = preEntryDistanceMm + 1`
    - Realita má prednosť - fyzický senzor korúge matematický model
    - Okamžitá UI synchronizácia cez `Dispatcher.UIThread.InvokeAsync` (Render priority)

- **Úloha 2 - Dynamický Flying Switch**:
    - V každom tiku heartbeat cyklu kontrola `IsNextBlockReservedForLoco()`
    - Ak sa podczas jazdy uvoľní/rezervuje ďalší blok → `requiresStop = false`
    - Vlak plynule prechádza hranicou blokov bez "cuknutia" brzdami

- **Úloha 3 - Inteligentné brzdenie (Safety Distance)**:
    - Výpočet brzdnej vzdialenosti: `CalculateBrakingDistanceMm(speedKmh, decelerationKmhPerSec)`
    - Ak brzdná dráha > zostávajúca vzdialenosť → **KRITICKÁ diagnostika + núdzové brzdenie na 0**
    - Normálne progresívne brzdenie od 70% dĺžky bloku

- **Úloha 4 - UI Cleanup**:
    - Všetky `LayoutRefreshRequested` volania cez `Dispatcher.UIThread.InvokeAsync` s `DispatcherPriority.Render`
    - Žiadne redundantné kontroly v cykle - clean event-driven flow
    - `finally` blok pre cleanup `_activeSimulations` pri ukončení segmentu

**Výsledok:**

- Reálne senzory majú prednosť pred simuláciou (teleport pri pre-early obsadení)
- Plynulá jazda (Flying Switch) funguje dynamicky - reaguje na zmeny rezervácie v reálnom čase
- Bezpečné brzdenie s KRITICKOU diagnostikou pri nebezpečných situáciách
- Žiadne vizuálne skoky pri real-time korekcii - monitor súhlasí s koľajiskom

## 2026-05-07 (Fáza 2 - Event-Driven Refactoring)
==============
**Oblasť:** Simulácia pohybu lokomotív - Event-Driven prístup

**Zmena:** Komplexná refaktorizácia `SimulateMoveHeartbeatAsync` na event-driven architektúru

**Dôvod:**

- Odstránenie vizuálneho "sekania" pri pohybe vlakov na monitore
- Eliminácia race conditions medzi UI refreshom a pohybovou logikou
- Presné triggery pri prekročení hraníc (boundary entry, tail clear)
- Oddelenie riadiacej logiky (návestidlá/rezervácie) od výpočtovej logiky

**Riešenie:**

- Jeden engine pre celý segment (pre-entry + target block) namiesto 2-3 separátnych
- Implementované **Boundary Triggers**:
    - **Boundary Entry Trigger**: Spúšťa sa pri prvom pohybe v cieľovom bloku (nie asynchrónne na začiatku)
    - **Tail Clear Trigger**: Fire-and-forget akcia pri prekročení hranice (cm presne)
- **Dynamická targetSpeed**: ViewModel reaguje na pozíciu v bloku (marker-based alebo braking-based)
- **UI Synchronizácia**: `Dispatcher.UIThread.InvokeAsync(..., DispatcherPriority.Render)` pre okamžitý refresh
- **Bezpečnostná kontrola**: `IsShadowSet` má prednosť pred vizuálnym aspektom návestidla
- Odstránené všetky prebytočné `Task.Delay` a "Approach" logika (teraz súčasť Enginu)
- ViewModel pôsobí ako "dispečer": hovorí Enginu cieľovú rýchlosť, reaguje na milimetrové hranice

**Výsledok:**

- Plynulý vizuálny pohyb bez "sekania" na monitore
- Engine počíta fyziku v pozadí, ViewModel reaguje iba na kľúčové hranice
- Precízne spúšťanie akcií (rezervácia, uvoľnenie) v správnych momentoch
- Čistejšia separácia: Engine = fyzika, ViewModel = dispečer

## 2026-05-07 (Fáza 1 - Simulation Engine)
==============
**Oblasť:** Simulácia pohybu lokomotív

**Zmena:** Refaktorizácia simulačného enginu - vytvorenie LocomotiveSimulationEngine

**Dôvod:** Oddelenie čistej matematickej simulácie od UI a DCC logiky. Eliminácia vizuálnych lagov a race conditions pri
pohybe vlakov.

**Riešenie:**

- Vytvorený čistý matematický engine `LocomotiveSimulationEngine` v `Services/Simulation/`
- Engine obsahuje iba fyzikálne výpočty (ramping, vzdialenosť, rýchlosť)
- Bez závislostí na SignalElement, BlockElement, DccClient
- Refaktorizácia `SimulateMoveHeartbeatAsync` do 3 fáz (pre-entry, in-block, final-stop)
- Pridaný `onLayoutRefresh` callback pre okamžitú aktualizáciu UI
- Odstránené duplicitné konštanty zo `SimulationDistanceScale` a `MmPerSecondPerKmh`

**Výsledok:**

- Čistý separovaný simulačný engine pripravený na ďalšie fázy (DCC vs Simulation split)
- Plynulejší pohyb vlakov s okamžitým UI refreshom
- Lepšia testovateľnosť simulačnej logiky
