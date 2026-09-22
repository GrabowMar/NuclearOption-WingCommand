param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f3/Editor/Unity.exe',
    [string]$PreviewDirectory = (Join-Path $env:TEMP ('WingCommandUiCheck-' + [guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$game = 'C:/Program Files (x86)/Steam/steamapps/common/Nuclear Option'
$dll = Join-Path $repo 'bin/Release/netstandard2.1/WingCommand.dll'
if (!(Test-Path -LiteralPath $dll)) { throw 'Build WingCommand Release before running this offline check.' }
New-Item -ItemType Directory -Force -Path "$PreviewDirectory/Assets/Harness", "$PreviewDirectory/ProjectSettings", "$PreviewDirectory/Packages", "$PreviewDirectory/NOAvionics" | Out-Null
Set-Content -LiteralPath "$PreviewDirectory/ProjectSettings/ProjectVersion.txt" -Value 'm_EditorVersion: 2022.3.62f3'
Set-Content -LiteralPath "$PreviewDirectory/Packages/manifest.json" -Value '{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.audio":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.uielements":"1.0.0","com.unity.modules.physics":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.ai":"1.0.0","com.unity.modules.particlesystem":"1.0.0"}}'
Get-ChildItem -LiteralPath "$game/NuclearOption_Data/Managed" -Filter '*.dll' | Where-Object {
    $_.Name -notmatch '^(System|Mono|Microsoft|UnityEngine|UnityEditor|mscorlib|netstandard|Unity.TextMeshPro|Accessibility|Novell)'
} | Copy-Item -Destination "$PreviewDirectory/Assets/"
Get-ChildItem -LiteralPath "$game/BepInEx/core" -Filter '*.dll' | Where-Object {
    $_.Name -match '^(BepInEx.dll$|Mono|0Harmony.dll$|HarmonyXInterop)'
} | Copy-Item -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath $dll -Destination "$PreviewDirectory/Assets/"
Copy-Item -LiteralPath "$PSScriptRoot/WingCommandUiCheck.cs", "$PSScriptRoot/WingCommandPreview.asmdef" -Destination "$PreviewDirectory/Assets/Harness/"
Copy-Item -LiteralPath "$repo/AvionicsUi/avionics.avss" -Destination "$PreviewDirectory/NOAvionics/"
# Isolated Editor project only: no plugin installation, game launch, or gameplay actions.
$arguments = @('-batchmode', '-disable-assembly-updater', '-projectPath', ('"' + $PreviewDirectory + '"'), '-executeMethod', 'WingCommandUiCheck.Run', '-logFile', ('"' + "$PreviewDirectory/check.log" + '"'))
$process = Start-Process -FilePath $Unity -ArgumentList $arguments -WorkingDirectory $PreviewDirectory -WindowStyle Hidden -PassThru
$process.WaitForExit()
Write-Output "Results and renders: $PreviewDirectory"
if (Test-Path -LiteralPath "$PreviewDirectory/result.txt") { Get-Content -LiteralPath "$PreviewDirectory/result.txt" }
else { Get-Content -LiteralPath "$PreviewDirectory/check.log" -Tail 100 }
if ($process.ExitCode -ne 0) { throw "Unity Wing Command UI check failed: $($process.ExitCode)" }
