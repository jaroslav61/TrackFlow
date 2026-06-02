using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using TrackFlow.Services;

namespace TrackFlow.Views.Dialogs;

public partial class ReadDecoderValuesWindow : Window
{
    private const int InterCvDelayMs = 450;
    private const int RetryDelayMs = 650;

    private static readonly IReadOnlyDictionary<int, string> CvDescriptions = new Dictionary<int, string>
    {
        [2] = "Minimálna rýchlosť",
        [6] = "Stredná rýchlosť",
        [5] = "Maximálna rýchlosť",
        [3] = "Zrýchlenie",
        [4] = "Brzdenie",
        [29] = "Konfigurácia dekodéra"
    };

    private CancellationTokenSource? _cts;
    private readonly Dictionary<int, int> _readValues = new();
    private readonly Func<int, CancellationToken, Task<int>>? _compatReadCvFunc;
    private bool _compatStartRequested;

    public bool WasCancelled { get; private set; }
    public Exception? Error { get; private set; }
    public IReadOnlyDictionary<int, int> ReadValues => new ReadOnlyDictionary<int, int>(_readValues);

    public ReadDecoderValuesWindow()
    {
        InitializeComponent();
    }

    // Kompatibilita s existujúcim volaním v projekte.
    public ReadDecoderValuesWindow(Func<int, CancellationToken, Task<int>> readCvFunc)
        : this()
    {
        _compatReadCvFunc = readCvFunc ?? throw new ArgumentNullException(nameof(readCvFunc));
        Opened += OnOpenedStartCompatReading;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public async Task StartReadingAsync(Func<int, CancellationToken, Task<int>> readCvFunc, Action<int, int> onCvReadSuccess)
    {
        if (readCvFunc == null)
            throw new ArgumentNullException(nameof(readCvFunc));
        if (onCvReadSuccess == null)
            throw new ArgumentNullException(nameof(onCvReadSuccess));

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        WasCancelled = false;
        Error = null;
        _readValues.Clear();

        var cvList = new List<int> { 2, 6, 5, 3, 4, 29 };
        int total = cvList.Count;

        try
        {
            for (int i = 0; i < total; i++)
            {
                int cv = cvList[i];
                int currentStep = i;

                if (currentStep > 0)
                    await Task.Delay(InterCvDelayMs, _cts.Token);

                Dispatcher.UIThread.Post(() =>
                {
                    var statusText = this.FindControl<TextBlock>("StatusText");
                    if (statusText != null)
                        statusText.Text = $"Čítam register {currentStep + 1} z {total}: CV{cv} ({GetCvDescription(cv)})...";
                });

                int resultValue = await ReadCvWithRetryAsync(readCvFunc, cv, total, currentStep);

                _readValues[cv] = resultValue;

                Dispatcher.UIThread.Post(() =>
                {
                    onCvReadSuccess(cv, resultValue);
                });

                Dispatcher.UIThread.Post(() =>
                {
                    var statusText = this.FindControl<TextBlock>("StatusText");
                    var statusProgress = this.FindControl<ProgressBar>("StatusProgress");
                    if (statusText != null)
                        statusText.Text = $"Načítaný register {currentStep + 1} z {total}: CV{cv} = {resultValue}";
                    if (statusProgress != null)
                        statusProgress.Value = ((double)(currentStep + 1) / total) * 100;
                });
            }
        }
        catch (OperationCanceledException)
        {
            WasCancelled = true;
            Dispatcher.UIThread.Post(() =>
            {
                var statusText = this.FindControl<TextBlock>("StatusText");
                var statusProgress = this.FindControl<ProgressBar>("StatusProgress");
                if (statusText != null)
                    statusText.Text = "Čítanie bolo zrušené používateľom.";
                if (statusProgress != null)
                    statusProgress.Value = 100;
            });
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            Error = ex;
            Dispatcher.UIThread.Post(() =>
            {
                var statusText = this.FindControl<TextBlock>("StatusText");
                if (statusText != null)
                    statusText.Text = $"Chyba: {ex.Message}";
            });
            await Task.Delay(2000);
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                this.Close();
            });
        }
    }

    private async void OnOpenedStartCompatReading(object? sender, EventArgs e)
    {
        try
        {
            if (_compatStartRequested || _compatReadCvFunc == null)
                return;

            _compatStartRequested = true;
            await StartReadingAsync(_compatReadCvFunc, (_, _) => { });
        }
        catch (Exception ex)
        {
            Error = ex;
            Program.ReportUnhandledException("ReadDecoderValuesWindow.OnOpenedStartCompatReading", ex, isTerminating: false);
            TrackFlowDoctorService.Instance.Diagnose("DCC", $"Compat štart čítania CV zlyhal: {ex.Message}", DiagnosticLevel.Warning);
            Dispatcher.UIThread.Post(Close);
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private async Task<int> ReadCvWithRetryAsync(
        Func<int, CancellationToken, Task<int>> readCvFunc,
        int cv,
        int total,
        int currentStep)
    {
        try
        {
            return await readCvFunc(cv, _cts!.Token);
        }
        catch (Exception ex) when (IsTransientNanoXResponse(ex) && _cts?.IsCancellationRequested != true)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var statusText = this.FindControl<TextBlock>("StatusText");
                if (statusText != null)
                    statusText.Text = $"Opakujem register {currentStep + 1} z {total}: CV{cv} ({GetCvDescription(cv)})...";
            });

            await Task.Delay(RetryDelayMs, _cts!.Token);
            return await readCvFunc(cv, _cts.Token);
        }
    }

    private static bool IsTransientNanoXResponse(Exception ex)
    {
        return ex is InvalidOperationException invalidOperationException
            && invalidOperationException.Message.Contains("Neočakávaná odpoveď NanoX-S88", StringComparison.Ordinal);
    }

    private static string GetCvDescription(int cv)
    {
        return CvDescriptions.TryGetValue(cv, out var description)
            ? description
            : "Register dekodéra";
    }
}
