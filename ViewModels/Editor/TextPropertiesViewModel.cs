using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;
using TrackFlow.Models.Layout;

namespace TrackFlow.ViewModels.Editor;

public partial class TextPropertiesViewModel : ObservableObject
{
    [ObservableProperty] private string text = "Text";
    [ObservableProperty] private bool visibleInEditModeOnly;
    [ObservableProperty] private string selectedFont = "Segoe UI";
    [ObservableProperty] private double fontSize = 12;
    [ObservableProperty] private bool isBold;
    [ObservableProperty] private bool isItalic;
    [ObservableProperty] private bool isUnderline;
    [ObservableProperty] private bool isStrikethrough;
    
    [ObservableProperty] private string backgroundColor = "#FFFFFF"; // Predvolená biela (pozadie markeru)
    [ObservableProperty] private bool isAutoBackground;
    
    [ObservableProperty] private string fillColor = "#000000"; // Predvolená čierna (farba textu)
    [ObservableProperty] private bool isAutoFill;
    
    [ObservableProperty] private string frameColor = "#FFFFFF"; // Predvolená biela (oramovanie markeru)
    [ObservableProperty] private bool isAutoFrame;
    
    [ObservableProperty] private double frameThickness = 0;
    
    // Automatické nastavenie farby a hrúbky rámčeka pri zmene auto režimu
    partial void OnIsAutoFrameChanged(bool value)
    {
        if (value)
        {
            // Pri zapnutí automatic režimu nastav predvolenú bielu farbu
            FrameColor = "#FFFFFF";
        }
        else
        {
            // Ak používateľ vypol automatický režim a hrúbka je 0, nastav na 1
            if (FrameThickness == 0)
            {
                FrameThickness = 1;
            }
        }
    }
    
    // Automatické nastavenie farby pozadia pri zmene auto režimu
    partial void OnIsAutoBackgroundChanged(bool value)
    {
        if (value)
        {
            // Pri zapnutí automatic režimu nastav predvolenú bielu farbu
            BackgroundColor = "#FFFFFF";
        }
    }
    [ObservableProperty] private string horizontalAlignment = "Stred";
    [ObservableProperty] private string verticalAlignment = "Stred";
    [ObservableProperty] private int widthInCells = 1;
    [ObservableProperty] private int heightInCells = 1;

    public ICommand PickFontCommand { get; }
    public ICommand PickBackgroundColorCommand { get; }
    public ICommand PickFrameColorCommand { get; }
    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    public TextPropertiesViewModel()
    {
        PickFontCommand = new RelayCommand(PickFont);
        PickBackgroundColorCommand = new RelayCommand(PickBackgroundColor);
        PickFrameColorCommand = new RelayCommand(PickFrameColor);
        OkCommand = new RelayCommand(Ok);
        CancelCommand = new RelayCommand(Cancel);
    }

    public ObservableCollection<string> HAlignChoices { get; } = new() { "Vľavo", "Stred", "Vpravo" };
    public ObservableCollection<string> VAlignChoices { get; } = new() { "Hore", "Stred", "Dole" };

    public bool DialogResult { get; private set; }

    public void LoadFromElement(TextElement element)
    {
        Text = element.Text;
        VisibleInEditModeOnly = element.VisibleInEditModeOnly;
        SelectedFont = element.FontName;
        FontSize = element.FontSize;
        
        // Spracovanie farby pozadia markeru
        if (string.IsNullOrEmpty(element.BackgroundColor) || element.BackgroundColor == "Transparent")
        {
            IsAutoBackground = true;
            BackgroundColor = "#FFFFFF"; // Predvolená biela
        }
        else
        {
            IsAutoBackground = false;
            BackgroundColor = element.BackgroundColor;
        }
        
        // Spracovanie farby textu (nepoužíva sa v dialógu, ale načítame ju pre úplnosť)
        if (element.FillColor == "Automatic")
        {
            IsAutoFill = true;
            FillColor = "#000000"; // Predvolená čierna
        }
        else
        {
            IsAutoFill = false;
            FillColor = string.IsNullOrEmpty(element.FillColor) ? "#000000" : element.FillColor;
        }
        
        // Spracovanie farby rámčeka
        if (element.FrameColor == "Automatic" || string.IsNullOrEmpty(element.FrameColor))
        {
            IsAutoFrame = true;
            FrameColor = "#FFFFFF"; // Predvolená biela
        }
        else
        {
            IsAutoFrame = false;
            FrameColor = element.FrameColor;
        }
        
        FrameThickness = element.FrameThickness;
        HorizontalAlignment = MapAlignment(element.HorizontalAlignment, true);
        VerticalAlignment = MapAlignment(element.VerticalAlignment, false);
        WidthInCells = element.WidthInCells;
        HeightInCells = element.HeightInCells;
    }

    public void SaveToElement(TextElement element)
    {
        element.Text = Text;
        element.VisibleInEditModeOnly = VisibleInEditModeOnly;
        
        // Uloženie farby pozadia markeru
        element.BackgroundColor = IsAutoBackground ? string.Empty : BackgroundColor;
            
        element.FontName = SelectedFont;
        element.FontSize = FontSize;
        
        // Uloženie farby rámčeka
        element.FrameColor = IsAutoFrame ? "Automatic" : FrameColor;
        
        element.FrameThickness = FrameThickness;
        element.HorizontalAlignment = MapAlignmentToEnglish(HorizontalAlignment, true);
        element.VerticalAlignment = MapAlignmentToEnglish(VerticalAlignment, false);
        element.WidthInCells = WidthInCells;
        element.HeightInCells = HeightInCells;
    }

    private string MapAlignment(string value, bool isHorizontal)
    {
        if (isHorizontal)
        {
            return value switch
            {
                "Left" => "Vľavo",
                "Center" => "Stred",
                "Right" => "Vpravo",
                _ => "Stred"
            };
        }
        else
        {
            return value switch
            {
                "Top" => "Hore",
                "Center" => "Stred",
                "Bottom" => "Dole",
                _ => "Stred"
            };
        }
    }

    private string MapAlignmentToEnglish(string value, bool isHorizontal)
    {
        if (isHorizontal)
        {
            return value switch
            {
                "Vľavo" => "Left",
                "Stred" => "Center",
                "Vpravo" => "Right",
                _ => "Center"
            };
        }
        else
        {
            return value switch
            {
                "Hore" => "Top",
                "Stred" => "Center",
                "Dole" => "Bottom",
                _ => "Center"
            };
        }
    }

    private void PickFont()
    {
        ShowFontPicker?.Invoke();
    }

    private void PickBackgroundColor()
    {
        var currentColor = BackgroundColor.StartsWith("#") ? BackgroundColor : "#FFFFFF";
        var pickedColor = ShowBackgroundColorPicker?.Invoke(currentColor);
        
        if (pickedColor != null)
        {
            BackgroundColor = pickedColor;
            IsAutoBackground = false; // Vypni auto režim pri manuálnom výbere
        }
    }

    private void PickFrameColor()
    {
        var currentColor = FrameColor == "Transparent" || !FrameColor.StartsWith("#") ? "#FFFFFF" : FrameColor;
        var pickedColor = ShowFrameColorPicker?.Invoke(currentColor);
        
        if (pickedColor != null)
        {
            FrameColor = pickedColor;
            IsAutoFrame = false; // Vypni auto režim pri manuálnom výbere
            
            // Automaticky nastav hrúbku rámčeka, ak je 0
            if (FrameThickness == 0)
            {
                FrameThickness = 1;
            }
        }
    }

    private void Ok()
    {
        DialogResult = true;
        RequestClose?.Invoke();
    }

    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    public System.Action? RequestClose { get; set; }
    public System.Action? ShowFontPicker { get; set; }
    public System.Func<string, string?>? ShowBackgroundColorPicker { get; set; }
    public System.Func<string, string?>? ShowFrameColorPicker { get; set; }
}

