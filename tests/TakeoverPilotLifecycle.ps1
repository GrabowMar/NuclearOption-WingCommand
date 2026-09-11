# Exercise the actual registry transition methods without loading Unity.
$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../Core/WingRegistry.cs" -Raw
$methods = foreach ($name in @('SetLeader', 'ReplaceWithLeader', 'Clear')) {
    $match = [regex]::Match($source, "(?ms)^        public (?:void|bool) $name\(.*?^        }")
    if (!$match.Success) { throw "Missing registry method: $name" }
    $match.Value
}
Add-Type -TypeDefinition (@'
using System;
using System.Collections.Generic;
public struct PersistentID { public int Value; }
public class Pilot { public bool dead, ejected; }
public class Aircraft {
    public PersistentID persistentID;
    public bool disabled;
    public Pilot pilot = new Pilot();
}
public class WingPilot { public bool Lost; }
public class WingMember { public Aircraft Aircraft; }
public static class PersonnelFacade {
    public static class Roster {
        public static Dictionary<PersistentID, WingPilot> assigned = new Dictionary<PersistentID, WingPilot>();
        public static int Losses;
        public static WingPilot Of(WingMember member) { return assigned[member.Aircraft.persistentID]; }
        public static void Retire(WingMember member, bool survived) { Retire(member.Aircraft.persistentID, survived); }
        public static void Retire(PersistentID id, bool survived) {
            WingPilot pilot;
            if (!assigned.TryGetValue(id, out pilot)) return;
            assigned.Remove(id);
            if (!survived) { pilot.Lost = true; Losses++; }
        }
        public static void Assign(Aircraft aircraft, WingPilot pilot) { assigned[aircraft.persistentID] = pilot; }
    }
    public static class Takeover {
        public static bool Begin(WingRegistry w, Aircraft a) { return false; }
        public static void LeaderRestored(Aircraft a) {}
    }
}
public static class WingHost { public static void NoteLeader(Aircraft a) {} }
public static class HangarDepartureLane { public static void Release(WingMember m) {} }
public static class WingMarkers { public static void Repaint(Aircraft a) {} }
public class WingRegistry {
    private List<WingMember> members = new List<WingMember>();
    public Aircraft Leader { get; private set; }
    private PersistentID? takeoverPilotAircraftId;
    public bool LeaderOnDeck;
    private static Pilot PrimaryPilot(Aircraft a) { return a == null ? null : a.pilot; }
    private void DisbandAll(string reason) {}
'@ + ($methods -join "`n") + @'
    public static void Check() {
        foreach (string outcome in new[] { "death", "disabled", "ejected", "switch", "missing", "repeat" }) {
            var registry = new WingRegistry();
            var original = new Aircraft { persistentID = new PersistentID { Value = 1 } };
            var replacement = new Aircraft { persistentID = new PersistentID { Value = 2 } };
            var member = new WingMember { Aircraft = original };
            var pilot = new WingPilot();
            PersonnelFacade.Roster.assigned.Clear();
            PersonnelFacade.Roster.Losses = 0;
            PersonnelFacade.Roster.Assign(original, pilot);
            registry.members.Add(member);
            if (!registry.ReplaceWithLeader(member, replacement) ||
                PersonnelFacade.Roster.assigned.ContainsKey(original.persistentID) ||
                PersonnelFacade.Roster.assigned[replacement.persistentID] != pilot || pilot.Lost)
                throw new Exception("Takeover did not preserve the pilot assignment");
            if (registry.ReplaceWithLeader(member, replacement))
                throw new Exception("Repeated takeover should be ignored");
            if (outcome == "death" || outcome == "repeat") replacement.pilot.dead = true;
            if (outcome == "disabled") replacement.disabled = true;
            if (outcome == "ejected") replacement.pilot.ejected = true;
            if (outcome == "missing") registry.Leader = null;
            Aircraft next = outcome == "switch" ? new Aircraft() : replacement;
            if (outcome == "missing") next = null;
            registry.SetLeader(next);
            registry.SetLeader(next);
            bool lost = outcome != "switch";
            if (pilot.Lost != lost || PersonnelFacade.Roster.Losses != (lost ? 1 : 0) ||
                PersonnelFacade.Roster.assigned.ContainsKey(replacement.persistentID))
                throw new Exception("Incorrect pilot settlement: " + outcome);
        }
    }
}
'@) -WarningAction SilentlyContinue
[WingRegistry]::Check()
Write-Output 'Takeover pilot lifecycle checks passed.'
