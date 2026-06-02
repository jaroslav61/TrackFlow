using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TrackFlow.Services;

namespace TrackFlow.Views;

public partial class DoctorWindow : Window
{
    private static readonly IReadOnlyDictionary<string, string[]> FilterableMultiTagAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["WAIT"] = new[] { "WAIT", "CAKANIE" },
        ["ARBITER"] = new[] { "ARBITER", "ARBITRAZ" },
        ["DEADLOCK"] = new[] { "DEADLOCK", "PAT" },
        ["BLOCK"] = new[] { "BLOCK", "BLOK" },
        ["TURNOUT"] = new[] { "TURNOUT", "VYHYBKA" },
        ["SIGNAL"] = new[] { "SIGNAL", "NAVESTIDLO" }
    };

    private readonly TrackFlowDoctorService _doctorService = TrackFlowDoctorService.Instance;
    private readonly HashSet<string> _activeFilters = new(StringComparer.OrdinalIgnoreCase);
    private bool _autoScrollEnabled = true;

    public ObservableCollection<DiagnosticEvent> FilteredEvents { get; } = new();

    public DoctorWindow()
    {
        InitializeComponent();
        DataContext = this;

        SaveLogButton.Click += OnSaveLogClick;
        SaveFilteredLogButton.Click += OnSaveFilteredLogClick;
        CopyFilteredLogButton.Click += OnCopyFilteredLogClick;
        CopyFullLogButton.Click += OnCopyFullLogClick;
        InsertMarkerButton.Click += OnInsertMarkerClick;
        ClearLogButton.Click += OnClearClick;
        AutoScrollCheckBox.Click += OnAutoScrollChanged;
        foreach (var filterCheckBox in FiltersPanel.Children.OfType<CheckBox>())
            filterCheckBox.Click += OnFilterChanged;

        _doctorService.Events.CollectionChanged += OnDoctorEventsChanged;
        Closed += OnWindowClosed;

        RefreshFilteredEvents();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _doctorService.Events.CollectionChanged -= OnDoctorEventsChanged;
        Closed -= OnWindowClosed;
    }

    private void OnDoctorEventsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshFilteredEvents();

        if (_autoScrollEnabled)
            _ = ScrollToLatestAsync();
    }

    private void RefreshFilteredEvents()
    {
        var snapshot = _doctorService.GetEventsChronologicalSnapshot();
        var filtered = snapshot.Where(MatchesFilters).ToList();

        FilteredEvents.Clear();
        foreach (var entry in filtered)
            FilteredEvents.Add(entry);

        UpdateEventCounter(filtered.Count, snapshot.Count);
    }

    private void UpdateEventCounter(int displayedCount, int totalCount)
    {
        if (EventCounterTextBlock != null)
            EventCounterTextBlock.Text = $"Udalosti: {displayedCount} / {totalCount}";
    }

    private bool MatchesFilters(DiagnosticEvent entry)
    {
        return ShouldDisplayEvent(entry, _activeFilters);
    }

    private static bool ShouldDisplayEvent(DiagnosticEvent entry, IReadOnlySet<string> activeFilters)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(activeFilters);

        if (!TrackFlowDoctorFormatter.ShouldDisplayInDoctorWindow(entry))
            return false;

        if (activeFilters.Count == 0)
            return true;

        if (!TryGetFilterableMultiTag(entry.Message, out var multiTag))
            return true;

        return activeFilters.Contains(multiTag);
    }

    private static bool TryGetFilterableMultiTag(string? message, out string multiTag)
    {
        multiTag = string.Empty;

        if (string.IsNullOrWhiteSpace(message) || !message.Contains("[MULTI][", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var filterableTag in FilterableMultiTagAliases)
        {
            foreach (var alias in filterableTag.Value)
            {
                if (!message.Contains($"[MULTI][{alias}]", StringComparison.OrdinalIgnoreCase))
                    continue;

                multiTag = filterableTag.Key;
                return true;
            }
        }

        return false;
    }

    private async Task ScrollToLatestAsync()
    {
        if (DoctorGrid == null || FilteredEvents.Count == 0)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => DoctorGrid.ScrollIntoView(FilteredEvents[^1], null), DispatcherPriority.Background);
    }

    private async void OnSaveLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = await PickLogSavePathAsync("Uložiť diagnostický log…");
            if (string.IsNullOrWhiteSpace(path))
                return;

            await File.WriteAllTextAsync(path, _doctorService.ExportCurrentLogText(), Encoding.UTF8);
            _doctorService.Diagnose("Doktor", $"Celý log bol uložený do súboru: [{path}]", DiagnosticLevel.Success);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("DoctorWindow.OnSaveLogClick", ex, isTerminating: false);
            _doctorService.Diagnose("Doktor", $"Uloženie celého logu zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    private async void OnSaveFilteredLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = await PickLogSavePathAsync("Uložiť filtrovaný diagnostický výpis…");
            if (string.IsNullOrWhiteSpace(path))
                return;

            await File.WriteAllTextAsync(path, ExportFilteredEventsText(), Encoding.UTF8);
            _doctorService.Diagnose("Doktor", $"Filtrovaný výpis bol uložený do súboru: [{path}]", DiagnosticLevel.Success);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("DoctorWindow.OnSaveFilteredLogClick", ex, isTerminating: false);
            _doctorService.Diagnose("Doktor", $"Uloženie filtrovaného výpisu zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    private async void OnCopyFilteredLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null)
                return;

            await topLevel.Clipboard.SetTextAsync(ExportFilteredEventsText());
            _doctorService.Diagnose("Doktor", "Filtrovaný výpis bol skopírovaný do schránky.", DiagnosticLevel.Success);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("DoctorWindow.OnCopyFilteredLogClick", ex, isTerminating: false);
            _doctorService.Diagnose("Doktor", $"Kopírovanie filtrovaného výpisu zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    private async void OnCopyFullLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null)
                return;

            await topLevel.Clipboard.SetTextAsync(_doctorService.ExportCurrentLogText());
            _doctorService.Diagnose("Doktor", "Celý log bol skopírovaný do schránky.", DiagnosticLevel.Success);
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("DoctorWindow.OnCopyFullLogClick", ex, isTerminating: false);
            _doctorService.Diagnose("Doktor", $"Kopírovanie celého logu zlyhalo: {ex.Message}", DiagnosticLevel.Warning);
        }
    }

    private async Task<string?> PickLogSavePathAsync(string title)
    {
        var storageProvider = StorageProvider;
        var suggestedName = $"doctor-log-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.log";
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("Diagnostický log") { Patterns = new[] { "*.log", "*.txt" } },
                FilePickerFileTypes.All
            }
        });

        return file?.TryGetLocalPath();
    }

    private string ExportFilteredEventsText()
        => _doctorService.ExportEventsText(FilteredEvents.ToList());

    private void OnInsertMarkerClick(object? sender, RoutedEventArgs e)
    {
        _doctorService.InsertMarker();
    }

    private void OnAutoScrollChanged(object? sender, RoutedEventArgs e)
    {
        _autoScrollEnabled = AutoScrollCheckBox?.IsChecked ?? false;
        if (_autoScrollEnabled)
            _ = ScrollToLatestAsync();
    }

    private void OnFilterChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkBox)
            return;

        var tag = checkBox.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(tag))
            return;

        if (checkBox.IsChecked == true)
            _activeFilters.Add(tag);
        else
            _activeFilters.Remove(tag);

        RefreshFilteredEvents();

        if (_autoScrollEnabled)
            _ = ScrollToLatestAsync();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _doctorService.Events.Clear();
    }
}