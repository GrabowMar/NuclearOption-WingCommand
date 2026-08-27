<#
    Builds the two release assets.

    THE BARE DLL IS THE ONE NOMM WANTS. Checking how NOMM actually lays out the mods it
    manages settles it - both installed plugins on this machine ship a plain .dll as their
    release asset and nothing else:

        BepInEx/plugins/NOSMR/NOSMR.dll                          <- id "NOSMR", asset NOSMR.dll
        BepInEx/plugins/com.nikkorap.blueprinter/Blueprinter_1.8.21.dll

    NOMM creates the folder from the manifest id and drops the DLL in. A zip of a folder
    tree is not what it takes from a file drop, which is why dragging one in did nothing.

    (For the record, .nobp is unrelated: it is a Blueprinter content bundle - custom
    aircraft - carried as artifact type "addon" with "extends" pointing at Blueprinter.
    WingCommand is a real BepInEx plugin, so it is type "plugin" and ships a DLL.)

    The zip is still built, for people installing by hand who want the README and licence
    alongside. It mirrors the game folder, so extracting it at the Nuclear Option root puts
    the DLL where BepInEx looks:

        BepInEx/plugins/WingCommand/WingCommand.dll

    NOMNOM requires the manifest's version to match the version in the DLL, so the version
    is read back out of the built assembly rather than typed anywhere. Both assets' sha256
    are printed at the end ready to paste.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root  = Split-Path -Parent $PSScriptRoot
$built = Join-Path $root "src\WingCommand\bin\$Configuration\netstandard2.1\WingCommand.dll"

if (-not (Test-Path $built)) {
    throw "Build output not found at $built. Run: dotnet build -c $Configuration"
}

# Read the version from the assembly itself. Typing it twice is how a manifest and a DLL
# drift apart, and NOMNOM rejects the pair when they disagree.
$version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($built).FileVersion
$version = ($version -split '\.')[0..2] -join '.'

$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'
$pluginDir = Join-Path $stage 'BepInEx\plugins\WingCommand'

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null

Copy-Item $built $pluginDir -Force
Copy-Item (Join-Path $root 'LICENSE') $pluginDir -Force
Copy-Item (Join-Path $root 'README.md') $pluginDir -Force

# Regenerated rather than kept by hand, so its version cannot fall behind the DLL.
$meta = [ordered]@{
    id       = 'WingCommand'
    artifact = [ordered]@{
        type        = 'plugin'
        fileName    = "WingCommand-$version.zip"
        version     = $version
        category    = 'Release'
        gameVersion = '0.34.2'
    }
}
$meta | ConvertTo-Json -Depth 5 | Out-File (Join-Path $pluginDir 'meta.json') -Encoding utf8

$archive = Join-Path $dist "WingCommand-$version.zip"
if (Test-Path $archive) { Remove-Item $archive -Force }

# Written entry by entry rather than with Compress-Archive, which on Windows PowerShell
# writes backslash separators into the entry names. The ZIP spec calls for forward slashes,
# and extractors that take it literally produce a single file called
# "BepInEx\plugins\WingCommand\WingCommand.dll" instead of the directory tree - which would
# put the DLL somewhere BepInEx never looks.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [System.IO.Compression.ZipFile]::Open($archive, 'Create')
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        $entryName = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $file.FullName, $entryName) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

Remove-Item $stage -Recurse -Force
$zipHash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLower()

# The primary asset: the bare DLL, which is what NOMM installs.
$loose = Join-Path $dist 'WingCommand.dll'
Copy-Item $built $loose -Force
$dllHash = (Get-FileHash $loose -Algorithm SHA256).Hash.ToLower()

$base = "https://github.com/GrabowMar/NuclearOption-WingCommand/releases/download/v$version"

Write-Host ""
Write-Host "Version $version"
Write-Host ""
Write-Host "Attach BOTH to the release, DLL FIRST - NOMNOM takes the first asset as the"
Write-Host "package, and NOMM installs a bare DLL rather than an archive."
Write-Host ""
Write-Host "  1. $loose"
Write-Host "     sha256  $dllHash"
Write-Host "  2. $archive"
Write-Host "     sha256  $zipHash   (manual installs only)"
Write-Host ""
Write-Host "NOMNOM manifest artifact entry:"
Write-Host "  `"fileName`":    `"WingCommand.dll`""
Write-Host "  `"version`":     `"$version`""
Write-Host "  `"hash`":        `"sha256:$dllHash`""
Write-Host "  `"downloadUrl`": `"$base/WingCommand.dll`""
