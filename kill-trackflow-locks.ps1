<#
.SYNOPSIS
  Stops processes that commonly lock TrackFlow build outputs (TrackFlow.exe / TrackFlow.dll).

.DESCRIPTION
  When building/testing, MSBuild may fail to copy TrackFlow.exe/TrackFlow.dll if they are
  locked by a running TrackFlow instance or a lingering .NET host/testhost process.

  This script tries to be conservative:
  - Always stops TrackFlow.exe (if running)
  - Stops only those dotnet/testhost processes whose CommandLine mentions TrackFlow.dll/TrackFlow.exe

  Run from the repository root:
    .\kill-trackflow-locks.ps1

.NOTES
  Requires permission to query process command lines (Win32_Process).
#>

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot

Write-Host "[kill-trackflow-locks] Repo: $repoRoot"

# 1) Stop TrackFlow.exe (any instance)
$trackflow = Get-Process -Name 'TrackFlow' -ErrorAction SilentlyContinue
if ($trackflow)
{
    Write-Host "[kill-trackflow-locks] Stopping TrackFlow.exe: $( $trackflow.Id -join ', ' )"
    $trackflow | Stop-Process -Force
}

# 2) Stop dotnet/testhost processes that reference TrackFlow outputs in their command line
$targets = @('TrackFlow.dll', 'TrackFlow.exe')

$procs = Get-CimInstance Win32_Process |
        Where-Object {
            $_.CommandLine -and (
            ($_.Name -in @('dotnet.exe', 'testhost.exe', 'vstest.console.exe')) -or
                    ($_.Name -like '*dotnet*') -or
                    ($_.Name -like '*testhost*')
            )
        } |
        Where-Object {
            $cmd = $_.CommandLine
            $targets | Where-Object { $cmd -like "*$_*" } | Measure-Object | Select-Object -ExpandProperty Count
        } |
        Select-Object ProcessId, Name, CommandLine

if (-not $procs)
{
    Write-Host "[kill-trackflow-locks] No matching .NET host processes found."
    exit 0
}

Write-Host "[kill-trackflow-locks] Stopping $( $procs.Count ) .NET host process(es) that reference TrackFlow:"
foreach ($p in $procs)
{
    Write-Host "  - PID $( $p.ProcessId ) $( $p.Name ) :: $( $p.CommandLine )"
}

$procs.ProcessId | ForEach-Object {
    Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue
}

Write-Host "[kill-trackflow-locks] Done."

