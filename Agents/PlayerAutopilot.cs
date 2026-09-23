using System;
using UnityEngine;

namespace WingCommand
{
    /// <summary>ALT/HDG/SPD/VS/LVL holds on the player's own fixed-wing aircraft (spec §7), flown by the same
    /// pipeline as the wingmen through <see cref="HoldGuidance"/>. It runs wherever the player's state runs, so
    /// it works on clients too.
    /// <list type="bullet">
    /// <item>A stick deflection hands that axis back to the pilot; release re-captures it.</item>
    /// <item>Engaging and recapturing seed every loop from the inputs last applied, so there is no bump.</item>
    /// </list></summary>
    internal sealed class PlayerAutopilot : IWingService
    {
        public static float Aggression = 0.3f;

        public static PlayerAutopilot Instance { get; private set; }

        public string Name => "Autopilot";
        public readonly AutopilotSession Session = new AutopilotSession();

        private Aircraft bound;
        private AircraftSensor sensor;
        private FixedWingPipeline pipeline;
        private AirframeProfile profile;
        private AircraftState last;
        private ControlOutput lastApplied;
        private bool haveState, throttleActive, faulted;
        private float playerThrottle, apThrottle;

        public PlayerAutopilot() => Instance = this;

        public void Activate() => Unbind();

        public void Deactivate() => Unbind();

        public void Tick(float dt)
        {
            if (bound == null) return;
            Aircraft local = GameManager.GetLocalAircraft(out Aircraft a) ? a : null;
            if (local != bound || bound.disabled)
            {
                if (Session.Engaged) WingToast.Show("Autopilot off");
                Unbind();
            }
        }

        public void FixedTick(float dt)
        {
        }

        public void SetLateral(LateralHold mode)
        {
            if (!Prepare()) return;
            Session.SetLateral(mode, last);
            Announce();
        }

        public void SetVertical(VerticalHold mode)
        {
            if (!Prepare()) return;
            Session.SetVertical(mode, last);
            Announce();
        }

        public void ToggleSpeed()
        {
            if (!Prepare()) return;
            Session.SetSpeed(!Session.Spec.Speed, last, playerThrottle);
            Announce();
        }

        public void Off()
        {
            Session.Off();
            throttleActive = false;
            WingToast.Show("Autopilot off");
        }

        /// <summary>Postfix of <c>PilotPlayerState.PlayerAxisControls</c>: the pilot's stick is in the inputs,
        /// the FBW filter has not run yet.</summary>
        internal void AfterAxisControls(PilotPlayerState state)
        {
            if (faulted) return;
            Pilot pilot = GameAccess.PilotOf(state);
            Aircraft a = pilot != null ? pilot.aircraft : null;
            if (a == null || !Bind(a)) return;
            float dt = Time.fixedDeltaTime;
            last = sensor.Read(a, dt);
            haveState = true;
            ControlInputs inputs = a.GetInputs();
            StickInputs raw = ControlWriter.Read(inputs);
            ControlOutput hands = EngineSticks.ToPure(raw);
            if (!Session.Engaged)
            {
                lastApplied = hands;
                throttleActive = false;
                return;
            }

            var pilotInputs = new PilotInputs
            {
                Pitch = hands.Pitch, Roll = hands.Roll, Yaw = hands.Yaw, Throttle = playerThrottle,
                Gloc = GameAccess.PilotStrength(state) < 0.2f,
                GearDown = a.gearState != LandingGear.GearState.LockedRetracted,
            };
            AutopilotTick t = Session.Step(last, pilotInputs, profile.MinimumSpeed(1f), dt);
            if (t.Disengaged != ApDisengage.None) WingToast.Show("Autopilot: " + Describe(t.Disengaged));
            if (!Session.Engaged)
            {
                lastApplied = hands;
                throttleActive = false;
                return;
            }
            if (t.Recaptured) pipeline.Track(last, lastApplied, profile);

            GuidanceCommand g = HoldGuidance.Evaluate(Session.Spec, last, profile);
            var ctx = new LimitContext { FloorY = float.NaN, Clearance = 0f, Aggression = Aggression };
            ControlOutput o = pipeline.Step(g, last, ctx, profile, dt);
            StickInputs sticks = EngineSticks.FromPure(o);
            if (t.WritePitch) inputs.pitch = sticks.Pitch;
            if (t.WriteRoll) inputs.roll = sticks.Roll;
            if (t.WriteYaw) inputs.yaw = sticks.Yaw;
            throttleActive = t.WriteThrottle;
            if (throttleActive)
            {
                apThrottle = sticks.Throttle;
                inputs.throttle = apThrottle;
            }
            lastApplied = new ControlOutput
            {
                Pitch = t.WritePitch ? o.Pitch : hands.Pitch,
                Roll = t.WriteRoll ? o.Roll : hands.Roll,
                Yaw = t.WriteYaw ? o.Yaw : hands.Yaw,
                Throttle = throttleActive ? apThrottle : inputs.throttle,
                Airbrake = throttleActive && o.Airbrake,
            };
        }

        /// <summary>Postfix of <c>PilotPlayerState.PlayerThrottleAxis1Controls</c>: the pilot's throttle is in
        /// the inputs.</summary>
        internal void AfterThrottle(PilotPlayerState state)
        {
            if (faulted) return;
            Pilot pilot = GameAccess.PilotOf(state);
            Aircraft a = pilot != null ? pilot.aircraft : null;
            if (a == null || a != bound) return;
            ControlInputs inputs = a.GetInputs();
            playerThrottle = inputs.throttle;
            if (throttleActive && Session.Spec.Speed) inputs.throttle = apThrottle;
        }

        internal void Fault(Exception e)
        {
            if (faulted) return;
            faulted = true;
            Session.Off();
            throttleActive = false;
            Plugin.Logger.LogError("[Autopilot] disabled for this mission after an error: " + e);
            WingToast.Show("Autopilot failed and is off; see the log");
        }

        private bool Prepare()
        {
            if (faulted)
            {
                WingToast.Show("Autopilot unavailable after an error; see the log");
                return false;
            }
            Aircraft a = GameManager.GetLocalAircraft(out Aircraft local) ? local : null;
            if (a == null || !Bind(a))
            {
                WingToast.Show("Autopilot: fixed-wing aircraft only");
                return false;
            }
            if (!haveState)
            {
                last = sensor.Read(a, 0f);
                lastApplied = EngineSticks.ToPure(ControlWriter.Read(a.GetInputs()));
                playerThrottle = a.GetInputs().throttle;
                haveState = true;
            }
            if (!Session.Engaged) pipeline.Track(last, lastApplied, profile);
            return true;
        }

        private bool Bind(Aircraft a)
        {
            if (a == bound) return true;
            Unbind();
            if (a.pilots == null || a.pilots.Length == 0 || a.pilots[0].pilotType != Pilot.PilotType.Plane) return false;
            bound = a;
            sensor = new AircraftSensor();
            pipeline = new FixedWingPipeline();
            profile = WingProfiles.For(a);
            return true;
        }

        private void Unbind()
        {
            Session.Off();
            bound = null;
            haveState = false;
            throttleActive = false;
            faulted = false;
        }

        private void Announce()
        {
            string text = WingHudText.Autopilot(Session.Spec, Session.LateralOverride, Session.VerticalOverride);
            WingToast.Show(text.Length == 0 ? "Autopilot off" : text);
        }

        private static string Describe(ApDisengage reason)
        {
            switch (reason)
            {
                case ApDisengage.Gloc: return "off (G-LOC)";
                case ApDisengage.GearLow: return "off (gear down, low)";
                case ApDisengage.Slow: return "off (too slow)";
                case ApDisengage.Throttle: return "speed hold off (throttle moved)";
                default: return "off";
            }
        }
    }
}
