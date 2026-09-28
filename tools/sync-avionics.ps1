<#
.SYNOPSIS
  Copies the shared NOAvionics toolkit from Boscali Summer's working tree into Wing Command (spec WMC program §3.1).
.PARAMETER Source
  The Boscali Summer checkout (default: ..\Boscali Summer next to this repo).
.PARAMETER Check
  Report drift and exit 1 when WC differs from the source; copy nothing.
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\..\Boscali Summer'),
    [switch]$Check
)
$ErrorActionPreference = 'Stop'
$target = Split-Path $PSScriptRoot -Parent

if (-not (Test-Path (Join-Path $Source 'AvionicsUi\avionics.avss'))) {
    Write-Host "No NOAvionics toolkit at '$Source' (expected AvionicsUi\avionics.avss). Nothing copied."
    exit 2
}

# Boscali-only files that live in the toolkit folders.
$skip = @('TheaterScoring.cs')
$sets = @(
    @{ Dir = 'Avionics';   Filter = '*.cs' },
    @{ Dir = 'AvionicsUi'; Filter = '*.cs' },
    @{ Dir = 'AvionicsUi'; Filter = 'avionics.avss' }
)

function Normalized([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    return [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
}

$changed = New-Object System.Collections.Generic.List[string]
foreach ($set in $sets) {
    $files = Get-ChildItem (Join-Path $Source $set.Dir) -Filter $set.Filter -File |
        Where-Object { $_.Name -notlike '*Tests.cs' -and $skip -notcontains $_.Name }
    foreach ($file in $files) {
        $dest = Join-Path (Join-Path $target $set.Dir) $file.Name
        if ((Normalized $file.FullName) -ceq (Normalized $dest)) { continue }
        $changed.Add("$($set.Dir)/$($file.Name)")
        if (-not $Check) { Copy-Item -LiteralPath $file.FullName -Destination $dest -Force }
    }
}

if ($changed.Count -eq 0) {
    Write-Host 'NOAvionics: in sync.'
    exit 0
}
$verb = if ($Check) { 'differs' } else { 'synced' }
foreach ($c in $changed) { Write-Host "NOAvionics: $verb  $c" }
if ($Check) { exit 1 }
exit 0
