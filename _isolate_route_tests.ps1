param(
    [int]$TimeoutSeconds = 20
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Get-Process testhost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$listOutput = dotnet test '.\TrackFlow.Tests\TrackFlow.Tests.csproj' --configuration Debug --no-build --filter 'FullyQualifiedName~OperationViewModelRouteActivationTests' --list-tests
$tests = $listOutput | Where-Object { $_ -match '^\s+TrackFlow\.Tests\.OperationViewModelRouteActivationTests\.' } | ForEach-Object { $_.Trim() }

if ($tests.Count -eq 0)
{
    Write-Host 'NO_TESTS_FOUND'
    exit 2
}

$logDir = Join-Path $PSScriptRoot 'route-test-isolation'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

foreach ($test in $tests)
{
    $safeName = ($test -replace '[^A-Za-z0-9_.-]', '_')
    $outFile = Join-Path $logDir "$safeName.out.txt"
    $errFile = Join-Path $logDir "$safeName.err.txt"
    Remove-Item $outFile, $errFile -Force -ErrorAction SilentlyContinue

    $args = @(
        'test', '.\TrackFlow.Tests\TrackFlow.Tests.csproj',
        '--configuration', 'Debug',
        '--no-build',
        '--filter', "FullyQualifiedName=$test",
        '--verbosity', 'quiet'
    )

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath 'dotnet' -ArgumentList $args -PassThru -WindowStyle Hidden -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    if (-not $p.WaitForExit($TimeoutSeconds * 1000))
    {
        try
        {
            $p.Kill($true)
        }
        catch
        {
            try
            {
                $p.Kill()
            }
            catch
            {
            }
        }
        Get-Process testhost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        $sw.Stop()
        Write-Host "TIMEOUT|$([math]::Round($sw.Elapsed.TotalSeconds, 2) )|$test"
        Write-Host "OUT=$outFile"
        Write-Host "ERR=$errFile"
        exit 124
    }
    $sw.Stop()

    if ($p.ExitCode -ne 0)
    {
        Write-Host "FAIL|$( $p.ExitCode )|$([math]::Round($sw.Elapsed.TotalSeconds, 2) )|$test"
        Write-Host "OUT=$outFile"
        Write-Host "ERR=$errFile"
        Get-Content $outFile -Tail 80 -ErrorAction SilentlyContinue
        Get-Content $errFile -Tail 80 -ErrorAction SilentlyContinue
        exit $p.ExitCode
    }

    Write-Host "PASS|$([math]::Round($sw.Elapsed.TotalSeconds, 2) )|$test"
}

Write-Host "ALL_PASS|$( $tests.Count )"
exit 0

