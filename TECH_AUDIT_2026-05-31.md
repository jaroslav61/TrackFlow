# Technický audit TrackFlow – 2026-05-31

Audit pokrýva celý projekt (Avalonia UI + MVVM + .NET, DCC vrstva Z21/XpressNet). Každé zistenie je viazané na konkrétny
súbor + riadky a obsahuje navrhovanú akciu. Závažnosť: **🟥 Kritická** / **🟧 Vysoká** / **🟨 Stredná** / **🟦 Nízka /
čistota**.

---

## 1. Konzistencia projektu a MVVM architektúra

### 1.1 🟧 `SettingsWindow` neuvoľňuje `IDisposable` subscriptions

**Súbor:** `Views/Settings/SettingsWindow.axaml.cs:17, 30-33, 45-57`

- V konštruktore sa vytvára `_tabIndexSubscription = tabs.GetObservable(...).Subscribe(...)` (uloženie lambdy ktorá
  zachytáva `this`).
- V `Closing` handleri sa volá iba `AttachToVm(null)` a `ResetCommunicationTestPanels()`, ale **`_tabIndexSubscription`
  sa nikdy nezdisposuje**.
- Pri opätovnom otvorení sa síce `_tabIndexSubscription?.Dispose()` zavolá v ďalšom ctore, ale to je nový inštance
  window – starý ostáva zaháknutý cez observable callback → memory leak rastúci s počtom otvorení Settings.

**Oprava:** v `Closing` rovnako: `_tabIndexSubscription?.Dispose(); _tabIndexSubscription = null;`. Alternatívne
presunúť subscribe do `Opened` / unsubscribe v `Closing`.

### 1.2 🟧 `MainWindowViewModel` – fan-out delegátov bez čistenia

**Súbor:** `ViewModels/MainWindowViewModel.cs:165` (`ShowSettingsDialogAsync`), `Views/MainWindow.axaml.cs:150`

- View nastavuje na VM lambdu zachytávajúcu `this` (
  `_vm.ShowSettingsDialogAsync = () => ShowSettingsDialogAsync(_vm);`). MainWindow je singleton pre lifetime aplikácie,
  takže reálny leak nehrozí, ale rovnaký vzor sa replikuje (CenterEditDialogFactory, PickOpenProjectPathAsync, …).
  Štruktúru oddelenia VM↔View by mal pokryť dedikovaný `IDialogService`, nie viacero `Func<…>` properties. Aktuálne pri
  každom skrytí MainWindow tieto referencie ostávajú aktívne až do explicitného `_vm.ShowSettingsDialogAsync = null` v
  `OnWindowClosing` (riadok 98).

**Oprava:** zaviesť `IDialogService` (Microsoft.Extensions.DependencyInjection registrovaný v `App.axaml.cs`), VM volá
`_dialogService.ShowSettingsAsync()` a delegátové pole zaniknú.

### 1.3 🟨 Zlomené presné oddelenie View ↔ Model

**Súbor:** `ViewModels/Operation/OperationViewModel.cs` (5090 riadkov!)

- VM siaha priamo na `Avalonia.Threading.Dispatcher.UIThread`, drží `IDccCentralClient`, programovacie/CV API aj
  manipuláciu s `TrackLayout`. Trieda preťažuje pojem "ViewModel" – ide o kombináciu Use-Case orchestrátora, controlleru
  režimu, prerezervácie a sieťového routeru.
- Konkrétne 5 090 riadkov, 90+ metód → ťažko testovateľné, vysoká pravdepodobnosť regresie. Rozdeliť na:
    - `OperationRoutingService` (rezervácie, advance window) – už čiastočne v `_reservationEngine`,
    - `OperationSignalCoordinator` (HandleOccupiedBlocks, signály),
    - `OperationDccBridge` (mapovanie loko → DCC).

### 1.4 🟨 `OperationView.OnDataContextChanged` nezuvádza subskripcie

**Súbor:** `Views/Operation/OperationView.axaml.cs:87-121`

- Subskribuje `vm.LayoutRefreshRequested += RefreshLayout;`, `loco.AttachedWagons.CollectionChanged += …`,
  `vm.Locomotives.CollectionChanged += …`.
- Pri zmene DataContextu (napr. otvorenie iného projektu) **starý VM ostáva referenced** a nový sa pridá popri. View
  nemá `Detached` handler, ani sa neuvolňujú handlery starého VM.

**Oprava:** držať `OperationViewModel? _vmCurrent`; pri DataContextChanged najprv odpojiť eventy starého `_vmCurrent`,
až potom uložiť nový + subscribe. Pridať `DetachedFromVisualTree` handler s rovnakým cleanup.

### 1.5 🟨 `ClockViewModel` – Dispose existuje, ale nikto ho nevolá

**Súbor:** `ViewModels/ClockViewModel.cs:24, 119-122`

- ClockViewModel subscribuje `TimeService.Instance.PropertyChanged`. Dispose síce odhlasuje, ale v MainWindow je clock
  view len zobrazený ako pop-up a tu len `_clockView?.Close()` (`Views/MainWindow.axaml.cs:468`) bez disposovania VM.

**Oprava:** v `ClockView.Closed` priamo `(DataContext as IDisposable)?.Dispose();` alebo ClockView držať singleton.

### 1.6 🟨 `VagonsWindow.axaml.cs` / `LocomotivesWindow.axaml.cs`

**Súbory:** `Views/Library/VagonsWindow.axaml.cs:114`, `Views/Library/LocomotivesWindow.axaml.cs:725, 848, 858`

- VagonsWindow správne odhlasuje `_vm.PropertyChanged -= …` v `Closing`.
- LocomotivesWindow však pridáva inline lambdy do `posSlider.PropertyChanged += (_, e) => …` (riadky 848, 858) bez
  ukladania delegátu → pri opätovnom otvorení každého locomotive editora pribudne ďalší slider event handler, ktorý drží
  referenciu na VM ⇒ rastúci leak.

**Oprava:** uložiť handlery do polí `_onPosSliderPropChanged`, odhlasovať v `Closing`/`Detached`.

### 1.7 🟦 `App.axaml.cs:50` – `System.Windows.Forms.Application.Exit()` z čistého Avalonia projektu

- Avalonia projekt nepotrebuje WinForms a importovanie ho aktivuje WinForms message-loop ATM. Z hľadiska čistoty MVVM
  toto patrí preč.

---

## 2. Duplicita a mŕtvy kód

### 2.1 🟧 Duplikované enkódovanie krátkej/dlhej DCC adresy

**Súbor:** `Services/Dcc/Z21Client.cs:201-202, 223-224, 924-928, 956-959` a `DccAddressCodec.cs:29-37`

Vzorec

```csharp
byte adrH = (byte)(address > 127 ? (0xC0 | (address >> 8)) : 0x00);
byte adrL = (byte)(address & 0xFF);
```

je v Z21Client zopakovaný **4×** (drive, function, POM read, POM write). `DccAddressCodec.EncodeLongAddress(...)` už
existuje s identickou logikou pre CV17/CV18 (XPN MSB byte je rovnaký koncept).

**Oprava:** rozšíriť `DccAddressCodec` o:

```csharp
public static (byte Hi, byte Lo) EncodeLocoAddress(int address);
```

a všetky 4 miesta v Z21Client refaktorovať na jednu funkciu (≈ −40 LOC, eliminácia driftu pri budúcich úpravách).

### 2.2 🟧 Mŕtve metódy v `SerialDccClient`

**Súbor:** `Services/Dcc/SerialDccClient.cs`

- `ReadResponseFrameAsync` (riadky 549-590) – privátna, **nikde nie volaná** (potvrdené grepom).
- `ReadByteWithTimeoutAsync` (592-611) – volaná iba z `ReadResponseFrameAsync`, takže rovnako mŕtva.
- `BuildStatusSummary` (733-742) – privátna, nikde nie volaná.

**Akcia:** zmazať, redukuje ~120 LOC. Aktuálna „živá" cesta používa lokálnu `ReadRawResponseAsync` (riadok 225).

### 2.3 🟧 Duplicitné catch bloky v `VehicleStripItem.axaml.cs`

**Súbor:**
`Views/Shared/VehicleStripItem.axaml.cs:182, 202, 211, 643, 657, 694, 697, 700, 727, 736, 766-767, 929, 943, 973`

15 prázdnych `catch { }` blokov, často 4 za sebou na obalenie jediného `Cursor = …`. Maskuje pravé výnimky a komplikuje
diagnostiku.

**Oprava:** vytvoriť `Helpers/ControlExceptionGuard.cs` so statickou `TrySetCursor`/`TryCapturePointer` a logovaním do
`TrackFlowDoctorService`. Použiť všade.

### 2.4 🟨 Trojnásobne identický CV write/read packet builder

**Súbor:** `Services/Dcc/Z21Client.cs:917-975`

`CreatePomCvReadPacket` a `CreatePomCvWritePacket` zdieľajú 90 % tela (rovnaká adresa, optCv H/L, XOR cyklus). Refaktor:
`BuildPomCvPacket(loco, cv, value, opCode)`.

### 2.5 🟦 Zakomentovaný kód / TODO bez tracking-u

- `Views/Editor/LayoutEditorView.axaml.cs:254-259` – `CheckAndCreateCrossings()` len komentár, telo prázdne → preč alebo
  presunúť do issue trackeru.
- `Services/Dcc/Z21Client.cs:1319` – „Sem v budúcnosti pribudnú ďalšie ramce..." komentár treba previesť na issue.
- Komentáre s `OPRAVENÉ:` (Z21Client.cs:279) označujúce historický fix kazia signál → odstrániť.

### 2.6 🟨 Konvertery s opakovanou kostrou

Adresár `Converters/` má 19 prevažne identických `IValueConverter`-ov. Väčšina je single-purpose a duplikuje pattern
`Convert: cast → switch → return`. Stoja za zhrnutie do generickej `LambdaConverter<TIn,TOut>` alebo radšej použiť *
*CommunityToolkit.MVVM `[ObservableProperty]` + Avalonia 11 funkčné bindingy**, čo by ich časť úplne eliminovalo.

---

## 3. Efektivita a logika kódu

### 3.1 🟥 Sync-over-async blokujúci UI thread

**Súbor:** `ViewModels/Operation/OperationViewModel.cs:867, 3143`

```csharp
return HandleOccupiedBlocks(layout, dccClient: null, ct: default, sendDcc: false)
        .GetAwaiter().GetResult();           // riadok 867
... AdvanceReservationWindowInternalAsync(...).GetAwaiter().GetResult();   // riadok 3143
```

`AdvanceReservationWindow` (3136) je volaný zo synchronných eventov (napr. R-BUS feedback). `HandleOccupiedBlocks`
reaguje na `RefreshSignalStatus()` ktorá je volaná z UI/property setterov.

**Riziká:**

- Ak `HandleOccupiedBlocks` interne spustí `await` ktorý sa nakoniec marshaluje cez `Dispatcher.UIThread.InvokeAsync`,
  vznikne **deadlock**, lebo blokujeme presne ten UI thread.
- Pri 100+ blokoch trvá synchronný beh viditeľne (UI zamrzne).

**Oprava:** poskytnúť čisto synchronnú verziu (bez `await` ciest) alebo previesť volajúcich na `async`. Volajúce kódy:

- `RefreshSignalStatus()` – už má async dvojičku, treba ju používať vo všetkých konzumentoch.
- `AdvanceReservationWindow` – publikovať len `Task` verziu a všetky synchronné call-sites zrefaktorovať (
  `fire-and-forget` cez `Dispatcher.UIThread.Post` je krajne nevhodný; lepšie zaviesť job queue na pozadí +
  `Channel<…>`).

### 3.2 🟥 `Z21Client.Dispose()` / `StopTelemetry()` – blokujúci shutdown

**Súbor:** `Services/Dcc/Z21Client.cs:1195, 1423`

```csharp
public void Dispose()
{
    BeginDisconnectAsync(waitForCompletion: true).GetAwaiter().GetResult();
    _sendLock.Dispose();
}
```

`Dispose` sa volá zo `MainWindow.OnWindowClosed` (`Views/MainWindow.axaml.cs:431, 480`) → blokuje UI thread. Vnútorne
čaká na ukončenie `MainReceiveLoopAsync` + `TelemetryPollLoopAsync`. Pri pomalej sieti / „stuck" socketu Avalonia hlavné
okno mrzne, kým socket fyzicky nepadne.

**Oprava:**

- `IDisposable` symbolicky implementovať s timeoutom (`Task.Wait(TimeSpan.FromSeconds(2))`).
- Lepšie: prejsť na `IAsyncDisposable` a v `OnWindowClosed` použiť `await … DisposeAsync()` (Avalonia podporuje async
  close pattern cez `WindowClosingEventArgs`).

### 3.3 🟧 `DccConnectionService.GetEffectiveSnapshot()` synchronne čaká na `SemaphoreSlim`

**Súbor:** `Services/Dcc/DccConnectionService.cs:292`

```csharp
private EffectiveSettings GetEffectiveSnapshot()
{
    _settingsLock.Wait();          // blokuje thread – ak je UI thread, môže zaspať
    ...
}
```

Synchronná verzia je volaná z `EnsureClientFromEffective()` (riadok 318) ktorá je verejná. Ak `_settingsLock` drží práve
`GetEffectiveSnapshotAsync` čakajúci na storage I/O, UI thread sa blokuje.

**Oprava:** `EnsureClientFromEffective` urobiť `EnsureClientFromEffectiveAsync` a v `ConnectAsync` ho volať pred
Connect.

### 3.4 🟧 `RefreshLayout` v `LayoutEditorView` – plný rebuild celého plátna pri každej zmene

**Súbor:** `Views/Editor/LayoutEditorView.axaml.cs:233-299`

`OnVmPropertyChanged` rebuilduje celý canvas na zmenu `SelectedElement` / `InspectorAngle` /
`InspectorSignalHighlightVersion`. Pre layout s 200 prvkami sa pri každom kliknutí znovuvytvára 200 `Border`/`Image`
controlov.

**Oprava:**

- Použiť `ItemsControl` s `Canvas` ako ItemsPanel a databindovať na pozorovateľnú kolekciu (Avalonia urobí diff sám).
- Selekcia by mala byť oddelená vrstva (overlay) ktorá sa renderuje nezávisle.

`OperationView.RefreshLayout` (`Views/Operation/OperationView.axaml.cs:136-196`) má podobný problém, ale aspoň cachuje
route hosty (`_routeHostCache`). Aplikujte rovnakú techniku všade.

### 3.5 🟨 `Task.Run(async () => …)` bez sledovania chýb v `Z21Client.StartTelemetry`

**Súbor:** `Services/Dcc/Z21Client.cs:1148, 1176`

```csharp
_ = Task.Run(async () =>
{
    try { ... await SendAsync(registerTelemetryPacket, ...); }
    catch (Exception ex) { Diagnose(... Warning); }
});
```

OK z hľadiska výnimiek (sú zachytené), ale **send sa púšťa nezávisle od `_telemetryCts`** – pri rýchlom
`Connect/Disconnect/Connect` sa môže registračný `LAN_SET_BROADCASTFLAGS` poslať z predchádzajúceho cyklu cez už nový
socket alebo naopak na zatvorený socket. Treba previazať tieto fire-and-forget tasky s `_telemetryCts.Token` alebo
radšej `await` priamo v hlavnom toku connect-u.

### 3.6 🟨 `ObservableCollection` z worker thread-u

**Súbor:** `ViewModels/StatusBarViewModel.cs`, `ViewModels/Settings/SettingsViewModel.cs`

R-BUS feedback aj `ConnectionStateChanged` chodia z pozadia. Treba zaručiť, že každá manipulácia s
`ObservableCollection` (`StatusBarCentralItem`, `ConfiguredCentrals`, `SmartStripsViewModel`) prebehne cez
`Dispatcher.UIThread.Post`. Grep ukázal že napr. `MainWindowViewModel.cs:883` o tom má len komentár – treba auditovať
každý event handler. Inak Avalonia hodí `InvalidOperationException: collection modified during enumeration` v UI thread
renderi.

---

## 4. Bug hunting – ošetrenie výnimiek a off-by-one

### 4.1 🟥 R-BUS / S88 mapovanie groupy – chybný off-by-one

**Súbor:** `Services/Dcc/Z21Client.cs:1375-1391`

```csharp
int firstDirectModuleAddress = data[4] + 1;          // ← BUG pre group=1
for (int byteIndex = 5; byteIndex < data.Length; byteIndex++)
    PublishRBusModuleState(firstDirectModuleAddress + (byteIndex - 5), data[byteIndex], data);
```

Podľa **Z21 LAN špec. v1.13**: `LAN_RMBUS_DATACHANGED` posiela `GroupIndex` ∈ {0, 1}; group 0 reprezentuje moduly *
*1..10**, group 1 moduly **11..20**. Jeden rámec nesie 10 bajtov masiek (jeden bajt = 8 vstupov modulu).

Aktuálna formula `data[4] + 1`:

- group 0 → first = 1 ✅
- group 1 → first = 2 ❌ (má byť 11)

V aktívnom kóde sa zatiaľ posiela len `LAN_RMBUS_GETDATA(group=0)` (riadok 1128, 1180), takže bug manifestuje **iba**
keď centrála spontánne pošle group 1 alebo keď fyzická konfigurácia má >10 R-BUS modulov / S88. Ale moduly 11–20 by sa
potom mapovali na adresy 2–11 a kolidovali s existujúcimi blokmi!

**Oprava:**

```csharp
int firstDirectModuleAddress = data[4] * 10 + 1;
```

+ pridať unit test pre obe groupy v `TrackFlow.Tests`.

### 4.2 🟧 R-BUS publikácia – „initial mask = 0xFF" hlásí všetky vstupy ako zmenené

**Súbor:** `Services/Dcc/Z21Client.cs:1399-1418`

```csharp
byte changedMask = hadPreviousMask ? (byte)(previousMask ^ mask) : (byte)0xFF;
```

Pri prvom prijatí rámca z modulu publikuje `RBusFeedbackChanged` pre **všetkých 8 portov** (aj keď väčšina je v stave
0). To môže spustiť `DccFeedbackLayoutApplier` ktorý:

- pre `isActive=false` len skontroluje aktuálny stav indikátora,
- ale eventy sa logujú do Doctora a uvíta sa 100+ zápisov na štart každej centrály.

**Oprava:** pri `hadPreviousMask == false` publikovať len bity, ktoré sú v `mask` skutočne `1` (čiže
`changedMask = mask`). Bity `0` netreba meniť – default je 0 v UI.

### 4.3 🟧 Prázdne / generické `catch` v sieťových slučkách

**Súbor:** `Services/Dcc/Z21Client.cs:1186-1190, 1241, 1248, 1278-1287, 1027-1028`

```csharp
catch
{
    // best-effort only
}
```

V `MainReceiveLoopAsync` to čiastočne dáva zmysel (po výnimke krátka pauza), ale **chýba whitelist typov výnimiek**, čo
môže zamaskovať:

- `OutOfMemoryException`,
- `StackOverflowException` (síce neodchytiteľná, ale signál),
- programátorský `NullReferenceException` v `DispatchIncomingFrame` (ten by mal padnúť aby sme videli bug).

**Oprava:**

```csharp
catch (SocketException) { … }
catch (Exception ex) when (!IsShutdownRequested)
{
    TrackFlowDoctorService.Instance.Diagnose("DCC",
        $"⚠️ MainReceiveLoop chyba: {ex.GetType().Name}: {ex.Message}",
        DiagnosticLevel.Warning);
    await Task.Delay(50, ct);
}
```

### 4.4 🟧 Účtovanie `_lastSpeedSentTicks` nie je thread-safe

**Súbor:** `Services/Dcc/Z21Client.cs:95-99, 192-199`

```csharp
private long _lastSpeedSentTicks = 0;
...
var elapsed = _speedStopwatch.ElapsedMilliseconds;
if (!isStop && (elapsed - _lastSpeedSentTicks) < SpeedThrottleMs) return;
_lastSpeedSentTicks = elapsed;
```

`SetLocomotiveSpeedAsync` môže byť volaná z viacerých UI eventov (CabStrip + drag) súbežne. Read-modify-write bez
`Interlocked` → throttle môže prepustiť 2 pakety za <80 ms, čo Z21 odpovedá `LAN_X_BC_BUSY` (typický symptóm „škubavé
rozbiehanie").

**Oprava:** `Interlocked.Read` / `Interlocked.Exchange` alebo prejsť na lock.

### 4.5 🟨 `async void` – nesprávne pre handlery ktoré komunikujú so sieťou

**Súbor:** zoznam volaní (20 výskytov), kľúčové:

| Súbor                                                           | Riadok             | Riziko                                         |
|-----------------------------------------------------------------|--------------------|------------------------------------------------|
| `Views/MainWindow.axaml.cs:335`                                 | `OnWindowClosing`  | výnimka v `await`-e ukončí proces              |
| `Views/Editor/LayoutEditorView.axaml.cs:2009, 2028, 2072, 2115` | properties dialog  | properties window otvorenie môže hodiť → crash |
| `Views/Operation/OperationView.axaml.cs:1162`                   | `OnCanvasLocoDrop` | DCC errors                                     |
| `Views/Library/LocomotivesWindow.axaml.cs:137, 273, 293, 1246`  | CV / calibration   | sieťové chyby utopia aplikáciu                 |

**Oprava:** každý takýto handler obaliť do `try/catch (Exception ex) { Diagnose(...) }`, alebo presunúť logiku do
RelayCommandu.

### 4.6 🟨 Hardcoded `locoAddress=3` v CV READ/WRITE

**Súbor:** `Services/Dcc/SerialDccClient.cs:160, 494`, `Services/Dcc/Z21Client.cs` (POM)

Default `int locoAddress = 3` sa pretláča aj cez programovaciu cestu – pre Service Mode je adresa irelevantná (modul je
riadený fyzicky), ale pre POM je toto **silent bug**: keď volajúci zabudne predať adresu, zápis pôjde na 3 (Märklin
default), čo môže nešťastne preprogramovať reálny dekoder.

**Oprava:** odstrániť default value pre POM cesty, pre Service Mode signature čisto bez `locoAddress`.

### 4.7 🟨 `SetTurnoutAsync` – výhybkový bit-pattern neobsahuje výber výstupu

**Súbor:** `Services/Dcc/Z21Client.cs:262-263`

```csharp
byte data = (byte)((activate ? 0x09 : 0x08));
```

Komentár vraví „bit3=activate, bit2=queue(0), bit1=0, bit0=výstup(0=priamo, 1=odbočka)", ale fixne sa posiela
0x09/0x08 – výstup je *vždy 1* keď `activate==true`, *vždy 0* keď false. To znamená že **API neumožňuje výber druhého
stavu výhybky bez „activate"**. V Z21 protokole správna sémantika je posielať dvojicu: `(activate=1,out=0)` pre rovno,
`(activate=1,out=1)` pre odbočku, potom druhý paket `(activate=0,out=…)` na de-energizáciu. Tu chýba parameter
`bool branch`.

**Oprava:** rozšíriť signature: `SetTurnoutAsync(int address, bool branch, bool activate, …)`.

---

## 5. DCC komunikácia (Network & Hardware vrstva)

### 5.1 🟧 Z21 keep-alive je „heavy" (kreuje nový socket)

**Súbor:** `Services/Dcc/Z21Client.cs:175-183`

```csharp
public async Task<bool> PingAsync(...)
{
    var serial = await TryGetSerialOnceAsync(_remoteEp, ct);   // krátkodobý nový UDP socket
    ...
}
```

Oficiálny návod Roco pre keep-alive je posielať `LAN_X_GET_STATUS` (alebo `LAN_SYSTEMSTATE_GETDATA` – ten už beží v
`TelemetryPollLoopAsync` každé 2 s). Vytvárať každý cyklus nový UDP socket je zbytočné a:

- zaberá ephemeral porty (po reštarte centrály krátkodobo `WSAEADDRINUSE`),
- pri NAT-e môže poslať z iného portu než pôvodný socket → centrála to nemusí spárovať.

**Oprava:** `PingAsync` len overí `IsConnected` a vráti `true` – pravidelný polling `LAN_SYSTEMSTATE_GETDATA` v
`TelemetryPollLoopAsync` zachová NAT mapovanie aj overí responsivitu centrály cez `MainReceiveLoopAsync`.
`PerCentralConnection.MonitorLoopAsync` potom len detekuje že telemetria mlčí > N ms.

### 5.2 🟧 `LAN_SET_BROADCASTFLAGS = 0x0111` – chýbajú flagy pre loko-info echo

**Súbor:** `Services/Dcc/Z21Client.cs:1148-1166`

Maska `0x00000111` = X-Bus + R-Bus + SystemState. Chýbajú:

- `0x00000002` – `LAN_LOCONET_DETECTOR` (LocoNet),
- `0x00010000` – `LAN_RAILCOM_DATACHANGED`,
- `0x00040000` – `LAN_LOCONET_Z21_RX` ak treba duplex feedback.

Pre projekt cieli na R-BUS + S88 to môže byť OK, ale pri integrácii LocoNet detektorov treba pridať.

**Oprava:** vytvoriť `Z21BroadcastFlags` `[Flags]` enum, parametrizovať masku podľa `eff.FeedbackSource` (R-BUS /
LocoNet / RailCom). Najmä: vyhnúť sa „magic number"-u 0x00000111.

### 5.3 🟧 `PerCentralConnection.StopMonitor()` nečaká na `_monitorTask`

**Súbor:** `Services/Dcc/PerCentralConnection.cs:109-115, 300-307`

```csharp
private void StopMonitor()
{
    try { _monitorCts?.Cancel(); } catch { }
    _monitorCts?.Dispose();        // môže hodiť ObjectDisposedException v práve bežiacom Task.Delay(... ct)
    _monitorCts = null;
    _monitorTask = null;            // task ostáva visieť na pozadí
}
```

- Dispose `CancellationTokenSource` skôr ako task uvidí cancel → v `MonitorLoopAsync` riadok 129
  `await Task.Delay(_monitorOptions.IntervalMs, ct)` vyhodí `ObjectDisposedException` namiesto
  `OperationCanceledException`.
- `_monitorTask = null` zruší našu referenciu, ale beh ostáva (race condition pri Disconnect → Connect).

**Oprava:**

```csharp
private async Task StopMonitorAsync()
{
    var cts = Interlocked.Exchange(ref _monitorCts, null);
    var task = Interlocked.Exchange(ref _monitorTask, null);
    if (cts == null) return;
    try { cts.Cancel(); } catch { }
    try { if (task != null) await task.ConfigureAwait(false); }
    catch (OperationCanceledException) { }
    cts.Dispose();
}
```

Dispose najprv počká, potom uvoľní CTS.

### 5.4 🟧 `MonitorLoopAsync` – pri `IsConnected==false` a vypnutom `AutoConnect` nečaká

**Súbor:** `Services/Dcc/PerCentralConnection.cs:145-154`

V tej vetve `continue;` znamená nový loop: ale `Task.Delay(_monitorOptions.IntervalMs, ct)` je až na začiatku ďalšej
iterácie → OK, pauza je. Avšak ihneď nato `disconnectNotified` zostane `true` aj keď sa link zase pripojí inou cestou (
manuálny Connect). Po `OnPerCentralStateChanged` z `Connect` by sa to dalo resetovať, ale nemá kto –
`disconnectNotified` zostane true → ďalšie strata spojenia sa NEoznámi.

**Oprava:** pri Connect-e v `PerCentralConnection.ConnectAsync` po úspechu resetovať flag (vyžaduje vystaviť ho cez
metódu alebo reštartovať monitor).

### 5.5 🟨 `Z21Client.SetExtendedAccessoryAspectAsync` – chybný 11-bit mask

**Súbor:** `Services/Dcc/Z21Client.cs:280`

```csharp
byte adrH = (byte)((addr >> 8) & 0x07);   // 3 bity
```

Extended Accessory v Z21 LAN špec. má **11-bitovú adresu** → high byte by mal byť `& 0x1F` (resp. `& 0x07` pre basic,
`& 0x1F` pre extended). Tu sa použila basic maska – pre adresy >2047 sa MSB orežú a paket pôjde na nesprávnu adresu.

**Oprava:** overiť v Z21 protokol PDF v1.13 sekcia 4.2.4 (`LAN_X_SET_EXT_ACCESSORY`); pravdepodobne
`adrH = (byte)((addr >> 8) & 0x07)` je správne pre 0..2047, ale potom by validácia mala odmietnuť >2047. Aktuálne
validácia chýba.

### 5.6 🟨 `SendAsync` swallow-uje SocketException ticho

**Súbor:** `Services/Dcc/Z21Client.cs:309-317`

```csharp
catch (SocketException) when (ct.IsCancellationRequested || IsShutdownRequested) { ... }
catch (Exception)
{
    if (!IsShutdownRequested)
        IsConnected = false;        // bez loggingu
}
```

Pri pravej sieťovej chybe (kabel out, ICMP unreachable) sa `IsConnected = false` bez záznamu do Doctora. Užívateľ uvidí
len že UI hlási „odpojené" bez vysvetlenia.

**Oprava:** logovať `ex.GetType()/Message` (Warning) pred set `IsConnected=false`.

---

## 6. Úroveň a profesionalita projektu

### Silné stránky

- ✅ Z21 protokol je komentovaný s odkazom na bajtové štruktúry – nadštandard.
- ✅ Centrálny diagnostický kanál (`TrackFlowDoctorService`) je konzistentne používaný.
- ✅ Dobré pokrytie testov (XUnit pre DccConnectionService, OperationVM rezervácie, SerialDccClient).
- ✅ Použitie `CommunityToolkit.Mvvm` (`ObservableObject`, `[RelayCommand]`) je idiomatické.
- ✅ `DccAddressCodec`, `SignalAspectExtensions` – pekné drobné value-type service.

### Slabé stránky

- ❌ Trieda `OperationViewModel` 5 090 LOC, `Z21Client` 1 427 LOC, `SerialDccClient` 1 093 LOC – porušuje SRP.
- ❌ V niekoľkých Views (Editor, Operation, VehicleStrip) je zmiešaná UI logika s biznis modelmi (priame manipulácie s
  `BlockElement.IsLocked`, `Locomotive.IsFlipped`).
- ❌ Pomenovanie: zmes slovenčiny + angličtiny v identifikátoroch (`_lastSpeedSentTicks` vs `_telemetryPollTask`),
  komentáre prevažne SK ale identifikátory EN. Stanoviť konvenciu (odporúčam EN identifikátory, SK len v diagnostických
  správach pre používateľa).
- ❌ Magic numbers: `0x00000111`, `SpeedThrottleMs = 80`, `RBusPollIntervalMs`. Sústrediť do konfigu `Z21Config`.
- ❌ Chýbajú `nullable enable` pre veľkú časť projektu (skontroluj v `Directory.Build.props`); nullable annotations
  zachytia veľa potenciálnych NRE.

---

## 7. Priorizovaný akčný plán

| #  | Závažnosť | Súbor                                           | Akcia                                                                                            |
|----|-----------|-------------------------------------------------|--------------------------------------------------------------------------------------------------|
| 1  | 🟥        | `Z21Client.cs:1383`                             | Opraviť R-BUS group formulu `data[4] * 10 + 1`. Doplniť test                                     |
| 2  | 🟥        | `OperationViewModel.cs:867, 3143`               | Odstrániť `.GetAwaiter().GetResult()` z UI cesty                                                 |
| 3  | 🟥        | `Z21Client.Dispose():1421-1425`                 | Prejsť na `IAsyncDisposable` alebo Dispose s timeoutom                                           |
| 4  | 🟧        | `SettingsWindow.axaml.cs:45-57`                 | Dispose `_tabIndexSubscription` v `Closing`                                                      |
| 5  | 🟧        | `PerCentralConnection.cs:109-115`               | `StopMonitor` musí čakať na `_monitorTask`                                                       |
| 6  | 🟧        | `Z21Client.cs` × 4                              | Centralizovať loco-address encoding do `DccAddressCodec`                                         |
| 7  | 🟧        | `Z21Client.cs:1399`                             | `changedMask = mask` pri initial fetch (eliminácia false-positives)                              |
| 8  | 🟧        | `Z21Client.cs:1148-1166`                        | Tasky vo `StartTelemetry` previazať s `_telemetryCts`                                            |
| 9  | 🟧        | `Z21Client.PingAsync:175`                       | Nahradiť heavy ping ľahkým `LAN_X_GET_STATUS` cez `_sendUdp`                                     |
| 10 | 🟧        | `Views/Operation/OperationView.axaml.cs:87-121` | Odhlasovať handlery starého VM pri DataContextChanged                                            |
| 11 | 🟧        | `SerialDccClient.cs:549-742`                    | Zmazať mŕtve metódy (`ReadResponseFrameAsync`, `ReadByteWithTimeoutAsync`, `BuildStatusSummary`) |
| 12 | 🟧        | `Z21Client.cs:309-317`                          | Logovať real SocketException pred `IsConnected=false`                                            |
| 13 | 🟨        | `VehicleStripItem.axaml.cs` ×15                 | Nahradiť `try {} catch {}` Helper s diagnostikou                                                 |
| 14 | 🟨        | `LocomotivesWindow.axaml.cs:848, 858`           | Uložiť slider handlery, odhlásiť v `Closing`                                                     |
| 15 | 🟨        | `LayoutEditorView.axaml.cs:233-299`             | Per-element update namiesto full canvas rebuild                                                  |
| 16 | 🟨        | `DccConnectionService.cs:292, 316`              | `EnsureClientFromEffectiveAsync` (async cesta)                                                   |
| 17 | 🟨        | `Z21Client.cs:192-199`                          | Throttle so `Interlocked`                                                                        |
| 18 | 🟨        | `Z21Client.cs:262-266`                          | `SetTurnoutAsync` rozšíriť o `branch` parameter                                                  |
| 19 | 🟨        | `Z21Client.cs:280`                              | Rozšíriť validáciu adresy v `SetExtendedAccessoryAspectAsync`                                    |
| 20 | 🟨        | `MainWindowViewModel.cs:165`                    | Refactor delegátov na `IDialogService`                                                           |
| 21 | 🟦        | `App.axaml.cs:50`                               | Odstrániť závislosť na WinForms                                                                  |
| 22 | 🟦        | Adresár `Converters/`                           | Konsolidovať podobné konvertery                                                                  |
| 23 | 🟦        | Celý projekt                                    | Aktivovať `<Nullable>enable</Nullable>` a opraviť warning-y                                      |

---

## Príloha A – Konkrétne čísla projektu

| Metrika                                             | Hodnota             |
|-----------------------------------------------------|---------------------|
| `OperationViewModel.cs`                             | 5 090 LOC, 1 trieda |
| `Z21Client.cs`                                      | 1 427 LOC           |
| `SerialDccClient.cs`                                | 1 093 LOC           |
| `LayoutEditorView.axaml.cs`                         | 3 182 LOC           |
| Počet `async void` handlerov                        | 20                  |
| Počet prázdnych `catch { }`                         | 20                  |
| Počet `.GetAwaiter().GetResult()` v produkčnom kóde | 5                   |
| Konvertery v `Converters/`                          | 19                  |

---

## Príloha B – Doporučené nové testy (T-DD)

1. `Z21ClientRBusGroupTests`
    - `Parses_Group0_StartsAtModule1`
    - `Parses_Group1_StartsAtModule11` ← **najdôležitejší (chytí bug 4.1)**
2. `Z21ClientInitialMaskTests`
    - `InitialFrame_PublishesOnlyActiveBits` (bug 4.2)
3. `PerCentralConnectionTests`
    - `Dispose_WaitsForMonitorLoopToFinish`
    - `Reconnect_AfterIntermittentLoss_ResetsDisconnectFlag`
4. `OperationViewModelThreadingTests`
    - `RefreshSignalStatus_DoesNotBlock_UiThread_WhenDccAvailable`

---

*Audit vyhotovil GitHub Copilot, 2026-05-31.*

