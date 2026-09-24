using System;
using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Helicopters land here and take off (spec M4 §5): each rotary member flying with the wing settles at its
    /// slot's ground point around the anchor and waits; Take Off or Form Up lifts it off to rejoin.</summary>
    internal sealed partial class WingService
    {
        /// <summary>Settles every rotary member flying with the wing. Returns how many; <paramref name="refusal"/> says why
        /// none did.</summary>
        public int LandHere(out string refusal, Func<WingMember, bool> who = null) => LandHere(false, out refusal, who);

        /// <summary>Spec M4 §7.2: every helicopter carrying cargo lands at its slot's ground point, deploys it and lifts off.</summary>
        public int DeliverCargo(out string refusal, Func<WingMember, bool> who = null) => LandHere(true, out refusal, who);

        /// <summary>Whether an order that lands (or delivers cargo) has a helicopter to do it among <paramref name="who"/>.</summary>
        public bool CanSettle(Func<WingMember, bool> who, bool cargo, out string reason)
        {
            reason = null;
            foreach (WingMember m in Members)
            {
                if (who != null && !who(m)) continue;
                if (m.Profile.Class == AirframeClass.FixedWing || !m.Alive || m.Released) continue;
                if (cargo && CargoStation(m.Aircraft) == null) continue;
                return true;
            }
            reason = cargo ? "no helicopter carries cargo" : "no helicopters to land";
            return false;
        }

        private int LandHere(bool cargoOnly, out string refusal, Func<WingMember, bool> who)
        {
            refusal = null;
            if (Wing == null || Wing.Frame == null)
            {
                refusal = "no formation to land from";
                return 0;
            }
            int rotary = 0, free = 0, n = 0;
            foreach (WingMember m in Members)
            {
                if (who != null && !who(m)) continue;
                if (m.Profile.Class == AirframeClass.FixedWing || (cargoOnly && CargoStation(m.Aircraft) == null)) continue;
                rotary++;
                if (m.Released || !m.Alive || m.Engaged || m.Recovery != null || m.OnGround || m.Settle != null) continue;
                WingFrame f = FrameOf(m);
                if (f == null || m.Brain.Slot < 0 || m.Brain.Slot >= f.Count) continue;
                free++;
                Vec3 slot = f.Slots[m.Brain.Slot].Ref.Pos;
                // Dry, level ground only (review M4c I5): no settle onto water or a slope.
                if (!TerrainProbe.Landing(slot, out float groundY, out float normalY) || !SettlePilot.Landable(true, normalY)) continue;
                EndDefence(m);   // review M4c I6: no countermeasure trigger held through the landing
                m.Settle = new SettlePilot(new Vec3(slot.X, groundY, slot.Z), Vec3.HeadingDeg(m.Last.Fwd), missionTime);
                m.Job = cargoOnly ? new SettleJob(SettleTask.Cargo) : null;
                n++;
            }
            if (rotary == 0) refusal = cargoOnly ? "no helicopter carries cargo" : "no helicopters in the wing";
            else if (free == 0) refusal = "no helicopter free to land";
            else if (n == 0) refusal = "no dry, level ground here";
            Plugin.Logger.LogInfo($"[Wing] {(cargoOnly ? "deliver cargo" : "land here")}: {n} of {rotary} helicopters{(refusal != null ? " (" + refusal + ")" : "")}");
            return n;
        }

        public static float RescueStandoff = 60f, RescueMinFuel = 0.25f, SettleSeparation = 40f, RescueApproachMargin = 90f;
        /// <summary>Cargo deployed this mission (automation reads it).</summary>
        public int CargoDeployed { get; private set; }
        private readonly List<Unit> downed = new List<Unit>();

        /// <summary>Spec M4 §7.1: the nearest wing helicopter able to rescue lands next to the downed wing pilot nearest the
        /// player and waits for the game to take them aboard. Returns the member sent, or null with the reason.</summary>
        public WingMember Rescue(out string result)
        {
            downed.Clear();
            WingSearchAndRescue.CollectDowned(downed);
            Vec3 from = Player != null ? Player.GlobalPosition().ToVec3() : Vec3.Zero;
            Unit survivor = null;
            WingMember already = null;
            float best = float.MaxValue;
            foreach (Unit u in downed)
            {
                if (!(u is PilotDismounted p) || p.disabled || p.IsSlung() || p.radarAlt > 3f || p.transform.position.GlobalY() < 0.5f) continue;
                // One helicopter per survivor (review M4c-2 C1).
                WingMember going = RescuerOf(u);
                if (going != null)
                {
                    already = going;
                    continue;
                }
                float d = (u.GlobalPosition().ToVec3() - from).SqrLength;
                if (d >= best) continue;
                best = d;
                survivor = u;
            }
            if (survivor == null)
            {
                result = already != null ? $"#{already.Number} is already on the way" : "no downed wing pilot on land";
                return null;
            }
            Vec3 at = survivor.GlobalPosition().ToVec3();
            WingMember heli = null;
            best = float.MaxValue;
            foreach (WingMember m in Members)
            {
                if (m.Profile.Class == AirframeClass.FixedWing || m.Released || !m.Alive || m.Engaged || m.Recovery != null ||
                    m.OnGround || m.Settle != null || m.Aircraft.definition == null || m.Aircraft.definition.captureCapacity <= 0 ||
                    m.Aircraft.GetFuelLevel() <= RescueMinFuel) continue;
                float d = (m.Last.Pos - at).SqrLength;
                if (d >= best) continue;
                best = d;
                heli = m;
            }
            if (heli == null)
            {
                result = "no helicopter able to rescue";
                return null;
            }
            // 60 m short of the survivor on the helicopter's side, else the other three sides.
            Vec3 toward = (heli.Last.Pos - at).Horizontal;
            toward = toward.SqrLength > 1f ? toward.Normalized : Vec3.Forward;
            for (int side = 0; side < 4; side++)
            {
                Vec3 dir = side == 0 ? toward : side == 1 ? new Vec3(toward.Z, 0f, -toward.X) : side == 2 ? -toward : new Vec3(-toward.Z, 0f, toward.X);
                Vec3 point = at + dir * RescueStandoff;
                if (!TerrainProbe.Landing(point, out float groundY, out float normalY) || !SettlePilot.Landable(true, normalY)) continue;
                if (NearAnotherSettle(heli, point)) continue;
                EndDefence(heli);
                heli.Settle = new SettlePilot(new Vec3(point.X, groundY, point.Z), Vec3.HeadingDeg(at - point), missionTime)
                {
                    // A far survivor gets the time to fly there (review M4c-2 I1).
                    ApproachLimit = System.Math.Max(SettlePilot.ApproachSeconds,
                        (heli.Last.Pos - point).Horizontal.Length / System.Math.Max(heli.Profile.CruiseSpeed, 20f) * 1.5f + RescueApproachMargin),
                };
                heli.Job = new SettleJob(SettleTask.Rescue);
                heli.RescueTarget = survivor;
                WingPilot pilot = WingSearchAndRescue.PilotOf(survivor as PilotDismounted);
                result = $"#{heli.Number} going for {(pilot != null ? pilot.Callsign : "the downed pilot")}";
                Plugin.Logger.LogInfo($"[Wing] rescue: {result}");
                return heli;
            }
            result = "no dry, level ground near them";
            return null;
        }

        /// <summary>The member on a rescue of this survivor, or null.</summary>
        private WingMember RescuerOf(Unit survivor)
        {
            foreach (WingMember m in Members)
                if (m.Settle != null && m.Settle.Phase != SettlePhase.Done && m.Job != null && m.Job.Task == SettleTask.Rescue &&
                    ReferenceEquals(m.RescueTarget, survivor)) return m;
            return null;
        }

        /// <summary>Another member already landing within <see cref="SettleSeparation"/> of the point (review M4c-2 C1).</summary>
        private bool NearAnotherSettle(WingMember self, Vec3 point)
        {
            foreach (WingMember m in Members)
                if (!ReferenceEquals(m, self) && m.Settle != null && m.Settle.Phase != SettlePhase.Done &&
                    (m.Settle.Point - point).Horizontal.Length < SettleSeparation) return true;
            return false;
        }

        /// <summary>The station carrying cargo (troops, vehicles, supplies), or null.</summary>
        private static WeaponStation CargoStation(Aircraft a)
        {
            if (a == null || a.weaponStations == null) return null;
            foreach (WeaponStation w in a.weaponStations)
                if (w.WeaponInfo != null && w.WeaponInfo.cargo && w.Ammo > 0) return w;
            return null;
        }

        /// <summary>The job's actions for a settled member once down (spec M4 §7.3).</summary>
        private void StepJob(WingMember m, SettlePilot s, float dt)
        {
            SettleJob job = m.Job;
            if (job == null) return;
            Unit t = m.RescueTarget;
            // Taken aboard (the game destroys it), dead, or on another helicopter's sling (review M4c-2 I2).
            bool rescued = job.Task == SettleTask.Rescue && (t == null || t.disabled || t.IsSlung());
            SettleAction act = job.Step(s.Phase, dt, rescued);
            if ((act & SettleAction.FireCargo) != 0)
            {
                // The game's own deploy (native transport state): the cargo station selected, then the pilot's fire.
                WeaponStation cargo = CargoStation(m.Aircraft);
                if (cargo != null && m.Pilot != null)
                {
                    WeaponManager wm = m.Aircraft.weaponManager;
                    wm.currentWeaponStation = cargo;
                    wm.ClearTargetList();
                    int before = cargo.Ammo;
                    m.Pilot.Fire();
                    // Counted only when something left (the game's fire returns silently when it cannot: review M4c-2 I3).
                    if (cargo.Ammo < before)
                    {
                        CargoDeployed++;
                        Plugin.Logger.LogInfo($"[Wing] #{m.Number} cargo deployed");
                    }
                    else Plugin.Logger.LogInfo($"[Wing] #{m.Number} cargo did not deploy");
                }
            }
            if ((act & SettleAction.TakeOff) != 0)
            {
                if (job.Task == SettleTask.Rescue)
                    Plugin.Logger.LogInfo($"[Wing] #{m.Number} lifting off from the rescue ({(rescued ? "aboard" : "no pickup")})");
                s.TakeOff();
            }
        }

        /// <summary>Every settled member lifts off. Returns how many.</summary>
        public int TakeOff(Func<WingMember, bool> who = null)
        {
            int n = 0;
            foreach (WingMember m in Members)
                if (m.Settle != null && m.Settle.Phase != SettlePhase.Done && (who == null || who(m)))
                {
                    m.Settle.TakeOff();
                    n++;
                }
            return n;
        }

        /// <summary>The wing as snapshot entries (spec M6 §8). Returns how many were written.</summary>
        public int FillSnapshot(SnapshotMember[] into)
        {
            int n = 0;
            foreach (WingMember m in Members)
            {
                if (n >= into.Length || m.Released || !m.Alive) continue;
                MemberDuty duty = m.Engaged ? MemberDuty.Engaged
                    : m.Recovery != null ? MemberDuty.Recovering
                    : m.Brain.Mind.Current == BehaviourId.Defend ? MemberDuty.Defending
                    : m.Settle != null ? MemberDuty.Settled
                    : m.OnGround ? MemberDuty.Grounded
                    : MemberDuty.Formation;
                float ammo = AmmoFraction(m.Aircraft);
                into[n++] = SnapshotBuilder.Member(m.Aircraft.persistentID.Id, m.Seat, (byte)m.Brain.Mind.Current, duty,
                    m.Aircraft.GetFuelLevel(), ammo, m.Brain.LastRejoin.FallingBehind, m.Bingo.Bingo, m.Bingo.Joker, ammo <= 0f, ElementOf(m));
            }
            return n;
        }

        /// <summary>Members per settle phase (automation reads it).</summary>
        public int Settled(SettlePhase phase)
        {
            int n = 0;
            foreach (WingMember m in Members)
                if (m.Settle != null && m.Settle.Phase == phase) n++;
            return n;
        }

        /// <summary>A settled member flies its settle instead of the formation (and is on the ground with the fly-by-wire
        /// off, so the no-FBW release must not fire); once lifted off it rejoins. True when it was flown here.</summary>
        private bool StepSettle(WingMember m, WingFrame frame, float dt)
        {
            SettlePilot s = m.Settle;
            if (s == null) return false;
            if (s.Phase == SettlePhase.Done)
            {
                m.Settle = null;
                m.Job = null;
                m.NoFbwSeconds = 0f;
                m.Brain.FormUp(missionTime, Events);
                return false;
            }
            // The approach from afar flies under the wing's terrain floor and collision bias (review M4c I4).
            int slot = m.Brain.Slot;
            bool near = frame.HasNearFloor[slot];
            var approach = new LimitContext
            {
                FloorY = near ? frame.NearFloorY[slot] : frame.FloorY, NearFloorY = frame.NearFloorY[slot], HasNearFloor = near,
                Clearance = m.Brain.Clearance, Aggression = 0.3f, CollisionBias = frame.Bias[slot],
            };
            ControlWriter.Fly(m.Aircraft, s.Step(m.Last, m.Profile, m.Brain.Pipeline, missionTime, dt, Events, m.Seat, approach), m.Profile.Class);
            StepJob(m, s, dt);
            return true;
        }
    }
}
