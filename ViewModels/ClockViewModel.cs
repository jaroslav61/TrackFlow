using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel;
using System.Windows.Input;
using TrackFlow.Services;

namespace TrackFlow.ViewModels;

public sealed class ClockViewModel : ObservableObject, IDisposable
{
    private const double Center = 70.0;
    private const double HourHandLength = 30.0;
    private const double MinuteHandLength = 46.0;
    private const double SecondHandLength = 54.0;

    private readonly TimeService _timeService;

    public ClockViewModel(TimeService? timeService = null)
    {
        _timeService = timeService ?? TimeService.Instance;
        _timeService.PropertyChanged += OnTimeServicePropertyChanged;
        TogglePauseCommand = new RelayCommand(_timeService.TogglePause);
    }

    public string CurrentModelTimeText => _timeService.CurrentModelTimeText;
    public string SimulationSpeedFactorText => _timeService.SimulationSpeedFactorText;
    public string PauseResumeText => _timeService.PauseResumeText;
    public string StateText => _timeService.StateText;
    public bool IsPaused => _timeService.IsPaused;

    public Point CenterPoint => new(Center, Center);
    public Point HourHandEnd => GetHandEnd(HourAngle, HourHandLength);
    public Point MinuteHandEnd => GetHandEnd(MinuteAngle, MinuteHandLength);
    public Point SecondHandEnd => GetHandEnd(SecondAngle, SecondHandLength);

    private double HourAngle
    {
        get
        {
            var time = _timeService.CurrentModelTime;
            var hour = time.Hour % 12;
            var minute = time.Minute + (time.Second + time.Millisecond / 1000.0) / 60.0;
            return (hour + minute / 60.0) * 30.0;
        }
    }

    private double MinuteAngle
    {
        get
        {
            var time = _timeService.CurrentModelTime;
            var second = time.Second + time.Millisecond / 1000.0;
            return (time.Minute + second / 60.0) * 6.0;
        }
    }

    private double SecondAngle
    {
        get
        {
            var time = _timeService.CurrentModelTime;
            return (time.Second + time.Millisecond / 1000.0) * 6.0;
        }
    }

    public ICommand TogglePauseCommand { get; }

    private static Point GetHandEnd(double angleDegrees, double length)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(
            Center + Math.Sin(radians) * length,
            Center - Math.Cos(radians) * length);
    }

    private void RaiseAnalogHandProperties()
    {
        OnPropertyChanged(nameof(CenterPoint));
        OnPropertyChanged(nameof(HourHandEnd));
        OnPropertyChanged(nameof(MinuteHandEnd));
        OnPropertyChanged(nameof(SecondHandEnd));
    }

    private void OnTimeServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        void Raise()
        {
            switch (e.PropertyName)
            {
                case nameof(TimeService.CurrentModelTime):
                case nameof(TimeService.CurrentModelTimeText):
                    OnPropertyChanged(nameof(CurrentModelTimeText));
                    RaiseAnalogHandProperties();
                    break;
                case nameof(TimeService.SimulationSpeedFactor):
                case nameof(TimeService.SimulationSpeedFactorText):
                    OnPropertyChanged(nameof(SimulationSpeedFactorText));
                    break;
                case nameof(TimeService.IsPaused):
                case nameof(TimeService.IsRunning):
                case nameof(TimeService.PauseResumeText):
                case nameof(TimeService.StateText):
                    OnPropertyChanged(nameof(IsPaused));
                    OnPropertyChanged(nameof(PauseResumeText));
                    OnPropertyChanged(nameof(StateText));
                    break;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Raise();
        else
            Dispatcher.UIThread.Post(Raise);
    }

    public void Dispose()
    {
        _timeService.PropertyChanged -= OnTimeServicePropertyChanged;
    }
}

