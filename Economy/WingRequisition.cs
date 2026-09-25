using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>The host's side of SUPPLY (spec WMC rebuild §SUPPLY; design wmc-rebuild/supply-shop-rules §5): the draft the page
    /// edits (airframe, per-airframe fit, fuel, base mode, bases turned OFF), snapshots of the game for <see cref="ShopRules"/>,
    /// the catalogue and field lists (refilled at most once a second), and what a requisition over the faction's AI limit does
    /// (user decision 2026-09-25: ×3 COST, MATCH ENEMY, RTB ONE).</summary>
    internal static class WingRequisition
    {
        /// <summary>The catalogue and the field list are refilled at most this often (FindObjectsOfType allocates).</summary>
        public static float ListSeconds = 1f;

        public static AircraftDefinition Selected;
        public static int FuelPercent = 100;
        public static LaunchMode Mode = LaunchMode.Nearest;

        private static readonly Dictionary<AircraftDefinition, string> fits = new Dictionary<AircraftDefinition, string>();
        private static readonly HashSet<string> off = new HashSet<string>();
        private static readonly Dictionary<AircraftDefinition, byte> kinds = new Dictionary<AircraftDefinition, byte>();
        private static readonly HarmonyLib.AccessTools.FieldRef<FactionHQ, List<Aircraft>> ActiveAi =
            HarmonyLib.AccessTools.FieldRefAccess<FactionHQ, List<Aircraft>>("activeAIAircraft");

        /// <summary>Listed airframes (<see cref="ShopRules.Listed"/>), in the game's order.</summary>
        public static readonly List<AircraftDefinition> Catalogue = new List<AircraftDefinition>();
        /// <summary>Friendly or unowned fields with a takeoff runway, nearest first.</summary>
        public static readonly List<Airbase> Fields = new List<Airbase>();
        private static float listedAt = float.NegativeInfinity;
        private static ShopBase[] bases = new ShopBase[8];

        public static void Reset()
        {
            Selected = null;
            FuelPercent = 100;
            Mode = LaunchMode.Nearest;
            fits.Clear();
            off.Clear();
            Catalogue.Clear();
            Fields.Clear();
            listedAt = float.NegativeInfinity;
        }

        // ---- the draft

        /// <summary>Null: AUTO (the game arms it); <see cref="CallSpec.YourLoadout"/>; else a LOADOUT template id.</summary>
        public static string FitOf(AircraftDefinition definition) =>
            definition != null && fits.TryGetValue(definition, out string fit) ? fit : null;

        public static void SetFit(AircraftDefinition definition, string fit)
        {
            if (definition == null) return;
            if (fit == null) fits.Remove(definition);
            else fits[definition] = fit;
        }

        public static string KeyOf(Airbase a) => a.SavedAirbase != null && !string.IsNullOrEmpty(a.SavedAirbase.UniqueName) ? a.SavedAirbase.UniqueName : a.name;

        public static string NameOf(Airbase a) => BaseName.Of(a.SavedAirbase != null ? a.SavedAirbase.DisplayName : null,
            a.SavedAirbase != null ? a.SavedAirbase.UniqueName : null, a.name);

        public static bool IsOn(Airbase a) => a != null && !off.Contains(KeyOf(a));

        public static void SetOn(Airbase a, bool on)
        {
            if (a == null) return;
            if (on) off.Remove(KeyOf(a));
            else off.Add(KeyOf(a));
        }

        // ---- lists

        /// <summary>Refills the catalogue and the field list at most once every <see cref="ListSeconds"/> (or now with
        /// <paramref name="force"/>). ponytail: FindObjectsOfType allocates once a refill, only while SUPPLY shows or an order runs.</summary>
        public static void RefreshLists(Aircraft caller, bool force = false)
        {
            if (!force && Time.time - listedAt < ListSeconds) return;
            listedAt = Time.time;
            Fields.Clear();
            if (caller != null) Fields.AddRange(WingService.FriendlyFields(caller));
            Catalogue.Clear();
            Encyclopedia enc = Encyclopedia.i;
            if (enc == null || enc.aircraft == null) return;
            FactionHQ hq = caller != null ? caller.NetworkHQ : null;
            bool sandbox = Plugin.Settings.SandboxFreeCalls.Value;
            foreach (AircraftDefinition d in enc.aircraft)
                if (d != null && ShopRules.Listed(For(d, hq), sandbox)) Catalogue.Add(d);
        }

        // ---- snapshots

        public static ShopWing Wing(Aircraft caller)
        {
            WingService wing = WingService.Instance;
            GameManager.GetLocalPlayer(out NuclearOption.Networking.Player player);
            FactionHQ hq = caller != null ? caller.NetworkHQ : null;
            var w = new ShopWing
            {
                Host = caller != null && caller.IsServer,
                Flying = caller != null && !caller.disabled,
                HasFaction = hq != null,
                Sandbox = Plugin.Settings.SandboxFreeCalls.Value,
                Members = wing != null ? wing.Members.Count : 0,
                Pending = SpawnService.Instance != null ? SpawnService.Instance.PendingTotal : 0,
                MaxMembers = WingService.MaxMembers,
                Funds = player != null ? player.Allocation : 0f,
                PlayerRank = player != null ? player.PlayerRank : 0,
                Mode = Plugin.Settings.OverLimit.Value,
            };
            if (hq != null)
            {
                w.FactionAi = FactionAi(hq);
                w.FactionAiLimit = Limit(hq);
                if (w.Mode == OverLimitMode.RtbOne && ShopRules.OverCap(w)) w.RtbCandidate = RtbCandidate(hq) != null;
            }
            foreach (Airbase a in Fields)
                if (a != null && IsOn(a)) w.BasesOn++;
            return w;
        }

        public static ShopAirframe For(AircraftDefinition d, FactionHQ hq)
        {
            byte kind = KindOf(d);
            var a = new ShopAirframe
            {
                Value = d.value,
                RankRequired = d.aircraftParameters != null ? d.aircraftParameters.rankRequired : 0,
                // The game keys mission restrictions by jsonKey (review of 0.9: it matched unitName and never hit).
                Restricted = hq != null && hq.restrictedAircraft != null && hq.restrictedAircraft.Contains(d.jsonKey),
                Vtol = kind == 2,
                Placeholder = kind == 3 || AirframeCatalogPolicy.IsHiddenFromPanels(d.unitName, d.code, d.jsonKey),
                Jet = kind == 1,
                Declared = hq != null && hq.AircraftSupply != null && hq.AircraftSupply.ContainsKey(d),
                FactionStock = hq != null ? hq.GetUnitSupply(d) : 0,
                Held = WingSupplyReserve.CountOf(d),
            };
            foreach (Airbase f in Fields)
                if (f != null && IsOn(f) && !(a.Jet && f.AttachedAirbase)) a.BasesForClass++;
            return a;
        }

        /// <summary>0 rotary or tiltwing, 1 jet, 2 VTOL (no AI state to adopt), 3 no usable prefab; cached per airframe.</summary>
        private static byte KindOf(AircraftDefinition d)
        {
            if (kinds.TryGetValue(d, out byte k)) return k;
            Aircraft t = d.unitPrefab != null ? d.unitPrefab.GetComponent<Aircraft>() : null;
            k = t == null || t.pilots == null || t.pilots.Length == 0 || t.pilots[0] == null ? (byte)3
                : t.pilots[0].pilotType == Pilot.PilotType.VTOL ? (byte)2
                : ProfileReader.ClassOf(t) == AirframeClass.FixedWing ? (byte)1 : (byte)0;
            kinds[d] = k;
            return k;
        }

        public static bool IsJet(AircraftDefinition d) => d != null && KindOf(d) == 1;

        // ---- fields

        /// <summary>The field a requisition of <paramref name="d"/> launches from under the base mode (-1 in the list: none).</summary>
        public static Airbase PickField(Aircraft caller, AircraftDefinition d)
        {
            if (caller == null || d == null) return null;
            if (bases.Length < Fields.Count) bases = new ShopBase[Fields.Count];
            Airbase picked = WingService.Instance != null ? WingService.Instance.LaunchField : null;
            Vector3 at = caller.transform.position;
            for (int i = 0; i < Fields.Count; i++)
            {
                Airbase f = Fields[i];
                bases[i] = f == null ? default : new ShopBase
                {
                    On = IsOn(f), Carrier = f.AttachedAirbase, HangarReady = HangarReady(f, d), Picked = ReferenceEquals(f, picked),
                    DistSq = (f.transform.position - at).sqrMagnitude,
                };
            }
            int k = ShopRules.PickBase(Mode, IsJet(d), bases, Fields.Count);
            return k >= 0 ? Fields[k] : null;
        }

        public static bool HangarReady(Airbase f, AircraftDefinition d)
        {
            if (f.hangars == null) return false;
            foreach (Hangar h in f.hangars)
                if (h != null && !h.Disabled && h.Available && h.CanSpawnAircraft(d)) return true;
            return false;
        }

        /// <summary>A field's row status: OFF, NO JETS (a carrier for a jet), READY (a hangar can spawn it), else APRON (a
        /// service-point spawn).</summary>
        public static string Status(Airbase f, AircraftDefinition d) =>
            !IsOn(f) ? "OFF" : IsJet(d) && f.AttachedAirbase ? "NO JETS" : d != null && HangarReady(f, d) ? "READY" : "APRON";

        public static AircraftDefinition FindAirframe(string key)
        {
            Encyclopedia enc = Encyclopedia.i;
            if (string.IsNullOrEmpty(key) || enc == null || enc.aircraft == null) return null;
            foreach (AircraftDefinition d in enc.aircraft)
                if (d != null && (d.jsonKey == key || d.unitName == key)) return d;
            return null;
        }

        public static Airbase FindField(string key)
        {
            foreach (Airbase f in Fields)
                if (f != null && KeyOf(f) == key) return f;
            return null;
        }

        // ---- over the faction's AI limit

        /// <summary>The faction's live AI aircraft, as the game counts them (our wingmen included), plus launches whose aircraft
        /// has not appeared yet.</summary>
        public static int FactionAi(FactionHQ hq)
        {
            List<Aircraft> live = ActiveAi(hq);
            return (live != null ? live.Count : 0) + (SpawnService.Instance != null ? SpawnService.Instance.Unspawned : 0);
        }

        /// <summary>The game's limit (FactionHQ.DeployAIAircraft).</summary>
        public static float Limit(FactionHQ hq)
        {
            int enemies = 0;
            foreach (FactionHQ other in FactionRegistry.GetAllHQs())
                if (other != null && other != hq && other.factionPlayers != null) enemies += other.factionPlayers.Count;
            int friends = hq.factionPlayers != null ? hq.factionPlayers.Count : 0;
            return hq.AIAircraftLimit + enemies * hq.addAIPerEnemyPlayer - friends * hq.reduceAIPerFriendlyPlayer;
        }

        /// <summary>RTB ONE: the faction's AI aircraft nearest one of its fields that may be sent home — never a wingman, the
        /// leader, a player, a launch of ours, a dead or ejected pilot, one on the ground or one already landing.</summary>
        public static Aircraft RtbCandidate(FactionHQ hq)
        {
            List<Airbase> own = new List<Airbase>();
            foreach (Airbase f in Fields)
                if (f != null && f.CurrentHQ == hq) own.Add(f);
            WingService wing = WingService.Instance;
            Aircraft best = null;
            float bestSq = float.MaxValue;
            foreach (Aircraft a in UnitRegistry.allAircraft)
            {
                if (a == null) continue;
                Pilot p = a.pilots != null && a.pilots.Length > 0 ? a.pilots[0] : null;
                bool landing = p != null && p.currentState != null &&
                               (ReferenceEquals(p.currentState, p.AILandingState) || ReferenceEquals(p.currentState, p.AIHeloLandingState));
                if (!AutoRtbCandidatePolicy.IsCandidateEligible(a.disabled, a.NetworkHQ == hq, a.Player != null,
                        wing != null && (wing.IsMember(a) || ReferenceEquals(wing.Leader, a)), false,
                        SpawnService.Instance != null && SpawnService.Instance.IsPending(a), false,
                        p == null || p.dead || p.ejected, landing) || a.radarAlt < 10f) continue;
                Vector3 at = a.transform.position;
                float d = float.MaxValue;
                foreach (Airbase f in own)
                    d = Mathf.Min(d, (f.transform.position - at).sqrMagnitude);
                if (own.Count == 0) d = 0f;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = a;
                }
            }
            return best;
        }

        /// <summary>What <paramref name="n"/> launches over the limit did, in words (the executor's answer): MATCH ENEMY lets every
        /// enemy faction field <paramref name="n"/> more AI aircraft; RTB ONE sends that many of the faction's own AI to land.</summary>
        public static string ApplyOverLimit(OverLimitMode mode, FactionHQ hq, int n)
        {
            switch (mode)
            {
                case OverLimitMode.MatchEnemy:
                    foreach (FactionHQ other in FactionRegistry.GetAllHQs())
                        if (other != null && other != hq) other.AIAircraftLimit += n;
                    Plugin.Logger.LogInfo($"[Supply] over the AI limit: every enemy faction may field {n} more");
                    return "enemy +" + n;
                case OverLimitMode.RtbOne:
                {
                    int sent = 0;
                    string last = null;
                    for (int i = 0; i < n; i++)
                    {
                        Aircraft a = RtbCandidate(hq);
                        Pilot p = a != null ? a.pilots[0] : null;
                        PilotBaseState landing = p != null ? (PilotBaseState)p.AILandingState ?? p.AIHeloLandingState : null;
                        if (landing == null) break;
                        p.SwitchState(landing);
                        last = a.definition != null ? a.definition.unitName : a.unitName;
                        sent++;
                    }
                    Plugin.Logger.LogInfo($"[Supply] over the AI limit: {sent} faction aircraft sent to land ({last})");
                    return sent > 0 ? last + " sent to base" : "no AI could go home";
                }
                default:
                    return "×" + ShopRules.SurchargeMultiplier.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " cost";
            }
        }
    }
}
