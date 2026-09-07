using Xunit;

namespace WingCommand.PureTests
{
    public class MapOrderPolicyTests
    {
        [Theory]
        [InlineData(WingOrder.OrbitHere)]
        [InlineData(WingOrder.LandHere)]
        [InlineData(WingOrder.SeekAndDestroy)]
        [InlineData(WingOrder.DeliverCargo)]
        [InlineData(WingOrder.Attack)]
        public void MapArmableOrdersStayInSyncWithTheCatalog(WingOrder order)
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
            Assert.False(MapOrderPolicy.ArmsOnMap(WingOrder.FireForEffect));
            Assert.False(MapOrderPolicy.ArmsOnMap(WingOrder.JamTarget));
            Assert.Equal(MapOrderButtonIntent.Ignore,
                MapOrderPolicy.ResolveButton(WingOrder.Formation, alreadyArmedWithThis: false,
                                             hasPlayerTargets: true));
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

        [Fact]
        public void ArmedAttackUsesTheClickedHostileAndOtherwiseAsksForATarget()
        {
            Assert.Equal(MapClickIntent.AttackTarget,
                MapOrderPolicy.ResolveRightClick(true, WingOrder.Attack, MapPointerKind.Enemy,
                                                 shift: false));
            Assert.Equal(MapClickIntent.QueueAttackTarget,
                MapOrderPolicy.ResolveRightClick(true, WingOrder.Attack, MapPointerKind.Enemy,
                                                 shift: true));
            Assert.Equal(MapClickIntent.NeedTarget,
                MapOrderPolicy.ResolveRightClick(true, WingOrder.Attack, MapPointerKind.Empty,
                                                 shift: false));
            Assert.Equal(MapClickIntent.NeedTarget,
                MapOrderPolicy.ResolveRightClick(true, WingOrder.Attack, MapPointerKind.Other,
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

        [Fact]
        public void AttackButtonExecutesImmediatelyWhenTargetsAreAlreadyChosen()
        {
            Assert.Equal(MapOrderButtonIntent.ExecuteAndArm,
                MapOrderPolicy.ResolveButton(WingOrder.Attack, alreadyArmedWithThis: false,
                                             hasPlayerTargets: true));
            Assert.Equal(MapOrderButtonIntent.Arm,
                MapOrderPolicy.ResolveButton(WingOrder.Attack, alreadyArmedWithThis: false,
                                             hasPlayerTargets: false));
            Assert.Equal(MapOrderButtonIntent.Disarm,
                MapOrderPolicy.ResolveButton(WingOrder.Attack, alreadyArmedWithThis: true,
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
        public void ArmedPromptsTellThePlayerToRightClickTheMap()
        {
            Assert.Contains("RIGHT-CLICK MAP", MapOrderPolicy.ArmPrompt(WingOrder.OrbitHere));
            Assert.Contains("RIGHT-CLICK A HOSTILE", MapOrderPolicy.ArmPrompt(WingOrder.Attack));
            Assert.Contains("PRESS AGAIN", MapOrderPolicy.ArmPrompt(WingOrder.DeliverCargo));
        }
    }
}
