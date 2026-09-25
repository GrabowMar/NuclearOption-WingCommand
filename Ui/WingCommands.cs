using System;
using System.Collections.Generic;

namespace WingCommand
{
    internal enum ApCommand : byte { Level, Heading, Altitude, VerticalSpeed, Speed, Off }

    /// <summary>Player commands shared by the radial and the hotkeys.</summary>
    internal static class WingCommands
    {
        public static void Autopilot(ApCommand c)
        {
            PlayerAutopilot ap = PlayerAutopilot.Instance;
            if (ap == null) return;
            switch (c)
            {
                case ApCommand.Level: ap.SetLateral(LateralHold.Level); break;
                case ApCommand.Heading: ap.SetLateral(LateralHold.Heading); break;
                case ApCommand.Altitude: ap.SetVertical(VerticalHold.Altitude); break;
                case ApCommand.VerticalSpeed: ap.SetVertical(VerticalHold.VerticalSpeed); break;
                case ApCommand.Speed: ap.ToggleSpeed(); break;
                default: ap.Off(); break;
            }
        }

        /// <summary>Wingmen always launch from a field (the picked one, else the nearest friendly one).</summary>
        /// <summary>Wingmen always launch from a field (the picked one, else the nearest friendly one).</summary>
        public static void Call(int n) => WingOrders.Run(new WingOrder { Kind = OrderKind.Call, Number = n });

        public static void NextField()
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            Airbase field = w.NextField(w.Player, CallAirframe());
            WingToast.Show(field != null
                ? $"Launch field: {field.name} ({(field.transform.position - w.Player.transform.position).magnitude / 1000f:0} km)"
                : "No friendly field to launch from");
        }

        public static void FormUp() => FormUp(default);

        public static void FormUp(WingScope scope) => WingOrders.Run(WingOrder.Of(OrderKind.FormUp, scope));

        /// <summary>The wing's helicopters land around you and wait (spec M4 §5).</summary>
        public static void LandHere() => LandHere(default);

        public static void LandHere(WingScope scope) => WingOrders.Run(WingOrder.Of(OrderKind.LandHere, scope));

        public static void TakeOff() => TakeOff(default);

        public static void TakeOff(WingScope scope) => WingOrders.Run(WingOrder.Of(OrderKind.TakeOff, scope));

        public static void NextShape() => WingOrders.Run(WingOrder.Of(OrderKind.NextShape));

        public static void NextFamily() => WingOrders.Run(WingOrder.Of(OrderKind.NextFamily));

        public static void SetSpacing(SpacingPreset preset) => WingOrders.Run(new WingOrder { Kind = OrderKind.SetSpacing, Number = (int)preset });

        public static void CycleSpacing()
        {
            WingService w = WingService.Instance;
            if (w?.Selection != null) SetSpacing((SpacingPreset)(((int)w.Selection.Spacing + 1) % 4));
        }

        /// <summary>The whole wing home to the reserve (spec M3 §4).</summary>
        public static void Rtb() => Rtb(default);

        public static void Rtb(WingScope scope) => WingOrders.Run(WingOrder.Of(OrderKind.Rtb, scope));

        /// <summary>The whole wing home to refuel and rearm, then back out.</summary>
        public static void Refit() => Refit(default);

        public static void Refit(WingScope scope) => WingOrders.Run(WingOrder.Of(OrderKind.Refit, scope));


        public static void Dismiss() => WingOrders.Run(WingOrder.Of(OrderKind.Dismiss));

        /// <summary>Escort the player's selected friendly unit (aircraft, vehicle or ship), else the nearest friendly
        /// aircraft ahead within 5 km (spec M2 §6).</summary>
        public static void EscortTarget()
        {
            if (!Ready(out WingService w)) return;
            Aircraft player = w.Player;
            if (player == null)
            {
                WingToast.Show("Not flying");
                return;
            }
            Unit target = SelectedFriendly(player, w) ?? NearestFriendlyAhead(player, w);
            if (target == null)
            {
                WingToast.Show("No friendly to escort");
                return;
            }
            WingOrders.Run(new WingOrder { Kind = OrderKind.EscortTarget, Units = new[] { target.persistentID.Id } });
        }

        /// <summary>Takes command of the selected friendly aircraft, else the nearest one ahead (spec M3 §5).</summary>
        public static void Recruit()
        {
            if (!Ready(out WingService w)) return;
            Aircraft player = w.Player;
            if (player == null)
            {
                WingToast.Show("Not flying");
                return;
            }
            Aircraft target = SelectedFriendly(player, w) as Aircraft ?? NearestFriendlyAhead(player, w) as Aircraft;
            if (target == null)
            {
                WingToast.Show("No friendly aircraft to adopt");
                return;
            }
            WingOrders.Run(new WingOrder { Kind = OrderKind.Recruit, Units = new[] { target.persistentID.Id } });
        }

        public static float MoveAheadMetres = 10000f, PatrolHalfMetres = 10000f, OrderHeightMin = 50f, ScoutAheadMetres = 20000f;

        /// <summary>The wing orbits the point below the player (spec M4 §2.4).</summary>
        public static void OrbitHere() => OrbitHere(default);

        public static void OrbitHere(WingScope scope) => Order(p => WingTask.Orbit(Point(p, 0f, 0f)), scope);

        public static void HoldHere() => HoldHere(default);

        public static void HoldHere(WingScope scope) =>
            Order(p => WingTask.Hold(Point(p, 0f, 0f), Vec3.HeadingDeg(p.transform.forward.ToVec3())), scope);

        /// <summary>Spec M4 §7.1: a wing helicopter lands next to the nearest downed wing pilot for the pickup.</summary>
        public static void Rescue() => Rescue(default);

        public static void Rescue(WingScope scope) => WingOrders.Run(WingOrder.Of(OrderKind.Rescue, scope));

        /// <summary>Spec M4 §7.2: helicopters carrying cargo land, deploy it and rejoin.</summary>
        public static void DeliverCargo() => DeliverCargo(default);

        public static void DeliverCargo(WingScope scope) => WingOrders.Run(WingOrder.Of(OrderKind.DeliverCargo, scope));

        public static void MoveAhead() => MoveAhead(default);

        public static void MoveAhead(WingScope scope) => Order(p => WingTask.Move(Point(p, MoveAheadMetres, 0f)), scope);

        /// <summary>Spec M7 §2.4: Move 20 km ahead, reporting ground contacts on the way and while orbiting there.</summary>
        public static void ScoutAhead() => ScoutAhead(default);

        public static void ScoutAhead(WingScope scope) => Order(p =>
        {
            WingTask t = WingTask.Move(Point(p, ScoutAheadMetres, 0f));
            t.Scout = true;
            return t;
        }, scope);

        public static void PatrolHere() => PatrolHere(default);

        public static void PatrolHere(WingScope scope) =>
            Order(p => WingTask.Patrol(false, Point(p, -PatrolHalfMetres, 0f), Point(p, PatrolHalfMetres, 0f)), scope);

        private static Waypoint Point(Aircraft p, float forward, float right)
        {
            Vec3 at = p.GlobalPosition().ToVec3();
            Vec3 f = p.transform.forward.ToVec3().Horizontal;
            f = f.SqrLength > 1e-4f ? f.Normalized : Vec3.Forward;
            Vec3 there = at + f * forward + new Vec3(f.Z, 0f, -f.X) * right;
            Waypoint w = Waypoint.At(there.X, there.Z);
            // The player's height only when flying: from the runway the wing keeps its own (review M4a I2d).
            w.Altitude = p.radarAlt > OrderHeightMin ? at.Y : float.NaN;
            return w;
        }

        /// <summary>A task built from the player's aircraft, for the whole wing (spec M4 §2.4).</summary>
        private static void Order(Func<Aircraft, WingTask> make, WingScope scope)
        {
            if (!Ready(out WingService w)) return;
            if (w.Player == null)
            {
                WingToast.Show("Not flying");
                return;
            }
            WingOrders.Run(WingOrder.Tasked(make(w.Player), scope));
        }

        public static void Engage() => Engage(default);

        public static void Engage(WingScope scope) => WingOrders.Run(WingOrder.Of(OrderKind.Engage, scope));

        public static float GoHighMetres = 300f, GoLowMetres = -150f;

        /// <summary>Spec M5 §10.1.</summary>
        public static void Stack(float metres, string what) => WingOrders.Run(new WingOrder { Kind = OrderKind.Stack, Number = metres, Text = what });

        /// <summary>Spec M5 §10.4: Buster (no afterburner) or Gate (afterburner allowed).</summary>
        public static void Afterburner(bool allowed) => WingOrders.Run(new WingOrder { Kind = OrderKind.Afterburner, Flag = allowed });

        /// <summary>Spec M5 §10.2: the second element attacks your targets; the first stays with you.</summary>
        public static void BuddyAttack()
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            List<Unit> targets = SelectedEnemies(w.Player);
            if (targets.Count == 0)
            {
                WingToast.Show("Buddy attack: select a target first");
                return;
            }
            // The second pair of the shape attacks; the first stays with you (spec M5 §10.2).
            var pair = new List<uint>();
            foreach (WingMember m in w.Members)
                if (!m.Released && m.Alive && !m.OnGround && m.Recovery == null && m.Settle == null && w.PairOf(m) == 1 &&
                    (object)m.Aircraft != null) pair.Add(m.Aircraft.persistentID.Id);
            if (pair.Count == 0)
            {
                WingToast.Show("Buddy attack: no second element");
                return;
            }
            WingOrders.Run(new WingOrder { Kind = OrderKind.Attack, Units = Ids(targets), Scope = WingScope.OfMembers(pair.ToArray()) });
        }

        /// <summary>The player's selected enemy targets, split across the wing (spec M5, M5b).</summary>
        public static void AttackTarget() => AttackTarget(default);

        public static void AttackTarget(WingScope scope)
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            List<Unit> targets = SelectedEnemies(w.Player);
            if (targets.Count == 0)
            {
                WingToast.Show("No enemy target selected");
                return;
            }
            WingOrders.Run(new WingOrder { Kind = OrderKind.Attack, Units = Ids(targets), Scope = scope });
        }

        /// <summary>At most <see cref="WingOrder.MaxUnits"/> units as persistent ids.</summary>
        private static uint[] Ids(List<Unit> units)
        {
            int n = Math.Min(units.Count, WingOrder.MaxUnits);
            var ids = new uint[n];
            for (int i = 0; i < n; i++) ids[i] = units[i].persistentID.Id;
            return ids;
        }

        /// <summary>Spec M5 §9.2: one missile from every wingman with your target in envelope.</summary>
        public static void Splash() => Splash(default);

        public static void Splash(WingScope scope)
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            List<Unit> targets = SelectedEnemies(w.Player);
            if (targets.Count == 0)
            {
                WingToast.Show("Splash: select a target first");
                return;
            }
            WingOrders.Run(new WingOrder { Kind = OrderKind.Splash, Units = new[] { targets[0].persistentID.Id }, Scope = scope });
        }

        public static void ClearMySix() => WingOrders.Run(WingOrder.Of(OrderKind.ClearSix));

        /// <summary>Spec M7 §1.5: the first wingman flying with the wing calls the nearest air threat's BRA from you.</summary>
        public static void BogeyDope() => WingOrders.Run(WingOrder.Of(OrderKind.BogeyDope));

        /// <summary>The Bogey Dope answer on the radio (false with the reason when nobody can answer).</summary>
        public static bool BogeyDopeCall(WingService w, out string answer)
        {
            WingMember speaker = null;
            foreach (WingMember m in w.Members)
                if (!m.Released && m.Alive && !m.OnGround)
                {
                    speaker = m;
                    break;
                }
            RadioDirector radio = RadioDirector.Instance;
            if (speaker == null || radio == null)
            {
                answer = "No wingmen to ask";
                return false;
            }
            answer = w.NearestAirThreat(w.Player, out Vec3 pos, out Vec3 vel, out string type)
                ? $"Bogey dope, {Bra.Format(w.Player.GlobalPosition().ToVec3(), pos, vel, PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Imperial)}. {type}."
                : "Picture clean.";
            return radio.Answer(speaker, "BOGEYDOPE", answer);
        }

        /// <summary>Reserve → Escort → Sweep: what members shoot at while holding formation (spec M5 §8).</summary>
        public static void NextDoctrine() => WingOrders.Run(WingOrder.Of(OrderKind.NextDoctrine));

        public static void Disengage() => Disengage(default);

        public static void Disengage(WingScope scope) => WingOrders.Run(WingOrder.Of(OrderKind.BreakOff, scope));

        private static List<Unit> SelectedEnemies(Aircraft player)
        {
            var enemies = new List<Unit>();
            List<Unit> targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;
            if (targets == null) return enemies;
            foreach (Unit u in targets)
                if (u != null && !u.disabled && u.NetworkHQ != null && u.NetworkHQ != player.NetworkHQ) enemies.Add(u);
            return enemies;
        }

        public static void EscortMe() => WingOrders.Run(WingOrder.Of(OrderKind.EscortMe));

        private static Unit SelectedFriendly(Aircraft player, WingService w)
        {
            List<Unit> targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;
            if (targets == null) return null;
            foreach (Unit u in targets)
                if (u != null && !u.disabled && !ReferenceEquals(u, player) && u.NetworkHQ == player.NetworkHQ &&
                    !(u is Aircraft a && w.IsMember(a))) return u;
            return null;
        }

        private static readonly List<Aircraft> candidates = new List<Aircraft>();
        private static Vec3[] candidatePositions = new Vec3[64];

        private static Unit NearestFriendlyAhead(Aircraft player, WingService w)
        {
            candidates.Clear();
            foreach (Aircraft a in UnitRegistry.allAircraft)
            {
                if (a == null || a.disabled || ReferenceEquals(a, player) || a.NetworkHQ != player.NetworkHQ || w.IsMember(a)) continue;
                candidates.Add(a);
            }
            if (candidatePositions.Length < candidates.Count) candidatePositions = new Vec3[candidates.Count];
            for (int i = 0; i < candidates.Count; i++) candidatePositions[i] = candidates[i].GlobalPosition().ToVec3();
            int chosen = EscortPick.Nearest(player.GlobalPosition().ToVec3(), player.transform.forward.ToVec3(),
                candidatePositions, candidates.Count);
            return chosen >= 0 ? candidates[chosen] : null;
        }

        /// <summary>The configured airframe to call, or null for the player's own type.</summary>
        public static AircraftDefinition CallAirframe()
        {
            string name = Plugin.Settings.CallAirframe.Value;
            if (string.IsNullOrWhiteSpace(name)) return null;
            var catalogue = Encyclopedia.i != null ? Encyclopedia.i.aircraft : null;
            if (catalogue != null)
                foreach (AircraftDefinition d in catalogue)
                    if (d != null && string.Equals(d.unitName, name.Trim(), StringComparison.OrdinalIgnoreCase)) return d;
            WingToast.Show($"Unknown airframe '{name}'; calling your own type");
            return null;
        }

        private static bool Ready(out WingService w)
        {
            w = WingService.Instance;
            if (w?.Selection != null) return true;
            WingToast.Show("Wing Command is not ready");
            return false;
        }
    }
}
