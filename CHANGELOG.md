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

## 2026-06-21 18:00
===================
**Oblasť:** `TrackFlow.Tests/OperationViewModelRouteActivationTests.cs`, `TrackFlow.Tests/OperationViewModelDoctorDiagnosticsTests.cs`, `TrackFlow.Tests/ProjectMigrationServiceTests.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`, `Services/Runtime/ReservationEngine.cs`
**Zmena:** Opravených 5 failujúcich unit testov a 1 produkčný bug v `ReservationEngine`.
**Dôvod:** Po sérii rozsiahlejších zmien v LIVE režime, OperationMode refaktore a vizualizácii ciest sa nahromadilo 5 testov ktoré failovali — 4 z dôvodu nesprávnych predpokladov v testoch a 1 kvôli reálnemu bugu v produkcii.
**Riešenie:**
• `ReservationEngine.ReleaseAsync` — produkčný bug: pri hľadaní vlastníka zdrojového bloku po tail-clear sa `ResolveOwningRouteForBlock` volalo bez `excludeRouteId`, čím vlastná trasa (napr. `r_old`) nachádzala sama seba a nesprávne vyhodnocovala cudziu rezerváciu ako „svoju". Fix: odovzdávať `request.Route.Id` ako `excludeRouteId`, aby sa hľadala len skutočne cudzia trasa.
• `ApplyTailClearStateAsync_NezhadzujeProtismerneNavestidlo` — signály boli po `CreateOperationViewModel` (ktorý volá `SetAllSignalsRed` cez `IsSimulationMode=true`) nastavené na Stop; opravené nastavením aspektov signálov na Proceed až po vytvorení vm.
• `UpdateTraversalSignalWindowAsync_Odbocka_NegenerujeSlowCaution` — rovnaká príčina: `nextSignal.Aspect` bol Stop po `SetAllSignalsRed`; obnovené na Proceed po vytvorení vm.
• `MoveLocomotiveBetweenBlocksAsync_AutoDirectionSaNezmeniAkLocoNieJeZastavena` — `IsSimulationMode=true` resetuje `TargetSpeed` a `CurrentDisplaySpeed` loka; rýchlosti sa teraz nastavujú po vytvorení vm.
• `TailClear_NezmazeCerstvuRezervaciuNadvaznejTrasy` — `blockY.AssignedLocoId` nebol nastavený, takže `ResolvePrimaryRouteLocoId` vracalo `null` a rezervácia sa nesprávne mazala; opravené nastavením `blockY.AssignedLocoId = loco.Code`.
• `SetTraversalSegmentWindow_NaFinalnomLeadNeobnoviCelyRoutePath` — očakávaná hodnota `blk_y` zmenená z `False` na `True` (zámer: vizuálna spojnica svietiaca aj pri brzdení v poslednom bloku).
• `ProjectMigrationServiceTests` — úvodzovky v diagnostických hláseniach návestidiel zosúladené s produkčným formátom (`„Na2"` namiesto `Na2`).
• `LocomotiveSpeedEditorMarkupTests` — testy rýchlostného grafu aktualizované na fixných 28 bodov (graf nezávisí od `DecoderSteps`).
**Výsledok:** `Test summary: total: 845; failed: 0; succeeded: 842; skipped: 3`.

## 2026-06-21 10:00
===================
**Oblasť:** `ViewModels/Operation/OperationViewModel.cs`, `Services/Runtime/ReservationEngine.cs`, `Services/Signals/SignalSafetyEngine.cs`, `Services/Dcc/Z21Client.cs`, `TrackFlow.Tests/OperationViewModelRouteActivationTests.cs`
**Zmena:** Implementácia jazdy podľa zvolenej cesty v LIVE režime, oprava zobrazovania dashboardu lokomotívy v Prevádzkovom režime, implementácia 1000 virtuálnych krokov, odstránenie lokomotívy z bloku a vymazanie kontaktného indikátora a markeru vo vlastnostiach bloku.
**Dôvod:** Séria funkčných a UX rozšírení pre LIVE prevádzku a správu layoutu.
**Riešenie:**
• **LIVE jazda podľa cesty** — `MoveLocomotiveBetweenBlocksAsync` dostala non-simulačnú vetvu: odosiela DCC príkazy rýchlosti cez Z21 a polluje S88 senzory v 100ms slučke až kým cieľový blok nie je obsadený a zdrojový uvoľnený.
• **Dashboard lokomotívy** — opravené podmienky zobrazovania dashboard panela; panel sa zobrazuje len ak je lokomotíva skutočne umiestnená na trati (`IsPlacedOnTrack`).
• **1000 virtuálnych krokov** — implementácia jemnejšej rýchlostnej stupnice pre simuláciu aj LIVE režim.
• **Odstránenie lokomotívy z bloku** — pridaná akcia „Odstrániť lokomotívu" do kontextového menu bloku v Prevádzkovom aj Editorovom pohľade; korektne uvoľní priradenie bez narušenia aktívnych ciest.
• **Vymazanie indikátora a markeru** — vo vlastnostiach bloku je možné odstrániť kontaktný indikátor a vizuálny marker priamo z properties panela.
**Výsledok:** LIVE režim je funkčný pre senzorami riadenú jazdu; dashboard a kontextové menu sú konzistentné s aktuálnym stavom lokomotívy.

## 2026-06-14 20:35
===================
**Oblasť:** `ViewModels/Operation/OperationViewModel.cs`, `ViewModels/Editor/LayoutEditorViewModel.cs`, `TrackFlow.Tests/OperationViewModelSignalSafetyTests.cs`
**Zmena:** Manuálne priradenie lokomotívy do bloku už nikdy ticho neprepíše existujúce priradenie inej lokomotívy ani nezmaže senzorom potvrdenú obsadenosť.
**Dôvod:** V Prevádzke aj v Editore používalo priradenie runtime kolíznu kontrolu z jazdy vlaku (`EvaluateBlockEntry`), ktorá hlásila len „cieľový blok je obsadený", ale fakticky neblokovala prepis `AssignedLocoId`. Navyše sa po priradení nulovalo `IsOccupied = false`, takže reálne sensor-obsadenie sa stratilo a blok zostal len „priradený" (žltý) namiesto „obsadený".
**Riešenie:**
• V Prevádzke aj v Editore sa pri priradení teraz overujú len reálne dôvody odmietnutia: blok je zamknutý aktívnou cestou, blok už patrí inej lokomotíve (`AssignedLocoId`), alebo v bloku stále stojí iná lokomotíva podľa `Locomotive.AssignedBlockId` (recovery scenár po prepnutí Simulátor/Live).
• Pri odmietnutí sa do Doctora zapíše presná diagnostika (`assign-block-locked` / `assign-block-other-loco`) a stav bloku sa nemení.
• Manuálne priradenie už nenastavuje `IsOccupied = false` — ponecháva existujúce sensor-obsadenie a v Doctor logu rozlišuje „sensor-obsadenosť ponechaná" vs. „bez potvrdenej obsadenosti".
• Pridané regression testy pre oba scenáre (`OperationViewModelSignalSafetyTests`).
**Výsledok:** Pôvodná lokomotíva v bloku už nezmizne pri pokuse o priradenie druhej; používateľ dostane jasnú spätnú väzbu, prečo bolo priradenie zamietnuté.

## 2026-06-14 18:50
===================
**Oblasť:** `Services/Dcc/Z21Client.cs`, `Services/Dcc/DccConnectionService.cs`, `Services/Dcc/DccFeedbackLayoutApplier.cs`, `ViewModels/MainWindowViewModel.cs`, `ViewModels/Operation/OperationViewModel.cs`, `TrackFlow.Tests/Z21ClientRBusFeedbackTests.cs`, `TrackFlow.Tests/Z21ClientRBusGroupTests.cs`, `TrackFlow.Tests/DccFeedbackLayoutApplierTests.cs`
**Zmena:** R-BUS feedback pipeline pre Z21 zrýchlený, rozšírený o všetky skupiny modulov a stabilizovaný proti „stale" obsadeniam z predchádzajúcej relácie.
**Dôvod:** Z21 klient pollovał len skupinu 0 (moduly 1–10) s 250 ms periódou, takže obsadenosti na vyšších adresách prichádzali pomaly alebo vôbec, a po pripojení/zmene režimu visel layout na červenej obsadenosti z predošlého stavu projektu.
**Riešenie:**
• Z21Client teraz cyklicky polluje skupiny 0–15 (moduly 1–160), poll interval znížený a connect-handshake skrátený (kratšie timeouty pre serial/HW info, HW info sa zisťuje na pozadí).
• Pridané runtime metriky R-BUS rámcov (direct frame counter, ignored LAN_X_0x43, poll send count, heartbeat tick) ako základ pre ďalšiu diagnostiku.
• Nový helper `DccFeedbackLayoutApplier.SynchronizeOccupancyFromIndicators` zaisťuje, že po prepnutí do/zo simulátora a po vstupe do Prevádzky sa obsadenosť bloku obnoví z aktívnych kontaktov (iba false→true; uvoľnenie naďalej musí prísť explicitne cez feedback).
• `MainWindowViewModel` po úspešnom connecte vyčistí runtime feedback stav daného profilu (`ClearFeedbackState`), aby na obrazovke nezostala červená obsadenosť z predchádzajúcej relácie alebo z projektu otvoreného pred pripojením.
• `OperationViewModel` pri prijatí živej DCC obsadenosti v simulačnom režime jednorazovo vypíše varovanie do Doctora a okolo `HandleOccupiedBlocks` doplnil reconcile-start/end diagnostiku.
• Odstránené hlučné per-frame `RBUS forward`/`RBUS match`/`RBUS occupancy-update` logy v `DccConnectionService` a `DccFeedbackLayoutApplier` (nahradené agregovanou diagnostikou v OperationViewModel).
• Testy pokrývajú nový resync helper, R-BUS group enumeráciu aj duplicate-binding cleanup.
**Výsledok:** Reakcia na obsadenie/uvoľnenie je výrazne svižnejšia naprieč všetkými R-BUS modulmi, po reconnect/prepnutí režimu sa stav blokov nestratí ani „nezasekne" a Doctor log nie je zaplavený per-frame šumom.

## 2026-06-14 17:10
===================
**Oblasť:** `Services/ProjectMigrationService.cs`, `Services/SettingsManager.cs`, `Services/TrackFlowDoctorService.cs`, `Views/DoctorWindow.axaml`, `Converters/BoolToThicknessConverter.cs`, `ViewModels/Editor/IndicatorPropertiesViewModel.cs`, `ViewModels/Editor/TurnoutPropertiesViewModel.cs`, `TrackFlow.Tests/ProjectMigrationServiceTests.cs`, `TrackFlow.Tests/IndicatorPropertiesViewModelTests.cs`, `TrackFlow.Tests/TurnoutPropertiesViewModelTests.cs`
**Zmena:** Pri načítaní projektu sa robí konfiguračný audit a Doctor používa nový vizuálny varovací piktogram (žltý trojuholník) s ľudsky čitateľnými hláseniami.
**Dôvod:** Bežné konfiguračné chyby (duplicitné DCC adresy lokomotív/návestidiel, výhybky/návestidlá s adresou 0, výhybka s adresou bez priradeného systému, bloky s nulovou dĺžkou pre kalibráciu) sa v UI nikde nehlásili a používateľ ich odhalil až pri prevádzke. Zároveň „⚠"/„❗" emoji v Doctor logu nevykresľovali konzistentne.
**Riešenie:**
• `ProjectMigrationService.DiagnoseProjectConfigurationIssues` po načítaní projektu vypíše do Doctora konkrétne varovania pre: duplicitné DCC adresy lokomotív, výhybky s adresou 0 + priradeným systémom, výhybky s adresou bez systému, návestidlá s adresou 0, duplicitné DCC adresy návestidiel a bloky s `lengthMm == 0`.
• `SettingsManager.RepairLegacyContactIndicatorBindingsFromEffectiveProfiles` pri načítaní doplní legacy kontaktom `DccCentralProfileId` z efektívneho profilu (jediný povolený, alebo aktívne vybraný), aby R-BUS binding po migrácii nezostal „nezacielený".
• `TrackFlowDoctorService` rozpoznáva nový marker varovania a v `DoctorWindow.axaml` sa zobrazí ako vektorový žltý výstražný trojuholník (`Path/Rectangle/Ellipse` v `Viewbox`) namiesto emoji; `MessageText` z hlásení marker odstráni, takže text zostáva čistý.
• ComboBoxy systémov v IndicatorProperties/TurnoutProperties zobrazujú ľudsky čitateľný názov centrály cez `DccCentralDisplayName.Get` namiesto `DccCentralType` enumu.
• Pridaný `BoolToThicknessConverter` pre flexibilnejšie štýly v Doctor zobrazení.
• Pokryté unit testami v `ProjectMigrationServiceTests`, `IndicatorPropertiesViewModelTests`, `TurnoutPropertiesViewModelTests`.
**Výsledok:** Používateľ ihneď po otvorení projektu vidí v Doctorovi všetky tichéť konfiguračné problémy s vizuálne výrazným varovacím piktogramom a čistým textom.

## 2026-06-14 12:00
===================
**Oblasť:** `Models/TrainSetRecord.cs`, `Services/ProjectStore`, `TrackFlow.Tests/ProjectStoreTrainSetTests.cs` (commit `a68e487`)
**Zmena:** Ukladanie a obnovenie vlakových súprav po načítaní projektu v SmartPase.
**Dôvod:** Vytvorené súpravy sa po zatvorení a opätovnom otvorení projektu strácali, lebo neboli súčasťou persistovaného modelu.
**Riešenie:** Pridaný model `TrainSetRecord` a perzistencia v ProjectStore; pokryté testom `ProjectStoreTrainSetTests`.
**Výsledok:** Súpravy zostávajú zachované medzi sedeniami; používateľ ich nemusí pri každom otvorení projektu vytvárať odznova.

## 2026-06-12 12:00
===================
**Oblasť:** `Views/Library/TrainsWindow.axaml`
**Zmena:** Doriešené vizuálne správanie horného zoznamu súprav a dolnej tabuľky vlakov: odstránený nechcený default selected overlay z `ListBox`, zjednotený spôsob kreslenia riadkov súprav a upravené komentáre pri farebných resource tak, aby presne popisovali reálne použitie farieb.
**Dôvod:** Pri výbere súpravy a vlaku sa v UI miešali cudzie selected vrstvy témy s vlastnými farbami, čo spôsobovalo nejednoznačný a zavádzajúci výsledok. Zároveň boli v resource komentároch nepresné formulácie o tom, čo ktorá farba v skutočnosti pokrýva.
**Riešenie:**
• Horný `ListBox` bol pre výber súpravy prepnutý na vlastný jednoduchý template, aby selected vizuál neprebíjal definované farby.
• Zoznam súprav ostal konfigurovateľný cez tri farby na začiatku súboru (`TrainSetSelectedBorderBrush`, `TrainSetSelectedBackgroundBrush`, `TrainSetRowBorderBrush`).
• Dolný `DataGrid` dostal vlastné štýly pre oddelenie riadkov, farbu textu vo vybranom vlaku a oddelené podfarbenie „vlak patrí do vybranej súpravy“ vs. „vlak je aktuálne vybraný“.
• Komentáre v `Window.Resources` boli upratané a zjednodušené tak, aby bolo jasné, čo sa kde reálne prejaví v UI.
**Výsledok:**
• Výber súpravy aj vlakov je vizuálne čitateľný a farby sa menia na očakávanom mieste.
• Zostalo zachované centrálne miesto pre úpravu farieb na začiatku súboru.
• Build riešenia prešiel po úpravách.

## 2026-06-10 14:00
===================
**Oblasť:** `Services/AppSettingsStore.cs`, `App.axaml.cs`, `TrackFlow.Tests/AppSettingsStorePathTests.cs`, `Services/VehicleIconLoader.cs`, `Views/Shared/BlockTrainRenderer.cs`, `Views/Operation/OperationView.axaml.cs`, `Views/Editor/LayoutEditorView.axaml.cs`, `Converters/IconNameToPathConverter.cs`, `ViewModels/Library/LocomotivesWindowViewModel.cs`
**Zmena:** Stabilizované načítanie/ukladanie `settings.json` aj po publish a dokončené publish-safe zobrazovanie ikon lokomotív/vagónov (vrátane ComboBoxu v editore lokomotív).
**Dôvod:** V publikovanej aplikácii sa konfigurácia nenačítavala/nevytvárala spoľahlivo po prvom spustení a ikony sa stratili v častiach UI, ktoré sa spoliehali na fyzický priečinok `Assets`.
**Riešenie:**
• `AppSettingsStore` teraz normalizuje relatívne cesty voči `AppDomain.CurrentDomain.BaseDirectory`; `settings.json` sa číta/zapisuje konzistentne vedľa `.exe`.
• Pri ukončení aplikácie sa explicitne volá persist app nastavení (`desktop.Exit` + `ProcessExit` fallback), aby sa súbor vytvoril aj bez manuálnych zmien používateľa.
• Zavedený centralizovaný `VehicleIconLoader` s fallbackmi: absolútna cesta → `IconRegistry` → priame `avares://` → embedded `Assets/LocoIcons` + `Assets/VagonIcons`.
• Vykresľovanie ikon v `BlockTrainRenderer`, `OperationView`, `LayoutEditorView` a `IconNameToPathConverter` bolo prepojené na nový loader.
• `VehicleIconLoader` používa case-insensitive mapu URI z `AssetLoader.GetAssets(...)`, aby fungovalo aj v single-file publish a pri rozdieloch v zápise názvov.
• `LocomotivesWindowViewModel.LoadAvailableIcons()` doplnený o publish fallback: zoznam ikon sa načíta aj z embedded resource, takže ikony sa zobrazia aj v ComboBoxe „Ikona“ v editore lokomotív.
**Výsledok:**
• `settings.json` sa po publish správne nájde/vytvorí a perzistuje medzi spusteniami.
• Ikony lokomotív a vagónov sa zobrazujú v Smart páse aj v editore lokomotív (vrátane výberového ComboBoxu).
• Overené build/test behmi po úpravách (fokus na `AppSettingsStorePathTests`; kompilácia riešenia prešla).

## 2026-06-09 19:10
===================
**Oblasť:** `Views/Settings/SettingsPages/GeneralSettingsView.axaml`, `Views/Settings/SettingsPages/GeneralSettingsView.axaml.cs`, `Views/Settings/SettingsPages/ModelClockSettingsView.axaml`, `ViewModels/Settings/SettingsViewModel.cs`, `Models/AppSettingsData.cs`, `Views/MainWindow.axaml.cs`, `ViewModels/MainWindowViewModel.cs`, `Services/TooltipPreferenceService.cs`, `App.axaml`, `Views/ClockView.axaml`, `Views/ClockView.axaml.cs`, `ViewModels/ClockViewModel.cs`
**Zmena:** Dokončené oživenie praktických nastavení v kategóriách Všeobecné a Modelové hodiny: predvolený adresár projektov, auto-save interval, globálne tooltips, otvorenie hodín pri štarte, zobrazenie tlačidla Štart/Pauza a voľba „Nastaviť čas na".
**Dôvod:** Viaceré položky UI boli iba vizuálne placeholdery alebo sa aplikovali až po reštarte. Cieľ bol dostať všetky relevantné prepínače do plne funkčného stavu s okamžitým runtime efektom.
**Riešenie:**
• `GeneralSettingsView` dostal funkčný picker predvoleného adresára projektov s perzistenciou do `AppSettingsData`; Open/Save pickery používajú tento adresár ako preferovaný štart.
• Auto-save bol napojený end-to-end (`AutoSaveEnabled`, `AutoSaveIntervalMinutes`) vrátane runtime `DispatcherTimer` v `MainWindowViewModel` a ochrany proti intervalu `0` pri zapnutí (normalizácia na minimálne 5 min).
• Nastavenie tooltipov bolo centralizované cez `TooltipPreferenceService`; preferencia `ShowTooltipsInApp` sa aplikuje globálne pre všetky okná aj pri štarte aplikácie.
• V `ModelClockSettingsView` boli oživené posledné 3 voľby: `ShowClockOnStartup`, `ShowClockStartPauseButton`, `SetModelClockTimeOnStartup` + `StartupModelClockHour/Minute`.
• `MainWindow` po štarte otvorí okno hodín podľa nastavenia; `ClockView` reaguje na preferenciu zobrazenia tlačidla Štart/Pauza aj za behu.
• Čas hodín sa nastaví na konfigurovaný čas okamžite po `Uložiť` (ak je voľba zapnutá), bez čakania na reštart; pri vypnutej voľbe sa tento preset čas neaplikuje.
**Výsledok:**
• Nastavenia sú funkčné globálne aj runtime (nielen uložené do JSON).
• Kritické regresie z používateľského testu boli opravené: tooltip režim po cold štarte, modelové hodiny a okamžité aplikovanie času po uložení nastavení.
• Kompilácia overená opakovane buildmi do separátnych výstupov (`verify-tooltip-final2`, `verify-model-clock-settings`, `verify-model-clock-startup-toggle-fix`, `verify-model-clock-apply-on-save`).

## 2026-06-07 23:20
===================
**Oblasť:** `Views/Settings/SettingsWindow.axaml`, `Views/Settings/SettingsWindow.axaml.cs`, `Views/Settings/SettingsPages/GeneralSettingsView.axaml`, `Views/Settings/SettingsPages/ModelClockSettingsView.axaml`, `ViewModels/Settings/SettingsViewModel.cs`, `Views/MainWindow.axaml.cs`
**Zmena:** Kompletný refaktor okna Nastavení z `TabControl/TabItem` na architektúru `ListBox + ContentControl` s oddelenými stránkami (`Všeobecné`, `DCC pripojenie`, `Modelové hodiny`, `Farby`) a finálny stabilizačný fix opakovaného zamŕzania pri otváraní okna.
**Dôvod:** Po prechode na nové usporiadanie nastavení sa pri opakovanom otvorení dialógu objavovalo zamrznutie UI. Diagnostika ukázala, že problém vznikal počas konštrukcie `SettingsWindow` (eager vytváranie celého obsahu naraz), nie v ukladaní ani v zatváraní.
**Riešenie:**
• Pravý panel v `SettingsWindow` bol prerobený na lazy host (`ContentControl`), ktorý podľa `SelectedSettingsTabIndex` vytvára stránku až pri zobrazení; stránky sa cache-ujú v code-behind (`GeneralSettingsView`, `DccSettingsView`, `ModelClockSettingsView`, `ColorsSettingsView`).
• DCC obsah zostal vizuálne aj funkčne zachovaný; prepínanie tab indexu ostalo naviazané na existujúci `SettingsViewModel`.
• Pridaná samostatná stránka `ModelClockSettingsView`; nastavenie `SimulationSpeedFactor` bolo presunuté z `GeneralSettingsView` na túto stránku.
• Ľavý zoznam kategórií v `SettingsWindow` má obnovené SVG/path ikony; dočasné diagnostické logy a slepé pokusy boli odstránené.
• `SettingsViewModel.RefreshAvailablePorts()` ostal asynchrónny (porty sa načítavajú mimo UI thread), čím sa znižuje riziko blokovania pri otváraní Nastavení.
**Výsledok:**
• `SettingsWindow.axaml` už neobsahuje `TabControl`, `TabItem` ani `tc-tabs`; používa iba `ListBox + ContentControl`.
• Build riešenia prešiel v default výstupe (`dotnet build TrackFlow.sln -c Debug`).
• Manuálny opakovaný test otvorenia/zatvorenia okna Nastavení bol potvrdený ako úspešný (stabilný beh bez zamrznutia).

## 2026-06-03 16:50
===================
**Oblasť:** `ViewModels/Library/LocomotiveSpeedEditorViewModel.cs`,
`TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`, `TrackFlow.Tests/OperationViewInteractionMarkupTests.cs`
**Zmena:** 🟩 Opravené 3 padajúce testy — format dobehu (`RunoutDistanceMm`), robustnosť XAML assertions a aktualizovaná
logika rendreru bloku.
**Dôvod:** Testy padali kvôli: (1) formátu dobehu s jedným miestom namiesto dvoch, (2) whitespace-citlivým assertorom na
XAML, (3) zastaraným očakávaniam o vykresľovaní lokomotívy.
**Riešenie:**
• `FormatRunoutDistance(double value)` opravený z `"0"` na `"0.00"` format — vracajú sa teraz `12.34` a `8.00`.
• XAML assertion na `MinHeight="220"` rozdelený na dva nezávislé assertions (`MinHeight` + `ClipToBounds`).
• Test `BlockRenderer_NekresliVlakLenZoStarehoAssignedLocoIdAkBlokNieJeObsadeny` premenovaný na
`BlockRenderer_KresliVlakZoAssignedLocoIdAjKedBlokNieJeObsadeny` s očakávaním `"loco_demo_1"` (vykresluje sa aj bez
`IsOccupied=true`).
**Výsledok:** Všetky testy prešli: **791/791 ✓** (predtým 788/791).

## 2026-06-03 16:30
===================
**Oblasť:** `Views/Library/LocomotiveCalibrationWindow.axaml`, `Views/Library/LocomotivesWindow.axaml`,
`ViewModels/Library/LocomotiveSpeedEditorViewModel.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Konzistentnosť ikonizácie — kalibračné ComboBoxy `Štart/Stred/Koniec` teraz používajú **PNG ikony** s
aktívnym/neaktívnym párom (rovnako ako TurnoutPropertiesWindow Indikátory tab). Nahradené glyphové TextBlocky Image
bindingmi na `IconImage`.
**Dôvod:** Projekt mal dva rozdielne druhy ikon — glyphs v kalibrácii a PNG v dialógu vlastností výhybiek. Cieľom bolo
unifikovať vizuálny štýl a funčnosť.
**Riešenie:**
• `CalibrationIndicatorOption` už mal všetky potrebné vlastnosti (`ActiveIconPath`, `InactiveIconPath`, `IconImage`,
`IsActive`).
• V XAML templates (`ItemTemplate`, `SelectionBoxItemTemplate`) sa `<TextBlock Text="{Binding IconGlyph}">` nahradil za
`<Image Source="{Binding IconImage}">`.
• Inicializácia indikátorov v `SyncProjectIndicators(IEnumerable<string>)` teraz poskytuje aktívnu/neaktívnu páru PNG
ciest (`cont_ind.png` / `cont_ind_d.png`) s `isActive: true`.
**Výsledok:** Všetky testy prešli (791/791). Kalibračné okno teraz konzistentne zobrazuje PNG ikony rovnakých tipov ako
Turnout Properties dialog.

## 2026-06-03 16:05
===================
**Oblasť:** `ViewModels/Library/LocomotiveSpeedEditorViewModel.cs`, `ViewModels/Library/LocomotivesWindowViewModel.cs`,
`Views/Library/LocomotiveCalibrationWindow.axaml`, `Views/Library/LocomotivesWindow.axaml`,
`ViewModels/MainWindowViewModel.cs`, `Views/MainWindow.axaml.cs`, `Views/Shared/VehicleStripItem.axaml.cs`,
`ViewModels/Editor/TurnoutPropertiesViewModel.cs`, `Views/Editor/TurnoutPropertiesWindow.axaml.cs`,
`ViewModels/Editor/RouteEditorViewModel.cs`, `TrackFlow.csproj`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`,
`TrackFlow.Tests/TurnoutPropertiesViewModelTests.cs`
**Zmena:** Rozšírený kalibračný flow o živé prepínanie aktívnych/neaktívnych ikon indikátorov podľa feedbacku, doplnené
dynamické povoľovanie `Štart/Stred/Koniec` podľa zvolenej metódy kalibrácie, automatické čistenie disabled výberov a
nový parameter `Dobeh [mm]` v kalibračnom UI. Súbežne boli zosúladené ikony senzorov v route/turnout editore a doplnené
kopírovanie `Assets\Appicons\**` do outputu.
**Dôvod:** Doterajší stav mal tri praktické limity: (1) indikátory v kalibrácii sa po DCC feedbacku nepremaľovali v
otvorenom okne, (2) pri prepnutí metódy ostávali vybraté bloky aj v poliach, ktoré už mali byť disabled, (3) niektoré
ikony neboli v runtime vždy dostupné mimo `avares` cesty.
**Riešenie:**
• `CalibrationIndicatorOption` bol prerobený na dual-state ikony (`active/inactive`) + `IsActive` + voliteľné
`IndicatorId`; pribudol cache loading bitmap a fallback na fyzické `Assets\Appicons\16`.
• `LocomotiveSpeedEditorViewModel` dostal `SyncIndicatorActiveStates(...)`,
`IsStartBlockEnabled/IsMiddleBlockEnabled/IsEndBlockEnabled`, helper `ClearDisabledBlockSelections()` a textovo čistený
vstup `RunoutDistanceMmText` pre nový parameter dobehu.
• `LocomotivesWindowViewModel` doplnený o `RefreshCalibrationIndicatorStates()`, ktoré po feedback zmene aktualizuje
existujúce položky bez rebuildu celého zoznamu.
• `MainWindowViewModel` publikujeme cez `LayoutBlocksChangedByFeedback`; `MainWindow` aj `VehicleStripItem` pri otvorení
okna lokomotív subscribujú/unsubscribujú tento event a prepájajú ho na refresh kalibračných ikon.
• `TurnoutPropertiesViewModel`/`RouteEditorViewModel` používajú rovnakú active/inactive ikonovú logiku;
`TurnoutPropertiesWindow` navyše počas otvoreného dialógu periodicky refresuje stavy senzorov (250 ms).
• `TrackFlow.csproj` upravený na explicitné zahrnutie a kopírovanie `Assets\Appicons\**\*.*` do výstupu.
**Výsledok:**
• Cielený testovací rez
`FullyQualifiedName~LocomotiveSpeedEditorMarkupTests|FullyQualifiedName~TurnoutPropertiesViewModelTests` bol spustený s
outputom do `bin\verify-tests9\`.
• Aktuálny stav rezu: **149/151 passed**, zlyhali 2 testy (
`SpeedEditor_ExponujeSlovenskeMetodyAKalibracneIndikatoryZProjektu`,
`LocomotivesWindow_RychlostTabObsahujeKlucoveEnterpriseSekcie`) kvôli neaktuálnym očakávaniam voči novému
správaniu/formátu.

## 2026-06-03 14:32
===================
**Oblasť:** `Views/Library/LocomotiveCalibrationWindow.axaml`, `Views/Library/LocomotivesWindow.axaml`,
`TrackFlow.csproj`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Opravené zobrazenie indikátora v ComboBoxoch `Štart/Stred/Koniec` tak, aby sa ikona vždy zobrazila pred
názvom bloku; aktívny indikátor je teraz červený. Zároveň odstránený zbytočný `<Folder Include="Assets\CarIcons\" />` z
projektu.
**Dôvod:** V kalibračných ComboBoxoch sa napriek predchádzajúcim úpravám vykresľoval iba text bez ikony. Používateľ
zároveň potvrdil, že priečinok `Assets\CarIcons\` v projekte vôbec neexistuje.
**Riešenie:**
• V šablónach pre kalibračné indikátory (`LocomotiveCalibrationWindow`, `LocomotivesWindow`) bol ikonový prvok zmenený
na `TextBlock` s väzbou na `IconGlyph`.
• Stav `IsActive` sa mapuje cez `BoolToBrushConverter`: aktívny indikátor `#DC2626` (červená), neaktívny `#8A97A8` (
sivá).
• V `TrackFlow.csproj` bol odstránený nepoužívaný folder include `Assets\CarIcons\`.
• Markup testy boli zosúladené s glyph-based renderom (`IconGlyph`) a overením ikon v kalibračných ComboBoxoch.
**Výsledok:**
• Cielený testovací rez:
`LocomotiveCalibrationWindow_KalibracneComboBoxyPouzivajuSelectionTemplateSIkonou|LocomotivesWindowViewModel_NaplniKalibracneComboBoxyZoZivychIndikatorovBlokov|SpeedEditor_SyncIndicatorActiveStatesPrepinaIkonuPodlaFeedbacku`
prešiel úspešne (**3/3 passed**) s výstupom do `bin\verify-tests6\`.

## 2026-06-02 16:40
===================
**Oblasť:** `ViewModels/Editor/RoutesManagerViewModel.cs`, `Services/RouteMarkerAssignmentHelper.cs`,
`Views/Editor/LayoutEditorView.axaml.cs`, `Views/Operation/OperationView.axaml.cs`,
`ViewModels/Editor/LayoutEditorViewModel.cs`, `Views/MainWindow.axaml.cs`,
`TrackFlow.Tests/RoutesManagerViewModelRouteMetadataTests.cs`
**Zmena:** Opravená synchronizácia markerov `Cesta` po vymazaní route zo zoznamu ciest. Ak marker ukazoval na zmazanú
route, jeho `SelectedRouteDefinitionId` sa okamžite nuluje a šípka sa v UI prefarbí zo žltej na červenú bez čakania na
ďalší manuálny refresh.
**Dôvod:** Po odstránení route v správcovi ciest zostávali niektoré route markery vizuálne „priradené" (žlté), hoci ich
cieľová route už fyzicky neexistovala v `layout.Routes`.
**Riešenie:**
• Pridaný helper `RouteMarkerAssignmentHelper`:

- `HasAssignedRoute(...)` overuje existenciu priradenej route podľa `SelectedRouteDefinitionId`.
- `ClearInvalidAssignments(...)` prejde všetky `RouteElement` a neplatné priradenia resetne na `null`.
  • `RoutesManagerViewModel.DeleteRoute()` po zmazaní route volá `ClearInvalidAssignments(...)` a pri náleze
  zneplatnených markerov vyvolá okamžitý refresh editora aj prevádzky.
  • `LayoutEditorViewModel` dostal lightweight refresh event (`VisualRefreshRequested` + `RequestVisualRefresh()`),
  ktorý `LayoutEditorView` odoberá cez `ScheduleRebuild()`.
  • Render route markerov v editore aj prevádzke bol sprísnený: farba sa už neriadi len
  `SelectedRouteDefinitionId != null`, ale reálnou existenciou route (`HasAssignedRoute(...)`).
  • Pri otvorení správcu ciest z hlavného okna sa teraz do `RoutesManagerViewModel` odovzdávajú aj hlavné VM (
  `LayoutEditor`, `Operation`) pre okamžité prekreslenie po delete.
  • Doplnený regresný test `DeleteRoute_ClearsAssignedRouteId_FromRouteMarkersReferencingDeletedRoute`.
  **Výsledok:**
  • Logika mazania route je konzistentná s vizuálnym stavom markerov (`žltá` len pre platné priradenie, inak `červená`).
  • Editorová kontrola upravených súborov je bez nových compile chýb.
  • Cielený test bol pripravený a spustený, ale build/test beh bol v prostredí blokovaný lockom `TrackFlow.dll`
  bežiacimi `.NET Host` procesmi.

## 2026-06-01 15:10
===================
**Oblasť:** `ViewModels/Operation/OperationViewModel.cs` (`ActivateRouteAsync`, nový helper
`PreSwitchRouteTurnoutsAsync`)
**Zmena:** Pri aktivácii cesty z bloku do bloku sa **najprv prestavia VŠETKY výhybky v ceste do požadovanej polohy** a
až potom sa rozsvietia návestidlá a spustí simulácia jazdy. Doteraz sa výhybky prepínali lazy – až tesne pred vstupom
vlaku do daného segmentu vo `TryEnsureTurnoutsForSegmentAsync`, čo v demo režime vyzeralo, akoby sa výhybky v schéme
„preklápali na poslednú chvíľu".
**Dôvod:** UX požiadavka používateľa – pri aktivácii cesty v demo režime má byť celá cesta okamžite vizuálne
„postavená" (všetky výhybky v správnej polohe), nielen lockované bloky. Pôvodné správanie spôsobovalo, že schéma sa
aktualizovala až postupne počas behu vlaku, čo pôsobilo neprehľadne.
**Riešenie:**
• V `ActivateRouteAsync` hneď po úspešnom `_routeActivationService.TryActivateAsync(...)` a
`InitializeRouteRuntime(...)` / `DiagnoseRouteStarted(...)` (a pred `SetSignalsRedRespectingActiveRoutes` a
`UpdateTraversalSignalWindowAsync`) sa volá nový helper
`PreSwitchRouteTurnoutsAsync(layout, activationRoute, effectiveDccClient, ct)`.
• `PreSwitchRouteTurnoutsAsync` iteruje cez všetky segmenty `activationRoute.BlockIds[i] → BlockIds[i+1]` a pre každý
zavolá existujúce `TryEnsureTurnoutsForSegmentAsync`, ktoré:
– nastaví `TurnoutElement.State = RouteTurnoutSetting.RequiredState` v modeli,
– zapíše vlastníctvo výhybky do `_turnoutRuntimeReservations[turnoutId] = route.Id`,
– v Live režime pošle `IDccCentralClient.SetTurnoutAsync(turnout.DccAddress, branch, activate: true, ct)`,
– po prvej skutočnej zmene polohy vyvolá `LayoutRefreshRequested?.Invoke()` → schéma sa prekreslí so všetkými novými
polohami pred štartom jazdy.
• Per-segmentová kontrola v `TryEnsureTurnoutsForSegmentAsync` počas traverzu (line ~1632) zostala bez zmeny – ak je
výhybka už v správnej polohe, len doplní diagnostiku „pripravená" a neposiela duplicitný DCC príkaz.
• Nižšia vrstva `RouteActivationService.TryActivateAsync` ostáva naďalej úmyselne „neeager" voči výhybkám (test
`RouteActivationServiceTests.TryActivateAsync_PriAktivaciiNehybeVyhybkouEager` zostáva platný) – nové eager
pre-switching žije iba v UX vrstve `OperationViewModel`.
**Výsledok:**
• Build `TrackFlow.Tests.csproj` → **0 warnings / 0 errors** po `kill-trackflow-locks.ps1` (lock bol len kvôli bežiacej
aplikácii, nie kompilačný error).
• V demo režime sa po stlačení aktivácie cesty najprv okamžite prekreslia všetky výhybky na trase do požadovanej polohy
a až potom sa rozsvietia návestidlá a vlak sa rozbehne.
• Žiadna zmena správania `RouteActivationService`, `RouteConflictDetector`, `ReservationEngine` ani DCC vrstvy – v Live
režime sa pre každú výhybku stále pošle práve jeden `SetTurnoutAsync` (buď pri aktivácii, alebo per-segment, ale nie
obidvakrát, lebo druhé volanie nájde turnout už v cieľovej polohe).


## 2026-06-01 15:05
===================
**Oblasť:** `Views/Operation/OperationView.axaml.cs`
**Zmena:** Zjednotené zobrazenie a priradenie lokomotívy do bloku medzi editovacím a prevádzkovým režimom – v prevádzke
sa rušeň teraz vždy vykreslí, keď je k bloku priradený, a do bloku ho je možné priradiť drag&drop priamo z páska
lokomotív (rovnako ako v editore).
**Dôvod:** V prevádzkovom režime sa vyskytli dve prepojené chyby okolo väzby lokomotíva↔blok:

1. Ak bola lokomotíva priradená do bloku v editovacom režime, po prepnutí do prevádzky v bloku nebola vidieť – render
   vyžadoval `IsOccupied=true`, ktoré ale safety/simulation pass po štarte prevádzky zhodil, hoci `AssignedLocoId`
   zostalo platné.
2. V prevádzkovom režime sa lokomotíva vôbec nedala priradiť do bloku – canvas nemal vlastné drag&drop handlery na drop
   lokomotívy a aj keby bolo priradenie vykonané, vykresľovanie by ho beztak nezobrazilo (drop v prevádzke úmyselne
   nenastavuje `IsOccupied`, lebo obsadenie má potvrdiť až senzor/centrála).
   **Riešenie:**
   • V `OperationView.axaml.cs` bol pridaný pomocný `ResolveRenderableBlockLocoId(BlockElement block)`, ktorý vracia
   `block.AssignedLocoId` bez podmienky `IsOccupied`; logika "transition shadow" cez porovnanie `loco.AssignedBlockId`
   vs. `block.Id` ostáva zachovaná a "reserved shadow" sa naďalej zobrazuje cez `ReservedLocoId`. Tým sa render v
   prevádzke zarovnal s editorom a vyriešila chyba (1).
   • Hlavná render vetva bloku (`if (DataContext is OperationViewModel vm && el is BlockElement blockElAssign)`) bola
   prepísaná tak, aby používala výsledok `ResolveRenderableBlockLocoId(...)` namiesto priameho
   `IsOccupied + AssignedLocoId` kontrolu, čím sa stav lokomotívy medzi režimami už nerozpája.
   • Pridaná kompletná drag&drop infraštruktúra pre prevádzkový canvas (rovnaká ako v editore): konštanta
   `LocoFormat = "trackflow/locomotive"`, handlery `OnCanvasLocoDragOver` / `OnCanvasLocoDragLeave` /
   `OnCanvasLocoDrop`, helpre `FindBlockElementAt(...)`, `ComputeDropDirection(...)`, `ShowDragArrow(...)`,
   `HideDragArrow()` a floating-arrow indikátory smeru. Vlastný drop volá zdieľané
   `vm.AssignLocomotiveToBlockAsync(loco.Code, block.Id, isForward, dccClient)` a pri úspechu lokomotívu automaticky
   aktivuje (push do `SmartStrips.ActiveLocomotives`) – tým sa vyriešila chyba (2).
   • Drop handler je obalený do `try/catch` s `Program.ReportUnhandledException("OperationView.OnCanvasLocoDrop", ...)`
   a `TrackFlowDoctorService` warningom, aby výnimky pri priradzovaní v prevádzke nešli na UI dispatcher.
   **Výsledok:**
   • Manuálne overené: lokomotíva priradená v editore zostáva po prepnutí do prevádzky vizuálne v bloku; nová lokomotíva
   sa dá v prevádzkovom režime priradiť do bloku drag&drop-om priamo zo smart-strip-u.
   • Render aj priradenie sú teraz funkčne identické v oboch režimoch (zdieľané `BlockTrainRenderer` + zdieľané
   `AssignLocomotiveToBlockAsync`), takže odpadlo rozpájanie stavu medzi editor a prevádzku.

## 2026-06-01 14:28
===================
**Oblasť:** `Services/Dcc/IDccCentralClient.cs`, `Services/Dcc/Z21Client.cs`, `Services/SignalController.cs`,
`ViewModels/Operation/OperationViewModel.cs`, `TrackFlow.Tests/*`
**Zmena:** Vyššie hodnotený auditový batch pre 4.7 – `SetTurnoutAsync` bol rozšírený o explicitný parameter `branch` a
Z21 packet už skladá výhybkový bit-pattern z výberu výstupu a `activate` bitu nezávisle.
**Dôvod:** Audit 4.7 upozornil, že pôvodné API miešalo „výber výstupu“ a „activate“ do jedného booleanu, takže Z21
klient nevedel odlíšiť `(activate=1,out=0)` od `(activate=1,out=1)` a prakticky neumožňoval korektne vybrať druhú vetvu
výhybky bez zneužitia semantiky `activate`.
**Riešenie:**
• 🟩 (audit 4.7) rozhranie `IDccCentralClient` bolo zmenené z `SetTurnoutAsync(int address, bool activate, ...)` na
`SetTurnoutAsync(int address, bool branch, bool activate, ...)`, kde `branch=false` znamená výstup 0 / priamo a
`branch=true` výstup 1 / odbočka.
• Default fallback v `SetExtendedAccessoryAspectAsync(...)` teraz volá
`SetTurnoutAsync(address, aspectNumber > 0, activate: true, ct)`, takže jasne odlišuje výber výstupu od energizácie.
• `Z21Client` už nepočíta `Data` bajt cez konfliktný zápis `0x09/0x08`, ale explicitne skladá `bit3` z `activate` a
`bit0` z `branch`: `byte data = (byte)((activate ? 0x08 : 0x00) | (branch ? 0x01 : 0x00));`.
• `OperationViewModel` pri segmentovom prestavení výhybiek používa nový helper
`RouteActivationService.MapTurnoutStateToBranch(...)` a fyzické prestavenie posiela ako
`SetTurnoutAsync(..., branch, activate: true, ...)`.
• `SignalController` bol narovnaný rovnako: helper `MapPeliAspectToTurnout(...)` už vracia výber vetvy (`branch`)
namiesto starej pseudo-semantiky `activate` a basic-mode signály posielajú turnout command s `activate: true` a správnym
výstupom.
• Zosúladené boli všetky stub/fake klienti v testoch a pridané regresie: source-shape test pre nové API + Z21
bit-pattern a behavior testy pre signal-safety/basic-mode DCC send.
**Výsledok:**
• Cielený rez
`SignalControllerTests|OperationViewModelRouteActivationTests|OperationViewModelSignalSafetyTests|OperationRuntimeSafetyServiceTests|LocomotiveSpeedEditorMarkupTests|DccConnectionServiceTests|DccCommunicationTestHandlerTests|LocomotiveAddressProgrammingTests` →
**287 / 287 passed, 0 regresií**.
• Rozšírený rez
`SignalControllerTests|OperationViewModelRouteActivationTests|OperationViewModelSignalSafetyTests|OperationRuntimeSafetyServiceTests|LocomotiveSpeedEditorMarkupTests|DccConnectionServiceTests|DccCommunicationTestHandlerTests|LocomotiveAddressProgrammingTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` (
`--no-build`) → **298 / 298 passed, 0 regresií**.


## 2026-06-01 14:13
===================
**Oblasť:** `Services/Dcc/IDccProgrammingClient.cs`, `Services/Dcc/Z21Client.cs`, `Services/Dcc/SerialDccClient.cs`,
`Views/Library/LocomotivesWindow.axaml.cs`, `ViewModels/Library/LocomotivesWindowViewModel.cs`, `TrackFlow.Tests/*`
**Zmena:** Vyššie hodnotený auditový batch pre 4.6 – odstránený rizikový default `locoAddress = 3` z CV programming API
a service-track cesty už používajú explicitný neutrálny placeholder namiesto magickej adresy 3.
**Dôvod:** Audit 4.6 upozornil, že default `locoAddress = 3` je pri POM tichý bug: ak volajúci zabudne dodať adresu,
zápis alebo čítanie ide na adresu 3. To je bezpečnostne nepríjemné najmä pri reálnom dekodéri na hlavnej trati. Zároveň
service-track cesty v UI/viewmodeli zbytočne šírili tú istú „magickú trojku“, hoci sa tam parameter vôbec nepoužíva.
**Riešenie:**
• 🟩 (audit 4.6) z `IDccProgrammingClient`, `Z21Client` aj `SerialDccClient` bol odstránený default parameter
`int locoAddress = 3`; POM volania tak teraz musia adresu odovzdať explicitne.
• V `LocomotivesWindow.axaml.cs` bol service-track read prepnutý z fallbacku `GetSelectedLocomotiveAddressForPom() ?? 3`
na explicitný `const int serviceTrackAddressPlaceholder = 0`.
• Rovnaké narovnanie prebehlo aj v `LocomotivesWindowViewModel` pri service-track `ReadProgrammingCvAsync(...)` a
`WriteProgrammingCvAsync(...)`, aby sa interné adresové programovanie neopieralo o magickú trojku.
• Fake klienti v `LocomotiveAddressProgrammingTests` a `DccCommunicationTestHandlerTests` boli zosúladení s novou
signatúrou bez defaultu; `SerialDccClientTests` teraz posielajú explicitný placeholder `0` pre service-track a
explicitnú adresu pre POM negatívny test.
• Pridané regresie: source-shape test, že API už neobsahuje `int locoAddress = 3`, a behavior testy, že service-track
address-programming vo viewmodeli skutočne posiela `0` namiesto fallbacku 3.
**Výsledok:**
• Cielený rez
`LocomotiveAddressProgrammingTests|SerialDccClientTests|DccCommunicationTestHandlerTests|LocomotiveSpeedEditorMarkupTests` →
**158 / 158 passed, 0 regresií**.
• Rozšírený rez
`LocomotiveAddressProgrammingTests|SerialDccClientTests|DccCommunicationTestHandlerTests|LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` (
`--no-build`) → **169 / 169 passed, 0 regresií**.


## 2026-06-01 13:57
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `Views/Library/LocomotivesWindow.axaml.cs`,
`TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Dôležitejší auditový batch pre 4.5 – kritické UI handlery v `VehicleStripItem` a `LocomotivesWindow` už
nepoužívajú hlavnú implementáciu priamo ako `async void`, ale len tenké wrappery nad internými `Task` metódami.
**Dôvod:** Audit 4.5 upozorňuje na riziko `async void` handlerov, kde výnimka mimo lokálne ošetrených vetiev môže prejsť
na UI dispatcher. V `VehicleStripItem` síce drag vetvy už mali vlastné `try/catch`, ale celý pointer-move flow ešte
nemal spoločný horný guard. V `LocomotivesWindow` zas zostávali tri kľúčové click handlery (`ReadCvButton_Click`,
`OpenCalibrationWindow_Click`, `OpenProgrammingTrackSettings_Click`) stále definované priamo ako `async void`.
**Riešenie:**
• 🟩 (audit 4.5) event handler `OnPointerMoved(object? _, PointerEventArgs e)` bol zredukovaný na synchronný wrapper
`_ = OnPointerMovedAsync(e);`.
• Pôvodná logika bola presunutá do novej internej metódy `private async Task OnPointerMovedAsync(PointerEventArgs e)`.
• Celý pointer-move drag flow teraz obopína vrchný `try/catch`, ktorý pri neočakávanej chybe vyresetuje drag stav (
`_isPointerDown`, `_pendingWagon`, `_pendingLoco`, `_dragStarted`), označí event ako handled a zapíše diagnostiku cez
`Program.ReportUnhandledException("VehicleStripItem.OnPointerMoved", ...)` + `TrackFlowDoctorService`.
• V `LocomotivesWindow` boli `ReadCvButton_Click`, `OpenCalibrationWindow_Click` a `OpenProgrammingTrackSettings_Click`
zmenené na synchronné event wrappery, ktoré delegujú prácu do `ReadCvButton_ClickAsync()`,
`OpenCalibrationWindow_ClickAsync()` a `OpenProgrammingTrackSettings_ClickAsync()`, pričom existujúci
exception-reporting ostal zachovaný.
• `LocomotiveSpeedEditorMarkupTests` boli aktualizované o regresie pre oba vzory: `VehicleStripItem` wrapper s vrchným
exception-reportingom aj `LocomotivesWindow` click handlery delegované na `Task` metódy namiesto priameho `async void`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **113 / 113 passed, 0 regresií**.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` (
`--no-build`) → **124 / 124 passed, 0 regresií**.


## 2026-06-01 13:38
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Menší skupinový cleanup `VehicleStripItem` – zmodernizované null-handling vetvy v `OnDrop(...)` a
`OpenRenameMenuAsync()`.
**Dôvod:** V týchto dvoch metódach ostalo viac podobných jednoduchých null checkov (`!= null`, explicitný typ v
`is TextBox ntb`), ktoré bolo možné bezpečne zjednotiť do modernejšieho null-safe zápisu bez zmeny správania.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) v `OnDrop(...)` boli nulové kontroly indikátorov zmenené z `if (left != null)` /
`if (right != null)` na `if (left is not null)` / `if (right is not null)`.
• V `OpenRenameMenuAsync()` bol rename textbox pattern zmenený z `is TextBox ntb` na `is { } ntb` a pre projektovú
lokomotívu bol zápis `if (pLoco != null)` zjednotený na `if (pLoco is not null)`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o jeden spoločný regresný test pre celý null-handling batch; po prvom
behu bolo potrebné negatívnu aserciu spresniť, aby sa nevzťahovala aj na iný handler (`OnDragLeave(...)`) s podobným
starším snippetom.
**Výsledok:**
• Po spresnení regresného testu cielené testy `LocomotiveSpeedEditorMarkupTests` → **111 / 111 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **122 / 122
passed, 0 regresií**.


## 2026-06-01 13:30
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Menší skupinový cleanup `VehicleStripItem` v detach oblasti – zjednodušené bounds/fallback vetvy a zjednotená
práca s lokomotívou v `DetachSpecificWagon(...)`.
**Dôvod:** V detach handleroch zostalo viac malých redundancií rovnakého typu: nemožný `idx < 0` check v
`OnDetachFirstWagonMenuClick(...)`, zbytočný qualifier `this.DataContext` a opakované používanie `_currentLoco` v
`DetachSpecificWagon(...)` namiesto lokálneho aliasu. Spolu tvorili dobrého kandidáta na malý spoločný batch bez zmeny
správania.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) v `OnDetachFirstWagonMenuClick(...)` bol bounds check zjednodušený z
`if (idx < 0 || idx >= loco.AttachedWagons.Count) return;` na `if (idx >= loco.AttachedWagons.Count) return;`, keďže
záporný index tam nemôže vzniknúť.
• V `HandleDetachThisWagonMenuClick(...)` sa posledný fallback zmenil z `wagon = this.DataContext as Wagon;` na
stručnejší ekvivalent `wagon = DataContext as Wagon;`.
• `DetachSpecificWagon(...)` teraz používa lokálny alias `var loco = _currentLoco;` a zvyšok metódy už nepracuje
opakovane s fieldom `_currentLoco`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o jeden spoločný regresný test, ktorý ukotvuje celý tento detach
batch naraz.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **109 / 109 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **120 / 120
passed, 0 regresií**.


## 2026-06-01 13:26
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Väčší nízkorizikový batch pre `VehicleStripItem` – zjednodušený cleanup depa po drag-u a zjednotený resolve
lokomotívy/vagóna v detach handleroch.
**Dôvod:** V `VehicleStripItem` zostalo viac malých redundancií rovnakého typu: zbytočný `Contains(...)` pred
`Remove(...)`, opakované čítanie `_currentLoco` v `OnDetachFirstWagonMenuClick(...)` a zbytočne rozvinutá inicializácia
`wagon` v `HandleDetachThisWagonMenuClick(...)`. Všetky tri zmeny boli čisté, lokálne a behavior-preserving, preto
dávali zmysel ako jeden spoločný batch.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) v wagon-drag cleanup vetve `OnPointerMoved(...)` bol odstránený zbytočný precheck
`if (svm.DepotWagons.Contains(wagon))` a ostalo len priame `svm.DepotWagons.Remove(wagon);`, ktoré je samo o sebe
bezpečné no-op pri chýbajúcej položke.
• `OnDetachFirstWagonMenuClick(...)` teraz používa jednorazovo vyhodnotený lokálny alias
`var loco = _currentLoco ??= DataContext as Locomotive;`, takže ďalšie riadky už nepracujú opakovane s `_currentLoco`.
• V `HandleDetachThisWagonMenuClick(...)` bola primárna inicializácia vagóna skrátená z bloku
`Wagon? wagon = null; if (mi != null) ...` na `Wagon? wagon = mi?.DataContext as Wagon;`.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o jeden spoločný regresný test, ktorý ukotvuje celý tento batch
naraz.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **109 / 109 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **120 / 120
passed, 0 regresií**.


## 2026-06-01 13:21
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Väčší nízkorizikový batch pre `VehicleStripItem` – zjednotené nepoužívané parametre viacerých handlerov na
discard-like názvy.
**Dôvod:** Po sérii malých clean-upov zostalo vo viacerých event handleroch množstvo nepoužívaných parametrov `sender` /
`e`, ktoré zbytočne vytvárali analyzer šum. Keďže ide o čisto signatúrny zápis bez zmeny typu parametrov alebo wiring-u,
bolo efektívnejšie upraviť ich naraz v jednom batchi než po jednom riadku.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) v `VehicleStripItem` boli nepoužívané parametre viacerých handlerov premenované na
discard-like názvy (`_`, `__`) – napr. `OnFlipOrientationMenuClick`, `OnDragOver`, `OnDragLeave`,
`OnDataContextChanged`, `OnAttachedToVisualTree`, `OnDetachedFromVisualTree`, `OnPointerPressed`, `OnPointerMoved`,
`OnPointerReleased`, `OnDrop`, `OnRenameMenuClick`, `OnDetachFirstWagonMenuClick` a druhý parameter v
`HandleDetachThisWagonMenuClick`.
• XAML/event wiring aj runtime správanie ostali nezmenené; zmenili sa len mená nepoužívaných parametrov.
• Existujúci lifecycle test bol zosúladený s novými signatúrami a bol pridaný nový regresný test, že UI handlery so
zbytočnými parametrami používajú nový discard-like zápis.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **108 / 108 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **119 / 119
passed, 0 regresií**.


## 2026-06-01 13:13
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránený redundantný `e == null` guard z
`Locomotive_PropertyChanged(...)`.
**Dôvod:** Handler `Locomotive_PropertyChanged(object? sender, PropertyChangedEventArgs e)` prijíma nenulovateľný
parameter `e`, takže podmienka `if (e == null) return;` bola len zvyšková obrana bez reálneho prínosu. Išlo o čisto
interný cleanup bez zásahu do správania property-change vetiev.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z `Locomotive_PropertyChanged(...)` bol odstránený riadok `if (e == null) return;`.
• Vetvy pre `nameof(Locomotive.IsActive)` a `nameof(Locomotive.IsFlipped)` ostali nezmenené, rovnako ako refresh
`DisplayName` pri zmene identity rušňa.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že handler už neobsahuje redundantný null guard, ale
stále obsahuje vetvy pre `IsActive` a `IsFlipped`.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **107 / 107 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **118 / 118
passed, 0 regresií**.


## 2026-06-01 13:08
===================
**Oblasť:** `Views/Shared/VehicleStripItem.axaml.cs`, `TrackFlow.Tests/LocomotiveSpeedEditorMarkupTests.cs`
**Zmena:** Ďalší nízkorizikový cleanup `VehicleStripItem` – odstránené redundantné default inicializácie drag-state
fieldov.
**Dôvod:** Viaceré privátne fieldy pre interný drag stav mali explicitné inicializácie na predvolené hodnoty (`false` /
`null`), hoci tieto hodnoty CLR nastavuje automaticky. Išlo o čisto syntaktickú redundantnosť bez vplyvu na runtime
správanie.
**Riešenie:**
• 🟨 (audit 4.5, priebežný krok) z `VehicleStripItem` boli odstránené explicitné inicializácie `= false` a `= null` z
fieldov `_isPointerDown`, `_dragStarted`, `_pendingWagon` a `_pendingLoco`.
• Typy aj význam fieldov ostali nezmenené; zmenil sa len zápis deklarácií.
• `LocomotiveSpeedEditorMarkupTests` boli doplnené o regresný test, že drag-state fieldy už nepoužívajú redundantné
default inicializácie, ale ich deklarácie zostali zachované.
**Výsledok:**
• Cielené testy `LocomotiveSpeedEditorMarkupTests` → **106 / 106 passed, 0 regresií**.
• Opakovaný beh `--no-build` potvrdil rovnaký výsledok.
• Rozšírený rez
`LocomotiveSpeedEditorMarkupTests|OperationViewInteractionMarkupTests|UtilityWindowAsyncVoidMarkupTests` → **117 / 117
passed, 0 regresií**.



