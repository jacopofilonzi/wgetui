# Stops wgetui instances started from this repo's Debug output (`make dev` / `make run`).
# With close-to-tray, closing the window keeps the app running and its DLLs locked, so the next
# `make dev` can't copy its build output. Only processes whose exe lives under this repo's
# src/WgetUi.App/bin/Debug are touched: an installed or published wgetui is left alone.
$debugDir = Join-Path $PSScriptRoot '..\src\WgetUi.App\bin\Debug'
$debugDir = [System.IO.Path]::GetFullPath($debugDir)
Get-CimInstance Win32_Process -Filter "Name='wgetui.exe'" |
    Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($debugDir, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        Write-Host "Stopping dev wgetui (PID $($_.ProcessId))"
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
