$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../Ui/MapCommandLayer.cs" -Raw
$method = [regex]::Match($source, '(?ms)^        internal float\? SelectedAssignmentCost\(\).*?^        }').Value
if (!$method) { throw 'Assignment quote method not found' }
$boundary = @'
using System;
using System.Collections.Generic;
enum FactionMode { Friendly, Enemy }
class Aircraft { public object Player; public bool disabled; public FactionMode NetworkHQ; public float Price; }
class MapIcon {}
class UnitMapIcon : MapIcon { public object unit; }
class DynamicMap {
    public List<MapIcon> selectedIcons = new List<MapIcon>();
    public static FactionMode GetFactionMode(FactionMode hq) => hq;
}
static class SceneSingleton<T> { public static T i; }
class WingRegistry {
    public Aircraft Leader = new Aircraft();
    public int Count;
    public static bool HasRoom(int count) => count < 4;
    public bool Contains(Aircraft a) => false;
}
static class PersonnelFacade {
    public static class Recruitment { public static float PriceOf(Aircraft a) => a.Price; }
}
public class AssignmentQuoteChecks {
    readonly WingRegistry wing = new WingRegistry();
    readonly List<Aircraft> recruited = new List<Aircraft>();
'@
$checks = @'
    public static void Run() {
        var q = new AssignmentQuoteChecks();
        var map = new DynamicMap(); SceneSingleton<DynamicMap>.i = map;
        if (q.SelectedAssignmentCost() != null) throw new Exception("Empty selection");
        var first = new Aircraft { Price = 100 };
        map.selectedIcons.Add(new UnitMapIcon { unit = first });
        map.selectedIcons.Add(new UnitMapIcon { unit = new Aircraft { Price = 200 } });
        map.selectedIcons.Add(new UnitMapIcon { unit = new Aircraft { Price = 900, NetworkHQ = FactionMode.Enemy } });
        if (q.SelectedAssignmentCost() != 300) throw new Exception("Friendly selection total");
        q.wing.Count = 3;
        if (q.SelectedAssignmentCost() != 100) throw new Exception("Available slot cap");
        first.Price = 0;
        if (q.SelectedAssignmentCost() != 0) throw new Exception("Free assignment");
        q.wing.Count = 4;
        if (q.SelectedAssignmentCost() != null) throw new Exception("Full wing");
    }
}
'@
Add-Type -TypeDefinition ($boundary + $method + $checks) -IgnoreWarnings -WarningAction SilentlyContinue
[AssignmentQuoteChecks]::Run()
Write-Output 'Assignment quote checks passed.'
