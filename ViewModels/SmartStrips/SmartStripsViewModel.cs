using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models;
using TrackFlow.Models.Layout;
using TrackFlow.Services;
using TrackFlow.ViewModels.Editor;

namespace TrackFlow.ViewModels.SmartStrips;

public partial class SmartStripsViewModel : ObservableObject
{
    public ObservableCollection<Locomotive> Locomotives { get; } = new();
    public ObservableCollection<Locomotive> ActiveLocomotives { get; } = new();
    public ObservableCollection<Locomotive> DepotLocomotives { get; } = new();
    public ObservableCollection<Wagon> DepotWagons { get; } = new();

    public IEnumerable<Locomotive> TopVehiclesView => Locomotives
        .OrderByDescending(l => l.HasWagons)
        .ThenBy(l => l.Name);

    private readonly SettingsManager _settings;

    // Design-time constructor (Avalonia designer instantiates VM from XAML).
    // Keep it lightweight and side-effect free.
    public SmartStripsViewModel() : this(new SettingsManager())
    {
    }

    public SmartStripsViewModel(SettingsManager settings)
    {
        _settings = settings;
        Locomotives.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TopVehiclesView));
        DepotWagons.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsDepotEmpty));

        ActiveLocomotives.CollectionChanged += (_, _) => { };

        _settings.ProjectChanged += RefreshFromProject;
        _settings.ProjectLocomotives.CollectionChanged += (_, _) => RefreshFromProject();
        _settings.ProjectWagons.CollectionChanged += (_, _) => RefreshFromProject();

        RefreshFromProject();
        ItemPressedCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(OnItemPressed);
        ItemDropCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(OnItemDrop);
    }

    /// <summary>
    /// Prepojí tento ViewModel s LayoutEditorViewModel tak, aby sa po priradení loky k bloku
    /// vizuálne označila ako "umiestnená na koľajisku".
    /// </summary>
    public void LinkLayoutEditor(LayoutEditorViewModel layoutEditor)
    {
        layoutEditor.LocomotiveAssignedToBlock += OnLocomotiveAssignedToBlock;
        layoutEditor.SmartStripsLocomotives = Locomotives;
    }

    private void OnLocomotiveAssignedToBlock(Models.Layout.BlockElement block, string locoCode, bool isForward)
    {
        // Odstráň IsPlacedOnTrack zo všetkých lôk, ktoré boli predtým v tomto bloku
        foreach (var loco in Locomotives)
        {
            if (loco.AssignedBlockId == block.Id && loco.Code != locoCode)
            {
                loco.IsPlacedOnTrack = false;
                loco.AssignedBlockId = null;
                loco.IsActive = false; // vrátiť opacity späť
            }
        }

        // Označ novú loku ako umiestnenú a okamžite aktivuj (opacity 100 aj v Editor režime)
        var target = Locomotives.FirstOrDefault(l => l.Code == locoCode);
        if (target != null)
        {
            target.IsPlacedOnTrack = true;
            target.AssignedBlockId = block.Id;
            target.IsActive = true; // okamžitá opacity 100 v Smart páse
        }
    }

    public System.Windows.Input.ICommand? ItemPressedCommand { get; private set; }
    public System.Windows.Input.ICommand? ItemDropCommand { get; private set; }

    private bool _suppressSelectionSync;

    private Locomotive? _selectedLocomotive;
    public Locomotive? SelectedLocomotive
    {
        get => _selectedLocomotive;
        set
        {
            if (_selectedLocomotive == value) return;
            _selectedLocomotive = value;
            OnPropertyChanged(nameof(SelectedLocomotive));

            if (!_suppressSelectionSync)
                OnSelectedLocomotiveChanged(value);
        }
    }

    [ObservableProperty]
    private bool isLocoSelected;

    private void OnItemPressed(object? parameter)
    {
        // parameter is the DataContext of the clicked item (Locomotive or Wagon)
        if (parameter is not Locomotive loco)
            return;

        // Toggle activation state on click
        var activeCountBefore = Locomotives.Count(l => l.IsActive);
        var wasActive = loco.IsActive;
        loco.IsActive = !wasActive;

        // Maintain ActiveLocomotives collection
        if (loco.IsActive)
        {
            if (!ActiveLocomotives.Contains(loco))
                ActiveLocomotives.Add(loco); // add to end

            // Move loco to position after existing active locos to preserve activation order
            var idx = Locomotives.IndexOf(loco);
            var targetIndex = activeCountBefore; // place after previously active items
            if (idx >= 0 && idx != targetIndex && targetIndex <= Locomotives.Count - 1)
                Locomotives.Move(idx, targetIndex);

            // select for dashboard
            _suppressSelectionSync = true;
            try { SelectedLocomotive = loco; }
            finally { _suppressSelectionSync = false; }
        }
        else
        {
            if (ActiveLocomotives.Contains(loco))
                ActiveLocomotives.Remove(loco);

            // After deactivation, move loco to just after active block (so active block remains contiguous)
            var idx = Locomotives.IndexOf(loco);
            var newActiveCount = Math.Max(0, activeCountBefore - (wasActive ? 1 : 0));
            var targetIndex = newActiveCount;
            if (idx >= 0 && idx != targetIndex && targetIndex <= Locomotives.Count - 1)
                Locomotives.Move(idx, targetIndex);

            // if deactivated and it was the selected one, clear selection
            if (ReferenceEquals(SelectedLocomotive, loco))
            {
                _suppressSelectionSync = true;
                try { SelectedLocomotive = null; }
                finally { _suppressSelectionSync = false; }
            }
        }

        OnPropertyChanged(nameof(TopVehiclesView));
    }

    private void OnSelectedLocomotiveChanged(Locomotive? loco)
    {
        if (_suppressSelectionSync)
            return;

        // Selection only controls dashboard visibility. Activation is handled by OnItemPressed.
        IsLocoSelected = loco != null;
    }

    private void OnItemDrop(object? parameter)
    {
        // Expect parameter as object[] { target, wagon }
        if (parameter is not object[] arr || arr.Length != 2)
            return;

        var target = arr[0];
        var wagon = arr[1] as Wagon;
        if (wagon == null)
            return;

        if (target is LocoRecord record)
        {
            AttachWagonToLocoRecord(record, wagon);
            return;
        }

        if (target is Locomotive loco)
        {
            AttachWagon(loco, wagon);
            return;
        }
    }

    private void RefreshFromProject()
    {
        // Ak sme zavolaní z nenadrozného vlákna (napr. SettingsManager event), presunúť prácu na UI thread.
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshFromProject());
            return;
        }
         // Synchronizovať runtime kolekciu Locomotives s projektovými záznamami z SettingsManager.
         // Poznámka: nechceme vytvárať nové runtime inštancie, ak už existujú, aby sa zachoval stav (AttachedWagons, IsActive, atď.).
         DepotWagons.Clear();
         DepotLocomotives.Clear();

         var list = _settings.ProjectLocomotives;

        // Vytvoriť množinu kľúčov, ktoré projekt stále obsahuje (pre rýchle vyhľadávanie a odstraňovanie runtime položiek)
        var projectKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recKey in list)
        {
            var key = !string.IsNullOrWhiteSpace(recKey.Id) ? recKey.Id : recKey.Address.ToString();
            projectKeys.Add(key);
       }

        // Debug output removed

        // Aktualizovať existujúce runtime inštancie alebo vytvoriť nové, ak chýbajú. Použiť robustné porovnanie
        foreach (var rec in list)
        {
            var key = !string.IsNullOrWhiteSpace(rec.Id) ? rec.Id : rec.Address.ToString();

            // Debug output removed

            // Pokus nájsť runtime loco podľa viacerých kritérií (Id, Address alebo meno)
            Locomotive? runtime = Locomotives.FirstOrDefault(l =>
                string.Equals(l.Code, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(l.Code, rec.Address.ToString(), StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(l.Name) && !string.IsNullOrWhiteSpace(rec.Name) && string.Equals(l.Name, rec.Name, StringComparison.OrdinalIgnoreCase))
            );

            if (runtime != null)
            {
                // Ak runtime.Code sa líši od kľúča, zosynchronizovať ho, aby budúce aktualizácie našli inštanciu
                if (!string.Equals(runtime.Code, key, StringComparison.OrdinalIgnoreCase))
                    runtime.Code = key;

                // Aktualizovať vlastnosti, ktoré pochádzajú zo záznamu projektu
                runtime.Name = rec.Name ?? string.Empty;
                runtime.IconName = rec.IconName ?? string.Empty;
                runtime.DccAddress = rec.Address;
                runtime.Record = rec;
                SyncFunctions(runtime, rec);
                if (string.IsNullOrWhiteSpace(runtime.TrainName))
                    runtime.TrainName = rec.Name;

                // Debug output removed
            }
            else
            {
                var r = new Locomotive(key, rec.Name ?? key) { IconName = rec.IconName ?? string.Empty, DccAddress = rec.Address, Record = rec };
                r.TrainName = rec.Name;
                SyncFunctions(r, rec);
                Locomotives.Add(r);

                // Debug output removed
            }
        }

        // Naplniť DepotWagons zo shared kolekcie ProjectWagons
        // Filtrovať vagóny, ktoré sú už priradené k lokomotívam - tie nesmú byť v depe
        var attachedWagonCodes = new HashSet<string>(
            Locomotives.SelectMany(l => l.AttachedWagons).Select(w => w.Code),
            StringComparer.OrdinalIgnoreCase);
        
        foreach (var w in _settings.ProjectWagons)
        {
            if (!attachedWagonCodes.Contains(w.Code))
                DepotWagons.Add(w);
        }

        // Odstrániť runtime locomotive inštancie, ktoré už v projekte nie sú
        var runtimeToRemove = Locomotives.Where(l => !projectKeys.Contains(l.Code)).ToList();
        var persistedChanges = false;
        foreach (var r in runtimeToRemove)
        {
            // presunúť pripojené vagóny do depa a do projektovej kolekcie, ak tam ešte nie sú
            foreach (var w in r.AttachedWagons.ToList())
            {
                if (!DepotWagons.Contains(w)) DepotWagons.Add(w);
                // perzistentne pridať vagón do projektu, ak ho ešte projekt neobsahuje
                if (!_settings.ProjectWagons.Any(x => x.Code == w.Code))
                {
                    _settings.ProjectWagons.Add(w);
                    persistedChanges = true;
                }
                r.AttachedWagons.Remove(w);
            }

            // odstrániť z aktívnych ak potrebné
            if (ActiveLocomotives.Contains(r))
                ActiveLocomotives.Remove(r);

            // odstrániť z runtime kolekcie
            Locomotives.Remove(r);

            // ak bol vybraný pre dashboard, zrušiť výber
            if (ReferenceEquals(SelectedLocomotive, r))
                SelectedLocomotive = null;
        }

        // Ak sme priradili nejaké vagóny do projektovej kolekcie, notifikovať zmeny v UI.
        // Poznámka: NEVOLÁME tu _settings.SaveCatalog(...) - nechceme automaticky prepísať settings.json.
        if (persistedChanges)
        {
            _settings.NotifyProjectChanged();
        }

        OnPropertyChanged(nameof(TopVehiclesView));
        // Dodatočné notifikácie, aby ItemsControl okamžite obnovil vizuály
        OnPropertyChanged(nameof(Locomotives));
        OnPropertyChanged(nameof(DepotLocomotives));
        OnPropertyChanged(nameof(DepotWagons));
     }

    private void LoadDepotWagonsFromProject()
    {
        var wagons = _settings.CurrentProject?.Wagons ?? new List<Wagon>();
        foreach (var w in wagons)
            DepotWagons.Add(w);
    }

    public void AttachWagon(Locomotive loco, Wagon wagon)
    {
        if (loco == null || wagon == null)
            return;

        if (DepotWagons.Contains(wagon))
            DepotWagons.Remove(wagon);

        // Add to attached wagons so UI preview shows the wagon behind the loco
        if (!loco.AttachedWagons.Contains(wagon))
            loco.AttachedWagons.Add(wagon);

        OnPropertyChanged(nameof(TopVehiclesView));
    }

    public void AttachWagonToLocoRecord(LocoRecord record, Wagon wagon)
    {
        if (record == null || wagon == null)
            return;

        // Wagon attachments live on runtime Locomotive instances. Bridge LocoRecord -> Locomotive by stable key.
        var key = !string.IsNullOrWhiteSpace(record.Id) ? record.Id : record.Address.ToString();
        var loco = Locomotives.FirstOrDefault(l =>
            string.Equals(l.Code, key, System.StringComparison.OrdinalIgnoreCase));

        if (loco == null)
        {
            loco = new Locomotive(key, record.Name) { IconName = record.IconName ?? string.Empty };
            Locomotives.Add(loco);
        }

        if (loco == null)
            return;

        AttachWagon(loco, wagon);
    }

    public void DetachLastWagon(Locomotive loco)
    {
        if (loco == null)
            return;

        if (loco.AttachedWagons.Count == 0)
            return;

        var wagon = loco.AttachedWagons[^1];
        DetachWagon(loco, wagon);
    }

    public void DetachAllWagons(Locomotive loco)
    {
        if (loco == null)
            return;
        if (loco.AttachedWagons.Count == 0)
            return;

        // copy first, because DetachWagon mutates the collection
        var wagons = loco.AttachedWagons.ToArray();
        foreach (var w in wagons)
            DetachWagon(loco, w);
    }

    public bool IsDepotEmpty => DepotWagons.Count == 0;

    [RelayCommand]
    private void DetachLastWagonFrom(object? parameter)
    {
        if (parameter is not Locomotive loco)
            return;

        DetachLastWagon(loco);
    }

    [RelayCommand]
    private void DetachAllWagonsFrom(object? parameter)
    {
        if (parameter is not Locomotive loco)
            return;

        DetachAllWagons(loco);
    }

    public void DetachWagon(Locomotive loco, Wagon wagon)
    {
        if (loco == null || wagon == null)
            return;

        if (loco.AttachedWagons.Contains(wagon))
            loco.AttachedWagons.Remove(wagon);

        if (!DepotWagons.Contains(wagon))
            DepotWagons.Add(wagon);

        OnPropertyChanged(nameof(TopVehiclesView));
    }

    [RelayCommand]
    private void ReturnWagonToDepot(object? parameter)
    {
        if (parameter is not Wagon wagon)
            return;

        foreach (var loco in Locomotives)
        {
            if (!loco.AttachedWagons.Contains(wagon))
                continue;

            DetachWagon(loco, wagon);
            return;
        }
    }

    /// <summary>Synchronizuje runtime Locomotive.Functions z LocoRecord.Functions.</summary>
    private static void SyncFunctions(Locomotive loco, LocoRecord rec)
    {
        loco.Functions.Clear();
        foreach (var fn in rec.Functions)
            loco.Functions.Add(fn);
    }

    /// <summary>
    /// Synchronizuje aktívne lokomotívy podľa aktuálneho režimu aplikácie.
    /// V režime Prevádzka (Operation) sa automaticky aktivujú všetky lokomotívy umiestnené v blokoch.
    /// V režime Editor sa Dashboard skryje, ale ručne aktivované lokomotívy ostávajú aktívne.
    /// </summary>
    public void SyncActiveLocomotivesWithMode(AppMode mode)
    {
        if (mode == AppMode.Operation)
        {
            // Zozbierať všetky locoIds priradené v blokoch layoutu (AssignedLocoId je uložené v projekte)
            var assignedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var layout = _settings.CurrentProject?.Layout;
            if (layout != null)
            {
                foreach (var el in layout.Elements)
                {
                    if (el is Models.Layout.BlockElement block && !string.IsNullOrEmpty(block.AssignedLocoId))
                    {
                        assignedIds.Add(block.AssignedLocoId);
                        // Synchronizovať IsPlacedOnTrack a AssignedBlockId na runtime loco objekte
                        var loco = Locomotives.FirstOrDefault(l =>
                            string.Equals(l.Code, block.AssignedLocoId, StringComparison.OrdinalIgnoreCase));
                        if (loco != null)
                        {
                            loco.IsPlacedOnTrack = true;
                            loco.AssignedBlockId = block.Id;
                        }
                    }
                }
            }

            // Aktivovať všetky lokomotívy v blokoch — vyčistiť ActiveLocomotives a pridať len tie v blokoch
            ActiveLocomotives.Clear();
            var activeCountBefore = Locomotives.Count(l => l.IsActive);
            foreach (var loco in Locomotives.Where(l => assignedIds.Contains(l.Code)).ToList())
            {
                loco.IsActive = true;
                ActiveLocomotives.Add(loco);

                // presunúť za existujúcimi aktívnymi
                var idx = Locomotives.IndexOf(loco);
                var targetIndex = activeCountBefore;
                if (idx >= 0 && idx != targetIndex && targetIndex <= Locomotives.Count - 1)
                    Locomotives.Move(idx, targetIndex);
                activeCountBefore++;
            }
        }
        else // AppMode.Editor
        {
            // V editore: deaktivovať Dashboard (ActiveLocomotives), ale zachovať ručne
            // aktivovaný stav IsActive. Lokomotívy priradené do bloku ostávajú aktívne.

            // Najprv zistiť, ktoré loky sú priradené v blokoch
            var assignedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var layout = _settings.CurrentProject?.Layout;
            if (layout != null)
            {
                foreach (var el in layout.Elements)
                {
                    if (el is Models.Layout.BlockElement block && !string.IsNullOrEmpty(block.AssignedLocoId))
                    {
                        assignedIds.Add(block.AssignedLocoId);
                        var loco = Locomotives.FirstOrDefault(l =>
                            string.Equals(l.Code, block.AssignedLocoId, StringComparison.OrdinalIgnoreCase));
                        if (loco != null)
                        {
                            loco.IsPlacedOnTrack = true;
                            loco.AssignedBlockId = block.Id;
                            loco.IsActive = true;
                        }
                    }
                }
            }

            // Loky bez bloku odznačiť ako "na koľajisku", ale nemeniť IsActive.
            // Týmto sa zachová ručné zvýraznenie/opacita po návrate z Prevádzky do Editu.
            foreach (var loco in Locomotives.Where(l => !assignedIds.Contains(l.Code)))
            {
                loco.IsPlacedOnTrack = false;
                loco.AssignedBlockId = null;
            }

            // Dashboard skryť (ActiveLocomotives prázdne)
            ActiveLocomotives.Clear();

            _suppressSelectionSync = true;
            try { SelectedLocomotive = null; }
            finally { _suppressSelectionSync = false; }
        }

        OnPropertyChanged(nameof(TopVehiclesView));
    }
}
