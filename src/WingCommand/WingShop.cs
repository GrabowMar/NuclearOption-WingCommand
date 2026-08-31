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

        private static readonly List<Offer> catalogue = new List<Offer>();

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
            undeclaredBought.Clear();
            purchasedAircraft.Clear();
            overLimitAircraft.Clear();
            overLimitPending = 0;
            ExceedLimit = false;
            squadronCachedAt = float.MinValue;
            WingLoadoutBook.Reset();
            WingLoadoutCatalog.Reset();
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
        private static int overLimitPending;

        /// <summary>Over-cap airframes still outstanding, counting deliveries in progress.</summary>
        public static int OverLimitOutstanding
        {
            get
            {
                overLimitAircraft.RemoveWhere(StillFlying);
                return overLimitAircraft.Count + overLimitPending;
            }
        }

        private static bool StillFlying(PersistentID id) =>
            !UnitRegistry.TryGetUnit(id, out Unit unit) || unit == null || unit.disabled;

        /// <summary>Record a delivered requisition and, when applicable, its over-cap slot.</summary>
        public static void NoteDelivery(Aircraft aircraft, bool overLimit, WingLoadoutChoice loadout)
        {
            if (overLimit) overLimitPending = Mathf.Max(0, overLimitPending - 1);
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
            if (aircraft == null) return false;
            PersistentID id = aircraft.persistentID;
            overLimitAircraft.Remove(id);
            return purchasedAircraft.Remove(id);
        }

        /// <summary>Release the reservation for a delivery that never arrived.</summary>
        public static void CancelOverLimitDelivery() =>
            overLimitPending = Mathf.Max(0, overLimitPending - 1);

        /// <summary>Whether the local player has earned the right to exceed the cap.</summary>
        public static bool MeetsExceedLimitRank =>
            GameManager.GetLocalPlayer(out Player player) && player != null &&
            player.PlayerRank >= ExceedLimitRank;

        /// <summary>True when the next purchase would be an over-cap one, and is allowed to be.</summary>
        public static bool WouldExceedLimit => ExceedLimit && Squadron().AtCapacity;

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

            Aircraft leader = WingCommandManager.Instance?.Wing?.Leader;
            FactionHQ hq = leader != null ? leader.NetworkHQ : null;
            if (hq == null) return catalogue;

            GameManager.GetLocalPlayer(out Player player);
            int rank = player != null ? player.PlayerRank : 0;
            var listed = new HashSet<AircraftDefinition>();

            foreach (KeyValuePair<AircraftDefinition, FactionHQ.RuntimeSupply> entry in hq.AircraftSupply)
            {
                int available = entry.Value.Count + WingSupplyReserve.CountOf(entry.Key);
                if (available <= 0) continue;
                if (!Sellable(entry.Key, hq, rank)) continue;

                catalogue.Add(new Offer(entry.Key, entry.Key.unitName,
                                        entry.Key.value, available));
                listed.Add(entry.Key);
            }

            // A held airframe remains selectable even when it was the faction's final one.
            // Recovered undeclared aircraft also live only in the wing reserve, not in the
            // mission's supply dictionary.
            foreach (AircraftDefinition definition in WingSupplyReserve.Definitions)
            {
                if (definition == null || listed.Contains(definition)) continue;
                int available = WingSupplyReserve.CountOf(definition);
                if (available <= 0 || !Sellable(definition, hq, rank)) continue;

                catalogue.Add(new Offer(definition, definition.unitName,
                                        definition.value, available));
                listed.Add(definition);
            }

            if (Plugin.Config2.IncludeUndeclaredAircraft.Value)
                AddUndeclared(hq, rank, listed);

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

        /// <summary>
        /// Try to buy one aircraft. Returns false with a reason the caller can show.
        ///
        /// Order matters more here than anywhere else in the mod: every gate runs, and the
        /// spawn happens, before a single credit or airframe moves. A purchase that took
        /// the money and then failed to produce an aircraft would be the worst bug this
        /// feature could have, so it is made structurally impossible rather than merely
        /// avoided.
        /// </summary>
        /// <param name="paid">
        /// What was actually charged. The capacity check here is deliberately live while the
        /// panel's is memoised, so the two can disagree for a fraction of a second as the
        /// squadron fills — reporting the real figure means the player always sees the price
        /// they were charged rather than the one the row happened to be showing.
        /// </param>
        public static bool Buy(AircraftDefinition definition, out string reason, out float paid)
        {
            reason = null;
            paid = 0f;

            if (!Plugin.Config2.ShopEnabled.Value)
            {
                reason = "The shop is disabled in config";
                return false;
            }

            if (definition == null)
            {
                reason = "No aircraft selected";
                return false;
            }

            WingRegistry wing = WingCommandManager.Instance?.Wing;
            Aircraft leader = wing?.Leader;
            if (leader == null)
            {
                reason = "Not flying";
                return false;
            }

            // Spawning is world state, which only the server may write.
            if (!leader.IsServer)
            {
                reason = "Host or single-player only";
                return false;
            }

            if (!MatchesLeader(definition))
            {
                bool rotary = IsRotary(definition);
                reason = rotary
                    ? "Helicopters cannot formate on a jet"
                    : "Jets cannot formate on a helicopter";
                return false;
            }

            if (wing.Count >= Plugin.Config2.MaxWingSize.Value)
            {
                reason = "Wing is full";
                return false;
            }

            FactionHQ hq = leader.NetworkHQ;
            if (hq == null)
            {
                reason = "No faction";
                return false;
            }

            bool declared = hq.AircraftSupply.ContainsKey(definition);
            WingSupplyReserve.Source reserveSource = WingSupplyReserve.NextSource(definition);
            int factionStock = declared ? hq.GetUnitSupply(definition) : UndeclaredRemaining(definition);
            int stock = factionStock + WingSupplyReserve.CountOf(definition);

            if (stock <= 0)
            {
                reason = definition.unitName + ": none left in stock";
                return false;
            }

            if (!ClearedForPurchase(hq, out float multiplier, out string capReason))
            {
                reason = capReason;
                return false;
            }

            if (!GameManager.GetLocalPlayer(out Player player) || player == null)
            {
                reason = "No player";
                return false;
            }

            // A requisition carries whatever the player planned for that airframe, unless it
            // is launching an airframe that has already flown for the wing and come home —
            // in which case it carries what it came home with.
            WingLoadoutChoice loadout = WingLoadoutBook.PlannedFor(definition);
            if (reserveSource != WingSupplyReserve.Source.None &&
                WingLoadoutBook.PeekReserved(definition, out WingLoadoutChoice recovered))
                loadout = recovered;

            bool alreadyOwned = reserveSource == WingSupplyReserve.Source.Owned;
            float price = alreadyOwned ? 0f : PriceOf(definition) * multiplier;
            if (player.Allocation < price)
            {
                reason = "Need " + Mathf.RoundToInt(price) + ", have " +
                         Mathf.RoundToInt(player.Allocation);
                return false;
            }

            // Capacity and price are separate facts. A host may configure a 1x multiplier;
            // that purchase is still over the cap and must still consume one of the three
            // outstanding slots.
            bool overLimit = Squadron(hq).AtCapacity;
            if (overLimit) overLimitPending++;

            if (!WingShopDelivery.Deliver(definition, leader, hq, overLimit, loadout, out reason))
            {
                if (overLimit) CancelOverLimitDelivery();
                return false;
            }

            // Only now, with the field committed to producing an aircraft, does anything get
            // spent. A hangar delivery may still be a few seconds from rolling out, but
            // TrySpawnAircraft returning Allowed is the game's own commitment to it — the
            // same signal FactionHQ treats as a successful deployment.
            player.AddAllocation(-price);
            paid = price;

            // Declared airframes come out of the faction's books; undeclared ones out of
            // ours, so the mission's own accounting is never handed entries it did not have.
            if (reserveSource != WingSupplyReserve.Source.None)
            {
                if (WingSupplyReserve.Consume(definition, reserveSource))
                {
                    // The parked loadout has now been collected by this launch.
                    WingLoadoutBook.PopReserved(definition);
                }
                else
                {
                    // World state changed between validation and commitment. The delivery is
                    // already authorised, so consume ordinary stock rather than duplicating
                    // an airframe or charging the reserve twice.
                    if (declared) hq.AddSupplyUnit(definition, -1);
                    else undeclaredBought[definition] =
                        (undeclaredBought.TryGetValue(definition, out int used) ? used : 0) + 1;
                }
            }
            else if (declared)
                hq.AddSupplyUnit(definition, -1);
            else
                undeclaredBought[definition] = (undeclaredBought.TryGetValue(definition, out int n) ? n : 0) + 1;

            Plugin.Logger.LogInfo(
                $"[Shop] requisitioned {definition.unitName} for {price:F0}" +
                $" [{WingLoadoutCatalog.Label(definition, loadout)}]" +
                (alreadyOwned ? " (owned reserve)" :
                 reserveSource == WingSupplyReserve.Source.Held ? " (held reserve)" : "") +
                (multiplier > 1f ? $" ({multiplier:0.##}x over squadron limit)" : "") +
                $", {Available(definition, hq)} available");

            return true;
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
        private static bool ClearedForPurchase(FactionHQ hq, out float multiplier, out string reason)
        {
            reason = null;
            multiplier = 1f;

            SquadronState squadron = Squadron(hq);
            if (!squadron.AtCapacity) return true;

            if (!ExceedLimit)
            {
                reason = "Squadron at capacity (" + squadron.Active + " of " + squadron.Limit +
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
