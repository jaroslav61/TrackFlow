using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using TrackFlow.Models;
using TrackFlow.Services;
using TrackFlow.Services.Dcc;

namespace TrackFlow.ViewModels.Settings;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsManager _mgr;

    private bool _suppressAutoDefaults;
    private bool _portTouchedByUser;

    public string? CurrentProjectPath => _mgr.CurrentProjectPath;

    public string CurrentProjectName =>
        string.IsNullOrWhiteSpace(_mgr.CurrentProjectPath) ? "—" : Path.GetFileName(_mgr.CurrentProjectPath);

    public ObservableCollection<DccCentralListItem> DccCentralItems { get; } = new();

    [ObservableProperty]
    private DccCentralListItem? selectedDccCentralItem;

    [ObservableProperty]
    private bool hasProject;

    [ObservableProperty]
    private bool useProjectForDcc;

    [ObservableProperty]
    private bool useProjectForScale;

    [ObservableProperty]
    private DccCentralType dccCentralType = DccCentralType.Z21;

    [ObservableProperty]
    private string dccCentralHost = "192.168.0.111";

    [ObservableProperty]
    private int? dccCentralPort = 21105;

    [ObservableProperty]
    private bool autoConnect;

    [ObservableProperty]
    private string language = "sk-SK";

    [ObservableProperty]
    private string scale = "H0";

    [ObservableProperty]
    private string accentColor = "#1E88E5";

    [ObservableProperty]
    private string connectionTestResult = "";

    [ObservableProperty]
    private bool isTestingConnection;

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }

    public event Action<bool>? CloseRequested;

    public SettingsViewModel(SettingsManager mgr)
    {
        _mgr = mgr;

        SaveCommand = new RelayCommand(OnSave);
        CancelCommand = new RelayCommand(OnCancel);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, CanTestConnection);

        _suppressAutoDefaults = true;
        Load();
        _suppressAutoDefaults = false;
    }

    partial void OnSelectedDccCentralItemChanged(DccCentralListItem? value)
    {
        // Do enumu zapisujeme iba listy (reálne centrály). Klik na skupinu nič nemení.
        if (value == null)
            return;

        if (value.IsHeader)
        {
            // UX: klik na skupinu nech nezmení typ a nech sa ne"zasekne" zvýraznenie na skupine.
            SelectedDccCentralItem = FindItemByType(DccCentralType);
            TestConnectionCommand.NotifyCanExecuteChanged();
            return;
        }

        if (value.Type is DccCentralType t)
        {
            if (!value.IsImplemented)
            {
                TestConnectionCommand.NotifyCanExecuteChanged();
                return;
            }

            if (DccCentralType != t)
                DccCentralType = t;
        }

        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnDccCentralTypeChanged(DccCentralType value)
    {
        // Synchronizácia opačným smerom – keď sa nastaví typ (Load, ukladanie), vyberieme položku v ComboBoxe.
        SelectedDccCentralItem = FindItemByType(value);

        // Auto default port: iba keď používateľ port vedome nemenil
        if (!_suppressAutoDefaults && !_portTouchedByUser)
        {
            var defPort = DccCentralCatalog.GetDefaultPort(value);
            if (defPort.HasValue && defPort.Value is >= 1 and <= 65535)
                DccCentralPort = defPort.Value;
        }

        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnDccCentralPortChanged(int? value)
    {
        if (!_suppressAutoDefaults)
            _portTouchedByUser = true;

        TestConnectionCommand.NotifyCanExecuteChanged();
    }

    public void RefreshProjectState()
    {
        HasProject = !string.IsNullOrWhiteSpace(_mgr.CurrentProjectPath);
        OnPropertyChanged(nameof(CurrentProjectPath));
        OnPropertyChanged(nameof(CurrentProjectName));
    }

    public void Load()
    {
        _suppressAutoDefaults = true;
        _portTouchedByUser = false;

        _mgr.LoadApp();

        // Keď je nastavená cesta projektu, berieme to ako "projekt otvorený".
        RefreshProjectState();

        // Globálne
        Language = _mgr.App.Language;
        AccentColor = _mgr.App.AccentColor;

        // DCC: projekt override ak existuje, inak app default
        if (HasProject && _mgr.Project != null &&
            (_mgr.Project.DccCentralType != null ||
             _mgr.Project.DccCentralHost != null ||
             _mgr.Project.DccCentralPort != null ||
             _mgr.Project.AutoConnect != null))
        {
            UseProjectForDcc = true;
            DccCentralType = _mgr.Project.DccCentralType ?? _mgr.App.DefaultDccCentralType;
            DccCentralHost = _mgr.Project.DccCentralHost ?? _mgr.App.DefaultDccCentralHost;
            DccCentralPort = _mgr.Project.DccCentralPort ?? _mgr.App.DefaultDccCentralPort;
            AutoConnect = _mgr.Project.AutoConnect ?? _mgr.App.DefaultAutoConnect;
        }
        else
        {
            UseProjectForDcc = false;
            DccCentralType = _mgr.App.DefaultDccCentralType;
            DccCentralHost = _mgr.App.DefaultDccCentralHost;
            DccCentralPort = _mgr.App.DefaultDccCentralPort;
            AutoConnect = _mgr.App.DefaultAutoConnect;
        }

        // Port môže byť null (keď je pole vymazané). Nech to nikdy nepadá.
        DccCentralPort ??= DccCentralCatalog.GetDefaultPort(DccCentralType);

        // Mierka: projekt override ak existuje, inak app default
        if (HasProject && _mgr.Project != null && _mgr.Project.Scale != null)
        {
            UseProjectForScale = true;
            Scale = _mgr.Project.Scale ?? _mgr.App.DefaultScale;
        }
        else
        {
            UseProjectForScale = false;
            Scale = _mgr.App.DefaultScale;
        }

        BuildDccCentralList();

        ConnectionTestResult = "";
        IsTestingConnection = false;
        TestConnectionCommand.NotifyCanExecuteChanged();

        _suppressAutoDefaults = false;
    }

    private void BuildDccCentralList()
    {
        DccCentralItems.Clear();

        void Header(string name) => DccCentralItems.Add(DccCentralListItem.Header(name));
        void Item(string name, DccCentralType t, int indent, bool implemented)
            => DccCentralItems.Add(DccCentralListItem.Item(name, t, indent, implemented));

        foreach (var g in DccCentralCatalog.GetGroups())
        {
            Header(g.Name);
            foreach (var it in g.Items)
                Item(it.Name, it.Type, 1, it.IsImplemented);
        }

        // synchronizácia výberu po naplnení
        SelectedDccCentralItem = FindItemByType(DccCentralType);
    }

    private DccCentralListItem? FindItemByType(DccCentralType type)
        => DccCentralItems.FirstOrDefault(x => !x.IsHeader && x.Type == type);

    public bool Save()
    {
        // Port môže byť null (vymazané pole). Pred persistenciou ho normalizujeme.
        var portToSave =
            (DccCentralPort is >= 1 and <= 65535)
                ? DccCentralPort!.Value
                : (DccCentralCatalog.GetDefaultPort(DccCentralType) ?? 21105);

        // 1) Vždy ulož globálne UI preferencie
        _mgr.App.Language = Language;
        _mgr.App.AccentColor = AccentColor;

        // 2) DCC – buď do projektu, alebo do app default
        if (HasProject && _mgr.Project != null && UseProjectForDcc)
        {
            _mgr.Project.DccCentralType = DccCentralType;
            _mgr.Project.DccCentralHost = DccCentralHost;
            _mgr.Project.DccCentralPort = portToSave;
            _mgr.Project.AutoConnect = AutoConnect;
        }
        else
        {
            _mgr.App.DefaultDccCentralType = DccCentralType;
            _mgr.App.DefaultDccCentralHost = DccCentralHost;
            _mgr.App.DefaultDccCentralPort = portToSave;
            _mgr.App.DefaultAutoConnect = AutoConnect;

            if (HasProject && _mgr.Project != null)
            {
                _mgr.Project.DccCentralType = null;
                _mgr.Project.DccCentralHost = null;
                _mgr.Project.DccCentralPort = null;
                _mgr.Project.AutoConnect = null;
            }
        }

        // 3) Mierka – buď do projektu, alebo do app default
        if (HasProject && _mgr.Project != null && UseProjectForScale)
        {
            _mgr.Project.Scale = Scale;
        }
        else
        {
            _mgr.App.DefaultScale = Scale;

            if (HasProject && _mgr.Project != null)
                _mgr.Project.Scale = null;
        }

        // 4) Persist
        var okApp = _mgr.SaveApp();
        var okProject = true;

        if (HasProject && _mgr.Project != null)
            okProject = _mgr.SaveProject();

        return okApp && okProject;
    }

    private void OnSave()
    {
        var ok = Save();
        CloseRequested?.Invoke(ok);
    }

    private void OnCancel()
    {
        CloseRequested?.Invoke(false);
    }

    private bool CanTestConnection()
    {
        if (SelectedDccCentralItem is null || SelectedDccCentralItem.IsHeader || !SelectedDccCentralItem.IsImplemented)
            return false;

        return !IsTestingConnection &&
               !string.IsNullOrWhiteSpace(DccCentralHost) &&
               DccCentralPort is >= 1 and <= 65535;
    }

    private async Task TestConnectionAsync()
    {
        if (!CanTestConnection())
            return;

        IsTestingConnection = true;
        ConnectionTestResult = "Testujem…";
        TestConnectionCommand.NotifyCanExecuteChanged();

        var typeName = DccCentralDisplayName.Get(DccCentralType);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            // 1) DNS / IP resolve
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(DccCentralHost);
            }
            catch (Exception ex)
            {
                ConnectionTestResult = "DNS/resolve zlyhalo: " + ex.Message;
                return;
            }

            if (addresses.Length == 0)
            {
                ConnectionTestResult = "DNS/resolve: bez výsledku.";
                return;
            }

            // 2) orientačný test
            var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses[0];
            var port = DccCentralPort ?? DccCentralCatalog.GetDefaultPort(DccCentralType) ?? 21105;

            // Z21 / z21: máme UDP probe (serial) cez LAN_GET_SERIAL_NUMBER
            var isZ21Family = DccCentralType is DccCentralType.Z21 or DccCentralType.Z21Legacy;

            if (isZ21Family)
            {
                var udp = await TryZ21UdpProbeAsync(ip, port, cts.Token);
                var tcpOk = await TryTcpConnectAsync(ip, port, cts.Token);

                if (udp.Received)
                {
                    var serialText = udp.SerialNumber.HasValue ? $" S/N: {udp.SerialNumber.Value}" : "";
                    ConnectionTestResult = $"OK: {typeName} UDP odpoveď z {udp.From}:{udp.FromPort}.{serialText}";
                    return;
                }

                if (udp.Sent)
                {
                    ConnectionTestResult =
                        $"UDP odoslanie OK na {ip}:{port}, bez odpovede (timeout). " +
                        (tcpOk ? "TCP connect zároveň OK." : "TCP connect zlyhal (pri UDP-only zariadeniach je to normálne).");
                    return;
                }

                ConnectionTestResult =
                    $"UDP test zlyhal na {ip}:{port}. " +
                    (tcpOk ? "TCP connect OK." : "TCP connect zlyhal.");
                return;
            }

            // Ostatné: len základný UDP send test (bez očakávania odpovede)
            var sent = await TryUdpSendAsync(ip, port, cts.Token);
            var tcpOk2 = await TryTcpConnectAsync(ip, port, cts.Token);

            if (sent)
            {
                ConnectionTestResult =
                    $"UDP odoslanie OK na {ip}:{port}. " +
                    (tcpOk2 ? "TCP connect zároveň OK." : "TCP connect zlyhal (pri UDP-only zariadeniach je to normálne).");
                return;
            }

            ConnectionTestResult =
                $"UDP odoslanie zlyhalo na {ip}:{port}. " +
                (tcpOk2 ? "TCP connect OK." : "TCP connect zlyhal.");
        }
        catch (Exception ex)
        {
            ConnectionTestResult = "Chyba testu: " + ex.Message;
        }
        finally
        {
            IsTestingConnection = false;
            TestConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    private sealed record UdpProbeResult(bool Sent, bool Received, string From, int FromPort, uint? SerialNumber);

    private static async Task<UdpProbeResult> TryZ21UdpProbeAsync(IPAddress ip, int port, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Connect(ip, port);

            // Z21: LAN_GET_SERIAL_NUMBER
            // Request: 04 00 10 00
            var payload = new byte[] { 0x04, 0x00, 0x10, 0x00 };
            await udp.SendAsync(payload, payload.Length);

            // pokus o odpoveď s timeoutom
            var receiveTask = udp.ReceiveAsync();
            var completed = await Task.WhenAny(receiveTask, Task.Delay(800, ct));

            if (completed == receiveTask)
            {
                var r = receiveTask.Result;

                // Očakávaná odpoveď: 08 00 10 00 + 4B serial (LE)
                uint? serial = null;
                var data = r.Buffer;

                if (data is { Length: >= 8 } &&
                    data[0] == 0x08 && data[1] == 0x00 &&
                    data[2] == 0x10 && data[3] == 0x00)
                {
                    serial = (uint)(data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24));
                }

                return new UdpProbeResult(
                    Sent: true,
                    Received: true,
                    From: r.RemoteEndPoint.Address.ToString(),
                    FromPort: r.RemoteEndPoint.Port,
                    SerialNumber: serial);
            }

            return new UdpProbeResult(true, false, "", 0, null);
        }
        catch
        {
            return new UdpProbeResult(false, false, "", 0, null);
        }
    }

    private static async Task<bool> TryUdpSendAsync(IPAddress ip, int port, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Connect(ip, port);

            // GenericIpUdp: zatiaľ nemáme protokol -> pošleme 1B "ping" (nulový bajt)
            var payload = new byte[] { 0x00 };
            await udp.SendAsync(payload, payload.Length);

            ct.ThrowIfCancellationRequested();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryTcpConnectAsync(IPAddress ip, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var reg = ct.Register(() => SafeClose(client));
            await client.ConnectAsync(ip, port);
            return client.Connected;
        }
        catch
        {
            return false;
        }

        static void SafeClose(TcpClient c)
        {
            try { c.Close(); } catch { }
        }
    }
}
