# Run the production member transition methods against a small native-game boundary.
$ErrorActionPreference = 'Stop'
$memberSource = Get-Content "$PSScriptRoot/../Core/WingMember.cs" -Raw
$taskingSource = Get-Content "$PSScriptRoot/../Core/WingMember.Tasking.cs" -Raw
$methods = foreach ($name in @('RequestRefit', 'AbandonRefit', 'CompleteRefit', 'SetDirective', 'TryAdvanceQueue', 'CheckReserves')) {
    $match = [regex]::Match($memberSource, "(?ms)^        (?:public|internal|private) (?:void|bool) $name\(.*?^        }")
    if (!$match.Success) { throw "Missing member method: $name" }
    $match.Value.Replace(
        'if (!taskQueue.Advance(startedRevision, directiveSerial, out WingDirective next)) return false;',
        'WingDirective next; if (!taskQueue.Advance(startedRevision, directiveSerial, out next)) return false;')
}
$apply = [regex]::Match($memberSource, '(?ms)^        public void Apply\(WingDirective directive\).*?^        }').Value
$resume = [regex]::Match($taskingSource, '(?ms)^        private bool CanResumeAfterRefit\(.*?^        }').Value
$stores = [regex]::Match($taskingSource, '(?ms)^        private bool CombatStoresEmpty\s*\{.*?^        }').Value.Replace(
    'Aircraft?.weaponStations == null', 'Aircraft == null || Aircraft.weaponStations == null')
if (!$apply -or !$resume -or !$stores) { throw 'Missing production apply/resume/store transition' }
$pure = foreach ($file in @('Flight/TaskRoute', 'Ai/StandingOrder')) {
    $text = (Get-Content "$PSScriptRoot/../Pure/$file.cs" -Raw) -replace '(?m)^using .*;\r?\n', ''
    $text = $text.Replace('public int Count => legs.Count;', 'public int Count { get { return legs.Count; } }')
    $text = $text.Replace('public T this[int index] => legs[index];', 'public T this[int index] { get { return legs[index]; } }')
    $text = $text.Replace('public void Add(T leg) => legs.Add(leg);', 'public void Add(T leg) { legs.Add(leg); }')
    $text = $text.Replace('public void CancelSuspension() => suspended = null;', 'public void CancelSuspension() { suspended = null; }')
    $text = $text.Replace('next = default;', 'next = default(T);')
    $text = $text.Replace('public IEnumerator<T> GetEnumerator() => legs.GetEnumerator();', 'public IEnumerator<T> GetEnumerator() { return legs.GetEnumerator(); }')
    $text = $text.Replace('IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();', 'IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }')
    $text = $text.Replace('this.sameIntent = sameIntent ?? throw new ArgumentNullException(nameof(sameIntent));',
        'if (sameIntent == null) throw new ArgumentNullException("sameIntent"); this.sameIntent = sameIntent;')
    $text
}
$boundary = @'
using System;
using System.Collections;
using System.Collections.Generic;
namespace WingCommand {
internal enum WingOrder { Formation, ReturnToBase, Maneuver, FallBack, Attack, FireForEffect,
    JamTarget, MoveToPoint, Engage, StandDown, LandHere, DeliverCargo, SeekAndDestroy }
internal struct GlobalPosition { public int Id; }
internal struct Vector3 {
    public static Vector3 forward { get { return new Vector3(); } }
    public static Vector3 operator -(Vector3 v) { return v; }
}
internal class Transform { public Vector3 forward; }
internal class Unit { public bool disabled; public object NetworkHQ; }
internal class Aircraft : Unit {
    public Transform transform;
    public float NetworkfuelLevel;
    public readonly FuelTank tank = new FuelTank();
    public readonly List<WeaponStation> weaponStations = new List<WeaponStation> { new WeaponStation() };
    public FuelTank[] GetFuelTanks() { return new[] { tank }; }
    public float GetFuelLevel() { return tank.Fuel; }
    public void RpcRearm(RearmEventArgs args) {
        for (int i = 0; i < args.Stations.Length; i++) weaponStations[i].Ammo += args.Stations[i];
    }
}
internal class Pilot { public bool dead, ejected; }
internal class FuelTank { public float Fuel; public void Refuel(float v) { Fuel = v; } }
internal class WeaponStation { public int FullAmmo = 4, Ammo; public bool Cargo; public int GetAmmoTotal() { return Ammo; } }
internal class RearmEventArgs { public Aircraft Rearmer; public int[] Stations; }
internal static class Mathf { public static int Max(int a, int b) { return Math.Max(a,b); } }
internal static class Time { public static float timeSinceLevelLoad = 50f; }
internal class Setting { public bool Value = true; }
internal class Settings { public Setting AutoReturnOnEmpty = new Setting(); public float BingoFuel = 0.15f; }
internal static class Plugin { public static Settings Settings = new Settings(); }
internal static class WingTuning { public const float BingoFuel = 0.15f, SplashCriticalFuel = 0.03f; }
internal static class WingComms { public enum Call { Bingo, OutOfAmmo } public static void Say(WingMember m, Call c) {} }
internal static class CombatFacade {
    internal static class Tactical { public static void ReleaseSelection(Aircraft a) {} }
    internal static class Weapons { public static void ClearTurretTargets(Aircraft a) {} }
}
internal static class TacticalMapOverlay { public static void Invalidate() {} }
internal static class PersonnelFacade { internal static class Roster { public static int Sorties; public static void NoteSortie(Aircraft a) { Sorties++; } } }
internal static class WingOrderRules { public static bool CanQueueWhilePending(WingOrder o) { return o != WingOrder.Maneuver; } }
internal static class WingOrderCatalog { public static bool CanApply(WingMember m, WingOrder o) { return m.Alive; } }
internal static class FallBackState { public static GlobalPosition FriendlyLoiterPoint(Aircraft a, Vector3 v) { return default(GlobalPosition); } }
internal class SplashState { public int Prepared; public void Prepare() { Prepared++; } }
internal class Brain { public void RequestEvaluation() {} }
internal struct WingDirective {
    public WingOrder Order; public Unit Target; public GlobalPosition Point;
    public bool HasPoint;
    public static WingDirective Simple(WingOrder o) { return new WingDirective { Order = o }; }
    public static WingDirective AtPoint(WingOrder o, GlobalPosition p) { return new WingDirective { Order = o, Point = p, HasPoint = true }; }
    public bool SameIntentAs(WingDirective other) { return Order == other.Order && Target == other.Target && Point.Id == other.Point.Id; }
}
internal class WingMember {
    internal readonly SplashState splashState = new SplashState();
    private readonly StandingOrder<WingDirective> standingOrder = new StandingOrder<WingDirective>(
        WingDirective.Simple(WingOrder.Formation), (a,b) => a.SameIntentAs(b));
    internal readonly TaskRoute<WingDirective> taskQueue = new TaskRoute<WingDirective>();
    private bool applyKeepsQueue, deliveryPending;
    private float engageActivityAt;
    private readonly Brain brain = new Brain();
    internal bool IsSurface, IsPanicking, AutoRefit;
    private float joinedAt;
    internal float Fuel { get { return Aircraft.GetFuelLevel(); } }
    internal int Ammo { get { int total=0; foreach(var s in Aircraft.weaponStations) if (!s.Cargo) total+=s.Ammo; return total; } }
    internal bool Alive { get { return !Pilot.dead && !Pilot.ejected; } }
    internal bool IsCommandable { get { return Alive && !deliveryPending; } }
    internal bool RefitPending { get; private set; }
    internal Aircraft Aircraft = new Aircraft();
    internal Pilot Pilot = new Pilot();
    internal int Launches;
    internal int directiveSerial { get { return standingOrder.Revision; } }
    internal WingDirective Directive { get { return standingOrder.Current; } }
    internal WingOrder Order { get { return Directive.Order; } }
    internal void Apply(WingOrder order) { Apply(WingDirective.Simple(order)); }
    private void Resolve(bool force) {}
    private void BeginRefitDeparture() { Launches++; deliveryPending = true; }
'@
$checks = @'
}
public static class RefitChecks {
    private static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
    public static void Run() {
        var member = new WingMember();
        var a = WingDirective.AtPoint(WingOrder.MoveToPoint, new GlobalPosition { Id = 1 });
        var b = WingDirective.AtPoint(WingOrder.MoveToPoint, new GlobalPosition { Id = 2 });
        member.Apply(a); member.taskQueue.Add(a); member.taskQueue.Add(b); member.taskQueue.SetRepeat(true);
        int oldRevision = member.directiveSerial;
        member.RequestRefit(); member.RequestRefit();
        Check(member.RefitPending && member.Order == WingOrder.ReturnToBase, "Refit must own RTB");
        Check(member.taskQueue.Count == 0, "Refit route must be suspended, not drawn as active");
        member.CompleteRefit();
        Check(member.Order == WingOrder.MoveToPoint && member.Directive.Point.Id == 1, "Resume current leg");
        Check(member.taskQueue.Count == 2 && member.taskQueue.Repeat, "Resume full patrol loop");
        Check(member.Launches == 1 && !member.RefitPending, "Exactly one relaunch");
        Check(member.Aircraft.NetworkfuelLevel == 1f && member.Aircraft.weaponStations[0].Ammo == 4, "Native resupply");
        Check(!member.TryAdvanceQueue(oldRevision), "Ignore stale pre-refit completion");
        Check(member.taskQueue[0].Point.Id == 1, "Stale callback cannot mutate restored route");
        member.CompleteRefit();
        Check(member.Launches == 1, "Duplicate completion cannot launch twice");

        var replaced = new WingMember();
        replaced.Apply(a); replaced.RequestRefit(); replaced.Apply(b); replaced.CompleteRefit();
        Check(replaced.Directive.Point.Id == 2 && replaced.Launches == 0, "New order wins during refit");

        var rtb = new WingMember();
        rtb.Apply(a); rtb.RequestRefit(); rtb.Apply(WingOrder.ReturnToBase); rtb.CompleteRefit();
        Check(!rtb.RefitPending && rtb.Launches == 0, "Identical RTB explicitly cancels refit");

        var target = new Unit { NetworkHQ = new object() };
        var attack = new WingMember();
        attack.Apply(new WingDirective { Order = WingOrder.Attack, Target = target });
        attack.RequestRefit(); target.disabled = true; attack.CompleteRefit();
        Check(attack.Order == WingOrder.Formation, "Destroyed target falls back to formation");

        var captured = new WingMember();
        target.disabled = false;
        captured.Apply(new WingDirective { Order = WingOrder.Attack, Target = target });
        captured.RequestRefit(); target.NetworkHQ = captured.Aircraft.NetworkHQ; captured.CompleteRefit();
        Check(captured.Order == WingOrder.Formation, "Friendly target must not resume");

        var dry = new WingMember { AutoRefit=true };
        dry.Aircraft.tank.Fuel=1f; dry.Apply(a); dry.CheckReserves();
        Check(dry.RefitPending, "Empty fitted combat stores trigger refit during a route");
        var unarmed = new WingMember { AutoRefit=true };
        unarmed.Aircraft.tank.Fuel=1f; unarmed.Aircraft.weaponStations.Clear(); unarmed.Apply(a); unarmed.CheckReserves();
        Check(!unarmed.RefitPending && unarmed.Order==WingOrder.MoveToPoint, "Unarmed route must not refit forever");
        unarmed.Aircraft.tank.Fuel=0.1f; unarmed.CheckReserves();
        Check(unarmed.RefitPending, "Bingo refits an unarmed route");
        var cargo = new WingMember { AutoRefit=true };
        cargo.Apply(WingOrder.DeliverCargo); cargo.CheckReserves();
        Check(!cargo.RefitPending && cargo.Order==WingOrder.DeliverCargo, "Deliberate cargo task takes priority");
        var splash = new WingMember { AutoRefit=true };
        splash.Aircraft.tank.Fuel=0.1f;
        splash.Apply(new WingDirective { Order=WingOrder.FireForEffect, Target=target });
        splash.CheckReserves();
        Check(splash.splashState.Prepared==1, "Snapshot saturation stores when an airborne order is received");
        Check(!splash.RefitPending && splash.Order==WingOrder.FireForEffect, "Routine bingo and empty-store refit cannot interrupt saturation");
        splash.Aircraft.tank.Fuel=0.03f; splash.CheckReserves();
        Check(splash.Order==WingOrder.ReturnToBase, "Critical fuel can interrupt saturation");

        var disabled = new WingMember { AutoRefit=true };
        disabled.Apply(a); Plugin.Settings.AutoReturnOnEmpty.Value=false; disabled.CheckReserves();
        Check(!disabled.RefitPending && disabled.Order==WingOrder.MoveToPoint, "Respect global automatic-return switch");
    }
}
}
'@
$definition = @($boundary, ($methods -join "`n"), $apply, $resume, $stores, $checks, ($pure -join "`n")) -join "`n"
Add-Type -TypeDefinition $definition -IgnoreWarnings -WarningAction SilentlyContinue
[WingCommand.RefitChecks]::Run()
Write-Output 'Refit resume lifecycle checks passed.'
