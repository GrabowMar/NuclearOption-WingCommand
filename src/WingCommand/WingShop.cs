using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Buying wingmen: catalogue, pricing and the purchase transaction. No UI.
    ///
    /// The whole economy is the game's own and needs no patching. Aircraft are priced from
    /// <c>AircraftDefinition.value</c>, the same field the player's own aircraft menu
    /// prices from; they are paid for out of <c>Player.Allocation</c>, the same pool that
    /// buys the player's own airframe and weapons; and they are drawn from the faction's
    /// stock through <c>FactionHQ.AddSupplyUnit</c>, the exact call the game's reserve flow
    /// uses. Buying a wingman therefore competes with the mission's own AI for airframes,
    /// which is the point.
    /// </summary>
    internal static class WingShop
    {
        /// <summary>One line in the shop.</summary>
        internal readonly struct Offer
        {
            public readonly AircraftDefinition Definition;
            public readonly string Name;
            public readonly float BasePrice;
            public readonly int Stock;

            public Offer(AircraftDefinition definition, string name, float basePrice, int stock)
            {
                Definition = definition;
                Name = name;
                BasePrice = basePrice;
                Stock = stock;
            }
        }

        /// <summary>
        /// One authoritative answer to "can this be bought now?" Shared by the button and
        /// the mutation path so the UI cannot promise a purchase that <see cref="Buy"/>
        /// immediately rejects for host, roster, stock, rank, squadron or funds.
        /// </summary>
        internal readonly struct PurchaseQuote
        {
            public readonly bool CanBuy;
            public readonly string Reason;
            public readonly float Price;
            public readonly int Stock;
            public readonly bool OverLimit;
            public readonly WingLoadoutChoice Loadout;

            internal readonly Player Player;
            internal readonly FactionHQ Hq;
            internal readonly WingSupplyReserve.Source Source;
            internal readonly bool Declared;

            internal PurchaseQuote(bool canBuy, string reason, float price, int stock,
                                   bool overLimit, WingLoadoutChoice loadout, Player player,
                                   FactionHQ hq, WingSupplyReserve.Source source, bool declared)
            {
                CanBuy = canBuy;
                Reason = reason;
                Price = price;
                Stock = stock;
                OverLimit = overLimit;
                Loadout = loadout;
                Player = player;
                Hq = hq;
                Source = source;
                Declared = declared;
            }
        }

        /// <summary>
        /// Funds, stock, fit and both capacity reservations for one accepted order.
        ///
        /// The economy is reserved before a field is asked to spawn anything, committed only
        /// when that exact aircraft registers, and restored by an idempotent rollback if no
        /// aircraft arrives. This object is the single owner of every compensating action.
        /// </summary>
        internal sealed class PurchaseTransaction
        {
            internal enum State { Reserving, AwaitingAircraft, RollingBack, Committed, RolledBack }

            internal readonly AircraftDefinition Definition;
            internal readonly Player Player;
            internal readonly FactionHQ Hq;
            internal readonly float Price;
            internal readonly bool OverLimit;
            internal readonly bool Declared;
            internal readonly WingSupplyReserve.Source Source;
            internal readonly WingLoadoutChoice Loadout;

            internal WingSupplyReserve.Slot ReserveSlot;
            internal State Status { get; private set; }

            private readonly RollbackJournal rollback = new RollbackJournal();
            private bool capacityReserved;

            internal PurchaseTransaction(AircraftDefinition definition, PurchaseQuote quote)
            {
                Definition = definition;
                Player = quote.Player;
                Hq = quote.Hq;
                Price = quote.Price;
                OverLimit = quote.OverLimit;
                Declared = quote.Declared;
                Source = quote.Source;
                Loadout = quote.Loadout;
                Status = State.Reserving;
            }

            internal void NoteReserveSlot(WingSupplyReserve.Slot slot)
            {
                ReserveSlot = slot;
                rollback.Add(() => WingSupplyReserve.CancelPurchase(slot));
            }

            internal void NoteFundsDebit() =>
                rollback.Add(() => Player?.AddAllocation(Price));

            internal void NoteFactionStockDebit() =>
                rollback.Add(() => Hq?.AddSupplyUnit(Definition, 1));

            internal void NoteUndeclaredStockDebit() =>
                rollback.Add(() => DecrementUndeclared(Definition));

            internal void ReserveCapacity()
            {
                capacityReservations.Reserve(OverLimit);
                capacityReserved = true;
                Status = State.AwaitingAircraft;
            }

            internal bool Commit(Aircraft aircraft)
            {
                if (Status != State.AwaitingAircraft || aircraft == null) return false;

                if (ReserveSlot != null && !WingSupplyReserve.CommitPurchase(ReserveSlot))
                {
                    Plugin.Logger.LogError(
                        "[Shop] delivered " + Definition.unitName +
                        " but its concrete reserve slot was no longer present");
                }

                rollback.Commit();
                ReleaseCapacity();
                Status = State.Committed;
                activeTransactions.Remove(this);
                NoteDelivery(aircraft, OverLimit, Loadout);
                return true;
            }

            internal bool Rollback(string reason)
            {
                if (Status == State.Committed || Status == State.RolledBack) return true;
                Status = State.RollingBack;

                if (!rollback.Rollback(e => Plugin.Logger.LogError(
                    "[Shop] rollback of " +
                    (Definition != null ? Definition.unitName : "order") + " failed: " + e)))
                    return false;

                ReleaseCapacity();
                Status = State.RolledBack;
                activeTransactions.Remove(this);

                Plugin.Logger.LogWarning(
                    "[Shop] cancelled " + (Definition != null ? Definition.unitName : "order") +
                    "; funds and stock restored (" + reason + ")");
                return true;
            }

            private void ReleaseCapacity()
            {
                if (!capacityReserved) return;
                capacityReservations.Release(OverLimit);
                capacityReserved = false;
            }
        }

        private static readonly List<Offer> catalogue = new List<Offer>();
        private static readonly HashSet<AircraftDefinition> listedDefinitions =
            new HashSet<AircraftDefinition>();
        private static readonly HashSet<PurchaseTransaction> activeTransactions =
            new HashSet<PurchaseTransaction>();

        /// <summary>
        /// Whether the player has chosen to requisition past the mission's AI aircraft cap.
        ///
        /// Off by default and deliberately explicit: exceeding the cap costs several times
        /// list price, and a surcharge that applied itself without being asked for would be
        /// a worse deal than the one the player thought they were taking.
        /// </summary>
        public static bool ExceedLimit { get; set; }

        /// <summary>Forget what was bought, and the over-limit choice, when a mission ends.</summary>
        public static void Reset()
        {
            foreach (PurchaseTransaction transaction in
                     new List<PurchaseTransaction>(activeTransactions))
                transaction.Rollback("mission reset");
            activeTransactions.Clear();
            listedDefinitions.Clear();
            undeclaredBought.Clear();
            purchasedAircraft.Clear();
            overLimitAircraft.Clear();
            capacityReservations.Reset();
            ExceedLimit = false;
            squadronCachedAt = float.MinValue;
            rotaryCache.Clear();
            autopilotCache.Clear();
            WingLoadoutBook.Reset();
            WingLoadoutCatalog.Reset();
        }

        /// <summary>Retry compensations that an external game API did not accept last frame.</summary>
        public static void Tick()
        {
            // The normal case is no in-flight purchase. Avoid allocating an empty snapshot
            // every frame; a snapshot is still required while Rollback may mutate the set.
            if (activeTransactions.Count == 0) return;

            foreach (PurchaseTransaction transaction in
                     new List<PurchaseTransaction>(activeTransactions))
                if (transaction.Status == PurchaseTransaction.State.RollingBack)
                    transaction.Rollback("retrying failed compensation");
        }

        // Whether a definition is a helicopter, resolved once per airframe.
        //
        // Nothing on AircraftDefinition says so - there is no category field and
        // AircraftParameters has no rotary flag - so the answer comes from the prefab's own
        // autopilot component, which is the same thing WingRegistry.IsRotary asks of a live
        // aircraft. Cached because the catalogue rebuilds several times a second and
        // GetComponentInChildren on a prefab is not free.
        private static readonly Dictionary<AircraftDefinition, bool> rotaryCache =
            new Dictionary<AircraftDefinition, bool>();

        /// <summary>True when this airframe flies on rotors rather than wings.</summary>
        public static bool IsRotary(AircraftDefinition definition)
        {
            if (definition == null) return false;

            if (rotaryCache.TryGetValue(definition, out bool cached)) return cached;

            bool rotary = true;
            GameObject prefab = definition.unitPrefab;

            if (prefab != null)
            {
                Autopilot autopilot = prefab.GetComponentInChildren<Autopilot>(includeInactive: true);
                if (autopilot != null) rotary = !(autopilot is AutopilotPlane);
            }

            rotaryCache[definition] = rotary;
            return rotary;
        }

        // Whether a definition is actually a flyable aircraft, resolved once per airframe.
        //
        // Blueprinter addons register ships and ground vehicles as AircraftDefinition too,
        // but their prefabs carry no Autopilot — so a ship or tank would otherwise slip past
        // MatchesLeader (IsRotary defaults to "rotary" when there is no autopilot) and be
        // offered as a wingman it could never be flown as.
        private static readonly Dictionary<AircraftDefinition, bool> autopilotCache =
            new Dictionary<AircraftDefinition, bool>();

        /// <summary>True when this definition's prefab is armed with an autopilot — i.e. it is an aircraft, not a ship or vehicle.</summary>
        public static bool IsFlyableAircraft(AircraftDefinition definition)
        {
            if (definition == null) return false;

            if (autopilotCache.TryGetValue(definition, out bool cached)) return cached;

            GameObject prefab = definition.unitPrefab;
            bool has = prefab != null &&
                       prefab.GetComponentInChildren<Autopilot>(includeInactive: true) != null;
            autopilotCache[definition] = has;
            return has;
        }

        /// <summary>
        /// Whether this airframe can join the player's formation at all.
        ///
        /// Rotary and fixed-wing cannot share a formation - they fly different autopilots
        /// and differ in speed by a factor of three - and WingRegistry refuses the mix. It
        /// used to refuse it *after* the purchase went through, so buying a helicopter as a
        /// jet spent the money, consumed the airframe, and left the aircraft orbiting on
        /// its own with no way to command it. The catalogue now hides what cannot be
        /// bought, and Buy refuses it as well in case anything reaches it another way.
        /// </summary>
        public static bool MatchesLeader(AircraftDefinition definition)
        {
            Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;
            if (leader == null || definition == null) return false;

            return IsRotary(definition) == WingRegistry.IsRotary(leader);
        }

        // ------------------------------------------------------------------- pricing

        /// <summary>
        /// What an airframe is worth: the same value the player's own aircraft menu prices
        /// from, with nothing added.
        ///
        /// The price used to compound with wing size — 1000, 1500, 2250, 3375 as the wing
        /// filled — which meant the number on a row was never the number you paid and the
        /// panel had to print the formula to explain itself. One aircraft, one price.
        /// </summary>
        public static float PriceOf(AircraftDefinition definition) =>
            definition != null ? definition.value : 0f;

        /// <summary>
        /// What this airframe costs to requisition right now, which is list price unless the
        /// purchase would take the faction past its AI aircraft cap. An owned airframe that
        /// returned safely to the wing reserve has already been paid for.
        /// </summary>
        public static float CurrentPriceOf(AircraftDefinition definition) =>
            Plugin.Config2.FreePlanePurchases.Value ||
            WingSupplyReserve.OwnedOf(definition) > 0
                ? 0f
                : PriceOf(definition) * (WouldExceedLimit ? ExceedLimitMultiplier : 1f);

        /// <summary>The multiplier charged for an airframe bought past the squadron cap.</summary>
        public static float ExceedLimitMultiplier =>
            Mathf.Max(1f, Plugin.Config2.ExceedLimitCostMultiplier.Value);

        /// <summary>Rank required before the cap may be exceeded at all.</summary>
        public static int ExceedLimitRank => Plugin.Config2.ExceedLimitRank.Value;

        /// <summary>How many over-cap airframes the player may have in the air at once.</summary>
        public static int ExceedLimitAllowance =>
            Mathf.Clamp(Plugin.Config2.ExceedLimitAllowance.Value, 1, 3);

        // Full-price requisitions remain the player's airframes while they survive. This is
        // deliberately separate from WingRecruitment's paid-assignment set: paying 25% to
        // redirect an already active mission aircraft is not buying that airframe.
        private static readonly HashSet<PersistentID> purchasedAircraft =
            new HashSet<PersistentID>();

        // Over-cap airframes the player has bought and still has flying.
        //
        // Counted this way, rather than as how far the faction's own aircraft count exceeds
        // its cap, because those are different things. A mission scripts in whatever AI it
        // likes regardless of the cap — the screenshot that prompted this had six friendly AI
        // against a computed limit of zero — so measuring the faction's excess would have
        // charged the player for the mission's decisions and locked the shop on exactly the
        // missions the over-limit purchase exists to rescue.
        private static readonly HashSet<PersistentID> overLimitAircraft = new HashSet<PersistentID>();

        // Bought but not yet in the world: a hangar delivery waits on the door sequence, and
        // without this the allowance could be spent several times over in that gap.
        // Every accepted order owns a future roster and squadron slot until its exact
        // aircraft registers or the order rolls back. Active recruitment consults the wing
        // count through WingRegistry.HasRoom, so it cannot steal capacity already paid for.
        private static readonly CapacityReservations capacityReservations =
            new CapacityReservations();

        internal static int PendingWingSlots => capacityReservations.Wing;

        /// <summary>Over-cap airframes still outstanding, counting deliveries in progress.</summary>
        public static int OverLimitOutstanding
        {
            get
            {
                overLimitAircraft.RemoveWhere(StillFlying);
                return overLimitAircraft.Count + capacityReservations.OverLimit;
            }
        }

        private static bool StillFlying(PersistentID id) =>
            !UnitRegistry.TryGetUnit(id, out Unit unit) || unit == null || unit.disabled;

        /// <summary>Record a delivered requisition and, when applicable, its over-cap slot.</summary>
        public static void NoteDelivery(Aircraft aircraft, bool overLimit, WingLoadoutChoice loadout)
        {
            if (aircraft == null) return;

            purchasedAircraft.Add(aircraft.persistentID);
            if (overLimit) overLimitAircraft.Add(aircraft.persistentID);

            // The airframe now exists, so the purchase order becomes a fact about this one
            // aircraft. Every later reader — the panels, the recovery path — asks the book
            // rather than the plan, which is what keeps one VT-7's fit off the next one.
            WingLoadoutBook.NoteSpawned(aircraft, loadout);
        }

        /// <summary>
        /// Transfer ownership information from a recovered world aircraft into the reserve.
        /// Also release any over-cap slot immediately rather than waiting for registry prune.
        /// </summary>
        public static bool TakePurchased(Aircraft aircraft)
        {
            return aircraft != null && TakePurchased(aircraft.persistentID);
        }

        internal static bool TakePurchased(PersistentID id)
        {
            overLimitAircraft.Remove(id);
            return purchasedAircraft.Remove(id);
        }

        /// <summary>Read ownership without transferring it out of the live aircraft.</summary>
        public static bool IsPurchased(Aircraft aircraft) =>
            aircraft != null && purchasedAircraft.Contains(aircraft.persistentID);

        /// <summary>Whether the local player has earned the right to exceed the cap.</summary>
        public static bool MeetsExceedLimitRank =>
            GameManager.GetLocalPlayer(out Player player) && player != null &&
            player.PlayerRank >= ExceedLimitRank;

        /// <summary>True when the next purchase would be an over-cap one, and is allowed to be.</summary>
        public static bool WouldExceedLimit =>
            ExceedLimit && Squadron().WouldExceed(capacityReservations.Squadron);

        /// <summary>The player's spendable allocation, or zero when there is no player.</summary>
        public static float Allocation =>
            GameManager.GetLocalPlayer(out Player player) && player != null ? player.Allocation : 0f;

        // ----------------------------------------------------------------- catalogue

        /// <summary>
        /// What can be bought right now.
        ///
        /// Two sources, because one is not enough. The faction's <c>AircraftSupply</c> is
        /// populated from the mission's declared stock, so anything the mission author did
        /// not list — every workshop and modded airframe, and any stock type the mission
        /// simply did not stock — has no entry and would never appear. Those are taken from
        /// <c>Encyclopedia.i.aircraft</c> instead, which is the registry the game itself
        /// spawns from and which modded aircraft register into.
        ///
        /// Undeclared types get their own small allowance rather than drawing on faction
        /// stock, tracked here: writing a supply entry for an airframe the faction was never
        /// given would be inventing stock on the mission's behalf.
        ///
        /// Both sources are then filtered the same way — mission restrictions, player rank,
        /// and whether the airframe could join this formation at all.
        /// </summary>
        public static IReadOnlyList<Offer> Catalogue()
        {
            catalogue.Clear();
            listedDefinitions.Clear();

            Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;
            FactionHQ hq = leader != null ? leader.NetworkHQ : null;
            if (hq == null) return catalogue;

            GameManager.GetLocalPlayer(out Player player);
            int rank = player != null ? player.PlayerRank : 0;

            foreach (KeyValuePair<AircraftDefinition, FactionHQ.RuntimeSupply> entry in hq.AircraftSupply)
            {
                int available = entry.Value.Count + WingSupplyReserve.CountOf(entry.Key);
                if (available <= 0) continue;
                if (!Sellable(entry.Key, hq, rank)) continue;

                catalogue.Add(new Offer(entry.Key, entry.Key.unitName,
                                        entry.Key.value, available));
                listedDefinitions.Add(entry.Key);
            }

            // A held airframe remains selectable even when it was the faction's final one.
            // Recovered undeclared aircraft also live only in the wing reserve, not in the
            // mission's supply dictionary.
            foreach (AircraftDefinition definition in WingSupplyReserve.Definitions)
            {
                if (definition == null || listedDefinitions.Contains(definition)) continue;
                int available = WingSupplyReserve.CountOf(definition);
                if (available <= 0 || !Sellable(definition, hq, rank)) continue;

                catalogue.Add(new Offer(definition, definition.unitName,
                                        definition.value, available));
                listedDefinitions.Add(definition);
            }

            if (Plugin.Config2.IncludeUndeclaredAircraft.Value)
                AddUndeclared(hq, rank, listedDefinitions);

            catalogue.Sort((a, b) => a.BasePrice.CompareTo(b.BasePrice));
            return catalogue;
        }

        /// <summary>Airframes the mission never stocked, offered from our own allowance.</summary>
        private static void AddUndeclared(FactionHQ hq, int rank,
                                          HashSet<AircraftDefinition> listed)
        {
            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia == null || encyclopedia.aircraft == null) return;

            List<AircraftDefinition> all = encyclopedia.aircraft;
            for (int i = 0; i < all.Count; i++)
            {
                AircraftDefinition definition = all[i];
                if (definition == null) continue;
                if (listed.Contains(definition)) continue;

                // Anything the faction actually stocks was handled above, at its real count.
                if (hq.AircraftSupply.ContainsKey(definition)) continue;
                if (!Sellable(definition, hq, rank)) continue;

                int left = UndeclaredRemaining(definition);
                if (left <= 0) continue;

                catalogue.Add(new Offer(definition, definition.unitName, definition.value, left));
                listed.Add(definition);
            }
        }

        /// <summary>Restrictions, rank and airframe class - the gates both sources share.</summary>
        private static bool Sellable(AircraftDefinition definition, FactionHQ hq, int rank)
        {
            if (definition == null) return false;

            // Ships and ground vehicles registered as AircraftDefinition by Blueprinter
            // addons are not flyable; never offer them, regardless of rotary/fixed-wing match.
            if (!IsFlyableAircraft(definition)) return false;

            // Hide what could never join the formation, rather than selling it and leaving
            // the aircraft orphaned in the air with no way to command it.
            if (!MatchesLeader(definition)) return false;

            if (hq.restrictedAircraft != null &&
                hq.restrictedAircraft.Contains(definition.unitName)) return false;

            if (definition.aircraftParameters != null &&
                definition.aircraftParameters.rankRequired > rank) return false;

            return true;
        }

        // How many of each undeclared airframe have been bought this mission. Kept here
        // rather than in the faction's supply dictionary, which describes what the mission
        // handed the faction and is not ours to invent entries in.
        private static readonly Dictionary<AircraftDefinition, int> undeclaredBought =
            new Dictionary<AircraftDefinition, int>();

        private static int UndeclaredRemaining(AircraftDefinition definition)
        {
            int allowance = Mathf.RoundToInt(Plugin.Config2.UndeclaredStock.Value);
            return allowance - (undeclaredBought.TryGetValue(definition, out int used) ? used : 0);
        }


        // ------------------------------------------------------------------ purchase

        /// <summary>Evaluate every purchase gate without changing funds, stock or capacity.</summary>
        public static PurchaseQuote Quote(AircraftDefinition definition)
        {
            WingLoadoutChoice loadout = WingLoadoutBook.PlannedFor(definition);
            Player player = null;
            FactionHQ hq = null;
            WingSupplyReserve.Source source = WingSupplyReserve.Source.None;
            bool declared = false;
            int stock = 0;
            float price = CurrentPriceOf(definition);
            bool overLimit = false;

            PurchaseQuote Denied(string why) =>
                new PurchaseQuote(false, why, price, stock, overLimit, loadout,
                                  player, hq, source, declared);

            if (!Plugin.Config2.ShopEnabled.Value) return Denied("The shop is disabled in config");
            if (definition == null) return Denied("No aircraft selected");

            WingRegistry wing = WingCommandManager.Instance?.Wing;
            Aircraft leader = wing?.Leader;
            if (leader == null) return Denied("Not flying");
            if (!leader.IsServer) return Denied("Host or single-player only");
            if (!WingRegistry.HasRoom(wing.Count)) return Denied("Wing is full");

            hq = leader.NetworkHQ;
            if (hq == null) return Denied("No faction");
            if (!GameManager.GetLocalPlayer(out player) || player == null) return Denied("No player");

            if (!IsFlyableAircraft(definition)) return Denied("Selected unit is not a flyable aircraft");
            if (!MatchesLeader(definition))
                return Denied(IsRotary(definition)
                    ? "Helicopters cannot formate on a jet"
                    : "Jets cannot formate on a helicopter");
            if (hq.restrictedAircraft != null && hq.restrictedAircraft.Contains(definition.unitName))
                return Denied(definition.unitName + " is restricted in this mission");
            if (definition.aircraftParameters != null &&
                definition.aircraftParameters.rankRequired > player.PlayerRank)
                return Denied("Requires rank " + definition.aircraftParameters.rankRequired);

            declared = hq.AircraftSupply.ContainsKey(definition);
            source = WingSupplyReserve.NextSource(definition);
            int factionStock = declared ? hq.GetUnitSupply(definition) : UndeclaredRemaining(definition);
            stock = factionStock + WingSupplyReserve.CountOf(definition);
            if (stock <= 0) return Denied(definition.unitName + ": none left in stock");

            if (source != WingSupplyReserve.Source.None &&
                WingSupplyReserve.PeekLoadout(definition, out WingLoadoutChoice recovered))
                loadout = recovered;

            if (!ClearedForPurchase(hq, out float multiplier, out string capReason,
                                    out overLimit))
                return Denied(capReason);

            bool alreadyOwned = source == WingSupplyReserve.Source.Owned;
            bool debugFree = Plugin.Config2.FreePlanePurchases.Value;
            price = alreadyOwned || debugFree ? 0f : PriceOf(definition) * multiplier;
            if (player.Allocation < price)
                return Denied("Need " + Mathf.RoundToInt(price) + ", have " +
                              Mathf.RoundToInt(player.Allocation));

            return new PurchaseQuote(true, null, price, stock, overLimit, loadout,
                                     player, hq, source, declared);
        }

        /// <summary>
        /// Reserve one purchase, ask the delivery system to produce it, and leave the
        /// transaction open until the exact aircraft registers. A synchronous failure rolls
        /// back here; a delayed timeout rolls back in <see cref="WingShopDelivery.Tick"/>.
        /// </summary>
        public static bool Buy(AircraftDefinition definition, out string reason, out float paid)
        {
            reason = null;
            paid = 0f;

            PurchaseQuote quote = Quote(definition);
            if (!quote.CanBuy)
            {
                reason = quote.Reason;
                return false;
            }

            if (!BeginTransaction(definition, quote, out PurchaseTransaction transaction,
                                  out reason))
                return false;

            if (!WingShopDelivery.Deliver(transaction, WingCommandManager.Instance.Wing.Leader,
                                          quote.Hq, out reason))
            {
                transaction.Rollback(reason ?? "delivery could not be started");
                return false;
            }

            paid = quote.Price;
            bool alreadyOwned = quote.Source == WingSupplyReserve.Source.Owned;
            bool debugFree = Plugin.Config2.FreePlanePurchases.Value;

            Plugin.Logger.LogInfo(
                $"[Shop] requisitioned {definition.unitName} for {quote.Price:F0}" +
                $" [{WingLoadoutCatalog.Label(definition, quote.Loadout)}]" +
                (alreadyOwned ? " (owned reserve)" :
                 quote.Source == WingSupplyReserve.Source.Held ? " (held reserve)" : "") +
                (debugFree && !alreadyOwned ? " (debug free purchase)" : "") +
                (quote.OverLimit ? $" ({ExceedLimitMultiplier:0.##}x over squadron limit)" : "") +
                $", {Available(definition, quote.Hq)} available");
            return true;
        }

        private static bool BeginTransaction(AircraftDefinition definition, PurchaseQuote quote,
                                             out PurchaseTransaction transaction,
                                             out string reason)
        {
            transaction = new PurchaseTransaction(definition, quote);
            activeTransactions.Add(transaction);
            reason = null;

            try
            {
                if (quote.Source != WingSupplyReserve.Source.None)
                {
                    if (!WingSupplyReserve.ReserveForPurchase(definition, quote.Source,
                                                              out WingSupplyReserve.Slot slot))
                    {
                        reason = definition.unitName + ": reserve changed before purchase";
                        transaction.Rollback(reason);
                        return false;
                    }
                    transaction.NoteReserveSlot(slot);
                }
                else if (quote.Declared)
                {
                    int before = quote.Hq.GetUnitSupply(definition);
                    if (before <= 0)
                    {
                        reason = definition.unitName + ": none left in stock";
                        transaction.Rollback(reason);
                        return false;
                    }

                    try
                    {
                        quote.Hq.AddSupplyUnit(definition, -1);
                    }
                    finally
                    {
                        if (quote.Hq.GetUnitSupply(definition) < before)
                            transaction.NoteFactionStockDebit();
                    }
                }
                else
                {
                    if (UndeclaredRemaining(definition) <= 0)
                    {
                        reason = definition.unitName + ": none left in stock";
                        transaction.Rollback(reason);
                        return false;
                    }
                    undeclaredBought[definition] =
                        (undeclaredBought.TryGetValue(definition, out int used) ? used : 0) + 1;
                    transaction.NoteUndeclaredStockDebit();
                }

                if (quote.Price > 0f)
                {
                    float before = quote.Player.Allocation;
                    try
                    {
                        quote.Player.AddAllocation(-quote.Price);
                    }
                    finally
                    {
                        if (quote.Player.Allocation < before) transaction.NoteFundsDebit();
                    }
                }

                transaction.ReserveCapacity();
                return true;
            }
            catch (Exception e)
            {
                reason = "Purchase reservation failed - " + e.Message;
                transaction.Rollback(reason);
                return false;
            }
        }

        private static void DecrementUndeclared(AircraftDefinition definition)
        {
            if (definition == null || !undeclaredBought.TryGetValue(definition, out int used)) return;
            if (used <= 1) undeclaredBought.Remove(definition);
            else undeclaredBought[definition] = used - 1;
        }

        private static int Available(AircraftDefinition definition, FactionHQ hq)
        {
            int ordinary = hq != null && hq.AircraftSupply.ContainsKey(definition)
                ? hq.GetUnitSupply(definition)
                : UndeclaredRemaining(definition);
            return ordinary + WingSupplyReserve.CountOf(definition);
        }

        /// <summary>
        /// How much of the mission's AI aircraft cap the faction is using.
        ///
        /// The limit mirrors the game's own formula in <c>FactionHQ.DeployAIAircraft</c> —
        /// the mission's base limit, raised for each enemy player and lowered for each
        /// friendly one. The faction's <c>activeAIAircraft</c> list is private, so the count
        /// is taken from the unit registry: friendly aircraft with no player in them.
        /// </summary>
        internal readonly struct SquadronState
        {
            public readonly int Active;
            public readonly int Limit;

            public SquadronState(int active, int limit)
            {
                Active = active;
                Limit = limit;
            }

            /// <summary>True when there is no room for one more aircraft.</summary>
            public bool AtCapacity => Active + 1 > Limit;

            /// <summary>Whether one more aircraft exceeds the cap after accepted orders.</summary>
            public bool WouldExceed(int pending) => Active + Mathf.Max(0, pending) + 1 > Limit;
        }

        private static SquadronState cachedSquadron;
        private static float squadronCachedAt = float.MinValue;

        /// <summary>
        /// The squadron state for display, memoised for a fraction of a second.
        ///
        /// Counting walks every aircraft in the world and every faction's player list, and
        /// the panel reads it once per shop row on every refresh. The authoritative overload
        /// below is never cached, so a purchase is always checked against a live count.
        /// </summary>
        public static SquadronState Squadron()
        {
            if (Time.unscaledTime - squadronCachedAt < 0.25f) return cachedSquadron;

            Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;
            FactionHQ hq = leader != null ? leader.NetworkHQ : null;

            cachedSquadron = Squadron(hq);
            squadronCachedAt = Time.unscaledTime;
            return cachedSquadron;
        }

        public static SquadronState Squadron(FactionHQ hq)
        {
            if (hq == null) return new SquadronState(0, 0);

            int friendlyPlayers = 0;
            int enemyPlayers = 0;

            foreach (FactionHQ other in FactionRegistry.GetAllHQs())
            {
                if (other == null) continue;
                int players = other.GetPlayers(sortByScore: false).Count;
                if (other == hq) friendlyPlayers += players;
                else enemyPlayers += players;
            }

            float limit = hq.AIAircraftLimit
                          + enemyPlayers * hq.addAIPerEnemyPlayer
                          - friendlyPlayers * hq.reduceAIPerFriendlyPlayer;

            int aiCount = 0;
            List<Aircraft> all = UnitRegistry.allAircraft;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft a = all[i];
                if (a == null || a.disabled) continue;
                if (a.NetworkHQ != hq) continue;
                if (a.Player != null) continue;

                // An aircraft the player has released is on its way home to be despawned.
                // Counting it holds capacity against a slot that is already being given
                // back, which is exactly the trap releasing a wingman to afford a better
                // one used to fall into.
                if (WingDeparture.Contains(a)) continue;

                aiCount++;
            }

            return new SquadronState(aiCount, Mathf.Max(0, Mathf.FloorToInt(limit)));
        }

        /// <summary>
        /// The cap, and the terms on which it may be broken.
        ///
        /// A mission that leaves no room at all — single-player missions routinely compute a
        /// limit of zero once the player's own presence is subtracted — used to make the
        /// shop simply unusable, with the reason visible only as a toast that had already
        /// gone by the time anyone read it. It can now be bought past, for a multiple of
        /// list price and only at rank, which keeps it a deliberate expense rather than a
        /// dead end.
        /// </summary>
        private static bool ClearedForPurchase(FactionHQ hq, out float multiplier,
                                               out string reason, out bool overLimit)
        {
            reason = null;
            multiplier = 1f;

            SquadronState squadron = Squadron(hq);
            overLimit = squadron.WouldExceed(capacityReservations.Squadron);
            if (!overLimit) return true;

            if (!ExceedLimit)
            {
                reason = "Squadron at capacity (" +
                         (squadron.Active + capacityReservations.Squadron) +
                         " of " + squadron.Limit +
                         ") - enable EXCEED LIMIT to requisition anyway";
                return false;
            }

            if (!MeetsExceedLimitRank)
            {
                reason = "Exceeding the squadron limit requires rank " + ExceedLimitRank;
                return false;
            }

            int outstanding = OverLimitOutstanding;
            if (outstanding >= ExceedLimitAllowance)
            {
                reason = "Already flying " + outstanding + " of " + ExceedLimitAllowance +
                         " airframes over the squadron limit";
                return false;
            }

            multiplier = ExceedLimitMultiplier;
            return true;
        }
    }
}
