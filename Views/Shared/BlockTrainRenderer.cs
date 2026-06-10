using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TrackFlow.Models;
using TrackFlow.Services;

namespace TrackFlow.Views.Shared;

/// <summary>
/// JEDNOTNĂ‰ vykresÄľovanie vlaku (lokomotĂ­va + vagĂłny + bodka + nĂˇzov) v bloku layoutu.
/// 
/// PrincĂ­p:
/// 1. CANONICAL HORIZONTAL ORIENTATION
///    - Vlak postavĂ­me vĹľdy ako horizontĂˇlny stack ikon v "kanonickej" orientĂˇcii.
///    - Forward (smer jazdy = +): hlava lokomotĂ­vy vpravo, kanonickĂ© poradie [WagonsLeft][Loco][WagonsRight].
///    - Backward (smer jazdy = â’): hlava lokomotĂ­vy vÄľavo, ScaleX=-1 na ikone loky,
///      kanonickĂ© poradie [WagonsRight rev][Loco flip][WagonsLeft rev].
///    - Bodka (ÄŤelo) vĹľdy v rohu strecha+hlava, t.j. VerticalAlignment=Top a HorizontalAlignment podÄľa flipu.
/// 
/// 2. WHOLE-STACK TRANSFORM PRE VERTIKĂLNY BLOK
///    - Aplikuje sa JEDNA rotĂˇcia +90Â° CW na celĂ˝ stack.
///    - Forward (head right, kanonickĂ©) + 90Â° CW => head DOWN  (Forward = dole). âś“
///    - Backward (head left,  kanonickĂ©) + 90Â° CW => head UP    (Backward = hore). âś“
///    - Bodka, vagĂłny, lokomotĂ­va â€” vĹˇetko sa transformuje rovnako, Ĺľiadne per-icon hacky.
///    - Ĺ˝IADNE ScaleY=-1 vĂ˝nimky, Ĺ˝IADNE direction-conditional dot-positions.
/// 
/// 3. ALIGNMENT V BLOKU
///    - HorizontĂˇlny: Forward = HorizontalAlignment.Right, Backward = .Left.
///    - VertikĂˇlny:   Forward = VerticalAlignment.Bottom,  Backward = .Top.
///      (LokomotĂ­va mĂˇ byĹĄ pri tom konci bloku, kam jej hlava smeruje.)
/// 
/// 4. NĂZOV LOKOMOTĂŤVY
///    - HorizontĂˇlny blok: ÄľavĂ˝ hornĂ˝ roh (malĂ˝, neutrĂˇlny).
///    - VertikĂˇlny blok: rotovanĂ˝ â’90Â° (ÄŤĂ­tanie zdola hore), umiestnenĂ˝ na opaÄŤnĂ˝ koniec neĹľ lokomotĂ­va.
/// </summary>
public static class BlockTrainRenderer
{
    public const int DefaultMaxVisibleWagons = 4;
    private const double IconHeight = 16;

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // PURE LOGIC (testovateÄľnĂ© bez Avalonia UI subsystĂ©mu)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>Plán vykreslenia vlaku — čistý dátový výsledok <see cref="ComputePlan"/>.</summary>
    /// <remarks>
    /// Lokomotíva je docknutá na head-edge bloku (pomocou DockPanel.Dock).
    /// Vagóny ju nasledujú a pri overflow sa orežú na vzdialenom konci — loko zostáva vždy viditeľná.
    /// </remarks>
    public readonly record struct TrainLayoutPlan(
        bool ShouldFlipLocoIcon,
        double StackRotationDeg,
        HorizontalAlignment HorizontalAlignment,
        VerticalAlignment VerticalAlignment,
        Dock LocoDock,
        IReadOnlyList<int> WagonIndicesInOrder,
        int HiddenWagonsCount,
        DotCorner DotCorner);

    /// <summary>KanonickĂ˝ roh, do ktorĂ©ho sa umiestni zelenĂˇ bodka (ÄŤelo lokomotĂ­vy).</summary>
    public enum DotCorner
    {
        /// <summary>KanonickĂˇ Forward (hlava vpravo) â€” bodka v pravom hornom rohu ikony.</summary>
        TopRight,
        /// <summary>KanonickĂˇ Backward (hlava vÄľavo) â€” bodka v Äľavom hornom rohu ikony.</summary>
        TopLeft,
    }

    /// <summary>
    /// VypoÄŤĂ­ta layout plĂˇn vlaku ÄŤisto z dĂˇtovĂ˝ch vstupov. NevytvĂˇra Ĺľiadne UI kontroly
    /// â€” vĂ˝stup je plne overiteÄľnĂ˝ jednotkovĂ˝mi testami.
    /// </summary>
    public static TrainLayoutPlan ComputePlan(
        TrainOrientation orientation,
        bool isLocoUserFlipped,
        int locoPosition,
        int totalWagons,
        int maxVisibleWagons = DefaultMaxVisibleWagons)
    {
        if (maxVisibleWagons < 0) maxVisibleWagons = 0;
        if (totalWagons < 0) totalWagons = 0;
        locoPosition = Math.Clamp(locoPosition, 0, totalWagons);

        bool isVertical = orientation.IsVertical();
        bool isForward  = orientation.IsForward();

        bool canonicalLeftFacing = !isForward;
        bool shouldFlipLoco = canonicalLeftFacing ^ isLocoUserFlipped;

        // Vagóny v poradí "od loky von": najskôr "after" (idx >= locoPosition),
        // potom "before" v reverze (idx locoPosition-1 .. 0). To zachová
        // LocoPosition sémantiku: vagóny pripojené na "predný" coupler loky
        // sú vizuálne najbližšie k nej, "zadné" sú ďalej.
        int wagonsToShow = Math.Min(totalWagons, maxVisibleWagons);
        var ordered = new List<int>(wagonsToShow);
        int afterCount = Math.Min(totalWagons - locoPosition, wagonsToShow);
        for (int i = 0; i < afterCount; i++) ordered.Add(locoPosition + i);
        int remaining = wagonsToShow - afterCount;
        for (int i = 0; i < remaining; i++) ordered.Add(locoPosition - 1 - i);

        int hidden = totalWagons - wagonsToShow;
        double rot = isVertical ? 90.0 : 0.0;

        // KANONICKÝ DOCK (Right/Left) — panel je layoutovaný ako HORIZONTÁLNY pred 90° rotáciou.
        // Po 90° CW rotácii (vertikálne bloky) sa mapuje:
        //   kanonické Right  → screen Bottom  (VDown = Forward, head dole)  ✓
        //   kanonické Left   → screen Top     (VUp   = Backward, head hore) ✓
        // Pre horizontálne bloky (bez rotácie) ostáva mapovanie 1:1.
        // POZOR: NESMIE sa použiť Dock.Bottom/Top pre vertikálne — DockPanel by lokomotíve dal
        // FULL WIDTH × icon height (16 px), čo by zožralo celý priestor a vagóny by sa prekrývali.
        var dock = isForward ? Dock.Right : Dock.Left;

        HorizontalAlignment hAlign;
        VerticalAlignment   vAlign;
        if (isVertical)
        {
            // Vertikálne: LayoutTransformControl musí dostať plnú výšku bloku,
            // aby DockPanel (po un-rotácii horizontálny ~ blockH × IconHeight) mal kde rozprestrieť vagóny.
            // BEZ Stretch by sa LTC zmenšil na natural size a vagóny by sa stlačili do jedného miesta.
            hAlign = HorizontalAlignment.Center;
            vAlign = VerticalAlignment.Stretch;
        }
        else
        {
            hAlign = HorizontalAlignment.Stretch;  // vagóny vypĺňajú šírku
            vAlign = VerticalAlignment.Center;
        }

        var dotCorner = shouldFlipLoco ? DotCorner.TopLeft : DotCorner.TopRight;

        return new TrainLayoutPlan(
            ShouldFlipLocoIcon: shouldFlipLoco,
            StackRotationDeg: rot,
            HorizontalAlignment: hAlign,
            VerticalAlignment: vAlign,
            LocoDock: dock,
            WagonIndicesInOrder: ordered,
            HiddenWagonsCount: Math.Max(0, hidden),
            DotCorner: dotCorner);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    // UI RENDERING (Avalonia)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    /// <summary>
    /// VytvorĂ­ kompletnĂ˝ vizuĂˇl vlaku pre blok (novĂˇ signatĂşra s enum orientĂˇciou).
    /// </summary>
    public static Control CreateTrainVisual(
        Locomotive loco,
        TrainOrientation orientation,
        double blockW,
        double blockH,
        bool showName = true,
        int maxVisibleWagons = DefaultMaxVisibleWagons,
        double visualOpacity = 1.0)
    {
        var plan = ComputePlan(
            orientation,
            loco.IsFlipped,
            loco.LocoPosition,
            loco.AttachedWagons.Count,
            maxVisibleWagons);

        // DockPanel: loko docknutá na head-edge, vagóny v StackPanel vypĺňajú zvyšok.
        Control? locoControl = BuildLocoIcon(loco, plan.ShouldFlipLocoIcon, plan.DotCorner);

        bool isForwardOrientation = orientation.IsForward();

        // FLOW DIRECTION: kľúčový trik pre správne poradie vagónov a smer overflow.
        //
        // WagonIndicesInOrder má semantiku "od loky von" → [W0, W1, W2, ...] kde
        // W0 musí byť VIZUÁLNE NAJBLIŽŠIE k lokomotíve. StackPanel však rendruje
        // children zľava doprava od svojho ľavého okraja:
        //
        //   FORWARD  (loko docknutá vpravo, slot vagónov vľavo):
        //     - LeftToRight by dal W0 najľavejšie (NAJĎALEJ od loky) — ZLE.
        //     - RightToLeft → W0 pri PRAVOM okraji stacku (= adjacent loko) ✓
        //     - Overflow nastane vľavo → orežú sa najvzdialenejšie vagóny ✓
        //
        //   BACKWARD (loko docknutá vľavo, slot vagónov vpravo):
        //     - LeftToRight → W0 pri ĽAVOM okraji stacku (= adjacent loko) ✓
        //     - Overflow nastane vpravo → orežú sa najvzdialenejšie vagóny ✓
        //
        // Toto pravidlo platí aj pre vertikálne bloky — celý stack sa rotuje
        // o 90° CW, ale kanonická logika ostáva rovnaká (Forward = canonical-right).
        var wagonsStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            IsHitTestVisible = false,
            FlowDirection = isForwardOrientation
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight,
        };

        foreach (var idx in plan.WagonIndicesInOrder)
            AddWagon(wagonsStack.Children, loco.AttachedWagons[idx]);

        if (plan.HiddenWagonsCount > 0)
        {
            wagonsStack.Children.Add(new TextBlock
            {
                Text = $"+{plan.HiddenWagonsCount}",
                FontSize = 10,
                Foreground = Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
                FlowDirection = FlowDirection.LeftToRight,  // text sa nesmie zrkadliť
            });
        }

        // Názov vlaku - pridáme priamo do wagonsStack ako posledný element
        // Bude tesne za posledným vagónom / +N indikátorom
        if (showName && !string.IsNullOrEmpty(loco.Name))
        {
            wagonsStack.Children.Add(new TextBlock
            {
                Text = loco.Name,
                FontSize = 11,
                Foreground = Brushes.Black,
                IsHitTestVisible = false,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Opacity = 0.75,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0),
                FlowDirection = FlowDirection.LeftToRight,  // text sa nesmie zrkadliť
            });
        }

        // Border s ClipToBounds=true je 100% spoľahlivý mechanizmus orezania
        // (ClipToBounds priamo na StackPaneli niekedy nezaberie kvôli measure/arrange
        // sekvencii). Border rezervuje slot a HARD-clipuje obsah na svojich hraniciach.
        var wagonsClipper = new Border
        {
            Child = wagonsStack,
            ClipToBounds = true,
            IsHitTestVisible = false,
            Background = Brushes.Transparent,
        };

        var trainDock = new DockPanel
        {
            IsHitTestVisible = false,
            Opacity = Math.Clamp(visualOpacity, 0.0, 1.0),
            LastChildFill = true,
        };

        // 1. Lokomotíva - docknutá na head-edge
        if (locoControl != null)
        {
            DockPanel.SetDock(locoControl, plan.LocoDock);
            trainDock.Children.Add(locoControl);
        }

        // 2. Vagóny + názov - LastChildFill (dostanú zvyšok priestoru)
        //    Názov je už súčasťou wagonsStack, tesne za posledným vagónom
        trainDock.Children.Add(wagonsClipper);

        // Pri vertikálnom bloku potrebujeme aby rotácia ZASIAHLA layout (RenderTransform
        // ho neovplyvňuje – Avalonia by stack stále meral ako horizontálny a vyčnieval
        // by mimo blok). Riešenie: LayoutTransformControl, ktorý merí už rotovaný obsah.
        Control stackHost;
        if (plan.StackRotationDeg != 0)
        {
            stackHost = new LayoutTransformControl
            {
                Child = trainDock,
                LayoutTransform = new RotateTransform(plan.StackRotationDeg),
                IsHitTestVisible = false,
            };
        }
        else
        {
            stackHost = trainDock;
        }

        stackHost.HorizontalAlignment = plan.HorizontalAlignment;
        stackHost.VerticalAlignment   = plan.VerticalAlignment;
        stackHost.Margin = new Thickness(2);

        var container = new Grid
        {
            Width = blockW,
            Height = blockH,
            IsHitTestVisible = false,
        };
        container.Children.Add(stackHost);


        return container;
    }

    /// <summary>
    /// SpĂ¤tne kompatibilnĂˇ signatĂşra (isVertical, isForwardDir) â€” delegĂˇt na enum variantu.
    /// </summary>
    public static Control CreateTrainVisual(
        Locomotive loco,
        bool isVertical,
        bool isForwardDir,
        double blockW,
        double blockH,
        bool showName = true)
        => CreateTrainVisual(
            loco,
            TrainOrientationExtensions.From(isVertical, isForwardDir),
            blockW, blockH, showName);

    private static Control? BuildLocoIcon(Locomotive loco, bool shouldFlipLoco, DotCorner dotCorner)
    {
        var locoIcon = LoadIconImage(loco.IconName);
        if (locoIcon == null) return null;

        locoIcon.Height = IconHeight;
        locoIcon.Stretch = Stretch.Uniform;

        if (shouldFlipLoco)
        {
            locoIcon.RenderTransform = new ScaleTransform { ScaleX = -1, ScaleY = 1 };
            locoIcon.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        var dot = new Ellipse
        {
            Width = 4,
            Height = 4,
            Fill = Brushes.Lime,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = dotCorner == DotCorner.TopLeft
                ? HorizontalAlignment.Left
                : HorizontalAlignment.Right,
            Margin = new Thickness(0, -1, 0, 0),
        };

        var grid = new Grid();
        grid.Children.Add(locoIcon);
        grid.Children.Add(dot);
        return grid;
    }

    private static void AddWagon(ICollection<Control> seq, Wagon wagon)
    {
        var icon = LoadIconImage(wagon.IconName);
        if (icon == null) return;
        icon.Height = IconHeight;
        icon.Stretch = Stretch.Uniform;
        seq.Add(icon);
    }

    /// <summary>
    /// Vytvorí TextBlock s názvom vlaku - určený pre vloženie priamo do DockPanelu
    /// (ako docknutý element na tail-edge). Celý DockPanel sa rotuje pre vertikálne bloky,
    /// takže nepotrebujeme LayoutTransformControl wrapper.
    /// </summary>
    private static Control BuildNameLabelInline(string name, TrainOrientation orientation)
    {
        // Pre vertikálne bloky musíme text tiež rotovať +90° CW aby bol čitateľný
        // po rotácii celého DockPanelu. Ale používame RenderTransform (nie Layout),
        // lebo DockPanel už má svoj LayoutTransform a Avalonia nepodporuje vnorené
        // LayoutTransform reťazce spoľahlivo.
        bool isVertical = orientation.IsVertical();
        
        return new TextBlock
        {
            Text = name,
            FontSize = 11,
            Foreground = Brushes.Black,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(2),
            RenderTransform = isVertical ? new RotateTransform(90) : null,
            RenderTransformOrigin = isVertical ? new RelativePoint(0.5, 0.5, RelativeUnit.Relative) : default,
        };
    }

    /// <summary>
    /// Vytvorí popisku názvu vlaku, ktorá je VŽDY umiestnená "ZA LOKOMOTÍVOU"
    /// (na opačnej strane bloku než smeruje hlava lokomotívy) — bez ohľadu
    /// na orientáciu bloku (horizontálny/vertikálny) a smer jazdy (Forward/Backward).
    ///
    /// Princíp:
    /// - Lokomotíva je vždy "docknutá" na head-edge bloku (pravá/ľavá/spodná/horná).
    /// - Názov je za vlakom (tesne za posledným vagónom / lokom ak nie sú vagóny).
    ///
    /// Pre vertikálne bloky používame LayoutTransformControl (nie RenderTransform),
    /// aby sa rotácia premietla do layoutu a alignmenty fungovali predvídateľne
    /// — rovnaký vzor ako pri rotácii celého stacku vlaku v <see cref="CreateTrainVisual"/>.
    /// </summary>
    private static Control BuildNameLabel(string name, TrainOrientation orientation, double blockW, double blockH)
    {
        bool isForward = orientation.IsForward();

        if (!orientation.IsVertical())
        {
            // HORIZONTÁLNY BLOK: text vodorovný, za vlakom (tail koniec)
            // HForward  → text vľavo za vagónmi (loko vpravo)
            // HBackward → text vpravo za vagónmi (loko vľavo)
            return new TextBlock
            {
                Text = name,
                FontSize = 11,
                Foreground = Brushes.Black,
                IsHitTestVisible = false,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                Opacity = 0.75,
                MaxWidth = Math.Max(0, blockW - 30),
                HorizontalAlignment = isForward ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = isForward
                    ? new Thickness(4, 0, 0, 0)   // menší odstup zľava  
                    : new Thickness(0, 0, 4, 0),  // menší odstup zprava
            };
        }

        // VERTIKÁLNY BLOK: text rotovaný +90° CW (čítanie zdola hore), za vlakom
        // VDown (head down) → text HORE za vagónmi
        // VUp   (head up)   → text DOLE za vagónmi
        //
        // POZOR: Používame +90° (nie -90°) aby text bol čitateľný správnym smerom.
        // Pri -90° by sa text otočil "hore nohami" kvôli interakcii s FlowDirection.
        var text = new TextBlock
        {
            Text = name,
            FontSize = 11,
            Foreground = Brushes.Black,
            IsHitTestVisible = false,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            Opacity = 0.75,
            MaxWidth = Math.Max(0, blockH - 30),
        };

        // LayoutTransformControl: rotácia +90° CW ovplyvní LAYOUT (nie len render).
        return new LayoutTransformControl
        {
            Child = text,
            LayoutTransform = new RotateTransform(90),  // +90° CW (nie -90°)
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = isForward ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            Margin = new Thickness(0, 4, 0, 4),
        };
    }

    /// <summary>
    /// NaÄŤĂ­ta ikonu vozidla. NajskĂ´r <see cref="IconRegistry"/> (custom cesty z importov),
    /// potom embedded avares:// URI v Assets/LocoIcons a Assets/VagonIcons.
    /// </summary>
    private static Image? LoadIconImage(string iconName)
    {
        var bitmap = VehicleIconLoader.TryLoadBitmap(iconName);
        return bitmap == null ? null : new Image { Source = bitmap };
    }
}
