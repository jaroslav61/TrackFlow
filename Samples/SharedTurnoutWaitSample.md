# SharedTurnoutWaitSample

## Čo schéma znázorňuje

Táto schéma znázorňuje **jednu zdieľanú výhybku** v topológii, ktorá je fyzicky vytvoriteľná.

- cesta 1 je `A -> B` (priamo)
- cesta 2 je `D -> A` (opačný smer cez odbočku)
- obe cesty používajú tú istú výhybku `V1 zdieľaná`
- horná cesta potrebuje na `V1` stav **priamo / Straight**
- opačný smer potrebuje na `V1` stav **odbočka / Diverge**

Pointa sample-u:
- jedna jednoduchá výhybka má iba 3 vetvy, preto tu nemôžu existovať dve nezávislé cesty typu `A -> B` a `C -> D` len s jednou výhybkou
- prvá aktivovaná cesta si vezme vlastníctvo výhybky `V1`
- druhá cesta nesmie výhybku prehodiť pod aktívnym vlakom
- preto prejde do WAIT a čaká na odovzdanie vlastníctva `V1`

## Ako čítať obrázok

- `Blok A - štart 1 / cieľ 2` je spoločný ľavý uzol sample-u
- `Blok B - cieľ 1` je cieľ prvej cesty
- `Blok D - štart 2` je štart druhej cesty v opačnom smere
- route marker `1.` znamená odporúčanú prvú aktiváciu
- route marker `2.` znamená odporúčanú druhú aktiváciu

## Odporúčaný testovací postup

1. Otvor `SharedTurnoutWaitSample.trackflow.json`.
2. V režime Prevádzka polož prvú lokomotívu do `Blok A - štart 1 / cieľ 2`.
3. Spusť route marker `1. A -> B (získa V1)`.
4. Polož druhú lokomotívu do `Blok D - štart 2`.
5. Spusť route marker `2. D -> A (WAIT na V1)`.
6. Sleduj, že druhá cesta čaká, kým prvá nepustí výhybku `V1`.

## Čo testovať

- dve cesty zdieľajú výhybku `V1 zdieľaná`
- jedna cesta potrebuje stav `Straight`, druhá stav `Diverge`
- druhá cesta musí čakať na uvoľnenie vlastníctva výhybky

## Očakávané správanie

- v Doctor okne sa objavia udalosti súvisiace s turnout ownership a WAIT
- pri prvej ceste sa výhybka zarezervuje a preloží do požadovaného stavu
- pri druhej ceste sa najprv objaví odmietnutie / WAIT, potom po uvoľnení úspešné prevzatie výhybky
- druhá cesta sa nesmie rozbehnúť skôr, než prvá výhybku reálne uvoľní
