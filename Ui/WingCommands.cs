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
            Airbase field = w.FieldFor(caller);
            if (field == null)
            {
                WingToast.Show("No friendly field to launch from");
                return;
            }
            SpawnService.Instance.LaunchFromField(field, CallAirframe() ?? caller.definition, n);
        }

        public static void NextField()
        {
            if (!Ready(out WingService w) || w.Player == null) return;
            Airbase field = w.NextField(w.Player);
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
