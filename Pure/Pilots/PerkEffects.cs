using System.Collections.Generic;

namespace WingCommand
{
    /// <summary>Spec M5 §11: a pilot's combat perks as the modifiers Wing Command's own firing, defence, allocation and
    /// flight apply (the native combat AI is unchanged). Computed once per seat; <see cref="None"/> is neutral.</summary>
    internal struct PerkEffects
    {
        public static float QuickDrawInterval = 0.6f, StandoffReach = 1.3f, SnapshotBoresight = 1.4f, HeadOnRange = 1.25f,
            HeadOnClosing = 350f, HeadOnConeDeg = 30f, ApexRange = 1.4f, ApexAltitude = 4000f, ApexMaxAspectDeg = 90f, TargetMasterKeep = 1.5f,
            TerrainHuggerClearance = -25f, EarlyWarningReaction = -1.5f, BreakTurnRange = 2500f;

        public float IntervalScale, ReachScale, BoresightScale, KeepScale, ClearanceDelta, ReactionDelta, BreakRange;
        public bool HeadOnJoust, ApexHunter;
        public int SplashShots;

        public static PerkEffects None => new PerkEffects
        {
            IntervalScale = 1f, ReachScale = 1f, BoresightScale = 1f, KeepScale = 1f, SplashShots = 1,
        };

        public static PerkEffects For(IList<PilotPerk> perks)
        {
            PerkEffects fx = None;
            if (perks == null) return fx;
            foreach (PilotPerk p in perks)
                switch (p)
                {
                    case PilotPerk.QuickDraw: fx.IntervalScale = QuickDrawInterval; break;
                    case PilotPerk.Standoff: fx.ReachScale = StandoffReach; break;
                    case PilotPerk.Snapshot: fx.BoresightScale = SnapshotBoresight; break;
                    case PilotPerk.HeadOnJoust: fx.HeadOnJoust = true; break;
                    case PilotPerk.ApexHunter: fx.ApexHunter = true; break;
                    case PilotPerk.SalvoSpecialist: fx.SplashShots = 2; break;
                    case PilotPerk.TargetMaster: fx.KeepScale = TargetMasterKeep; break;
                    case PilotPerk.TerrainHugger: fx.ClearanceDelta = TerrainHuggerClearance; break;
                    case PilotPerk.EarlyWarning: fx.ReactionDelta = EarlyWarningReaction; break;
                    case PilotPerk.BreakTurn: fx.BreakRange = BreakTurnRange; break;
                }
            return fx;
        }

        /// <summary>A missile's max range with the range perks: HeadOnJoust for a head-on (within
        /// <see cref="HeadOnConeDeg"/>), fast (over <see cref="HeadOnClosing"/>) closure; ApexHunter for a radar missile
        /// from above <see cref="ApexAltitude"/> at a target not flying away (under <see cref="ApexMaxAspectDeg"/>: a
        /// fleeing target outruns the extra range, review M5g C2). Never shorter.</summary>
        public float MaxRange(float maxRange, bool radar, float ownAltitude, float closingSpeed, float aspectDeg)
        {
            float scale = 1f;
            if (HeadOnJoust && closingSpeed > HeadOnClosing && aspectDeg < HeadOnConeDeg) scale *= HeadOnRange;
            if (ApexHunter && radar && ownAltitude > ApexAltitude && aspectDeg < ApexMaxAspectDeg) scale *= ApexRange;
            return maxRange * scale;
        }

        /// <summary>An off-boresight limit with Snapshot (0 = never stays never).</summary>
        public float Boresight(float limit) => limit * BoresightScale;
    }
}
