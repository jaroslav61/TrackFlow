using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using TrackFlow.Models;
using Avalonia.VisualTree;
using System.ComponentModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using Avalonia.Media;
using System.Threading.Tasks;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using TrackFlow.Helpers;
using TrackFlow.Services;

namespace TrackFlow.Views.Shared;

public partial class VehicleStripItem : UserControl, INotifyPropertyChanged
  {
    public static readonly StyledProperty<string?> IconNameProperty =
        AvaloniaProperty.Register<VehicleStripItem, string?>(nameof(IconName));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<VehicleStripItem, string?>(nameof(Title));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> DropCommandProperty =
        AvaloniaProperty.Register<VehicleStripItem, System.Windows.Input.ICommand?>("DropCommand");

    // Nové príkazové vlastnosti pre akcie kontextového menu
    // Príkazové vlastnosti, ktoré boli predtým exponované ako styled properties, boli nahradené
    // bežnými auto-vlastnosťami, aby bolo možné lokálne riadiť ich stav CanExecute.

    // Príkazové vlastnosti exponované ako len-na-čítanie auto-vlastnosti. Inicializujú sa v konštruktore.
    public System.Windows.Input.ICommand DetachLastWagonCommand { get; }
    public System.Windows.Input.ICommand ClearWagonsCommand { get; }
    public System.Windows.Input.ICommand ShowPropertiesCommand { get; }

    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public static readonly StyledProperty<bool> IsLocoProperty =
    AvaloniaProperty.Register<VehicleStripItem, bool>(nameof(IsLoco), defaultValue: false);

    public bool IsLoco
    {
        get => GetValue(IsLocoProperty);
        set => SetValue(IsLocoProperty, value);
    }

    // Vlastnosť IsFlipped pre binding v XAML - číta z aktuálnej lokomotívy
    public bool IsFlipped => _currentLoco?.IsFlipped ?? false;

    private void OnFlipOrientationMenuClick(object? _, RoutedEventArgs __)
    {
        var loco = _currentLoco ??= DataContext as Locomotive;
        if (loco == null)
            return;

        loco.IsFlipped = !loco.IsFlipped;
        UpdateDisplayNameInUi();
        UpdateIconFlipTransform();
        NotifyPropertyChanged(nameof(DisplayName));
    }


    public string DisplayName => _currentLoco?.DisplayName ?? Title ?? string.Empty;

    private void OnDragOver(object? _, DragEventArgs e)
    {
        // Prísne pravidlo: povoliť iba Move pri ťahaní vagóna na lokomotívu
        if (DragDropCompat.Contains(e, WagonDataFormat) && ((e.Source as Control)?.DataContext as Locomotive ?? this.DataContext as Locomotive) != null)
        {
            e.DragEffects = DragDropEffects.Move;

            try
            {
                var leftInd = this.FindControl<Rectangle>("LeftIndicator");
                var rightInd = this.FindControl<Rectangle>("RightIndicator");
                var icon = this.FindControl<Image>("IconImage");
                if (leftInd != null) leftInd.Opacity = 0;
                if (rightInd != null) rightInd.Opacity = 0;

                if (leftInd != null && rightInd != null && icon != null)
                {
                    var posRelToLoco = e.GetPosition(icon).X;
                    bool isLeft = posRelToLoco < (icon.Bounds.Width / 2);
                    
                    // Ak je lokomotíva otočená, invertovať strany
                    if (_currentLoco?.IsFlipped ?? false)
                        isLeft = !isLeft;
                    
                    leftInd.Opacity = isLeft ? 1 : 0;
                    rightInd.Opacity = isLeft ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Program.ReportUnhandledException("VehicleStripItem.OnDragOver.Indicators", ex, isTerminating: false);
                TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Aktualizácia drag indikátorov zlyhala: {ex.Message}", DiagnosticLevel.Warning);
            }
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }

        e.Handled = true; // zabezpečiť, aby Avalonia aktualizovala kurzor podľa DragEffects
    }


    private void OnDragLeave(object? _, DragEventArgs __)
    {
        var border = this.FindControl<Border>("BorderRoot");
        if (border != null)
            border.Opacity = 1.0;
        try { this.Cursor = null; }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("VehicleStripItem.OnDragLeave.CursorReset", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Reset kurzora pri drag-leave zlyhal: {ex.Message}", DiagnosticLevel.Warning);
        }

        try
        {
            var left = this.FindControl<Rectangle>("LeftIndicator");
            var right = this.FindControl<Rectangle>("RightIndicator");
            if (left != null) left.Opacity = 0;
            if (right != null) right.Opacity = 0;
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("VehicleStripItem.OnDragLeave.IndicatorsReset", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Reset drag indikátorov zlyhal: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    // Pomocné položky pre spustenie ťahania
    private bool _isPointerDown;
    private Avalonia.Point _pressPoint;
    private bool _dragStarted;
    private Wagon? _pendingWagon;
    private Locomotive? _pendingLoco; // Pre drag lokomotív
    private Locomotive? _currentLoco;
    private SettingsManager? _settings;
    private bool _isInVisualTree;

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void NotifyPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public const string WagonDataFormat = "trackflow/wagon";


    public System.Windows.Input.ICommand? DropCommand
    {
        get => GetValue(DropCommandProperty);
        set => SetValue(DropCommandProperty, value);
    }

    public VehicleStripItem()
    {
        AvaloniaXamlLoader.Load(this);

        // Inicializovať príkazy po InitializeComponent, aby ich XAML neprepísal.
        // Detach posledného vagóna
        var detach = new RelayCommand(DetachLastWagon, () => HasWagons);
        // Clear všetkých vagónov
        var clear = new RelayCommand(ClearAttachedWagons, () => HasWagons);
        // Zobraziť vlastnosti (s parametrom) - otvoriť príslušný editor
        var showProps = new RelayCommand<object?>(param => _ = ShowPropertiesAsync(param));

        // assign to read-only auto-properties (allowed in constructor)
        DetachLastWagonCommand = detach;
        ClearWagonsCommand = clear;
        ShowPropertiesCommand = showProps;

        // notify bindings that ICommand properties are available
        NotifyPropertyChanged(nameof(DetachLastWagonCommand));
        NotifyPropertyChanged(nameof(ClearWagonsCommand));
        NotifyPropertyChanged(nameof(ShowPropertiesCommand));
        // Track DataContext property-changed so we can update opacity when Locomotive.IsActive changes
        this.DataContextChanged += OnDataContextChanged;

        // prihlásiť sa na udalosti presunu/uvoľnenia ukazovateľa v kóde (XAML handlery odstránené)
        this.PointerMoved += OnPointerMoved;
        this.PointerReleased += OnPointerReleased;
        
        // Načítať/uvoľniť nastavenia keď je control pripojený/odpojený od vizuálneho stromu
        this.AttachedToVisualTree += OnAttachedToVisualTree;
        this.DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDataContextChanged(object? _, EventArgs __)
    {
        DetachFromCurrentLoco();

        _currentLoco = DataContext as Locomotive;
        AttachToCurrentLoco();

        // keep reference to shared settings manager from MainWindow if available
        RefreshVisibleWagonsLimit();

        UpdateOpacityFromDataContext();
        UpdateIconFlipTransform();
        // notify bindings for attached wagons and overflow when DataContext changes
        NotifyPropertyChanged(nameof(AttachedWagons));
        NotifyPropertyChanged(nameof(AttachedOverflowText));
        NotifyPropertyChanged(nameof(WagonsLeft));
        NotifyPropertyChanged(nameof(WagonsRight));
        NotifyPropertyChanged(nameof(ShowOverflowIndicator));

        // ensure initial CanExecute / HasWagons state is evaluated for the new DataContext
        AttachedWagons_CollectionChanged(null, null);
    }

    private void DetachLastWagon()
    {
        if (_currentLoco == null || _currentLoco.AttachedWagons.Count == 0)
            return;

        // Ak sú vagóny naľavo (LocoPosition > 0), vizuálne posledný vagón je na indexe 0.
        // Ak sú napravo (LocoPosition == 0), posledný je na konci zoznamu.
        int idx = (_currentLoco.LocoPosition > 0)
           ? 0
          : _currentLoco.AttachedWagons.Count - 1;
        var removed = _currentLoco.AttachedWagons[idx];
        _currentLoco.AttachedWagons.RemoveAt(idx);

        // nájsť SmartStripsViewModel a vrátiť vagón do depa
        var svm = GetSmartStripsViewModel();
        if (svm != null)
            svm.DepotWagons.Add(removed);

        // Ak po odpojení už nezostali žiadne vagóny, zrušiť názov vlaku
        if (_currentLoco.AttachedWagons.Count == 0)
        {
            _currentLoco.LocoPosition = 0;
            NotifyPropertyChanged(nameof(WagonsLeft));
            NotifyPropertyChanged(nameof(WagonsRight));
            _currentLoco.TrainName = null; // Vráti sa k DisplayName založenému na Name/Code
            NotifyPropertyChanged(nameof(DisplayName));
            UpdateDisplayNameInUi();
        }

        // Neukladáme settings.json zo UI handlera; len notifikujeme zmenu pre UI.
        _settings?.NotifyProjectChanged();
    }

    private void ClearAttachedWagons()
    {
        if (_currentLoco == null)
            return;

        var svm = GetSmartStripsViewModel();
        if (svm != null)
        {
            // pridaj späť všetky vagóny do depa
            foreach (var w in _currentLoco.AttachedWagons.ToList())
                svm.DepotWagons.Add(w);
        }

        _currentLoco.AttachedWagons.Clear();

        _currentLoco.LocoPosition = 0;
        NotifyPropertyChanged(nameof(WagonsLeft));
        NotifyPropertyChanged(nameof(WagonsRight));

        // Po rozpustení súpravy vráti lokomotíve jej pôvodný názov (zobrazí sa DisplayName)
        _currentLoco.TrainName = null; // Vráti sa k DisplayName založenému na Name/Code
        NotifyPropertyChanged(nameof(DisplayName));
        UpdateDisplayNameInUi();
    }


    private void OnAttachedToVisualTree(object? _, VisualTreeAttachmentEventArgs __)
    {
        _isInVisualTree = true;
        RefreshVisibleWagonsLimit();
        AttachToCurrentLoco();
    }

    private void OnDetachedFromVisualTree(object? _, VisualTreeAttachmentEventArgs __)
    {
        _isInVisualTree = false;

        if (_settings != null)
            _settings.AppSettingsChanged -= OnAppSettingsChanged;

        DetachFromCurrentLoco();
    }

    private void AttachToCurrentLoco()
    {
        if (_currentLoco == null)
            return;

        _currentLoco.AttachedWagons.CollectionChanged -= AttachedWagons_CollectionChanged;
        _currentLoco.AttachedWagons.CollectionChanged += AttachedWagons_CollectionChanged;

        if (_currentLoco is INotifyPropertyChanged notifier)
        {
            notifier.PropertyChanged -= Locomotive_PropertyChanged;
            notifier.PropertyChanged += Locomotive_PropertyChanged;
        }
    }

    private void DetachFromCurrentLoco()
    {
        if (_currentLoco == null)
            return;

        _currentLoco.AttachedWagons.CollectionChanged -= AttachedWagons_CollectionChanged;

        if (_currentLoco is INotifyPropertyChanged notifier)
            notifier.PropertyChanged -= Locomotive_PropertyChanged;
    }
    
    private void OnAppSettingsChanged()
    {
        // Aktualizovať limit zobrazených vagónov z nastavení
        Dispatcher.UIThread.Post(RefreshVisibleWagonsLimit);
    }
    
    private void RefreshVisibleWagonsLimit()
    {
        var previousSettings = _settings;
        var owner = this.GetVisualRoot() as Window;
        var mainVm = owner?.DataContext as TrackFlow.ViewModels.MainWindowViewModel;
        _settings = mainVm?.SettingsManager;

        if (!ReferenceEquals(previousSettings, _settings) && previousSettings != null)
            previousSettings.AppSettingsChanged -= OnAppSettingsChanged;

        if (_isInVisualTree && _settings != null)
        {
            _settings.AppSettingsChanged -= OnAppSettingsChanged;
            _settings.AppSettingsChanged += OnAppSettingsChanged;
        }
        
        // Načítať limit zobrazených vagónov z nastavení
        var visibleWagons = _settings?.App?.VisibleWagonsInTrain ?? 0;
        AttachedPreviewLimit = visibleWagons > 0 ? visibleWagons : int.MaxValue;
        
        // Aktualizovať UI
        NotifyPropertyChanged(nameof(WagonsLeft));
        NotifyPropertyChanged(nameof(WagonsRight));
        NotifyPropertyChanged(nameof(ShowOverflowIndicator));
        NotifyPropertyChanged(nameof(AttachedOverflowText));
    }


    private void Locomotive_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrackFlow.Models.Locomotive.IsActive))
        {
            UpdateOpacityFromDataContext();
            return;
        }

        if (e.PropertyName == nameof(TrackFlow.Models.Locomotive.IsFlipped))
        {
            UpdateDisplayNameInUi();
            NotifyPropertyChanged(nameof(WagonsLeft));
            NotifyPropertyChanged(nameof(WagonsRight));
            NotifyPropertyChanged(nameof(IsFlipped));
            UpdateIconFlipTransform();
            return;
        }

        // Ak sa zmenila identita rušňa, aktualizovať zobrazené meno
        if (e.PropertyName == nameof(TrackFlow.Models.Locomotive.Name) || e.PropertyName == nameof(TrackFlow.Models.Locomotive.Code) || e.PropertyName == nameof(TrackFlow.Models.Locomotive.TrainName))
        {
            NotifyPropertyChanged(nameof(DisplayName));
        }
    }

    private const int AttachedPreviewLimitDefault = 3;
    public int AttachedPreviewLimit { get; set; } = AttachedPreviewLimitDefault;

    private void AttachedWagons_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        // oznámiť, že AttachedWagons sa zmenili pre binding
        NotifyPropertyChanged(nameof(AttachedWagons));
        NotifyPropertyChanged(nameof(AttachedOverflowText));
        // oznámiť rozdelené kolekcie
        NotifyPropertyChanged(nameof(WagonsLeft));
        NotifyPropertyChanged(nameof(WagonsRight));
        // oznámiť indikátor pretečenia
        NotifyPropertyChanged(nameof(ShowOverflowIndicator));
        // oznámiť, či sú vagóny prítomné
        NotifyPropertyChanged(nameof(HasWagons));
        // RelayCommand inštancie sú uložené v auto-vlastnostiach; prehoď a oznám CanExecute
        if (DetachLastWagonCommand is RelayCommand detachRc) detachRc.NotifyCanExecuteChanged();
        if (ClearWagonsCommand is RelayCommand clearRc) clearRc.NotifyCanExecuteChanged();
        if (ShowPropertiesCommand is RelayCommand<object?> propsRc) propsRc.NotifyCanExecuteChanged();

        if (e != null)
            _settings?.Dirty.MarkDirty("train-set");
    }

    private TrackFlow.ViewModels.SmartStrips.SmartStripsViewModel? GetSmartStripsViewModel()
    {
        foreach (var anc in this.GetVisualAncestors())
        {
            if (anc is Control c && c.DataContext is TrackFlow.ViewModels.SmartStrips.SmartStripsViewModel svm)
                return svm;
        }
        return null;
    }

    // Exponovať AttachedWagons lokomotívy pre XAML binding
    public System.Collections.IEnumerable AttachedWagons => (System.Collections.IEnumerable?)_currentLoco?.AttachedWagons ?? Array.Empty<Wagon>();

    public bool HasWagons => (_currentLoco?.AttachedWagons.Count ?? 0) > 0;

    // Pre UI rozdelenie: vagóny, ktoré by sa mali zobraziť na ľavej strane rušňa
    public System.Collections.IEnumerable WagonsLeft
    {
        get
        {
            if (_currentLoco == null) return Array.Empty<Wagon>();
            // left = first LocoPosition wagons
            var leftWagons = _currentLoco.AttachedWagons.Take(Math.Max(0, _currentLoco.LocoPosition)).ToList();
            
            // Ak je limit nastavený, obmedziť počet zobrazených vagónov
            if (AttachedPreviewLimit < int.MaxValue)
            {
                // Rozdeliť limit medzi ľavú a pravú stranu proporcionálne
                var totalWagons = _currentLoco.AttachedWagons.Count;
                var locoPos = Math.Max(0, _currentLoco.LocoPosition);
                
                // Ak máme viac vagónov ako limit, obmedziť
                if (totalWagons > AttachedPreviewLimit)
                {
                    // Limit pre ľavú stranu - proporcionálne k pozícii lokomotívy
                    var leftLimit = locoPos > 0 ? Math.Max(1, (int)Math.Ceiling((double)AttachedPreviewLimit * locoPos / totalWagons)) : 0;
                    leftWagons = leftWagons.TakeLast(Math.Min(leftLimit, leftWagons.Count)).ToList();
                }
            }
            
            return leftWagons;
        }
    }

    // Pre UI rozdelenie: vagóny, ktoré by sa mali zobraziť na pravej strane rušňa
    public System.Collections.IEnumerable WagonsRight
    {
        get
        {
            if (_currentLoco == null) return Array.Empty<Wagon>();
            var rightWagons = _currentLoco.AttachedWagons.Skip(Math.Max(0, _currentLoco.LocoPosition)).ToList();
            
            // Ak je limit nastavený, obmedziť počet zobrazených vagónov
            if (AttachedPreviewLimit < int.MaxValue)
            {
                var totalWagons = _currentLoco.AttachedWagons.Count;
                var locoPos = Math.Max(0, _currentLoco.LocoPosition);
                
                // Ak máme viac vagónov ako limit, obmedziť
                if (totalWagons > AttachedPreviewLimit)
                {
                    // Limit pre pravú stranu - proporcionálne
                    var leftLimit = locoPos > 0 ? Math.Max(1, (int)Math.Ceiling((double)AttachedPreviewLimit * locoPos / totalWagons)) : 0;
                    var rightLimit = Math.Max(1, AttachedPreviewLimit - leftLimit);
                    rightWagons = rightWagons.Take(Math.Min(rightLimit, rightWagons.Count)).ToList();
                }
            }
            
            return rightWagons;
        }
    }
    
    // Či sa má zobraziť indikátor pretečenia (červený krúžok s +X)
    public bool ShowOverflowIndicator => (_currentLoco?.AttachedWagons.Count ?? 0) > AttachedPreviewLimit && AttachedPreviewLimit < int.MaxValue;

    public string AttachedOverflowText => ((_currentLoco?.AttachedWagons.Count ?? 0) - AttachedPreviewLimit) > 0 ? $"+{(_currentLoco?.AttachedWagons.Count ?? 0) - AttachedPreviewLimit}" : string.Empty;

    private void OnPointerPressed(object? _, PointerPressedEventArgs e)
    {
        // Reagovať len na stlačenia ľavého tlačidla
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false)
            return;

        // Ak je DataContext typu Locomotive, vykonávať aktiváciu cez VM príkaz iba pri dvojkliku
        if (DataContext is TrackFlow.Models.Locomotive loco)
        {
            // Aktivovať len pri dvojkliku
            if (e.ClickCount == 2)
            {
                foreach (var anc in this.GetVisualAncestors())
                {
                    if (anc is Control c && c.DataContext is TrackFlow.ViewModels.SmartStrips.SmartStripsViewModel svm)
                    {
                        var vmCmd = svm.ItemPressedCommand;
                        if (vmCmd != null && vmCmd.CanExecute(loco))
                        {
                            vmCmd.Execute(loco);
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }

            // Pre jednoduchý klik: pripraviť potenciálny drag (spustí sa až po threshold pohybe)
            // Toto NEVOLÁ DoDragDrop hneď, takže opacity sa nemení.
            _isPointerDown = true;
            _pressPoint = e.GetPosition(this);
            _dragStarted = false;
            _pendingLoco = loco;
            _pendingWagon = null;
            try { e.Pointer.Capture(this); }
            catch (Exception ex)
            {
                Program.ReportUnhandledException("VehicleStripItem.OnPointerPressed.LocoPointerCapture", ex, isTerminating: false);
                TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Zachytenie pointera pre lokomotívu zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
            }
            e.Handled = true;
            return;
        }

        // Ak je DataContext typu Wagon, pripraviť sa na možné ťahanie (čakať na pohyb)
        if (DataContext is Wagon wagon)
        {
            _isPointerDown = true;
            _pressPoint = e.GetPosition(this);
            _dragStarted = false;
            _pendingWagon = wagon;
            _pendingLoco = null;
            // uchopiť ukazovateľ pre sledovanie pohybu
             try { e.Pointer.Capture(this); }
            catch (Exception ex)
            {
                Program.ReportUnhandledException("VehicleStripItem.OnPointerPressed.WagonPointerCapture", ex, isTerminating: false);
                TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Zachytenie pointera pre vagón zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
            }
            // spracované, aby sa zabránilo zmene výberu počas ťahania
            e.Handled = true;
            return;
        }
    }

    private const string LocoDataFormat = "trackflow/locomotive";

    private void OnPointerMoved(object? _, PointerEventArgs e)
    {
        _ = OnPointerMovedAsync(e);
    }

    private async Task OnPointerMovedAsync(PointerEventArgs e)
    {
        try
        {
            if (!_isPointerDown || _dragStarted)
                return;
            if (_pendingWagon == null && _pendingLoco == null)
                return;

            var pos = e.GetPosition(this);
            var dx = pos.X - _pressPoint.X;
            var dy = pos.Y - _pressPoint.Y;
            var dist2 = dx * dx + dy * dy;
            const double threshold = 9; // ~3px movement squared = 9
            if (dist2 < threshold)
                return;

            // Spustiť ťahanie
            _dragStarted = true;

            // --- Drag lokomotívy ---
            if (_pendingLoco != null)
            {
                var loco = _pendingLoco;
                _pendingLoco = null;

                // spustiť ťahanie lokomotívy

                try
                {
                    try { this.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.No); }
                    catch (Exception ex)
                    {
                        Program.ReportUnhandledException("VehicleStripItem.OnPointerMoved.LocoDrag.CursorSet", ex, isTerminating: false);
                        TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Nastavenie drag kurzora pre lokomotívu zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
                    }
                    await DragDropCompat.DoDragDropAsync(e, LocoDataFormat, loco, DragDropEffects.Move);
                }
                catch (Exception ex)
                {
                    Program.ReportUnhandledException("VehicleStripItem.OnPointerMoved.LocoDrag", ex, isTerminating: false);
                    TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Ťahanie lokomotívy zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
                }
                finally
                {
                    try { this.Cursor = null; }
                    catch (Exception ex)
                    {
                        Program.ReportUnhandledException("VehicleStripItem.OnPointerMoved.LocoDrag.CursorReset", ex, isTerminating: false);
                        TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Reset drag kurzora pre lokomotívu zlyhal: {ex.Message}", DiagnosticLevel.Warning);
                    }
                }

                _isPointerDown = false;
                _dragStarted = false;
                e.Handled = true;
                return;
            }

            // --- Drag vagóna ---
            var wagon = _pendingWagon;
            _pendingWagon = null;
            if (wagon == null)
            {
                _isPointerDown = false;
                _dragStarted = false;
                e.Handled = true;
                return;
            }

            // spustiť ťahanie vagóna

            DragDropEffects result = DragDropEffects.None;
            try
            {
                // pri ťahaní zobraziť na zdroji znak "zakázané"
                try { this.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.No); }
                catch (Exception ex)
                {
                    Program.ReportUnhandledException("VehicleStripItem.OnPointerMoved.WagonDrag.CursorSet", ex, isTerminating: false);
                    TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Nastavenie drag kurzora pre vagón zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
                }
                result = await DragDropCompat.DoDragDropAsync(e, WagonDataFormat, wagon, DragDropEffects.Move);
            }
            catch (Exception ex)
            {
                Program.ReportUnhandledException("VehicleStripItem.OnPointerMoved.WagonDrag", ex, isTerminating: false);
                TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Ťahanie vagóna zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
            }
            finally
            {
                try { this.Cursor = null; }
                catch (Exception ex)
                {
                    Program.ReportUnhandledException("VehicleStripItem.OnPointerMoved.WagonDrag.CursorReset", ex, isTerminating: false);
                    TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Reset drag kurzora pre vagón zlyhal: {ex.Message}", DiagnosticLevel.Warning);
                }
            }

            if (result == DragDropEffects.Move)
            {
                // Prejsť predkov k nájdeniu SmartStripsViewModel a odstrániť z DepotWagons, ak je stále prítomný
                foreach (var anc in this.GetVisualAncestors())
                {
                    if (anc is Control c && c.DataContext is TrackFlow.ViewModels.SmartStrips.SmartStripsViewModel svm)
                    {
                        svm.DepotWagons.Remove(wagon);
                        break;
                    }
                }
            }

            // obnoviť stav
            _isPointerDown = false;
            _dragStarted = false;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            _isPointerDown = false;
            _pendingWagon = null;
            _pendingLoco = null;
            _dragStarted = false;
            e.Handled = true;

            Program.ReportUnhandledException("VehicleStripItem.OnPointerMoved", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Spracovanie pointer move pri drag-u zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    private void OnPointerReleased(object? _, PointerReleasedEventArgs e)
    {
        _isPointerDown = false;
        _pendingWagon = null;
        _pendingLoco = null;
        _dragStarted = false;
        try { e.Pointer.Capture(null); }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("VehicleStripItem.OnPointerReleased.PointerCaptureRelease", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Uvoľnenie pointer capture zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
        }

        try { this.Cursor = null; }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("VehicleStripItem.OnPointerReleased.CursorReset", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Reset kurzora pri pointer release zlyhal: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    private void UpdateOpacityFromDataContext()
    {
        var border = this.FindControl<Border>("BorderRoot");
        if (border == null)
            return;
        var loco = DataContext as TrackFlow.Models.Locomotive;
        var icon = this.FindControl<Image>("IconImage");

        if (loco != null)
        {
            var converter = (TrackFlow.Converters.BoolToOpacityConverter?)this.FindResource("BoolToOpacity");
            double opacity = loco.IsActive ? 1.0 : 0.4;

            if (converter != null)
            {
                var val = converter.Convert(loco.IsActive, typeof(double), null, System.Globalization.CultureInfo.InvariantCulture);
                if (val is double d)
                    opacity = d;
            }

            border.Opacity = opacity; // border nasledovať stav aktivity rušňa
            if (icon != null) icon.Opacity = opacity; // ikona nasledovať stav aktivity rušňa

            // Spustiť jednoduchú animačnú sekvenciu (zmeny mierky) pri aktivácii
            if (loco.IsActive)
                _ = AnimateActivateAsync(border);
            else
                _ = AnimateDeactivateAsync(border);
        }
        else
        {
            // Nie je to lokomotíva (napr. Wagon) – vagóny by mali byť vždy plne nepriezračné
            border.Opacity = 1.0;
            if (icon != null) icon.Opacity = 1.0;
        }
    }

    private async Task AnimateActivateAsync(Border border)
    {
        // zabezpečiť ScaleTransform
        if (border.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            border.RenderTransform = st;
            border.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        // rýchly pulz: 0.96 -> 1.06 -> 1.0
        var seq = new[] { 0.96, 1.06, 1.0 };
        foreach (var s in seq)
        {
            // animate in small steps
            var startX = st.ScaleX;
            var startY = st.ScaleY;
            var steps = 6;
            for (int i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                st.ScaleX = startX + (s - startX) * t;
                st.ScaleY = startY + (s - startY) * t;
                await Task.Delay(16);
            }
        }
    }

    private async Task AnimateDeactivateAsync(Border border)
    {
        if (border.RenderTransform is not ScaleTransform st)
        {
            st = new ScaleTransform(1, 1);
            border.RenderTransform = st;
            border.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }

        // ľahké zmenšenie na 0.98 -> 1.0
        var seq = new[] { 0.98, 1.0 };
        foreach (var s in seq)
        {
            var startX = st.ScaleX;
            var startY = st.ScaleY;
            var steps = 6;
            for (int i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                st.ScaleX = startX + (s - startX) * t;
                st.ScaleY = startY + (s - startY) * t;
                await Task.Delay(16);
            }
        }
    }

    private void OnDrop(object? _, DragEventArgs e)
    {
        // Ak je viazaný DropCommand, extrahovať vagón z drag dát a preposlať ho do VM
        var cmd = GetValue(DropCommandProperty) as System.Windows.Input.ICommand;
        if (!DragDropCompat.Contains(e, WagonDataFormat))
            return;

        if (!DragDropCompat.TryGet(e, WagonDataFormat, out Wagon wagon))
            return;

        // Cieľ je ovládací prvok, ktorý prijal drop; získať jeho DataContext
        var control = e.Source as Control;
        var targetDataContext = control?.DataContext ?? this.DataContext;

        // Samodrop: ak používateľ presunie vagón na seba, zamietnuť
        if (targetDataContext is Wagon targetWagon && ReferenceEquals(targetWagon, wagon))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // Ak drop pristál na zobrazení Lokomotívy, priamo pripojiť vagón
        var target = targetDataContext as Locomotive;

        if (target is Locomotive loco)
        {
            if (loco.AttachedWagons.Count == 0)
            {
                loco.LocoPosition = 0;
                NotifyPropertyChanged(nameof(WagonsLeft));
                NotifyPropertyChanged(nameof(WagonsRight));
            }

            // Vyhnúť sa duplicitám
            if (!loco.AttachedWagons.Contains(wagon))
            {
                var icon = this.FindControl<Image>("IconImage");
                if (icon != null)
                {
                    var posRelToLoco = e.GetPosition(icon).X;
                    bool isLeft = posRelToLoco < (icon.Bounds.Width / 2);
                    
                    // Ak je lokomotíva otočená, invertovať strany
                    if (loco.IsFlipped)
                        isLeft = !isLeft;
                    
                    if (isLeft)
                    {
                        loco.AttachedWagons.Insert(0, wagon);
                        loco.LocoPosition++;
                    }
                    else
                    {
                        loco.AttachedWagons.Add(wagon);
                    }
                }
                else
                {
                    loco.AttachedWagons.Add(wagon);
                }
            }

            // Indikovať úspešný presun, aby zdroj mohol odstrániť vagón z depa
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
            try { this.Cursor = null; }
            catch (Exception ex)
            {
                Program.ReportUnhandledException("VehicleStripItem.OnDrop.CursorReset", ex, isTerminating: false);
                TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Reset kurzora po drop-e zlyhal: {ex.Message}", DiagnosticLevel.Warning);
            }

            // oznámiť UI o rozdelení kolekcií
            NotifyPropertyChanged(nameof(WagonsLeft));
            NotifyPropertyChanged(nameof(WagonsRight));

            // skryť indikátory po drope
            try
            {
                var left = this.FindControl<Rectangle>("LeftIndicator");
                var right = this.FindControl<Rectangle>("RightIndicator");
                if (left is not null) left.Opacity = 0;
                if (right is not null) right.Opacity = 0;
            }
            catch (Exception ex)
            {
                Program.ReportUnhandledException("VehicleStripItem.OnDrop.IndicatorsReset", ex, isTerminating: false);
                TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Reset drag indikátorov po drop-e zlyhal: {ex.Message}", DiagnosticLevel.Warning);
            }
            return;
        }

        // Ak je viazaný DropCommand, preposlať ho VM (zachovať predchádzajúce správanie)
        var param = new[] { (object?)target, wagon };
        if (cmd != null)
        {
            if (cmd.CanExecute(param))
                cmd.Execute(param);
            e.Handled = true;
            return;
        }

        // Fallback: no-op
    }

    private void OnRenameMenuClick(object? _, RoutedEventArgs __)
    {
        // Dáme menu čas na úplné zatvorenie ContextMenu. Použijeme ApplicationIdle, aby sa menu stihlo zavrieť.
        Dispatcher.UIThread.Post(() => _ = OpenRenameMenuAsync(), DispatcherPriority.ApplicationIdle);
    }

    private async Task OpenRenameMenuAsync()
    {
        try
        {
            var owner = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner == null || DataContext is not Locomotive loco) return;

            // Skontroluj, či už nie je otvorené okno RenameTrainWindow
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var existing = lifetime?.Windows?.OfType<Window>().FirstOrDefault(w => w.GetType().FullName == "TrackFlow.Views.Library.RenameTrainWindow");
            if (existing != null)
            {
                try { existing.Activate(); }
                catch (Exception ex)
                {
                    Program.ReportUnhandledException("VehicleStripItem.OnRenameMenuClick.ActivateExistingWindow", ex, isTerminating: false);
                    TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Aktivácia existujúceho rename okna zlyhala: {ex.Message}", DiagnosticLevel.Warning);
                }
                return;
            }

            // Vytvor nový dialóg (ak existuje typ v assembly)
            var dlgType = Type.GetType("TrackFlow.Views.Library.RenameTrainWindow, TrackFlow");
            if (dlgType == null)
                return;

            if (Activator.CreateInstance(dlgType) is not Window dlg)
                return;

            // Pred otvorením nastav pôvodný názov
            if (dlg.FindControl<TextBox>("NameTextBox") is { } ntb)
                ntb.Text = loco.DisplayName;

            var result = await dlg.ShowDialog<string?>(owner);
            if (!string.IsNullOrWhiteSpace(result))
            {
                var newName = result.Trim();
                loco.TrainName = newName;
                var pLoco = _settings?.ProjectLocomotives.FirstOrDefault(l => l.Address.ToString() == loco.Code || l.Id == loco.Code);
                if (pLoco is not null) pLoco.Name = newName;
                UpdateDisplayNameInUi();
                NotifyPropertyChanged(nameof(DisplayName));
                // Neukladáme settings.json zo UI handlera.
                _settings?.NotifyProjectChanged();
            }
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("VehicleStripItem.OpenRenameMenuAsync", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Otvorenie rename okna zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    private void UpdateDisplayNameInUi()
    {
        this.FindControl<TextBlock>("DisplayNameTextBlock")?.SetCurrentValue(TextBlock.TextProperty, DisplayName);
    }

    private async Task ShowPropertiesAsync(object? param)
    {
        try
        {
            var owner = this.GetVisualRoot() as Window;
            var mainVm = owner?.DataContext as TrackFlow.ViewModels.MainWindowViewModel;

            if ((param is Locomotive || param is Wagon) && mainVm == null)
                return;

            var resolvedMainVm = mainVm!;

            if (param is Locomotive)
            {
                var locoVm = new TrackFlow.ViewModels.Library.LocomotivesWindowViewModel(resolvedMainVm.SettingsManager, resolvedMainVm.LayoutEditor.Elements, resolvedMainVm.Dcc);
                var dlg = new TrackFlow.Views.Library.LocomotivesWindow { DataContext = locoVm };

                void OnFeedbackBlocksChanged(IReadOnlyList<TrackFlow.Models.Layout.BlockElement> _)
                    => locoVm.RefreshCalibrationIndicatorStates();

                resolvedMainVm.LayoutBlocksChangedByFeedback += OnFeedbackBlocksChanged;
                try
                {
                    if (owner != null) await dlg.ShowDialog(owner);
                    else dlg.Show();
                }
                finally
                {
                    resolvedMainVm.LayoutBlocksChangedByFeedback -= OnFeedbackBlocksChanged;
                }
                return;
            }

            if (param is Wagon)
            {
                var dlg = new TrackFlow.Views.Library.VagonsWindow
                {
                    DataContext = new TrackFlow.ViewModels.Library.VagonsWindowViewModel(resolvedMainVm.SettingsManager)
                };
                if (owner != null) await dlg.ShowDialog(owner);
                else dlg.Show();
                return;
            }
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("VehicleStripItem.ShowPropertiesAsync", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Otvorenie vlastností vozidla zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    private void UpdateIconFlipTransform()
    {
        var icon = this.FindControl<Image>("IconImage");
        if (icon == null) return;

        bool flipped = _currentLoco?.IsFlipped ?? false;
        double scaleX = flipped ? -1.0 : 1.0;

        // Nastaviť RenderTransformOrigin na stred
        icon.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        // Aplikovať ScaleTransform priamo
        icon.RenderTransform = new ScaleTransform(scaleX, 1.0);
    }

    private void NotifyAllProperties()
    {
        // Udržiavať toto miesto jediné, aby operácie odpojenia/pripojenia mohli konzistentne obnoviť UI.
        NotifyPropertyChanged(nameof(AttachedWagons));
        NotifyPropertyChanged(nameof(AttachedOverflowText));
        NotifyPropertyChanged(nameof(WagonsLeft));
        NotifyPropertyChanged(nameof(WagonsRight));
        NotifyPropertyChanged(nameof(HasWagons));
        NotifyPropertyChanged(nameof(DisplayName));
    }

    // ContextMenu handler: odpojiť prvý vagón (najbližší k lokomotíve)
    private void OnDetachFirstWagonMenuClick(object? _, Avalonia.Interactivity.RoutedEventArgs __)
    {
        var loco = _currentLoco ??= DataContext as Locomotive;
        if (loco == null) return;
        if (loco.AttachedWagons.Count == 0) return;

        // Prvý (najbližší) vagón:
        // - ak sú vagóny naľavo (LocoPosition > 0), je na LocoPosition - 1
        // - inak je na indexe 0 (vagóny sú len napravo)
        var idx = loco.LocoPosition > 0 ? loco.LocoPosition - 1 : 0;
        if (idx >= loco.AttachedWagons.Count) return;

        var wagon = loco.AttachedWagons[idx];
        DetachSpecificWagon(wagon);
    }
 
    // Click handler pre "Odpojiť tento vagón" (volané z XAML)
    public void HandleDetachThisWagonMenuClick(object? sender, RoutedEventArgs __)
    {
        try
        {
            var mi = sender as MenuItem;
            Wagon? wagon = mi?.DataContext as Wagon;

            // záložka: ContextMenu.PlacementTarget.DataContext
            if (wagon == null && mi != null)
            {
                var cm = mi.Parent as ContextMenu ?? mi.GetVisualAncestors().OfType<ContextMenu>().FirstOrDefault();
                if (cm != null && cm.PlacementTarget is Control ctrl)
                    wagon = ctrl.DataContext as Wagon;
            }

            // posledná možnosť: DataContext kontrolu
            if (wagon == null)
                wagon = DataContext as Wagon;

            if (wagon != null)
                DetachSpecificWagon(wagon);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("VehicleStripItem.HandleDetachThisWagonMenuClick", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("Súprava", $"Odpojenie konkrétneho vagóna zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    // Odpojiť konkrétny vagón zo súpravy
    private void DetachSpecificWagon(Wagon wagon)
    {
        var loco = _currentLoco;
        if (loco == null) return;

        var idx = loco.AttachedWagons.IndexOf(wagon);
        if (idx < 0) return;

        if (idx < loco.LocoPosition)
            loco.LocoPosition = Math.Max(0, loco.LocoPosition - 1);

        loco.AttachedWagons.RemoveAt(idx);

        var svm = GetSmartStripsViewModel();
        if (svm != null)
            svm.DepotWagons.Add(wagon);

        if (loco.AttachedWagons.Count == 0)
        {
            loco.LocoPosition = 0;
            loco.TrainName = null;
        }

        UpdateDisplayNameInUi();
        NotifyAllProperties();

        // Nezapisujeme settings.json priamo z UI handlera; len notifikujeme zmenu pre UI.
        _settings?.NotifyProjectChanged();
    }

}
