using System;
using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace WingCommand
{
 /// <summary>Catalogue, pricing, and purchase transactions using native aircraft values, player
 /// allocation, and faction supply. Purchases compete with mission AI for stock.</summary>
    internal static class WingShop
    {
     /// <summary>One shop catalogue offer.</summary>
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

     /// <summary>Shared purchase eligibility for UI and execution, covering host, roster, stock, rank,
     /// squadron capacity, and funds.</summary>
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
            internal readonly Aircraft AutoRtbCandidate;

            internal PurchaseQuote(bool canBuy, string reason, float price, int stock,
                                   bool overLimit, WingLoadoutChoice loadout, Player player,
                                   FactionHQ hq, WingSupplyReserve.Source source, bool declared,
                                   Aircraft autoRtbCandidate = null)
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
                AutoRtbCandidate = autoRtbCandidate;
            }
        }

     /// <summary>Owns funds, stock, fit, and capacity reservations until the exact delivery registers.
     /// Commit on delivery or restore through idempotent rollback.</summary>
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
            internal WingPilot Pilot { get; private set; }
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

            internal void NotePilot(WingPilot pilot)
            {
                Pilot = pilot;
                rollback.Add(() => WingPilotRoster.ReleaseReservation(pilot, restoreSelection: true));
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
                NoteDelivery(aircraft, OverLimit, Loadout, Price);
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

     /// <summary>Explicit opt-in to ranked, surcharged purchases beyond the mission AI cap; disabled by
     /// default.</summary>
        public static bool ExceedLimit { get; set; }

     /// <summary>Launch fuel as a fraction of full tank capacity, selected from SpawnFuelSteps.
     /// Defaults to full, independent of the airframe's preset fuel.</summary>
        public static float SpawnFuelLevel { get; private set; } = WingTuning.DefaultSpawnFuel;

     /// <summary>Cycle launch fuel to the next step, wrapping at the end.</summary>
        public static void CycleSpawnFuel()
        {
            float[] steps = WingTuning.SpawnFuelSteps;
            if (steps == null || steps.Length == 0) return;
            int idx = 0;
            for (int i = 0; i < steps.Length; i++)
                if (Mathf.Approximately(steps[i], SpawnFuelLevel)) { idx = i; break; }
            SpawnFuelLevel = steps[(idx + 1) % steps.Length];
        }

     /// <summary>Launch fuel fraction relative to full tank capacity.</summary>
        public static float SpawnFuelFor(AircraftDefinition definition) =>
            Mathf.Clamp01(SpawnFuelLevel);

     /// <summary>Clear mission purchase records and the over-limit preference.</summary>
        public static void Reset()
        {
            foreach (PurchaseTransaction transaction in
                     new List<PurchaseTransaction>(activeTransactions))
                transaction.Rollback("mission reset");
            activeTransactions.Clear();
            listedDefinitions.Clear();
            purchasedAircraft.Clear();
            purchasePrice.Clear();
            overLimitAircraft.Clear();
            capacityReservations.Reset();
            ExceedLimit = false;
            SpawnFuelLevel = WingTuning.DefaultSpawnFuel;
            squadronCachedAt = float.MinValue;
            rotaryCache.Clear();
            autopilotCache.Clear();
            WingLoadoutBook.Reset();
            WingLoadoutCatalog.Reset();
        }

     /// <summary>Retry rollback actions rejected by external game APIs.</summary>
        public static void Tick()
        {
            // Allocate a transaction snapshot only when nonempty; rollback may mutate the live set.
            if (activeTransactions.Count == 0) return;

            foreach (PurchaseTransaction transaction in
                     new List<PurchaseTransaction>(activeTransactions))
                if (transaction.Status == PurchaseTransaction.State.RollingBack)
                    transaction.Rollback("retrying failed compensation");
        }

        // Cache rotary classification from the prefab autopilot; AircraftDefinition has no class flag
        // and catalogue refreshes are frequent.
        private static readonly Dictionary<AircraftDefinition, bool> rotaryCache =
            new Dictionary<AircraftDefinition, bool>();

     /// <summary>Whether the definition uses rotary flight control.</summary>
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

        // Cache autopilot presence to distinguish aircraft from addon ships and vehicles registered as
        // AircraftDefinition.
        private static readonly Dictionary<AircraftDefinition, bool> autopilotCache =
            new Dictionary<AircraftDefinition, bool>();

     /// <summary>Whether this definition lacks aircraft flight control and represents a surface
     /// unit.</summary>
        public static bool IsSurfaceDefinition(AircraftDefinition definition) =>
            definition != null && !IsFlyableAircraft(definition);

     /// <summary>Whether Supply/Loadout may expose the definition under host capabilities, excluding
     /// event placeholders even if they have autopilots.</summary>
        public static bool IsCommandableUnit(AircraftDefinition definition) =>
            definition != null
            && !AirframeCatalogPolicy.IsHiddenFromPanels(
                definition.unitName, definition.code, definition.jsonKey)
            && (IsFlyableAircraft(definition) || WingHost.Current.AllowSurfaceWingmen);

     /// <summary>Whether the prefab has an autopilot and can use aircraft control.</summary>
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

     /// <summary>Check formation compatibility before offering or buying an aircraft; reject
     /// unsupported rotary/fixed-wing mixtures before spending stock or funds.</summary>
        public static bool MatchesLeader(AircraftDefinition definition)
        {
            Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;
            if (leader == null || definition == null) return false;

            // Overwatch allows both aircraft classes to escort surface leaders in separate orbits.
            if (WingHost.Current.AllowMixedAirframes) return true;

            // Check surface capability separately because missing autopilots also classify as rotary.
            if (!IsFlyableAircraft(definition)) return WingHost.Current.AllowSurfaceWingmen;

            return IsRotary(definition) == WingRegistry.IsRotary(leader);
        }

        // Aircraft pricing.

     /// <summary>Native airframe list value without a wing-size multiplier.</summary>
        public static float PriceOf(AircraftDefinition definition) =>
            definition != null ? definition.value : 0f;

     /// <summary>Current purchase cost, including any over-cap surcharge. Recovered owned airframes are
     /// already paid for.</summary>
        public static float CurrentPriceOf(AircraftDefinition definition) =>
            Plugin.Settings.CheatFreePurchases ||
            WingSupplyReserve.OwnedOf(definition) > 0
                ? 0f
                : PriceOf(definition) * (WouldExceedLimit ? ExceedLimitMultiplier : 1f);

     /// <summary>Purchase-price multiplier beyond the squadron cap.</summary>
        public static float ExceedLimitMultiplier =>
            Mathf.Max(1f, WingTuning.ExceedLimitCostMultiplier);

     /// <summary>Minimum player rank for over-cap purchases.</summary>
        public static int ExceedLimitRank => WingTuning.ExceedLimitRank;

     /// <summary>Maximum simultaneous player-purchased over-cap aircraft.</summary>
        public static int ExceedLimitAllowance =>
            Mathf.Clamp(WingTuning.ExceedLimitAllowance, 1, 3);

        // Track full-price aircraft ownership separately from discounted command-right assignment of
        // mission aircraft.
        private static readonly HashSet<PersistentID> purchasedAircraft =
            new HashSet<PersistentID>();

        private static readonly Dictionary<PersistentID, float> purchasePrice =
            new Dictionary<PersistentID, float>();

        // Count the player's surviving over-cap purchases, not mission-scripted excess AI, which the
        // player did not buy.
        private static readonly HashSet<PersistentID> overLimitAircraft = new HashSet<PersistentID>();

        // Reserve roster and squadron capacity while deliveries are pending so purchases and active
        // recruitment cannot spend the same slots.
        private static readonly CapacityReservations capacityReservations =
            new CapacityReservations();

        internal static int PendingWingSlots => capacityReservations.Wing;

     /// <summary>Live and pending player purchases using over-cap allowance.</summary>
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

     /// <summary>Record delivered ownership, fit, and any over-cap allocation.</summary>
        public static void NoteDelivery(Aircraft aircraft, bool overLimit, WingLoadoutChoice loadout,
                                        float paid)
        {
            if (aircraft == null) return;

            purchasedAircraft.Add(aircraft.persistentID);
            purchasePrice[aircraft.persistentID] = Mathf.Max(0f, paid);
            if (overLimit) overLimitAircraft.Add(aircraft.persistentID);

            // Bind the delivered fit to this aircraft ID; later reads must not use the next purchase's
            // plan.
            WingLoadoutBook.NoteSpawned(aircraft, loadout);
        }

     /// <summary>Transfer recovered aircraft ownership to reserve and free its over-cap slot
     /// immediately.</summary>
        internal static bool TakePurchased(PersistentID id)
        {
            overLimitAircraft.Remove(id);
            purchasePrice.Remove(id);
            return purchasedAircraft.Remove(id);
        }

     /// <summary>Actual charged allocation for the aircraft; zero for free purchases.</summary>
        public static float PaidFor(PersistentID id) =>
            purchasePrice.TryGetValue(id, out float paid) ? paid : 0f;

        public static float PaidFor(Aircraft aircraft) =>
            aircraft != null ? PaidFor(aircraft.persistentID) : 0f;

     /// <summary>Query live ownership without transferring it.</summary>
        public static bool IsPurchased(Aircraft aircraft) =>
            aircraft != null && purchasedAircraft.Contains(aircraft.persistentID);

     /// <summary>Whether the local player meets the over-cap rank requirement.</summary>
        public static bool MeetsExceedLimitRank =>
            (Plugin.Settings != null && Plugin.Settings.CheatBypassRank) ||
            (GameManager.GetLocalPlayer(out Player player) && player != null &&
             player.PlayerRank >= ExceedLimitRank);

     /// <summary>Whether the next allowed purchase uses over-cap capacity.</summary>
        public static bool WouldExceedLimit =>
            ExceedLimit && Squadron().WouldExceed(capacityReservations.Squadron);

     /// <summary>Spendable player allocation, or zero without a player.</summary>
        public static float Allocation =>
            GameManager.GetLocalPlayer(out Player player) && player != null ? player.Allocation : 0f;

        // Shop catalogue.

     /// <summary>Offer only mission faction stock and concrete wing-reserve airframes, filtered by
     /// restrictions, rank, and formation compatibility. Reserve entries remain available when faction
     /// stock is empty; encyclopedia entries alone do not create purchasable stock.</summary>
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

            // Include reserve-only types even when faction stock is exhausted or the mission never
            // declared that type.
            foreach (AircraftDefinition definition in WingSupplyReserve.Definitions)
            {
                if (definition == null || listedDefinitions.Contains(definition)) continue;
                int available = WingSupplyReserve.CountOf(definition);
                if (available <= 0 || !Sellable(definition, hq, rank)) continue;

                catalogue.Add(new Offer(definition, definition.unitName,
                                        definition.value, available));
                listedDefinitions.Add(definition);
            }

            catalogue.Sort((a, b) => a.BasePrice.CompareTo(b.BasePrice));
            return catalogue;
        }

        private static readonly List<Offer> loadoutCatalogue = new List<Offer>();

     /// <summary>Loadout-editing catalogue across aircraft classes, independent of the current leader's
     /// class.</summary>
        public static IReadOnlyList<Offer> LoadoutCatalogue()
        {
            loadoutCatalogue.Clear();
            listedDefinitions.Clear();

            Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;
            FactionHQ hq = leader != null ? leader.NetworkHQ : null;
            GameManager.GetLocalPlayer(out Player player);
            int rank = player != null ? player.PlayerRank : 0;

            if (hq != null && hq.AircraftSupply != null)
            {
                foreach (KeyValuePair<AircraftDefinition, FactionHQ.RuntimeSupply> entry in hq.AircraftSupply)
                {
                    if (entry.Key == null || !Sellable(entry.Key, hq, rank, matchLeader: false)) continue;
                    int available = entry.Value.Count + WingSupplyReserve.CountOf(entry.Key);
                    loadoutCatalogue.Add(new Offer(entry.Key, entry.Key.unitName, entry.Key.value, available));
                    listedDefinitions.Add(entry.Key);
                }
            }

            foreach (AircraftDefinition definition in WingSupplyReserve.Definitions)
            {
                if (definition == null || listedDefinitions.Contains(definition)) continue;
                if (!Sellable(definition, hq, rank, matchLeader: false)) continue;
                int available = WingSupplyReserve.CountOf(definition);
                loadoutCatalogue.Add(new Offer(definition, definition.unitName, definition.value, available));
                listedDefinitions.Add(definition);
            }

            // Include known stock and modded encyclopedia airframes for template editing.
            Encyclopedia enc = Encyclopedia.i;
            if (enc != null && enc.aircraft != null)
            {
                for (int i = 0; i < enc.aircraft.Count; i++)
                {
                    AircraftDefinition def = enc.aircraft[i];
                    if (def == null || listedDefinitions.Contains(def)) continue;
                    if (!IsCommandableUnit(def)) continue;
                    loadoutCatalogue.Add(new Offer(def, def.unitName, def.value, 0));
                    listedDefinitions.Add(def);
                }
            }

            loadoutCatalogue.Sort((a, b) => a.BasePrice.CompareTo(b.BasePrice));
            return loadoutCatalogue;
        }

     /// <summary>Shared restriction, rank, and optional leader-class filters.</summary>
        private static bool Sellable(AircraftDefinition definition, FactionHQ hq, int rank, bool matchLeader = true)
        {
            if (definition == null) return false;

            if (!IsCommandableUnit(definition)) return false;

            // Exclude incompatible formations before a purchase can create an uncommandable aircraft.
            if (matchLeader && !MatchesLeader(definition)) return false;

            if (hq != null && hq.restrictedAircraft != null &&
                hq.restrictedAircraft.Contains(definition.unitName)) return false;

            if (!Plugin.Settings.CheatBypassRank &&
                definition.aircraftParameters != null &&
                definition.aircraftParameters.rankRequired > rank) return false;

            return true;
        }


        // Purchase execution.

     /// <summary>Evaluate purchase eligibility without mutating funds, stock, or capacity.</summary>
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

            if (!Plugin.Settings.ShopEnabled.Value) return Denied("The shop is disabled in config");
            if (definition == null) return Denied("No aircraft selected");

            WingRegistry wing = WingCommandManager.Instance?.Wing;
            Aircraft leader = wing?.Leader;
            if (leader == null) return Denied("Not flying");
            if (!leader.IsServer) return Denied("Host or single-player only");
            if (!WingRegistry.HasRoom(wing.Count)) return Denied("Wing is full");

            hq = leader.NetworkHQ;
            if (hq == null) return Denied("No faction");
            if (!GameManager.GetLocalPlayer(out player) || player == null) return Denied("No player");

            if (!IsCommandableUnit(definition))
                return Denied("Selected unit is not a flyable aircraft");
            if (!MatchesLeader(definition))
                return Denied(IsRotary(definition)
                    ? "Helicopters cannot formate on a jet"
                    : "Jets cannot formate on a helicopter");
            if (hq.restrictedAircraft != null && hq.restrictedAircraft.Contains(definition.unitName))
                return Denied(definition.unitName + " is restricted in this mission");
            if (!Plugin.Settings.CheatBypassRank &&
                definition.aircraftParameters != null &&
                definition.aircraftParameters.rankRequired > player.PlayerRank)
                return Denied("Requires rank " + definition.aircraftParameters.rankRequired);

            if (!IsSurfaceDefinition(definition))
            {
                if (!WingLaunchFields.HasAnyAllowed(hq))
                    return Denied("No launch base selected");
                if (!WingLaunchFields.CanAnyAllowedLaunch(hq, definition))
                    return Denied("No selected base can launch " + definition.unitName);
                string launchBlock = WingShopDelivery.LaunchBlockReason(hq, definition,
                    leader.transform.position);
                if (launchBlock != null) return Denied(launchBlock);
            }

            declared = hq.AircraftSupply.ContainsKey(definition);
            source = WingSupplyReserve.NextSource(definition);

            // Undeclared types can come only from an existing reserve slot, never invented faction
            // stock.
            int factionStock = declared ? hq.GetUnitSupply(definition) : 0;
            stock = factionStock + WingSupplyReserve.CountOf(definition);
            if (stock <= 0) return Denied(definition.unitName + ": none left in stock");

            if (source != WingSupplyReserve.Source.None &&
                WingSupplyReserve.PeekLoadout(definition, out WingLoadoutChoice recovered))
                loadout = recovered;

            if (!ClearedForPurchase(hq, out float multiplier, out string capReason,
                                    out overLimit, out Aircraft autoRtbCandidate))
                return Denied(capReason);

            bool alreadyOwned = source == WingSupplyReserve.Source.Owned;
            bool debugFree = Plugin.Settings.CheatFreePurchases;
            price = alreadyOwned || debugFree ? 0f : PriceOf(definition) * multiplier;
            if (player.Allocation < price)
                return Denied("Need " + Mathf.RoundToInt(price) + ", have " +
                              Mathf.RoundToInt(player.Allocation));

            return new PurchaseQuote(true, null, price, stock, overLimit, loadout,
                                     player, hq, source, declared, autoRtbCandidate);
        }

     /// <summary>Reserve and request delivery, committing only when the exact aircraft registers. Roll
     /// back synchronous failures here and delayed failures in WingShopDelivery.Tick.</summary>
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

            if (quote.AutoRtbCandidate != null)
            {
                Aircraft candidate = quote.AutoRtbCandidate;
                WingMember member = WingCommandManager.Instance?.Wing?.Find(candidate);
                if (member != null)
                {
                    member.SendHome("auto-RTB to free squadron slot for requisition");
                }
                else
                {
                    Pilot pilot = WingRegistry.PrimaryPilot(candidate);
                    if (pilot != null)
                    {
                        PilotBaseState landing =
                            (PilotBaseState)pilot.AILandingState ?? pilot.AIHeloLandingState;
                        if (landing != null) pilot.SwitchState(landing);
                    }
                    WingDeparture.Begin(candidate);
                }
                WingCommandManager.Instance?.Toast(
                    "Ordered " + candidate.unitName + " to RTB to free squadron slot");
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
            bool debugFree = Plugin.Settings.CheatFreePurchases;

            Plugin.LogVerbose(
                $"[Shop] requisitioned {definition.unitName} for {quote.Price:F0}" +
                $" [{WingLoadoutCatalog.Label(quote.Loadout)}]" +
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
                WingPilot reservedPilot = WingPilotRoster.ReserveForRequisition();
                if (reservedPilot != null)
                {
                    transaction.NotePilot(reservedPilot);
                }

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
                    // Recheck that an actual stock source exists before debiting.
                    reason = definition.unitName + ": none left in stock";
                    transaction.Rollback(reason);
                    return false;
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

        private static int Available(AircraftDefinition definition, FactionHQ hq)
        {
            int ordinary = hq != null && hq.AircraftSupply.ContainsKey(definition)
                ? hq.GetUnitSupply(definition)
                : 0;
            return ordinary + WingSupplyReserve.CountOf(definition);
        }

     /// <summary>Faction AI capacity using the native mission/enemy/friendly-player formula. Count
     /// friendly non-player aircraft from the public registry because activeAIAircraft is
     /// private.</summary>
        internal readonly struct SquadronState
        {
            public readonly int Active;
            public readonly int Limit;

            public SquadronState(int active, int limit)
            {
                Active = active;
                Limit = limit;
            }

         /// <summary>Whether adding one aircraft exceeds capacity.</summary>
            public bool AtCapacity => Active + 1 > Limit;

         /// <summary>Whether accepted pending orders plus another aircraft exceed capacity.</summary>
            public bool WouldExceed(int pending) => Active + Mathf.Max(0, pending) + 1 > Limit;
        }

        private static SquadronState cachedSquadron;
        private static float squadronCachedAt = float.MinValue;

     /// <summary>Cache squadron counts briefly for repeated UI reads. Purchase validation uses the
     /// uncached overload for live authority.</summary>
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

                // Exclude dismissed RTB aircraft from capacity while they await recovery and despawn.
                if (WingDeparture.Contains(a)) continue;

                aiCount++;
            }

            return new SquadronState(aiCount, Mathf.Max(0, Mathf.FloorToInt(limit)));
        }

     /// <summary>Validate capacity and explicit ranked over-cap purchase terms, returning the
     /// applicable price multiplier.</summary>
        private static bool ClearedForPurchase(FactionHQ hq, out float multiplier,
                                               out string reason, out bool overLimit,
                                               out Aircraft autoRtbCandidate)
        {
            reason = null;
            multiplier = 1f;
            autoRtbCandidate = null;

            // The debug wing-limit bypass also waives the mission AI capacity limit.
            if (Plugin.Settings != null && Plugin.Settings.CheatNoWingLimit)
            {
                overLimit = false;
                return true;
            }

            SquadronState squadron = Squadron(hq);
            overLimit = squadron.WouldExceed(capacityReservations.Squadron);
            if (!overLimit) return true;

            if (!ExceedLimit)
            {
                // Without over-limit purchasing, select a nearby friendly AI to return home and free
                // capacity.
                autoRtbCandidate = FindClosestAiToAirbase(hq);
                if (autoRtbCandidate != null)
                {
                    overLimit = false;
                    return true;
                }

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

     /// <summary>Find the active friendly AI nearest a friendly base for an at-capacity return
     /// order.</summary>
        public static Aircraft FindClosestAiToAirbase(FactionHQ hq)
        {
            if (hq == null) return null;
            var airbases = new List<Airbase>();
            IEnumerable<Airbase> hqBases = hq.GetAirbases();
            if (hqBases != null)
            {
                foreach (Airbase ab in hqBases)
                {
                    if (ab != null && !ab.disabled) airbases.Add(ab);
                }
            }
            if (airbases.Count == 0) return null;

            Aircraft best = null;
            float bestDistSq = float.MaxValue;
            List<Aircraft> all = UnitRegistry.allAircraft;

            for (int i = 0; i < all.Count; i++)
            {
                Aircraft a = all[i];
                if (a == null || a.disabled) continue;
                if (a.NetworkHQ != hq) continue;
                if (a.Player != null) continue;
                if (WingDeparture.Contains(a)) continue;

                Pilot pilot = WingRegistry.PrimaryPilot(a);
                if (pilot == null || pilot.dead || pilot.ejected) continue;
                if (pilot.currentState != null &&
                    (pilot.currentState == pilot.AILandingState || pilot.currentState == pilot.AIHeloLandingState))
                    continue;

                Vector3 pos = a.transform.position;
                float dForA = float.MaxValue;
                for (int j = 0; j < airbases.Count; j++)
                {
                    Airbase ab = airbases[j];
                    if (ab == null || ab.disabled) continue;
                    float d = (pos - ab.transform.position).sqrMagnitude;
                    if (d < dForA) dForA = d;
                }

                if (dForA < bestDistSq)
                {
                    bestDistSq = dForA;
                    best = a;
                }
            }

            return best;
        }
    }
}
