using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrackFlow.Models;
using TrackFlow.Services.Dcc;

namespace TrackFlow.ViewModels.Settings;

public class DccCentralEditViewModel : ObservableObject
{
    /// <summary>
    /// Hierarchický strom centrál pre TreeView: skupiny (výrobcovia) → listy (modely).
    /// Single-type prístup – žiadne type-matching problémy v šablóne.
    /// </summary>
    public ObservableCollection<DccCentralTreeNode> TreeNodes { get; } = new();

    public ObservableCollection<string> AvailablePorts { get; } = new();

    private DccCentralTreeNode? _selectedNode;

    /// <summary>Aktuálne vybraný model centrály (listový uzol).</summary>
    public DccCentralTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (!SetProperty(ref _selectedNode, value))
                return;
            OnPropertyChanged(nameof(UsesNetworkSettings));
            OnPropertyChanged(nameof(UsesSerialSettings));
            OnPropertyChanged(nameof(CanOk));
            OkCommand?.NotifyCanExecuteChanged();
        }
    }

    /// <summary>True iba ak je vybraný listový uzol (model centrály) – povoľuje OK.</summary>
    public bool CanOk => _selectedNode != null && !_selectedNode.IsGroup && _selectedNode.Type.HasValue;

    /// <summary>
    /// Riadi viditeľnosť rozbaľovacieho menu hierarchického výberu centrály.
    /// </summary>
    private bool _isTypeDropDownOpen;
    public bool IsTypeDropDownOpen
    {
        get => _isTypeDropDownOpen;
        set => SetProperty(ref _isTypeDropDownOpen, value);
    }

    /// <summary>
    /// Volané z code-behind po kliknutí na uzol v TreeView.
    /// Nastaví výber a zatvorí menu – ignoruje skupiny a neimplementované modely.
    /// </summary>
    public void SelectCentralItemFromTree(DccCentralTreeNode? node)
    {
        if (node == null || !node.IsSelectable)
            return;
        SelectedNode = node;
        IsTypeDropDownOpen = false;
    }

    private string _host = "192.168.0.111";
    public string Host
    {
        get => _host;
        set => SetProperty(ref _host, value);
    }

    private string _serialPort = string.Empty;
    public string SerialPort
    {
        get => _serialPort;
        set => SetProperty(ref _serialPort, value);
    }

    public bool UsesNetworkSettings => SelectedNode != null && SelectedNode.Type.HasValue && SelectedNode.Type != DccCentralType.NanoX_S88;
    public bool UsesSerialSettings  => SelectedNode?.Type == DccCentralType.NanoX_S88;

    private bool _isComPortDropDownOpen;
    public bool IsComPortDropDownOpen
    {
        get => _isComPortDropDownOpen;
        set
        {
            if (!SetProperty(ref _isComPortDropDownOpen, value))
                return;
            if (value)
                RefreshPorts();
        }
    }

    public static string[] StartupBehaviorOptions { get; } = new[]
    {
        "Poslať všetky funkcie lokomotív pri štarte",
        "Poslať len aktivované funkcie lokomotív pri štarte",
        "Neposielať funkcie a predpokladať predchádzajúci stav",
        "Neposielať funkcie a predpokladať vypnutý stav"
    };

    private string _selectedStartupBehavior = StartupBehaviorOptions[0];
    public string SelectedStartupBehavior
    {
        get => _selectedStartupBehavior;
        set => SetProperty(ref _selectedStartupBehavior, value);
    }

    // Per-profile flag. UI checkbox je momentálne v hlavnom Settings okne;
    // dialóg ho len prenáša, aby editácia profilu nerozbila existujúcu hodnotu.
    public bool AutoConnect { get; set; }

    public DccCentralProfile? Result { get; private set; }

    public event System.Action<bool>? CloseRequested;

    public IRelayCommand OkCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public DccCentralEditViewModel(DccCentralProfile? existing = null)
    {
        OkCommand     = new RelayCommand(Ok, () => CanOk);
        CancelCommand = new RelayCommand(Cancel);

        BuildCentralTree();
        RefreshPorts();

        if (existing != null)
        {
            // Editácia – predvyberieme existujúci typ centrály.
            _selectedNode = FindByType(existing.Type);
            Host       = existing.Host;
            SerialPort = existing.SerialPort;
            AutoConnect = existing.AutoConnect;
            var idx = (int)existing.StartupBehavior;
            _selectedStartupBehavior = idx >= 0 && idx < StartupBehaviorOptions.Length
                ? StartupBehaviorOptions[idx]
                : StartupBehaviorOptions[0];
        }
        else
        {
            // Nový záznam – čistý štít (žiadny predvolený typ centrály).
            _selectedNode = null;
            AutoConnect = false;
        }
    }

    private void Ok()
    {
        if (SelectedNode == null || SelectedNode.IsGroup || !SelectedNode.Type.HasValue)
            return;

        Result = new DccCentralProfile
        {
            Type            = SelectedNode.Type.Value,
            Host            = Host,
            Port            = DccCentralCatalog.GetDefaultPort(SelectedNode.Type.Value) ?? 21105,
            SerialPort      = SerialPort,
            BaudRate        = 19200,
            AutoConnect     = AutoConnect,
            StartupBehavior = (StartupFunctionBehavior)Array.IndexOf(StartupBehaviorOptions, SelectedStartupBehavior)
        };
        CloseRequested?.Invoke(true);
    }

    private void Cancel() => CloseRequested?.Invoke(false);

    private void BuildCentralTree()
    {
        TreeNodes.Clear();
        foreach (var g in DccCentralCatalog.GetGroups())
        {
            // Preskočíme prázdne skupiny (výrobcovia bez modelov)
            if (g.Items.Count == 0)
                continue;

            var groupNode = new DccCentralTreeNode(g.Name);
            foreach (var it in g.Items)
                groupNode.Children.Add(new DccCentralTreeNode(it.Name, it.Type, it.IsImplemented));

            TreeNodes.Add(groupNode);
        }
    }

    private void RefreshPorts()
    {
        var previous = SerialPort;
        AvailablePorts.Clear();
        try
        {
            foreach (var p in System.IO.Ports.SerialPort.GetPortNames()
                                    .OrderBy(x => x, System.StringComparer.OrdinalIgnoreCase))
                AvailablePorts.Add(p);
        }
        catch { /* test env */ }

        if (!string.IsNullOrWhiteSpace(previous) && !AvailablePorts.Contains(previous))
            AvailablePorts.Insert(0, previous);

        if (!string.IsNullOrWhiteSpace(previous))
            SerialPort = previous;
    }

    private DccCentralTreeNode? FindByType(DccCentralType type)
        => TreeNodes.SelectMany(g => g.Children).FirstOrDefault(n => n.Type == type);
}
