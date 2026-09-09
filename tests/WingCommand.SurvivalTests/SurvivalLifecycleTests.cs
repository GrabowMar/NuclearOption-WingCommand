using System.Reflection;
using UnityEngine;
using Xunit;
using Random = UnityEngine.Random;

namespace WingCommand
{
    public class SurvivalLifecycleTests
    {
        public SurvivalLifecycleTests()
        {
            WingPilotRoster.Reset();
            WingDeparture.Reset();
            UnitRegistry.Units.Clear();
            Plugin.Settings = new Config();
            WingCommandManager.Instance = new WingCommandManager();
            Time.timeSinceLevelLoad = 0f;
            Random.Next = 0f;
            Random.Rolls = 0;
        }

        private static Aircraft Plane(int id = 1)
        {
            var aircraft = new Aircraft { persistentID = id, NetworkHQ = new FactionHQ() };
            aircraft.Pilot = new Pilot { aircraft = aircraft };
            UnitRegistry.Units[id] = aircraft;
            return aircraft;
        }

        private static PilotDismounted Eject(Aircraft aircraft)
        {
            var native = new PilotDismounted {
                parentUnit = aircraft.persistentID, NetworkHQ = aircraft.NetworkHQ,
                animationState = PilotDismounted.PilotState.landing
            };
            native.transform.position = new Vector3(0, 100, 0);
            WingSearchAndRescue.Track(native);
            aircraft.Pilot.ejected = true;
            return native;
        }

        [Fact]
        public void ImportedRankGetsPerksAndLargePromotionAwardsEveryCrossedRank()
        {
            var pilot = WingPilotRoster.ImportCustom(new CustomPilotRecord { Callsign = "TEST", Xp = 120 });
            Assert.Single(pilot.Perks);
            var aircraft = Plane();
            WingPilotRoster.Assign(aircraft, pilot);
            WingPilotRoster.Award(aircraft, 2000, "test");
            Assert.Equal(WingRank.Legend, pilot.Rank);
            Assert.Equal(4, pilot.Perks.Count);
            WingPilotRoster.Award(aircraft, 40, "test");
            Assert.Equal(4, pilot.Perks.Count);
        }

        [Fact]
        public void DownedPilotCannotBeSelectedReservedOrAssignedUntilRescued()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Commando);
            pilot.Xp = 400;
            var native = Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            Assert.False(pilot.Lost);
            Assert.Equal(PilotRecoveryStatus.Downed, pilot.RecoveryStatus);
            Assert.False(WingPilotRoster.IsFree(pilot));
            WingPilotRoster.Select(pilot);
            Assert.NotSame(pilot, WingPilotRoster.Selected);
            Assert.NotSame(pilot, WingPilotRoster.ReserveForRequisition());
            Assert.NotSame(pilot, WingPilotRoster.Assign(Plane(2), pilot));
            WingPilotRoster.ReleaseReservation(pilot, true);
            Assert.False(WingPilotRoster.IsFree(pilot));
            WingSearchAndRescue.Captured(native, aircraft);
            Assert.True(WingPilotRoster.IsFree(pilot));
            Assert.Equal(400, pilot.Xp);
            Assert.Contains(PilotPerk.Commando, pilot.Perks);
            Assert.Same(pilot, WingPilotRoster.Assign(Plane(3), pilot));
            WingSearchAndRescue.Captured(native, aircraft); // Late callback cannot retire the new seat.
            WingPilotRoster.Retire(1, false);
            Assert.True(WingPilotRoster.IsFlying(pilot));
            Assert.Same(pilot, WingPilotRoster.Of(UnitRegistry.Units[3] as Aircraft));
        }

        [Fact]
        public void NativeReturnBeforeRosterPruneIsIdempotent()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            var native = Eject(aircraft);
            native.NetworkunitState = Unit.UnitState.Returned;
            WingSearchAndRescue.Observe(native);
            WingSearchAndRescue.Observe(native);
            WingPilotRoster.Retire(1, false);
            Assert.True(WingPilotRoster.IsFree(pilot));
            Assert.False(pilot.Lost);
            Assert.Single(WingCommandManager.Instance.Messages);
        }

        [Fact]
        public void EnemyCaptureDoesNotBecomeKiaOrEscape()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Commando);
            var native = Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            WingSearchAndRescue.Tick();
            WingSearchAndRescue.Captured(native, Plane(2));
            Time.timeSinceLevelLoad = 500;
            WingSearchAndRescue.Tick();
            Assert.Equal(PilotRecoveryStatus.Captured, pilot.RecoveryStatus);
            Assert.False(pilot.Lost);
            Assert.False(WingPilotRoster.IsSelectable(pilot));
        }

        [Fact]
        public void DeathAfterEjectionCancelsEscapePermanently()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Commando);
            var native = Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            WingSearchAndRescue.Tick();
            native.animationState = PilotDismounted.PilotState.dead;
            WingSearchAndRescue.Observe(native);
            Time.timeSinceLevelLoad = 500;
            WingSearchAndRescue.Tick();
            WingSearchAndRescue.Captured(native, aircraft);
            Assert.True(pilot.Lost);
            Assert.False(WingPilotRoster.IsFree(pilot));
        }

        [Fact]
        public void DisabledAircraftCanSpawnSurvivorAfterRosterRemoval()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            aircraft.disabled = true;
            WingPilotRoster.Retire(1, false);
            Assert.False(pilot.Lost);
            Assert.False(WingPilotRoster.IsSelectable(pilot));
            Time.timeSinceLevelLoad = 2;
            WingSearchAndRescue.Tick();
            var native = Eject(aircraft);
            Assert.Equal(PilotRecoveryStatus.Downed, pilot.RecoveryStatus);
            WingSearchAndRescue.Captured(native, aircraft);
            Assert.True(WingPilotRoster.IsFree(pilot));
        }

        [Fact]
        public void UnconfirmedSignalLossIsMiaNotKia()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            WingPilotRoster.Retire(1, false);
            Time.timeSinceLevelLoad = 31;
            WingSearchAndRescue.Tick();
            Assert.Equal(PilotRecoveryStatus.Missing, pilot.RecoveryStatus);
            Assert.False(pilot.Lost);
            Assert.False(WingPilotRoster.IsFree(pilot));
        }

        [Theory]
        [InlineData(0.499f, true)]
        [InlineData(0.5f, false)]
        public void CommandoRollsOnceAndWaitsForEscapeDelay(float roll, bool succeeds)
        {
            Random.Next = roll;
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Commando);
            Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            WingSearchAndRescue.Tick();
            Time.timeSinceLevelLoad = 119;
            WingSearchAndRescue.Tick();
            Assert.False(WingPilotRoster.IsFree(pilot));
            Random.Next = 0; // Failure must not be rerolled later.
            Time.timeSinceLevelLoad = 120;
            WingSearchAndRescue.Tick();
            Time.timeSinceLevelLoad = 400;
            WingSearchAndRescue.Tick();
            Assert.Equal(1, Random.Rolls);
            Assert.Equal(succeeds, WingPilotRoster.IsFree(pilot));
        }

        [Fact]
        public void CommandoCannotEscapeWhileSlungOrDisabled()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Commando);
            var native = Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            native.Slung = true;
            WingSearchAndRescue.Tick();
            Assert.Equal(0, Random.Rolls);
            native.Slung = false;
            Time.timeSinceLevelLoad = 1;
            WingSearchAndRescue.Tick();
            native.disabled = true;
            Time.timeSinceLevelLoad = 200;
            WingSearchAndRescue.Tick();
            Assert.False(WingPilotRoster.IsFree(pilot));
        }

        [Theory]
        [InlineData(0.299f, true)]
        [InlineData(0.3f, false)]
        public void LuckRollIsStableAcrossGuidanceUpdatesAndRetargeting(float roll, bool succeeds)
        {
            var aircraft = Plane();
            aircraft.transform.position = new Vector3(0, 0, 1000);
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Luck);
            var missile = new Missile { targetID = 1 };
            Random.Next = roll;
            GlobalPosition aim = aircraft.GlobalPosition();
            WingSurvivalPerks.BiasGuidance(missile, ref aim);
            var expected = aim;
            Random.Next = 0;
            for (int i = 0; i < 50; i++)
            {
                aim = aircraft.GlobalPosition();
                WingSurvivalPerks.BiasGuidance(missile, ref aim);
                Assert.Equal(expected.Value, aim.Value);
            }
            missile.targetID = 99;
            WingSurvivalPerks.BiasGuidance(missile, ref aim);
            missile.targetID = 1;
            aim = aircraft.GlobalPosition();
            WingSurvivalPerks.BiasGuidance(missile, ref aim);
            Assert.Equal(1, Random.Rolls);
            Assert.Equal(succeeds ? 122500f : 0f, (aim - aircraft.GlobalPosition()).sqrMagnitude);
        }

        [Fact]
        public void PerksDoNotAffectClientsOrDisabledProgression()
        {
            var aircraft = Plane();
            WingPilotRoster.Assign(aircraft).Perks.Add(PilotPerk.FuelDiscipline);
            Assert.True(WingSurvivalPerks.Has(aircraft, PilotPerk.FuelDiscipline));
            aircraft.IsServer = false;
            Assert.False(WingSurvivalPerks.Has(aircraft, PilotPerk.FuelDiscipline));
            aircraft.IsServer = true;
            Plugin.Settings.RankEffect.Value = 0;
            Assert.False(WingSurvivalPerks.Has(aircraft, PilotPerk.FuelDiscipline));
            Plugin.Settings.RankEffect.Value = 1;
            Plugin.Settings.PilotProgression.Value = false;
            Assert.False(WingSurvivalPerks.Has(aircraft, PilotPerk.FuelDiscipline));
        }

        [Fact]
        public void ToughnessIsAppliedBeforeFatalDamageIsRecorded()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Toughness);
            object[] args = { aircraft.Pilot, 110f, 0f, 0f, 0f, 100f, (byte)0 };
            typeof(WingPilotFatalDamagePatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
            Assert.Equal(71.5f, args[1]);
            Assert.Null(pilot.LossCause);
        }

        [Fact]
        public void SarDispatchUsesOnlyIdleEligibleHelicoptersOnLand()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            var native = Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            var busy = new WingMember { Aircraft = Plane(2), Order = WingOrder.Attack };
            var idle = new WingMember { Aircraft = Plane(3) };
            busy.Aircraft.NetworkHQ = idle.Aircraft.NetworkHQ = aircraft.NetworkHQ;
            var wing = new WingRegistry();
            wing.Members.Add(busy);
            wing.Members.Add(idle);
            native.transform.position = Vector3.zero;
            WingSearchAndRescue.Dispatch(pilot, wing);
            Assert.Equal(WingOrder.Formation, idle.Order);
            native.transform.position = new Vector3(0, 100, 0);
            WingSearchAndRescue.Dispatch(pilot, wing);
            Assert.Equal(WingOrder.LandHere, idle.Order);
            Assert.Equal(WingOrder.Attack, busy.Order);
        }

        [Fact]
        public void SuccessfulBaseRecoveryClearsPendingNativeSurvivor()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            var native = Eject(aircraft);
            WingPilotRoster.Retire(1, true);
            native.animationState = PilotDismounted.PilotState.dead;
            WingSearchAndRescue.Observe(native);
            Assert.True(WingPilotRoster.IsFree(pilot));
            Assert.False(pilot.Lost);
            WingPilotRoster.Reset();
            Time.timeSinceLevelLoad = 500;
            WingSearchAndRescue.Tick();
            Assert.Empty(WingPilotRoster.DisplayRoster());
        }

        [Fact]
        public void ReleasedAircraftWithEjectedCrewAtBaseWaitsForRecoverySettlement()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            WingDeparture.Begin(aircraft);
            Eject(aircraft);
            aircraft.AtHome = true;
            WingDeparture.Prune();
            Assert.Single(WingDeparture.Outbound);
            Assert.True(WingPilotRoster.IsFlying(pilot));
            Assert.False(pilot.Lost);
            WingPilotRoster.Retire(1, true);
            Assert.True(WingPilotRoster.IsFree(pilot));
        }

        [Fact]
        public void ReleasedAircraftLostEnrouteTransfersSurvivorToSar()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            WingDeparture.Begin(aircraft);
            Eject(aircraft);
            WingDeparture.Prune();
            Assert.Empty(WingDeparture.Outbound);
            Assert.Equal(PilotRecoveryStatus.Downed, pilot.RecoveryStatus);
            Assert.False(WingPilotRoster.IsFlying(pilot));
            Assert.False(pilot.Lost);
        }

        [Fact]
        public void SpecializedDamageProtectionAndToughnessStackWithoutChangingOtherComponents()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            foreach (PilotPerk perk in new[] { PilotPerk.BallisticVest, PilotPerk.BlastSurvivor,
                        PilotPerk.Fireproof, PilotPerk.CrashTraining })
            {
                pilot.Perks.Clear(); pilot.Perks.Add(perk);
                float p = 100, b = 100, f = 100, i = 100, hp = 1000;
                WingSurvivalPerks.ProtectPilotDamage(aircraft, ref p, ref b, ref f, ref i, ref hp);
                Assert.Equal(perk == PilotPerk.BallisticVest ? 40f : 100f, p);
                Assert.Equal(perk == PilotPerk.BlastSurvivor ? 40f : 100f, b);
                Assert.Equal(perk == PilotPerk.Fireproof ? 25f : 100f, f);
                Assert.Equal(perk == PilotPerk.CrashTraining ? 40f : 100f, i);
            }
            pilot.Perks.Clear(); pilot.Perks.Add(PilotPerk.Toughness); pilot.Perks.Add(PilotPerk.Fireproof);
            float pierce = 0, blast = 0, fire = 100, impact = 0, health = 100;
            WingSurvivalPerks.ProtectPilotDamage(aircraft, ref pierce, ref blast, ref fire, ref impact, ref health);
            Assert.Equal(16.25f, fire);
        }

        [Fact]
        public void SecondChanceSavesExactlyOneFatalHitAndRearmsOnlyAtSortieBoundary()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.SecondChance);
            float p = 50, b = 0, f = 0, i = 0, hp = 100;
            WingSurvivalPerks.ProtectPilotDamage(aircraft, ref p, ref b, ref f, ref i, ref hp);
            Assert.False(pilot.SecondChanceUsed);
            p = 200;
            WingSurvivalPerks.ProtectPilotDamage(aircraft, ref p, ref b, ref f, ref i, ref hp);
            Assert.Equal(1f, hp - p);
            Assert.True(pilot.SecondChanceUsed);
            WingPilotRoster.Assign(aircraft); // Repeated assignment of the same seat cannot recharge it.
            p = 200;
            WingSurvivalPerks.ProtectPilotDamage(aircraft, ref p, ref b, ref f, ref i, ref hp);
            Assert.Equal(200f, p);
            WingPilotRoster.NoteSortie(aircraft);
            Assert.False(pilot.SecondChanceUsed);
            hp = 0;
            WingSurvivalPerks.ProtectPilotDamage(aircraft, ref p, ref b, ref f, ref i, ref hp);
            Assert.Equal(1f, hp - p);
        }

        [Fact]
        public void FuelPerksDependOnAltitudeAndActualWarning()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.CoolHead);
            Assert.Equal(100f, WingSurvivalPerks.FuelUse(aircraft, 100));
            aircraft.Warning.Active = true;
            Assert.Equal(60f, WingSurvivalPerks.FuelUse(aircraft, 100), 4);
            pilot.Perks.Clear(); pilot.Perks.Add(PilotPerk.HighAltitudeCruise);
            Assert.Equal(100f, WingSurvivalPerks.FuelUse(aircraft, 100));
            aircraft.radarAlt = 3000;
            Assert.Equal(60f, WingSurvivalPerks.FuelUse(aircraft, 100), 4);
            pilot.Perks.Add(PilotPerk.FuelDiscipline); pilot.Perks.Add(PilotPerk.CoolHead);
            Assert.InRange(WingSurvivalPerks.FuelUse(aircraft, 100), 25, 25.3f);
            Assert.Equal(-100f, WingSurvivalPerks.FuelUse(aircraft, -100));
        }

        [Fact]
        public void SeekerAndFlightContextDetermineOneInitialRoll()
        {
            var aircraft = Plane();
            aircraft.transform.position = new Vector3(0, 0, 1000);
            aircraft.radarAlt = 1000;
            var pilot = WingPilotRoster.Assign(aircraft);
            foreach (PilotPerk perk in new[] { PilotPerk.HeatGhost, PilotPerk.RadarGhost,
                         PilotPerk.LowLevelEvasion, PilotPerk.NotchExpert })
            {
                pilot.Perks.Clear(); pilot.Perks.Add(perk);
                aircraft.radarAlt = perk == PilotPerk.LowLevelEvasion ? 100 : 1000;
                aircraft.rb.velocity = new Vector3(100, 0, 0);
                var missile = new Missile { targetID = 1, Seeker = perk == PilotPerk.HeatGhost ? "IR" : "ARH" };
                var aim = aircraft.GlobalPosition();
                Random.Next = 0.2f;
                WingSurvivalPerks.BiasGuidance(missile, ref aim);
                Assert.Equal(122500f, (aim - aircraft.GlobalPosition()).sqrMagnitude);
            }
            pilot.Perks.Clear(); pilot.Perks.Add(PilotPerk.LowLevelEvasion);
            aircraft.radarAlt = 1000;
            var highEncounter = new Missile { targetID = 1 };
            var highAim = aircraft.GlobalPosition();
            WingSurvivalPerks.BiasGuidance(highEncounter, ref highAim);
            aircraft.radarAlt = 100;
            WingSurvivalPerks.BiasGuidance(highEncounter, ref highAim);
            Assert.Equal(aircraft.GlobalPosition().Value, highAim.Value);
            pilot.Perks.Clear(); pilot.Perks.Add(PilotPerk.RadarGhost);
            var wrongSeeker = new Missile { targetID = 1, Seeker = "IR" };
            WingSurvivalPerks.BiasGuidance(wrongSeeker, ref highAim);
            Assert.Equal(aircraft.GlobalPosition().Value, highAim.Value);
        }

        [Fact]
        public void CountermeasureHooksBoostAndRestoreNativeInstanceTuning()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.ChaffReflex); pilot.Perks.Add(PilotPerk.FlareReflex);
            pilot.Perks.Add(PilotPerk.EcmSpecialist);
            float interval = 2;
            WingChaffReflexPatch.Prefix(new ChaffEjector { aircraft = aircraft }, ref interval, out var chaffState);
            Assert.Equal(1f, interval);
            WingChaffReflexPatch.Finalizer(ref interval, chaffState);
            Assert.Equal(2f, interval);
            WingFlareReflexPatch.Prefix(new FlareEjector { aircraft = aircraft }, ref interval, out var flareState);
            Assert.Equal(1f, interval);
            WingFlareReflexPatch.Finalizer(ref interval, flareState);
            Assert.Equal(2f, interval);
            float intensity = 100;
            WingEcmSpecialistPatch.Prefix(new RadarJammer { aircraft = aircraft }, ref intensity, out var ecmState);
            Assert.Equal(175f, intensity);
            WingEcmSpecialistPatch.Finalizer(ref intensity, ecmState);
            Assert.Equal(100f, intensity);
            WingEcmSpecialistPatch.Finalizer(ref intensity, null); // Prefix skipped by another patch.
            Assert.Equal(100f, intensity);
        }

        [Fact]
        public void ProgressionAndWeaponPerksHaveIndependentRealEffects()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.FastLearner);
            WingPilotRoster.Award(aircraft, 40, "sortie");
            Assert.Equal(60, pilot.Xp);
            pilot.Perks.Add(PilotPerk.QuickDraw);
            pilot.Perks.Add(PilotPerk.Standoff);
            Assert.Equal(0.65f, WingPilotRoster.ReactionScale(aircraft));
            Assert.Equal(1.25f, WingPilotRoster.EnvelopeScale(aircraft));
            Plugin.Settings.RankEffect.Value = 0;
            Assert.Equal(1f, WingPilotRoster.ReactionScale(aircraft));
            Assert.Equal(1f, WingPilotRoster.EnvelopeScale(aircraft));
        }

        [Fact]
        public void SurvivalistProtectsOnlyTheTrackedLivingPrimarySurvivor()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Survivalist);
            var native = Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            float p = 100, b = 100, f = 100;
            WingSurvivalistPatch.Prefix(native, ref p, ref b, ref f);
            Assert.Equal(40f, p); Assert.Equal(40f, b); Assert.Equal(40f, f);
            native.pilotNumber = 1;
            p = 100;
            WingSurvivalistPatch.Prefix(native, ref p, ref b, ref f);
            Assert.Equal(100f, p);
        }

        [Theory]
        [InlineData(false, 0.349f, true)]
        [InlineData(false, 0.35f, false)]
        [InlineData(true, 0.674f, true)]
        [InlineData(true, 0.675f, false)]
        public void PathfinderWorksAloneAndCombinesWithCommando(bool commando, float roll, bool escapes)
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Pathfinder);
            if (commando) pilot.Perks.Add(PilotPerk.Commando);
            Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            Random.Next = roll;
            WingSearchAndRescue.Tick();
            Time.timeSinceLevelLoad = 59;
            WingSearchAndRescue.Tick();
            Assert.False(WingPilotRoster.IsFree(pilot));
            Time.timeSinceLevelLoad = 60;
            WingSearchAndRescue.Tick();
            Assert.Equal(escapes, WingPilotRoster.IsFree(pilot));
            Assert.Equal(1, Random.Rolls);
        }
    }
}
