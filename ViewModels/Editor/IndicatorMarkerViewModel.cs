using CommunityToolkit.Mvvm.ComponentModel;
using System;
using TrackFlow.Models.Layout;

namespace TrackFlow.ViewModels.Editor;

/// <summary>
/// ViewModel pre marker v indikátore - pre zobrazenie v editore
/// </summary>
public partial class IndicatorMarkerViewModel : ObservableObject
{
    private readonly IndicatorMarker _marker;
    private readonly BlockIndicatorViewModel _parentIndicator;

    public Guid Id => _marker.Id;
    public MarkerType Type => _marker.Type;
    public MarkerDirection Direction => _marker.Direction;
    
    [ObservableProperty] private int positionCm;
    [ObservableProperty] private int endPositionCm;
    [ObservableProperty] private int? speedValue;
    [ObservableProperty] private string? stopPosition;
    [ObservableProperty] private bool isSelected;

    // Absolútna pozícia v bloku (pre vykresľovanie)
    public int AbsolutePositionCm => _parentIndicator.StartCm + PositionCm;
    public int AbsoluteEndCm => _parentIndicator.StartCm + EndPositionCm;

    // Farba podľa typu
    public string ColorHex => Type switch
    {
        MarkerType.Distance => "#0078D7", // Modrá
        MarkerType.Braking => "#36b958",  // Zelená
        MarkerType.Stop => "#E53935",     // Červená
        MarkerType.Action => "#646464",   // Šedá
        _ => "#000000"
    };

    // Path geometry podľa smeru
    public string PathData => Direction == MarkerDirection.Forward
        ? "M 3,0 L 28,0 L 34,10 L 28,20 L 3,20 A 3,3 0 0 1 0,17 L 0,3 A 3,3 0 0 1 3,0 Z"  // Šípka doprava
        : "M 6,0 L 31,0 A 3,3 0 0 1 34,3 L 34,17 A 3,3 0 0 1 31,20 L 6,20 L 0,10 Z";       // Šípka doľava

    // Label podľa typu
    public string Label => Type switch
    {
        MarkerType.Distance => "R",
        MarkerType.Braking => "B",
        MarkerType.Stop => "S",
        MarkerType.Action => "A",
        _ => "?"
    };

    public IndicatorMarkerViewModel(IndicatorMarker marker, BlockIndicatorViewModel parentIndicator)
    {
        _marker = marker;
        _parentIndicator = parentIndicator;
        
        PositionCm = marker.PositionCm;
        EndPositionCm = marker.EndPositionCm;
        SpeedValue = marker.SpeedValue;
        StopPosition = marker.StopPosition;
        IsSelected = false;
    }

    partial void OnPositionCmChanged(int value)
    {
        _marker.PositionCm = value;
        OnPropertyChanged(nameof(AbsolutePositionCm));
    }

    partial void OnEndPositionCmChanged(int value)
    {
        _marker.EndPositionCm = value;
        OnPropertyChanged(nameof(AbsoluteEndCm));
    }

    partial void OnSpeedValueChanged(int? value)
    {
        _marker.SpeedValue = value;
    }

    partial void OnStopPositionChanged(string? value)
    {
        _marker.StopPosition = value;
    }

    public IndicatorMarker GetModel() => _marker;

    /// <summary>
    /// Aktualizuj absolútne pozície po zmene indikátora
    /// </summary>
    public void RefreshAbsolutePositions()
    {
        OnPropertyChanged(nameof(AbsolutePositionCm));
        OnPropertyChanged(nameof(AbsoluteEndCm));
    }
}

