# TailClearReleaseSample

## Čo schéma znázorňuje

Táto schéma testuje, či sa zdieľaná infra neuvoľní priskoro.

- cesta 1 ide `A -> X -> B`
- cesta 2 ide `D -> X -> A`
- obe cesty používajú spoločnú časť `Blok X + V1`
- druhá cesta ide opačným smerom, aby topológia s jednou výhybkou ostala fyzicky možná

Pointa ukážky:
- potvrdiť, že uvoľnenie nepríde už pri vstupe čela do ďalšieho bloku
- potvrdiť, že infra sa uvoľní až po prejazde chvosta vlaku

## Ako čítať obrázok

- `Blok A - štart 1 / cieľ 2` je spoločný ľavý blok sample-u
- `Blok D - štart 2` je pravý spodný štart pre opačný smer
- `V1 tail-clear` a `Blok X - zdieľaný tail-clear` sú spoločné body vlastníctva
- marker cesty `1.` spusti ako prvý, marker cesty `2.` potom sleduj ako čakajúci scenár

## Odporúčaný testovací postup

1. Otvor `TailClearReleaseSample.trackflow.json`.
2. Polož lokomotívu 1 do `Blok A - štart 1 / cieľ 2` a spusti route 1.
3. Polož lokomotívu 2 do `Blok D - štart 2` a spusti route 2.
4. Sleduj, že cesta 2 nedostane zdieľanú infra okamžite pri vstupe prvého vlaku do ďalšieho bloku.
5. Sleduj uvoľnenie až po tail-clear.

## Čo testovať

- sleduj, že `Blok X - tail-clear` a `V1 tail-clear` ostanú držané až do prejazdu celého vlaku
- druhá cesta má po uvoľnení dostať šancu pokračovať

## Očakávané správanie

- v Doctor okne sa objavia udalosti bloku, výhybky a uvoľnenia súvisiace s tail-clear
- uvoľnenie nesmie prísť hneď pri vstupe čela vlaku do ďalšieho bloku
- ukážka je vhodná na sledovanie leakov rezervácií po prejazde a na potvrdenie správneho načasovania uvoľnenia
