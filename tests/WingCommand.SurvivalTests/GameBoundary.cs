using System;
using System.Collections.Generic;
using System.Reflection;

// Only the Unity/game boundary is faked; production roster and survival methods are linked unchanged.
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object value) { if (!ReferenceEquals(value, null)) value.Destroyed = true; }
        public static bool operator ==(Object a, Object b) =>
            (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed) || ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 right => new Vector3(1, 0, 0);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized => sqrMagnitude == 0 ? zero : this * (1f / MathF.Sqrt(sqrMagnitude));
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
    }
    public class Transform { public Vector3 position; }
    public class Rigidbody { public Vector3 velocity; }
    public static class Mathf
    {
        public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
    }
    public static class Time { public static float timeSinceLevelLoad; }
    public static class Random
    {
        public static float Next;
        public static int Rolls;
        public static float value { get { Rolls++; return Next; } }
        public static int Range(int min, int max) => min;
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public class HarmonyPatch : Attribute { public HarmonyPatch() {} public HarmonyPatch(Type type, string method) {} }
    public class HarmonyPrefix : Attribute {}
    public class HarmonyPostfix : Attribute {}
    public class HarmonyFinalizer : Attribute {}
    public static class AccessTools
    {
        public static MethodInfo FirstMethod(Type type, Func<MethodInfo, bool> predicate) => null;
    }
}
public readonly struct PersistentID
{
    public readonly int Value;
    public PersistentID(int value) { Value = value; }
    public static implicit operator PersistentID(int value) => new PersistentID(value);
}
public struct GlobalPosition
{
    public UnityEngine.Vector3 Value;
    public static implicit operator GlobalPosition(UnityEngine.Vector3 v) => new GlobalPosition { Value = v };
    public static GlobalPosition operator +(GlobalPosition a, UnityEngine.Vector3 b) => a.Value + b;
    public static UnityEngine.Vector3 operator -(GlobalPosition a, GlobalPosition b) => a.Value - b.Value;
}
public class Unit : UnityEngine.Object
{
    public enum UnitState { Active, Returned, Destroyed }
    public UnitState unitState;
    public UnitState NetworkunitState { get => unitState; set => unitState = value; }
    public bool IsServer = true, LocalSim = true, disabled;
    public bool Networkdisabled { get => disabled; set => disabled = value; }
    public PersistentID persistentID;
    public FactionHQ NetworkHQ;
    public AircraftDefinition definition = new AircraftDefinition();
    public string unitName = "test aircraft";
    public float radarAlt, speed;
    public UnityEngine.Transform transform = new UnityEngine.Transform();
    public UnityEngine.Object gameObject => this;
    public GlobalPosition GlobalPosition() => transform.position;
}
public class Aircraft : Unit {
    public Pilot Pilot;
    public bool Rotary = true, AtHome;
    public UnityEngine.Rigidbody rb = new UnityEngine.Rigidbody();
    public MissileWarning Warning = new MissileWarning();
    public MissileWarning GetMissileWarningSystem() => Warning;
}
public class MissileWarning { public bool Active; public bool IsWarning() => Active; }
public class AircraftDefinition { public string unitName = "helo"; public int captureCapacity = 1; }
public class FactionHQ {}
public class Pilot
{
    public Aircraft aircraft;
    public bool dead, ejected;
    public void ApplyDamage() {}
    public void TakeGForceDamage() {}
}
public class FuelTank { public void UseFuel() {} }
public class Missile : Unit { public PersistentID targetID; public string Seeker = "IR"; public string GetSeekerType() => Seeker; public void SetAimpoint() {} }
public class Countermeasure { public Aircraft aircraft; }
public class ChaffEjector : Countermeasure { public void Fire() {} }
public class FlareEjector : Countermeasure { public void Fire() {} }
public class RadarJammer : Countermeasure { public void Fire() {} }
public class MessageManager {}
public class PilotDismounted : Unit
{
    public enum PilotState { ejecting, landing, dead }
    public PilotState animationState;
    public PersistentID parentUnit;
    public byte pilotNumber;
    public bool Slung;
    public bool IsSlung() => Slung;
    public void UnitDisabled() {}
    public void SetPilotState() {}
    public void Capture() {}
    public void TakeDamage() {}
}
public static class Datum { public static float LocalSeaY; }
public static class UnitRegistry
{
    public static readonly Dictionary<PersistentID, Unit> Units = new Dictionary<PersistentID, Unit>();
    public static bool TryGetUnit(PersistentID id, out Unit unit) => Units.TryGetValue(id, out unit);
    public static bool TryGetPersistentUnit(PersistentID id, out Unit unit) => TryGetUnit(id, out unit);
}
namespace WingCommand
{
    internal enum WingLoadoutChoice { Standard }
    internal static class EconomyFacade { internal static class Shop { public static bool IsPurchased(Aircraft a) => false; } }
    internal static class WingRecovery { public static bool IsHome(Aircraft a) => a != null && !a.disabled && a.AtHome; }
    internal enum ChatterPersona { Calm }
    internal enum WingOrder { Formation, OrbitHere, LandHere, Attack }
    internal class WingDirective
    {
        public WingOrder Order;
        public GlobalPosition Point;
        public static WingDirective AtPoint(WingOrder order, GlobalPosition point) => new WingDirective { Order = order, Point = point };
    }
    internal class WingMember
    {
        public Aircraft Aircraft;
        public string Name = "Rescuer";
        public bool IsCommandable = true, IsPanicking;
        public float Fuel = 1f;
        public WingOrder Order;
        public bool LoadoutKnown;
        public WingLoadoutChoice Loadout;
        public void Apply(WingDirective directive) { Order = directive.Order; }
    }
    internal class WingRegistry
    {
        public readonly List<WingMember> Members = new List<WingMember>();
        public static bool IsRotary(Aircraft a) => a.Rotary;
        public static Pilot PrimaryPilot(Aircraft a) => a == null ? null : a.Pilot;
        public WingMember Find(Aircraft a) => Members.Find(m => m.Aircraft == a);
    }
    internal class WingCommandManager
    {
        public static WingCommandManager Instance = new WingCommandManager();
        public WingRegistry Wing = new WingRegistry();
        public List<string> Messages = new List<string>();
        public void Toast(string message) => Messages.Add(message);
    }
    internal class Setting<T> { public T Value; public Setting(T value) { Value = value; } }
    internal class Config
    {
        public Setting<bool> PilotProgression = new Setting<bool>(true), VerboseLogging = new Setting<bool>(false);
        public Setting<float> RankEffect = new Setting<float>(1f);
    }
    internal class Log { public void LogWarning(string message) {} }
    internal static class Plugin
    {
        public static Config Settings = new Config();
        public static Log Logger = new Log();
        public static void LogVerbose(string message) {}
    }
    internal static class PilotPortrait { public static void Reset() {} }
    internal static class PilotIdentity
    {
        public static string Callsign(Func<int, int> random, Func<string, bool> exists) => Guid.NewGuid().ToString();
        public static string Name(Func<int, int> random) => "Pilot";
        public static string Background(Func<int, int> random, ChatterPersona persona) => "";
    }
    internal class CustomPilotRecord
    {
        public string Name, Callsign, ResolvedDialogueTag, Background;
        public ChatterPersona Persona;
        public int Xp, Kills, Sorties;
    }
    internal static class WingTuning
    {
        public const int XpPerRank = 120, XpPerKill = 25, XpPerSortie = 40, XpPerEngagement = 10;
        public const float RankEffect = 1f;
    }
    internal static class PilotSelectionPolicy
    {
        public static int NextIndex(int start, int count, Func<int, bool> free)
        {
            for (int i = 1; i <= count; i++) if (free((start + i) % count)) return (start + i) % count;
            return count == 0 ? -1 : (start + 1) % count;
        }
    }
    internal static class WingComms
    {
        public enum Call { Splash }
        public static void Say(WingMember member, Call call, string name) {}
    }
}
namespace NuclearOption.Networking { }
