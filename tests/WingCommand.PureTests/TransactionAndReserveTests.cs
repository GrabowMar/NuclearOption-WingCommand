using System;
using System.Collections.Generic;
using Xunit;

namespace WingCommand
{
    public sealed class TransactionAndReserveTests
    {
        [Fact]
        public void Rollback_restores_every_effect_once_even_when_one_compensation_fails()
        {
            int funds = 700;
            int stock = 2;
            int capacity = 1;
            int errors = 0;
            bool failOnce = true;
            var journal = new RollbackJournal();

            journal.Add(() => stock++);
            journal.Add(() =>
            {
                if (!failOnce) return;
                failOnce = false;
                throw new InvalidOperationException("simulated refund failure");
            });
            journal.Add(() => funds += 300);
            journal.Add(() => capacity--);

            Assert.False(journal.Rollback(_ => errors++));
            Assert.True(journal.Rollback(_ => errors++));
            Assert.True(journal.Rollback(_ => errors++));

            Assert.Equal(1000, funds);
            Assert.Equal(3, stock);
            Assert.Equal(0, capacity);
            Assert.Equal(1, errors);
        }

        [Fact]
        public void Commit_discards_compensations()
        {
            int funds = 700;
            var journal = new RollbackJournal();
            journal.Add(() => funds += 300);

            journal.Commit();
            journal.Rollback();

            Assert.Equal(700, funds);
        }

        [Fact]
        public void Pending_capacity_tracks_normal_and_over_limit_orders()
        {
            var capacity = new CapacityReservations();
            capacity.Reserve(overLimit: false);
            capacity.Reserve(overLimit: true);

            Assert.Equal(2, capacity.Wing);
            Assert.Equal(2, capacity.Squadron);
            Assert.Equal(1, capacity.OverLimit);

            capacity.Release(overLimit: false);
            capacity.Release(overLimit: true);
            capacity.Release(overLimit: true); // idempotent floor

            Assert.Equal(0, capacity.Wing);
            Assert.Equal(0, capacity.Squadron);
            Assert.Equal(0, capacity.OverLimit);
        }

        [Fact]
        public void Purchase_and_release_choose_one_concrete_slot_with_its_own_fit()
        {
            var slots = new List<(string Definition, bool Owned, bool Fit, bool Reserved)>
            {
                ("VT-7", true,  true,  false),
                ("VT-7", false, false, false),
                ("VT-7", false, true,  false),
                ("VT-7", true,  true,  true),
            };

            int purchase = ReserveSlotPolicy.SelectForPurchase(
                slots.Count, i => slots[i].Definition == "VT-7",
                i => slots[i].Owned, i => slots[i].Reserved);
            int release = ReserveSlotPolicy.SelectForRelease(
                slots.Count, i => slots[i].Definition == "VT-7",
                i => slots[i].Owned, i => slots[i].Fit, i => slots[i].Reserved);

            Assert.Equal(0, purchase); // already-paid recovered airframe and its fit
            Assert.Equal(1, release);  // manual hold, not either recovered fit
        }

        [Fact]
        public void Rotary_hover_requires_both_a_hovering_leader_and_an_established_slot()
        {
            Assert.False(RotaryHoverPolicy.ShouldHover(
                wasHovering: false, leaderHorizontalSpeed: 0f, horizontalSlotError: 500f,
                spacing: 100f, hoverSpeed: 8f, hysteresis: 3f, stationSpacings: 1.5f));

            Assert.True(RotaryHoverPolicy.ShouldHover(
                wasHovering: false, leaderHorizontalSpeed: 2f, horizontalSlotError: 80f,
                spacing: 100f, hoverSpeed: 8f, hysteresis: 3f, stationSpacings: 1.5f));

            // Once hovering, hysteresis keeps the mode stable through small speed changes.
            Assert.True(RotaryHoverPolicy.ShouldHover(
                wasHovering: true, leaderHorizontalSpeed: 9f, horizontalSlotError: 80f,
                spacing: 100f, hoverSpeed: 8f, hysteresis: 3f, stationSpacings: 1.5f));
        }

        [Fact]
        public void Explicit_orders_own_engagement_while_movement_tasks_only_apply_roe_when_compatible()
        {
            Assert.Equal(OrderEngagementAuthority.ExplicitTarget,
                         OrderRoePolicy.Authority(WingOrder.Attack));
            Assert.Equal(OrderEngagementAuthority.ExplicitTarget,
                         OrderRoePolicy.Authority(WingOrder.FireForEffect));
            Assert.Equal(OrderEngagementAuthority.StandingRoe,
                         OrderRoePolicy.Authority(WingOrder.Formation));
            Assert.Equal(OrderEngagementAuthority.StandingRoe,
                         OrderRoePolicy.Authority(WingOrder.OrbitHere));
            Assert.Equal(OrderEngagementAuthority.AutonomousCombat,
                         OrderRoePolicy.Authority(WingOrder.Engage));

            WingOrder[] defensiveOnly =
            {
                WingOrder.ReturnToBase, WingOrder.FallBack, WingOrder.DeliverCargo,
                WingOrder.LandHere, WingOrder.MoveToPoint,
            };
            foreach (WingOrder order in defensiveOnly)
                Assert.Equal(OrderEngagementAuthority.DefensiveOnly,
                             OrderRoePolicy.Authority(order));
        }
    }
}
