using System;
using System.Collections.Generic;
using WingCommand;
using Xunit;

namespace WingCommand.PureTests
{
    public class FormationLayoutTests
    {
        public static IEnumerable<object[]> Shapes()
        {
            foreach (FormationShape shape in FormationShapes.All)
                yield return new object[] { (int)shape };
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void LeaderAndUnassignedSlotsStayAtOrigin(int shape)
        {
            foreach (int slot in new[] { -1, 0 })
            {
                SlotLayout leader = FormationLayout.Slot((FormationShape)shape, slot);
                Assert.Equal(0f, leader.Lateral);
                Assert.Equal(0f, leader.Back);
                Assert.Equal(0f, leader.Height);
            }
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void ExtendedFormationRetainsHorizontalClearanceThroughoutTurnCompression(int shape)
        {
            // Include leader, large debug wings, and every intermediate deformation.
            // Ignore stack: terrain floors can flatten it and ships do not use it.
            for (int step = 0; step <= 8; step++)
            {
                float blend = step / 8f;
                float lateral = 1f + (FormationLayout.TurnLateralScale - 1f) * blend;
                float back = 1f + (FormationLayout.TurnBackScale - 1f) * blend;
                for (int slot = 1; slot <= 64; slot++)
                {
                    SlotLayout point = FormationLayout.Slot((FormationShape)shape, slot);
                    Assert.True(float.IsFinite(point.Lateral) && float.IsFinite(point.Back) &&
                                float.IsFinite(point.Height));
                    Assert.True(point.Back >= 0f, $"{(FormationShape)shape} slot {slot} leads the leader");
                    for (int previous = 0; previous < slot; previous++)
                    {
                        SlotLayout other = FormationLayout.Slot((FormationShape)shape, previous);
                        float dx = (point.Lateral - other.Lateral) * lateral;
                        float dz = (point.Back - other.Back) * back;
                        float distance = MathF.Sqrt(dx * dx + dz * dz);
                        Assert.True(distance >= FormationLayout.MinimumPlanarSeparation,
                            $"{(FormationShape)shape} slots {previous}/{slot} gap {distance} at turn {blend}");
                    }
                }
            }
        }

        [Fact]
        public void EchelonsNeverFoldAcrossTheLeaderOrReverseTheirRanks()
        {
            SlotLayout previous = default;
            for (int slot = 1; slot <= 128; slot++)
            {
                SlotLayout right = FormationLayout.Slot(FormationShape.EchelonRight, slot);
                SlotLayout left = FormationLayout.Slot(FormationShape.EchelonLeft, slot);
                Assert.True(right.Lateral > previous.Lateral);
                Assert.True(right.Back > previous.Back);
                Assert.Equal(-right.Lateral, left.Lateral);
                Assert.Equal(right.Back, left.Back);
                Assert.Equal(right.Height, left.Height);
                previous = right;
            }
        }

        [Fact]
        public void LineAbreastStaysOnTheBeamAndVicKeepsPairedArms()
        {
            for (int slot = 1; slot < 64; slot += 2)
            {
                foreach (FormationShape shape in new[] { FormationShape.LineAbreast, FormationShape.Vic })
                {
                    SlotLayout right = FormationLayout.Slot(shape, slot);
                    SlotLayout left = FormationLayout.Slot(shape, slot + 1);
                    Assert.True(right.Lateral > 0f);
                    Assert.Equal(-right.Lateral, left.Lateral);
                    Assert.Equal(right.Back, left.Back);
                    Assert.Equal(right.Height, left.Height);
                    if (shape == FormationShape.LineAbreast) Assert.Equal(0f, right.Back);
                    else Assert.True(right.Back > 0f);
                }
            }
        }

        [Fact]
        public void CombatSpreadExtendsAftInPairsRatherThanGrowingTurnRadius()
        {
            for (int element = 0; element < 32; element++)
            {
                SlotLayout lead = FormationLayout.Slot(FormationShape.CombatSpread, element * 2);
                SlotLayout wing = FormationLayout.Slot(FormationShape.CombatSpread, element * 2 + 1);
                Assert.InRange(lead.Lateral, -0.60f, 0f);
                Assert.InRange(wing.Lateral, 1.60f, 2.30f);
                Assert.True(wing.Lateral - lead.Lateral >= 2f);
                Assert.True(wing.Back > lead.Back);
                Assert.True(wing.Height > lead.Height);
                if (element > 0)
                {
                    SlotLayout preceding = FormationLayout.Slot(FormationShape.CombatSpread, element * 2 - 1);
                    Assert.True(lead.Back - preceding.Back >= 2f);
                    Assert.True(lead.Height > 0f);
                }
            }
        }

        [Fact]
        public void FingerFourKeepsTwoMatchingWingmanIntervals()
        {
            for (int group = 0; group < 16; group++)
            {
                SlotLayout lead = FormationLayout.Slot(FormationShape.FingerFour, group * 4);
                SlotLayout leftWing = FormationLayout.Slot(FormationShape.FingerFour, group * 4 + 1);
                SlotLayout elementLead = FormationLayout.Slot(FormationShape.FingerFour, group * 4 + 2);
                SlotLayout rightWing = FormationLayout.Slot(FormationShape.FingerFour, group * 4 + 3);
                Assert.Equal(lead.Lateral - leftWing.Lateral, rightWing.Lateral - elementLead.Lateral, 4);
                Assert.Equal(leftWing.Back - lead.Back, rightWing.Back - elementLead.Back, 4);
                Assert.Equal(leftWing.Height - lead.Height, rightWing.Height - elementLead.Height, 4);
                Assert.True(leftWing.Lateral < lead.Lateral);
                Assert.True(elementLead.Lateral > lead.Lateral);
            }
        }

        [Fact]
        public void DiamondEdgesRemainEqualAcrossConnectedDiamonds()
        {
            for (int group = 0; group < 16; group++)
            {
                SlotLayout lead = FormationLayout.Slot(FormationShape.Diamond, group * 3);
                SlotLayout right = FormationLayout.Slot(FormationShape.Diamond, group * 3 + 1);
                SlotLayout left = FormationLayout.Slot(FormationShape.Diamond, group * 3 + 2);
                SlotLayout tail = FormationLayout.Slot(FormationShape.Diamond, group * 3 + 3);
                Assert.Equal(-right.Lateral, left.Lateral);
                Assert.Equal(0f, tail.Lateral);
                Assert.Equal(right.Back - lead.Back, tail.Back - right.Back, 4);
                Assert.Equal(right.Back, left.Back);
            }
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void OrdinaryStacksStayNearLeaderAltitudeAndLadderClimbs(int shape)
        {
            for (int slot = 1; slot <= 128; slot++)
            {
                SlotLayout point = FormationLayout.Slot((FormationShape)shape, slot);
                if ((FormationShape)shape == FormationShape.Ladder)
                {
                    Assert.True(point.Height > FormationLayout.Slot((FormationShape)shape, slot - 1).Height);
                    Assert.Equal(0f, point.Lateral);
                }
                else Assert.InRange(point.Height, -1.21f, 3f);
            }
        }
    }
}
