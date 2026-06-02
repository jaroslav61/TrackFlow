# DeadlockPotentialSample

## Čo schéma znázorňuje

Táto schéma je zámerne konfliktná a slúži len na diagnostiku.

- horná cesta ide `A -> X -> Y -> B`
- dolná cesta ide `C -> Y -> X -> D`
- obe cesty chcú tie isté zdieľané bloky, ale v opačnom poradí

## Odporúčaný testovací postup

1. Otvor `DeadlockPotentialSample.trackflow.json`.
2. Polož lokomotívy do `Blok A - štart 1` a `Blok C - štart 2`.
3. Aktivuj obe cesty v krátkom odstupe.
4. Sleduj správanie ČAKANIA a vlastníctvo blokov `X` a `Y`.
5. Ak vznikne dlhé ČAKANIE, skús jednu cestu zrušiť a sleduj uvoľnenie rezervácií.

## Očakávané správanie

- v Doctor okne sa majú objaviť výrazné udalosti ČAKANIA, BLOKU a CESTY
- sample je vhodný na sledovanie starvation / patovej situácie a uvoľnenia po zrušení
- aktuálne sa neočakáva inteligentné riešenie patovej situácie
