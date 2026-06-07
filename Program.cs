using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Serilog;
using TrackFlow.Services;

namespace TrackFlow
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            int exitCode;

            // Prevent multiple instances (e.g., when IDE/run tooling accidentally launches repeatedly).
            // Extra instances exit immediately.
            using var singleInstance = new Mutex(initiallyOwned: true, name: @"Local\TrackFlow", createdNew: out var createdNew);
            if (!createdNew)
            {
                return;
            }

            // Inicializovať Serilog logger
            // Logy idú vždy do projektového koreňa (kde je .csproj), nie do bin/
            var logDir = FindProjectLogsDir();
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "trackflow-.txt");

            // Best-effort: clear stale Avalonia designer hosts that still point to TrackFlow.dll
            // from previous Rider sessions so a new run starts cleanly.
            AvaloniaDesignerHostCleanup.CleanupForCurrentProject("startup");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug)
                .CreateLogger();

            AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            {
                var exception = eventArgs.ExceptionObject as Exception
                    ?? new Exception($"Non-Exception unhandled object: {eventArgs.ExceptionObject}");
                ReportUnhandledException("AppDomain.CurrentDomain.UnhandledException", exception, eventArgs.IsTerminating);
            };

            TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            {
                ReportUnhandledException("TaskScheduler.UnobservedTaskException", eventArgs.Exception, isTerminating: false);
                eventArgs.SetObserved();
            };
            
            try
            {
                Log.Information("TrackFlow starting up... (logs: {LogPath})", logPath);
                exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }

            Environment.ExitCode = exitCode;
        }

        internal static void ReportUnhandledException(string source, Exception exception, bool isTerminating)
        {
            var safeException = exception ?? new Exception("Unknown unhandled exception.");
            var report = BuildUnhandledExceptionReport(source, safeException, isTerminating);

            try
            {
                Directory.CreateDirectory(GetProjectLogsDir());
                var filePath = Path.Combine(GetProjectLogsDir(), $"trackflow-unhandled-{DateTime.Now:yyyyMMdd}.txt");
                File.AppendAllText(filePath, report + Environment.NewLine + new string('-', 100) + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // best-effort fallback only
            }

            try
            {
                if (isTerminating)
                    Log.Fatal(safeException, "Unhandled exception in {Source}", source);
                else
                    Log.Error(safeException, "Unhandled exception in {Source}", source);
            }
            catch
            {
                // best-effort fallback only
            }

            try
            {
                var state = isTerminating ? "Aplikácia padá" : "Zachytená neobslúžená chyba";
                TrackFlowDoctorService.Instance.Diagnose(
                    "Aplikácia",
                    $"{state}. Zdroj: {source}. {safeException.GetType().Name}: {safeException.Message}",
                    DiagnosticLevel.Critical);
            }
            catch
            {
                // best-effort fallback only
            }
        }

        internal static string GetProjectLogsDir() => FindProjectLogsDir();

        private static string BuildUnhandledExceptionReport(string source, Exception exception, bool isTerminating)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            builder.AppendLine($"Source: {source}");
            builder.AppendLine($"IsTerminating: {isTerminating}");
            builder.AppendLine($"OS: {Environment.OSVersion}");
            builder.AppendLine($"Runtime: {Environment.Version}");
            builder.AppendLine($"ThreadId: {Environment.CurrentManagedThreadId}");
            builder.AppendLine();
            builder.AppendLine(exception.ToString());
            return builder.ToString();
        }

        /// <summary>
        /// Nájde adresár logs/ v projektovom koreňi (kde leží .csproj alebo .sln).
        /// Fallback: vedľa executable.
        /// </summary>
        private static string FindProjectLogsDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (dir.GetFiles("*.csproj").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                    return Path.Combine(dir.FullName, "logs");
                dir = dir.Parent;
            }
            return Path.Combine(AppContext.BaseDirectory, "logs");
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
		.WithInterFont()
                .LogToTrace();

    }
}
