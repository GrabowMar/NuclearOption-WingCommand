$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path "$PSScriptRoot/../..").Path
$output = Join-Path $repo 'build/flight-tuning'
New-Item -ItemType Directory -Force $output | Out-Null
# Compile fresh copies of production policy. Only the experiment's copy has mutable tuning.
foreach ($name in @('FormationGuidance', 'FormationTracking', 'FormationIntercept', 'FormationClosure', 'FormationRecovery', 'FormationControlRules')) {
    Copy-Item -LiteralPath "$repo/Pure/Flight/$name.cs" -Destination $output
}
(Get-Content "$repo/Pure/Ai/WingTuning.cs" -Raw).Replace('public const float', 'public static float') | Set-Content "$output/WingTuning.cs"
Copy-Item -LiteralPath "$PSScriptRoot/Program.cs" -Destination $output
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup>
</Project>
'@ | Set-Content "$output/Sweep.csproj"
dotnet run --project "$output/Sweep.csproj" -c Release -- $output
if ($LASTEXITCODE -ne 0) { throw 'Flight tuning sweep failed' }
