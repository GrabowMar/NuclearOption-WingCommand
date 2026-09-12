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
            Plugin.Logger = new Log();
            WingCommandManager.Instance = new WingCommandManager();
            GameManager.LocalPlayer = new Player();
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
        public void ImportedCustomPortraitKeepsSemanticSelectionRatherThanAtlasAddresses()
        {
            var record = new CustomPilotRecord
            {
                Callsign = "PORTRAIT",
                Body = PortraitBody.Female,
                Face = 5,
                Hair = 6,
                Uniform = 3,
                Accessory = 8,
                Backdrop = 3,
            };

            var pilot = WingPilotRoster.ImportCustom(record);
            Assert.True(pilot.HasCustomPortrait);
            Assert.Equal(record.Selection, pilot.PortraitSelection.Value);
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
            Assert.Equal(2, WingCommandManager.Instance.Messages.Count);
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

        [Fact]
        public void MissingPilotCanBeRecoveredByLocalSearchOnlyOnce()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            WingPilotRoster.Retire(1, false);
            Time.timeSinceLevelLoad = 31;
            WingSearchAndRescue.Tick();
            GameManager.LocalPlayer.Allocation = 20_000_000f;
            Assert.True(WingSearchAndRescue.OrganizeLocalRecovery(pilot));
            Assert.False(WingSearchAndRescue.OrganizeLocalRecovery(pilot));
            Assert.Contains("05:00", WingSearchAndRescue.Status(pilot));
            Time.timeSinceLevelLoad = 330;
            WingSearchAndRescue.Tick();
            Assert.False(WingPilotRoster.IsFree(pilot));
            Time.timeSinceLevelLoad = 331;
            WingSearchAndRescue.Tick();
            Assert.True(WingPilotRoster.IsFree(pilot));
            Assert.Equal(10_000_000f, GameManager.LocalPlayer.Allocation);
        }

        [Fact]
        public void LateSurvivorAfterSignalLossStillReceivesNativeRescueOutcomes()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            WingPilotRoster.Retire(1, false);
            Time.timeSinceLevelLoad = 31;
            WingSearchAndRescue.Tick();
            var native = Eject(aircraft);
            Assert.Equal(PilotRecoveryStatus.Downed, pilot.RecoveryStatus);
            native.animationState = PilotDismounted.PilotState.dead;
            WingSearchAndRescue.Observe(native);
            Assert.True(pilot.Lost);
            Assert.False(WingSearchAndRescue.OrganizeLocalRecovery(pilot));
        }

        [Theory]
        [InlineData(0.599f, true)]
        [InlineData(0.60f, false)]
        public void CommandoRollsOnceAndWaitsForEscapeDelay(float roll, bool succeeds)
        {
            Random.Next = roll;
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Commando);
            Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            WingSearchAndRescue.Tick();
            Time.timeSinceLevelLoad = 89;
            WingSearchAndRescue.Tick();
            Assert.False(WingPilotRoster.IsFree(pilot));
            Random.Next = 0; // Failure must not be rerolled later.
            Time.timeSinceLevelLoad = 90;
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
        [InlineData(0.349f, true)]
        [InlineData(0.36f, false)]
        public void GhostRollIsStableAcrossGuidanceUpdatesAndRetargeting(float roll, bool succeeds)
        {
            var aircraft = Plane();
            aircraft.transform.position = new Vector3(0, 0, 1000);
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Ghost);
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
            WingPilotRoster.Assign(aircraft).Perks.Add(PilotPerk.Toughness);
            Assert.True(WingSurvivalPerks.Has(aircraft, PilotPerk.Toughness));
            aircraft.IsServer = false;
            Assert.False(WingSurvivalPerks.Has(aircraft, PilotPerk.Toughness));
            aircraft.IsServer = true;
            Plugin.Settings.RankEffect.Value = 0;
            Assert.False(WingSurvivalPerks.Has(aircraft, PilotPerk.Toughness));
            Plugin.Settings.RankEffect.Value = 1;
            Plugin.Settings.PilotProgression.Value = false;
            Assert.False(WingSurvivalPerks.Has(aircraft, PilotPerk.Toughness));
        }

        [Fact]
        public void ToughnessIsAppliedBeforeFatalDamageIsRecorded()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Toughness);
            object[] args = { aircraft.Pilot, 110f, 0f, 0f, 0f, 100f, (byte)0 };
            typeof(WingPilotFatalDamagePatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
            Assert.Equal(55f, args[1]);
            Assert.Null(pilot.LossCause);
        }

        [Theory]
        [InlineData(101f, 0f, 0f, 0f, "Projectile")]
        [InlineData(0f, 101f, 0f, 0f, "Explosion")]
        [InlineData(0f, 0f, 101f, 0f, "Fire")]
        [InlineData(0f, 0f, 0f, 101f, "Impact / collision")]
        [InlineData(60f, 60f, 0f, 0f, "Projectile + Explosion")]
        public void FatalDamageRetainsCauseAndLateKillerWithoutRecyclingPilot(
            float projectile, float blast, float fire, float impact, string cause)
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            string airframe = pilot.LastAircraft;
            var hook = typeof(WingPilotFatalDamagePatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
            object[] args = { aircraft.Pilot, projectile, blast, fire, impact,
                projectile + blast + fire + impact, (byte)0 };
            hook.Invoke(null, args);
            Assert.Null(pilot.LossCause); // Exactly zero HP is not fatal in the native hook.
            args[5] = 100f;
            aircraft.Pilot.ejected = true;
            hook.Invoke(null, args);
            Assert.Null(pilot.LossCause);
            aircraft.Pilot.ejected = false;
            args[6] = (byte)1;
            hook.Invoke(null, args);
            Assert.Null(pilot.LossCause); // Secondary crew must not retire the assigned pilot.
            args[6] = (byte)0;
            hook.Invoke(null, args);
            Assert.Equal(cause, pilot.LossCause);

            aircraft.Pilot.dead = true; // Native death occurs after the prefix.
            WingPilotRoster.Retire(aircraft.persistentID, false);
            var killer = Plane(2);
            killer.definition.unitName = "SAM launcher";
            WingPilotRoster.RecordKiller(aircraft.persistentID, killer.persistentID);
            WingPilotRoster.RecordKiller(aircraft.persistentID, 0);
            WingPilotRoster.Retire(aircraft.persistentID, true); // Late recovery cannot resurrect a loss.

            Assert.True(pilot.Lost);
            Assert.False(WingPilotRoster.IsFree(pilot));
            Assert.Null(WingPilotRoster.Of(aircraft));
            Assert.Equal("SAM launcher", pilot.KilledBy);
            Assert.Equal(airframe, pilot.LastAircraft);
            Assert.Contains("cause=" + cause, Assert.Single(Plugin.Logger.Warnings));
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
        public void LocalRecoveryChargesTenMillionAndReturnsPilotAfterFiveMinutes()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            var native = Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            GameManager.LocalPlayer.Allocation = 20_000_000f;

            Assert.True(WingSearchAndRescue.OrganizeLocalRecovery(pilot));
            Assert.Equal(10_000_000f, GameManager.LocalPlayer.Allocation);
            Assert.Contains("05:00", WingSearchAndRescue.Status(pilot));

            Time.timeSinceLevelLoad = 299.9f;
            WingSearchAndRescue.Tick();
            Assert.False(WingPilotRoster.IsFree(pilot));

            Time.timeSinceLevelLoad = 300.5f;
            WingSearchAndRescue.Tick();
            Assert.True(WingPilotRoster.IsFree(pilot));
            Assert.Equal(Unit.UnitState.Returned, native.unitState);
            Assert.True(native.disabled);
            Assert.Equal(10_000_000f, GameManager.LocalPlayer.Allocation);
        }

        [Fact]
        public void LocalRecoveryDoesNotStartWithoutEnoughFunds()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            Eject(aircraft);
            WingPilotRoster.Retire(1, false);
            GameManager.LocalPlayer.Allocation = 9_999_999f;

            Assert.False(WingSearchAndRescue.OrganizeLocalRecovery(pilot));
            Time.timeSinceLevelLoad = 300f;
            WingSearchAndRescue.Tick();
            Assert.False(WingPilotRoster.IsFree(pilot));
            Assert.Equal(9_999_999f, GameManager.LocalPlayer.Allocation);
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
        public void ToughnessHalvesAllDamageTypesEqually()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Toughness);
            float p = 100, b = 100, f = 100, i = 100, hp = 1000;
            WingSurvivalPerks.ProtectPilotDamage(aircraft, ref p, ref b, ref f, ref i, ref hp);
            Assert.Equal(50f, p);
            Assert.Equal(50f, b);
            Assert.Equal(50f, f);
            Assert.Equal(50f, i);
        }

        [Fact]
        public void SeekerAndFlightContextDetermineOneInitialRoll()
        {
            var aircraft = Plane();
            aircraft.transform.position = new Vector3(0, 0, 1000);
            aircraft.radarAlt = 1000;
            var pilot = WingPilotRoster.Assign(aircraft);

            // Ghost disrupts all guided missiles regardless of seeker type.
            pilot.Perks.Clear();
            pilot.Perks.Add(PilotPerk.Ghost);
            var missile = new Missile { targetID = 1, Seeker = "IR" };
            var aim = aircraft.GlobalPosition();
            Random.Next = 0.2f;
            WingSurvivalPerks.BiasGuidance(missile, ref aim);
            Assert.Equal(122500f, (aim - aircraft.GlobalPosition()).sqrMagnitude);

            // NotchExpert disrupts radar missiles when beaming.
            pilot.Perks.Clear();
            pilot.Perks.Add(PilotPerk.NotchExpert);
            aircraft.rb.velocity = new Vector3(100, 0, 0);
            missile = new Missile { targetID = 1, Seeker = "ARH" };
            aim = aircraft.GlobalPosition();
            Random.Next = 0.2f;
            WingSurvivalPerks.BiasGuidance(missile, ref aim);
            Assert.Equal(122500f, (aim - aircraft.GlobalPosition()).sqrMagnitude);

            // NotchExpert does not disrupt IR missiles even when beaming.
            var wrongSeeker = new Missile { targetID = 1, Seeker = "IR" };
            var aimMissile = aircraft.GlobalPosition();
            WingSurvivalPerks.BiasGuidance(wrongSeeker, ref aimMissile);
            Assert.Equal(aircraft.GlobalPosition().Value, aimMissile.Value);
        }

        [Fact]
        public void CountermeasureHooksBoostAndRestoreNativeInstanceTuning()
        {
            var aircraft = Plane();
            var pilot = WingPilotRoster.Assign(aircraft);
            pilot.Perks.Add(PilotPerk.Countermeasures);
            float interval = 2;
            WingFlareReflexPatch.Prefix(new FlareEjector { aircraft = aircraft }, ref interval, out var flareState);
            Assert.Equal(1f, interval);
            WingFlareReflexPatch.Finalizer(ref interval, flareState);
            Assert.Equal(2f, interval);
            float intensity = 100;
            WingEcmSpecialistPatch.Prefix(new RadarJammer { aircraft = aircraft }, ref intensity, out var ecmState);
            Assert.Equal(200f, intensity);
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
            Assert.Equal(0.60f, WingPilotRoster.ReactionScale(aircraft));
            Assert.Equal(1.30f, WingPilotRoster.EnvelopeScale(aircraft));
            Plugin.Settings.RankEffect.Value = 0;
            Assert.Equal(1f, WingPilotRoster.ReactionScale(aircraft));
            Assert.Equal(1f, WingPilotRoster.EnvelopeScale(aircraft));
        }
    }
}
