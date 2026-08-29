using System;
using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Keeps the surviving wing available after the player's pilot is lost, then replaces
    /// the selected AI aircraft with a fresh player-controlled copy. Spawning through the
    /// stock player path lets the game perform authority, cockpit, HUD, camera and local-sim
    /// setup itself; no live AI aircraft is ever possessed or manually rewired.
    /// </summary>
    internal static class WingTakeover
    {
        private static WingRegistry wing;
        private static Aircraft lostLeader;
        private static GlobalPosition lossPosition;
        private static bool active;
        private static bool defeatSuppressed;
        private static Rect window = new Rect(0f, 0f, 720f, 350f);

        private static bool stylesReady;
        private static GUIStyle panelStyle;
        private static GUIStyle alertStyle;
        private static GUIStyle titleStyle;
        private static GUIStyle subtitleStyle;
        private static GUIStyle cardStyle;
        private static GUIStyle cardCallsignStyle;
        private static GUIStyle cardTypeStyle;
        private static GUIStyle cardMetaStyle;
        private static GUIStyle keyStyle;
        private static GUIStyle footerStyle;
        private static GUIStyle secondaryButtonStyle;
        private static Texture2D accentTexture;
        private static Texture2D headerTexture;
        private static Texture2D borderTexture;
        private static Texture2D barBackgroundTexture;
        private static Texture2D barReadyTexture;
        private static Texture2D barWarningTexture;

        public static bool Active => active;

        /// <summary>Called when the registry first notices that its leader is no longer flyable.</summary>
        public static bool Begin(WingRegistry registry, Aircraft previousLeader)
        {
            if (!CanOffer(registry)) return false;

            wing = registry;
            lostLeader = previousLeader;
            lossPosition = previousLeader.GlobalPosition();
            active = true;
            CentreWindow();

            // Put the choice in the same context as the game's normal post-loss flow. The
            // maximised tactical map also releases the cursor immediately instead of making
            // the player wait for the stock five-second death delay before buttons work.
            try
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && !DynamicMap.mapMaximized) map.Maximize();
            }
            catch { /* Numeric shortcuts still make the prompt usable if the map is absent. */ }

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
            var candidates = CurrentCandidates();
            for (int i = 0; i < candidates.Count && i < 8; i++)
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
            }
        }

        public static void LeaderRestored(Aircraft leader)
        {
            if (!active) return;
            active = false;
            defeatSuppressed = false;
            lostLeader = null;
            lossPosition = default(GlobalPosition);
            wing = null;
            Plugin.Logger.LogInfo("[Takeover] player acquired " + leader.unitName + " through the normal game flow");
        }

        public static void DrawWindow()
        {
            if (!active || wing == null) return;

            EnsureStyles();
            int rows = Mathf.CeilToInt(CandidateCount() / 2f);
            window.height = 166f + rows * 82f;
            CentreWindow();
            window = GUI.Window(0x574D43, window, DrawContents, GUIContent.none, panelStyle);
        }

        private static void DrawContents(int id)
        {
            float width = window.width;
            GUI.DrawTexture(new Rect(1f, 1f, width - 2f, 36f), headerTexture);
            GUI.DrawTexture(new Rect(0f, 0f, width, 1f), borderTexture);
            GUI.DrawTexture(new Rect(0f, window.height - 1f, width, 1f), borderTexture);
            GUI.DrawTexture(new Rect(0f, 0f, 1f, window.height), borderTexture);
            GUI.DrawTexture(new Rect(width - 1f, 0f, 1f, window.height), borderTexture);
            GUI.DrawTexture(new Rect(18f, 36f, width - 36f, 1f), accentTexture);

            GUI.Label(new Rect(24f, 9f, width - 48f, 20f), "WING COMMAND  /  AIRFRAME RECOVERY", titleStyle);
            GUI.Label(new Rect(24f, 46f, width - 48f, 28f), "PILOT DOWN", alertStyle);
            GUI.Label(new Rect(24f, 77f, width - 48f, 20f),
                      "Select a surviving wing aircraft. A fresh player airframe will replace it in position.",
                      subtitleStyle);

            List<WingMember> candidates = CurrentCandidates();
            const float gap = 12f;
            const float left = 24f;
            const float cardHeight = 70f;
            float cardWidth = (width - left * 2f - gap) * 0.5f;
            float top = 107f;

            for (int i = 0; i < candidates.Count; i++)
            {
                WingMember member = candidates[i];
                Aircraft aircraft = member.Aircraft;
                int column = i % 2;
                int row = i / 2;
                Rect card = new Rect(left + column * (cardWidth + gap), top + row * 82f,
                                     cardWidth, cardHeight);

                if (GUI.Button(card, GUIContent.none, cardStyle))
                {
                    TakeControl(member);
                    GUIUtility.ExitGUI();
                }

                GUI.Label(new Rect(card.x + 12f, card.y + 8f, 42f, 22f), (i + 1).ToString(), keyStyle);
                GUI.Label(new Rect(card.x + 60f, card.y + 7f, 80f, 22f), Callsign(member), cardCallsignStyle);

                string type = aircraft.definition != null
                    ? (!string.IsNullOrEmpty(aircraft.definition.code)
                        ? aircraft.definition.code
                        : aircraft.definition.unitName)
                    : member.Name;
                GUI.Label(new Rect(card.x + 142f, card.y + 8f, card.width - 154f, 20f),
                          UiTheme.Truncate(type, 22), cardTypeStyle);

                int fuel = Mathf.RoundToInt(member.Fuel * 100f);
                float range = Mathf.Sqrt(FastMath.SquareDistance(aircraft.GlobalPosition(), lossPosition));
                string rangeText = range >= 1000f
                    ? (range / 1000f).ToString("F1") + " KM"
                    : Mathf.RoundToInt(range) + " M";
                GUI.Label(new Rect(card.x + 60f, card.y + 34f, card.width - 72f, 18f),
                          "FUEL " + fuel + "%     STORES " + member.Ammo + "     RANGE " + rangeText,
                          cardMetaStyle);

                Rect fuelBar = new Rect(card.x + 60f, card.y + 56f, card.width - 72f, 3f);
                GUI.DrawTexture(fuelBar, barBackgroundTexture);
                fuelBar.width *= Mathf.Clamp01(member.Fuel);
                GUI.DrawTexture(fuelBar,
                    member.Fuel <= Plugin.Config2.BingoFuel.Value ? barWarningTexture : barReadyTexture);
            }

            float footerY = top + Mathf.CeilToInt(candidates.Count / 2f) * 82f + 6f;
            GUI.Label(new Rect(24f, footerY, width - 260f, 28f),
                      "[1–8] SELECT AIRCRAFT", footerStyle);

            string decline = MissionHelper.CanRespawn ? "[R]  NORMAL RESPAWN" : "[R]  ACCEPT DEFEAT";
            if (GUI.Button(new Rect(width - 226f, footerY - 3f, 202f, 30f), decline,
                           secondaryButtonStyle))
            {
                ContinueWithoutTakeover("Returning to aircraft selection");
                GUIUtility.ExitGUI();
            }
        }

        private static List<WingMember> CurrentCandidates()
        {
            var result = new List<WingMember>();
            if (wing == null) return result;

            foreach (WingMember member in wing.Members)
            {
                if (IsCandidate(member)) result.Add(member);
            }
            return result;
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

                active = false;
                defeatSuppressed = false;
                lostLeader = null;
                lossPosition = default(GlobalPosition);
                wing = null;
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
                if (replacement != null &&
                    GameManager.GetLocalAircraft(out Aircraft current) && current == replacement)
                {
                    wing.ReplaceWithLeader(member, replacement);
                    active = false;
                    defeatSuppressed = false;
                    lostLeader = null;
                    lossPosition = default(GlobalPosition);
                    wing = null;
                }
            }
        }

        private static bool CanOffer(WingRegistry registry)
        {
            if (!Plugin.Config2.TakeoverOnDeath.Value || registry == null ||
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

            active = false;
            defeatSuppressed = false;
            lostLeader = null;
            lossPosition = default(GlobalPosition);
            wing = null;

            oldWing?.DisbandAll(reason);
            WingCommandManager.Instance?.Toast(reason);
            Plugin.Logger.LogInfo("[Takeover] " + reason);

            if (finishDefeat) GameManager.FinishGame(GameResolution.Defeat);
        }

        public static void Reset()
        {
            active = false;
            defeatSuppressed = false;
            lostLeader = null;
            lossPosition = default(GlobalPosition);
            wing = null;
        }

        private static void CentreWindow()
        {
            window.x = Mathf.Max(10f, (Screen.width - window.width) * 0.5f);
            window.y = Mathf.Max(10f, (Screen.height - window.height) * 0.35f);
        }

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

        private static void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;

            Color green = UiTheme.Green;
            Color friendly = UiTheme.Friendly;
            Color alert = UiTheme.Alert;

            panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = Solid(new Color(0.105f, 0.135f, 0.165f, 0.96f));
            panelStyle.border = new RectOffset(1, 1, 1, 1);

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
            };
            titleStyle.normal.textColor = new Color(0.78f, 1f, 0.80f, 1f);

            alertStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
            };
            alertStyle.normal.textColor = alert.WithAlpha(0.90f);

            subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
            };
            subtitleStyle.normal.textColor = new Color(0.84f, 0.90f, 0.86f, 1f);

            cardStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
            };
            cardStyle.normal.background = Solid(new Color(0.18f, 0.22f, 0.26f, 0.96f));
            cardStyle.hover.background = Solid(new Color(0.24f, 0.34f, 0.27f, 0.98f));
            cardStyle.active.background = Solid(new Color(0.30f, 0.43f, 0.32f, 1f));

            cardCallsignStyle = new GUIStyle(titleStyle) { fontSize = 14 };
            cardCallsignStyle.normal.textColor = new Color(0.76f, 1f, 0.78f, 1f);
            cardTypeStyle = new GUIStyle(titleStyle)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleRight,
            };
            cardTypeStyle.normal.textColor = Color.white;
            cardMetaStyle = new GUIStyle(subtitleStyle) { fontSize = 10 };
            cardMetaStyle.normal.textColor = new Color(0.73f, 0.90f, 0.76f, 0.88f);

            keyStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            keyStyle.normal.background = Solid(new Color(0.30f, 0.34f, 0.36f, 0.98f));
            keyStyle.normal.textColor = new Color(0.80f, 1f, 0.82f, 1f);

            footerStyle = new GUIStyle(titleStyle) { fontSize = 10 };
            footerStyle.normal.textColor = new Color(0.72f, 0.82f, 0.75f, 0.80f);

            secondaryButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            secondaryButtonStyle.normal.background = Solid(new Color(0.34f, 0.36f, 0.37f, 0.96f));
            secondaryButtonStyle.hover.background = Solid(new Color(0.29f, 0.48f, 0.32f, 0.98f));
            secondaryButtonStyle.normal.textColor = new Color(0.93f, 0.96f, 0.93f);
            secondaryButtonStyle.hover.textColor = Color.white;

            accentTexture = Solid(green.WithAlpha(0.58f));
            headerTexture = Solid(new Color(0.16f, 0.20f, 0.24f, 0.96f));
            borderTexture = Solid(new Color(0.48f, 0.52f, 0.53f, 0.76f));
            barBackgroundTexture = Solid(new Color(0.24f, 0.28f, 0.28f, 1f));
            barReadyTexture = Solid(new Color(0.36f, 0.68f, 0.40f, 0.95f));
            barWarningTexture = Solid(UiTheme.Warning.WithAlpha(0.95f));
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return texture;
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
