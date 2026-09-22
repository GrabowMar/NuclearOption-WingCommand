using Xunit;

namespace WingCommand.PureTests
{
    public class MapOrderPolicyTests
    {
        [Theory]
        [InlineData(WingOrder.Attack)]
        [InlineData(WingOrder.FireForEffect)]
        [InlineData(WingOrder.OrbitHere)]
        [InlineData(WingOrder.LandHere)]
        [InlineData(WingOrder.SeekAndDestroy)]
        [InlineData(WingOrder.DeliverCargo)]
        public void ArmedOrdersAreIdentifiedConsistently(WingOrder order)
        {
            Assert.True(MapOrderPolicy.ArmsOnMap(order));
            Assert.Equal(WingOrderCatalog.TakesPoint(order), MapOrderPolicy.PlacesPoint(order));
        }

        [Fact]
        public void ImmediateOrdersAreNotArmedForTheMap()
        {
            Assert.False(MapOrderPolicy.ArmsOnMap(WingOrder.Formation));
            Assert.False(MapOrderPolicy.ArmsOnMap(WingOrder.Engage));
            Assert.False(MapOrderPolicy.ArmsOnMap(WingOrder.FallBack));
            Assert.False(MapOrderPolicy.ArmsOnMap(WingOrder.JamTarget));
            Assert.Equal(MapOrderButtonIntent.Ignore,
                MapOrderPolicy.ResolveButton(WingOrder.Formation, alreadyArmedWithThis: false,
                                             hasPlayerTargets: true));
        }

        [Fact]
        public void ContextMenuOffersAttackOnAHostileAndMoveOnEmptyGround()
        {
            var dest = new WingOrder[MapOrderPolicy.ContextOrderCap];
            int enemy = MapOrderPolicy.CopyContextOrders(MapPointerKind.Enemy, rotary: false,
                canCargo: false, canJam: false, dest);
            Assert.True(enemy >= 3);
            Assert.Contains(WingOrder.Attack, dest[..enemy]);
            Assert.Contains(WingOrder.MoveToPoint, dest[..enemy]);
            Assert.Contains(WingOrder.ReturnToBase, dest[..enemy]);

            int empty = MapOrderPolicy.CopyContextOrders(MapPointerKind.Empty, rotary: true,
                canCargo: true, canJam: false, dest);
            Assert.Contains(WingOrder.MoveToPoint, dest[..empty]);
            Assert.Contains(WingOrder.OrbitHere, dest[..empty]);
            Assert.Contains(WingOrder.LandHere, dest[..empty]);
            Assert.DoesNotContain(WingOrder.Attack, dest[..empty]);
        }

        [Fact]
        public void UnarmedRightClickIsAlwaysAMoveEvenOverAHostile()
        {
            Assert.Equal(MapClickIntent.Move,
                MapOrderPolicy.ResolveRightClick(false, WingOrder.Attack, MapPointerKind.Enemy,
                                                 shift: false));
            Assert.Equal(MapClickIntent.QueueMove,
                MapOrderPolicy.ResolveRightClick(false, WingOrder.Attack, MapPointerKind.Enemy,
                                                 shift: true));
            Assert.Equal(MapClickIntent.Move,
                MapOrderPolicy.ResolveRightClick(false, default, MapPointerKind.Empty,
                                                 shift: false));
        }

        [Theory]
        [InlineData(WingOrder.OrbitHere)]
        [InlineData(WingOrder.SeekAndDestroy)]
        [InlineData(WingOrder.LandHere)]
        [InlineData(WingOrder.DeliverCargo)]
        public void ArmedPointOrderPlacesAtTheCursorIncludingOverUnits(WingOrder order)
        {
            Assert.Equal(MapClickIntent.PlacePoint,
                MapOrderPolicy.ResolveRightClick(true, order, MapPointerKind.Empty, shift: false));
            Assert.Equal(MapClickIntent.QueuePlacePoint,
                MapOrderPolicy.ResolveRightClick(true, order, MapPointerKind.Enemy, shift: true));
        }

        [Theory]
        [InlineData(WingOrder.Attack)]
        [InlineData(WingOrder.FireForEffect)]
        public void ArmedAttackUsesTheClickedHostileAndOtherwiseAsksForATarget(WingOrder order)
        {
            Assert.Equal(MapClickIntent.AttackTarget,
                MapOrderPolicy.ResolveRightClick(true, order, MapPointerKind.Enemy,
                                                 shift: false));
            Assert.Equal(MapClickIntent.QueueAttackTarget,
                MapOrderPolicy.ResolveRightClick(true, order, MapPointerKind.Enemy,
                                                 shift: true));
            Assert.Equal(MapClickIntent.NeedTarget,
                MapOrderPolicy.ResolveRightClick(true, order, MapPointerKind.Empty,
                                                 shift: false));
            Assert.Equal(MapClickIntent.NeedTarget,
                MapOrderPolicy.ResolveRightClick(true, order, MapPointerKind.Other,
                                                 shift: false));
        }

        [Fact]
        public void HeightAndSpeedButtonsStepAndClamp()
        {
            float start = MapOrderPolicy.DefaultMoveAltitude(rotary: false);
            Assert.Equal(start + WingTuning.MoveAltitudeStepFixed,
                MapOrderPolicy.StepMoveAltitude(0f, 1, rotary: false));
            Assert.Equal(WingTuning.MoveAltitudeMaxFixed,
                MapOrderPolicy.StepMoveAltitude(WingTuning.MoveAltitudeMaxFixed, 1, rotary: false));
            Assert.Equal(WingTuning.MoveAltitudeMinRotary,
                MapOrderPolicy.StepMoveAltitude(WingTuning.MoveAltitudeMinRotary, -1, rotary: true));

            Assert.Equal(WingTuning.MoveSpeedDefault,
                MapOrderPolicy.StepMoveSpeed(0f, 0));
            Assert.Equal(WingTuning.MoveSpeedMin,
                MapOrderPolicy.StepMoveSpeed(WingTuning.MoveSpeedMin, -1));
            Assert.Equal(WingTuning.MoveSpeedMax,
                MapOrderPolicy.StepMoveSpeed(WingTuning.MoveSpeedMax, 1));
            Assert.Equal(WingTuning.MoveSpeedDefault - WingTuning.MoveSpeedStep,
                MapOrderPolicy.StepMoveSpeed(0f, -1));
        }

        [Fact]
        public void ShiftMoveDoesNotQueueBehindADifferentOrderKind()
        {
            Assert.False(MapOrderPolicy.CanFollowOn(WingOrder.Formation));
            Assert.True(MapOrderPolicy.CanFollowOn(WingOrder.MoveToPoint));
            Assert.False(MapOrderPolicy.ArmsOnMap(WingOrder.StandDown));
            Assert.False(MapOrderPolicy.ArmsOnMap(WingOrder.Engage));
        }

        [Theory]
        [InlineData(WingOrder.Attack)]
        [InlineData(WingOrder.FireForEffect)]
        public void AttackButtonExecutesImmediatelyWhenTargetsAreAlreadyChosen(WingOrder order)
        {
            Assert.Equal(MapOrderButtonIntent.ExecuteAndArm,
                MapOrderPolicy.ResolveButton(order, alreadyArmedWithThis: false,
                                             hasPlayerTargets: true));
            Assert.Equal(MapOrderButtonIntent.Arm,
                MapOrderPolicy.ResolveButton(order, alreadyArmedWithThis: false,
                                             hasPlayerTargets: false));
            Assert.Equal(MapOrderButtonIntent.Disarm,
                MapOrderPolicy.ResolveButton(order, alreadyArmedWithThis: true,
                                             hasPlayerTargets: true));
        }

        [Fact]
        public void PointOrderButtonsToggleAndCargoFallsBackToTheStockRoute()
        {
            Assert.Equal(MapOrderButtonIntent.Arm,
                MapOrderPolicy.ResolveButton(WingOrder.OrbitHere, alreadyArmedWithThis: false,
                                             hasPlayerTargets: false));
            Assert.Equal(MapOrderButtonIntent.Disarm,
                MapOrderPolicy.ResolveButton(WingOrder.OrbitHere, alreadyArmedWithThis: true,
                                             hasPlayerTargets: false));
            Assert.Equal(MapOrderButtonIntent.CargoFallback,
                MapOrderPolicy.ResolveButton(WingOrder.DeliverCargo, alreadyArmedWithThis: true,
                                             hasPlayerTargets: false));
        }

        [Fact]
        public void ArmedPromptsTellThePlayerToLeftClickTheMap()
        {
            Assert.Contains("LEFT-CLICK MAP", MapOrderPolicy.ArmPrompt(WingOrder.OrbitHere));
            Assert.Contains("LEFT-CLICK A HOSTILE", MapOrderPolicy.ArmPrompt(WingOrder.Attack));
            Assert.Contains("LEFT-CLICK A HOSTILE", MapOrderPolicy.ArmPrompt(WingOrder.FireForEffect));
            Assert.Contains("PRESS AGAIN", MapOrderPolicy.ArmPrompt(WingOrder.DeliverCargo));
        }
    }
}
