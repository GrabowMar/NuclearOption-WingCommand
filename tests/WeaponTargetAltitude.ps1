# Execute the shared production shot envelope across weapon categories and AGM-99's asset values.
$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../Combat/WingWeapons.cs" -Raw
$method = [regex]::Match($source, '(?ms)^        private static bool ShotIsValid\(.*?^        }').Value
if (!$method) { throw 'Production shot envelope not found' }
$boundary = @'
using System;
namespace WingCommand {
struct Vector3 {
    public float x,y,z;
    public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
    public float Magnitude => (float)Math.Sqrt(x*x+y*y+z*z);
    public Vector3 normalized { get { float m=Magnitude;return m==0?this:new Vector3(x/m,y/m,z/m); } }
    public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    public static float Dot(Vector3 a,Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
    public static float Angle(Vector3 a,Vector3 b) => (float)(Math.Acos(Math.Clamp(Dot(a.normalized,b.normalized),-1,1))*180/Math.PI);
}
class Body { public Vector3 velocity=new Vector3(); }
class Transform { public Vector3 forward=new Vector3(1,0,0); }
class Unit { public float radarAlt; public Body rb=null; public Vector3 position; public Vector3 GlobalPosition()=>position; }
class Aircraft:Unit { public Transform transform=new Transform(); }
class TargetRequirements { public float minRange=1000,maxRange=100000,minAltitude=0,maxAltitude=50,minAlignment=30; }
class WeaponInfo { public bool gun=false,missile=true,bomb=false,glideBomb=false; public TargetRequirements targetRequirements=new TargetRequirements(); }
class WeaponStation { public WeaponInfo WeaponInfo=new WeaponInfo(); }
static class FastMath { public static float Distance(Vector3 a,Vector3 b)=>(a-b).Magnitude; }
enum PilotPerk { Marksman,ApexHunter,HeadOnJoust,Snapshot }
static class PersonnelFacade { public static class Roster {
    public static float EnvelopeScale(Aircraft a)=>1;
    public static bool HasPerk(Aircraft a,PilotPerk p)=>false;
} }
static class PilotPerks {
    public static float GunRangeMultiplier(bool p)=>1;
    public static float HighAltitudeRangeMultiplier(float h,bool p)=>1;
    public static bool IsHeadOn(float closing,float dot)=>false;
    public static float HeadOnRangeMultiplier(bool p)=>1;
    public static float OffBoresightMultiplier(bool p)=>1;
}
public static class WeaponAltitudeChecks {
    static void Check(bool pass,string why) { if(!pass) throw new Exception(why); }
    public static void Run() {
        // Every branch of the shared envelope must apply altitude limits to the target,
        // regardless of weapon category, launcher height, or the configured ceiling.
        foreach (string category in new[]{"missile","gun","bomb","glideBomb","other"})
        foreach (float ceiling in new[]{0f,50f,20000f}) {
            var launcher=new Aircraft { radarAlt=50000,position=new Vector3() };
            var target=new Unit { radarAlt=ceiling,position=new Vector3(50000,0,0) };
            var weapon=new WeaponStation();
            var info=weapon.WeaponInfo;
            info.missile=category=="missile"; info.gun=category=="gun";
            info.bomb=category=="bomb"; info.glideBomb=category=="glideBomb";
            info.targetRequirements.maxAltitude=ceiling;
            Check(ShotIsValid(launcher,weapon,target),category+": target at ceiling must be valid despite launcher altitude");
            target.radarAlt=ceiling+1;
            Check(!ShotIsValid(launcher,weapon,target),category+": target above ceiling must be rejected");
            launcher.radarAlt=0;
            Check(!ShotIsValid(launcher,weapon,target),category+": lowering launcher cannot make an invalid target valid");
            info.targetRequirements.maxAltitude=20000;
            info.targetRequirements.minAltitude=100;
            target.radarAlt=50;
            Check(ShotIsValid(launcher,weapon,target),category+": distance-scaled target floor is inclusive");
            target.radarAlt=49;
            Check(!ShotIsValid(launcher,weapon,target),category+": target below scaled floor must be rejected");
        }
        var aircraft=new Aircraft { radarAlt=1000,position=new Vector3(0,1000,0) };
        var carrier=new Unit { radarAlt=0,position=new Vector3(20000,0,0) };
        var station=new WeaponStation();
        Check(ShotIsValid(aircraft,station,carrier),"AGM-99 must launch from 1000 m against a sea-level carrier");
        aircraft.radarAlt=5000;
        Check(ShotIsValid(aircraft,station,carrier),"Launch altitude is independent of target altitude limits");
        carrier.radarAlt=51;
        Check(!ShotIsValid(aircraft,station,carrier),"Reject a target above AGM-99's 50 m target ceiling");
        carrier.radarAlt=0;carrier.position=new Vector3(101000,0,0);
        Check(!ShotIsValid(aircraft,station,carrier),"Maximum range still enforced");
        aircraft.position=new Vector3();carrier.position=new Vector3(999,0,0);
        Check(!ShotIsValid(aircraft,station,carrier),"Minimum range still enforced");
        carrier.position=new Vector3(-20000,0,0);
        Check(!ShotIsValid(aircraft,station,carrier),"Alignment still enforced");
        carrier.position=new Vector3(50000,0,0);carrier.radarAlt=50;
        station.WeaponInfo.targetRequirements.minAltitude=100;
        station.WeaponInfo.targetRequirements.maxAltitude=10000;
        Check(ShotIsValid(aircraft,station,carrier),"Native target floor scales with distance/maxRange");
        carrier.radarAlt=49;
        Check(!ShotIsValid(aircraft,station,carrier),"Reject a target below the distance-scaled floor");
    }
'@
Add-Type ($boundary + "`n" + $method + "`n} }")
[WingCommand.WeaponAltitudeChecks]::Run()
Write-Host 'Weapon target-altitude regression checks passed.'
