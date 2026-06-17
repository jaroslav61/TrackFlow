using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models.Layout;

namespace TrackFlow.ViewModels.Editor;

/// <summary>
/// ViewModel pre indikátor v bloku - pre zobrazenie v Canvas.
/// </summary>
public partial class BlockIndicatorViewModel : ObservableObject
{
    private const string ActiveContactIconUri = "avares://TrackFlow/Assets/Appicons/16/cont_ind.png";
    private const string InactiveContactIconUri = "avares://TrackFlow/Assets/Appicons/16/cont_ind_d.png";
    private const string FlagIconUri = "avares://TrackFlow/Assets/Appicons/16/flag.png";
    private const string VirtualContactIconUri = "avares://TrackFlow/Assets/Appicons/16/virt_cont.png";

    private readonly BlockIndicator _indicator;
    private readonly double _canvasWidth; // Celková šírka bloku v pixeloch (420px)
    private readonly int _blocklengthMm;  // Celková dĺžka bloku v cm

    public Guid Id => _indicator.Id;
    public BlockIndicatorType Type => _indicator.Type;
    public string Name => _indicator.Name;
    public string ToolTipText => _indicator.Name;
    public bool IsActive => _indicator.IsActive;
    
    [ObservableProperty] private int startCm;
    [ObservableProperty] private int endCm;
    [ObservableProperty] private string portAddress = string.Empty;
    [ObservableProperty] private bool isSelected;

    // Vypočítané vlastnosti pre Canvas
    public double StartX => (StartCm / (double)_blocklengthMm) * _canvasWidth;
    public double Width => ((EndCm - StartCm) / (double)_blocklengthMm) * _canvasWidth;
    public double CenterX => StartX + Width / 2;
    
    // Ikona podľa typu
    public string IconPath => Type switch
    {
        BlockIndicatorType.Contact => IsActive
            ? ActiveContactIconUri
            : InactiveContactIconUri,
        BlockIndicatorType.Flagman => FlagIconUri,
        BlockIndicatorType.Virtual => VirtualContactIconUri,
        _ => InactiveContactIconUri
    };

    // Markery tohto indikátora
    private List<IndicatorMarkerViewModel>? _markers;
    public IReadOnlyList<IndicatorMarkerViewModel> Markers
    {
        get
        {
            if (_markers == null)
            {
                _markers = _indicator.Markers
                    .Select(m => new IndicatorMarkerViewModel(m, this))
                    .ToList();
            }
            return _markers;
        }
    }

    public BlockIndicatorViewModel(BlockIndicator indicator, int blocklengthMm, double canvasWidth = 420)
    {
        _indicator = indicator;
        _blocklengthMm = blocklengthMm;
        _canvasWidth = canvasWidth;
        
        StartCm = indicator.StartCm;
        EndCm = indicator.EndCm;
        PortAddress = indicator.PortAddress;
        IsSelected = indicator.IsSelected;
    }

    partial void OnStartCmChanged(int value)
    {
        _indicator.StartCm = value;
        OnPropertyChanged(nameof(StartX));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(CenterX));
        
        // Refresh absolútnych pozícií markerov
        foreach (var marker in Markers)
        {
            marker.RefreshAbsolutePositions();
        }
    }

    partial void OnEndCmChanged(int value)
    {
        _indicator.EndCm = value;
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(CenterX));
        
        // Refresh absolútnych pozícií markerov
        foreach (var marker in Markers)
        {
            marker.RefreshAbsolutePositions();
        }
    }

    partial void OnPortAddressChanged(string value)
    {
        _indicator.PortAddress = value;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _indicator.IsSelected = value;
    }

    public void RefreshVisualState()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IconPath));
    }

    public BlockIndicator GetModel() => _indicator;

    /// <summary>
    /// Pridaj nový marker do tohto indikátora
    /// </summary>
    public IndicatorMarkerViewModel AddMarker(MarkerType type, MarkerDirection direction, int positionCm = 0)
    {
        var marker = new IndicatorMarker
        {
            Type = type,
            Direction = direction,
            PositionCm = positionCm,
            EndPositionCm = 0, // Prázdne - používateľ si hodnotu zadá sám
            SpeedValue = (type == MarkerType.Distance || type == MarkerType.Braking) ? 0 : null,
            StopPosition = null // Prázdne - zobrazí sa placeholder
        };
        
        _indicator.Markers.Add(marker);
        var markerVm = new IndicatorMarkerViewModel(marker, this);
        _markers?.Add(markerVm);
        
        OnPropertyChanged(nameof(Markers));
        return markerVm;
    }

    /// <summary>
    /// Odstráň marker z tohto indikátora
    /// </summary>
    public void RemoveMarker(Guid markerId)
    {
        var marker = _indicator.Markers.FirstOrDefault(m => m.Id == markerId);
        if (marker != null)
        {
            _indicator.Markers.Remove(marker);
            _markers = null; // Force refresh
            OnPropertyChanged(nameof(Markers));
        }
    }

    /// <summary>
    /// Refresh markerov collection
    /// </summary>
    public void RefreshMarkers()
    {
        _markers = null;
        OnPropertyChanged(nameof(Markers));
    }
}

