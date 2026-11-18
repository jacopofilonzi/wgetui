# Compiles installer/wgetui.iss with Inno Setup 7 (`make installer`, after `make publish`).
# ISCC.exe is usually not on PATH and lives under a folder with spaces, which make can't search for, so it is
# resolved here: -Iscc (`make installer ISCC=...`), then the per-user install (%LOCALAPPDATA%\Programs\Inno Setup 7),
# the all-users one (Program Files, used by the release workflow), then PATH. The script refuses Inno Setup 6.
param([string]$Iscc = '')

$ErrorActionPreference = 'Stop'

if (-not $Iscc) {
    # The Inno Setup 7 folders come before PATH: GitHub's Windows runners put Inno Setup 6 on PATH (Chocolatey).
    $candidates = @()
    foreach ($root in @($env:LOCALAPPDATA, $env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($root) {
            $base = if ($root -eq $env:LOCALAPPDATA) { Join-Path $root 'Programs' } else { $root }
            $candidates += Join-Path (Join-Path $base 'Inno Setup 7') 'ISCC.exe'
        }
    }
    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
    $Iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $Iscc) {
        Write-Error "Inno Setup 7 (ISCC.exe) not found. Install it from https://jrsoftware.org/isdl.php or pass ISCC=<path to ISCC.exe>."
    }
}
elseif (-not (Test-Path -LiteralPath $Iscc)) {
    Write-Error "ISCC not found at '$Iscc'."
}

$script = Join-Path $PSScriptRoot '..\installer\wgetui.iss'
Write-Host "Using $Iscc"
& $Iscc $script
exit $LASTEXITCODE
