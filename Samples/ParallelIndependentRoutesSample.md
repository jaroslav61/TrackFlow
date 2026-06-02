# ParallelIndependentRoutesSample

## Čo schéma znázorňuje

Táto schéma je kontrolný scenár bez konfliktu.

- horná cesta je `A -> B`
- dolná cesta je `C -> D`
- žiadne bloky ani výhybky nie sú spoločné

## Odporúčaný testovací postup

1. Otvor `ParallelIndependentRoutesSample.trackflow.json`.
2. Polož prvú lokomotívu do `Blok A - linka 1`.
3. Polož druhú lokomotívu do `Blok C - linka 2`.
4. Aktivuj obe route takmer naraz.
5. Sleduj, že ani jedna cesta neprejde do WAIT.

## Očakávané správanie

- v Doctor okne sa objavia route, block a signal udalosti
- nemá sa objaviť WAIT kvôli ownership konfliktu
- obe cesty sa aktivujú, prejdú a dobehnú bez zásahu do seba navzájom
