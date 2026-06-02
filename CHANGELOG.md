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

> Pokračovanie zápisov po naplnení pôvodného `CHANGELOG.md`.

> Konvencia: **🟩** = položka z auditu / follow-upu je už opravená a zapracovaná v kóde.

## 2026-06-02 16:40
===================
**Oblasť:** `ViewModels/Editor/RoutesManagerViewModel.cs`, `Services/RouteMarkerAssignmentHelper.cs`, `Views/Editor/LayoutEditorView.axaml.cs`, `Views/Operation/OperationView.axaml.cs`, `ViewModels/Editor/LayoutEditorViewModel.cs`, `Views/MainWindow.axaml.cs`, `TrackFlow.Tests/RoutesManagerViewModelRouteMetadataTests.cs`
**Zmena:** Opravená synchronizácia markerov `Cesta` po vymazaní route zo zoznamu ciest. Ak marker ukazoval na zmazanú route, jeho `SelectedRouteDefinitionId` sa okamžite nuluje a šípka sa v UI prefarbí zo žltej na červenú bez čakania na ďalší manuálny refresh.
**Dôvod:** Po odstránení route v správcovi ciest zostávali niektoré route markery vizuálne „priradené" (žlté), hoci ich cieľová route už fyzicky neexistovala v `layout.Routes`.
**Riešenie:**
• Pridaný helper `RouteMarkerAssignmentHelper`:
  - `HasAssignedRoute(...)` overuje existenciu priradenej route podľa `SelectedRouteDefinitionId`.
  - `ClearInvalidAssignments(...)` prejde všetky `RouteElement` a neplatné priradenia resetne na `null`.
• `RoutesManagerViewModel.DeleteRoute()` po zmazaní route volá `ClearInvalidAssignments(...)` a pri náleze zneplatnených markerov vyvolá okamžitý refresh editora aj prevádzky.
• `LayoutEditorViewModel` dostal lightweight refresh event (`VisualRefreshRequested` + `RequestVisualRefresh()`), ktorý `LayoutEditorView` odoberá cez `ScheduleRebuild()`.
• Render route markerov v editore aj prevádzke bol sprísnený: farba sa už neriadi len `SelectedRouteDefinitionId != null`, ale reálnou existenciou route (`HasAssignedRoute(...)`).
• Pri otvorení správcu ciest z hlavného okna sa teraz do `RoutesManagerViewModel` odovzdávajú aj hlavné VM (`LayoutEditor`, `Operation`) pre okamžité prekreslenie po delete.
• Doplnený regresný test `DeleteRoute_ClearsAssignedRouteId_FromRouteMarkersReferencingDeletedRoute`.
**Výsledok:**
• Logika mazania route je konzistentná s vizuálnym stavom markerov (`žltá` len pre platné priradenie, inak `červená`).
• Editorová kontrola upravených súborov je bez nových compile chýb.
• Cielený test bol pripravený a spustený, ale build/test beh bol v prostredí blokovaný lockom `TrackFlow.dll` bežiacimi `.NET Host` procesmi.

## 2026-06-01 15:10
===================
**Oblasť:** `ViewModels/Operation/OperationViewModel.cs` (`ActivateRouteAsync`, nový helper `PreSwitchRouteTurnoutsAsync`)
**Zmena:** Pri aktivácii cesty z bloku do bloku sa **najprv prestavia VŠETKY výhybky v ceste do požadovanej polohy** a až potom sa rozsvietia návestidlá a spustí simulácia jazdy. Doteraz sa výhybky prepínali lazy – až tesne pred vstupom vlaku do daného segmentu vo `TryEnsureTurnoutsForSegmentAsync`, čo v demo režime vyzeralo, akoby sa výhybky v schéme „preklápali na poslednú chvíľu".
**Dôvod:** UX požiadavka používateľa – pri aktivácii cesty v demo režime má byť celá cesta okamžite vizuálne „postavená" (všetky výhybky v správnej polohe), nielen lockované bloky. Pôvodné správanie spôsobovalo, že schéma sa aktualizovala až postupne počas behu vlaku, čo pôsobilo neprehľadne.
**Riešenie:**
• V `ActivateRouteAsync` hneď po úspešnom `_routeActivationService.TryActivateAsync(...)` a `InitializeRouteRuntime(...)` / `DiagnoseRouteStarted(...)` (a pred `SetSignalsRedRespectingActiveRoutes` a `UpdateTraversalSignalWindowAsync`) sa volá nový helper `PreSwitchRouteTurnoutsAsync(layout, activationRoute, effectiveDccClient, ct)`.
• `PreSwitchRouteTurnoutsAsync` iteruje cez všetky segmenty `activationRoute.BlockIds[i] → BlockIds[i+1]` a pre každý zavolá existujúce `TryEnsureTurnoutsForSegmentAsync`, ktoré:
– nastaví `TurnoutElement.State = RouteTurnoutSetting.RequiredState` v modeli,
– zapíše vlastníctvo výhybky do `_turnoutRuntimeReservations[turnoutId] = route.Id`,
– v Live režime pošle `IDccCentralClient.SetTurnoutAsync(turnout.DccAddress, branch, activate: true, ct)`,
– po prvej skutočnej zmene polohy vyvolá `LayoutRefreshRequested?.Invoke()` → schéma sa prekreslí so všetkými novými polohami pred štartom jazdy.
• Per-segmentová kontrola v `TryEnsureTurnoutsForSegmentAsync` počas traverzu (line ~1632) zostala bez zmeny – ak je výhybka už v správnej polohe, len doplní diagnostiku „pripravená" a neposiela duplicitný DCC príkaz.
• Nižšia vrstva `RouteActivationService.TryActivateAsync` ostáva naďalej úmyselne „neeager" voči výhybkám (test `RouteActivationServiceTests.TryActivateAsync_PriAktivaciiNehybeVyhybkouEager` zostáva platný) – nové eager pre-switching žije iba v UX vrstve `OperationViewModel`.
**Výsledok:**
• Build `TrackFlow.Tests.csproj` → **0 warnings / 0 errors** po `kill-trackflow-locks.ps1` (lock bol len kvôli bežiacej aplikácii, nie kompilačný error).
• V demo režime sa po stlačení aktivácie cesty najprv okamžite prekreslia všetky výhybky na trase do požadovanej polohy a až potom sa rozsvietia návestidlá a vlak sa rozbehne.
• Žiadna zmena správania `RouteActivationService`, `RouteConflictDetector`, `ReservationEngine` ani DCC vrstvy – v Live režime sa pre každú výhybku stále pošle práve jeden `SetTurnoutAsync` (buď pri aktivácii, alebo per-segment, ale nie obidvakrát, lebo druhé volanie nájde turnout už v cieľovej polohe).


## 2026-06-01 15:05
===================
  **Oblasť:** `Views/Operation/OperationView.axaml.cs`
  **Zmena:** Zjednotené zobrazenie a priradenie lokomotívy do bloku medzi editovacím a prevádzkovým režimom – v prevádzke sa rušeň teraz vždy vykreslí, keď je k bloku priradený, a do bloku ho je možné priradiť drag&drop priamo z páska lokomotív (rovnako ako v editore).
  **Dôvod:** V prevádzkovom režime sa vyskytli dve prepojené chyby okolo väzby lokomotíva↔blok:
   1. Ak bola lokomotíva priradená do bloku v editovacom režime, po prepnutí do prevádzky v bloku nebola vidieť – render vyžadoval `IsOccupied=true`, ktoré ale safety/simulation pass po štarte prevádzky zhodil, hoci `AssignedLocoId` zostalo platné.
   2. V prevádzkovom režime sa lokomotíva vôbec nedala priradiť do bloku – canvas nemal vlastné drag&drop handlery na drop lokomotívy a aj keby bolo priradenie vykonané, vykresľovanie by ho beztak nezobrazilo (drop v prevádzke úmyselne nenastavuje `IsOccupied`, lebo obsadenie má potvrdiť až senzor/centrála).
  **Riešenie:**
   • V `OperationView.axaml.cs` bol pridaný pomocný `ResolveRenderableBlockLocoId(BlockElement block)`, ktorý vracia `block.AssignedLocoId` bez podmienky `IsOccupied`; logika "transition shadow" cez porovnanie `loco.AssignedBlockId` vs. `block.Id` ostáva zachovaná a "reserved shadow" sa naďalej zobrazuje cez `ReservedLocoId`. Tým sa render v prevádzke zarovnal s editorom a vyriešila chyba (1).
   • Hlavná render vetva bloku (`if (DataContext is OperationViewModel vm && el is BlockElement blockElAssign)`) bola prepísaná tak, aby používala výsledok `ResolveRenderableBlockLocoId(...)` namiesto priameho `IsOccupied + AssignedLocoId` kontrolu, čím sa stav lokomotívy medzi režimami už nerozpája.
   • Pridaná kompletná drag&drop infraštruktúra pre prevádzkový canvas (rovnaká ako v editore): konštanta `LocoFormat = "trackflow/locomotive"`, handlery `OnCanvasLocoDragOver` / `OnCanvasLocoDragLeave` / `OnCanvasLocoDrop`, helpre `FindBlockElementAt(...)`, `ComputeDropDirection(...)`, `ShowDragArrow(...)`, `HideDragArrow()` a floating-arrow indikátory smeru. Vlastný drop volá zdieľané `vm.AssignLocomotiveToBlockAsync(loco.Code, block.Id, isForward, dccClient)` a pri úspechu lokomotívu automaticky aktivuje (push do `SmartStrips.ActiveLocomotives`) – tým sa vyriešila chyba (2).
   • Drop handler je obalený do `try/catch` s `Program.ReportUnhandledException("OperationView.OnCanvasLocoDrop", ...)` a `TrackFlowDoctorService` warningom, aby výnimky pri priradzovaní v prevádzke nešli na UI dispatcher.
  **Výsledok:**
   • Manuálne overené: lokomotíva priradená v editore zostáva po prepnutí do prevádzky vizuálne v bloku; nová lokomotíva sa dá v prevádzkovom režime priradiť do bloku drag&drop-om priamo zo smart-strip-u.
   • Render aj priradenie sú teraz funkčne identické v oboch režimoch (zdieľané `BlockTrainRenderer` + zdieľané `AssignLocomotiveToBlockAsync`), takže odpadlo rozpájanie stavu medzi editor a prevádzku.


## 2026-06-01 14:28
===================
  **Oblasť:** `Services/Dcc/IDccCentralClient.cs`, `Services/Dcc/Z21Client.cs`, `Services/SignalController.cs`, `ViewModels/Operation/OperationViewModel.cs`, `TrackFlow.Tests/*`
  **Zmena:** Vyššie hodnotený auditový batch pre 4.7 – `SetTurnoutAsync` bol rozšírený o explicitný parameter `branch` a Z21 packet už skladá výhybkový bit-pattern z výberu výstupu a `activate` bitu nezávisle.
  **Dôvod:** Audit 4.7 upozornil, že pôvodné API miešalo „výber výstupu“ a „activate“ do jedného booleanu, takže Z21 klient nevedel odlíšiť `(activate=1,out=0)` od `(activate=1,out=1)` a prakticky neumožňoval korektne vybrať druhú vetvu výhybky bez zneužitia semantiky `activate`.
  **Riešenie:**
   • 🟩 (audit 4.7) rozhranie `IDccCentralClient` bolo zmenené z `SetTurnoutAsync(int address, bool activate, ...)` na `SetTurnoutAsync(int address, bool branch, bool activate, ...)`, kde `branch=false` znamená výstup 0 / priamo a `branch=true` výstup 1 / odbočka.
   • Default fallback v `SetExtendedAccessoryAspectAsync(...)` teraz volá `SetTurnoutAsync(address, aspectNumber > 0, activate: true, ct)`, takže jasne odlišuje výber výstupu od energizácie.
   • `Z21Client` už nepočíta `Data` bajt cez konfliktný zápis `0x09/0x08`, ale explicitne skladá `bit3` z `activate` a `bit0` z `branch`: `byte data = (byte)((activate ? 0x08 : 0x00) | (branch ? 0x01 : 0x00));`.
   • `OperationViewModel` pri segmentovom prestavení výhybiek používa nový helper `RouteActivationService.MapTurnoutStateToBranch(...)` a fyzické prestavenie posiela ako `SetTurnoutAsync(..., branch, activate: true, ...)`.
   • `SignalController` bol narovnaný rovnako: helper `MapPeliAspectToTurnout(...)` už vracia výber vetvy (`branch`) namiesto starej pseudo-semantiky `activate` a basic-mode signály posielajú turnout command s `activate: true` a správnym výstupom.
   • Zosúladené boli všetky stub/fake klienti v testoch a pridané regresie: source-shape test pre nové API + Z21 bit-pattern a behavior testy pre signal-safety/basic-mode DCC send.
  **Výsledok:**
   • Cielený rez `SignalControllerTests|OperationViewModelRouteActivationTests|OperationViewModelSignalSafetyTests|OperationRuntimeSafetyServiceTests|LocomotiveSpeedEditorMarkupTests|DccConnectionServiceTests|DccCommunicationTestHandlerTests|LocomotiveAddressProgrammingTests` → **287 / 287 passed, 0 regresií**.
   • Rozšírený rez `SignalControllerTests|OperationViewModelRouteActivationTests|OperationViewModelSignalSafetyTests|OperationRuntimeSafetyServiceTests|LocomotiveSpeedEditorMarkupTests|DccConnectionServiceTests|DccCommunicationTestHandlerTests|LocomotiveAddressProgrammingTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` (`--no-build`) → **298 / 298 passed, 0 regresií**.


## 2026-06-01 14:13
===================
  **Oblasť:** `Services/Dcc/IDccProgrammingClient.cs`, `Services/Dcc/Z21Client.cs`, `Services/Dcc/SerialDccClient.cs`, `Views/Library/LocomotivesWindow.axaml.cs`, `ViewModels/Library/LocomotivesWindowViewModel.cs`, `TrackFlow.Tests/*`
  **Zmena:** Vyššie hodnotený auditový batch pre 4.6 – odstránený rizikový default `locoAddress = 3` z CV programming API a service-track cesty už používajú explicitný neutrálny placeholder namiesto magickej adresy 3.
  **Dôvod:** Audit 4.6 upozornil, že default `locoAddress = 3` je pri POM tichý bug: ak volajúci zabudne dodať adresu, zápis alebo čítanie ide na adresu 3. To je bezpečnostne nepríjemné najmä pri reálnom dekodéri na hlavnej trati. Zároveň service-track cesty v UI/viewmodeli zbytočne šírili tú istú „magickú trojku“, hoci sa tam parameter vôbec nepoužíva.
  **Riešenie:**
   • 🟩 (audit 4.6) z `IDccProgrammingClient`, `Z21Client` aj `SerialDccClient` bol odstránený default parameter `int locoAddress = 3`; POM volania tak teraz musia adresu odovzdať explicitne.
   • V `LocomotivesWindow.axaml.cs` bol service-track read prepnutý z fallbacku `GetSelectedLocomotiveAddressForPom() ?? 3` na explicitný `const int serviceTrackAddressPlaceholder = 0`.
   • Rovnaké narovnanie prebehlo aj v `LocomotivesWindowViewModel` pri service-track `ReadProgrammingCvAsync(...)` a `WriteProgrammingCvAsync(...)`, aby sa interné adresové programovanie neopieralo o magickú trojku.
   • Fake klienti v `LocomotiveAddressProgrammingTests` a `DccCommunicationTestHandlerTests` boli zosúladení s novou signatúrou bez defaultu; `SerialDccClientTests` teraz posielajú explicitný placeholder `0` pre service-track a explicitnú adresu pre POM negatívny test.
   • Pridané regresie: source-shape test, že API už neobsahuje `int locoAddress = 3`, a behavior testy, že service-track address-programming vo viewmodeli skutočne posiela `0` namiesto fallbacku 3.
  **Výsledok:**
   • Cielený rez `LocomotiveAddressProgrammingTests|SerialDccClientTests|DccCommunicationTestHandlerTests|LocomotiveSpeedEditorMarkupTests` → **158 / 158 passed, 0 regresií**.
   • Rozšírený rez `LocomotiveAddressProgrammingTests|SerialDccClientTests|DccCommunicationTestHandlerTests|LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` (`--no-build`) → **169 / 169 passed, 0 regresií**.


## 2026-06-01 13:57
===================
  **Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `Views/Library/LocomotivesWindow.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
  **Zmena:** Dôležitejší auditový batch pre 4.5 – kritické UI handlery v `VehicleStripItem` a `LocomotivesWindow` už nepoužívajú hlavnú implementáciu priamo ako `async void`, ale len tenké wrappery nad internými `Task` metódami.
  **Dôvod:** Audit 4.5 upozorňuje na riziko `async void` handlerov, kde výnimka mimo lokálne ošetrených vetiev môže prejsť na UI dispatcher. V `VehicleStripItem` síce drag vetvy už mali vlastné `try/catch`, ale celý pointer-move flow ešte nemal spoločný horný guard. V `LocomotivesWindow` zas zostávali tri kľúčové click handlery (`ReadCvButton_Click`, `OpenCalibrationWindow_Click`, `OpenProgrammingTrackSettings_Click`) stále definované priamo ako `async void`.
  **Riešenie:**
   • 🟩 (audit 4.5) event handler `OnPointerMoved(object? _, PointerEventArgs e)` bol zredukovaný na synchronný wrapper `_ = OnPointerMovedAsync(e);`.
   • Pôvodná logika bola presunutá do novej internej metódy `private async Task OnPointerMovedAsync(PointerEventArgs e)`.
   • Celý pointer-move drag flow teraz obopína vrchný `try/catch`, ktorý pri neočakávanej chybe vyresetuje drag stav (`_isPointerDown`, `_pendingWagon`, `_pendingLoco`, `_dragStarted`), označí event ako handled a zapíše diagnostiku cez `Program.ReportUnhandledException("VehicleStripItem.OnPointerMoved", ...)` + `TrackFlowDoctorService`.
   • V `LocomotivesWindow` boli `ReadCvButton_Click`, `OpenCalibrationWindow_Click` a `OpenProgrammingTrackSettings_Click` zmenené na synchronné event wrappery, ktoré delegujú prácu do `ReadCvButton_ClickAsync()`, `OpenCalibrationWindow_ClickAsync()` a `OpenProgrammingTrackSettings_ClickAsync()`, pričom existujúci exception-reporting ostal zachovaný.
   • `LocomotiveSpeedEditorMarkupTests` boli aktualizované o regresie pre oba vzory: `VehicleStripItem` wrapper s vrchným exception-reportingom aj `LocomotivesWindow` click handlery delegované na `Task` metódy namiesto priameho `async void`.
  **Výsledok:**
   • Cielené testy `LocomotiveSpeedEditorMarkupTests` → **113 / 113 passed, 0 regresií**.
   • Rozšírený rez `LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` (`--no-build`) → **124 / 124 passed, 0 regresií**.


## 2026-06-01 13:38
===================
  **Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
  **Zmena:** Menší skupinový cleanup `VehicleStripItem` – zmodernizované null-handling vetvy v `OnDrop(...)` a `OpenRenameMenuAsync()`.
  **Dôvod:** V týchto dvoch metódach ostalo viac podobných jednoduchých null checkov (`!= null`, explicitný typ v `is TextBox ntb`), ktoré bolo možné bezpečne zjednotiť do modernejšieho null-safe zápisu bez zmeny správania.
  **Riešenie:**
   • 🟨 (audit 4.5, priebežný krok) v `OnDrop(...)` boli nulové kontroly indikátorov zmenené z `if (left != null)` / `if (right != null)` na `if (left is not null)` / `if (right is not null)`.
   • V `OpenRenameMenuAsync()` bol rename textbox pattern zmenený z `is TextBox ntb` na `is { } ntb` a pre projektovú lokomotívu bol zápis `if (pLoco != null)` zjednotený na `if (pLoco is not null)`.
   • `LocomotiveSpeedEditorMarkupTests` boli doplnené o jeden spoločný regresný test pre celý null-handling batch; po prvom behu bolo potrebné negatívnu aserciu spresniť, aby sa nevzťahovala aj na iný handler (`OnDragLeave(...)`) s podobným starším snippetom.
  **Výsledok:**
   • Po spresnení regresného testu cielené testy `LocomotiveSpeedEditorMarkupTests` → **111 / 111 passed, 0 regresií**.
   • Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
   • Rozšírený rez `LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **122 / 122 passed, 0 regresií**.


## 2026-06-01 13:30
===================
  **Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
  **Zmena:** Menší skupinový cleanup `VehicleStripItem` v detach oblasti – zjednodušené bounds/fallback vetvy a zjednotená práca s lokomotívou v `DetachSpecificWagon(...)`.
  **Dôvod:** V detach handleroch zostalo viac malých redundancií rovnakého typu: nemožný `idx < 0` check v `OnDetachFirstWagonMenuClick(...)`, zbytočný qualifier `this.DataContext` a opakované používanie `_currentLoco` v `DetachSpecificWagon(...)` namiesto lokálneho aliasu. Spolu tvorili dobrého kandidáta na malý spoločný batch bez zmeny správania.
  **Riešenie:**
   • 🟨 (audit 4.5, priebežný krok) v `OnDetachFirstWagonMenuClick(...)` bol bounds check zjednodušený z `if (idx < 0 || idx >= loco.AttachedWagons.Count) return;` na `if (idx >= loco.AttachedWagons.Count) return;`, keďže záporný index tam nemôže vzniknúť.
   • V `HandleDetachThisWagonMenuClick(...)` sa posledný fallback zmenil z `wagon = this.DataContext as Wagon;` na stručnejší ekvivalent `wagon = DataContext as Wagon;`.
   • `DetachSpecificWagon(...)` teraz používa lokálny alias `var loco = _currentLoco;` a zvyšok metódy už nepracuje opakovane s fieldom `_currentLoco`.
   • `LocomotiveSpeedEditorMarkupTests` boli doplnené o jeden spoločný regresný test, ktorý ukotvuje celý tento detach batch naraz.
  **Výsledok:**
   • Cielené testy `LocomotiveSpeedEditorMarkupTests` → **109 / 109 passed, 0 regresií**.
   • Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
   • Rozšírený rez `LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **120 / 120 passed, 0 regresií**.


## 2026-06-01 13:26
===================
  **Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
  **Zmena:** Väčší nízkorizikový batch pre `VehicleStripItem` – zjednodušený cleanup depa po drag-u a zjednotený resolve lokomotívy/vagóna v detach handleroch.
  **Dôvod:** V `VehicleStripItem` zostalo viac malých redundancií rovnakého typu: zbytočný `Contains(...)` pred `Remove(...)`, opakované čítanie `_currentLoco` v `OnDetachFirstWagonMenuClick(...)` a zbytočne rozvinutá inicializácia `wagon` v `HandleDetachThisWagonMenuClick(...)`. Všetky tri zmeny boli čisté, lokálne a behavior-preserving, preto dávali zmysel ako jeden spoločný batch.
  **Riešenie:**
   • 🟨 (audit 4.5, priebežný krok) v wagon-drag cleanup vetve `OnPointerMoved(...)` bol odstránený zbytočný precheck `if (svm.DepotWagons.Contains(wagon))` a ostalo len priame `svm.DepotWagons.Remove(wagon);`, ktoré je samo o sebe bezpečné no-op pri chýbajúcej položke.
   • `OnDetachFirstWagonMenuClick(...)` teraz používa jednorazovo vyhodnotený lokálny alias `var loco = _currentLoco ??= DataContext as Locomotive;`, takže ďalšie riadky už nepracujú opakovane s `_currentLoco`.
   • V `HandleDetachThisWagonMenuClick(...)` bola primárna inicializácia vagóna skrátená z bloku `Wagon? wagon = null; if (mi != null) ...` na `Wagon? wagon = mi?.DataContext as Wagon;`.
   • `LocomotiveSpeedEditorMarkupTests` boli doplnené o jeden spoločný regresný test, ktorý ukotvuje celý tento batch naraz.
  **Výsledok:**
   • Cielené testy `LocomotiveSpeedEditorMarkupTests` → **109 / 109 passed, 0 regresií**.
   • Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
   • Rozšírený rez `LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **120 / 120 passed, 0 regresií**.


## 2026-06-01 13:21
===================
  **Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
  **Zmena:** Väčší nízkorizikový batch pre `VehicleStripItem` – zjednotené nepoužívané parametre viacerých handlerov na discard-like názvy.
  **Dôvod:** Po sérii malých clean-upov zostalo vo viacerých event handleroch množstvo nepoužívaných parametrov `sender` / `e`, ktoré zbytočne vytvárali analyzer šum. Keďže ide o čisto signatúrny zápis bez zmeny typu parametrov alebo wiring-u, bolo efektívnejšie upraviť ich naraz v jednom batchi než po jednom riadku.
  **Riešenie:**
   • 🟨 (audit 4.5, priebežný krok) v `VehicleStripItem` boli nepoužívané parametre viacerých handlerov premenované na discard-like názvy (`_`, `__`) – napr. `OnFlipOrientationMenuClick`, `OnDragOver`, `OnDragLeave`, `OnDataContextChanged`, `OnAttachedToVisualTree`, `OnDetachedFromVisualTree`, `OnPointerPressed`, `OnPointerMoved`, `OnPointerReleased`, `OnDrop`, `OnRenameMenuClick`, `OnDetachFirstWagonMenuClick` a druhý parameter v `HandleDetachThisWagonMenuClick`.
   • XAML/event wiring aj runtime správanie ostali nezmenené; zmenili sa len mená nepoužívaných parametrov.
   • Existujúci lifecycle test bol zosúladený s novými signatúrami a bol pridaný nový regresný test, že UI handlery so zbytočnými parametrami používajú nový discard-like zápis.
  **Výsledok:**
   • Cielené testy `LocomotiveSpeedEditorMarkupTests` → **108 / 108 passed, 0 regresií**.
   • Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
   • Rozšírený rez `LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **119 / 119 passed, 0 regresií**.


## 2026-06-01 13:13
===================
  **Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
  **Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránený redundantný `e == null` guard z `Locomotive_PropertyChanged(...)`.
  **Dôvod:** Handler `Locomotive_PropertyChanged(object? sender, PropertyChangedEventArgs e)` prijíma nenulovateľný parameter `e`, takže podmienka `if (e == null) return;` bola len zvyšková obrana bez reálneho prínosu. Išlo o čisto interný cleanup bez zásahu do správania property-change vetiev.
  **Riešenie:**
   • 🟨 (audit 4.5, priebežný krok) z `Locomotive_PropertyChanged(...)` bol odstránený riadok `if (e == null) return;`.
   • Vetvy pre `nameof(Locomotive.IsActive)` a `nameof(Locomotive.IsFlipped)` ostali nezmenené, rovnako ako refresh `DisplayName` pri zmene identity rušňa.
   • `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že handler už neobsahuje redundantný null guard, ale stále obsahuje vetvy pre `IsActive` a `IsFlipped`.
  **Výsledok:**
   • Cielené testy `LocomotiveSpeedEditorMarkupTests` → **107 / 107 passed, 0 regresií**.
   • Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
   • Rozšírený rez `LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **118 / 118 passed, 0 regresií**.


## 2026-06-01 13:08
===================
  **Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
  **Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránené redundantné default inicializácie drag-state fieldov.
  **Dôvod:** Viaceré privátne fieldy pre interný drag stav mali explicitné inicializácie na predvolené hodnoty (`false` / `null`), hoci tieto hodnoty CLR nastavuje automaticky. Išlo o čisto syntaktickú redundantnosť bez vplyvu na runtime správanie.
  **Riešenie:**
   • 🟨 (audit 4.5, priebežný krok) z `VehicleStripItem` boli odstránené explicitné inicializácie `= false` a `= null` z fieldov `_isPointerDown`, `_dragStarted`, `_pendingWagon` a `_pendingLoco`.
   • Typy aj význam fieldov ostali nezmenené; zmenil sa len zápis deklarácií.
   • `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že drag-state fieldy už nepoužívajú redundantné default inicializácie, ale ich deklarácie zostali zachované.
  **Výsledok:**
   • Cielené testy `LocomotiveSpeedEditorMarkupTests` → **106 / 106 passed, 0 regresií**.
   • Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
   • Rozšírený rez `LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **117 / 117 passed, 0 regresií**.



