# Writes the CHANGELOG.md section of one version (the lines under "## [x.y.z] - date", heading excluded) to a file,
# for the GitHub release notes. Fails when the section is missing or has no bullet: every version needs its notes
# before it is tagged. Used by .github/workflows/release.yml; runs locally too:
#   pwsh tools/changelog-section.ps1 -Version 0.3.0 -Out notes.md
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Out,
    [string] $Changelog = ''
)

$ErrorActionPreference = 'Stop'
if (-not $Changelog) { $Changelog = Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md' }
$lines = @(Get-Content -LiteralPath $Changelog -Encoding utf8)
$heading = '^##\s+\[?v?' + [regex]::Escape($Version) + '\]?(\s|$)'
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match $heading) { $start = $i; break }
}
if ($start -lt 0) { Write-Error "CHANGELOG.md has no section for $Version (expected '## [$Version] - YYYY-MM-DD')"; exit 1 }

$end = $lines.Count
for ($j = $start + 1; $j -lt $lines.Count; $j++) {
    if ($lines[$j] -match '^##\s') { $end = $j; break }
}

# Link reference definitions ("[x]: url") belong to the whole file, not to one release.
$section = @()
if ($end -gt $start + 1) {
    $section = @($lines[($start + 1)..($end - 1)] | Where-Object { $_ -notmatch '^\s{0,3}\[[^\]]+\]:\s*\S' })
}
$body = ($section -join "`n").Trim()
if ($body -notmatch '(?m)^\s{0,3}[-*+]\s+\S') { Write-Error "The CHANGELOG.md section for $Version has no notes"; exit 1 }

Set-Content -LiteralPath $Out -Value $body -Encoding utf8
Write-Output "Release notes for ${Version}:"
Write-Output $body
