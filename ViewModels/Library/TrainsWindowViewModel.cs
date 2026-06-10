using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace TrackFlow.ViewModels.Library;

 public partial class TrainsWindowViewModel : ObservableObject
 {
    public ObservableCollection<TrainRow> Trains { get; } = new();
    
    [ObservableProperty]
    private TrainRow? vybranyVlak;
    
    [ObservableProperty]
    private string cisloVlaku = "";

    [ObservableProperty]
    private string nazovVlaku = "";

    [ObservableProperty]
    private string typVlaku = "";
    
    [ObservableProperty]
    private string supravaVlaku = "";
    
    [ObservableProperty]
    private bool jeOsobny;

    [ObservableProperty]
    private bool jeRychlik;

    [ObservableProperty]
    private bool jeNakladny;

    [ObservableProperty]
    private bool jePosun;

    [ObservableProperty]
    private string platiOd = "";

    [ObservableProperty]
    private string platiDo = "";

    public TrainsWindowViewModel()
    {
        Trains.Add(new TrainRow
        {
            TrainNumber = "Os 5001",
            TrainName = "Košice – Lipany",
            TrainTypeIcon = "🟢",
            TrainType = "Osobný",
            TrainSet = "S001",
            ValidFrom = "01.01.2026",
            ValidTo = "-"
        });

        Trains.Add(new TrainRow
        {
            TrainNumber = "Os 5005",
            TrainName = "Prešov – Košice",
            TrainTypeIcon = "🟢",
            TrainType = "Osobný",
            TrainSet = "S001",
            ValidFrom = "01.01.2026",
            ValidTo = "-"
        });

        Trains.Add(new TrainRow
        {
            TrainNumber = "R 601",
            TrainName = "Tatran",
            TrainTypeIcon = "🔵",
            TrainType = "Rýchlik",
            TrainSet = "S002",
            ValidFrom = "01.01.2026",
            ValidTo = "-"
        });
    }
    partial void OnVybranyVlakChanged(TrainRow? value)
    {
        if (value is null)
            return;

        CisloVlaku = value.TrainNumber;
        NazovVlaku = value.TrainName;
        TypVlaku = value.TrainType;
        SupravaVlaku = value.TrainSet;
        PlatiOd = value.ValidFrom;
        PlatiDo = value.ValidTo;
        JeOsobny = value.TrainType == "Osobný";
        JeRychlik = value.TrainType == "Rýchlik";
        JeNakladny = value.TrainType == "Nákladný";
        JePosun = value.TrainType == "Posun";
    }
    
}

public sealed class TrainRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string TrainNumber { get; set; } = "";
    public string TrainName { get; set; } = "";
    public string TrainType { get; set; } = "";
    public string TrainTypeIcon { get; set; } = "";
    public string TrainSet { get; set; } = "";
    public string ValidFrom { get; set; } = "";
    public string ValidTo { get; set; } = "";

}