<#
.SYNOPSIS
  Builds (optional) and starts TrackFlow, with a pre-step to release common file locks.

.DESCRIPTION
  This is the "one command" script for running the app from repo:
  - Stops TrackFlow / dotnet host processes that often lock TrackFlow outputs
  - (Optionally) builds Debug
  - Starts bin\Debug\net9.0-windows\TrackFlow.exe

.PARAMETER Configuration
  Build configuration (Debug/Release). Default: Debug

.PARAMETER NoBuild
  Skip the build step and just start the already-built executable.

.PARAMETER DryRun
  Print what would be executed but do not start the app.

.EXAMPLE
  .\run-trackflow.ps1

.EXAMPLE
  .\run-trackflow.ps1 -NoBuild

.EXAMPLE
  .\run-trackflow.ps1 -DryRun
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoBuild,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$csproj = Join-Path $repoRoot 'TrackFlow.csproj'

# 1) Release locks
& (Join-Path $repoRoot 'kill-trackflow-locks.ps1')

# 2) Build (optional)
if (-not $NoBuild)
{
    $buildCmd = "dotnet build -c $Configuration `"$csproj`""
    if ($DryRun)
    {
        Write-Host "[run-trackflow] DRY RUN: $buildCmd"
    }
    else
    {
        Write-Host "[run-trackflow] Building ($Configuration)..."
        dotnet build -c $Configuration "$csproj"
    }
}

# 3) Start executable
$exePath = Join-Path $repoRoot ("bin\\$Configuration\\net9.0-windows\\TrackFlow.exe")

if (-not (Test-Path $exePath))
{
    throw "TrackFlow.exe not found at: $exePath (try without -NoBuild)"
}

$startMsg = "[run-trackflow] Starting: $exePath"
if ($DryRun)
{
    Write-Host "[run-trackflow] DRY RUN: $startMsg"
    exit 0
}

Write-Host $startMsg
Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath -Parent)
Write-Host "[run-trackflow] Started."

