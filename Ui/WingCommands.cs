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
        public static void Call(int n)
        {
            if (!Ready(out WingService w) || SpawnService.Instance == null) return;
            Aircraft caller = w.Player;
            if (caller == null)
            {
                WingToast.Show("Not flying");
                return;
            }
            AircraftDefinition type = CallAirframe() ?? caller.definition;
            Airbase field = w.FieldFor(caller, type);
            if (field == null)
            {
                WingToast.Show("No friendly field to launch from");
                return;
            }
            SpawnService.Instance.LaunchFromField(field, type, n);
        }

        public static void NextField()
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            Airbase field = w.NextField(w.Player, CallAirframe());
            WingToast.Show(field != null
                ? $"Launch field: {field.name} ({(field.transform.position - w.Player.transform.position).magnitude / 1000f:0} km)"
                : "No friendly field to launch from");
        }

        public static void FormUp()
        {
            if (!Ready(out WingService w)) return;
            w.FormUp();
            WingToast.Show("Form up");
        }

        public static void NextShape()
        {
            if (Ready(out WingService w)) w.NextShape();
        }

        public static void NextFamily()
        {
            if (Ready(out WingService w)) w.NextFamily();
        }

        public static void SetSpacing(SpacingPreset preset)
        {
            if (Ready(out WingService w)) w.SetSpacing(preset);
        }

        public static void CycleSpacing()
        {
            if (Ready(out WingService w)) w.SetSpacing((SpacingPreset)(((int)w.Selection.Spacing + 1) % 4));
        }

        /// <summary>The whole wing home to the reserve (spec M3 §4).</summary>
        public static void Rtb() => Recover(RecoveryIntent.Rtb, "returning to base");

        /// <summary>The whole wing home to refuel and rearm, then back out.</summary>
        public static void Refit() => Recover(RecoveryIntent.Refit, "going to refit");

        private static void Recover(RecoveryIntent intent, string what)
        {
            if (!Ready(out WingService w)) return;
            int n = w.RecoverAll(intent);
            WingToast.Show(n > 0 ? $"{n} wingm{(n == 1 ? "an" : "en")} {what}" : "No wingman can go: no friendly field, or already going");
        }

        public static void Dismiss()
        {
            if (!Ready(out WingService w)) return;
            if (w.Members.Count == 0)
            {
                WingToast.Show("No wingmen to dismiss");
                return;
            }
            w.Dismiss();
            WingToast.Show("Wing dismissed");
        }

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
            w.SetEscort(target);
            WingToast.Show("Escorting " + target.unitName);
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
                WingToast.Show("No friendly aircraft to recruit");
                return;
            }
            WingToast.Show(WingRecruitment.TryRecruit(w, target, out _, out string reason)
                ? target.unitName + " joins the wing"
                : "Cannot recruit " + target.unitName + ": " + reason);
        }

        public static float MoveAheadMetres = 10000f, PatrolHalfMetres = 10000f;

        /// <summary>The wing orbits the point below the player (spec M4 §2.4).</summary>
        public static void OrbitHere() => Order(p => WingTask.Orbit(Point(p, 0f, 0f)), "orbiting here");

        public static void HoldHere() =>
            Order(p => WingTask.Hold(Point(p, 0f, 0f), Vec3.HeadingDeg(p.transform.forward.ToVec3())), "holding here");

        public static void MoveAhead() => Order(p => WingTask.Move(Point(p, MoveAheadMetres, 0f)), "moving ahead");

        public static void PatrolHere() =>
            Order(p => WingTask.Patrol(false, Point(p, -PatrolHalfMetres, 0f), Point(p, PatrolHalfMetres, 0f)), "patrolling here");

        private static Waypoint Point(Aircraft p, float forward, float right)
        {
            Vec3 at = p.GlobalPosition().ToVec3();
            Vec3 f = p.transform.forward.ToVec3().Horizontal;
            f = f.SqrLength > 1e-4f ? f.Normalized : Vec3.Forward;
            Vec3 there = at + f * forward + new Vec3(f.Z, 0f, -f.X) * right;
            Waypoint w = Waypoint.At(there.X, there.Z);
            w.Altitude = at.Y;
            return w;
        }

        private static void Order(Func<Aircraft, WingTask> make, string what)
        {
            if (!Ready(out WingService w)) return;
            if (w.Player == null)
            {
                WingToast.Show("Not flying");
                return;
            }
            OrderResult r = w.Order(make(w.Player));
            WingToast.Show(r.Accepted ? "Wing " + what : "Wing cannot: " + r.Reason);
        }

        public static void Engage()
        {
            if (!Ready(out WingService w)) return;
            int n = w.Engage(null);
            WingToast.Show(n > 0 ? $"{n} engaging" : "Nobody can engage");
        }

        /// <summary>The player's selected enemy target for every engaged member.</summary>
        public static void AttackTarget()
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            Unit target = SelectedEnemy(w.Player);
            if (target == null)
            {
                WingToast.Show("No enemy target selected");
                return;
            }
            int n = w.Engage(target);
            WingToast.Show(n > 0 ? $"{n} attacking {target.unitName}" : "Nobody can attack");
        }

        public static void Disengage()
        {
            if (!Ready(out WingService w)) return;
            int n = w.Disengage();
            WingToast.Show(n > 0 ? $"{n} disengaging" : "Nobody engaged");
        }

        private static Unit SelectedEnemy(Aircraft player)
        {
            List<Unit> targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;
            if (targets == null) return null;
            foreach (Unit u in targets)
                if (u != null && !u.disabled && u.NetworkHQ != null && u.NetworkHQ != player.NetworkHQ) return u;
            return null;
        }

        public static void EscortMe()
        {
            if (!Ready(out WingService w)) return;
            if (!w.Escorting)
            {
                WingToast.Show("Already on you");
                return;
            }
            w.SetEscort(null);
            WingToast.Show("Escorting you");
        }

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
