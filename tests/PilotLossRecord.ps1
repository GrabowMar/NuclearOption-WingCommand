# Run the actual fatal-damage hook and roster settlement without Unity.
$ErrorActionPreference = 'Stop'
$roster = Get-Content "$PSScriptRoot/../Core/WingPilots.cs" -Raw
$patch = Get-Content "$PSScriptRoot/../Core/WingPilotLoss.cs" -Raw
$fatal = [regex]::Match($patch, '(?ms)^        private static void Prefix\(Pilot.*?^        }').Value.Replace('private static', 'public static')
$retire = [regex]::Match($roster, '(?ms)^        internal static void Retire\(PersistentID.*?^        }').Value
$killer = [regex]::Match($roster, '(?ms)^        internal static void RecordKiller\(.*?^        }').Value
if (!$fatal -or !$retire -or !$killer) { throw 'Loss record methods not found' }
Add-Type -TypeDefinition (@'
using System;
using System.Collections.Generic;
using PersistentID = System.Int32;
public class Aircraft {}
public class Pilot { public bool dead, ejected; public Aircraft aircraft = new Aircraft(); }
public class WingPilot {
    public bool Lost;
    public string LossCause, KilledBy, LastAircraft = "FS-12", Callsign, Name;
    public int Rank, Kills, Sorties, Xp;
}
public class Definition { public string unitName = "SAM launcher"; }
public class PersistentUnit { public Definition definition = new Definition(); public string unitName; }
public static class UnitRegistry {
    public static bool TryGetPersistentUnit(int id, out PersistentUnit unit) {
        unit = id == 2 ? new PersistentUnit() : null; return unit != null;
    }
}
public class WingCommandManager {
    public static WingCommandManager Instance;
    public void Toast(string text) {}
}
public static class Plugin { public static void LogVerbose(string text) {} }
public static class WingPilotRoster {
    static Dictionary<int, WingPilot> assigned = new Dictionary<int, WingPilot>();
    static Dictionary<int, WingPilot> losses = new Dictionary<int, WingPilot>();
    static List<WingPilot> pool = new List<WingPilot>();
    static WingPilot selectedPilot = null;
    static void AdvanceSelected(WingPilot p) {}
    static string RankName(int rank) { return ""; }
    public static WingPilot Of(Aircraft a) { return assigned.TryGetValue(1, out var p) ? p : null; }
'@ + $fatal + $retire + $killer + @'
    public static void Check() {
        foreach (string cause in new[] { "Projectile", "Explosion", "Fire", "Impact / collision" }) {
            var record = new WingPilot(); assigned[1] = record;
            var pilot = new Pilot();
            float p = cause == "Projectile" ? 101 : 0, b = cause == "Explosion" ? 101 : 0;
            float f = cause == "Fire" ? 101 : 0, i = cause == "Impact / collision" ? 101 : 0;
            Prefix(pilot, p, b, f, i, 101, 0);
            if (record.LossCause != null) throw new Exception("Nonfatal damage recorded");
            pilot.ejected = true; Prefix(pilot, p, b, f, i, 100, 0);
            if (record.LossCause != null) throw new Exception("Ejected pilot recorded");
            pilot.ejected = false; Prefix(pilot, p, b, f, i, 100, 0);
            if (record.LossCause != cause) throw new Exception("Wrong fatal cause");
            Retire(1, false); RecordKiller(1, 2); RecordKiller(1, 0); Retire(1, true);
            if (!record.Lost || record.KilledBy != "SAM launcher" || record.LastAircraft != "FS-12" ||
                assigned.ContainsKey(1) || pool.Contains(record)) throw new Exception("Lost record not retained");
        }
        var survivor = new WingPilot(); assigned[1] = survivor; Retire(1, true);
        if (survivor.Lost || !pool.Contains(survivor)) throw new Exception("Survivor lost");
    }
}
'@) -WarningAction SilentlyContinue
[WingPilotRoster]::Check()
Write-Output 'Pilot loss record checks passed.'
