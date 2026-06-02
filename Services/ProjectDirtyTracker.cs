using System;
using System.Threading;
using Serilog;

namespace TrackFlow.Services;

/// <summary>
/// Centralizovaný „dirty" tracker pre aktuálne otvorený projekt.
/// Jediné správne miesto, kde sa mení <see cref="Models.TrackFlowProject.IsDirty"/>.
/// 
/// Použitie:
///   _settings.Dirty.MarkDirty("layout");          // po akejkoľvek zmene v projekte
///   _settings.Dirty.MarkClean();                  // po úspešnom Save / pri Open / pri New
///   using (_settings.Dirty.SuspendTracking()) {   // počas načítavania (aby load nehlásil dirty)
///       LoadElements();
///   }
/// 
/// Eventy:
///   DirtyChanged – UI vrstva (MainWindowViewModel) sa naňho pripojí, aby aktualizovala
///                  titulok okna (* marker) a status-bar hint.
/// </summary>
public sealed class ProjectDirtyTracker
{
    private readonly SettingsManager _settings;
    private int _suspendDepth;

    public event Action? DirtyChanged;

    internal ProjectDirtyTracker(SettingsManager settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// True, ak je tracker dočasne pozastavený (napr. počas Load).
    /// </summary>
    public bool IsSuspended => Volatile.Read(ref _suspendDepth) > 0;

    /// <summary>
    /// Označí projekt ako zmenený (IsDirty = true) a vyvolá DirtyChanged.
    /// Bez efektu, ak nie je otvorený projekt, ak je tracker pozastavený,
    /// alebo ak už je projekt dirty (idempotentné).
    /// </summary>
    /// <param name="reason">Krátky popis pre diagnostiku (napr. "layout", "loco-edit").</param>
    public void MarkDirty(string reason = "")
    {
        if (IsSuspended) return;

        var p = _settings.CurrentProject;
        if (p == null) return;

        if (p.IsDirty) return; // idempotent

        p.IsDirty = true;

        if (!string.IsNullOrEmpty(reason))
            Log.Debug("Project marked dirty ({Reason})", reason);

        SafeRaise();
    }

    /// <summary>
    /// Označí projekt ako čistý (IsDirty = false) a vyvolá DirtyChanged.
    /// Volá sa po úspešnom Save/SaveAs alebo po New/Open/Close.
    /// </summary>
    public void MarkClean()
    {
        var p = _settings.CurrentProject;
        if (p == null)
        {
            // Po Close môže byť projekt null – stále notifikuj UI, aby zmizla * v titule.
            SafeRaise();
            return;
        }

        if (!p.IsDirty)
        {
            // Stále notifikuj – môže ísť o post-load refresh.
            SafeRaise();
            return;
        }

        p.IsDirty = false;
        SafeRaise();
    }

    /// <summary>
    /// Pozastaví tracker na dobu trvania scope. Vhodné počas Load/Migrate,
    /// kedy by inicializácia kolekcií inak vyvolala MarkDirty.
    /// Vnárateľné (depth counter).
    /// </summary>
    public IDisposable SuspendTracking()
    {
        Interlocked.Increment(ref _suspendDepth);
        return new SuspendScope(this);
    }

    private void SafeRaise()
    {
        try { DirtyChanged?.Invoke(); }
        catch (Exception ex) { Log.Warning(ex, "DirtyChanged subscriber threw"); }
    }

    private sealed class SuspendScope : IDisposable
    {
        private readonly ProjectDirtyTracker _owner;
        private bool _disposed;

        public SuspendScope(ProjectDirtyTracker owner) => _owner = owner;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Decrement(ref _owner._suspendDepth);
        }
    }
}

