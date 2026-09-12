# Run each PowerShell check in a fresh process: Add-Type definitions cannot be unloaded.
$ErrorActionPreference = 'Stop'

foreach ($project in Get-ChildItem "$PSScriptRoot/*/*.csproj") {
    & dotnet test $project.FullName --nologo
    if ($LASTEXITCODE -ne 0) { throw "Failed: $($project.Name)" }
}

foreach ($check in Get-ChildItem "$PSScriptRoot/*.ps1" | Where-Object FullName -ne $PSCommandPath) {
    & pwsh -NoProfile -File $check.FullName
    if ($LASTEXITCODE -ne 0) { throw "Failed: $($check.Name)" }
}
