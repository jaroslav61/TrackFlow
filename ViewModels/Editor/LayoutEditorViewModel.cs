using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using TrackFlow.Extensions;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using Serilog;

namespace TrackFlow.ViewModels.Editor;

public partial class LayoutEditorViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private readonly CollisionDetectionService _collisionService = new();

    public TrackLayout? CurrentLayout => _settings.CurrentProject?.Layout;

    // =====================================================================================
    // Undo / Redo (snapshot-based)
    // =====================================================================================

    private sealed record LayoutSnapshot(
        double CanvasWidth,
        double CanvasHeight,
        double ZoomFactor,
        double PanX,
        double PanY,
        List<LayoutElement> Elements
    );

    private readonly Stack<LayoutSnapshot> _undoStack = new();
    private readonly Stack<LayoutSnapshot> _redoStack = new();
    private bool _undoRedoGuard;
    private bool _suppressUndoHistory;

    private string? _lastUndoReason;
    private DateTime _lastUndoSnapshotUtc;

    /// <summary>Vyvolané pri zmene dostupnosti Undo/Redo (napr. po Capture/Undo/Redo/Reset).</summary>
    public event Action? UndoRedoStateChanged;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void ResetUndoRedoHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        UndoRedoStateChanged?.Invoke();
    }

    private LayoutSnapshot MakeSnapshot()
    {
        // ClonePreserveId() je dôležité: Undo/Redo musí zachovať identitu prvkov.
        var cloned = Elements.Select(e => e.ClonePreserveId()).ToList();
        return new LayoutSnapshot(CanvasWidth, CanvasHeight, ZoomFactor, 0, 0, cloned);
    }

    private void RestoreSnapshot(LayoutSnapshot snapshot)
    {
        _undoRedoGuard = true;
        try
        {
            Elements.Clear();
            foreach (var el in snapshot.Elements)
                Elements.Add(el.ClonePreserveId());

            CanvasWidth = snapshot.CanvasWidth;
            CanvasHeight = snapshot.CanvasHeight;
            ZoomFactor = snapshot.ZoomFactor;

            _selection.ClearSelection();
            SelectedElement = null;

            NotifyInspector();
            SyncToProject();
        }
        finally
        {
            _undoRedoGuard = false;
        }
    }

    /// <summary>
    /// Uloží checkpoint pred zmenou (Undo sa vráti sem). Volaj pred mutáciou Elements / vlastností prvkov.
    /// </summary>
    public void CaptureUndoCheckpoint(string reason = "edit", bool force = false)
    {
        if (_undoRedoGuard || _suppressUndoHistory)
            return;

        // Coalescing: typicky pri písaní do inšpektora nechceme snapshot na každý znak.
        var now = DateTime.UtcNow;
        if (!force
            && string.Equals(_lastUndoReason, reason, StringComparison.Ordinal)
            && (now - _lastUndoSnapshotUtc).TotalMilliseconds < 750)
        {
            return;
        }

        _lastUndoReason = reason;
        _lastUndoSnapshotUtc = now;

        _undoStack.Push(MakeSnapshot());
        _redoStack.Clear();
        UndoRedoStateChanged?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo)
            return;

        var current = MakeSnapshot();
        var target = _undoStack.Pop();
        _redoStack.Push(current);
        RestoreSnapshot(target);
        UndoRedoStateChanged?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo)
            return;

        var current = MakeSnapshot();
        var target = _redoStack.Pop();
        _undoStack.Push(current);
        RestoreSnapshot(target);
        UndoRedoStateChanged?.Invoke();
    }
    
    /// <summary>Prístup k SettingsManager pre dialógy.</summary>
    public SettingsManager? SettingsManager => _settings;

    /// <summary>Runtime Locomotives z SmartStripsViewModel pre rendering ghost v blokoch.</summary>
    private ObservableCollection<Locomotive>? _smartStripsLocomotives;
    public ObservableCollection<Locomotive>? SmartStripsLocomotives
    {
        get => _smartStripsLocomotives;
        set
        {
            if (_smartStripsLocomotives != null)
            {
                _smartStripsLocomotives.CollectionChanged -= OnSmartStripsLocosChanged;
                foreach (var loco in _smartStripsLocomotives)
                {
                    loco.AttachedWagons.CollectionChanged -= OnLocoWagonsChanged;
                    loco.PropertyChanged -= OnLocoPropertyChanged;
                }
            }
            _smartStripsLocomotives = value;
            if (_smartStripsLocomotives != null)
            {
                _smartStripsLocomotives.CollectionChanged += OnSmartStripsLocosChanged;
                foreach (var loco in _smartStripsLocomotives)
                {
                    loco.AttachedWagons.CollectionChanged += OnLocoWagonsChanged;
                    loco.PropertyChanged += OnLocoPropertyChanged;
                }
            }
        }
    }

    private void OnSmartStripsLocosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (Locomotive loco in e.OldItems)
            {
                loco.AttachedWagons.CollectionChanged -= OnLocoWagonsChanged;
                loco.PropertyChanged -= OnLocoPropertyChanged;
            }
        if (e.NewItems != null)
            foreach (Locomotive loco in e.NewItems)
            {
                loco.AttachedWagons.CollectionChanged += OnLocoWagonsChanged;
                loco.PropertyChanged += OnLocoPropertyChanged;
            }
    }

    /// <summary>Vyvolan pri zmene vlastnost lokomotvy (nzov, ikona, stav)  prekresl jej priraden blok.</summary>
    private void OnLocoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Locomotive loco)
            return;

        foreach (var el in Elements)
        {
            if (el is BlockElement block && string.Equals(block.AssignedLocoId, loco.Code, StringComparison.OrdinalIgnoreCase))
                RequestBlockRepaint?.Invoke(block);
        }
    }

    /// <summary>Vyvolan pri zmene AttachedWagons ubovonej loky  prekresl blok v ktorom sa loka nachdza.</summary>
    private void OnLocoWagonsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var el in Elements)
        {
            if (el is BlockElement block && !string.IsNullOrEmpty(block.AssignedLocoId))
                RequestBlockRepaint?.Invoke(block);
        }
    }

    //  Nstroj 
    [ObservableProperty] private LayoutTool selectedTool = LayoutTool.Select;
    [ObservableProperty] private LayoutElementType? pendingElementType;

    /// <summary>
    /// String identifiktor aktvneho markera (CommandParameter)  pre zvraznenie v XAML.
    /// Napr. "TrackSegment", "Curve_45", "Curve_90", "Bumper", "Turnout_L" at.
    /// </summary>
    [ObservableProperty] private string? selectedMarkerKey;

    //  Pltno 
    public const double CellSize = 24.0;   // vekos bunky mrieky v px
    public const double RulerSize = 20.0;  // rka/vka pravtka v px

    [ObservableProperty] private double canvasWidth  = 2400;
    [ObservableProperty] private double canvasHeight = 1440;
    [ObservableProperty] private double zoomFactor   = 1.0;

    // Zvraznen bunka (hover)
    [ObservableProperty] private int hoverCellX = -1;
    [ObservableProperty] private int hoverCellY = -1;

    // Vybran prvok
    [ObservableProperty] private LayoutElement? selectedElement;

    /// <summary>Automaticky sa vol pri zmene SelectedElement - aktualizuje Inpektor.</summary>
    partial void OnSelectedElementChanged(LayoutElement? value)
    {
        NotifyInspector();
    }

    // Kolekcia prvkov  ItemsControl na Canvas ich renderuje
    public ObservableCollection<LayoutElement> Elements { get; } = new();

    //  Multi-select 
    private readonly MarqueeSelectionService _selection = new();
    
    /// <summary>Service pre sprvu vberu prvkov.</summary>
    public MarqueeSelectionService Selection => _selection;
    
    /// <summary>i je aktvny multi-select reim.</summary>
    public bool IsMultiSelectMode => _selection.SelectionCount > 1;
    
    /// <summary>Poet vybranch prvkov.</summary>
    public int SelectedCount => _selection.SelectionCount;

    /// <summary>Multi-clipboard pre koprovanie viacerch prvkov.</summary>
    private List<LayoutElement>? _multiClipboard;

    //  Inpektor 
    // Sradnice s 1-based (rovnako ako pravtka) - bunka 1, 2, 3...
    public string InspectorType   => SelectedElement == null ? "-" : GetMarkerDisplayName(SelectedElement);
    public int InspectorX         => SelectedElement == null ? 0 : (int)(SelectedElement.X / CellSize) + 1;
    public int InspectorY         => SelectedElement == null ? 0 : (int)(SelectedElement.Y / CellSize) + 1;
    public double InspectorAngle  => SelectedElement?.Rotation ?? 0;

    /// <summary>Vrti uvatesky prvetiv nzov markeru pre Inpektor.</summary>
    private string GetMarkerDisplayName(LayoutElement element)
    {
        // Pre TrackSegment, Curve, at. pouijeme MarkerKey
        var key = element.MarkerKey;
        
        return key switch
        {
            "TrackSegment" => "Rovn koaj",
            "Bumper" => "Zaradlo",
            "Curve_45" => "Oblk 45",
            "Curve_90" => "Oblk 90",
            "Turnout_L" => "Vhybka av",
            "Turnout_R" => "Vhybka prav",
            "TurnoutL90" => "Vhybka av 90",
            "TurnoutR90" => "Vhybka prav 90",
            "TurnoutCurve_L" => "Oblkov vhybka av",
            "TurnoutCurve_R" => "Oblkov vhybka prav",
            "Turnout_Y" => "Y-vhybka",
            "Turnout_3W" => "3-cestn vhybka",
            "Cross90" => "Kriovatka 90",
            "Cross45" => "Kriovatka 45",
            "DoubleSlip" => "Kriovatkov vhybka",
            "Bridge90" => "Most 90",
            "Bridge45L" => "Most 45 av",
            "Bridge45R" => "Most 45 prav",
            "Signal" or "Signal5" or "Signal4" or "Signal2Main" or "Signal2Shunt" or "Signal2Route" or "Signal3Entry" => "Signl / nves",
            "Sensor" => "Senzor obsadenosti",
            "Block" => "Blok",  // Vdy len "Blok", nie Label
            _ => element.ElementType.ToString()
        };
    }

    /// <summary>Editovaten nzov/ttok prvku (binduje sa obojsmerne na SelectedElement.Label).</summary>
    public string InspectorLabel
    {
        get => SelectedElement?.Label ?? "";
        set
        {
            if (SelectedElement == null) return;
            CaptureUndoCheckpoint("inspector-label");
            SelectedElement.Label = value;
            SyncToProject();
        }
    }

    /// <summary>DCC adresa  zobrazuje sa len ak prvok m DCC adresu (vhybky, signly, senzory).</summary>
    public int InspectorDccAddress
    {
        get => SelectedElement is TurnoutElement t  ? t.DccAddress
             : SelectedElement is SignalElement  s  ? s.DccAddress
             : SelectedElement is SensorElement  se ? se.SensorAddress
             : 0;
        set
        {
            if (!DccAccessoryAddressValidator.IsValid(value))
            {
                OnPropertyChanged(nameof(InspectorDccAddressError));
                OnPropertyChanged(nameof(InspectorHasDccAddressError));
                return;
            }
            try
            {
                CaptureUndoCheckpoint("inspector-dcc");
                if      (SelectedElement is TurnoutElement t)  { t.DccAddress    = value; SyncToProject(); }
                else if (SelectedElement is SignalElement  s)  { s.DccAddress    = value; SyncToProject(); }
                else if (SelectedElement is SensorElement  se) { se.SensorAddress = value; SyncToProject(); }
            }
            catch (ArgumentOutOfRangeException)
            {
                // Bezpenostn sie  neplatn hodnota, nezapeme
            }
            OnPropertyChanged(nameof(InspectorDccAddressError));
            OnPropertyChanged(nameof(InspectorHasDccAddressError));
        }
    }

    /// <summary>Chybov hlka pri neplatnej DCC adrese v inpektore.</summary>
    public string InspectorDccAddressError =>
        DccAccessoryAddressValidator.GetValidationError(InspectorDccAddress);

    /// <summary>Viditenos chybovej hlky v inpektore.</summary>
    public bool InspectorHasDccAddressError => !string.IsNullOrEmpty(InspectorDccAddressError);

    /// <summary>i m vybran prvok DCC adresu (pre zobrazenie/skrytie poa v inpektore).</summary>
    public bool InspectorHasDcc => SelectedElement is TurnoutElement or SignalElement or SensorElement;

    /// <summary>i je v inpektore vybran blok (zobraz smerov priradenie nvestidiel).</summary>
    public bool InspectorIsBlock => SelectedElement is BlockElement;

    public ObservableCollection<InspectorDirectionalSignalOption> InspectorDirectionalSignalItems { get; } = new();

    private bool _updatingInspectorDirectionalSignals;
    private InspectorDirectionalSignalOption? _inspectorSelectedSignalLeft;
    private InspectorDirectionalSignalOption? _inspectorSelectedSignalRight;
    private InspectorDirectionalSignalOption? _inspectorSelectedSignalUp;
    private InspectorDirectionalSignalOption? _inspectorSelectedSignalDown;
    private string _inspectorSignalDirectionWarning = string.Empty;
    private int _inspectorSignalHighlightVersion;

    public InspectorDirectionalSignalOption? InspectorSelectedSignalLeft
    {
        get => _inspectorSelectedSignalLeft;
        set
        {
            if (SetProperty(ref _inspectorSelectedSignalLeft, value) && !_updatingInspectorDirectionalSignals)
                ApplyInspectorDirectionalSignalAssignments();
        }
    }

    public InspectorDirectionalSignalOption? InspectorSelectedSignalRight
    {
        get => _inspectorSelectedSignalRight;
        set
        {
            if (SetProperty(ref _inspectorSelectedSignalRight, value) && !_updatingInspectorDirectionalSignals)
                ApplyInspectorDirectionalSignalAssignments();
        }
    }

    public InspectorDirectionalSignalOption? InspectorSelectedSignalUp
    {
        get => _inspectorSelectedSignalUp;
        set
        {
            if (SetProperty(ref _inspectorSelectedSignalUp, value) && !_updatingInspectorDirectionalSignals)
                ApplyInspectorDirectionalSignalAssignments();
        }
    }

    public InspectorDirectionalSignalOption? InspectorSelectedSignalDown
    {
        get => _inspectorSelectedSignalDown;
        set
        {
            if (SetProperty(ref _inspectorSelectedSignalDown, value) && !_updatingInspectorDirectionalSignals)
                ApplyInspectorDirectionalSignalAssignments();
        }
    }

    public string InspectorSignalDirectionWarning
    {
        get => _inspectorSignalDirectionWarning;
        private set
        {
            if (SetProperty(ref _inspectorSignalDirectionWarning, value))
                OnPropertyChanged(nameof(InspectorHasSignalDirectionWarning));
        }
    }

    public bool InspectorHasSignalDirectionWarning => !string.IsNullOrWhiteSpace(InspectorSignalDirectionWarning);

    /// <summary>Verzia zvraznenia pre signal overlay v canvase (inkrement pri zmene vberu).</summary>
    public int InspectorSignalHighlightVersion => _inspectorSignalHighlightVersion;

    /// <summary>ID nvestidiel vybranch v inspector comboboxoch.</summary>
    public IReadOnlyCollection<string> InspectorHighlightedSignalIds
        => new[]
            {
                InspectorSelectedSignalLeft?.Id,
                InspectorSelectedSignalRight?.Id,
                InspectorSelectedSignalUp?.Id,
                InspectorSelectedSignalDown?.Id
            }
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

    /// <summary>Pomocn metda pre View  i m by signal zvraznen poda inspector priradenia.</summary>
    public bool IsInspectorDirectionalSignalHighlighted(LayoutElement element)
        => element is SignalElement signal
           && InspectorHighlightedSignalIds.Any(id => string.Equals(id, signal.Id, StringComparison.OrdinalIgnoreCase));

    /// <summary>i je nieo vybran  pre IsEnabled v inpektore.</summary>
    public bool HasSelection => SelectedElement != null;

    /// <summary>i je aktvny reim Select  pre podfarbenie tlaidla Vber.</summary>
    public bool IsSelectToolActive => SelectedTool == LayoutTool.Select;

    public LayoutEditorViewModel() : this(null!) { }

    public LayoutEditorViewModel(SettingsManager settings)
    {
        _settings = settings;
        // Pri tarte je aktvny reim Select
        SelectedTool = LayoutTool.Select;
        SelectedMarkerKey = null;
        
        // Multi-select event handler
        _selection.SelectionChanged += OnSelectionChanged;
        Elements.CollectionChanged += (_, _) => NotifyInspector();
    }

    private void RebuildInspectorDirectionalSignalItems()
    {
        // Pri Clear()/Add() ComboBox automaticky propaguje SelectedItem=null sp do VM.
        // Bez tejto ochrany by setter zavolal ApplyInspectorDirectionalSignalAssignments()
        // a prepsal block.Signal*Id na null hne po uloen dialgu Vlastnosti bloku.
        _updatingInspectorDirectionalSignals = true;
        try
        {
            InspectorDirectionalSignalItems.Clear();
            InspectorDirectionalSignalItems.Add(new InspectorDirectionalSignalOption
            {
                Id = null,
                DisplayName = "-- bez priradenia --"
            });

            foreach (var signal in Elements.OfType<SignalElement>()
                         .Where(s => !string.IsNullOrWhiteSpace(s.Id))
                         .OrderBy(s => s.Label, StringComparer.CurrentCultureIgnoreCase))
            {
                var display = string.IsNullOrWhiteSpace(signal.Label)
                    ? $"Nvestidlo {signal.Id[..Math.Min(8, signal.Id.Length)]}"
                    : signal.Label;

                InspectorDirectionalSignalItems.Add(new InspectorDirectionalSignalOption
                {
                    Id = signal.Id,
                    DisplayName = display
                });
            }
        }
        finally
        {
            _updatingInspectorDirectionalSignals = false;
        }
    }

    private void RefreshInspectorDirectionalSignalSelectionsFromBlock()
    {
        _updatingInspectorDirectionalSignals = true;
        try
        {
            if (SelectedElement is not BlockElement block)
            {
                InspectorSelectedSignalLeft = InspectorDirectionalSignalItems.FirstOrDefault();
                InspectorSelectedSignalRight = InspectorDirectionalSignalItems.FirstOrDefault();
                InspectorSelectedSignalUp = InspectorDirectionalSignalItems.FirstOrDefault();
                InspectorSelectedSignalDown = InspectorDirectionalSignalItems.FirstOrDefault();
                InspectorSignalDirectionWarning = string.Empty;
                return;
            }

            InspectorSelectedSignalLeft = FindInspectorDirectionalSignalOption(block.SignalLeftId);
            InspectorSelectedSignalRight = FindInspectorDirectionalSignalOption(block.SignalRightId);
            InspectorSelectedSignalUp = FindInspectorDirectionalSignalOption(block.SignalUpId);
            InspectorSelectedSignalDown = FindInspectorDirectionalSignalOption(block.SignalDownId);
        }
        finally
        {
            _updatingInspectorDirectionalSignals = false;
        }

        UpdateInspectorSignalDirectionWarning();
    }

    private InspectorDirectionalSignalOption? FindInspectorDirectionalSignalOption(string? signalId)
    {
        if (string.IsNullOrWhiteSpace(signalId))
            return InspectorDirectionalSignalItems.FirstOrDefault();

        var normalized = signalId.Trim();
        return InspectorDirectionalSignalItems.FirstOrDefault(s =>
                   string.Equals(s.Id, normalized, StringComparison.OrdinalIgnoreCase))
               ?? InspectorDirectionalSignalItems.FirstOrDefault();
    }

    private void ApplyInspectorDirectionalSignalAssignments()
    {
        if (SelectedElement is not BlockElement block)
            return;

        block.SignalLeftId = NormalizeInspectorSignalId(InspectorSelectedSignalLeft?.Id);
        block.SignalRightId = NormalizeInspectorSignalId(InspectorSelectedSignalRight?.Id);
        block.SignalUpId = NormalizeInspectorSignalId(InspectorSelectedSignalUp?.Id);
        block.SignalDownId = NormalizeInspectorSignalId(InspectorSelectedSignalDown?.Id);

        UpdateInspectorSignalDirectionWarning();
        RaiseInspectorSignalHighlightChanged();
        SyncToProject();
    }

    private static string? NormalizeInspectorSignalId(string? signalId)
        => string.IsNullOrWhiteSpace(signalId) ? null : signalId.Trim();

    private void RaiseInspectorSignalHighlightChanged()
    {
        _inspectorSignalHighlightVersion++;
        OnPropertyChanged(nameof(InspectorSignalHighlightVersion));
        OnPropertyChanged(nameof(InspectorHighlightedSignalIds));
    }

    private void UpdateInspectorSignalDirectionWarning()
    {
        if (SelectedElement is not BlockElement)
        {
            InspectorSignalDirectionWarning = string.Empty;
            return;
        }

        var checks = new (NavigationDirection Direction, string? SignalId)[]
        {
            (NavigationDirection.Left, InspectorSelectedSignalLeft?.Id),
            (NavigationDirection.Right, InspectorSelectedSignalRight?.Id),
            (NavigationDirection.Up, InspectorSelectedSignalUp?.Id),
            (NavigationDirection.Down, InspectorSelectedSignalDown?.Id),
        };

        foreach (var check in checks)
        {
            var warning = BuildInspectorDirectionWarning(check.Direction, check.SignalId);
            if (!string.IsNullOrWhiteSpace(warning))
            {
                InspectorSignalDirectionWarning = warning;
                return;
            }
        }

        InspectorSignalDirectionWarning = string.Empty;
    }

    private string BuildInspectorDirectionWarning(NavigationDirection direction, string? signalId)
    {
        if (string.IsNullOrWhiteSpace(signalId))
            return string.Empty;

        var signal = Elements.OfType<SignalElement>()
            .FirstOrDefault(s => string.Equals(s.Id, signalId, StringComparison.OrdinalIgnoreCase));
        if (signal == null)
            return string.Empty;

        var facingDirection = ResolveSignalFacingDirection(signal.Rotation);
        if (facingDirection == direction)
            return string.Empty;

        var signalName = string.IsNullOrWhiteSpace(signal.Label)
            ? signal.Id[..Math.Min(8, signal.Id.Length)]
            : signal.Label;

        return $"Varovanie: orientcia nvestidla '{signalName}' pravdepodobne nezodpoved smeru {DirectionToText(direction)}.";
    }

    private static NavigationDirection ResolveSignalFacingDirection(double rotation)
    {
        int rightAngle = ((int)Math.Round(rotation) % 360 + 360) % 360;
        rightAngle = ((rightAngle + 45) / 90) * 90 % 360;

        return rightAngle switch
        {
            90 => NavigationDirection.Right,
            180 => NavigationDirection.Down,
            270 => NavigationDirection.Left,
            _ => NavigationDirection.Up
        };
    }

    private static string DirectionToText(NavigationDirection direction)
        => direction switch
        {
            NavigationDirection.Left => "doava",
            NavigationDirection.Right => "doprava",
            NavigationDirection.Up => "nahor",
            NavigationDirection.Down => "nadol",
            _ => "?"
        };

    //  Multi-select metdy 
    
    /// <summary>Handler pre zmenu vberu.</summary>
    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(IsMultiSelectMode));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
    }
    
    /// <summary>Event pre batch opercie - View odpoj handlery.</summary>
    public event Action? BatchOperationStarting;
    public event Action? BatchOperationCompleted;

    //  Priradenie lokomotvy k bloku 

    /// <summary>
    /// Udalos vyvolan po spenom priraden lokomotvy k bloku.
    /// Parametry: (blok, kd loky, isForward).
    /// </summary>
    public event Action<BlockElement, string, bool>? LocomotiveAssignedToBlock;

    /// <summary>
    /// Prirad lokomotvu (poda jej kdu) k zadanmu bloku so smerom.
    /// Ak bola predtm v inom bloku, zo starho bloku ju automaticky odstrni.
    /// </summary>
    public void AssignLocomotiveToBlock(BlockElement block, string locoCode, bool isForward)
    {
        CaptureUndoCheckpoint("assign-loco", force: true);
        // Editor režim: pri manuálnom umiestnení lokomotívy chceme kontrolovať len cieľový blok.
        // Kontrola susedných blokov (safetyDistanceBlocks > 0) je vhodnejšia pre runtime jazdu,
        // ale pri editácii spôsobuje „nepriradenie“ aj v prípadoch, ktoré užívateľ považuje za OK.
        var collision = _collisionService.EvaluateEntry(Elements, block.Id, locoCode, safetyDistanceBlocks: 0);
        if (!collision.IsSafe)
        {
            Log.Warning("Locomotive assign blocked in editor. Loco={LocoCode}, Block={BlockId}, Reason={Reason}, BlockingBlock={BlockingBlockId}",
                locoCode, block.Id, collision.Reason, collision.BlockingBlockId);
            return;
        }

        // Odstr lokomotvu z predchdzajceho bloku (ak existuje)
        foreach (var el in Elements)
        {
            if (el is BlockElement other && other != block && other.AssignedLocoId == locoCode)
            {
                other.AssignedLocoId = null;
                other.IsOccupied     = false;
                other.IsDragOverActive = false;
                // Vyvolaj prekreslenie starho bloku
                RequestBlockRepaint?.Invoke(other);
                break;
            }
        }

        // Nastav nov priradenie
        block.AssignedLocoId      = locoCode;
        block.AssignedLocoIsForward = isForward;
        block.IsOccupied          = true;
        block.IsDragOverActive    = false;

        RequestBlockRepaint?.Invoke(block);
        LocomotiveAssignedToBlock?.Invoke(block, locoCode, isForward);
    }

    /// <summary>
    /// Nastav prechodov stav drag-over na bloku. Ak je block null, vyist predchdzajci stav.
    /// </summary>
    public void SetBlockDragOver(BlockElement? block, bool isForward)
    {
        // Vyisti predchdzajci drag-over blok
        if (_dragOverBlock != null && _dragOverBlock != block)
        {
            _dragOverBlock.IsDragOverActive = false;
            RequestBlockRepaint?.Invoke(_dragOverBlock);
        }

        _dragOverBlock = block;

        if (block != null)
        {
            block.IsDragOverActive = true;
            block.DragOverIsForward = isForward;
            RequestBlockRepaint?.Invoke(block);
        }
    }

    private BlockElement? _dragOverBlock;

    /// <summary>Callback pre View  poiada View o prekreslenie jednho bloku.</summary>
    public Action<BlockElement>? RequestBlockRepaint { get; set; }
    
    /// <summary>Vymae vetky vybran prvky (multi-select).</summary>
    [RelayCommand]
    public void DeleteSelectedElements()
    {
        if (_selection.SelectionCount == 0) return;

        CaptureUndoCheckpoint("delete-selected", force: true);
    
        var toDelete = _selection.SelectedElements.ToList();
        
        // Signalizuj View e zana batch opercia
        BatchOperationStarting?.Invoke();
        
        try
        {
            // Dvkov odstrnenie
            foreach (var elem in toDelete)
            {
                Elements.Remove(elem);
            }
            
            _selection.ClearSelection();
            SelectedElement = null;
        }
        finally
        {
            // Signalizuj View e batch opercia skonila
            BatchOperationCompleted?.Invoke();
        }
        
        NotifyInspector();
        SyncToProject();
    }
    
    [RelayCommand]
    public void CopySelectedElements()
    {
        if (_selection.SelectionCount == 0) return;
        
        _multiClipboard = new List<LayoutElement>();
        foreach (var elem in _selection.SelectedElements)
        {
            _multiClipboard.Add(elem.Clone());
        }
    }
    
    /// <summary>Vystrihne vetky vybran prvky.</summary>
    [RelayCommand]
    public void CutSelectedElements()
    {
        if (_selection.SelectionCount == 0) return;
        
        CopySelectedElements();
        DeleteSelectedElements();
    }
    
    /// <summary>Vlo prvky z multi-clipboard.</summary>
    [RelayCommand]
    public void PasteSelectedElements()
    {
        if (_multiClipboard == null || _multiClipboard.Count == 0) return;

        CaptureUndoCheckpoint("paste-selected", force: true);
        
        _selection.ClearSelection();

        var clones = new List<LayoutElement>(_multiClipboard.Count);
        foreach (var elem in _multiClipboard)
        {
            var clone = elem.Clone();
            // Posun o jednu bunku doprava/dole
            clone.X += CellSize;
            clone.Y += CellSize;
            SnapElementToGrid(clone);
            clones.Add(clone);
        }

        if (WouldCreateIllegalOverlap(changed: clones, allElementsAfterChange: Elements.Concat(clones)))
        {
            Log.Warning("PasteSelectedElements zrušené: viedlo by k nelegálnemu prekrytiu prvkov.");
            return;
        }

        foreach (var clone in clones)
        {
            Elements.Add(clone);
            _selection.AddToSelection(clone);
        }

        SyncToProject();
    }
    
    /// <summary>Posunie vetky vybran prvky o dan offset.</summary>
    public void MoveSelectedElements(double deltaX, double deltaY)
    {
        if (_selection.SelectionCount == 0) return;

        CaptureUndoCheckpoint("move-selected", force: true);

        var moved = _selection.SelectedElements.ToList();
        var before = moved.ToDictionary(e => e, e => (e.X, e.Y));

        foreach (var elem in moved)
        {
            elem.X += deltaX;
            elem.Y += deltaY;
            SnapElementToGrid(elem);
        }

        if (WouldCreateIllegalOverlap(changed: moved, allElementsAfterChange: Elements))
        {
            // Revert
            foreach (var kv in before)
            {
                kv.Key.X = kv.Value.X;
                kv.Key.Y = kv.Value.Y;
            }
            Log.Warning("MoveSelectedElements zrušené: viedlo by k nelegálnemu prekrytiu prvkov.");
            return;
        }

        SyncToProject();
    }
    
    /// <summary>Vyberie vetky prvky.</summary>
    [RelayCommand]
    public void SelectAll()
    {
        _selection.SelectAll(Elements);
    }



    //  Prkazy nstrojov 
    [RelayCommand]
    private void SelectTool()
    {
        if (SelectedTool == LayoutTool.Select)
        {
            // Optovn stlaenie vypne Select  prepne na Place (bez markera)
            SelectedTool = LayoutTool.Place;
        }
        else
        {
            SelectedTool = LayoutTool.Select;
            SelectedMarkerKey = null;
        }
        OnPropertyChanged(nameof(IsSelectToolActive));
    }

    // Mapa CommandParameter  LayoutElementType
    private static readonly System.Collections.Generic.Dictionary<string, LayoutElementType> MarkerMap = new()
    {
        ["TrackSegment"]  = LayoutElementType.TrackSegment,
        ["Bumper"]        = LayoutElementType.Bumper,
        ["Curve_45"]      = LayoutElementType.CurveNarrow,
        ["Curve_90"]      = LayoutElementType.Curve,
        ["Curve_135"]     = LayoutElementType.CurveNarrow,
        ["Curve_180"]     = LayoutElementType.Curve,
        ["Turnout_L"]     = LayoutElementType.Turnout,
        ["Turnout_R"]     = LayoutElementType.Turnout,
        ["TurnoutL90"]    = LayoutElementType.TurnoutL90,
        ["TurnoutR90"]    = LayoutElementType.TurnoutR90,
        ["TurnoutCurve_L"]= LayoutElementType.TurnoutCurve,
        ["TurnoutCurve_R"]= LayoutElementType.TurnoutCurve,
        ["Turnout_Y"]     = LayoutElementType.TurnoutY,
        ["Turnout_3W"]    = LayoutElementType.Turnout3W,
        ["Cross90"]       = LayoutElementType.Cross90,
        ["Cross45"]       = LayoutElementType.Cross45,
        ["DoubleSlip"]    = LayoutElementType.DoubleSlip,
        ["Bridge90"]      = LayoutElementType.Bridge90,
        ["Bridge45L"]     = LayoutElementType.Bridge45L,
        ["Bridge45R"]     = LayoutElementType.Bridge45R,
        ["Signal"]        = LayoutElementType.Signal,
        ["Signal5"]       = LayoutElementType.Signal,  // 5-znakov vchodov (default)
        ["Signal4"]       = LayoutElementType.Signal,  // 4-znakov odchodov
        ["Signal2Main"]   = LayoutElementType.Signal,  // 2-znakov hlavn (erven-zelen)
        ["Signal2Shunt"]  = LayoutElementType.Signal,  // 2-znakov zriaovacie (modr-biela)
        ["Signal2Route"]  = LayoutElementType.Signal,  // 2-znakov cestové (červená-biela)
        ["Signal3Entry"]  = LayoutElementType.Signal,  // 3-znakové vchodové (zelená-červená-biela)
        ["Sensor"]        = LayoutElementType.Sensor,
        ["Block"]         = LayoutElementType.Block,
        ["Route"]         = LayoutElementType.Route,
        ["Text"]          = LayoutElementType.Text,
    };

    [RelayCommand]
    private void PlaceElement(string typeStr)
    {
        if (MarkerMap.TryGetValue(typeStr, out var t))
        {
            PendingElementType = t;
            SelectedTool = LayoutTool.Place;
        }
        SelectedMarkerKey = typeStr;
        OnPropertyChanged(nameof(IsSelectToolActive));
    }

    // Rotation while placing: pending rotation in degrees (multiples of 45)
    [ObservableProperty] private int pendingRotation = 0;

    public void RotatePendingRight()
    {
        PendingRotation = (PendingRotation + 45) % 360;
    }

    public void RotatePendingLeft()
    {
        var v = (PendingRotation - 45) % 360;
        PendingRotation = v < 0 ? v + 360 : v;
    }

    [RelayCommand]
    public void DeleteSelected()
    {
        if (SelectedElement != null)
        {
            CaptureUndoCheckpoint("delete", force: true);
            Elements.Remove(SelectedElement);
            SelectedElement = null;
            _selection.ClearSelection(); // Vymaza aj multi-select vber
            NotifyInspector();
            SyncToProject();
        }
    }

    [RelayCommand]
    public void RotateSelected()
    {
        if (SelectedElement == null) return;

        CaptureUndoCheckpoint("rotate", force: true);
    
        // Block sa rotuje len do 3 uhlov (0, 90, 270)
        if (SelectedElement.MarkerKey == "Block")
        {
            SelectedElement.Rotation = SelectedElement.Rotation switch
            {
                0 => 90,
                90 => 270,
                _ => 0  // 270 alebo akkovek in uhol  0
            };
        }
        // Prvky s vlastnou geometrickou rotciou musia s po 90
        // (TurnoutL90/R90, Signal  inak sa visulne neotoia, lebo snapuj na 90)
        else if (SelectedElement.MarkerKey is "TurnoutL90" or "TurnoutR90" or "Signal" or "Signal5" or "Signal4" or "Signal2Main" or "Signal2Shunt" or "Signal2Route" or "Signal3Entry")
        {
            SelectedElement.Rotation = (SelectedElement.Rotation + 90) % 360;
        }
        else
        {
            SelectedElement.Rotation = (SelectedElement.Rotation + 45) % 360;
        }
    
        OnPropertyChanged(nameof(InspectorAngle));
        SyncToProject();
    }

    public void RotateSelectedLeft()
    {
        if (SelectedElement == null) return;

        CaptureUndoCheckpoint("rotate", force: true);
    
        // Block sa rotuje len do 3 uhlov (0, 90, 270)
        if (SelectedElement.MarkerKey == "Block")
        {
            SelectedElement.Rotation = SelectedElement.Rotation switch
            {
                0 => 270,
                270 => 90,
                _ => 0  // 90 alebo akkovek in uhol  0
            };
        }
        // 90-rotcia (TurnoutL90/R90, Signal)
        else if (SelectedElement.MarkerKey is "TurnoutL90" or "TurnoutR90" or "Signal" or "Signal5" or "Signal4" or "Signal2Main" or "Signal2Shunt" or "Signal2Route" or "Signal3Entry")
        {
            var v = (SelectedElement.Rotation - 90) % 360;
            SelectedElement.Rotation = v < 0 ? v + 360 : v;
        }
        else
        {
            // Vetky ostatn prvky rotuj po 45 doava
            var v = (SelectedElement.Rotation - 45) % 360;
            SelectedElement.Rotation = v < 0 ? v + 360 : v;
        }
    
        OnPropertyChanged(nameof(InspectorAngle));
        SyncToProject();
    }

    //  Clipboard pre vetky prvky 
    private LayoutElement? _clipboard;

    [RelayCommand]
    public void CopyElement()
    {
        if (SelectedElement != null)
        {
            _clipboard = SelectedElement.Clone();
            OnPropertyChanged(nameof(CanPaste));
            OnPropertyChanged(nameof(CanPasteBlock));
        }
    }

    [RelayCommand]
    public void CutElement()
    {
        if (SelectedElement == null) return;
        CaptureUndoCheckpoint("cut", force: true);
        _clipboard = SelectedElement.Clone();
        Elements.Remove(SelectedElement);
        SelectedElement = null;
        NotifyInspector();
        SyncToProject();
        OnPropertyChanged(nameof(CanPaste));
        OnPropertyChanged(nameof(CanPasteBlock));
    }

    [RelayCommand]
    public void PasteElement()
    {
        if (_clipboard == null) return;
        CaptureUndoCheckpoint("paste", force: true);
        var clone = _clipboard.Clone();
        // Posun o jednu bunku doprava/dole aby nebolo prekrytie
        clone.X += CellSize;
        clone.Y += CellSize;
        SnapElementToGrid(clone);

        if (WouldCreateIllegalOverlap(changed: new[] { clone }, allElementsAfterChange: Elements.Append(clone)))
        {
            Log.Warning("PasteElement zrušené: viedlo by k nelegálnemu prekrytiu prvkov.");
            return;
        }

        Elements.Add(clone);
        SelectedElement = clone;
        NotifyInspector();
        SyncToProject();
    }

    /// <summary>Vlo prvok zo schrnky na konkrtnu pozciu (snap na mrieku).</summary>
    public void PasteElementAt(double x, double y)
    {
        if ((_multiClipboard == null || _multiClipboard.Count == 0) && _clipboard == null)
            return;

        // Pozn.: capture iba raz na začiatku, aby sa multi-paste nezapisovalo po každom prvku.
        CaptureUndoCheckpoint("paste-at", force: true);
        // Ak je multi-clipboard pln, pouijeme PasteSelectedElements
        if (_multiClipboard != null && _multiClipboard.Count > 0)
        {
            // Njdeme minimlnu X a Y pozciu (av horn roh vberu)
            double minX = _multiClipboard.Min(e => e.X);
            double minY = _multiClipboard.Min(e => e.Y);
            
            // Snap cieovej pozcie na mrieku
            double targetX = System.Math.Floor(x / CellSize) * CellSize;
            double targetY = System.Math.Floor(y / CellSize) * CellSize;
            
            // Pre kad prvok vypotame relatvny offset a vlome ho
            var clones = new List<LayoutElement>(_multiClipboard.Count);
            foreach (var elem in _multiClipboard)
            {
                var clone = elem.Clone();
                
                // Relatvny offset od avho hornho rohu
                double offsetX = elem.X - minX;
                double offsetY = elem.Y - minY;
                
                // Nov pozcia = cieov pozcia + relatvny offset
                clone.X = targetX + offsetX;
                clone.Y = targetY + offsetY;
                SnapElementToGrid(clone);
                clones.Add(clone);
            }

            if (WouldCreateIllegalOverlap(changed: clones, allElementsAfterChange: Elements.Concat(clones)))
            {
                Log.Warning("PasteElementAt (multi) zrušené: viedlo by k nelegálnemu prekrytiu prvkov.");
                return;
            }

            foreach (var c in clones)
                Elements.Add(c);
            
            // Zrume vber - vloen prvky u nemusia by vybran
            _selection.ClearSelection();
            SelectedElement = null;
            NotifyInspector();
            
            SyncToProject();
            return;
        }
        
        // Inak pouijeme single clipboard
        if (_clipboard == null) return;
        var singleClone = _clipboard.Clone();
        singleClone.X = System.Math.Floor(x / CellSize) * CellSize;
        singleClone.Y = System.Math.Floor(y / CellSize) * CellSize;
        SnapElementToGrid(singleClone);

        if (WouldCreateIllegalOverlap(changed: new[] { singleClone }, allElementsAfterChange: Elements.Append(singleClone)))
        {
            Log.Warning("PasteElementAt zrušené: viedlo by k nelegálnemu prekrytiu prvkov.");
            return;
        }

        Elements.Add(singleClone);
        
        // Zrume vber aj pre single clipboard
        SelectedElement = null;
        NotifyInspector();
        SyncToProject();
    }

    //  Kompatibilita (star nzov prkazov pre Block) 
    public bool CanPasteBlock => _clipboard?.MarkerKey == "Block";
    
    /// <summary>i je v schrnke nejak prvok (pre zobrazenie kontextovho menu Vloi).</summary>
    public bool CanPaste => _clipboard != null || (_multiClipboard != null && _multiClipboard.Count > 0);
    
    [RelayCommand]
    public void CopyBlock() => CopyElement();
    
    [RelayCommand]
    public void CutBlock() => CutElement();
    
    [RelayCommand]
    public void PasteBlock() => PasteElement();
    
    public void PasteBlockAt(double x, double y) => PasteElementAt(x, y);
    
    //  Vlastnosti prvkov 

    /// <summary>Event na otvorenie dialgu Vlastnosti bloku (handle v View).</summary>
    public event Action<BlockElement>? BlockPropertiesRequested;

    /// <summary>Event na otvorenie dialgu Vlastnosti vhybky (handle v View).</summary>
    public event Action<TurnoutElement>? TurnoutPropertiesRequested;

    /// <summary>Event na otvorenie dialgu Vlastnosti signlu (handle v View).</summary>
    public event Action<SignalElement>? SignalPropertiesRequested;

    /// <summary>Event na otvorenie dialgu Vlastnosti senzora (handle v View).</summary>
    public event Action<SensorElement>? SensorPropertiesRequested;

    /// <summary>Event na otvorenie dialgu Vlastnosti cesty (handle v View).</summary>
    public event Action<RouteElement>? RoutePropertiesRequested;

    /// <summary>Event na otvorenie dialgu Vlastnosti textu (handle v View).</summary>
    public event Action<TextElement>? TextPropertiesRequested;

    /// <summary>Lightweight požiadavka na prekreslenie view bez zmeny kolekcie Elements.</summary>
    public event Action? VisualRefreshRequested;

    /// <summary>Dynamick text pre kontextov menu  napr. "Vlastnosti bloku", "Vlastnosti vhybky" at.</summary>
    public string PropertiesMenuText => SelectedElement?.MarkerKey switch
    {
        "Block" => "Vlastnosti bloku",
        "Turnout_L" or "Turnout_R" or "TurnoutL90" or "TurnoutR90"
            or "TurnoutCurve_L" or "TurnoutCurve_R" 
            or "Turnout_Y" or "Turnout_3W" or "DoubleSlip" => "Vlastnosti vhybky",
        "Signal" or "Signal5" or "Signal4" or "Signal2Main" or "Signal2Shunt" or "Signal2Route" or "Signal3Entry" => "Vlastnosti signlu",
        "Sensor" => "Vlastnosti senzora",
        "Route" => "Vlastnosti cesty",
        _ => "Vlastnosti"
    };

    [RelayCommand]
    public void ShowElementProperties()
    {
        System.Diagnostics.Debug.WriteLine($"[ShowElementProperties] Called. SelectedElement: {SelectedElement?.MarkerKey ?? "NULL"}");
        
        if (SelectedElement == null)
        {
            System.Diagnostics.Debug.WriteLine($"[ShowElementProperties] SelectedElement is NULL, returning.");
            return;
        }

        // Block
        if (SelectedElement.MarkerKey == "Block")
        {
            System.Diagnostics.Debug.WriteLine($"[ShowElementProperties] Opening Block properties...");
            BlockElement block;
            if (SelectedElement is BlockElement be)
            {
                block = be;
            }
            else
            {
                block = new BlockElement
                {
                    Id        = SelectedElement.Id,
                    X         = SelectedElement.X,
                    Y         = SelectedElement.Y,
                    Rotation  = SelectedElement.Rotation,
                    Label     = SelectedElement.Label,
                    MarkerKey = SelectedElement.MarkerKey,
                };
                var idx = Elements.IndexOf(SelectedElement);
                if (idx >= 0) Elements[idx] = block;
                SelectedElement = block;
            }
            BlockPropertiesRequested?.Invoke(block);
            return;
        }

        // Turnout (vetky typy vhybiek)
        if (SelectedElement is TurnoutElement turnout)
        {
            System.Diagnostics.Debug.WriteLine($"[ShowElementProperties] Opening Turnout properties...");
            TurnoutPropertiesRequested?.Invoke(turnout);
            return;
        }

        // Signal
        if (SelectedElement is SignalElement signal)
        {
            System.Diagnostics.Debug.WriteLine($"[ShowElementProperties] Opening Signal properties...");
            SignalPropertiesRequested?.Invoke(signal);
            return;
        }

        // Sensor
        if (SelectedElement is SensorElement sensor)
        {
            System.Diagnostics.Debug.WriteLine($"[ShowElementProperties] Opening Sensor properties...");
            SensorPropertiesRequested?.Invoke(sensor);
            return;
        }

        // Route
        if (SelectedElement.MarkerKey == "Route")
        {
            System.Diagnostics.Debug.WriteLine($"[ShowElementProperties] Opening Route properties...");
            RouteElement route;
            if (SelectedElement is RouteElement re)
            {
                route = re;
            }
            else
            {
                route = new RouteElement
                {
                    Id        = SelectedElement.Id,
                    X         = SelectedElement.X,
                    Y         = SelectedElement.Y,
                    Rotation  = SelectedElement.Rotation,
                    Label     = SelectedElement.Label,
                    MarkerKey = SelectedElement.MarkerKey,
                };
                var idx = Elements.IndexOf(SelectedElement);
                if (idx >= 0) Elements[idx] = route;
                SelectedElement = route;
            }
            RoutePropertiesRequested?.Invoke(route);
            return;
        }

        // Text
        if (SelectedElement is TextElement textElement)
        {
            System.Diagnostics.Debug.WriteLine($"[ShowElementProperties] Opening Text properties...");
            TextPropertiesRequested?.Invoke(textElement);
            return;
        }
        
        System.Diagnostics.Debug.WriteLine($"[ShowElementProperties] WARNING: Element type not supported: {SelectedElement.GetType().Name}");
    }

    /// <summary>Legacy prkaz pre Block (kompatibilita).</summary>
    [RelayCommand]
    public void BlockProperties() => ShowElementProperties();

    /// <summary>Volajte po zatvoren dialgu Vlastnosti bloku (sync + refresh UI).</summary>
    public void OnBlockPropertiesEdited()
    {
        NotifyInspector();
        SyncToProject();
    }

    //  Uloi do projektu 
    
    /// <summary>Event ktor sa zavol po uloen schmy - Prevdzka odpova tento event.</summary>
    public event Action? LayoutSaved;
    
    [RelayCommand]
    private void SaveToProject()
    {
        SyncToProject();
        _settings.SaveProject();
        
        // Notifikova Prevdzku e schma bola uloen
        LayoutSaved?.Invoke();
    }

    public void RequestVisualRefresh()
    {
        VisualRefreshRequested?.Invoke();
    }

    /// <summary>Vdy aktivuje Select reim (bez toggle). Vol sa napr. pravm klikom na canvas.</summary>
    public void ActivateSelectTool()
    {
        SelectedTool = LayoutTool.Select;
        SelectedMarkerKey = null;
        OnPropertyChanged(nameof(IsSelectToolActive));
    }

    /// <summary>Vytvor signlov element s prednastavenm profilom poda MarkerKey.</summary>
    private SignalElement CreateSignalElement(double x, double y)
    {
        // Defaultn ablny: poda kliknutej ikony nastavme vhodn profil
        string profile = SelectedMarkerKey switch
        {
            "Signal2Main"  => "2-aspect-main",    // 2-znakov hlavn (erven-zelen)
            "Signal2Shunt" => "2-aspect-shunt",   // 2-znakov zriaovacie (modr-biela)
            "Signal2Route" => "2-aspect-route",   // 2-znakové cestové (červená-biela)
            "Signal3Entry" => "3-aspect-entry",   // 3-znakové vchodové (zelená-červená-biela)
            "Signal4"      => "5-aspect-departure", // plné SR odchodové (Ž/Z/Č/B/Ž)
            "Signal5"      => "5-aspect",         // 5-znakov vchodov
            _              => "5-aspect",         // default: 5-znakov vchodov
        };

        return new SignalElement
        {
            X = x,
            Y = y,
            SignalSystemId = SignalSystemDefinition.DefaultSystemId,
            SignalProfile = profile,
            Aspect = SignalAspect.Stop,
        };
    }

    //  Nata z projektu 
    public void LoadFromProject()
    {
        _suppressUndoHistory = true;
        Elements.Clear();
        var layout = _settings == null ? null : _settings.CurrentProject?.Layout;
        if (layout == null)
        {
            ResetUndoRedoHistory();
            _suppressUndoHistory = false;
            return;
        }
        
        foreach (var el in layout.Elements)
            Elements.Add(el);
            
        CanvasWidth  = layout.CanvasWidth  > 0 ? layout.CanvasWidth  : 2400;
        CanvasHeight = layout.CanvasHeight > 0 ? layout.CanvasHeight : 1440;

        ResetUndoRedoHistory();
        _suppressUndoHistory = false;
    }

    //  Prida prvok na pozciu 
    public void PlaceElementAt(double x, double y)
    {
        if (SelectedTool != LayoutTool.Place || PendingElementType == null) return;

        CaptureUndoCheckpoint("place", force: true);

        // Snap to grid
        var snappedX = System.Math.Floor(x / CellSize) * CellSize;
        var snappedY = System.Math.Floor(y / CellSize) * CellSize;

        // Pre Text musme kontrolova obsadenos vetkch buniek (default 4x2)
        const double tolerance = 0.01;
        if (PendingElementType == LayoutElementType.Text)
        {
            // Nov text v zkladnej vekosti 4x2 bunky
            int newTextWidth = 4;   // default rka
            int newTextHeight = 2;  // default vka
            
            // Kontrola kolzie pre vetky bunky ktor Text zaber
            for (int row = 0; row < newTextHeight; row++)
            {
                for (int col = 0; col < newTextWidth; col++)
                {
                    double checkX = snappedX + (col * CellSize);
                    double checkY = snappedY + (row * CellSize);
                    
                    foreach (var e in Elements)
                    {
                        // Kontrola kolzie s Blokom
                        if (IsCellInsideBlockFootprint(e, checkX, checkY, tolerance))
                        {
                            return; // Kolzia
                        }
                        // Kontrola kolzie so Signal footprintom (2-aspect = 1x1, ostatn 1x2/2x1)
                        else if (e is SignalElement signalEl)
                        {
                            if (IsCellInsideSignalFootprint(signalEl, checkX, checkY, tolerance))
                                return; // Kolzia
                        }
                        // Kontrola kolzie s inm Text elementom
                        else if (IsCellInsideTextFootprint(e, checkX, checkY, tolerance))
                        {
                            return; // Kolzia
                        }
                        // Kontrola kolzie so tandardnmi markermi (1 bunka)
                        else
                        {
                            if (IsSameCell(e, checkX, checkY, tolerance))
                            {
                                return; // Kolzia
                            }
                        }
                    }
                }
            }
        }
        // Pre Block musme kontrolova obsadenos vetkch buniek (horizontlne pri vkladan)
        else if (PendingElementType == LayoutElementType.Block)
        {
            // Nov block v zkladnej polohe (0) zaber 4 bunky horizontlne (default)
            int newBlockLength = 4; // default pre nov blok
            
            for (int i = 0; i < newBlockLength; i++)
            {
                double checkX = snappedX + (i * CellSize);
                
                // Kontrola kolzie s existujcimi prvkami
                foreach (var e in Elements)
                {
                    // Ak existujci prvok je Block, musme kontrolova vetky jeho bunky
                    if (IsCellInsideBlockFootprint(e, checkX, snappedY, tolerance))
                    {
                        // Kolzia s existujcim Blokom
                        return;
                    }
                    else if (e is SignalElement signalEl)
                    {
                        // Blok nesmie prekry footprint signlu.
                        if (IsCellInsideSignalFootprint(signalEl, checkX, snappedY, tolerance))
                            return;
                    }
                    else
                    {
                        // Ostatn prvky s jednobunkov.
                        // Blok sa me vloi na rovn koaj (TrackSegment), ale nie na ostatn markery
                        if (IsSameCell(e, checkX, snappedY, tolerance))
                        {
                            // Ak je to TrackSegment, meme pokraova (Block sa me vloi na rovn koaj)
                            if (e.MarkerKey == "TrackSegment")
                                continue;
                            
                            // Inak je to kolzia s inm markerom
                            return;
                        }
                    }
                }
            }
        }
        else
        {
            // tandardn kontrola pre ostatn markery:
            // - kolzia s viacbunkovm Block
            // - kolzia so Signal footprintom (1x1 alebo 1x2/2x1)
            // - jednobunkov prvky na rovnakej bunke
            LayoutElement? trackToReplace = null;
            
            foreach (var e in Elements)
            {
                if (IsCellInsideBlockFootprint(e, snappedX, snappedY, tolerance))
                {
                    // Bunka je obsaden Blokom
                    return;
                }
                else if (e is SignalElement signalEl)
                {
                    if (IsCellInsideSignalFootprint(signalEl, snappedX, snappedY, tolerance))
                        return;
                }
                else if (IsSameCell(e, snappedX, snappedY, tolerance))
                {
                    // Bunka je u obsaden inm markerom
                    
                    // 1. Ak na pozcii je Bumper, meme ho nahradi akmkovek inm markerom
                    if (e.MarkerKey == "Bumper")
                    {
                        trackToReplace = e;
                        // Pokraujeme alej, aby sme skontrolovali ostatn kolzie
                    }
                    // 2. Ak vkladme vhybku, most, kriovatku alebo double slip a na pozcii je TrackSegment, ozname koaj na vymazanie
                    else
                    {
                        bool canReplaceTrack = PendingElementType == LayoutElementType.Turnout ||
                                               PendingElementType == LayoutElementType.TurnoutCurve ||
                                               PendingElementType == LayoutElementType.TurnoutY ||
                                               PendingElementType == LayoutElementType.Turnout3W ||
                                               PendingElementType == LayoutElementType.DoubleSlip ||
                                               PendingElementType == LayoutElementType.Bridge90 ||
                                               PendingElementType == LayoutElementType.Bridge45L ||
                                               PendingElementType == LayoutElementType.Bridge45R ||
                                               PendingElementType == LayoutElementType.Cross90 ||
                                               PendingElementType == LayoutElementType.Cross45;
                        
                        if (canReplaceTrack && e.MarkerKey == "TrackSegment")
                        {
                            trackToReplace = e;
                            // Pokraujeme alej, aby sme skontrolovali ostatn kolzie
                        }
                        else
                        {
                            // Kolzia s inm markerom - nedovoli vloenie
                            return;
                        }
                    }
                }
            }
            
            // Ak je oznaen marker na nahradenie (Bumper alebo TrackSegment pri vkladan vhybky/mostu/kriovatky), vymaeme ho
            if (trackToReplace != null)
            {
                Elements.Remove(trackToReplace);
            }
        }

        LayoutElement el = PendingElementType switch
        {
            LayoutElementType.TrackSegment  => new TrackSegmentElement { X = snappedX, Y = snappedY },
            LayoutElementType.Curve         => new CurveElement        { X = snappedX, Y = snappedY },
            LayoutElementType.CurveNarrow   => new CurveElement        { X = snappedX, Y = snappedY, Label = "Narrow" },
            LayoutElementType.Turnout       => new TurnoutElement      { X = snappedX, Y = snappedY },
            LayoutElementType.TurnoutL90    => new TurnoutElement      { X = snappedX, Y = snappedY },
            LayoutElementType.TurnoutR90    => new TurnoutElement      { X = snappedX, Y = snappedY },
            LayoutElementType.TurnoutCurve  => new TurnoutElement      { X = snappedX, Y = snappedY },
            LayoutElementType.TurnoutY      => new TurnoutElement      { X = snappedX, Y = snappedY },
            LayoutElementType.Turnout3W     => new TurnoutElement      { X = snappedX, Y = snappedY },
            LayoutElementType.Cross90       => new TrackSegmentElement { X = snappedX, Y = snappedY, Label = "Cross90" },
            LayoutElementType.Cross45       => new TrackSegmentElement { X = snappedX, Y = snappedY, Label = "Cross45" },
            LayoutElementType.DoubleSlip    => new TurnoutElement      { X = snappedX, Y = snappedY },
            LayoutElementType.Bridge90      => new TrackSegmentElement { X = snappedX, Y = snappedY, Label = "Bridge90" },
            LayoutElementType.Bridge45L     => new TrackSegmentElement { X = snappedX, Y = snappedY, Label = "Bridge45L" },
            LayoutElementType.Bridge45R     => new TrackSegmentElement { X = snappedX, Y = snappedY, Label = "Bridge45R" },
            LayoutElementType.Signal        => CreateSignalElement(snappedX, snappedY),
            LayoutElementType.Sensor        => new SensorElement        { X = snappedX, Y = snappedY },
            LayoutElementType.Bumper        => new BumperElement        { X = snappedX, Y = snappedY },
            LayoutElementType.Block         => new BlockElement         { X = snappedX, Y = snappedY, Label = $"Blok {GetNextBlockNumber()}" },
            LayoutElementType.Route         => new RouteElement         { X = snappedX, Y = snappedY, RouteName = $"Cesta {GetNextRouteNumber()}" },
            LayoutElementType.Text          => new TextElement          { X = snappedX, Y = snappedY, Text = "Text", WidthInCells = 1, HeightInCells = 1, BackgroundColor = "#FFFFFF" },
            _                               => new TrackSegmentElement  { X = snappedX, Y = snappedY },
        };

        // Marker sa vdy vlo vo vchodiskovej polohe 0  rotciu rieia klvesy R/T po vloen
        // Signly ukladme pod jednotnm MarkerKey "Signal" (profil uruje variant).
        el.MarkerKey = PendingElementType == LayoutElementType.Signal
            ? "Signal"
            : (SelectedMarkerKey ?? string.Empty);
        
        // Automatick priraovanie nzvov pre vhybky poda MarkerKey
        if (el is TurnoutElement && string.IsNullOrEmpty(el.Label))
        {
            el.Label = GetTurnoutAutoName(el.MarkerKey);
        }

        if (el is SignalElement && string.IsNullOrWhiteSpace(el.Label))
        {
            el.Label = GetSignalAutoName();
        }
        
        Elements.Add(el);
        SelectedElement = el;
        NotifyInspector();
        SyncToProject();
        
        // Pre TextElement automaticky otvorme dialg vlastnost (potrebuje editova text)
        // SignalElement u m nastaven profil poda kliknutej ikony, dialg nie je potrebn
        if (el is TextElement)
        {
            ShowElementProperties();
        }
    }

    //  Vybra prvok 
    public void SelectElementAt(double x, double y)
    {
        foreach (var el in Elements)
        {
            if (LayoutElementFootprintHelper.IsPointInside(el, x, y, CellSize, compactTwoAspectSignals: true))
            {
                SelectedElement = el;
                NotifyInspector();
                return;
            }
        }
        SelectedElement = null;
        NotifyInspector();
    }

    public void NotifyInspector()
    {
        RebuildInspectorDirectionalSignalItems();
        RefreshInspectorDirectionalSignalSelectionsFromBlock();
        RaiseInspectorSignalHighlightChanged();

        OnPropertyChanged(nameof(InspectorType));
        OnPropertyChanged(nameof(InspectorLabel));
        OnPropertyChanged(nameof(InspectorX));
        OnPropertyChanged(nameof(InspectorY));
        OnPropertyChanged(nameof(InspectorAngle));
        OnPropertyChanged(nameof(InspectorDccAddress));
        OnPropertyChanged(nameof(InspectorDccAddressError));
        OnPropertyChanged(nameof(InspectorHasDccAddressError));
        OnPropertyChanged(nameof(InspectorHasDcc));
        OnPropertyChanged(nameof(InspectorIsBlock));
        OnPropertyChanged(nameof(InspectorDirectionalSignalItems));
        OnPropertyChanged(nameof(InspectorSelectedSignalLeft));
        OnPropertyChanged(nameof(InspectorSelectedSignalRight));
        OnPropertyChanged(nameof(InspectorSelectedSignalUp));
        OnPropertyChanged(nameof(InspectorSelectedSignalDown));
        OnPropertyChanged(nameof(InspectorSignalDirectionWarning));
        OnPropertyChanged(nameof(InspectorHasSignalDirectionWarning));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(PropertiesMenuText));
    }

    public void SyncToProject()
    {
        if (_settings == null)
            return;
        
        try
        {
            // Zabezpei existenciu projektu (ak nie je otvoren, vytvor sa przdny)
            if (_settings.CurrentProject == null)
                _settings.EnsureProjectSettings();
            
            var layout = _settings.CurrentProject?.Layout;
            if (layout == null)
                return;
            
            layout.Elements.Clear();
            foreach (var el in Elements)
                layout.Elements.Add(el);
                
            // Oznai projekt ako zmenen (cez centralizovan tracker)
            if (_settings.CurrentProject != null)
            {
                _settings.Dirty.MarkDirty("layout");
                _settings.NotifyProjectChanged();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to sync layout to project");
            // TODO: Zobraz user-friendly error dialog
        }
    }

    /// <summary>Zska poradov slo pre nov blok (poet blokov + 1).</summary>
    private int GetNextBlockNumber()
    {
        // Spotaj existujce bloky
        int blockCount = Elements.Count(e => e.MarkerKey == "Block");
        return blockCount + 1;
    }

    /// <summary>Zska poradov slo pre nov cestu (poet ciest + 1).</summary>
    private int GetNextRouteNumber()
    {
        // Spotaj existujce cesty
        int routeCount = Elements.Count(e => e.MarkerKey == "Route");
        return routeCount + 1;
    }

    /// <summary>Generuje automatick nzov pre vhybku poda MarkerKey a poradovho sla.</summary>
    private string GetTurnoutAutoName(string markerKey)
    {
        string prefix = GetTurnoutPrefix(markerKey);
        int counter = GetNextTurnoutCount(markerKey);
        
        return $"{prefix} {counter}";
    }

    private string GetSignalAutoName()
    {
        int maxNumber = 0;
        foreach (var signal in Elements.OfType<SignalElement>())
        {
            if (string.IsNullOrWhiteSpace(signal.Label))
                continue;

            var label = signal.Label.Trim();
            if (!label.StartsWith("Na", StringComparison.OrdinalIgnoreCase))
                continue;

            var numericPart = label.Substring(2).Trim();
            if (int.TryParse(numericPart, out int parsed) && parsed > maxNumber)
                maxNumber = parsed;
        }

        return $"Na{maxNumber + 1}";
    }

    /// <summary>Zska poradov slo pre konkrtny typ vhybky.</summary>
    private int GetNextTurnoutCount(string markerKey)
    {
        string prefix = GetTurnoutPrefix(markerKey);
        
        // Njdeme najvyie poradov slo z existujcich nzvov s tmto prefixom
        int maxNumber = 0;
        foreach (var element in Elements)
        {
            if (element.Label != null && element.Label.StartsWith(prefix + " "))
            {
                // Poksime sa extrahova slo z nzvu (napr. "V 5" -> 5)
                var parts = element.Label.Split(' ');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int num))
                {
                    if (num > maxNumber)
                        maxNumber = num;
                }
            }
        }
        
        return maxNumber + 1;
    }

    private static string GetTurnoutPrefix(string markerKey)
        => markerKey switch
        {
            "Turnout_L" => "V",
            "Turnout_R" => "VP",
            "TurnoutL90" => "OV",
            "TurnoutR90" => "OVP",
            "TurnoutCurve_L" => "OVO",
            "TurnoutCurve_R" => "OVPO",
            "Turnout_Y" => "YV",
            "Turnout_3W" => "3W",
            "DoubleSlip" => "KV",
            _ => "V"
        };

    private static bool IsCellInsideSignalFootprint(SignalElement signal, double cellX, double cellY, double tolerance)
    {
        return LayoutElementFootprintHelper.IsPointInside(signal, cellX, cellY, CellSize, compactTwoAspectSignals: true, tolerance: tolerance);
    }

    private static bool IsCellInsideTextFootprint(LayoutElement element, double cellX, double cellY, double tolerance)
    {
        if (element is not TextElement)
            return false;

        return LayoutElementFootprintHelper.IsPointInside(element, cellX, cellY, CellSize, compactTwoAspectSignals: true, tolerance: tolerance);
    }

    private static bool IsSameCell(LayoutElement element, double cellX, double cellY, double tolerance)
        => System.Math.Abs(element.X - cellX) < tolerance && System.Math.Abs(element.Y - cellY) < tolerance;

    private static bool IsCellInsideBlockFootprint(LayoutElement element, double cellX, double cellY, double tolerance)
    {
        if (!string.Equals(element.MarkerKey, "Block", StringComparison.Ordinal))
            return false;

        int blockLength = LayoutElementFootprintHelper.GetBlockLength(element);
        bool isVertical = LayoutElementFootprintHelper.IsVertical(element.Rotation);

        for (int i = 0; i < blockLength; i++)
        {
            double blockX = element.X + (isVertical ? 0 : i * CellSize);
            double blockY = element.Y + (isVertical ? i * CellSize : 0);
            if (System.Math.Abs(cellX - blockX) < tolerance && System.Math.Abs(cellY - blockY) < tolerance)
                return true;
        }

        return false;
    }

    private static void SnapElementToGrid(LayoutElement element)
    {
        element.X = Math.Round(element.X / CellSize, MidpointRounding.AwayFromZero) * CellSize;
        element.Y = Math.Round(element.Y / CellSize, MidpointRounding.AwayFromZero) * CellSize;
    }

    private bool WouldCreateIllegalOverlap(IEnumerable<LayoutElement> changed, IEnumerable<LayoutElement> allElementsAfterChange)
    {
        var changedSet = new HashSet<LayoutElement>(changed);

        var layout = new TrackLayout
        {
            Elements = allElementsAfterChange.ToList()
        };

        var overlaps = LayoutOverlapIntegrityService.FindIllegalOverlaps(layout, CellSize);
        return overlaps.Any(o => o.Elements.Any(changedSet.Contains));
    }
}

public enum LayoutTool { Select, Place }

public sealed class InspectorDirectionalSignalOption
{
    public string? Id { get; init; }
    public string DisplayName { get; init; } = string.Empty;
}

