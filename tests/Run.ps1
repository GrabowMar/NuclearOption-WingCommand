# Run every test project, then each PowerShell check in a fresh process (Add-Type definitions cannot be
# unloaded). Checks under quarantine/ target files quarantined in WingCommand.csproj and are not run;
# move each back beside this script when its subject is restored:
#   M1 WingSquadApi   M3 AirbaseDisplayName, DepartureDispatch   M4 AssignmentQuote, TacticalAircraftSafety
#   M7 TacticalLayout
$ErrorActionPreference = 'Stop'

foreach ($project in Get-ChildItem "$PSScriptRoot/*/*.csproj") {
    & dotnet test $project.FullName --nologo
    if ($LASTEXITCODE -ne 0) { throw "Failed: $($project.Name)" }
}

# Prefer PowerShell 7; fall back to Windows PowerShell where it is not installed.
$shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell.exe' }
foreach ($check in Get-ChildItem "$PSScriptRoot/*.ps1" | Where-Object FullName -ne $PSCommandPath) {
    & $shell -NoProfile -ExecutionPolicy Bypass -File $check.FullName
    if ($LASTEXITCODE -ne 0) { throw "Failed: $($check.Name)" }
}
