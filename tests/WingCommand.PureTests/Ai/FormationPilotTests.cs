using System;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationPilotTests
    {
        private const float Dt = 1f / 60f;
        private static readonly AirframeProfile Fighter = new AirframeProfile();
        private static readonly Vec3 North = new Vec3(0f, 0f, 200f);

        /// <summary>Leader straight north at 200 m/s; the test places the member itself (no plant).</summary>
        private sealed class Rig
        {
            public readonly FormationWing Wing = new FormationWing(new FormationDefinition
            {
                Id = "one", Slots = new[] { new SlotDef(-1f, 1f, 0f) }, Element = new[] { 0 },
                SpacingMin = 40f, SpacingDefault = 80f, SpacingMax = 160f,
            }, 80f);
            public readonly FormationPilot Pilot = new FormationPilot(0, AirframeClass.FixedWing);
            public readonly WingEventRing Events = new WingEventRing();
            private readonly WingMemberInput[] members = new WingMemberInput[1];
            public Vec3 LeaderPos = new Vec3(0f, 2000f, 0f);
            private float time;

            public WingFrame Step(bool leaderPresent = true, Vec3? memberPos = null, Vec3? memberVel = null,
                float floorY = float.NaN)
            {
                time += Dt;
                LeaderPos += North * Dt;
                var leader = new AnchorSample { Pos = LeaderPos, Vel = North, Present = leaderPresent, Airborne = true };
                AircraftState s = TestStates.Flying(memberPos ?? LeaderPos + new Vec3(-80f, 0f, -80f), memberVel ?? North);
                members[0] = new WingMemberInput
                {
                    State = s, Capability = new MemberCapability { MaxSpeed = 300f, MinSpeed = 80f }, Radius = 8f,
                };
                WingFrame frame = Wing.Update(leader, members, 1, floorY, 60f, 8f, Dt);
                Pilot.Step(frame, s, Fighter, time, Dt, Events);
                return frame;
            }
        }

        [Fact]
        public void HoldOrbitFliesAtTheMembersCruiseNeverUnderItsSafeMinimum()
        {
            // A helicopter's reference airspeed can be a jet-like default (170 m/s); its hold orbit must use its cruise.
            AirframeProfile helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 80f });
            Assert.Equal(0.75f * helo.CruiseSpeed, FormationPilot.OrbitSpeed(helo), 3);
            var slowJet = new AirframeProfile { StallSpeed = 60f, CruiseSpeed = 100f };
            Assert.Equal(1.5f * slowJet.MinimumSpeed(1f), FormationPilot.OrbitSpeed(slowJet), 3);
        }

        [Fact]
        public void AnOwnIntentStillFliesTheWingsCollisionBias()
        {
            // Review M3b C1: the recovery approach dropped the frame's collision bias.
            var rig = new Rig();
            WingFrame frame = rig.Step();
            frame.Bias[0] = new Vec3(3f, 0f, 0f);
            AircraftState s = TestStates.Flying(rig.LeaderPos + new Vec3(-80f, 0f, -80f), North);
            var intent = new FlightIntent
            {
                Ref = new RefState(s.Pos + North * 10f, North, Vec3.Zero), Limits = new SpeedLimits(80f, 300f, false, true),
            };
            rig.Pilot.FlyIntent(intent, frame, s, Fighter, Dt);
            Assert.True(rig.Pilot.Pipeline.Report.CollisionActive);
        }

        [Fact]
        public void IntentCarriesTheLeadersHeadingForSlowMembersToFace()
        {
            var rig = new Rig();
            rig.Step();
            Assert.True(rig.Pilot.LastIntent.HasHeading);
            Assert.Equal(0f, rig.Pilot.LastIntent.HeadingDeg, 1);   // the leader flies north
        }

        [Fact]
        public void TrailWithoutAFrameReferenceFallsBackToTheRejoinReference()
        {
            // Review M2b I3: the tick the mind entered Trail it flew the frame's stale trail reference (default: the
            // map origin), because the frame computes trail references from the role of the previous tick.
            AirframeProfile helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 80f });
            var wing = new FormationWing(new FormationDefinition
            {
                Id = "one", Slots = new[] { new SlotDef(-1f, 1f, 0f) }, Element = new[] { 0 },
                SpacingMin = 40f, SpacingDefault = 80f, SpacingMax = 160f,
            }, 80f);
            var pilot = new FormationPilot(0, AirframeClass.Rotary);
            var members = new WingMemberInput[1];
            Vec3 lead = new Vec3(0f, 2000f, 0f);
            float nearestToOrigin = float.MaxValue;
            for (int i = 0; i < 8 * 60; i++)
            {
                lead += North * Dt;
                AircraftState s = TestStates.Flying(lead + new Vec3(0f, -150f, -3000f), new Vec3(0f, 0f, 60f));
                members[0] = new WingMemberInput { State = s, Capability = new MemberCapability { MaxSpeed = 64f }, Radius = 9f };
                WingFrame frame = wing.Update(new AnchorSample { Pos = lead, Vel = North, Present = true, Airborne = true },
                    members, 1, float.NaN, 60f, 8f, Dt);
                pilot.Step(frame, s, helo, i * Dt, Dt, null);
                if (pilot.Mind.Current == BehaviourId.Trail) nearestToOrigin = Math.Min(nearestToOrigin, pilot.LastIntent.Ref.Pos.Length);
            }
            Assert.Equal(BehaviourId.Trail, pilot.Mind.Current);
            Assert.True(nearestToOrigin > 1000f, $"reference {nearestToOrigin:0} m from the map origin");
        }

        [Fact]
        public void ATiltwingConversionIsLogged()
        {
            // Review M2b I8: every mode change is logged with a reason.
            AirframeProfile tilt = AirframeProfile.Derive(new ProfileInputs
            {
                Class = AirframeClass.Tiltwing, PublishedStallKmh = 45f * 3.6f, MaxSpeed = 150f,
            });
            var rig = new Rig();
            var pilot = new FormationPilot(0, AirframeClass.Tiltwing);
            var events = new WingEventRing();
            var members = new WingMemberInput[1];
            Vec3 lead = new Vec3(0f, 2000f, 0f);
            for (int i = 0; i < 6 * 60; i++)
            {
                float speed = i < 60 ? 80f : 30f;
                lead += North * Dt;
                AircraftState s = TestStates.Flying(lead + new Vec3(-80f, 0f, -80f), new Vec3(0f, 0f, speed));
                members[0] = new WingMemberInput { State = s, Capability = new MemberCapability { MaxSpeed = 150f }, Radius = 7f };
                WingFrame frame = rig.Wing.Update(new AnchorSample { Pos = lead, Vel = North, Present = true, Airborne = true },
                    members, 1, float.NaN, 60f, 8f, Dt);
                pilot.Step(frame, s, tilt, i * Dt, Dt, events);
            }
            Assert.Equal(1, events.CountOf(WingEventKind.Converted, 0));
        }

        [Fact]
        public void MemberInItsSlotIsCapturedAndThenTracksTheSlot()
        {
            var rig = new Rig();
            WingFrame frame = null;
            for (int i = 0; i < 4 * 60; i++) frame = rig.Step();
            Assert.Equal(BehaviourId.StationKeep, rig.Pilot.Mind.Current);
            Assert.Equal(frame.Slots[0].Ref.Pos, rig.Pilot.LastIntent.Ref.Pos);
            Assert.Equal(1, rig.Events.CountOf(WingEventKind.BehaviourChanged, 0));
            Assert.Equal(TransitionReason.Captured, rig.Events[0].Reason);
        }

        [Fact]
        public void RejoinLanesClearTheCollisionBiasRadius()
        {
            // Adjacent lanes closer than the bias radius would keep crossing rejoins inside it: lane 1 must sit at
            // least R + 5 m below the leader (R = max(2·8 + 15, 0.35·80) = 31 m).
            var rig = new Rig();
            rig.Step(memberPos: rig.LeaderPos + new Vec3(-80f, 0f, -5000f));
            float depth = rig.Wing.Frame.Leader.Pos.Y - rig.Pilot.LastIntent.Ref.Pos.Y;
            Assert.True(depth >= CollisionBias.RadiusFor(8f, 80f) + 5f - 0.01f, $"lane 1 only {depth:0.0} m below the leader");
        }

        [Fact]
        public void LeaderLossSendsTheMemberToAHoldAboveTheLastEstimate()
        {
            var rig = new Rig();
            for (int i = 0; i < 60; i++) rig.Step();
            float leaderY = rig.Wing.Frame.Leader.Pos.Y;
            rig.Step(leaderPresent: false);
            Assert.Equal(BehaviourId.HoldOverhead, rig.Pilot.Mind.Current);
            Assert.Equal(leaderY + HoldOrbit.BaseHeight + HoldOrbit.SlotHeight, rig.Pilot.LastIntent.Ref.Pos.Y, 1);
            Assert.Equal(TransitionReason.LeaderLost, rig.Events[rig.Events.Count - 1].Reason);
        }

        [Fact]
        public void GcasActivationIsLoggedOncePerActivation()
        {
            var rig = new Rig();
            for (int i = 0; i < 3; i++)
                rig.Step(memberPos: rig.LeaderPos + new Vec3(-80f, -1850f, -80f), memberVel: new Vec3(0f, -60f, 190f), floorY: 0f);
            Assert.True(rig.Pilot.Pipeline.GcasActive);
            Assert.Equal(1, rig.Events.CountOf(WingEventKind.GcasActivated, 0));
        }

        [Fact]
        public void FrameCollisionBiasReachesTheConstraintChain()
        {
            var rig = new Rig();
            for (int i = 0; i < 30; i++) rig.Step(memberPos: rig.LeaderPos + new Vec3(5f, 0f, 0f));
            Assert.True(rig.Pilot.Pipeline.Report.CollisionActive);
            Assert.Equal(1, rig.Events.CountOf(WingEventKind.CollisionEmergency, 0));
        }

        [Fact]
        public void FallingBehindIsLoggedOnce()
        {
            var rig = new Rig();
            rig.Pilot.AfterburnerAllowed = false;   // usable 255 m/s against a 200 m/s leader 20 km ahead: 360 s
            for (int i = 0; i < 15 * 60; i++)
                rig.Step(memberPos: rig.LeaderPos + new Vec3(-80f, 0f, -20000f), memberVel: new Vec3(0f, 0f, 150f));
            Assert.Equal(1, rig.Events.CountOf(WingEventKind.FallingBehind, 0));
            Assert.True(rig.Pilot.LastRejoin.FallingBehind);
        }

        [Fact]
        public void StepKeepsTheGuidanceCommandForTheOverlay()
        {
            var rig = new Rig();
            rig.Step(memberPos: rig.LeaderPos + new Vec3(-80f, 0f, -600f));
            Assert.NotEqual(Vec3.Zero, rig.Pilot.LastGuidance.VelCmd);
        }

        [Fact]
        public void FormUpLogsACommandedRejoin()
        {
            var pilot = new FormationPilot(1, AirframeClass.FixedWing);
            var events = new WingEventRing();
            pilot.FormUp(3f, events);
            Assert.Equal(0, events.Count);   // already rejoining
            pilot.Mind.Force(BehaviourId.StationKeep);
            pilot.FormUp(4f, events);
            Assert.Equal(1, events.Count);
            WingEvent e = events[0];
            Assert.Equal(WingEventKind.BehaviourChanged, e.Kind);
            Assert.Equal(BehaviourId.StationKeep, e.From);
            Assert.Equal(BehaviourId.Rejoin, e.To);
            Assert.Equal(TransitionReason.Commanded, e.Reason);
            Assert.Equal(1, e.Member);
        }

        [Fact]
        public void AMissileTakesTheMemberIntoDefendAtOnceAndBackToRejoinAfter()
        {
            // Spec M5 §7.2: a Survive behaviour pre-empts the dwell and the leader-lost switch, and hands back to Rejoin.
            var rig = new Rig();
            for (int i = 0; i < 60; i++) rig.Step();
            rig.Pilot.Threat = new MissileThreat
            {
                Present = true, Pos = rig.LeaderPos + new Vec3(0f, 0f, 4000f), Vel = new Vec3(0f, 0f, -600f), Seeker = MissileSeeker.Radar,
            };
            for (int i = 0; i < 90; i++) rig.Step();
            Assert.Equal(BehaviourId.Defend, rig.Pilot.Mind.Current);
            for (int i = 0; i < 60; i++) rig.Step(leaderPresent: false);
            Assert.Equal(BehaviourId.Defend, rig.Pilot.Mind.Current);
            rig.Pilot.FormUp(0f, rig.Events);
            Assert.Equal(BehaviourId.Defend, rig.Pilot.Mind.Current);
            rig.Pilot.Threat = default;
            for (int i = 0; i < 90; i++) rig.Step();
            Assert.Equal(BehaviourId.Rejoin, rig.Pilot.Mind.Current);
            int inbound = 0, clear = 0;
            for (int i = 0; i < rig.Events.Count; i++)
            {
                if (rig.Events[i].Reason == TransitionReason.MissileInbound) inbound++;
                if (rig.Events[i].Reason == TransitionReason.MissileClear) clear++;
            }
            Assert.Equal(1, inbound);
            Assert.Equal(1, clear);
        }

        private static MissileThreat Infrared(Vec3 at) => new MissileThreat
        {
            Present = true, Id = 1, Pos = at + new Vec3(0f, 0f, 4000f), Vel = new Vec3(0f, 0f, -600f), Seeker = MissileSeeker.Infrared,
        };

        [Fact]
        public void ARotaryMembersCollectiveIsNeverOverridden()
        {
            // Review M5c C1: idle throttle is a helicopter's collective at 0: it fell.
            AirframeProfile helo = AirframeProfile.Derive(new ProfileInputs { Class = AirframeClass.Rotary, MaxSpeed = 80f });
            var wing = new FormationWing(new FormationDefinition
            {
                Id = "one", Slots = new[] { new SlotDef(-1f, 1f, 0f) }, Element = new[] { 0 },
                SpacingMin = 40f, SpacingDefault = 80f, SpacingMax = 160f,
            }, 80f);
            var pilot = new FormationPilot(0, AirframeClass.Rotary);
            var members = new WingMemberInput[1];
            Vec3 lead = new Vec3(0f, 300f, 0f), slow = new Vec3(0f, 0f, 40f);
            for (int i = 0; i < 4 * 60; i++)
            {
                lead += slow * Dt;
                AircraftState s = TestStates.Flying(lead + new Vec3(-80f, 0f, -80f), slow);
                if (i >= 60) pilot.Threat = Infrared(s.Pos);
                members[0] = new WingMemberInput { State = s, Capability = new MemberCapability { MaxSpeed = 64f }, Radius = 9f };
                WingFrame frame = wing.Update(new AnchorSample { Pos = lead, Vel = slow, Present = true, Airborne = true },
                    members, 1, float.NaN, 60f, 8f, Dt);
                pilot.Step(frame, s, helo, i * Dt, Dt, null);
            }
            Assert.Equal(BehaviourId.Defend, pilot.Mind.Current);
            Assert.True(pilot.LastOutput.Throttle > 0.2f, $"collective {pilot.LastOutput.Throttle:0.00}");
        }

        [Fact]
        public void EndDefenceHandsBackToRejoinWithAReason()
        {
            // Review M5c I3: a member recovered or engaged mid-defence must not keep Defend (Form Up was ignored).
            var rig = new Rig();
            for (int i = 0; i < 60; i++) rig.Step();
            rig.Pilot.Threat = Infrared(rig.LeaderPos);
            for (int i = 0; i < 90; i++) rig.Step();
            Assert.Equal(BehaviourId.Defend, rig.Pilot.Mind.Current);
            rig.Pilot.EndDefence(10f, rig.Events);
            Assert.Equal(BehaviourId.Rejoin, rig.Pilot.Mind.Current);
            Assert.False(rig.Pilot.Defence.Active);
            WingEvent last = rig.Events[rig.Events.Count - 1];
            Assert.Equal(TransitionReason.Commanded, last.Reason);
            Assert.Equal(BehaviourId.Defend, last.From);
        }

        [Fact]
        public void IdleIsNotForcedOnASlowMember()
        {
            // Review M5c I5: idle below 1.3 × the loaded minimum speed (or under GCAS) risks a stall at low level.
            var rig = new Rig();
            float slow = 1.1f * Fighter.MinimumSpeed(1f);
            Vec3 vel = new Vec3(0f, 0f, slow);
            for (int i = 0; i < 60; i++) rig.Step(memberVel: vel);
            rig.Pilot.Threat = Infrared(rig.LeaderPos);
            for (int i = 0; i < 90; i++) rig.Step(memberVel: vel);
            Assert.Equal(BehaviourId.Defend, rig.Pilot.Mind.Current);
            Assert.True(rig.Pilot.LastOutput.Throttle > 0f, "idle forced on a member barely above its minimum speed");
        }

        [Fact]
        public void TheThrottlePicksUpFromTheOverrideWhenItEnds()
        {
            // Review M5c I4: the first tick without the override jumped from idle to the energy loop's own output.
            var rig = new Rig();
            for (int i = 0; i < 120; i++) rig.Step();
            rig.Pilot.Threat = Infrared(rig.LeaderPos);
            for (int i = 0; i < 180; i++) rig.Step();
            Assert.Equal(0f, rig.Pilot.LastOutput.Throttle);
            rig.Pilot.Threat = default;
            rig.Step();
            Assert.True(rig.Pilot.LastOutput.Throttle < 0.2f, $"throttle jumped to {rig.Pilot.LastOutput.Throttle:0.00}");
        }

        private static AircraftState Member(Rig rig) => TestStates.Flying(rig.LeaderPos + new Vec3(-80f, 0f, -80f), North);

        private static string Break(Rig rig, ReactionKind kind = ReactionKind.BreakRight) =>
            rig.Pilot.React(new ReactionOrder { Kind = kind }, Member(rig), 2000f, Fighter, 1f, rig.Events);

        [Fact]
        public void AReactionIsLoggedAndEndsIntoRejoinWithManeuverDone()
        {
            var rig = new Rig();
            for (int i = 0; i < 60; i++) rig.Step();
            Assert.Null(Break(rig));
            Assert.Equal(BehaviourId.React, rig.Pilot.Mind.Current);
            WingEvent start = rig.Events[rig.Events.Count - 1];
            Assert.Equal((BehaviourId.React, TransitionReason.Commanded), (start.To, start.Reason));
            rig.Step();
            Assert.Equal(ReactionManeuver.Aggression, rig.Pilot.LastIntent.Aggression);
            Assert.False(rig.Pilot.LastIntent.HasHeading);
            for (int i = 0; i < (int)(ReactionManeuver.BreakSeconds * 60f) + 5; i++) rig.Step();
            Assert.Equal(BehaviourId.Rejoin, rig.Pilot.Mind.Current);
            Assert.False(rig.Pilot.Reaction.Active);
            int done = 0;
            for (int i = 0; i < rig.Events.Count; i++)
                if (rig.Events[i].From == BehaviourId.React && rig.Events[i].Reason == TransitionReason.ManeuverDone) done++;
            Assert.Equal(1, done);
        }

        [Fact]
        public void AMissilePreemptsAReactionAtOnceAndIsNotResumed()
        {
            var rig = new Rig();
            for (int i = 0; i < 60; i++) rig.Step();
            Assert.Null(Break(rig, ReactionKind.PullUp));
            rig.Pilot.Threat = Infrared(rig.LeaderPos);
            for (int i = 0; i < 90; i++) rig.Step();
            Assert.Equal(BehaviourId.Defend, rig.Pilot.Mind.Current);
            Assert.False(rig.Pilot.Reaction.Active);
            bool preempted = false;
            for (int i = 0; i < rig.Events.Count; i++)
                preempted |= rig.Events[i].From == BehaviourId.React && rig.Events[i].To == BehaviourId.Defend
                             && rig.Events[i].Reason == TransitionReason.MissileInbound;
            Assert.True(preempted);
            rig.Pilot.Threat = default;
            for (int i = 0; i < 30 * 60; i++)
            {
                rig.Step();
                Assert.NotEqual(BehaviourId.React, rig.Pilot.Mind.Current);
            }
        }

        [Fact]
        public void FormUpEndsAReactionAsCommanded()
        {
            var rig = new Rig();
            for (int i = 0; i < 60; i++) rig.Step();
            Break(rig);
            rig.Pilot.FormUp(2f, rig.Events);
            Assert.Equal(BehaviourId.Rejoin, rig.Pilot.Mind.Current);
            Assert.False(rig.Pilot.Reaction.Active);
            WingEvent last = rig.Events[rig.Events.Count - 1];
            Assert.Equal((BehaviourId.React, BehaviourId.Rejoin, TransitionReason.Commanded), (last.From, last.To, last.Reason));
        }

        [Fact]
        public void EndDefenceAlsoEndsAReaction()
        {
            // Engage, release, recovery and settle all leave formation flight through EndDefence.
            var rig = new Rig();
            for (int i = 0; i < 60; i++) rig.Step();
            Break(rig);
            rig.Pilot.EndDefence(2f, rig.Events);
            Assert.Equal(BehaviourId.Rejoin, rig.Pilot.Mind.Current);
            Assert.False(rig.Pilot.Reaction.Active);
            Assert.Equal(TransitionReason.Commanded, rig.Events[rig.Events.Count - 1].Reason);
        }

        [Fact]
        public void AReactionIsRefusedWhileDefendingAndInsideItsDwell()
        {
            var rig = new Rig();
            for (int i = 0; i < 60; i++) rig.Step();
            Assert.Null(Break(rig));
            Assert.Equal("still maneuvering", Break(rig, ReactionKind.BreakLeft));
            for (int i = 0; i < (int)(ReactionManeuver.MinDwell * 60f) + 2; i++) rig.Step();
            Assert.Null(Break(rig, ReactionKind.BreakLeft));
            Assert.Equal(ReactionKind.BreakLeft, rig.Pilot.Reaction.Kind);
            for (int i = 0; i < (int)(ReactionManeuver.MinDwell * 60f) + 2; i++) rig.Step();
            Assert.Equal("too low (below 120 m)", rig.Pilot.React(new ReactionOrder { Kind = ReactionKind.Split }, Member(rig), 50f, Fighter, 9f, rig.Events));

            var defending = new Rig();
            for (int i = 0; i < 60; i++) defending.Step();
            defending.Pilot.Threat = Infrared(defending.LeaderPos);
            for (int i = 0; i < 90; i++) defending.Step();
            Assert.Equal("defending", Break(defending));
        }

        [Fact]
        public void AReactionStillFliesTheFrameCollisionBiasAndFloor()
        {
            var rig = new Rig();
            for (int i = 0; i < 60; i++) rig.Step();
            Assert.Null(Break(rig));
            for (int i = 0; i < 30; i++) rig.Step(memberPos: rig.LeaderPos + new Vec3(5f, 0f, 0f), floorY: rig.LeaderPos.Y + 200f);
            Assert.Equal(BehaviourId.React, rig.Pilot.Mind.Current);
            Assert.True(rig.Pilot.Pipeline.Report.CollisionActive);
            Assert.Equal(ConstraintId.Terrain, rig.Pilot.Pipeline.Report.VerticalBy);
        }
    }
}
