using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using TrackFlow.Services;
using TrackFlow.ViewModels.Library;
using TrackFlow.Views.Dialogs;

namespace TrackFlow.Views.Library;

public partial class LocomotiveCalibrationWindow : Window
{
    private LocomotiveSpeedEditorViewModel? _vm;
    private bool _speedChartHooked;

    public LocomotiveCalibrationWindow()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += (_, _) => AttachVm(DataContext as LocomotiveSpeedEditorViewModel);

        Opened += (_, _) =>
        {
            if (!_speedChartHooked)
                HookSpeedChartInteractions();
        };

        AttachVm(DataContext as LocomotiveSpeedEditorViewModel);
    }

    private void AttachVm(LocomotiveSpeedEditorViewModel? vm)
    {
        _vm = vm;
        HookSpeedChartInteractions();
    }

    private void HookSpeedChartInteractions()
    {
        if (_speedChartHooked)
            return;

        var chartCanvas = this.FindControl<Canvas>("ForwardSpeedProfileChartInteractionCanvas") ?? this.FindControl<Canvas>("ForwardSpeedProfileChartCanvas");
        if (chartCanvas == null)
            return;

        chartCanvas.PointerPressed += OnSpeedChartPointerPressed;
        chartCanvas.PointerMoved += OnSpeedChartPointerMoved;
        chartCanvas.PointerReleased += OnSpeedChartPointerReleased;
        chartCanvas.PointerCaptureLost += OnSpeedChartPointerCaptureLost;
        _speedChartHooked = true;
    }

    private void OnSpeedChartPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (sender is not Canvas chartCanvas || _vm == null)
                return;

            var handled = _vm.HandleChartPointerPressed(e.GetPosition(chartCanvas));
            if (!handled)
                return;

            if (_vm.IsDraggingChartPoint)
                e.Pointer.Capture(chartCanvas);

            e.Handled = true;
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotiveCalibrationWindow.OnSpeedChartPointerPressed", ex, isTerminating: false);
            _vm?.ReportChartInteractionFailure("Klik do grafu", ex);
            e.Handled = true;
        }
    }

    private void OnSpeedChartPointerMoved(object? sender, PointerEventArgs e)
    {
        try
        {
            if (sender is not Canvas chartCanvas || _vm == null)
                return;

            if (_vm.HandleChartPointerMoved(e.GetPosition(chartCanvas)))
                e.Handled = true;
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotiveCalibrationWindow.OnSpeedChartPointerMoved", ex, isTerminating: false);
            _vm?.ReportChartInteractionFailure("Presun bodu v grafe", ex);
            e.Handled = true;
        }
    }

    private void OnSpeedChartPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (_vm == null)
                return;

            _vm.HandleChartPointerReleased();

            if (e.Pointer.Captured == sender)
                e.Pointer.Capture(null);

            e.Handled = true;
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotiveCalibrationWindow.OnSpeedChartPointerReleased", ex, isTerminating: false);
            _vm?.ReportChartInteractionFailure("Ukončenie úpravy grafu", ex);
            e.Handled = true;
        }
    }

    private void OnSpeedChartPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        try
        {
            _vm?.HandleChartPointerReleased();
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotiveCalibrationWindow.OnSpeedChartPointerCaptureLost", ex, isTerminating: false);
            _vm?.ReportChartInteractionFailure("Strata zachytenia kurzora v grafe", ex);
        }
    }

    private async void OnInitializeProfileClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_vm == null)
                return;

            var dialog = new ConfirmDialog(
                "Inicializovať profil",
                "Naozaj chcete inicializovať profil? Všetky doteraz namerané RAW dáta pre oba smery budú vymazané.");
            TooltipPreferenceService.Attach(dialog);
            await dialog.ShowDialog(this);
            if (dialog.Result != ConfirmDialog.DialogResult.Yes)
                return;

            _vm.InitializeProfiles();
        }
        catch (Exception ex)
        {
            Program.ReportUnhandledException("LocomotiveCalibrationWindow.OnInitializeProfileClick", ex, isTerminating: false);
            _vm?.ReportChartInteractionFailure("Inicializácia profilu", ex);
        }
    }
}


