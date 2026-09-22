# Exercise the production dispatch pass with a small native-game boundary.
$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../Economy/WingShopDelivery.cs" -Raw
$dispatch = [regex]::Match($source, '(?ms)^        private static void DispatchPending\(\).*?^        }').Value
if (!$dispatch) { throw 'Missing production dispatch pass' }
$boundary = @'
using System;
using System.Collections.Generic;
#pragma warning disable 0649
namespace DepartureDispatchCheck {
class Airbase { public bool Free = true, Blocked; }
class AircraftDefinition {}
class Transaction { public AircraftDefinition Definition = new AircraftDefinition(); public object Hq; }
class PendingDelivery {
    public bool Claimed, Starting, Pinned = true;
    public Airbase Origin;
    public Transaction Transaction = new Transaction();
}
struct Vector3 { public static Vector3 zero; }
class Transform { public Vector3 position; }
class Aircraft { public Transform transform = new Transform(); }
class Wing { public Aircraft Leader; }
class WingCommandManager { public static WingCommandManager Instance; public Wing Wing; }
static class Time { public static float unscaledTime; }
static class WingTuning { public const float HangarRetryInterval = .5f; }
enum HangarLaunchMode { Any }
public static class Check {
    static readonly List<PendingDelivery> pending = new List<PendingDelivery>();
    static readonly List<PendingDelivery> attempts = new List<PendingDelivery>();
    static readonly List<Airbase> fields = new List<Airbase>();
    static readonly List<Airbase> fieldScratch = new List<Airbase>();
    static readonly HashSet<Airbase> dispatchedFields = new HashSet<Airbase>();
    static float nextDispatchAt;
    static void CollectFields(object hq) { fieldScratch.Clear(); fieldScratch.AddRange(fields); }
    static bool CanLaunchNow(Airbase field, AircraftDefinition definition) { return field.Free; }
    static int SelectOrigin(AircraftDefinition definition, Vector3 from, HangarLaunchMode mode) {
        return fieldScratch.FindIndex(f => f.Free);
    }
    static void Attempt(PendingDelivery order) {
        attempts.Add(order);
        if (order.Origin.Blocked) return;
        order.Origin.Free = false;
        order.Claimed = true;
        pending.Remove(order);
    }
    static void Require(bool pass, string message) { if (!pass) throw new Exception(message); }
    static PendingDelivery Add(Airbase field, bool pinned = true) {
        var order = new PendingDelivery { Origin = field, Pinned = pinned };
        pending.Add(order); return order;
    }
    static void Reset() {
        pending.Clear(); attempts.Clear(); fields.Clear(); dispatchedFields.Clear();
        nextDispatchAt = 0; Time.unscaledTime = 0;
    }
    public static void Run() {
        Reset();
        var a = new Airbase { Blocked = true }; var b = new Airbase();
        var first = Add(a); var second = Add(a); var independent = Add(b);
        DispatchPending();
        Require(attempts.Count == 2 && attempts[0] == first && attempts[1] == independent,
            "One attempt per field, oldest first, without blocking another field");
        Add(a); Time.unscaledTime = .25f; DispatchPending();
        Require(attempts.Count == 2, "New arrivals cannot bypass retry cadence");
        a.Blocked = false; Time.unscaledTime = .5f; DispatchPending();
        Require(attempts.Count == 3 && attempts[2] == first && pending.Contains(second),
            "The oldest held order launches first when the runway clears");

        Reset(); a = new Airbase(); b = new Airbase();
        first = Add(a); second = Add(b); DispatchPending();
        Require(attempts.Count == 2 && attempts[1] == second && pending.Count == 0,
            "Synchronous removal must not skip the shifted order");

        Reset(); a = new Airbase { Blocked = true }; b = new Airbase();
        fields.Add(a); fields.Add(b);
        first = Add(null, false); second = Add(null, false); DispatchPending();
        Require(attempts.Count == 2 && second.Origin == b && first.Origin == null,
            "Any-field orders spread over eligible fields and release failed selections");

        Reset(); a = new Airbase { Free = false }; b = new Airbase();
        Add(a); second = Add(b); DispatchPending();
        Require(attempts.Count == 1 && attempts[0] == second, "Occupied fields must not spawn");
    }
'@
Add-Type -TypeDefinition ($boundary + "`n" + $dispatch + "`n}}") -WarningAction SilentlyContinue
[DepartureDispatchCheck.Check]::Run()
Write-Output 'Departure dispatch checks passed.'
