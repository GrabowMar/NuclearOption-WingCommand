$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../Economy/WingLaunchFields.cs" -Raw
$method = [regex]::Match($source, '(?ms)^        public static string DisplayName\(Airbase airbase\).*?^        }').Value
if (!$method) { throw 'Airbase display-name method not found' }
$formatter = Get-Content "$PSScriptRoot/../Pure/Economy/AirbaseNameFormatter.cs" -Raw
$checks = @'
namespace WingCommand {
    public class SavedAirbase { public string DisplayName; public string UniqueName; }
    public class Airbase { public SavedAirbase SavedAirbase; public string name; }
    public static class AirbaseDisplayNameChecks {
'@
$run = @'
        public static void Run() {
            var field = new Airbase { name = "CustomAirbase", SavedAirbase = new SavedAirbase {
                DisplayName = "Opal Airport", UniqueName = "airbase_NE" } };
            Check(DisplayName(field), "Opal Airport");
            field.SavedAirbase.DisplayName = "CVN-04_Test";
            Check(DisplayName(field), "CVN-04_Test"); // Read renames live, preserve authored text.
            field.SavedAirbase.DisplayName = " ";
            Check(DisplayName(field), "Northeast Airbase");
            field.SavedAirbase = null;
            Check(DisplayName(field), "Custom Airbase");
            Check(DisplayName(null), "FIELD");
        }
        static void Check(string actual, string expected) {
            if (actual != expected) throw new System.Exception(actual + " != " + expected);
        }
    }
}
'@
Add-Type -TypeDefinition ($formatter + $checks + $method + $run)
[WingCommand.AirbaseDisplayNameChecks]::Run()
Write-Output 'Airbase display-name checks passed.'
