namespace WingCommand
{
    internal static class WingInfluences
    {
        internal static void RegisterDefaults()
        {
            WingAi.RegisterInfluence(new Rendezvous());
            WingAi.RegisterInfluence(new Condition());
            WingAi.RegisterInfluence(new Proficiency());
        }

        private sealed class Rendezvous : IWingInfluence
        {
            public string Id => "wingcommand.rendezvous";
            public bool RequiresSmartMode => false;
            public WingFlightContribution Evaluate(in WingFlightSituation s)
            {
                if (!s.Situation.LeaderPresent) return default;
                float distance = WingFlightProfile.Smooth(
                    (s.SlotError / s.CaptureDistance - 0.5f) / 3.5f);
                float closing = WingFlightProfile.Smooth(
                    s.RelativeClosingSpeed / System.Math.Max(15f, s.LeaderSpeed * 0.3f));
                // Hurry a distant arrival, then yield to braking as closure develops.
                return new WingFlightContribution(distance * (1f - closing * 0.75f), captureGain: 1.3f);
            }
        }

        private sealed class Condition : IWingInfluence
        {
            public string Id => "wingcommand.condition";
            public bool RequiresSmartMode => false;
            public WingFlightContribution Evaluate(in WingFlightSituation s)
            {
                float fuel = WingFlightProfile.Smooth((0.3f - s.Situation.Fuel) / 0.25f);
                float damage = WingFlightProfile.Smooth((0.85f - s.Situation.Integrity) / 0.6f);
                // Use the flight controller's actual envelope when available. Rotation
                // speed can be far below fixed-wing stall speed on a vectoring aircraft.
                float slow = s.Situation.MemberIsRotary ? 0f : s.MinimumAirspeed > 0f
                    ? WingFlightProfile.Smooth((1.15f - s.Situation.Airspeed / s.MinimumAirspeed) / 0.15f)
                    : s.Situation.TakeoffSpeed > 0f
                        ? WingFlightProfile.Smooth((1.4f - s.Situation.Airspeed / s.Situation.TakeoffSpeed) / 0.4f)
                        : 0f;
                float caution = System.Math.Max(slow, System.Math.Max(fuel, damage));
                // ROE and threat geometry already set their own spacing. This adds
                // only room for an aircraft whose energy or condition needs it.
                return new WingFlightContribution(caution, captureGain: 0.85f,
                    spacingScale: 1.15f, dampingScale: 1.3f, bankScale: 0.75f);
            }
        }

        private sealed class Proficiency : IWingInfluence
        {
            public string Id => "wingcommand.proficiency";
            public bool RequiresSmartMode => true;
            public WingFlightContribution Evaluate(in WingFlightSituation s)
            {
                float experience = s.PilotSkill;
                float hold = s.Situation.Roe == WingRoe.Hold ? 1f : 0f;
                return new WingFlightContribution(1f,
                    captureGain: 0.97f + experience * 0.09f + hold * 0.03f,
                    spacingScale: 1.04f - experience * 0.04f,
                    dampingScale: 1.12f - experience * 0.08f + hold * 0.04f);
            }
        }
    }
}
