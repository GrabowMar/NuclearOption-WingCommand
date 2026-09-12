# Exercise the production confirmation and aircraft-action guards without Unity.
$ErrorActionPreference = 'Stop'
$screen = Get-Content "$PSScriptRoot/../Ui/WmcScreen.cs" -Raw
$tactical = Get-Content "$PSScriptRoot/../Ui/WmcScreen.Tactical.cs" -Raw
$orders = Get-Content "$PSScriptRoot/../Core/WingCommandManager.Orders.cs" -Raw
function Extract([string]$source, [string]$pattern) {
    $match = [regex]::Match($source, $pattern)
    if (!$match.Success) { throw "Production method not found: $pattern" }
    $match.Value
}
$confirmation = Extract $screen '(?ms)^        private sealed class Confirmation.*?^        }'
$confirmEject = Extract $tactical '(?ms)^            private void ConfirmEject\(\).*?^            }'
$guard = Extract $orders '(?ms)^        internal bool CanControlAircraft\(.*?;'
$actions = foreach ($name in @('ToggleMemberRadar', 'EjectMember')) {
    Extract $orders "(?ms)^        internal void $name\(.*?^        }"
}
$boundary = @'
using System;
using System.Linq;
using System.Collections.Generic;
static class Time { public static float unscaledTime; }
class Radar { public bool activated; }
class Aircraft {
    public bool LocalSim=true, IsServer=true, Ejected;
    public object Player;
    public Radar radar=new Radar();
    public int Ejections, Toggles;
    public bool HasEjected() { return Ejected; }
    public void CmdToggleRadar() { if (IsServer) throw new Exception("AI has no player RPC authority"); Toggles++; radar.activated=!radar.activated; }
    public void UserCode_CmdToggleRadar_1821461427() { Toggles++; radar.activated=!radar.activated; }
    public void StartEjectionSequence() { Ejections++; Ejected=true; }
}
class WingMember {
    public bool IsCommandable=true, IsSurface, Abandoned;
    public string Name="TEST";
    public Aircraft Aircraft=new Aircraft();
    public void AbandonRefit() { Abandoned=true; }
}
class Registry { public IReadOnlyList<WingMember> Members; }
class WingButton {
    public string Text;
    public bool Latched;
    public void SetText(string text) { Text=text; }
    public void SetLatched(bool value) { Latched=value; }
}
class WingCommandManager {
    public static WingCommandManager Instance;
    public Registry Wing=new Registry();
    public void Toast(string message) {}
'@
$checks = @'
    WingMember bound;
    readonly Confirmation ejection=new Confirmation();
    readonly WingButton eject=new WingButton();
    static void Check(bool pass,string message) { if (!pass) throw new Exception(message); }
    public static void Run() {
        var member=new WingMember();
        var manager=new WingCommandManager();
        WingCommandManager.Instance=manager;
        manager.Wing.Members=new List<WingMember>{member};
        var row=new TacticalAircraftSafetyChecks { bound=member };
        row.ConfirmEject();
        Check(member.Aircraft.Ejections==0 && row.eject.Text=="EJ?", "First press only arms");
        Time.unscaledTime=4;
        row.ConfirmEject();
        Check(member.Aircraft.Ejections==0, "Expired confirmation rearms without ejecting");
        row.ConfirmEject();
        Check(member.Aircraft.Ejections==1 && member.Abandoned && row.eject.Text=="EJ", "Second timely press ejects and abandons refit");
        row.ConfirmEject();
        Check(member.Aircraft.Ejections==1, "Already ejected aircraft cannot eject twice");
        member.Aircraft.Ejected=false;
        member.IsSurface=true;
        row.ConfirmEject(); manager.EjectMember(member);
        Check(member.Aircraft.Ejections==1 && !row.ejection.IsArmedFor(member), "Surface units cannot arm or eject");
        member.IsSurface=false;
        member.Aircraft.Player=new object();
        Check(!manager.CanControlAircraft(member), "Player aircraft rejected");
        member.Aircraft.Player=null; member.Aircraft.LocalSim=false;
        Check(!manager.CanControlAircraft(member), "Nonlocal aircraft rejected");
        member.Aircraft.LocalSim=true; member.IsCommandable=false;
        Check(!manager.CanControlAircraft(member), "Uncommandable aircraft rejected");
        member.IsCommandable=true;
        Check(!manager.CanControlAircraft(null) && !manager.CanControlAircraft(new WingMember()), "Null and non-wing aircraft rejected");
        manager.ToggleMemberRadar(member); manager.ToggleMemberRadar(member);
        Check(member.Aircraft.Toggles==2 && !member.Aircraft.radar.activated, "Radar toggles on and off");
        member.Aircraft.radar=null; manager.ToggleMemberRadar(member);
        Check(member.Aircraft.Toggles==2, "Aircraft without radar ignored");
        row.ejection.Arm(member);
        var replacement=new WingMember();
        manager.Wing.Members=new List<WingMember>{replacement}; row.bound=replacement;
        row.ConfirmEject();
        Check(replacement.Aircraft.Ejections==0, "Confirmation never transfers to another aircraft");
        WingCommandManager.Instance=null; row.ConfirmEject();
        Check(!row.ejection.IsArmedFor(replacement), "Missing manager disarms confirmation");
    }
}
'@
Add-Type -TypeDefinition ($boundary + $guard + ($actions -join "`n") + "`n}`npublic class TacticalAircraftSafetyChecks {`n" + $confirmation + $confirmEject + $checks) -IgnoreWarnings -WarningAction SilentlyContinue
[TacticalAircraftSafetyChecks]::Run()
Write-Output 'Tactical aircraft safety checks passed.'
