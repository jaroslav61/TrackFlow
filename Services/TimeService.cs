using System;
using System.ComponentModel;
using System.Threading;
using TrackFlow.Models;

namespace TrackFlow.Services;

/// <summary>
/// Globálne modelové hodiny (Fast Clock). Jedna reálna sekunda posúva modelový čas
/// o <see cref="SimulationSpeedFactor"/> modelových sekúnd.
/// </summary>
public sealed class TimeService : INotifyPropertyChanged, IDisposable
{
    private const int TimerPeriodMs = 50;

    public static TimeService Instance { get; } = new();

    private readonly object _sync = new();
    private readonly Timer? _timer;
    private DateTime _currentModelTime;
    private DateTime _lastRealUtc;
    private double _simulationSpeedFactor = ProjectSettingsData.DefaultSimulationSpeedFactor;
    private bool _isPaused;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TimeService(DateTime? initialModelTime = null, bool autoStart = true)
    {
        _currentModelTime = initialModelTime ?? DateTime.Today.AddHours(8);
        _lastRealUtc = DateTime.UtcNow;

        if (autoStart)
            _timer = new Timer(OnTimerTick, null, TimerPeriodMs, TimerPeriodMs);
    }

    public DateTime CurrentModelTime
    {
        get
        {
            lock (_sync)
                return _currentModelTime;
        }
    }

    public string CurrentModelTimeText => CurrentModelTime.ToString("HH:mm:ss");

    public double SimulationSpeedFactor
    {
        get
        {
            lock (_sync)
                return _simulationSpeedFactor;
        }
        set
        {
            var normalized = ProjectSettingsData.NormalizeSimulationSpeedFactor(value);
            var changed = false;
            lock (_sync)
            {
                if (Math.Abs(_simulationSpeedFactor - normalized) > 0.0001)
                {
                    _simulationSpeedFactor = normalized;
                    changed = true;
                }
            }

            if (changed)
            {
                OnPropertyChanged(nameof(SimulationSpeedFactor));
                OnPropertyChanged(nameof(SimulationSpeedFactorText));
            }
        }
    }

    public string SimulationSpeedFactorText => $"{SimulationSpeedFactor:0.0}×";

    public bool IsPaused
    {
        get
        {
            lock (_sync)
                return _isPaused;
        }
    }

    public bool IsRunning => !IsPaused;
    public string PauseResumeText => IsPaused ? "Spustiť" : "Pauza";
    public string StateText => IsPaused ? "Pozastavené" : "Beží";

    public void Pause()
    {
        var changed = false;
        lock (_sync)
        {
            if (!_isPaused)
            {
                AdvanceFromRealClockLocked(DateTime.UtcNow);
                _isPaused = true;
                changed = true;
            }
        }

        if (changed)
            RaiseClockStateChanged();
    }

    public void Resume()
    {
        var changed = false;
        lock (_sync)
        {
            if (_isPaused)
            {
                _lastRealUtc = DateTime.UtcNow;
                _isPaused = false;
                changed = true;
            }
        }

        if (changed)
            RaiseClockStateChanged();
    }

    public void TogglePause()
    {
        if (IsPaused)
            Resume();
        else
            Pause();
    }

    public void SetCurrentModelTime(DateTime modelTime)
    {
        lock (_sync)
        {
            _currentModelTime = modelTime;
            _lastRealUtc = DateTime.UtcNow;
        }

        RaiseModelTimeChanged();
    }

    /// <summary>
    /// Posunie modelový čas o zadaný reálny interval a vráti modelový prírastok.
    /// Pri pauze vráti nulu a čas nemení.
    /// </summary>
    public TimeSpan AdvanceModelTime(TimeSpan realElapsed)
    {
        TimeSpan modelDelta;
        lock (_sync)
        {
            modelDelta = AdvanceModelTimeLocked(realElapsed);
        }

        if (modelDelta > TimeSpan.Zero)
            RaiseModelTimeChanged();

        return modelDelta;
    }

    public double GetModelDeltaSecondsSince(ref DateTime previousModelTime)
    {
        var current = CurrentModelTime;
        if (current <= previousModelTime)
        {
            previousModelTime = current;
            return 0.0;
        }

        var dt = (current - previousModelTime).TotalSeconds;
        previousModelTime = current;
        return dt;
    }

    private void OnTimerTick(object? state)
    {
        if (_disposed)
            return;

        TimeSpan modelDelta;
        lock (_sync)
        {
            modelDelta = AdvanceFromRealClockLocked(DateTime.UtcNow);
        }

        if (modelDelta > TimeSpan.Zero)
            RaiseModelTimeChanged();
    }

    private TimeSpan AdvanceFromRealClockLocked(DateTime utcNow)
    {
        var realElapsed = utcNow - _lastRealUtc;
        _lastRealUtc = utcNow;
        return AdvanceModelTimeLocked(realElapsed);
    }

    private TimeSpan AdvanceModelTimeLocked(TimeSpan realElapsed)
    {
        if (_isPaused || realElapsed <= TimeSpan.Zero)
            return TimeSpan.Zero;

        var factor = ProjectSettingsData.NormalizeSimulationSpeedFactor(_simulationSpeedFactor);
        var modelDeltaTicks = (long)Math.Round(realElapsed.Ticks * factor);
        if (modelDeltaTicks <= 0)
            return TimeSpan.Zero;

        var modelDelta = TimeSpan.FromTicks(modelDeltaTicks);
        _currentModelTime = _currentModelTime.Add(modelDelta);
        return modelDelta;
    }

    private void RaiseModelTimeChanged()
    {
        OnPropertyChanged(nameof(CurrentModelTime));
        OnPropertyChanged(nameof(CurrentModelTimeText));
    }

    private void RaiseClockStateChanged()
    {
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(PauseResumeText));
        OnPropertyChanged(nameof(StateText));
    }

    private void OnPropertyChanged(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
    }
}


