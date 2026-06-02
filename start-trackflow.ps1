<#
.SYNOPSIS
  One-command launcher for TrackFlow.

.DESCRIPTION
  Wrapper around run-trackflow.ps1 so it's obvious what to run.

.EXAMPLE
  .\start-trackflow.ps1
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$runner = Join-Path $repoRoot 'run-trackflow.ps1'

if (-not (Test-Path $runner)) {
    throw "run-trackflow.ps1 not found at: $runner"
}

if ($NoBuild) {
    & $runner -Configuration $Configuration -NoBuild
}
else {
    & $runner -Configuration $Configuration
}

