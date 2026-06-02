# SharedBlockWaitSample

## Čo schéma znázorňuje

Táto schéma je najjednoduchší prípad **ČAKANIA na zdieľaný blok**.

Je zámerne **abstraktná**: `X` tu reprezentuje konflikt zdieľaného runtime vlastníctva, nie doslovný fyzický jeden rovný blok so štyrmi vetvami.

- horná cesta ide `A -> X -> B`
- dolná cesta ide `C -> X -> D`
- spoločný konflikt je `Blok X - logická kolízna zóna`

Pointa ukážky:
- prvý vlak si zarezervuje a obsadí `Blok X`
- druhý vlak sa k `X` dostane neskôr a musí prejsť do ČAKANIA
- po uvoľnení `X` má druhý vlak pokračovať bez potreby ručného zásahu

Použi túto ukážku na diagnostiku vlastníctva bloku.
Ak chceš fyzicky plausibilnejšiu koľajovú geometriu so zdieľaním infra, použi radšej `SharedTurnoutWaitSample` alebo `TailClearReleaseSample`.

## Odporúčaný testovací postup

1. Otvor `SharedBlockWaitSample.trackflow.json`.
2. Polož prvú lokomotívu do `Blok A - vlak 1`.
3. Spusť route `1. A -> B cez X`.
4. Polož druhú lokomotívu do `Blok C - vlak 2`.
5. Spusť route `2. C -> D cez X (WAIT)`.
6. Sleduj, že druhá cesta čaká práve na `Blok X - logická kolízna zóna` a po jej uvoľnení pokračuje.

## Očakávané správanie

- v Doctor okne sa objavia udalosti ČAKANIA, vlastníctva bloku, životného cyklu cesty a blokovania návestidlom
- druhá cesta dostane vstup do ČAKANIA a opakované retry pokusy
- po tail-clear sa zdieľaný blok uvoľní a druhá cesta pokračuje
