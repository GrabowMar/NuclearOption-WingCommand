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
        /// <summary>Where a purchased aircraft appears.</summary>
        internal enum Delivery
        {
            /// <summary>In the circuit over the nearest friendly airbase. It flies to you.</summary>
            Base,

            /// <summary>Behind you, at your speed and altitude. Costs the surcharge.</summary>
            Fast,
        }

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

        /// <summary>Delivery mode used by the next purchase. Toggled from the WMC page.</summary>
        public static Delivery Mode { get; private set; } = Delivery.Base;

        public static void ToggleDelivery()
        {
            Mode = Mode == Delivery.Base ? Delivery.Fast : Delivery.Base;
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
        /// What the next aircraft of this type costs.
        ///
        /// The price compounds with wing size, so each wingman makes the next one dearer:
        /// a 1000-credit airframe runs 1000, 1500, 2250, 3375 as the wing fills. That is
        /// the balance lever — a large wing is meant to be a serious investment rather than
        /// something the allocation absorbs without noticing.
        /// </summary>
        public static float PriceOf(AircraftDefinition definition, Delivery mode)
        {
            if (definition == null) return 0f;

            int wingSize = WingCommandManager.Instance?.Wing?.Count ?? 0;

            float price = definition.value *
                          Mathf.Pow(Plugin.Config2.WingPriceGrowth.Value, wingSize);

            if (mode == Delivery.Fast)
                price *= 1f + Plugin.Config2.FastDeliverySurcharge.Value;

            return price;
        }

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

            foreach (KeyValuePair<AircraftDefinition, FactionHQ.RuntimeSupply> entry in hq.AircraftSupply)
            {
                if (entry.Value.Count <= 0) continue;
                if (!Sellable(entry.Key, hq, rank)) continue;

                catalogue.Add(new Offer(entry.Key, entry.Key.unitName,
                                        entry.Key.value, entry.Value.Count));
            }

            if (Plugin.Config2.IncludeUndeclaredAircraft.Value)
                AddUndeclared(hq, rank);

            catalogue.Sort((a, b) => a.BasePrice.CompareTo(b.BasePrice));
            return catalogue;
        }

        /// <summary>Airframes the mission never stocked, offered from our own allowance.</summary>
        private static void AddUndeclared(FactionHQ hq, int rank)
        {
            Encyclopedia encyclopedia = Encyclopedia.i;
            if (encyclopedia == null || encyclopedia.aircraft == null) return;

            List<AircraftDefinition> all = encyclopedia.aircraft;
            for (int i = 0; i < all.Count; i++)
            {
                AircraftDefinition definition = all[i];
                if (definition == null) continue;

                // Anything the faction actually stocks was handled above, at its real count.
                if (hq.AircraftSupply.ContainsKey(definition)) continue;
                if (!Sellable(definition, hq, rank)) continue;

                int left = UndeclaredRemaining(definition);
                if (left <= 0) continue;

                catalogue.Add(new Offer(definition, definition.unitName, definition.value, left));
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

        /// <summary>Forget what was bought, when a mission ends.</summary>
        public static void Reset() => undeclaredBought.Clear();

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
        public static bool Buy(AircraftDefinition definition, Delivery mode, out string reason)
        {
            reason = null;

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
            int stock = declared ? hq.GetUnitSupply(definition) : UndeclaredRemaining(definition);

            if (stock <= 0)
            {
                reason = definition.unitName + ": none left in stock";
                return false;
            }

            if (!WithinAircraftLimit(hq, out string capReason))
            {
                reason = capReason;
                return false;
            }

            if (!GameManager.GetLocalPlayer(out Player player) || player == null)
            {
                reason = "No player";
                return false;
            }

            float price = PriceOf(definition, mode);
            if (player.Allocation < price)
            {
                reason = "Need " + Mathf.RoundToInt(price) + ", have " +
                         Mathf.RoundToInt(player.Allocation);
                return false;
            }

            Aircraft bought = WingShopDelivery.Spawn(definition, leader, hq, mode);
            if (bought == null)
            {
                reason = "Delivery failed - see the BepInEx log";
                return false;
            }

            // Only now, with an aircraft actually in the world, does anything get spent.
            player.AddAllocation(-price);

            // Declared airframes come out of the faction's books; undeclared ones out of
            // ours, so the mission's own accounting is never handed entries it did not have.
            if (declared)
                hq.AddSupplyUnit(definition, -1);
            else
                undeclaredBought[definition] = (undeclaredBought.TryGetValue(definition, out int n) ? n : 0) + 1;

            WingCommandManager.Instance?.QueueRecruit(bought);

            Plugin.Logger.LogInfo(
                $"[Shop] bought {definition.unitName} for {price:F0} " +
                $"({mode} delivery), {(declared ? hq.GetUnitSupply(definition) : UndeclaredRemaining(definition))} left in stock");

            return true;
        }

        /// <summary>
        /// The mission's own aircraft cap, applied strictly.
        ///
        /// The effective limit mirrors the game's formula in <c>FactionHQ.DeployAIAircraft</c>
        /// — the mission's base limit, raised for each enemy player and lowered for each
        /// friendly one. The faction's own <c>activeAIAircraft</c> list is private, so the
        /// count is taken from the unit registry: friendly aircraft with no player in them.
        /// </summary>
        private static bool WithinAircraftLimit(FactionHQ hq, out string reason)
        {
            reason = null;

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

            if (aiCount + 1 <= limit) return true;

            reason = "Squadron at capacity (" + aiCount + " of " +
                     Mathf.FloorToInt(limit) + " AI aircraft)";
            return false;
        }
    }
}
