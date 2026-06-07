<#
.SYNOPSIS
    Zabalí iba zdrojový kód TrackFlow do ZIPu (bez bin/obj/.git/artifactov).

.DESCRIPTION
    Vytvorí ZIP archív obsahujúci len skutočné zdrojáky projektu — vynechá:
      - bin\, obj\                (build výstupy)
      - artifacts\, Artifacts\    (zakázané MSBuild outputy)
      - .git\, .vs\, .idea\       (VCS / IDE metadáta)
      - logs\, _archived\         (lokálne logy, staré archívy)
      - *.user, *.suo, *.cache, *.pdb, doctor-log-*.log, build_*.txt
    Výsledný ZIP má typicky < 50 MB namiesto 2+ GB.

.PARAMETER OutputPath
    Cesta k výslednému .zip súboru. Default: ..\TrackFlow-src-<yyyyMMdd-HHmm>.zip

.PARAMETER IncludeTests
    Ak je nastavené, zahrnie aj TrackFlow.Tests\ adresár (default: áno).

.EXAMPLE
    .\pack-source.ps1
    .\pack-source.ps1 -OutputPath D:\backup\TrackFlow.zip
#>
[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$NoTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectName = Split-Path -Leaf $root

if (-not $OutputPath)
{
    $stamp = Get-Date -Format 'yyyyMMdd-HHmm'
    $OutputPath = Join-Path (Split-Path -Parent $root) "$projectName-src-$stamp.zip"
}

# Vzory adresárov ktoré preskočiť (porovnáva sa s relatívnou cestou, segmenty oddelené '\')
$excludeDirSegments = @(
    'bin', 'obj',
    'artifacts', 'Artifacts',
    '.git', '.vs', '.idea',
    'logs', '_archived',
    'TestResults', 'route-test-isolation',
    '.test-out'
)
if ($NoTests)
{
    $excludeDirSegments += 'TrackFlow.Tests'
}

# Vzory názvov súborov ktoré preskočiť (wildcard match na názov súboru)
$excludeFilePatterns = @(
    '*.user', '*.suo', '*.cache', '*.pdb',
    'apphost.exe',
    'doctor-log-*.log',
    'build_*.txt', '.build_errors.txt', '.berr.txt',
    'build_out.txt'
)

Write-Host "Zdroj : $root" -ForegroundColor Cyan
Write-Host "Cieľ  : $OutputPath" -ForegroundColor Cyan

if (Test-Path -LiteralPath $OutputPath)
{
    Remove-Item -LiteralPath $OutputPath -Force
}

function Test-Excluded
{
    param([string]$RelativePath, [bool]$IsDirectory)
    $segments = $RelativePath -split '[\\/]'
    foreach ($seg in $segments)
    {
        if ($excludeDirSegments -contains $seg)
        {
            return $true
        }
    }
    if (-not $IsDirectory)
    {
        $name = $segments[-1]
        foreach ($pat in $excludeFilePatterns)
        {
            if ($name -like $pat)
            {
                return $true
            }
        }
    }
    return $false
}

# Vytvor zoznam súborov na zabalenie
Write-Host "Zbieram súbory..." -ForegroundColor Yellow
$files = @()
$totalBytes = 0L
Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object {
    $rel = $_.FullName.Substring($root.Length).TrimStart('\', '/')
    if (-not (Test-Excluded -RelativePath $rel -IsDirectory $false))
    {
        $files += [pscustomobject]@{ Full = $_.FullName; Rel = $rel; Size = $_.Length }
        $totalBytes += $_.Length
    }
}
Write-Host ("  {0} súborov, {1:N1} MB" -f $files.Count, ($totalBytes / 1MB)) -ForegroundColor Yellow

# Vytvor ZIP cez System.IO.Compression (zachová relatívne cesty bez koreňového adresára)
Write-Host "Vytváram ZIP..." -ForegroundColor Yellow
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$fs = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::CreateNew)
try
{
    $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try
    {
        $i = 0
        foreach ($f in $files)
        {
            $i++
            if ($i % 200 -eq 0)
            {
                Write-Progress -Activity "Pakujem" -Status "$i / $( $files.Count )" -PercentComplete (($i / $files.Count) * 100)
            }
            $entryName = "$projectName/" + ($f.Rel -replace '\\', '/')
            $entry = $zip.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
            $es = $entry.Open()
            try
            {
                $src = [System.IO.File]::OpenRead($f.Full)
                try
                {
                    $src.CopyTo($es)
                }
                finally
                {
                    $src.Dispose()
                }
            }
            finally
            {
                $es.Dispose()
            }
        }
        Write-Progress -Activity "Pakujem" -Completed
    }
    finally
    {
        $zip.Dispose()
    }
}
finally
{
    $fs.Dispose()
}

$outSize = (Get-Item -LiteralPath $OutputPath).Length
Write-Host ""
Write-Host ("HOTOVO: {0}" -f $OutputPath) -ForegroundColor Green
Write-Host ("Veľkosť archívu: {0:N1} MB (originál {1:N1} MB)" -f ($outSize / 1MB), ($totalBytes / 1MB)) -ForegroundColor Green

