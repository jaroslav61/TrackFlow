# Samples

Tento priecinok obsahuje demo subory pre rychle vizualne testy.

## Demo layout pre navestidla

Subor: `demo-layout.trackflow.json`

Obsahuje:

- 3 bloky (`Blok A`, `Blok B`, `Blok C`),
- prepojovacie useky,
- 4 signal markery oznacene ako demo pre 2/3/4/5 znakove navestidla,
- 1 ukazkovu cestu `Demo A -> C`.

Poznamka:

- Aktualny marker signalu je zatial jednotny.
- Typy navestidiel (2/3/4/5 znakov) su v tomto dema iba ilustracne cez nazvy a umiestnenie.

## Demo 2 - stanica + zhlavie

Subor: `demo-stanica-zhlavie.trackflow.json`

Obsahuje:

- tratovy blok, dva stanicne bloky a odchodovy blok,
- dve vyhybky (`V1`, `V2`) ako jednoduche zhlavie,
- viac signalov (vjazd, odjazdy, posun),
- 3 ukazkove cesty: priama, odbocka, posun.

Poznamka:

- Cielom je vizualna predstavivost buduceho navrhu navestnych sustav, nie finalna prevadzkova logika.

## Demo 3 - Faza 2.1 (runtime route + DCC)

📋 **Podrobný návod na testovanie: [`TESTOVANIE_FAZA_2_1.md`](TESTOVANIE_FAZA_2_1.md)**

Subor: `demo-faza-2-1-runtime.trackflow.json`

Obsahuje:

- startovy blok `Blok A`, cielove bloky `Blok B` (priamo) a `Blok C` (odbocka),
- 1 vyhybku `V1`,
- 1 startove navestidlo `S1 start` priradene na `SignalRightId` bloku A,
- 2 route definicie:
    - `r_straight` (A -> B, vyhybka priamo),
    - `r_diverge` (A -> C, vyhybka odbocka),
- 2 route markery v operation rezime:
    - `PRIAMO A -> B`,
    - `ODBOCKA A -> C`,
- 1 demo lokomotivu `Demo 754`.

### Ako to vyskusat

1. Otvor projekt `demo-faza-2-1-runtime.trackflow.json`.
2. Prejdi do rezimu Prevadzka.
3. V zozname lokomotiv vyber `Demo 754`.
4. Pretiahni lokomotivu na `Blok A - start` (aby mala zdrojovy blok).
5. Klikni na route marker `PRIAMO A -> B`.
6. Po dokonceni presunu vrat lokomotivu spat na `Blok A - start`.
7. Klikni na route marker `ODBOCKA A -> C`.

### Co ocakavat

- Pri route `PRIAMO A -> B`:
    - startove navestidlo `S1` sa prepne na hornu zltu (`Yellow`, ocakavaj Stoj),
    - po obsadeni prveho bloku za navestidlom sa zhodi na `Red`,
    - route sa uvolni.

- Pri route `ODBOCKA A -> C`:
    - startove navestidlo `S1` sa prepne na dolnu zltu (`LowerYellow`),
    - po obsadeni prveho bloku za navestidlom sa zhodi na `Red`,
    - route sa uvolni.

- Pri deaktivacii route (alebo deactivate all) sa dotknute navestidla vratia na `SafetyFallbackAspect` (`Red`).

### Poznamka k warning scenaru

Ak v editore z bloku A odstranis priradenie startoveho navestidla pre smer `Right` (SignalRightId),
aktivacia route prebehne fail-safe a v logu uvidis warning o chybajucom navestidle.

## Demo 4 - Faza 2.2 (look-ahead / UpperYellowBlinking)

📋 **Podrobný návod na testovanie: [`TESTOVANIE_FAZA_2_2.md`](TESTOVANIE_FAZA_2_2.md)**

Subor: `demo-faza-2-2-lookahead.trackflow.json`

Obsahuje:

- hlavnu linku Blok A → Blok B → Blok C s dvoma navestidlami (S1 pri A, S2 pri B),
- odbocku Blok A → Blok D cez vyhybku V1,
- 3 route definicie: `r_ab` (priamo), `r_bc` (priamo), `r_ad` (odbocka),
- 2 demo lokomotivy: `Demo 754` (adresa 3) a `Demo 040E` (adresa 7).

### Preco look-ahead nefunguje priamo z UI

Look-ahead (UpperYellowBlinking) vyzaduje, aby boli dve cesty aktívne sucasne.
V simulacnom rezime to brani **bezpecnostny mechanizmus** (Blok B je obsadeny/zamknuty
pocas r_bc aktivacie). Primarnym overenim su preto **unit testy**:

```powershell
dotnet test TrackFlow.Tests --filter "DisplayName~LookAhead"
# Vysledok: 21/21 testov OK
```

## Multi-route diagnosticke samples

Tieto schemy su urcene na realne prevadzkove testovanie multi-route runtime spravania,
WAIT retry scenarov, ownership handover a cleanupu rezervacii.

### Ako citat tieto schemy

- horny rad / horna vetva typicky reprezentuje **cestu 1**
- dolny rad / dolna vetva typicky reprezentuje **cestu 2**
- route markery zacinaju cislami `1.` a `2.`; tieto cisla zaroven odporucaju poradie manualneho spustenia
- textovy banner hore hovori hlavnu pointu scenara
- ak je v strede **blok X**, ide zvycajne o zdieľany blokovy konflikt
- ak je v strede **V1**, ide zvycajne o zdieľanú výhybku / zdieľané vlastníctvo výhybky
- pri sample s **jednou jednoduchou vyhybkou** moze byt druha cesta vedena **opacnym smerom**, pretoze jedna bezna
  vyhybka ma iba 3 vetvy a nevie sama vytvorit 4 nezavisle koncove body
- detailny vyznam scenara, odporucany postup a ocakavane spravanie su vzdy v samostatnom `.md` subore pri sample

Ak schema pri prvom pohlade posobi "nedokoncene", ber ju ako **diagnosticku testovaciu maketu**, nie ako realisticku
stanicu.
Je zamerne mala, aby bolo na obrazovke hned vidiet iba konfliktne miesto a reakciu runtime.

### SharedBlockWaitSample

- Projekt: `SharedBlockWaitSample.trackflow.json`
- README: `SharedBlockWaitSample.md`
- Na obrazovke: dva starty (`Blok A`, `Blok C`) a jedna spolocna konfliktova zona `X`.
- Zmysel: ide o abstraktnu logicku maketu konfliktu zdieľaného vlastníctva bloku; prvy vlak obsadi `X`, druhy musi
  prejst do WAIT a po uvolneni `X` pokracovat.
- Poznamka: tento sample nepredstavuje doslovny fyzicky 4-portovy blok, ale zjednodusenu diagnosticku reprezentaciu
  runtime konfliktu.

### SharedTurnoutWaitSample

- Projekt: `SharedTurnoutWaitSample.trackflow.json`
- README: `SharedTurnoutWaitSample.md`
- Na obrazovke: cesta 1 ide `A -> B`, cesta 2 ide `D -> A`; obe pouzivaju jednu vyhybku `V1 zdieľaná`.
- Zmysel: schema je upravena tak, aby bola s jednou jednoduchou vyhybkou fyzicky mozna; druha cesta preto ide opacnym
  smerom a caka na odovzdanie `V1`.

### ParallelIndependentRoutesSample

- Projekt: `ParallelIndependentRoutesSample.trackflow.json`
- README: `ParallelIndependentRoutesSample.md`
- Na obrazovke: dve oddelene linky `A -> B` a `C -> D` bez spolocnych blokov aj bez spolocnej vyhybky.
- Zmysel: obe cesty sa maju dat pustit naraz bez WAIT a bez ownership konfliktu.

### DeadlockPotentialSample

- Projekt: `DeadlockPotentialSample.trackflow.json`
- README: `DeadlockPotentialSample.md`
- Na obrazovke: dve cesty zdieľaju dvojicu blokov `X` a `Y`, ale v opacnom poradi.
- Zmysel: schema sluzi na diagnostiku starvation / deadlock rizika, nie na automaticke vyriesenie.

### TailClearReleaseSample

- Projekt: `TailClearReleaseSample.trackflow.json`
- README: `TailClearReleaseSample.md`
- Na obrazovke: cesta 1 ide `A -> X -> B`, cesta 2 ide `D -> X -> A`; spolocna infra je `Blok X + V1`.
- Zmysel: schema je s jednou vyhybkou fyzicky mozna a sluzi na sledovanie, ci sa `X` a `V1` neuvolnia prilis skoro a ci
  druha cesta dostane sancu az po tail-clear.

### Generator samples

Ak budes chciet tieto sample nanovo vygenerovat, pouzi skript:

```powershell
powershell -ExecutionPolicy Bypass -File .\Samples\generate-multi-route-samples.ps1
```

## Odporúčané poradie otvárania sample scenárov

Pre prvé testovanie odporúčam ísť v tomto poradí:

1. `ParallelIndependentRoutesSample.trackflow.json`
    - najjednoduchší kontrolný scenár
    - ukáže, ako vyzerá súbežná jazda **bez konfliktu**

2. `SharedBlockWaitSample.trackflow.json`
    - prvý konfliktný scenár
    - ľahko viditeľný WAIT na zdieľanom bloku `Blok X`

3. `SharedTurnoutWaitSample.trackflow.json`
    - druhý konfliktný scenár
    - ukáže ownership konflikt nad výhybkou `V1`

4. `TailClearReleaseSample.trackflow.json`
    - nadväzuje na konfliktné scenáre
    - sleduj timing release-u po prejazde celého vlaku

5. `DeadlockPotentialSample.trackflow.json`
    - otváraj až nakoniec
    - je zámerne rizikový a slúži skôr na diagnostiku starvation / deadlock správania než na „peknú“ ukážku

