<#
    Deploys the built plugin into the game's BepInEx plugins folder.

    The game lives under Program Files, so this may need an elevated shell depending on
    how Steam's folder permissions are set.

    The deployed meta.json is DERIVED FROM THE DLL BEING DEPLOYED rather than copied from
    build/meta.json. A hash can only ever describe one exact binary, and builds are not
    deterministic - the assembly MVID changes on every rebuild, so a hand-written hash is
    wrong again the moment anyone rebuilds. Copying the file verbatim is how the game
    folder ended up advertising a version and a hash that matched nothing on disk.

    Everything that cannot be read off the assembly (id, category, game version) still
    comes from build/meta.json, which stays the one place those are written down.
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

# Three parts, the way the tags and the release assets are numbered: a four-part
# AssemblyVersion is padded with a trailing .0 that no release ever carries.
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($built).FileVersion
$version = ($version -split '\.')[0..2] -join '.'
$hash    = (Get-FileHash $built -Algorithm SHA256).Hash.ToLower()

$meta = Get-Content (Join-Path $PSScriptRoot 'meta.json') -Raw | ConvertFrom-Json
if ($meta.artifact.version -ne $version) {
    Write-Warning ("build/meta.json says $($meta.artifact.version) but the DLL is $version. " +
                   "Deploying $version; update build/meta.json before packaging a release.")
}
$meta.artifact.version = $version
$meta.artifact.hash    = "sha256:$hash"
$meta | ConvertTo-Json -Depth 5 | Out-File (Join-Path $target 'meta.json') -Encoding utf8

Write-Host "Deployed WingCommand.dll $version -> $target"
Write-Host "  sha256  $hash"
