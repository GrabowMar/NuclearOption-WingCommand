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
        public void Delivery_origin_is_the_nearest_stocked_field_even_when_a_far_one_is_ready()
        {
            float[] dist = { 100f, 10_000f };
            bool[] stocks = { true, true };

            Assert.Equal(0, HangarFieldPolicy.SelectNearestStocked(
                dist.Length, i => dist[i], i => stocks[i]));
        }

        [Fact]
        public void Delivery_origin_skips_fields_that_cannot_produce_the_airframe()
        {
            float[] dist = { 10f, 100f };
            bool[] stocks = { false, true };

            Assert.Equal(1, HangarFieldPolicy.SelectNearestStocked(
                dist.Length, i => dist[i], i => stocks[i]));
        }

        [Fact]
        public void Only_nearest_ignores_an_unchecked_closer_field()
        {
            float[] dist = { 10f, 100f };
            bool[] allowed = { false, true };
            bool[] stocks = { true, true };
            bool[] ready = { true, false };

            Assert.Equal(1, HangarFieldPolicy.SelectOrigin(
                dist.Length, HangarLaunchMode.OnlyNearest,
                i => dist[i], i => allowed[i], i => stocks[i], i => ready[i]));
        }

        [Fact]
        public void Only_nearest_pins_a_busy_closer_field_instead_of_a_ready_far_one()
        {
            float[] dist = { 100f, 10_000f };
            bool[] allowed = { true, true };
            bool[] stocks = { true, true };
            bool[] ready = { false, true };

            Assert.Equal(0, HangarFieldPolicy.SelectOrigin(
                dist.Length, HangarLaunchMode.OnlyNearest,
                i => dist[i], i => allowed[i], i => stocks[i], i => ready[i]));
        }

        [Fact]
        public void Any_picks_the_closest_ready_allowed_field_not_a_closer_busy_one()
        {
            float[] dist = { 100f, 500f, 10_000f };
            bool[] allowed = { true, true, true };
            bool[] stocks = { true, true, true };
            bool[] ready = { false, true, true };

            Assert.Equal(1, HangarFieldPolicy.SelectOrigin(
                dist.Length, HangarLaunchMode.Any,
                i => dist[i], i => allowed[i], i => stocks[i], i => ready[i]));
        }

        [Fact]
        public void Any_returns_no_origin_when_nothing_is_ready()
        {
            float[] dist = { 100f, 200f };
            bool[] allowed = { true, true };
            bool[] stocks = { true, true };
            bool[] ready = { false, false };

            Assert.Equal(-1, HangarFieldPolicy.SelectOrigin(
                dist.Length, HangarLaunchMode.Any,
                i => dist[i], i => allowed[i], i => stocks[i], i => ready[i]));
        }

        [Fact]
        public void Empty_allowed_set_has_no_origin()
        {
            float[] dist = { 10f, 20f };
            bool[] allowed = { false, false };
            bool[] stocks = { true, true };
            bool[] ready = { true, true };

            Assert.Equal(-1, HangarFieldPolicy.SelectOrigin(
                dist.Length, HangarLaunchMode.OnlyNearest,
                i => dist[i], i => allowed[i], i => stocks[i], i => ready[i]));
            Assert.Equal(-1, HangarFieldPolicy.SelectOrigin(
                dist.Length, HangarLaunchMode.Any,
                i => dist[i], i => allowed[i], i => stocks[i], i => ready[i]));
        }

        [Fact]
        public void Queued_orders_read_QUE_until_a_hangar_is_claimed()
        {
            Assert.Equal("QUE", HangarFieldPolicy.StatusCode(hangarClaimed: false));
            Assert.Equal("DEPT", HangarFieldPolicy.StatusCode(hangarClaimed: true));
        }

        [Fact]
        public void Climb_out_is_for_a_low_far_rejoin_not_nap_of_earth()
        {
            Assert.True(ClimbOutPolicy.ShouldClimbOut(
                radarAlt: 30f, leaderDistance: 8000f, order: WingOrder.Formation,
                incumbent: false, deliveryPending: false, leaderPresent: true));
            Assert.False(ClimbOutPolicy.ShouldClimbOut(
                radarAlt: 30f, leaderDistance: 80f, order: WingOrder.Formation,
                incumbent: false, deliveryPending: false, leaderPresent: true));
            Assert.False(ClimbOutPolicy.ShouldAbort(
                radarAlt: 20f, leaderDistance: 8000f, order: WingOrder.Attack,
                incumbent: false, deliveryPending: false));
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

        [Fact]
        public void Owned_airframes_bypass_faction_reserve_holding_capacity()
        {
            const int capacity = 3;

            // Faction unpaid stock respects capacity
            Assert.True(ReserveSlotPolicy.CanStoreAirframe(owned: false, currentCount: 0, factionStockCapacity: capacity));
            Assert.True(ReserveSlotPolicy.CanStoreAirframe(owned: false, currentCount: 2, factionStockCapacity: capacity));
            Assert.False(ReserveSlotPolicy.CanStoreAirframe(owned: false, currentCount: 3, factionStockCapacity: capacity));
            Assert.False(ReserveSlotPolicy.CanStoreAirframe(owned: false, currentCount: 5, factionStockCapacity: capacity));

            // Player-owned airframes are never discarded, even when reserve exceeds standard hold capacity
            Assert.True(ReserveSlotPolicy.CanStoreAirframe(owned: true, currentCount: 3, factionStockCapacity: capacity));
            Assert.True(ReserveSlotPolicy.CanStoreAirframe(owned: true, currentCount: 10, factionStockCapacity: capacity));
        }

        [Fact]
        public void Selection_toggle_policy_deselects_all_when_all_active_or_all_selected()
        {
            // In ALL mode, clicking SELECT ALL deselects
            Assert.True(SelectionTogglePolicy.ShouldDeselectAll(isAllMode: true, selectedCount: 4, totalCount: 4));
            Assert.True(SelectionTogglePolicy.ShouldDeselectAll(isAllMode: true, selectedCount: 0, totalCount: 4));

            // In explicit mode, if all members are selected, clicking SELECT ALL deselects all
            Assert.True(SelectionTogglePolicy.ShouldDeselectAll(isAllMode: false, selectedCount: 4, totalCount: 4));

            // In explicit mode with partial selection, clicking SELECT ALL selects all (does not deselect)
            Assert.False(SelectionTogglePolicy.ShouldDeselectAll(isAllMode: false, selectedCount: 2, totalCount: 4));
            Assert.False(SelectionTogglePolicy.ShouldDeselectAll(isAllMode: false, selectedCount: 0, totalCount: 4));
        }

        [Fact]
        public void Selection_toggle_policy_toggles_individual_selection_on_second_click()
        {
            // Clicking the only selected member deselects it
            Assert.True(SelectionTogglePolicy.ShouldDeselectMemberOnClick(
                isExplicitMode: true, selectedCount: 1, isMemberSelected: true));

            // Clicking an unselected member does not deselect
            Assert.False(SelectionTogglePolicy.ShouldDeselectMemberOnClick(
                isExplicitMode: true, selectedCount: 1, isMemberSelected: false));

            // In multi-selection, clicking a member without Shift selects only that member (not deselect all)
            Assert.False(SelectionTogglePolicy.ShouldDeselectMemberOnClick(
                isExplicitMode: true, selectedCount: 3, isMemberSelected: true));

            // In ALL mode, clicking does not trigger single-member deselect
            Assert.False(SelectionTogglePolicy.ShouldDeselectMemberOnClick(
                isExplicitMode: false, selectedCount: 1, isMemberSelected: true));
        }
    }
}
