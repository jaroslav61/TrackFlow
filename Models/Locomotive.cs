using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TrackFlow.Models;

public partial class Locomotive : Vehicle
{
    private string? _trainName;

    public string? TrainName
    {
        get => _trainName;
        set
        {
            if (_trainName == value) return;
            _trainName = value;
            OnPropertyChanged(nameof(TrainName));
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => !string.IsNullOrWhiteSpace(TrainName) ? TrainName : (!string.IsNullOrWhiteSpace(Name) ? Name : Code);

    public Locomotive(string code, string name) : base(code, name)
    {
        IsForward = true;
        AttachAttachedWagonsHandlers();
    }

    // Attached wagons forming a train
    public ObservableCollection<Wagon> AttachedWagons { get; } = new();

    // Backing store for legacy data: codes of attached wagons
    public List<string> AttachedWagonCodes { get; set; } = new();

    // Legacy train name stored in old settings
    public string? TrainNameLegacy { get; set; }

    // Locomotive own weight (optional) - can be used in TrainTotalWeight
    [ObservableProperty]
    private double weight;

    private bool _isFlipped;
    public bool IsFlipped
    {
        get => _isFlipped;
        set
        {
            if (_isFlipped == value) return;
            _isFlipped = value;
            OnPropertyChanged(nameof(IsFlipped));
        }
    }

    // Position index that separates left/right wagon lists in UI
    public int LocoPosition { get; set; } = 0;

    public ObservableCollection<Wagon> Wagons { get; } = new();

    public bool HasWagons => Wagons.Count > 0;

    // Icon file name (e.g. '754.png') - pozor: musí notifikovať bindingy pri zmene
    private string _iconName = string.Empty;
    public string IconName
    {
        get => _iconName;
        set => SetProperty(ref _iconName, value);
    }

    public bool IsDirectionSelected => IsForward || IsReverse;

    private bool _isReversedByOrientation;

    // Logicky nesulad medzi orientaciou cela v bloku a smerom aktivnej cesty.
    public bool IsReversedByOrientation
    {
        get => _isReversedByOrientation;
        set
        {
            if (!SetProperty(ref _isReversedByOrientation, value))
                return;

            OnPropertyChanged(nameof(IsDashboardForwardLit));
            OnPropertyChanged(nameof(IsDashboardReverseLit));
        }
    }

    // Dashboard "vpred" svieti podľa zvoleného/traversal smeru, nie podľa fyzického DCC smeru.
    public bool IsDashboardForwardLit => IsForward;

    // Dashboard "vzad" svieti podľa zvoleného/traversal smeru, nie podľa fyzického DCC smeru.
    public bool IsDashboardReverseLit => IsReverse;

    [ObservableProperty]
    private bool isActive;

    [ObservableProperty]
    private bool isForward;

    [ObservableProperty]
    private bool isReverse;

    [ObservableProperty]
    private int targetSpeed;

    /// <summary>Aktuálne vizuálne zobrazovaná rýchlosť (ramping), 0..100.</summary>
    private int _currentDisplaySpeed;
    public int CurrentDisplaySpeed
    {
        get => _currentDisplaySpeed;
        set => SetProperty(ref _currentDisplaySpeed, value);
    }

    /// <summary>Lokomotíva je umiestnená na fyzickom koľajisku (priradená k bloku).</summary>
    [ObservableProperty]
    private bool isPlacedOnTrack;

    /// <summary>ID bloku, v ktorom sa lokomotíva nachádza (runtime – nie je serializované).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? AssignedBlockId { get; set; }

    /// <summary>DCC adresa lokomotívy (1..10239). Nastavuje sa pri načítaní z projektu.</summary>
    [ObservableProperty]
    private int dccAddress;

    /// <summary>Odkaz na projektový záznam – pre prístup k funkciám, ikone atď.</summary>
    public LocoRecord? Record { get; set; }

    /// <summary>Runtime kópia funkcií z LocoRecord – compiled-binding safe pre dashboard.</summary>
    public ObservableCollection<LocoFunctionDef> Functions { get; } = new();

    
    partial void OnIsForwardChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDirectionSelected));
        OnPropertyChanged(nameof(IsDashboardForwardLit));
        OnPropertyChanged(nameof(IsDashboardReverseLit));

        if (value && IsReverse)
        {
            // use property to ensure notifications and any side-effects
            IsReverse = false;
        }

        if (value)
        {
            // when direction changes to forward, immediately reset target speed
            TargetSpeed = 0;
            CurrentDisplaySpeed = 0;
        }
    }

    partial void OnIsReverseChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDirectionSelected));
        OnPropertyChanged(nameof(IsDashboardForwardLit));
        OnPropertyChanged(nameof(IsDashboardReverseLit));

        if (value && IsForward)
        {
            // use property to ensure notifications and any side-effects
            IsForward = false;
        }

        if (value)
        {
            // when direction changes to reverse, immediately reset target speed
            TargetSpeed = 0;
            CurrentDisplaySpeed = 0;
        }
    }

    partial void OnIsActiveChanged(bool value)
    {
        // when locomotive activation state changes (e.g. start/stop), reset target speed
        // This ensures slider jumps to 0 on activation changes as required.
        TargetSpeed = 0;
        CurrentDisplaySpeed = 0;
    }

    partial void OnWeightChanged(double value)
    {
        OnPropertyChanged(nameof(TrainTotalWeight));
    }

    // Total weight of locomotive + attached wagons
    public double TrainTotalWeight => Weight + AttachedWagons.Sum(w => w?.TotalWeight ?? 0.0);

    // subscribe to AttachedWagons changes to update TrainTotalWeight when collection or wagon contents change
    public Locomotive()
        : base(string.Empty, string.Empty)
    {
        // this parameterless ctor is only used for XAML/designtime if needed; ensure collection handlers
        AttachAttachedWagonsHandlers();
        this.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Name) || e.PropertyName == nameof(Code))
                OnPropertyChanged(nameof(DisplayName));
        };
    }

    // Note: handlers attached from constructors

    private void AttachAttachedWagonsHandlers()
    {
        AttachedWagons.CollectionChanged += AttachedWagons_CollectionChanged;
    }

    private void AttachedWagons_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged ipc)
                    ipc.PropertyChanged += Wagon_PropertyChanged;
            }
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged ipc)
                    ipc.PropertyChanged -= Wagon_PropertyChanged;
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // detach from all (best-effort)
            // NOTE: cannot enumerate old items on Reset; detach by scanning none
        }

        OnPropertyChanged(nameof(TrainTotalWeight));
    }

    private void Wagon_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e == null) return;
        // any change affecting weight should update TrainTotalWeight
        if (e.PropertyName == nameof(Wagon.TareWeight) || e.PropertyName == nameof(Wagon.CargoWeight) || e.PropertyName == nameof(Wagon.TotalWeight))
        {
            OnPropertyChanged(nameof(TrainTotalWeight));
        }
    }

    // remove direction properties if not used
}
