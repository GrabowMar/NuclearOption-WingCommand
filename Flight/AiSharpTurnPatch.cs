using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Allow powered, airborne fixed-wing AI to commit to sharp rapid combat manoeuvres.
    /// Excludes formation station keeping, takeoff/landing/taxi, and terrain aborts to protect flight safety.</summary>
    [HarmonyPatch(typeof(AutopilotPlane), nameof(AutopilotPlane.AutoAim))]
    internal static class AiSharpTurnPatch
    {
        private static bool ShouldInhibit(Aircraft aircraft)
        {
            if (aircraft == null) return true;
            Pilot pilot = WingRegistry.PrimaryPilot(aircraft);
            if (pilot == null) return false;

            var currentState = pilot.currentState;
            if (currentState == null) return false;

            return currentState is FormationFlyState ||
                   currentState is TerrainAbortState ||
                   currentState is LandInPlaceState ||
                   currentState is PilotParkedState ||
                   currentState is AIPilotTakeoffState ||
                   currentState is AIPilotTaxiState ||
                   currentState == pilot.AILandingState ||
                   currentState == pilot.AIHeloLandingState;
        }

        [HarmonyPrefix]
        private static void Prefix(
            AutopilotPlane __instance,
            Aircraft ___aircraft,
            GlobalPosition destination,
            bool runwayAlign,
            ref float effort,
            ref float bankAllowed)
        {
            if (!Plugin.Settings.AiSharpTurns.Value) return;
            Aircraft aircraft = ___aircraft;
            if (aircraft == null || aircraft.disabled || aircraft.Player != null ||
                aircraft.rb == null || runwayAlign)
                return;

            if (ShouldInhibit(aircraft)) return;

            var parameters = aircraft.GetAircraftParameters();
            float speed = aircraft.speed;
            float landingSpeed = parameters != null ? parameters.landingSpeed : 60f;

            if (!SharpTurnPolicy.IsEligible(
                isPlaneAutopilot: __instance != null,
                isDisabled: aircraft.disabled,
                hasPlayer: aircraft.Player != null,
                runwayAlign: runwayAlign,
                gearDeployed: aircraft.gearDeployed,
                radarAlt: aircraft.radarAlt,
                speed: speed,
                landingSpeed: landingSpeed))
                return;

            Vector3 toDestination = destination - aircraft.GlobalPosition();
            if (toDestination.sqrMagnitude < 1f) return;

            float turnAngle = Vector3.Angle(aircraft.rb.velocity, toDestination);
            if (turnAngle < SharpTurnPolicy.SharpTurnMinAngleDeg) return;

            bool leadPursuit = PersonnelFacade.Roster.HasPerk(aircraft, PilotPerk.LeadPursuit);
            if (effort <= 1f)
            {
                effort = PilotPerks.DogfightEffort(leadPursuit);
            }

            bankAllowed = SharpTurnPolicy.SafeBankCeiling(bankAllowed, aircraft.radarAlt);
        }

        [HarmonyPostfix]
        private static void Postfix(
            AutopilotPlane __instance,
            Aircraft ___aircraft,
            GlobalPosition destination,
            bool runwayAlign,
            float bankAllowed)
        {
            if (!Plugin.Settings.AiSharpTurns.Value) return;
            Aircraft aircraft = ___aircraft;
            if (aircraft == null || aircraft.disabled || aircraft.Player != null ||
                aircraft.rb == null || runwayAlign)
                return;

            if (ShouldInhibit(aircraft)) return;

            ControlInputs controls = aircraft.GetInputs();
            if (controls == null) return;

            var parameters = aircraft.GetAircraftParameters();
            float speed = aircraft.speed;
            float landingSpeed = parameters != null ? parameters.landingSpeed : 60f;
            float cornerSpeed = parameters != null ? parameters.cornerSpeed : 180f;

            if (!SharpTurnPolicy.IsEligible(
                isPlaneAutopilot: __instance != null,
                isDisabled: aircraft.disabled,
                hasPlayer: aircraft.Player != null,
                runwayAlign: runwayAlign,
                gearDeployed: aircraft.gearDeployed,
                radarAlt: aircraft.radarAlt,
                speed: speed,
                landingSpeed: landingSpeed))
                return;

            Vector3 toDestination = destination - aircraft.GlobalPosition();
            if (toDestination.sqrMagnitude < 1f) return;

            float turnAngle = Vector3.Angle(aircraft.rb.velocity, toDestination);
            if (turnAngle < SharpTurnPolicy.SharpTurnMinAngleDeg) return;

            bool energyFighter = PersonnelFacade.Roster.HasPerk(aircraft, PilotPerk.EnergyFighter);

            // 1. Corner-speed airbraking / throttle control (ground-safe)
            float vertSpeed = aircraft.rb != null ? aircraft.rb.velocity.y : 0f;
            var (thr, brk) = SharpTurnPolicy.ComputeSpeedControl(
                speed, cornerSpeed, turnAngle, energyFighter, aircraft.radarAlt, vertSpeed);
            if (brk > 0f)
            {
                controls.brake = brk;
                controls.throttle = thr;
            }

            // 2. Coordinated rudder assist for turn roll-in
            Vector3 localDest = aircraft.transform.InverseTransformDirection(toDestination.normalized);
            float headingErr = Mathf.Atan2(localDest.x, localDest.z) * Mathf.Rad2Deg;
            float ownBank = FixedWingFormation.BankOf(aircraft);
            float targetBank = Mathf.Sign(headingErr) * Mathf.Min(bankAllowed, Mathf.Max(60f, turnAngle * 1.5f));
            float rollError = targetBank - ownBank;

            float rudderKick = SharpTurnPolicy.ComputeRudderKick(headingErr, rollError, aircraft.radarAlt);
            if (Mathf.Abs(rudderKick) > 0.05f)
            {
                controls.yaw = Mathf.Clamp(controls.yaw + rudderKick, -1f, 1f);
            }

            // 3. Slice-turn pitch authority: restore elevator authority as bank establishes
            float pitchMult = SharpTurnPolicy.ComputePitchAuthority(rollError, turnAngle);
            if (pitchMult > 1.05f)
            {
                controls.pitch = Mathf.Clamp(controls.pitch * pitchMult, -1f, 1f);
            }

            aircraft.FilterInputs();
        }
    }
}
