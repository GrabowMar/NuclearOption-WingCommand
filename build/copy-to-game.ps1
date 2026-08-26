<#
    Deploys the built plugin into the game's BepInEx plugins folder.

    The game lives under Program Files, so this may need an elevated shell depending on
    how Steam's folder permissions are set.
#>
[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $PSScriptRoot
$built   = Join-Path $root "src\WingCommand\bin\$Configuration\netstandard2.1\WingCommand.dll"
$target  = Join-Path $GameDir 'BepInEx\plugins\WingCommand'

if (-not (Test-Path $built)) {
    throw "Build output not found at $built. Run: dotnet build -c $Configuration"
}

if (-not (Test-Path $target)) {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
}

Copy-Item $built $target -Force
Copy-Item (Join-Path $PSScriptRoot 'meta.json') $target -Force

Write-Host "Deployed WingCommand.dll -> $target"
