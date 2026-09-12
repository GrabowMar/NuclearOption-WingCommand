# Run the production saturation controller against a small native-weapon boundary.
$ErrorActionPreference = 'Stop'
$salvo = Get-Content "$PSScriptRoot/../Combat/SplashSalvo.cs" -Raw
$boundary = @'
using System;
using System.Collections.Generic;
namespace WingCommand {
class Unit { public bool disabled; public float Distance=10000; public int Kind; }
class Pilot {}
class Aircraft { public List<WeaponStation> weaponStations=new List<WeaponStation>(); }
class WeaponStation {
    public int Ammo=3, Kind, Shots; public float Range=20000, Interval=1, NextShot;
    public bool Blocked=false, Turret=false, SalvoInProgress=false; public Unit Target;
    public bool HasTurret() => Turret;
    public Unit GetStationTarget() => Target;
}
static class WingWeapons {
    public static float Now;
    public static bool CanDamage(WeaponStation station, Unit target) =>
        station != null && station.Ammo>0 && target != null && !target.disabled && station.Kind==target.Kind;
    public static bool CanReach(Aircraft a, WeaponStation station, Unit target) =>
        CanDamage(station,target) && target.Distance<=station.Range;
    public static bool FireSaturation(Aircraft a,Pilot p,WeaponStation station,Unit target) {
        if (!CanReach(a,station,target) || station.Blocked || station.SalvoInProgress) return false;
        if (station.Turret) { station.Target=target; return true; }
        if (Now<station.NextShot) return false;
        station.Ammo--; station.Shots++; station.Target=target; station.NextShot=Now+station.Interval;
        return true;
    }
}
public static class SplashChecks {
    static void Check(bool ok,string why) { if(!ok) throw new Exception(why); }
    public static void Run() {
        var carrier=new Unit(); var escort=new Unit(); var plane=new Unit { Kind=1 };
        var a=new Aircraft(); var p=new Pilot();
        var fast=new WeaponStation { Interval=0.1f };
        var slow=new WeaponStation { Interval=1f };
        var cooling=new WeaponStation { NextShot=2f };
        var gun=new WeaponStation { Range=1000 };
        var wrong=new WeaponStation { Kind=2 };
        a.weaponStations.AddRange(new[]{fast,slow,cooling,gun,wrong});
        var order=new List<Unit>{carrier,escort,plane}; var salvo=new SplashSalvo();
        salvo.Begin(a,order,0); order.Clear();
        Check(salvo.StationCount==3,"Only matching in-range stores commit, even during cooldown");
        Check(salvo.Tick(a,p,out _,out _),"Salvo remains active");
        Check(fast.Shots==1 && slow.Shots==1 && cooling.Shots==0,"Every ready station fires on receipt");
        Check(fast.Target==carrier && slow.Target==escort,"Stations distribute across designated targets");
        WingWeapons.Now=0.1f; salvo.Tick(a,p,out _,out _);
        Check(fast.Shots==2 && slow.Shots==1,"Native station cadence, no shared delay");
        WingWeapons.Now=0.2f; salvo.Tick(a,p,out _,out _);
        Check(fast.Ammo==0 && slow.Ammo==2,"Fast station drains independently");
        carrier.disabled=true; escort.Distance=500;
        WingWeapons.Now=10f; salvo.Tick(a,p,out _,out _);
        Check(slow.Target==escort && cooling.Target==escort,"After interruption, survivors receive the retained salvo");
        Check(gun.Shots==0,"Closing never adds initially out-of-range guns");
        WingWeapons.Now=11f; salvo.Tick(a,p,out _,out _);
        WingWeapons.Now=12f; salvo.Tick(a,p,out _,out _);
        Check(!salvo.Tick(a,p,out _,out _),"Complete after all committed ammo is spent");
        Check(gun.Ammo==3 && wrong.Ammo==3,"Uncommitted stores remain aboard");

        a.weaponStations.Clear(); var blocked=new WeaponStation{Blocked=true};
        var ready=new WeaponStation(); a.weaponStations.AddRange(new[]{blocked,ready});
        salvo.Begin(a,new[]{escort},0); salvo.Tick(a,p,out _,out _);
        Check(blocked.Shots==0 && ready.Shots==1,"Blocked shot does not suppress another station");
        escort.disabled=true;
        Check(!salvo.Tick(a,p,out _,out _),"Dead designations complete without sweeping unrelated targets");

        carrier.disabled=escort.disabled=false;
        a.weaponStations.Clear(); var t1=new WeaponStation{Turret=true};var t2=new WeaponStation{Turret=true};
        a.weaponStations.AddRange(new[]{t1,t2}); salvo.Begin(a,new[]{carrier,escort},0);
        salvo.Tick(a,p,out _,out _); salvo.Tick(a,p,out _,out _);
        Check(t1.Target==carrier && t2.Target==escort,"Turret locks must not bounce between targets");

        salvo.Begin(a,Array.Empty<Unit>(),0);
        Check(!salvo.Tick(a,p,out _,out _),"Empty target list completes cleanly");
    }
}
}
'@
Add-Type ($boundary + "`n" + ($salvo -replace '^using System.Collections.Generic;',''))
[WingCommand.SplashChecks]::Run()
Write-Host 'Saturation dispatch checks passed.'
