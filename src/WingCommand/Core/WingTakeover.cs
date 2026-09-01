using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Keeps the surviving wing available after the player's pilot is lost, then replaces
    /// the selected AI aircraft with a fresh player-controlled copy. Spawning through the
    /// stock player path lets the game perform authority, cockpit, HUD, camera and local-sim
    /// setup itself; no live AI aircraft is ever possessed or manually rewired.
    ///
    /// The prompt is drawn with <see cref="WingUi"/>, the same widgets the WMC page is built
    /// from, on a canvas of its own. It used to be an IMGUI window with a hand-picked slate
    /// palette and Unity's default skin, which is why it read as a debug overlay dropped on
    /// top of the game rather than as part of it.
    /// </summary>
    internal static class WingTakeover
    {
        private const float PanelWidth = 720f;
        private const float Pad = 24f;
        private const float HeaderHeight = 36f;
        private const float CardHeight = 70f;
        private const float CardGap = 12f;
        private const float CardStride = CardHeight + CardGap;
        private const float CardsTop = 118f;

        /// <summary>Cards built once; the roster can only shrink while the prompt is open.</summary>
        private const int MaxCards = 8;

        private static WingRegistry wing;
        private static Aircraft lostLeader;
        private static GlobalPosition lossPosition;
        private static bool active;
        private static bool defeatSuppressed;

        private static GameObject canvasRoot;
        private static RectTransform panel;
        private static RectTransform content;
        private static WingButton declineButton;
        private static readonly List<Card> cards = new List<Card>();
        private static readonly List<WingMember> candidates = new List<WingMember>();
        private static float nextRefresh;
        private static int lastCardCount = -1;

        public static bool Active => active;

        /// <summary>Called when the registry first notices that its leader is no longer flyable.</summary>
        public static bool Begin(WingRegistry registry, Aircraft previousLeader)
        {
            if (!CanOffer(registry)) return false;

            wing = registry;
            lostLeader = previousLeader;
            lossPosition = previousLeader.GlobalPosition();
            active = true;

            // Put the choice in the same context as the game's normal post-loss flow. The
            // maximised tactical map also releases the cursor immediately instead of making
            // the player wait for the stock five-second death delay before buttons work.
            try
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            }
            catch { /* Numeric shortcuts still make the prompt usable if the map is absent. */ }

            Build();
            Plugin.Logger.LogInfo($"[Takeover] leader lost; offering {CandidateCount()} aircraft");
            return true;
        }

        /// <summary>True while the exact player-death/ejection call may safely suppress defeat.</summary>
        public static bool CanSuppressPlayerLoss()
        {
            WingCommandManager manager = WingCommandManager.Instance;
            return manager != null && CanOffer(manager.Wing);
        }

        public static void MarkDefeatSuppressed()
        {
            defeatSuppressed = true;
            Plugin.Logger.LogInfo("[Takeover] delayed player-loss defeat while a wing aircraft is available");
        }

        public static void Tick()
        {
            if (!active) return;

            if (CandidateCount() == 0)
            {
                ContinueWithoutTakeover("No surviving wing aircraft remain");
                return;
            }

            // Immediate keyboard operation matters because death can occur while the cursor
            // is still captured. The visible cards use the same numbers.
            CurrentCandidates();
            for (int i = 0; i < candidates.Count && i < MaxCards; i++)
            {
                KeyCode alpha = (KeyCode)((int)KeyCode.Alpha1 + i);
                KeyCode keypad = (KeyCode)((int)KeyCode.Keypad1 + i);
                if (Input.GetKeyDown(alpha) || Input.GetKeyDown(keypad))
                {
                    TakeControl(candidates[i]);
                    return;
                }
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                ContinueWithoutTakeover("Returning to aircraft selection");
                return;
            }

            // The player may use the stock map to respawn instead of clicking our window.
            // SetLeader handles the roster transition; this check also closes the prompt if
            // another system changes the local aircraft first.
            if (GameManager.GetLocalAircraft(out Aircraft local) &&
                local != null && local != lostLeader && !local.disabled)
            {
                LeaderRestored(local);
                return;
            }

            if (Time.unscaledTime < nextRefresh && candidates.Count == lastCardCount) return;
            nextRefresh = Time.unscaledTime + 0.2f;
            Refresh();
        }

        public static void LeaderRestored(Aircraft leader)
        {
            if (!active) return;
            Close();
            Plugin.Logger.LogInfo("[Takeover] player acquired " + leader.unitName + " through the normal game flow");
        }

        // ------------------------------------------------------------------------ panel

        /// <summary>
        /// One offered aircraft. Built once and rebound, so the numbers can tick over
        /// without the panel being torn down under the player's cursor.
        /// </summary>
        private sealed class Card
        {
            public GameObject Root;
            public TMP_Text Key;
            public TMP_Text Callsign;
            public TMP_Text Type;
            public TMP_Text Meta;
            public Image FuelTrack;
            public Image FuelFill;
            public WingMember Bound;
        }

        private static void Build()
        {
            if (canvasRoot != null) return;

            canvasRoot = new GameObject("WingCommand_Takeover", typeof(RectTransform),
                                        typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(canvasRoot);

            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the map and the HUD: this is a modal choice, and anything drawn over it
            // would be a choice the player cannot see they are making.
            canvas.sortingOrder = 5000;

            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel = panelObject.GetComponent<RectTransform>();
            panel.SetParent(canvasRoot.transform, worldPositionStays: false);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.localScale = Vector3.one;

            Image background = panelObject.GetComponent<Image>();
            background.sprite = WingUi.PanelSprite();
            background.type = Image.Type.Sliced;
            background.color = Color.white;
            background.raycastTarget = true;

            var contentObject = new GameObject("Content", typeof(RectTransform));
            content = contentObject.GetComponent<RectTransform>();
            content.SetParent(panel, worldPositionStays: false);
            WingUi.Stretch(content);

            BuildHeader();
            BuildCards();
            BuildFooter();

            lastCardCount = -1;
            nextRefresh = 0f;
            Refresh();
        }

        private static void BuildHeader()
        {
            float width = PanelWidth - Pad * 2f;

            WingUi.Label(content, "WING COMMAND  /  AIRFRAME RECOVERY",
                         new Rect(Pad, -10f, width, 20f), WingUi.Green, 13f,
                         FontStyles.Normal, TextAlignmentOptions.Left);
            WingUi.Rule(content, new Rect(Pad, -HeaderHeight, width, 1f), WingUi.Green);

            WingUi.Label(content, "PILOT DOWN", new Rect(Pad, -48f, width, 28f),
                         WingUi.Alert, 22f, FontStyles.Normal, TextAlignmentOptions.Left);
            WingUi.Label(content,
                         "Select a surviving wing aircraft. A fresh player airframe will replace it in position.",
                         new Rect(Pad, -82f, width, 20f), WingUi.Friendly, 12f,
                         FontStyles.Normal, TextAlignmentOptions.Left);
        }

        private static void BuildCards()
        {
            float cardWidth = (PanelWidth - Pad * 2f - CardGap) * 0.5f;

            for (int i = 0; i < MaxCards; i++)
            {
                int column = i % 2;
                int row = i / 2;
                var rect = new Rect(Pad + column * (cardWidth + CardGap),
                                    -(CardsTop + row * CardStride), cardWidth, CardHeight);

                var go = new GameObject("Card" + i, typeof(RectTransform));
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.SetParent(content, worldPositionStays: false);
                WingUi.Place(rt, rect);

                WingUi.Panel(rt, new Rect(0f, 0f, cardWidth, CardHeight),
                             WingMarkers.MemberColor.WithAlpha(0.58f));

                var card = new Card { Root = go };

                // The whole card is the button; the labels sit on top of it and never take
                // the raycast, so there is no dead area inside a clickable row.
                WingUi.HitButton(rt, new Rect(0f, 0f, cardWidth, CardHeight), () =>
                {
                    if (card.Bound != null) TakeControl(card.Bound);
                });

                card.Key = WingUi.Label(rt, "", new Rect(12f, -8f, 26f, 22f),
                                        WingUi.Green, 15f, FontStyles.Normal,
                                        TextAlignmentOptions.Center);
                WingUi.Outline(rt, new Rect(10f, -6f, 30f, 26f), WingUi.FrameColor);

                card.Callsign = WingUi.Label(rt, "", new Rect(50f, -7f, 90f, 22f),
                                             WingMarkers.MemberColor, 15f, FontStyles.Normal,
                                             TextAlignmentOptions.Left);
                card.Type = WingUi.Label(rt, "", new Rect(144f, -8f, cardWidth - 156f, 20f),
                                         WingUi.Friendly, 13f, FontStyles.Normal,
                                         TextAlignmentOptions.Right);
                card.Meta = WingUi.Label(rt, "", new Rect(50f, -34f, cardWidth - 62f, 18f),
                                         WingUi.Dim, 10f, FontStyles.Normal,
                                         TextAlignmentOptions.Left);

                card.FuelTrack = WingUi.Rule(rt, new Rect(50f, -58f, cardWidth - 62f, 2f),
                                             WingUi.Grey.WithAlpha(0.35f));
                card.FuelFill = WingUi.Rule(rt, new Rect(50f, -58f, cardWidth - 62f, 2f),
                                            WingUi.Friendly);

                go.SetActive(false);
                cards.Add(card);
            }
        }

        /// <summary>
        /// The keyboard hint and the decline button. Both are repositioned by
        /// <see cref="Refresh"/>, which is the only place that knows how many rows of cards
        /// the footer has to clear.
        /// </summary>
        private static void BuildFooter()
        {
            footerHint = WingUi.Label(content, "[1-8]  SELECT AIRCRAFT",
                                      new Rect(Pad, 0f, PanelWidth - Pad * 2f - 220f, 20f),
                                      WingUi.Dim, 10f, FontStyles.Normal,
                                      TextAlignmentOptions.Left).rectTransform;

            declineButton = WingUi.Button(content, "",
                                          new Rect(PanelWidth - Pad - 210f, 0f, 210f, WingUi.RowHeight),
                                          () => ContinueWithoutTakeover("Returning to aircraft selection"));
        }

        private static RectTransform footerHint;

        private static void Refresh()
        {
            if (canvasRoot == null) return;

            CurrentCandidates();
            int rows = Mathf.Max(1, Mathf.CeilToInt(candidates.Count / 2f));
            float height = CardsTop + rows * CardStride + 46f;

            panel.sizeDelta = new Vector2(PanelWidth, height);

            if (candidates.Count != lastCardCount)
            {
                lastCardCount = candidates.Count;
                float footerY = -(CardsTop + rows * CardStride + 4f);
                if (footerHint != null)
                    footerHint.anchoredPosition = new Vector2(Pad, footerY - 5f);
                if (declineButton != null)
                {
                    ((RectTransform)declineButton.transform).anchoredPosition =
                        new Vector2(PanelWidth - Pad - 210f, footerY);
                }
            }

            declineButton?.SetText(MissionHelper.CanRespawn
                ? "[R]  NORMAL RESPAWN"
                : "[R]  ACCEPT DEFEAT");

            for (int i = 0; i < cards.Count; i++)
            {
                Card card = cards[i];
                if (i >= candidates.Count)
                {
                    card.Bound = null;
                    if (card.Root.activeSelf) card.Root.SetActive(false);
                    continue;
                }

                WingMember member = candidates[i];
                card.Bound = member;
                if (!card.Root.activeSelf) card.Root.SetActive(true);

                Aircraft aircraft = member.Aircraft;
                card.Key.text = (i + 1).ToString();
                card.Callsign.text = Callsign(member);
                card.Type.text = UiTheme.Truncate(TypeName(member), 22);

                int fuel = Mathf.RoundToInt(member.Fuel * 100f);
                float range = aircraft != null
                    ? Mathf.Sqrt(FastMath.SquareDistance(aircraft.GlobalPosition(), lossPosition))
                    : 0f;
                card.Meta.text = "FUEL " + fuel + "%     STORES " + member.Ammo +
                                 "     RANGE " + UnitConverter.DistanceReading(range);

                bool low = member.Fuel <= Plugin.Settings.BingoFuel.Value;
                RectTransform fill = card.FuelFill.rectTransform;
                float trackWidth = card.FuelTrack.rectTransform.sizeDelta.x;
                fill.sizeDelta = new Vector2(trackWidth * Mathf.Clamp01(member.Fuel),
                                             fill.sizeDelta.y);
                card.FuelFill.color = low ? WingUi.Warning : WingUi.Friendly;
            }
        }

        private static string TypeName(WingMember member)
        {
            Aircraft aircraft = member.Aircraft;
            if (aircraft == null || aircraft.definition == null) return member.Name;

            return !string.IsNullOrEmpty(aircraft.definition.code)
                ? aircraft.definition.code
                : aircraft.definition.unitName;
        }

        private static void Teardown()
        {
            if (canvasRoot != null) UnityEngine.Object.Destroy(canvasRoot);
            canvasRoot = null;
            panel = null;
            content = null;
            declineButton = null;
            footerHint = null;
            cards.Clear();
            candidates.Clear();
            lastCardCount = -1;
            nextRefresh = 0f;
        }

        // ----------------------------------------------------------------------- offers

        private static void CurrentCandidates()
        {
            candidates.Clear();
            if (wing == null) return;

            foreach (WingMember member in wing.Members)
            {
                if (IsCandidate(member)) candidates.Add(member);
            }
        }

        private static void TakeControl(WingMember member)
        {
            if (!active || wing == null || !IsCandidate(member)) return;

            if (!GameManager.GetLocalPlayer<NuclearOption.Networking.Player>(out var player) ||
                player == null || !player.IsServer)
            {
                WingCommandManager.Instance?.Toast("Aircraft takeover is host/single-player only");
                return;
            }

            Aircraft target = member.Aircraft;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (spawner == null)
            {
                WingCommandManager.Instance?.Toast("Unable to access the aircraft spawner");
                return;
            }

            Aircraft replacement = null;
            try
            {
                Loadout loadout = CloneLoadout(target.Networkloadout);
                Vector3 velocity = target.rb != null ? target.rb.velocity : Vector3.zero;
                Vector3 angularVelocity = target.rb != null ? target.rb.angularVelocity : Vector3.zero;

                replacement = spawner.SpawnAircraft(
                    player: player,
                    prefab: target.definition.unitPrefab,
                    loadout: loadout,
                    fuelLevel: target.GetFuelLevel(),
                    livery: target.NetworkLiveryKey,
                    globalPosition: target.GlobalPosition(),
                    rotation: target.transform.rotation,
                    startingVel: velocity,
                    spawningHangar: null,
                    HQ: target.NetworkHQ,
                    uniqueName: "WingCommand_Takeover_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    skill: target.skill,
                    bravery: target.bravery);

                if (replacement == null)
                    throw new InvalidOperationException("the game spawner returned no aircraft");

                if (replacement.rb != null)
                    replacement.rb.angularVelocity = angularVelocity;

                if (!wing.ReplaceWithLeader(member, replacement))
                    throw new InvalidOperationException("selected aircraft left the wing during replacement");

                // Remove the AI source without DisableUnit: reporting it as destroyed would
                // create a false kill, score event and supply loss. The replacement already
                // occupies the same position and represents the same one airframe.
                NetworkManagerNuclearOption.i.ServerObjectManager.Destroy(
                    target.Identity, !target.Identity.IsSceneObject);

                Close();
                WingCommandManager.Instance?.Toast("Replacement aircraft ready: " + replacement.unitName);
                Plugin.Logger.LogInfo("[Takeover] spawned player copy of " + target.unitName +
                                      " and removed the AI source");
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogError("[Takeover] aircraft replacement failed: " + ex);
                WingCommandManager.Instance?.Toast("Aircraft replacement failed; see LogOutput.log");

                // SpawnAircraft commits player ownership during the spawn callback. If a
                // later cleanup step fails, keep that valid player aircraft and only ensure
                // the old AI source can no longer receive wing orders.
                if (wing != null && replacement != null &&
                    GameManager.GetLocalAircraft(out Aircraft current) && current == replacement)
                {
                    wing.ReplaceWithLeader(member, replacement);
                    Close();
                }
            }
        }

        private static bool CanOffer(WingRegistry registry)
        {
            if (!Plugin.Settings.TakeoverOnDeath.Value || registry == null ||
                NetworkSceneSingleton<Spawner>.i == null)
                return false;

            if (!GameManager.GetLocalPlayer<NuclearOption.Networking.Player>(out var player) ||
                player == null || !player.IsServer)
                return false;

            foreach (WingMember member in registry.Members)
            {
                if (IsCandidate(member)) return true;
            }
            return false;
        }

        /// <summary>
        /// Loadout owns a mutable list, so give the spawned aircraft a new container while
        /// reusing the immutable WeaponMount definitions. Sharing the original Loadout
        /// object lets either aircraft's initialization mutate the other's equipment.
        /// </summary>
        private static Loadout CloneLoadout(Loadout source)
        {
            if (source == null) return null;

            return new Loadout
            {
                weapons = source.weapons != null
                    ? new List<WeaponMount>(source.weapons)
                    : new List<WeaponMount>()
            };
        }

        private static bool IsCandidate(WingMember member)
        {
            return member != null && member.Alive && member.Aircraft != null &&
                   member.Aircraft.LocalSim && member.Aircraft.Player == null;
        }

        private static int CandidateCount()
        {
            if (wing == null) return 0;
            int count = 0;
            foreach (WingMember member in wing.Members)
            {
                if (IsCandidate(member)) count++;
            }
            return count;
        }

        private static void ContinueWithoutTakeover(string reason)
        {
            WingRegistry oldWing = wing;
            bool finishDefeat = defeatSuppressed && GameManager.gameResolution == GameResolution.Ongoing;

            Close();

            oldWing?.DisbandAll(reason);
            WingCommandManager.Instance?.Toast(reason);
            Plugin.Logger.LogInfo("[Takeover] " + reason);

            if (finishDefeat) GameManager.FinishGame(GameResolution.Defeat);
        }

        /// <summary>Dismiss the prompt and drop everything it was holding.</summary>
        private static void Close()
        {
            active = false;
            defeatSuppressed = false;
            lostLeader = null;
            lossPosition = default(GlobalPosition);
            wing = null;
            Teardown();
        }

        public static void Reset() => Close();

        private static string Callsign(WingMember member)
        {
            switch (member.Slot)
            {
                case 1: return "TWO";
                case 2: return "THREE";
                case 3: return "FOUR";
                default: return "WING " + (member.Slot + 1);
            }
        }
    }

    /// <summary>
    /// Narrow defeat interception: the guard is raised only around the two vanilla methods
    /// that call FinishGame because the local player was killed or ejected. Objective and
    /// scripted defeats reach FinishGame without the guard and are never affected.
    /// </summary>
    [HarmonyPatch]
    internal static class WingTakeoverPatches
    {
        [ThreadStatic]
        private static int playerLossDepth;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Pilot), nameof(Pilot.ApplyDamage))]
        private static void BeforePilotDamage(
            Pilot __instance,
            float pierceDamage,
            float blastDamage,
            float fireDamage,
            float impactDamage,
            float ___hitPoints,
            byte ___pilotNumber,
            out bool __state)
        {
            float damage = pierceDamage + blastDamage + fireDamage + impactDamage;
            __state = ___pilotNumber == 0 && !__instance.dead && !__instance.ejected &&
                      ___hitPoints - damage < 0f &&
                      GameManager.IsLocalAircraft(__instance.aircraft) &&
                      WingTakeover.CanSuppressPlayerLoss();
            if (__state) playerLossDepth++;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Pilot), nameof(Pilot.ApplyDamage))]
        private static Exception AfterPilotDamage(Exception __exception, bool __state)
        {
            if (__state) playerLossDepth--;
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Aircraft), "UserCode_RpcJettisonCanopy_1196305304")]
        private static void BeforeCanopyJettison(Aircraft __instance, out bool __state)
        {
            __state = GameManager.IsLocalAircraft(__instance) &&
                      WingTakeover.CanSuppressPlayerLoss();
            if (__state) playerLossDepth++;
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(Aircraft), "UserCode_RpcJettisonCanopy_1196305304")]
        private static Exception AfterCanopyJettison(Exception __exception, bool __state)
        {
            if (__state) playerLossDepth--;
            return __exception;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GameManager), nameof(GameManager.FinishGame))]
        private static bool BeforeFinishGame(GameResolution resolution)
        {
            if (resolution != GameResolution.Defeat || playerLossDepth <= 0) return true;

            WingTakeover.MarkDefeatSuppressed();
            return false;
        }
    }
}
