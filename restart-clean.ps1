# TrackFlow - Čisté reštartovanie aplikácie
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "TrackFlow - Čisté reštartovanie" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Zastaviť všetky TrackFlow procesy
Write-Host "1. Zastavujem všetky bežiace TrackFlow procesy..." -ForegroundColor Yellow
Get-Process | Where-Object { $_.Path -like "*TrackFlow*" } | ForEach-Object {
    Write-Host "   Zastavujem: $($_.ProcessName) (PID: $($_.Id))" -ForegroundColor Gray
    Stop-Process -Id $_.Id -Force
}

# Niektoré behy (napr. dotnet run / test / host) môžu držať zamknuté TrackFlow.dll cez dotnet.exe.
# Preto ukončíme aj dotnet procesy, ktoré majú v príkazovej riadke referenciu na TrackFlow.
Write-Host "   Hľadám dotnet hosty, ktoré používajú TrackFlow (zamknuté DLL/EXE)..." -ForegroundColor Gray
try {
    Get-CimInstance Win32_Process -Filter "Name = 'dotnet.exe'" |
        Where-Object {
            $_.CommandLine -and (
                $_.CommandLine -like "*TrackFlow.dll*" -or
                $_.CommandLine -like "*TrackFlow.exe*" -or
                $_.CommandLine -like "*\\TrackFlow\\bin\\*"
            )
        } |
        ForEach-Object {
            Write-Host "   Zastavujem: dotnet (PID: $($_.ProcessId))" -ForegroundColor Gray
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
}
catch {
    Write-Host "   Nepodarilo sa prečítať zoznam procesov cez CIM: $($_.Exception.Message)" -ForegroundColor DarkYellow
}

# Počkať na ukončenie
Start-Sleep -Seconds 2

# 2. Vyčistiť bin/obj adresáre
Write-Host ""
Write-Host "2. Čistím build cache..." -ForegroundColor Yellow
if (Test-Path "bin") {
    Remove-Item -Path "bin" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "   bin/ odstránené" -ForegroundColor Gray
}
if (Test-Path "obj") {
    Remove-Item -Path "obj" -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "   obj/ odstránené" -ForegroundColor Gray
}

# 3. Build projektu
Write-Host ""
Write-Host "3. Kompilujem projekt..." -ForegroundColor Yellow
dotnet build --configuration Debug --no-incremental
if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "Kompilácia zlyhala!" -ForegroundColor Red
    Write-Host "================================================" -ForegroundColor Red
    exit 1
}

# 4. Spustenie aplikácie
Write-Host ""
Write-Host "4. Spúšťam aplikáciu..." -ForegroundColor Green
Write-Host ""
Write-Host "================================================" -ForegroundColor Green
dotnet run --no-build --configuration Debug

