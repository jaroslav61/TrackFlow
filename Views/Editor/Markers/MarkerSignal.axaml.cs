using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;
using TrackFlow.Models.Layout;
using TrackFlow.Services;

namespace TrackFlow.Views.Editor.Markers;

/// <summary>Nastavi vizualny aspekt (farbu) signalu.</summary>
public interface IMarkerSignalAspect
{
    void SetAspect(SignalAspect aspect);
}

/// <summary>Nastavi pocet znakov signalu (2-5) - pocet svetiel.</summary>
public interface IMarkerSignalProfile
{
    void SetProfile(int signCount);
}

/// <summary>Nastavi ID profilu signalu pre sprvne zobrazenie farieb (napr. "2-aspect-main" vs "2-aspect").</summary>
public interface IMarkerSignalProfileId
{
    void SetProfileId(string? profileId);
}

/// <summary>
/// Preview md: v editore zobraz vetky svetl v ich prirodzench farbch
/// (ako nhad v ribbon pse), namiesto svietenia len aktvneho aspektu.
/// </summary>
public interface IMarkerSignalPreview
{
    void SetPreviewAllLit(bool allLit);
}

/// <summary>
/// Vynuti kompaktny footprint pre 2-znakovy signal aj mimo preview modu.
/// </summary>
public interface IMarkerSignalCompact
{
    void SetCompactTwoAspect(bool compact);
}

public partial class MarkerSignal : UserControl, IMarkerAngle, IMarkerSignalAspect, IMarkerSignalProfile, IMarkerSignalProfileId, IMarkerSignalPreview, IMarkerSignalCompact
{
    private static readonly HashSet<MarkerSignal> ActiveBlinkingSignals = new();
    private static readonly HashSet<string> UnsupportedAspectFallbackWarnings = new(StringComparer.Ordinal);
    private static readonly DispatcherTimer BlinkTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };
    private static bool _blinkPhaseVisible = true;

    // === Farby ===
    private static readonly IBrush BodyBrush   = Brush.Parse("#111111");
    private static readonly IBrush BodyStroke  = Brush.Parse("#101010");
    private static readonly IBrush OffBrush    = Brush.Parse("#3A3A3A");
    private static readonly IBrush RedBrush    = Brush.Parse("#FF0000");
    private static readonly IBrush YellowBrush = Brush.Parse("#FFEE00");
    private static readonly IBrush GreenBrush  = Brush.Parse("#00FF44");
    private static readonly IBrush WhiteBrush  = Brush.Parse("#FFFFFF");
    private static readonly IBrush BlueBrush   = Brush.Parse("#00CCFF");

    // === Geometria ===
    private const double Cell       = 24.0;  
    private const double LongAxis   = Cell * 2;                       // 48 - štandardne marker zaberá 2 bunky
    private const double ShortAxis  = Cell;                           // 24 - krátka strana
    private const double BodyShort  = 12;                             // hrúbka tela (šírka pri 0°)
    private const double BodyLength = 44;                             // výška tela (pri 0°)
    private const double ConnectorWidth = 4;                          // šírka vertikálneho spojovacieho stĺpika
    private const double ConnectorHeight = 2;                         // výška vertikálneho spojovacieho stĺpika
    private const double StandWidth = 12;                             // šírka horizontálneho stojanu
    private const double StandHeight = 2;                             // výška horizontálneho stojanu
    private const double LightSize  = 7;
    private const double EndMargin  = 2;

    private Canvas? _canvas;
    private int _signCount = 5;
    private string? _profileId;
    private int _angle;            // 0 / 90 / 180 / 270
    private SignalAspect _aspect = SignalAspect.Stop;
    private bool _previewAllLit;
    private bool _compactTwoAspect;
    private readonly List<Ellipse> _lights = new();
    private int? _blinkingLightIndex;
    private IBrush? _blinkingLightBrush;
    private bool _isAttached;

    // V editore (preview mode) chceme 2-znakové profily kresliť kompaktne do 1 bunky.
    private bool IsCompactTwoAspect => (_previewAllLit || _compactTwoAspect) && _signCount == 2;

    public MarkerSignal()
    {
        BlinkTimer.Tick -= OnBlinkTimerTick;
        BlinkTimer.Tick += OnBlinkTimerTick;

        AvaloniaXamlLoader.Load(this);
        _canvas = this.FindControl<Canvas>("MainCanvas");
        AttachedToVisualTree += (_, _) =>
        {
            _isAttached = true;
            UpdateBlinkRegistration();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _isAttached = false;
            UnregisterBlinking(this);
        };
        ApplySize();
        Rebuild();
    }

    /// <summary>
    /// Nastaví uhol (snap na 90°). Geometria sa prebuduje - žiadny RenderTransform,
    /// vďaka tomu vizuál vždy presne vyplní bbox markera a selection rámček s ním sedí
    /// (rovnaký princíp ako pri markeri Block).
    /// </summary>
    public void SetAngle(int angle)
    {
        int normalized = ((angle % 360) + 360) % 360;
        int snapped = ((normalized + 45) / 90) * 90 % 360;

        if (snapped == _angle && _canvas != null && _lights.Count == _signCount)
            return;

        _angle = snapped;
        RenderTransform = null; // bezpenostn reset, rotujeme len cez geometriu
        ApplySize();
        Rebuild();
    }

    public void SetProfile(int signCount)
    {
        int clamped = Math.Clamp(signCount, 2, 5);
        if (clamped == _signCount && _lights.Count == clamped)
        {
            ApplyAspect();
            return;
        }
        _signCount = clamped;
        ApplySize();
        Rebuild();
    }

    public void SetProfileId(string? profileId)
    {
        if (_profileId == profileId) return;
        _profileId = profileId;
        ApplyAspect();
    }

    public void SetAspect(SignalAspect aspect)
    {
        _aspect = aspect;
        ApplyAspect();
    }

    /// <summary>
    /// Preview mód pre editor: ak true, všetky svetlá zobrazia svoje prirodzené farby
    /// (rovnako ako v ribbon náhľade). Ak false, svieti len aktívny aspekt.
    /// </summary>
    public void SetPreviewAllLit(bool allLit)
    {
        if (_previewAllLit == allLit) return;
        _previewAllLit = allLit;
        ApplySize();
        Rebuild();
        ApplyAspect();
    }

    public void SetCompactTwoAspect(bool compact)
    {
        if (_compactTwoAspect == compact) return;
        _compactTwoAspect = compact;
        ApplySize();
        Rebuild();
        ApplyAspect();
    }

    // === Layout ===

    private bool IsHorizontal => _angle == 90 || _angle == 270;

    private void ApplySize()
    {
        double longAxis = IsCompactTwoAspect ? Cell : LongAxis;
        Width  = IsHorizontal ? longAxis  : ShortAxis;
        Height = IsHorizontal ? ShortAxis : longAxis;

        if (_canvas != null)
        {
            _canvas.Width  = Width;
            _canvas.Height = Height;
        }
    }

    private void Rebuild()
    {
        if (_canvas == null) return;

        bool compactTwoAspect = IsCompactTwoAspect;
        double longAxis = compactTwoAspect ? Cell : LongAxis;
        double shortAxis = ShortAxis;
        double bodyShort = compactTwoAspect ? 9 : BodyShort;
        double bodyLength = compactTwoAspect ? 20 : BodyLength;
        double connectorWidth = compactTwoAspect ? 2 : ConnectorWidth;
        double connectorHeight = compactTwoAspect ? 2 : ConnectorHeight;
        double standWidth = compactTwoAspect ? 9 : StandWidth;
        double standHeight = compactTwoAspect ? 2 : StandHeight;
        double bodyOffset = (shortAxis - bodyShort) / 2.0;
        double lightSize = compactTwoAspect ? 6 : LightSize;
        double endMargin = compactTwoAspect ? 2 : EndMargin;
        double bodyCornerRadius = compactTwoAspect ? 2 : 4;
        double lightStrokeThickness = compactTwoAspect ? 0.5 : 0.75;

        _canvas.Children.Clear();
        _lights.Clear();

        // Telo navestidla - obsahuje svetlá
        var body = new Rectangle
        {
            Fill             = BodyBrush,
            Stroke           = BodyStroke,
            StrokeThickness  = 0.5,
            RadiusX          = bodyCornerRadius,
            RadiusY          = bodyCornerRadius,
            IsHitTestVisible = false,
        };

        switch (_angle)
        {
            case 0:   // Vertikálne, stojan dole - telo hore
                body.Width  = bodyShort;
                body.Height = bodyLength;
                Canvas.SetLeft(body, bodyOffset);
                Canvas.SetTop(body, 0);
                break;

            case 90:  // Horizontálne, stojan vľavo - telo vpravo
                body.Width  = bodyLength;
                body.Height = bodyShort;
                Canvas.SetLeft(body, longAxis - bodyLength);
                Canvas.SetTop(body, bodyOffset);
                break;

            case 180: // Vertikálne,stojan hore - telo dole
                body.Width  = bodyShort;
                body.Height = bodyLength;
                Canvas.SetLeft(body, bodyOffset);
                Canvas.SetTop(body, longAxis - bodyLength);
                break;

            case 270: // Horizontálne, stojan vpravo - telo vľavo
                body.Width  = bodyLength;
                body.Height = bodyShort;
                Canvas.SetLeft(body, 0);
                Canvas.SetTop(body, bodyOffset);
                break;
        }
        _canvas.Children.Add(body);
        
        // Spojovací stĺpik a stojan - pozícia závisí od uhla rotácie
        var connector = new Rectangle
        {
            Fill             = BodyBrush,
            Stroke           = BodyStroke,
            StrokeThickness  = 0.5,
            RadiusX          = 1,
            RadiusY          = 1,
            IsHitTestVisible = false,
        };

        var stand = new Rectangle
        {
            Fill             = BodyBrush,
            Stroke           = BodyStroke,
            StrokeThickness  = 0.5,
            RadiusX          = 1,
            RadiusY          = 1,
            IsHitTestVisible = false,
        };

        switch (_angle)
        {
            case 0:   // Stojan DOLE (pod telom)
                connector.Width  = connectorWidth;
                connector.Height = connectorHeight;
                Canvas.SetLeft(connector, (shortAxis - connectorWidth) / 2.0);
                Canvas.SetTop(connector, bodyLength);

                stand.Width  = standWidth;
                stand.Height = standHeight;
                Canvas.SetLeft(stand, (shortAxis - standWidth) / 2.0);
                Canvas.SetTop(stand, bodyLength + connectorHeight);
                break;

            case 90:  // Stojan VĽAVO (vľavo od tela)
                connector.Width  = connectorHeight;
                connector.Height = connectorWidth;
                Canvas.SetLeft(connector, longAxis - bodyLength - connectorHeight);
                Canvas.SetTop(connector, (shortAxis - connectorWidth) / 2.0);

                stand.Width  = standHeight;
                stand.Height = standWidth;
                Canvas.SetLeft(stand, 0);
                Canvas.SetTop(stand, (shortAxis - standWidth) / 2.0);
                break;

            case 180: // Stojan HORE (nad telom)
                connector.Width  = connectorWidth;
                connector.Height = connectorHeight;
                Canvas.SetLeft(connector, (shortAxis - connectorWidth) / 2.0);
                Canvas.SetTop(connector, longAxis - bodyLength - connectorHeight);

                stand.Width  = standWidth;
                stand.Height = standHeight;
                Canvas.SetLeft(stand, (shortAxis - standWidth) / 2.0);
                Canvas.SetTop(stand, 0);
                break;

            case 270: // Stojan VPRAVO (vpravo od tela)
                connector.Width  = connectorHeight;
                connector.Height = connectorWidth;
                Canvas.SetLeft(connector, bodyLength);
                Canvas.SetTop(connector, (shortAxis - connectorWidth) / 2.0);

                stand.Width  = standHeight;
                stand.Height = standWidth;
                Canvas.SetLeft(stand, bodyLength + connectorHeight);
                Canvas.SetTop(stand, (shortAxis - standWidth) / 2.0);
                break;
        }

        _canvas.Children.Add(connector);
        _canvas.Children.Add(stand);

        // Svetlá rozmiestnené po dĺžke tela.
        double available = bodyLength - 2 * endMargin;
        double slot      = _signCount <= 0 ? 0 : available / _signCount;

        for (int logicalIndex = 0; logicalIndex < _signCount; logicalIndex++)
        {
            // logicalIndex = 0 je vždy "horný" znak v profile (SK norma).
            // Pre rotáciu prepočítame vizuálnu pozíciu pozdĺž dlhej osi:
            //   0° - zhora nadol             (logical 0 hore)
            //   90° - sprava doľava           (logical 0 vpravo)
            //   180° - zdola nahor             (logical 0 dole)
            //   270° - zľava doprava           (logical 0 vavo)
            int positionIndex = _angle switch
            {
                90  => _signCount - 1 - logicalIndex,
                180 => _signCount - 1 - logicalIndex,
                _   => logicalIndex, // 0 a 270
            };

            double along  = endMargin + positionIndex * slot + (slot - lightSize) / 2.0;
            double across = (shortAxis - lightSize) / 2.0;

            var ellipse = new Ellipse
            {
                Width            = lightSize,
                Height           = lightSize,
                Fill             = OffBrush,
                Stroke           = BodyStroke,
                StrokeThickness  = lightStrokeThickness,
                IsHitTestVisible = false,
            };

            // Pozícia svetla závisí od uhla a offsetu tela
            switch (_angle)
            {
                case 0:   // Vertikálne, telo hore (top=0)
                    Canvas.SetLeft(ellipse, across);
                    Canvas.SetTop(ellipse, along);
                    break;

                case 90:  // Horizontálne, telo vpravo (left=4)
                    Canvas.SetLeft(ellipse, along + (longAxis - bodyLength));
                    Canvas.SetTop(ellipse, across);
                    break;

                case 180: // Vertikálne, telo dole (top=4)
                    Canvas.SetLeft(ellipse, across);
                    Canvas.SetTop(ellipse, along + (longAxis - bodyLength));
                    break;

                case 270: // Horizontálne, telo vľavo (left=0)
                    Canvas.SetLeft(ellipse, along);
                    Canvas.SetTop(ellipse, across);
                    break;
            }

            _canvas.Children.Add(ellipse);
            _lights.Add(ellipse);
        }

        ApplyAspect();
    }

    // === Aspekty ===

    /// <summary>
    /// Vráti prirodzenú farbu svetla na danej logickej pozícii podľa SK normy.
    /// Pre count=2 závisí aj od profilu:
    ///   "2-aspect" - [Yellow, Green] (Predzvesť)
    ///   "2-aspect-main" - [Red, Green] (Hlavné)
    ///   "2-aspect-shunt" - [Blue, White] (Zriaďovacie)
    /// </summary>
    private IBrush NaturalColorAt(int logicalIndex) 
    {
        if (_signCount == 2)
        {
            return _profileId switch
            {
                "2-aspect-main" => logicalIndex == 0 ? RedBrush : GreenBrush,
                "2-aspect-shunt" => logicalIndex == 0 ? BlueBrush : WhiteBrush,
                "2-aspect-route" => logicalIndex == 0 ? RedBrush : WhiteBrush,
                _ => logicalIndex == 0 ? YellowBrush : GreenBrush,  // default: predzvesť
            };
        }

        if (_signCount == 3 && string.Equals(_profileId, "3-aspect-entry", StringComparison.Ordinal))
        {
            // Vchodové (Z/R/B): logical 0=horná zelená, 1=červená, 2=biela
            return logicalIndex switch
            {
                0 => GreenBrush,
                1 => RedBrush,
                2 => WhiteBrush,
                _ => OffBrush
            };
        }

        return (_signCount, logicalIndex) switch
        {
            (3, 0) => YellowBrush,
            (3, 1) => GreenBrush,
            (3, 2) => RedBrush,

            (4, 0) => _profileId == "4-aspect-departure" ? GreenBrush : YellowBrush,
            (4, 1) => RedBrush,
            (4, 2) => WhiteBrush,
            (4, 3) => YellowBrush,

            (5, 0) => YellowBrush,
            (5, 1) => GreenBrush,
            (5, 2) => RedBrush,
            (5, 3) => WhiteBrush,
            (5, 4) => YellowBrush,

            _ => OffBrush
        };
    }

    private void ApplyAspect()
    {
        if (_lights.Count == 0)
            return;

        ResetBlinkingState();

        // Preview mód v editore: nakresli všetky svetlá v ich prirodzených farbách
        // (rovnako ako náhľad v ribbon páse). Operation režim používa SetAspect().
        if (_previewAllLit)
        {
            for (int i = 0; i < _lights.Count; i++)
            {
                _lights[i].Fill = NaturalColorAt(i);
                _lights[i].Stroke = BodyStroke;
                _lights[i].StrokeThickness = IsCompactTwoAspect ? 0.5 : 0.75;
                _lights[i].Opacity = 1.0;
            }
            return;
        }

        // Operation režim:zhasni všetky a rozsvieť len aktívny aspekt na svojej logickej pozícii.
        foreach (var light in _lights)
        {
            light.Fill = OffBrush;
            light.Stroke = BodyStroke;
            light.StrokeThickness = IsCompactTwoAspect ? 0.5 : 0.75;
            light.Opacity = 1.0;
        }

        // Runtime model can still carry legacy aliases (Green/Yellow/White).
        // Normalize to canonical aspects so rendering is deterministic.
        var effectiveAspect = _aspect switch
        {
            SignalAspect.Green => SignalAspect.Proceed,
            SignalAspect.Yellow => SignalAspect.Caution,
            SignalAspect.White => SignalAspect.ShuntingPermitted,
            _ => _aspect
        };

        var normalizedAspect = SignalSystemRegistry.ResolveFailSafeAspect(
            SignalSystemDefinition.DefaultSystemId,
            _profileId,
            effectiveAspect);
        if (normalizedAspect != effectiveAspect)
        {
            WarnUnsupportedAspectFallback(effectiveAspect, normalizedAspect);
            effectiveAspect = normalizedAspect;
        }

        switch (_signCount)
        {
            case 2:
                // Farby závisia od profilu
                if (_profileId == "2-aspect-main")
                {
                    if (effectiveAspect == SignalAspect.Proceed) SetLight(1, GreenBrush);
                    else SetLight(0, RedBrush);
                }
                else if (_profileId == "2-aspect-shunt")
                {
                    if (effectiveAspect == SignalAspect.ShuntingPermitted) SetLight(1, WhiteBrush);
                    else SetLight(0, BlueBrush);
                }
                else if (_profileId == "2-aspect-route")
                {
                    // Cestové: biela = "cesta dovolená" (mapujeme na ShuntingPermitted; Proceed fallback tiež na bielu)
                    if (effectiveAspect is SignalAspect.ShuntingPermitted or SignalAspect.Proceed) SetLight(1, WhiteBrush);
                    else SetLight(0, RedBrush);
                }
                else // default: predzvesť (2-aspect)
                {
                    if (effectiveAspect == SignalAspect.Proceed) SetLight(1, GreenBrush);
                    else if (effectiveAspect == SignalAspect.SlowProceed)
                    {
                        // SR "40 a Voľno": tento 2-znakový profil nemá dolnú žltú,
                        // preto nikdy nerozsvecuj hornú žltú pre SlowProceed.
                        SetLight(1, GreenBrush);
                    }
                    else if (effectiveAspect == SignalAspect.SlowExpect40) SetBlinkingLight(0, YellowBrush);
                    else SetLight(0, YellowBrush);
                }
                break;

            case 3:
                if (_profileId == "3-aspect-entry")
                {
                    // Vchodové (Z/R/B)
                    if (effectiveAspect == SignalAspect.Proceed) SetLight(0, GreenBrush);
                    else if (effectiveAspect == SignalAspect.ShuntingPermitted) SetLight(2, WhiteBrush);
                    else if (effectiveAspect == SignalAspect.SlowProceed)
                    {
                        // 3-aspect entry profil (Z/R/B) fyzicky nemá dolnú žltú,
                        // preto SR "40 a Voľno" fallbackuje na zelenú (NIKDY hornú žltú).
                        WarnUnsupportedAspectFallback(effectiveAspect, SignalAspect.Proceed);
                        SetLight(0, GreenBrush);
                    }
                    else if (effectiveAspect == SignalAspect.Caution
                          || effectiveAspect == SignalAspect.SlowCaution
                          || effectiveAspect == SignalAspect.SlowExpect40)
                    {
                        // 3-aspect entry nemá žltú lampu - fallback na zelenú (permissive),
                        // aby sa vlak nezasekol; nikdy nepredstierame žltú, ktorá tu nie je.
                        WarnUnsupportedAspectFallback(effectiveAspect, SignalAspect.Proceed);
                        SetLight(0, GreenBrush);
                    }
                    else SetLight(1, RedBrush); // default bezpečne na červenú
                }
                else
                {
                    // Oddielové (Ž/Z/R) - [0]=horná žltá, [1]=zelená, [2]=červená.
                    // Profil fyzicky NEMÁ dolnú žltú, preto SR aspekty, ktoré ju vyžadujú,
                    // musia fallbackovať na zelenú (nie hornú žltú = "výstraha pred Stoj").
                    if (effectiveAspect == SignalAspect.Stop) SetLight(2, RedBrush);
                    else if (effectiveAspect == SignalAspect.Proceed) SetLight(1, GreenBrush);
                    else if (effectiveAspect == SignalAspect.SlowProceed)
                    {
                        // SR "40 a Voľno": chce zelenú + dolnú žltú, ale dolná žltá tu nie je.
                        // Bezpečný fallback = zelená (Voľno). NIKDY nerozsvecuj hornú žltú
                        // pre SlowProceed - to by užívateľ vnímal ako "Výstrahu", čo je iný aspekt.
                        WarnUnsupportedAspectFallback(effectiveAspect, SignalAspect.Proceed);
                        SetLight(1, GreenBrush);
                    }
                    else if (effectiveAspect == SignalAspect.SlowCaution)
                    {
                        // SR "40 a Výstraha": potrebuje obe žlté, dolnú nemáme - fallback na hornú žltú (Výstraha).
                        WarnUnsupportedAspectFallback(effectiveAspect, SignalAspect.Caution);
                        SetLight(0, YellowBrush);
                    }
                    else if (effectiveAspect == SignalAspect.SlowExpect40) SetBlinkingLight(0, YellowBrush);
                    else SetLight(0, YellowBrush); // Caution / fallback
                }
                break;

            case 4:
                if (effectiveAspect == SignalAspect.Stop)
                    SetLight(1, RedBrush);
                else if (effectiveAspect == SignalAspect.ShuntingPermitted)
                    SetLight(2, WhiteBrush);
                else if (_profileId == "4-aspect-departure")
                {
                    // Legacy 4-znakové odchodové: [0]=zelená, [1]=červená, [2]=biela, [3]=dolná žltá.
                    // Profil fyzicky nemá hornú žltú, preto nesmie predstierať Caution/SlowCaution/SlowExpect40.
                    if (effectiveAspect == SignalAspect.SlowExpect40) 
                    {
                        WarnUnsupportedAspectFallback(effectiveAspect, SignalAspect.SlowProceed);
                        SetLight(0, GreenBrush);
                        SetLight(3, YellowBrush);
                    }
                    else if (effectiveAspect == SignalAspect.SlowProceed)
                    {
                        // SR "40 a Voľno": zelená + dolná žltá
                        SetLight(0, GreenBrush);
                        SetLight(3, YellowBrush);
                    }
                    else if (effectiveAspect == SignalAspect.Caution)
                    {
                        WarnUnsupportedAspectFallback(effectiveAspect, SignalAspect.Stop);
                        SetLight(1, RedBrush);
                    }
                    else if (effectiveAspect == SignalAspect.SlowCaution)
                    {
                        WarnUnsupportedAspectFallback(effectiveAspect, SignalAspect.SlowProceed);
                        SetLight(0, GreenBrush);
                        SetLight(3, YellowBrush);
                    }
                    else if (effectiveAspect == SignalAspect.Proceed)
                        SetLight(0, GreenBrush);
                    else
                        SetLight(0, GreenBrush);  // Default: zelená
                }
                else if (effectiveAspect == SignalAspect.SlowProceed)
                {
                    // SR "40 a Voľno": zelená + dolná žltá, bez hornej žltej.
                    SetLight(1, GreenBrush);
                    SetLight(3, YellowBrush);
                }
                else if (effectiveAspect == SignalAspect.SlowCaution)
                {
                    // SK norma "40 a Výstraha": horná žltá + dolná žltá
                    SetLight(0, YellowBrush);
                    SetLight(3, YellowBrush);
                }
                else if (effectiveAspect == SignalAspect.SlowExpect40)
                    SetBlinkingLight(0, YellowBrush);
                else if (effectiveAspect == SignalAspect.Caution)
                    SetLight(0, YellowBrush);
                else if (effectiveAspect == SignalAspect.Proceed)
                {
                    // v1.5: Vchodové 4-znakové nemá zelenú - fallback na hornú žltú (Caution)
                    SetLight(0, YellowBrush);
                }
                else
                    SetLight(0, YellowBrush);  // Default: horná žltá
                break;

            case 5:
                // [0]=Yellow(Caution2), [1]=Green, [2]=Red, [3]=White, [4]=Yellow(Caution)
                if (effectiveAspect == SignalAspect.Stop) SetLight(2, RedBrush);
                else if (effectiveAspect == SignalAspect.Proceed) SetLight(1, GreenBrush);
                else if (effectiveAspect == SignalAspect.ShuntingPermitted) SetLight(3, WhiteBrush);
                else if (effectiveAspect == SignalAspect.SlowProceed)
                {
                    // SR "40 a Voľno": zelená + dolná žltá
                    SetLight(1, GreenBrush);
                    SetLight(4, YellowBrush);
                }
                else if (effectiveAspect == SignalAspect.SlowCaution)
                {
                    SetLight(0, YellowBrush);
                    SetLight(4, YellowBrush);
                }
                else if (effectiveAspect == SignalAspect.SlowExpect40)
                {
                    SetBlinkingLight(0, YellowBrush);
                    SetLight(4, YellowBrush);
                }
                else SetLight(0, YellowBrush);
                break;
        }
    }

    private void SetLight(int logicalIndex, IBrush brush)
    {
        if (logicalIndex >= 0 && logicalIndex < _lights.Count)
            _lights[logicalIndex].Fill = brush;
    }

    private void WarnUnsupportedAspectFallback(SignalAspect requestedAspect, SignalAspect fallbackAspect)
    {
        string profileId = _profileId ?? "<null>";
        string key = $"{profileId}|{requestedAspect}|{fallbackAspect}";
        if (!UnsupportedAspectFallbackWarnings.Add(key))
            return;

        Log.Warning(
            "Signal profile {ProfileId} cannot physically render aspect {RequestedAspect}; rendering fail-safe fallback {FallbackAspect}.",
            profileId,
            requestedAspect,
            fallbackAspect);
    }

    private void SetBlinkingLight(int logicalIndex, IBrush brush)
    {
        if (logicalIndex < 0 || logicalIndex >= _lights.Count)
            return;

        _blinkingLightIndex = logicalIndex;
        _blinkingLightBrush = brush;
        ApplyBlinkVisual(_blinkPhaseVisible);
        UpdateBlinkRegistration();
    }

    private void ResetBlinkingState()
    {
        UnregisterBlinking(this);
        _blinkingLightIndex = null;
        _blinkingLightBrush = null;
    }

    private void ApplyBlinkVisual(bool isVisible)
    {
        if (!_blinkingLightIndex.HasValue)
            return;

        int logicalIndex = _blinkingLightIndex.Value;
        if (logicalIndex < 0 || logicalIndex >= _lights.Count)
            return;

        var light = _lights[logicalIndex];
        if (isVisible)
        {
            light.Fill = _blinkingLightBrush ?? YellowBrush;
            light.Stroke = WhiteBrush;
            light.StrokeThickness = IsCompactTwoAspect ? 1.0 : 1.4;
            light.Opacity = 1.0;
        }
        else
        {
            light.Fill = OffBrush;
            light.Stroke = BodyStroke;
            light.StrokeThickness = IsCompactTwoAspect ? 0.5 : 0.75;
            light.Opacity = 1.0;
        }
    }

    private void UpdateBlinkRegistration()
    {
        if (_blinkingLightIndex.HasValue && _isAttached && !_previewAllLit)
            RegisterBlinking(this);
        else
            UnregisterBlinking(this);
    }

    private static void RegisterBlinking(MarkerSignal signal)
    {
        ActiveBlinkingSignals.Add(signal);
        if (!BlinkTimer.IsEnabled)
        {
            _blinkPhaseVisible = true;
            BlinkTimer.Start();
        }
    }

    private static void UnregisterBlinking(MarkerSignal signal)
    {
        if (!ActiveBlinkingSignals.Remove(signal))
            return;

        if (ActiveBlinkingSignals.Count == 0)
            BlinkTimer.Stop();
    }

    private static void OnBlinkTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;

        _blinkPhaseVisible = !_blinkPhaseVisible;
        foreach (var signal in ActiveBlinkingSignals.ToList())
            signal.ApplyBlinkVisual(_blinkPhaseVisible);
    }
}
