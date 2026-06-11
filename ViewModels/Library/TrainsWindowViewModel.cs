using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;

namespace TrackFlow.ViewModels.Library;

public partial class TrainsWindowViewModel : ObservableObject
{
    public ObservableCollection<TrainRow> Trains { get; } = new();

    [ObservableProperty] private TrainRow? vybranyVlak;

    public enum RezimVlaku
    {
        Novy,
        Prehliadanie,
        Uprava
    }

    [ObservableProperty]
    private RezimVlaku rezim = RezimVlaku.Novy;
    
    public bool JeNovyVlak => Rezim == RezimVlaku.Novy;

    public bool JeVybranyVlak => Rezim == RezimVlaku.Prehliadanie;

    public bool JeUpravaVlaku => Rezim == RezimVlaku.Uprava;
    
    [ObservableProperty]
    private bool jeEditorPovoleny;
    
    public string NadpisDetailu =>
        Rezim switch
        {
            RezimVlaku.Novy => "Nový vlak",
            RezimVlaku.Prehliadanie => "Detail vlaku",
            RezimVlaku.Uprava => "Úprava vlaku",
            _ => "Nový vlak"
        };
    public string TextDruhehoTlacidla => "Zrušiť";
    public string TextAkcnehoTlacidla =>
        Rezim == RezimVlaku.Uprava
            ? "Uložiť"
            : "Zmazať";
    
    public bool JeAkcneTlacidloPovolene =>
        Rezim == RezimVlaku.Prehliadanie ||
        Rezim == RezimVlaku.Uprava;
    
    public bool JeTlacidloZrusitPovolene =>
        Rezim != RezimVlaku.Novy;
    
    [ObservableProperty] private string cisloVlaku = "";
    partial void OnCisloVlakuChanged(string value)
    {
        AktivujRezimUpravy();
    }

    [ObservableProperty] private string nazovVlaku = "";
    
    partial void OnNazovVlakuChanged(string value)
    {
      AktivujRezimUpravy();
    }

    [ObservableProperty] private string typVlaku = "";

    [ObservableProperty] private string supravaVlaku = "";
    partial void OnSupravaVlakuChanged(string value)
    {
        AktivujRezimUpravy();
    }

    [ObservableProperty] private bool jeOsobny;
    partial void OnJeOsobnyChanged(bool value)
    {
        AktivujRezimUpravy();
    }

    [ObservableProperty] private bool jeRychlik;
    partial void OnJeRychlikChanged(bool value)
    {
        AktivujRezimUpravy();
    }

    [ObservableProperty] private bool jeNakladny;
    partial void OnJeNakladnyChanged(bool value)
    {
        AktivujRezimUpravy();
    }

    [ObservableProperty] private bool jePosun;
    partial void OnJePosunChanged(bool value)
    {
        AktivujRezimUpravy();
    }

    [ObservableProperty] private string platiOd = "";

    [ObservableProperty] private string platiDo = "";
    partial void OnPlatiDoChanged(string value)
    {
        AktivujRezimUpravy();
    }

    [ObservableProperty] private bool upravujeSaVlak;
    private bool nacitavamFormular;

 private void AktivujRezimUpravy()
 {
     if (nacitavamFormular)
         return;
 
     if (Rezim == RezimVlaku.Novy ||
         Rezim == RezimVlaku.Prehliadanie)
     {
         Rezim = RezimVlaku.Uprava;
     }
 
     OnPropertyChanged(nameof(NadpisDetailu));
     OnPropertyChanged(nameof(JeEditorPovoleny));
     OnPropertyChanged(nameof(TextAkcnehoTlacidla));
     OnPropertyChanged(nameof(JeAkcneTlacidloPovolene));
     OnPropertyChanged(nameof(JeTlacidloZrusitPovolene));
 }
    
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
        JeEditorPovoleny = false;
    }

    partial void OnVybranyVlakChanged(TrainRow? value)
    {
        if (value is null)
            return;

        NacitajVlakDoFormulara(value);

        OnPropertyChanged(nameof(NadpisDetailu));
        OnPropertyChanged(nameof(TextAkcnehoTlacidla));
        OnPropertyChanged(nameof(JeAkcneTlacidloPovolene));
        OnPropertyChanged(nameof(JeTlacidloZrusitPovolene));
        
        UpravujeSaVlak = value != null;
        
        if (value != null)
        {
            Rezim = RezimVlaku.Prehliadanie;
            JeEditorPovoleny = true;
            
            OnPropertyChanged(nameof(NadpisDetailu));
            OnPropertyChanged(nameof(JeNovyVlak));
            OnPropertyChanged(nameof(JeVybranyVlak));
            OnPropertyChanged(nameof(JeUpravaVlaku));
            OnPropertyChanged(nameof(JeEditorPovoleny));
            OnPropertyChanged(nameof(TextAkcnehoTlacidla));
            OnPropertyChanged(nameof(JeAkcneTlacidloPovolene));
            OnPropertyChanged(nameof(JeTlacidloZrusitPovolene));
        }
    }
    
    private void NacitajVlakDoFormulara(TrainRow value)
    {
        nacitavamFormular = true;
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
        nacitavamFormular = false;
     }

    public partial class TrainRow : ObservableObject
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [ObservableProperty] private string trainNumber = "";

        [ObservableProperty] private string trainName = "";

        [ObservableProperty] private string trainType = "";

        [ObservableProperty] private string trainTypeIcon = "";

        [ObservableProperty] private string trainSet = "";

        [ObservableProperty] private string validFrom = "";

        [ObservableProperty] private string validTo = "";
    }
    
    [RelayCommand]
    private void NovyVlak()
    {
        VycistitFormular();

        Rezim = RezimVlaku.Novy;
        JeEditorPovoleny = true;

        OnPropertyChanged(nameof(NadpisDetailu));
        
        OnPropertyChanged(nameof(TextAkcnehoTlacidla));
        OnPropertyChanged(nameof(JeAkcneTlacidloPovolene));
        OnPropertyChanged(nameof(JeTlacidloZrusitPovolene));
        OnPropertyChanged(nameof(JeEditorPovoleny));
    }
    
    [RelayCommand]
    public void UlozZmeny()
    {
        if (VybranyVlak is null)
        {
            var novyVlak = new TrainRow
            {
                TrainNumber = CisloVlaku,
                TrainName = NazovVlaku,
                TrainSet = SupravaVlaku,
                ValidFrom = PlatiOd,
                ValidTo = PlatiDo
            };

            if (JeOsobny)
            {
                novyVlak.TrainType = "Osobný";
                novyVlak.TrainTypeIcon = "🟢";
            }
            else if (JeRychlik)
            {
                novyVlak.TrainType = "Rýchlik";
                novyVlak.TrainTypeIcon = "🔵";
            }
            else if (JeNakladny)
            {
                novyVlak.TrainType = "Nákladný";
                novyVlak.TrainTypeIcon = "🟠";
            }
            else if (JePosun)
            {
                novyVlak.TrainType = "Posun";
                novyVlak.TrainTypeIcon = "🟣";
            }

            Trains.Add(novyVlak);

            VybranyVlak = novyVlak;

            Rezim = RezimVlaku.Prehliadanie;

            OnPropertyChanged(nameof(NadpisDetailu));
            OnPropertyChanged(nameof(TextAkcnehoTlacidla));
            OnPropertyChanged(nameof(JeAkcneTlacidloPovolene));
            OnPropertyChanged(nameof(JeTlacidloZrusitPovolene));

            return;

            return; 
        }
        
        VybranyVlak.TrainNumber = CisloVlaku;
        VybranyVlak.TrainName = NazovVlaku;
        VybranyVlak.TrainSet = SupravaVlaku;
        VybranyVlak.ValidFrom = PlatiOd;
        VybranyVlak.ValidTo = PlatiDo;

        if (JeOsobny)
        {
            VybranyVlak.TrainType = "Osobný";
            VybranyVlak.TrainTypeIcon = "🟢";
        }
        else if (JeRychlik)
        {
            VybranyVlak.TrainType = "Rýchlik";
            VybranyVlak.TrainTypeIcon = "🔵";
        }
        else if (JeNakladny)
        {
            VybranyVlak.TrainType = "Nákladný";
            VybranyVlak.TrainTypeIcon = "🟠";
        }
        else if (JePosun)
        {
            VybranyVlak.TrainType = "Posun";
            VybranyVlak.TrainTypeIcon = "🟣";
        }

        Rezim = RezimVlaku.Prehliadanie;

        OnPropertyChanged(nameof(NadpisDetailu));
        
        OnPropertyChanged(nameof(TextAkcnehoTlacidla));
        OnPropertyChanged(nameof(JeAkcneTlacidloPovolene));
        OnPropertyChanged(nameof(JeTlacidloZrusitPovolene));
      
    }
    
    private void VycistitFormular()
    {
        VybranyVlak = null;

        CisloVlaku = "";
        NazovVlaku = "";
        SupravaVlaku = "";
        PlatiOd = "";
        PlatiDo = "";

        JeOsobny = false;
        JeRychlik = false;
        JeNakladny = false;
        JePosun = false;
    }
    
    [RelayCommand]
    private void ZrusitUpravu()
    {
        if (Rezim == RezimVlaku.Uprava)
        {
            if (VybranyVlak != null)
            {
                NacitajVlakDoFormulara(VybranyVlak);
            }

            Rezim = RezimVlaku.Prehliadanie;

            OnPropertyChanged(nameof(NadpisDetailu));
            OnPropertyChanged(nameof(JeEditorPovoleny));
            OnPropertyChanged(nameof(TextAkcnehoTlacidla));
            OnPropertyChanged(nameof(JeAkcneTlacidloPovolene));
            OnPropertyChanged(nameof(JeTlacidloZrusitPovolene));

            return;
        }

        if (Rezim == RezimVlaku.Prehliadanie)
        {
            VybranyVlak = null;

            CisloVlaku = "";
            NazovVlaku = "";
            SupravaVlaku = "";
            PlatiOd = "";
            PlatiDo = "";

            JeOsobny = false;
            JeRychlik = false;
            JeNakladny = false;
            JePosun = false;

            Rezim = RezimVlaku.Novy;
            JeEditorPovoleny = false;

            OnPropertyChanged(nameof(NadpisDetailu));
            OnPropertyChanged(nameof(JeEditorPovoleny));
            OnPropertyChanged(nameof(TextAkcnehoTlacidla));
            OnPropertyChanged(nameof(JeAkcneTlacidloPovolene));
            OnPropertyChanged(nameof(JeTlacidloZrusitPovolene));
        }
    }
    
}