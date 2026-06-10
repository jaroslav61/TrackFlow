using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;

namespace TrackFlow.Services;

internal static class AvaloniaDesignerHostCleanup
{
    public static void CleanupForCurrentProject(string reason = "shutdown")
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var projectDll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TrackFlow.dll"));
            var currentPid = Environment.ProcessId;

            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, CommandLine, Name FROM Win32_Process WHERE Name = 'dotnet.exe'");

            foreach (var obj in searcher.Get().OfType<ManagementObject>())
            {
                try
                {
                    var pid = Convert.ToInt32(obj["ProcessId"] ?? 0);
                    if (pid <= 0 || pid == currentPid)
                        continue;

                    var commandLine = obj["CommandLine"]?.ToString() ?? string.Empty;
                    if (commandLine.Length == 0)
                        continue;

                    if (!commandLine.Contains("Avalonia.Designer.HostApp.dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!commandLine.Contains(projectDll, StringComparison.OrdinalIgnoreCase)
                        && !commandLine.Contains("TrackFlow.dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        process.Kill(entireProcessTree: false);
                    }
                    catch
                    {
                        // best-effort only
                    }
                }
                catch
                {
                    // best-effort only
                }
            }
        }
        catch
        {
            // best-effort only
        }
    }
}

