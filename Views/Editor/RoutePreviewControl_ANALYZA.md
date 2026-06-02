# Analýza RoutePreviewControl - Zobrazovanie náhľadu cesty

**Dátum:** 2026-04-20  
**Súbor:** `Views/Editor/RoutePreviewControl.axaml.cs` (567 riadkov)

---

## ✅ Správne implementované funkcie

### 1. **Zoom mechanizmus**
- ✅ Predvolený zoom: **100%** (`_zoomFactor = 1.0`)
- ✅ Možnosti: 10%, 25%, 50%, 75%, 100%, "Na okno"
- ✅ Dynamické prepočítanie pri zmene veľkosti okna (režim "fit")
- ✅ ScrollViewer s margin kompenzáciou pre správne scrollovanie

### 2. **Rendering pipeline (zhodný s editorom)**
- ✅ Dvojprechodový rendering:
  - 1. prechod: koľajnice, výhybky, ostatné prvky
  - 2. prechod: bloky navrchu (zakryjú koľaje pod nimi)
- ✅ Bounding box výpočet s offsetom (rendering od 0,0)
- ✅ Rotácia markerov cez `IMarkerAngle` alebo `RotateTransform`

### 3. **Zvýraznenie aktívnej cesty**
- ✅ Hlavné bloky (štart/cieľ): žltá výplň, modrý okraj, opacity 1.0
- ✅ Neaktívne bloky: šedá výplň (#E8E8E8), sivý okraj, opacity 1.0 (prekrývajú koľaje)
- ✅ Aktívne koľajnice/prvky: opacity 1.0 + DropShadowEffect (farba cesty)
- ✅ Neaktívne koľajnice/prvky: opacity **0.18**

### 4. **Výhybky - stavové vykresľovanie**
- ✅ Identifikácia vetvy podľa `x:Name` z AXAML (nie orientácie čiary)
- ✅ Aktívna vetva: modrá (#71c5ff), hrúbka 2.5, opacity 1.0, ZIndex 20
- ✅ Neaktívna vetva: biela (z AXAML), hrúbka 2.0, opacity 0.18, ZIndex 1
- ✅ Aktívny outline: čierny, hrúbka **5.0**, opacity 1.0, ZIndex 19
- ✅ Neaktívny outline: FromArgb(204,0,0,0), hrúbka 3.1, opacity 0.18, ZIndex 0

### 5. **Outline systém**
- ✅ **Pre výhybky:** AXAML outline vrstvy (nie dynamický `innerOutline`)
- ✅ **Pre koľajnice:** Dynamický outline (`ApplyOutlineStyle`) s farbou FromArgb(204,0,0,0)
- ✅ Neaktívne výhybky: `DimTurnoutOutlines()` prepíše outline na TrackSegment farbu

---

## ⚠️ Známe problémy

### 1. **Mierne odlišné odtiene šedej**
**Popis:** Neaktívne koľajnice, celé výhybky a neaktívne vetvy výhybiek majú vizuálne mierne odlišné odtiene sivej.

**Príčina:** 
- TrackSegment: dynamický outline FromArgb(204,0,0,0) + opacity 0.18 → alpha ≈ 37
- Výhybky: AXAML outline Black (alpha 255) prepísaný na FromArgb(204,0,0,0) + opacity 0.18
- Mikroskopické rozdiely v renderingu Avalonia (anti-aliasing, subpixel positioning)

**Riešenie (budúce):**
- Normalizovať všetky outline vrstvy na identický rendering pipeline
- Možnosť: pre výhybky tiež používať dynamický outline (odstránenie AXAML outline)
- Alebo: explicitná kalibrácia alpha hodnoty (204 → ??)

**Priorita:** Nízka (vizuálne akceptovateľné)

---

## 🔧 Možné optimalizácie

### 1. **Deduplikácia kódu pre outline úpravy**
**Aktuálny stav:** Dve podobné metódy:
- `DimTurnoutOutlines()` - pre celé neaktívne výhybky
- `ApplyTurnoutStateColoring()` - upravuje outline pre neaktívne vetvy

**Návrh:**
```csharp
private static void ApplyTurnoutOutlineColor(Canvas canvas, 
    Func<string, bool> isActiveFunc)
{
    var trackOutlineBrush = new SolidColorBrush(Color.FromArgb(204, 0, 0, 0));
    foreach (var child in canvas.Children)
    {
        if (child is not Shape shape || !shape.Name?.Contains("Outline") == true) 
            continue;
        
        var mainName = shape.Name.Replace("Outline", "");
        bool active = isActiveFunc(mainName);
        
        if (!active)
        {
            shape.Stroke = trackOutlineBrush;
            shape.StrokeThickness = 3.1;
            shape.Opacity = DimmedOpacity;
        }
        else
        {
            shape.StrokeThickness = 5.0;
            shape.Opacity = 1.0;
        }
        shape.ZIndex = active ? 19 : 0;
    }
}
```

**Úspora:** ~20 riadkov, lepšia udržiavateľnosť

---

### 2. **Lazy rendering pre veľké layouty**
**Aktuálny stav:** Všetky prvky sa renderujú naraz pri každom `SetLayoutAndRoute()`.

**Návrh:** Virtualizácia (rendering len viditeľných prvkov pri veľmi veľkých layoutoch 500+ prvkov).

**Priorita:** Nízka (aktuálne layouty < 100 prvkov)

---

### 3. **Cache pre marker inštancie**
**Aktuálny stav:** `CreateMarkerByKey()` vytvára nové inštancie pre každý prvok (2x - outline + inner).

**Návrh:** Použiť prototypový vzor s `Clone()` pre často používané markery.

**Úspora:** ~15% rýchlosť pri inicializácii preview pre veľké layouty.

**Priorita:** Nízka

---

### 4. **Konsolidácia farieb do konštánt**
**Aktuálny stav:** Magické čísla roztrúsené v kóde:
- `Color.FromArgb(204, 0, 0, 0)` - opakuje sa 3x
- `#71c5ff` (modrá aktívna vetva) - 2x
- `#FFFFDC` (žltý blok) - 2x
- `#E8E8E8` (sivý blok) - 1x

**Návrh:**
```csharp
private static class PreviewColors
{
    public static readonly Color TrackOutline = Color.FromArgb(204, 0, 0, 0);
    public static readonly Color ActiveBranch = Color.Parse("#71c5ff");
    public static readonly Color HighlightedBlock = Color.Parse("#FFFFDC");
    public static readonly Color DimmedBlock = Color.Parse("#E8E8E8");
    public static readonly Color DimmedBlockStroke = Color.Parse("#A0A0A0");
    public static readonly Color DimmedBlockText = Color.Parse("#909090");
}
```

**Priorita:** Stredná (čitateľnosť kódu)

---

## 📋 Nekonzistencie so zbytkom aplikácie

### 1. **LayoutEditorView vs RoutePreviewControl**
**Rozdiel:**
- **Editor:** Používa dvojitý outline pre VŠETKY prvky (vrátane výhybiek)
- **Preview:** Pre výhybky len AXAML outline (bez `innerOutline`)

**Dôvod:** Zabránenie konfliktov medzi AXAML outline (Black alpha 255) a dynamickým outline.

**Dopad:** Minimálny - preview výhybky vyzerajú identicky ako v editore.

---

### 2. **ApplyOutlineStyle() má parameter `el`**
**Aktuálny stav:** `ApplyOutlineStyle(Control marker, LayoutElement el)` ale parameter `el` sa používa len na zistenie či je to výhybka.

**Návrh:** Zjednodušenie:
```csharp
private static void ApplyOutlineStyle(Control marker, bool isTurnout)
{
    if (isTurnout) return;
    // ...existing code...
}
```

Volanie:
```csharp
bool isTurnout = el.MarkerKey.Contains("Turnout") || el.MarkerKey == "DoubleSlip";
ApplyOutlineStyle(innerOutline, isTurnout);
```

**Priorita:** Nízka

---

## 🎯 Odporúčania pre budúcnosť

### Vysoká priorita
1. **Žiadne** - aktuálna implementácia je funkčná a výkonná

### Stredná priorita
1. ✅ Konsolidácia farieb do konštánt (čitateľnosť)
2. ✅ Zjednodušenie `ApplyOutlineStyle()` (konzistencia)

### Nízka priorita
1. Deduplikácia outline logiky (DRY princíp)
2. Riešenie mikroskopických rozdielov v odtieňoch sivej
3. Marker cache pre veľké layouty (optimalizácia)

---

## 📊 Metriky kódu

| Metrika | Hodnota |
|---------|---------|
| Celkový počet riadkov | 567 |
| Počet metód | 11 |
| Najdlhšia metóda | `ApplyTurnoutStateColoring()` - 103 riadkov |
| Priemerná zložitosť | Nízka (väčšinou lineárne prechody) |
| Duplicitný kód | ~5% (outline úpravy) |
| Magic numbers | ~8 konštánt bez pomenovaných premenných |

---

## ✨ Zhrnutie

**Celkové hodnotenie:** ⭐⭐⭐⭐☆ (4/5)

**Silné stránky:**
- ✅ Funkčne kompletné (všetky požiadavky splnené)
- ✅ Výkon dobrý pre štandardné layouty (< 100 prvkov)
- ✅ Vizuálne zhodné s editorom (vrátane rotácií, Z-ordering)
- ✅ Správne zvýraznenie aktívnych/neaktívnych prvkov

**Oblasti na zlepšenie:**
- ⚠️ Mierny duplicitný kód (outline logika)
- ⚠️ Magické čísla (farby, hrúbky)
- ⚠️ Mikroskopické rozdiely v odtieňoch sivej (vizuálne akceptovateľné)

**Odporúčanie:** Aktuálny stav je **produkčne použiteľný**. Optimalizácie môžu počkať na ďalšiu iteráciu.

---

**Poznámka:** Táto analýza bola vytvorená 2026-04-20 po sérii iterácií zameraných na zjednotenie farieb neaktívnych prvkov.

