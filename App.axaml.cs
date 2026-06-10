using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.IO;
using Serilog;
using TrackFlow.ViewModels;
using TrackFlow.Views;
using TrackFlow.Services;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Win32;
using System.Reflection;
using System.Threading;

namespace TrackFlow;

public partial class App : Application
{
    private bool _globalExceptionHandlersRegistered;
    private int _desktopExitCleanupStarted;

    // Referencia pre núdzové uvoľnenie COM portov pri pádoch a ProcessExit
    private static MainWindowViewModel? _emergencyCleanupVm;

    private static void PersistAppSettingsBestEffort(string reason)
    {
        try
        {
            var vm = _emergencyCleanupVm;
            if (vm == null)
                return;

            var ok = vm.SettingsManager.SaveApp();
            if (!ok)
                Log.Warning("Failed to persist app settings during {Reason}.", reason);
        }
        catch (Exception ex)
        {
            try
            {
                Log.Warning(ex, "Error while persisting app settings during {Reason}.", reason);
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static void BestEffortShutdownWinFormsInfrastructure()
    {
        // TrackFlow uses some WinForms dialogs (FontDialog/ColorDialog).
        // On Windows, WinForms may spin up a foreground SystemEvents thread which can
        // keep the process alive after all Avalonia windows are closed (IDE still shows RUN/STOP).
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            // Stop the SystemEvents thread if it was started.
            // On some framework versions there is no public API, so we fall back to reflection.
            var t = typeof(SystemEvents);
            var shutdown = t.GetMethod(
                               "Shutdown",
                               BindingFlags.NonPublic | BindingFlags.Static,
                               binder: null,
                               types: Type.EmptyTypes,
                               modifiers: null)
                           ?? t.GetMethod(
                               "Dispose",
                               BindingFlags.NonPublic | BindingFlags.Static,
                               binder: null,
                               types: Type.EmptyTypes,
                               modifiers: null);
            shutdown?.Invoke(null, null);
        }
        catch
        {
            // best-effort
        }

        try
        {
            // Best-effort: tear down any WinForms message loop remnants.
            // IMPORTANT: Do not call desktop.Shutdown() from Exit handler cleanup,
            // it can recursively re-enter Exit and keep shutdown spinning.
            System.Windows.Forms.Application.Exit();
            System.Windows.Forms.Application.ExitThread();
        }
        catch
        {
            // best-effort
        }
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Ak bežíme v dizajnéri, stačí zavolať base a nepokračovať v inicializácii okien a logiky
        if (Design.IsDesignMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        Log.Information("Framework init completed. Lifetime: {LifetimeType}", ApplicationLifetime?.GetType().FullName ?? "null");
        RegisterGlobalExceptionHandlers();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Critical for correct shutdown in debug/release:
            // DoctorWindow (and other auxiliary windows) must not keep the process alive
            // after MainWindow is closed.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            try
            {
                // Register available loco icons so converters can resolve by name
                var baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
                var iconsDir = Path.Combine(baseDir, "Assets", "LocoIcons");
                if (!Directory.Exists(iconsDir))
                {
                    // try a project-relative fallback
                    iconsDir = Path.Combine(baseDir, "..", "..", "Assets", "LocoIcons");
                }

                if (Directory.Exists(iconsDir))
                {
                    foreach (var f in Directory.GetFiles(iconsDir, "*.png"))
                    {
                        var name = Path.GetFileName(f);
                        IconRegistry.Register(name, Path.GetFullPath(f));
                    }
                }

                // Register wagon icons too
                var wagonIconsDir = Path.Combine(baseDir, "Assets", "VagonIcons");
                if (!Directory.Exists(wagonIconsDir))
                {
                    wagonIconsDir = Path.Combine(baseDir, "..", "..", "Assets", "VagonIcons");
                }

                if (Directory.Exists(wagonIconsDir))
                {
                    foreach (var f in Directory.GetFiles(wagonIconsDir, "*.png"))
                    {
                        var name = Path.GetFileName(f);
                        IconRegistry.Register(name, Path.GetFullPath(f));
                    }
                }
            }
            catch (Exception)
            {
                // icon fallback errors should not block startup
            }

            try
            {
                var mw = new MainWindow
                {
                    DataContext = new MainWindowViewModel()
                };
                TooltipPreferenceService.Attach(mw);
                desktop.MainWindow = mw;
                _emergencyCleanupVm = mw.DataContext as MainWindowViewModel;
                Log.Information("MainWindow initialized successfully.");
                
                // Bezpečné ukončenie aplikácie: zavolať Dispose na OperationViewModel
                // Registrujeme až PO úspešnom vytvorení MainWindow
                desktop.Exit += (s, e) =>
                {
                    // Exit can be re-entered by nested shutdown paths; run cleanup only once.
                    if (Interlocked.Exchange(ref _desktopExitCleanupStarted, 1) != 0)
                        return;

                    try
                    {
                        // Best-effort: ensure no auxiliary windows keep the dispatcher alive.
                        try
                        {
                            // Copy to avoid collection mutation while closing.
                            var windows = new List<Window>(desktop.Windows);
                            foreach (var w in windows)
                            {
                                if (w != desktop.MainWindow)
                                {
                                    try { w.Close(); } catch { /* ignore */ }
                                }
                            }
                        }
                        catch
                        {
                            // ignore
                        }

                        if (desktop.MainWindow?.DataContext is MainWindowViewModel mwvm)
                        {
                            PersistAppSettingsBestEffort("desktop-exit");

                            // Zavolať Dispose na OperationViewModel
                            mwvm.Tabs?.Operation?.Dispose();
                            
                            // Zavolať Dispose na MainWindowViewModel
                            mwvm.Dispose();
                            
                            // Flush logov
                            Log.Information("Application exiting gracefully.");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Best-effort cleanup - aplikácia sa už vypína
                        try
                        {
                            Log.Error(ex, "Error during application shutdown cleanup");
                        }
                        catch
                        {
                            // Silent fail
                        }
                    }
                    finally
                    {
                        try
                        {
                            TimeService.Instance.Dispose();
                        }
                        catch
                        {
                            // ignore
                        }

                        // Important: prevent WinForms / SystemEvents foreground thread from
                        // keeping the process alive after all Avalonia windows are closed.
                        BestEffortShutdownWinFormsInfrastructure();

                        // Rider/Avalonia may keep project-specific preview host processes alive
                        // even after the app window closes. Terminate only TrackFlow-bound hosts.
                        AvaloniaDesignerHostCleanup.CleanupForCurrentProject("desktop-exit");

                        try
                        {
                            Log.CloseAndFlush();
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "MainWindow initialization failed.");
                throw;
            }
        }
        else
        {
            // V dizajnéri alebo iných lifetime-och sem len zapíšeme log, ale nevyhadzujeme chybu,
            // aby sa dizajnér mohol v poriadku spustiť.
            Log.Warning("Non-desktop ApplicationLifetime detected: {LifetimeType}", ApplicationLifetime?.GetType().FullName ?? "null");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void RegisterGlobalExceptionHandlers()
    {
        if (_globalExceptionHandlersRegistered)
            return;

        _globalExceptionHandlersRegistered = true;
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;

        // Odchytenie pádu procesu (neočakávané ukončenie napr. z iných vlákien)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                Program.ReportUnhandledException("AppDomain.UnhandledException", ex ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown"), isTerminating: e.IsTerminating);
            }
            catch { /* silent */ }

            // Pred pádom aplikácie korektne zatvoriť všetky COM porty
            CleanUpDccResources("unhandled-exception");
        };

        // Odchytenie štandardného ukončenia procesu (aj po páde, aj pri normálnom Exit)
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            PersistAppSettingsBestEffort("process-exit");
            CleanUpDccResources("process-exit");
        };
    }

    /// <summary>
    /// Núdzové, synchrónne uvoľnenie všetkých COM portov.
    /// Volaná z ProcessExit, UnhandledException a OnWindowClosing.
    /// Je bezpečná pre viacnásobné volanie (idempotentná).
    /// </summary>
    internal static void CleanUpDccResources(string reason = "shutdown")
    {
        try
        {
            var vm = _emergencyCleanupVm;
            if (vm == null) return;

            System.Diagnostics.Debug.WriteLine($"App.CleanUpDccResources: uvoľňujem COM porty, dôvod='{reason}'.");

            // Odpoj všetky DCC centrály (synchrónne, bezpečné pre crash handlery)
            try
            {
                vm.Dcc.DisconnectAll(reason);
                System.Diagnostics.Debug.WriteLine("App.CleanUpDccResources: DisconnectAll dokončené.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App.CleanUpDccResources: chyba DisconnectAll: {ex.Message}");
            }

            // Dispose celého DCC serwisu pre úplné uvoľnenie zdrojov
            try
            {
                vm.Dcc.Dispose();
                System.Diagnostics.Debug.WriteLine("App.CleanUpDccResources: DccConnectionService.Dispose dokončené.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"App.CleanUpDccResources: chyba Dispose DCC: {ex.Message}");
            }

            // Nulujeme referenciu, aby sa predišlo dvojitému čisteniu
            _emergencyCleanupVm = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App.CleanUpDccResources: kritická chyba: {ex.Message}");
        }
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Program.ReportUnhandledException("Avalonia.Dispatcher.UIThread.UnhandledException", e.Exception, isTerminating: false);

        try
        {
            TrackFlowDoctorService.Instance.Diagnose(
                "Aplikácia",
                "Používateľské rozhranie zachytilo kritickú chybu. Skontrolujte logs/trackflow-unhandled-*.txt.",
                DiagnosticLevel.Critical);
        }
        catch
        {
            // best-effort fallback only
        }

        e.Handled = true;
    }
}