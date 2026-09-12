using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// Harmony calls prefixes and postfixes by reflection, so suppress IDE0051 in this file.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Adds a Wing Command root slice and nested pages by swapping actionsMain and rebuilding
    /// native radial content. Restore stock content after leaf actions or timeout; native Rewired
    /// selection works with a captured cursor.</summary>
    internal static class WingRadialMenu
    {
        private const string RootLabel = "Wing Command";
        private const float RestoreAfterSeconds = 6f;

        private static WingMenuAction rootEntry;
        private static WingMenuAction[] commanderMenu;
        private static WingMenuAction[] secondaryMenu;
        private static WingMenuAction[] formationMenu;
        private static WingMenuAction[] combatManeuverMenu;
        private static WingMenuAction[] roeMenu;

        /// <summary>Root actions captured when entering the mod menu.</summary>
        private static RadialMenuAction[] stockActions;

        /// <summary>First confirmed main wheel, used to distinguish other mods' submenus.</summary>
        private static RadialMenuAction[] baselineWheel;

        private static bool inSubmenu;
        private static float lastInUseTime;

        private static WingCommandManager Mgr => WingCommandManager.Instance;

        // Radial lifecycle.

        /// <summary>Check submenu restore timeout each manager frame.</summary>
        public static void Tick()
        {
            if (!GameAccess.Available) return;

            RadialMenuMain menu = SceneSingleton<RadialMenuMain>.i;
            if (menu == null) return;

            bool inUse;
            try { inUse = RadialMenuMain.IsInUse(); }
            catch { return; }

            if (inUse)
            {
                lastInUseTime = Time.unscaledTime;
                return;
            }

            if (inSubmenu && Time.unscaledTime - lastInUseTime > RestoreAfterSeconds)
                RestoreStockWheel();
        }

        /// <summary>Ensure the root slice survives native SetupMain rebuilds.</summary>
        internal static bool EnsureRootInjected(RadialMenuMain menu, bool openingRoot = false)
        {
            if (menu == null || inSubmenu) { Trace(openingRoot, "menu null or in submenu"); return false; }

            RadialMenuAction[] current = GameAccess.GetActionsMain(menu);
            if (current == null) { Trace(openingRoot, "actionsMain is null"); return false; }

            // Capture baseline only at OpenMenu's root boundary; SetupMain also rebuilds foreign
            // submenus.
            if (openingRoot && baselineWheel == null)
                baselineWheel = current;
            else if (baselineWheel == null || !SharesAnyEntry(current, baselineWheel))
            {
                Trace(openingRoot, "not the root wheel (baseline " +
                                   (baselineWheel == null ? "unset" : "set") + ", " +
                                   current.Length + " entries)");
                return false;
            }

            BuildMenus(menu);

            if (Array.IndexOf(current, rootEntry) >= 0)
            {
                Trace(openingRoot, "already injected (" + current.Length + " entries)");
                return false;
            }

            var grown = new RadialMenuAction[current.Length + 1];
            current.CopyTo(grown, 0);
            grown[grown.Length - 1] = rootEntry;

            GameAccess.SetActionsMain(menu, grown);
            baselineWheel = grown;
            Trace(openingRoot, "INJECTED, wheel now " + grown.Length + " entries, aircraft=" +
                               (GameAccess.GetMenuAircraft(menu) == null ? "null" : "set"));
            return true;
        }

        /// <summary>Log each distinct injection diagnostic once to avoid repeated open/rebuild
        /// noise.</summary>
        private static readonly HashSet<string> traced = new HashSet<string>();

        private static void Trace(bool openingRoot, string what)
        {
            string line = "[Radial] " + (openingRoot ? "root" : "rebuild") + ": " + what;
            if (traced.Add(line)) Plugin.LogVerbose(line);
        }

        private static bool SharesAnyEntry(RadialMenuAction[] a, RadialMenuAction[] b)
        {
            foreach (RadialMenuAction candidate in a)
            {
                if (candidate == null || candidate == rootEntry) continue;
                if (Array.IndexOf(b, candidate) >= 0) return true;
            }
            return false;
        }

        // Radial menu construction.

        private static int builtRevision = -1;

        private static void BuildMenus(RadialMenuMain menu)
        {
            // Rebuild cached labels when host revision changes their command meaning.
            if (rootEntry != null && builtRevision == WingHost.Revision) return;
            builtRevision = WingHost.Revision;

            // Use existing native actions as appearance templates.
            RadialMenuAction[] templates = GameAccess.GetActionsMain(menu);
            Func<int, RadialMenuAction> template = i =>
                (templates != null && templates.Length > 0) ? templates[i % templates.Length] : null;

            // Reuse root action identity because injection detects it by reference; replacement would
            // duplicate the slice.
            if (rootEntry == null)
                rootEntry = WingMenuAction.Create(RootLabel, _ => ShowCommanderMenu());

            // Show primary whole-wing orders directly, with the sixth sector opening secondary
            // controls.
            var commander = new List<WingMenuAction>
            {
                Leaf(WingOrderCatalog.Label(WingOrder.Formation), WingAction.Rejoin, "rejoin",
                     () => WingOrderCatalog.IsOfferable(WingOrder.Formation)),
                Leaf(WingOrderCatalog.Label(WingOrder.Attack), WingAction.AttackMyTarget, "attack",
                     () => WingOrderCatalog.IsOfferable(WingOrder.Attack)),
                Leaf(WingOrderCatalog.Label(WingOrder.Engage), WingAction.Engage, "engage",
                     () => WingOrderCatalog.IsOfferable(WingOrder.Engage)),
                Leaf(WingOrderCatalog.Label(WingOrder.FallBack), WingAction.FallBack, "fallback",
                     () => WingOrderCatalog.IsOfferable(WingOrder.FallBack)),
                Leaf(WingOrderCatalog.Label(WingOrder.FireForEffect), WingAction.FireForEffect, "attack",
                     () => WingOrderCatalog.IsOfferable(WingOrder.FireForEffect)),
                Icon(WingMenuAction.Create("More Orders", _ => ShowSecondaryMenu()), "tasking"),
            };

            var secondary = new List<WingMenuAction>
            {
                Icon(WingMenuAction.Create("Rules Of Engagement", _ => ShowRoeMenu()), "posture"),
                Icon(WingMenuAction.Create("Formation", _ => ShowFormationMenu()), "formation"),
                Leaf(WingOrderCatalog.Label(WingOrder.OrbitHere), WingAction.OrbitHere, "orbit",
                     () => WingOrderCatalog.IsOfferable(WingOrder.OrbitHere)),
                Leaf(WingOrderCatalog.Label(WingOrder.JamTarget), WingAction.JamMyTarget, "jam",
                     () => WingFidelity.Jamming && WingOrderCatalog.IsOfferable(WingOrder.JamTarget)),
                Icon(WingMenuAction.Create("Manoeuvres", _ => ShowCombatManeuverMenu(),
                                           _ => WingFidelity.Manoeuvres), "maneuver"),
                Back(ShowCommanderMenu),
            };

            var combatManeuvers = new List<WingMenuAction>
            {
                ManeuverLeaf(ManeuverKind.BreakLeft),
                ManeuverLeaf(ManeuverKind.BreakRight),
                ManeuverLeaf(ManeuverKind.NotchThreat),
                ManeuverLeaf(ManeuverKind.MaskTerrain),
                ManeuverLeaf(ManeuverKind.SplitS),
                ManeuverLeaf(ManeuverKind.Immelmann),
                Back(ShowSecondaryMenu),
            };

            var roes = new List<WingMenuAction>
            {
                Roe("Hold", WingRoe.Hold),
                Roe("Tight", WingRoe.Tight),
                Roe("Free", WingRoe.Free),
                Back(ShowSecondaryMenu),
            };

            var formations = new List<WingMenuAction>();
            foreach (FormationShape shape in FormationShapes.Core)
            {
                FormationShape captured = shape;
                WingMenuAction entry = WingMenuAction.Create(FormationShapes.Pretty(captured), _ =>
                {
                    Plugin.LogVerbose($"[FormationChange] {WingFormation.Shape} -> {captured}");
                    WingFormation.Shape = captured;
                    Mgr?.Toast("Formation: " + FormationShapes.Pretty(captured));
                    WingRadioAudio.Transmission();
                    RestoreStockWheel();
                });
                formations.Add(Icon(entry, "shape_" + captured));
            }
            formations.Add(Back(ShowSecondaryMenu));

            // Destroy hidden submenu assets explicitly during root-only rebuilds; HideAndDontSave does
            // not manage their lifetime.
            DestroySubmenus();
            commanderMenu = commander.ToArray();
            secondaryMenu = secondary.ToArray();
            formationMenu = formations.ToArray();
            combatManeuverMenu = combatManeuvers.ToArray();
            roeMenu = roes.ToArray();

            // Copy native wedge appearance, then replace only the glyph.
            ApplyAppearance(rootEntry, template(0), "root");
            ApplyAll(commanderMenu, template);
            ApplyAll(secondaryMenu, template);
            ApplyAll(formationMenu, template);
            ApplyAll(combatManeuverMenu, template);
            ApplyAll(roeMenu, template);
        }

        private static void ApplyAll(WingMenuAction[] entries, Func<int, RadialMenuAction> template)
        {
            for (int i = 0; i < entries.Length; i++)
                ApplyAppearance(entries[i], template(i), null);
        }

        /// <summary>Assign the entry's glyph key.</summary>
        private static WingMenuAction Icon(WingMenuAction action, string iconKey)
        {
            action.IconKey = iconKey;
            return action;
        }

        /// <summary>Create a Back action that opens another menu.</summary>
        private static WingMenuAction Back(Action target) =>
            Icon(WingMenuAction.Create("Back", _ => target()), "back");

        private static void ApplyAppearance(WingMenuAction action, RadialMenuAction template, string iconKey)
        {
            action.CopyAppearanceFrom(template);

            string key = iconKey ?? action.IconKey;
            if (string.IsNullOrEmpty(key)) return;

            try
            {
                GameAccess.SetIconSprite(action, IconFactory.Get(key));
            }
            catch (Exception e)
            {
                // Retain the borrowed icon if custom glyph construction fails.
                Plugin.Logger.LogWarning("Could not build icon '" + key + "': " + e.Message);
            }
        }

        /// <summary>Execute a leaf order and restore the stock wheel. Optional gates grey unavailable
        /// commands.</summary>
        private static WingMenuAction Leaf(string label, WingAction action, string iconKey,
                                           Func<bool> available = null)
        {
            WingMenuAction entry = WingMenuAction.Create(
                label,
                _ => { Mgr?.Execute(action); RestoreStockWheel(); },
                available == null ? (Func<Aircraft, bool>)null : _ => available());
            return Icon(entry, iconKey);
        }

        /// <summary>Execute a wing-wide manoeuvre and restore the stock wheel.</summary>
        private static WingMenuAction ManeuverLeaf(ManeuverKind kind)
        {
            WingMenuAction entry = WingMenuAction.Create(
                ManeuverCatalog.Label(kind),
                _ => { Mgr?.ExecuteManeuver(kind, wholeWing: true); RestoreStockWheel(); },
                _ => WingFidelity.Manoeuvres);
            return Icon(entry, "maneuver");
        }

        /// <summary>Select a specific ROE directly.</summary>
        private static WingMenuAction Roe(string label, WingRoe roe)
        {
            WingMenuAction entry = WingMenuAction.Create(label, _ =>
            {
                if (Mgr != null)
                {
                    Mgr.Wing.Roe = roe;
                    Mgr.Toast("ROE: " + CombatFacade.Roe.Label(roe));
                    WingRadioAudio.Play(WingRadioAudio.Earcon.RoeCycle);
                }
                RestoreStockWheel();
            });
            return Icon(entry, "posture");
        }


        // Menu swapping.

        private static void ShowCommanderMenu()
        {
            if (commanderMenu != null && commanderMenu.Length >= 5)
            {
                Unit target = null;
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud != null)
                {
                    List<Unit> targets = hud.GetTargetList();
                    if (targets != null && targets.Count > 0 && targets[0] != null && !targets[0].disabled)
                        target = targets[0];
                }

                string targetSuffix = target != null ? $" ({target.unitName})" : " (No Target)";
                commanderMenu[1].DisplayName = WingOrderCatalog.Label(WingOrder.Attack) + targetSuffix;
                commanderMenu[4].DisplayName = WingOrderCatalog.Label(WingOrder.FireForEffect) + targetSuffix;
            }
            Swap(commanderMenu, submenu: true);
        }

        private static void ShowSecondaryMenu() => Swap(secondaryMenu, submenu: true);

        private static void ShowFormationMenu()
        {
            if (formationMenu != null)
            {
                FormationShape current = WingFormation.Shape;
                for (int i = 0; i < FormationShapes.Core.Length && i < formationMenu.Length; i++)
                {
                    FormationShape shape = FormationShapes.Core[i];
                    string pretty = FormationShapes.Pretty(shape);
                    formationMenu[i].DisplayName = (shape == current) ? $"▶ {pretty} (Active)" : pretty;
                }
            }
            Swap(formationMenu, submenu: true);
        }

        private static void ShowCombatManeuverMenu() => Swap(combatManeuverMenu, submenu: true);

        private static void ShowRoeMenu()
        {
            if (roeMenu != null && roeMenu.Length >= 3 && Mgr?.Wing != null)
            {
                WingRoe current = Mgr.Wing.Roe;
                roeMenu[0].DisplayName = current == WingRoe.Hold ? "▶ Hold (Active)" : "Hold";
                roeMenu[1].DisplayName = current == WingRoe.Tight ? "▶ Tight (Active)" : "Tight";
                roeMenu[2].DisplayName = current == WingRoe.Free ? "▶ Free (Active)" : "Free";
            }
            Swap(roeMenu, submenu: true);
        }

        internal static void RestoreStockWheel()
        {
            if (!inSubmenu || stockActions == null) return;
            Swap(stockActions, submenu: false);
            stockActions = null;
        }

        private static void Swap(RadialMenuAction[] actions, bool submenu)
        {
            RadialMenuMain menu = SceneSingleton<RadialMenuMain>.i;
            if (menu == null || actions == null) return;

            // Require the cached aircraft before SetupMain; stock AllowedOnAircraft dereferences it.
            if (GameAccess.GetMenuAircraft(menu) == null) return;

            if (stockActions == null && !submenu) return;
            if (submenu && !inSubmenu) stockActions = GameAccess.GetActionsMain(menu);

            GameAccess.SetActionsMain(menu, (RadialMenuAction[])actions.Clone());
            inSubmenu = submenu;
            lastInUseTime = Time.unscaledTime;

            try
            {
                GameAccess.SetupMain(menu);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Radial submenu rebuild failed, restoring stock wheel: " + e);
                GameAccess.SetActionsMain(menu, stockActions);
                inSubmenu = false;
                try { GameAccess.SetupMain(menu); } catch { /* Leave the wheel unchanged if restoration fails. */ }
            }
        }

        /// <summary>Clear mission-specific radial state.</summary>
        internal static void Reset()
        {
            stockActions = null;
            baselineWheel = null;
            inSubmenu = false;
            DestroySubmenus();
            if (rootEntry != null) UnityEngine.Object.Destroy(rootEntry);
            rootEntry = null;
            builtRevision = -1;
        }

        private static void DestroySubmenus()
        {
            DestroyActions(ref commanderMenu);
            DestroyActions(ref secondaryMenu);
            DestroyActions(ref formationMenu);
            DestroyActions(ref combatManeuverMenu);
            DestroyActions(ref roeMenu);
        }

        private static void DestroyActions(ref WingMenuAction[] actions)
        {
            if (actions == null) return;
            foreach (WingMenuAction action in actions)
                if (action != null) UnityEngine.Object.Destroy(action);
            actions = null;
        }
    }

    [HarmonyPatch(typeof(RadialMenuMain))]
    internal static class WingRadialMenuPatches
    {
        private static bool reportedInactive;

        /// <summary>Log once why native integration is inactive so silent refusal is
        /// diagnosable.</summary>
        private static void ReportInactive(string where)
        {
            if (reportedInactive) return;
            reportedInactive = true;
            Plugin.Logger.LogWarning(
                "[Radial] " + where + ": the game's wheel is being left alone because the " +
                "reflection it needs did not resolve" +
                (GameAccess.UnavailableReason == null
                    ? "" : " (" + GameAccess.UnavailableReason + ")") +
                ". Bind Keys/WingMenu to open the mod's own wheel instead.");
        }

        /// <summary>Seed actionsMain in inherited SceneSingleton Awake, resolved explicitly because
        /// RadialMenuMain does not declare it. Defer SetupMain until OpenMenu supplies a cached
        /// aircraft.</summary>
        [HarmonyPatch]
        internal static class AwakePatch
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(SceneSingleton<RadialMenuMain>), "Awake");

            [HarmonyPostfix]
            private static void Postfix(SceneSingleton<RadialMenuMain> __instance)
            {
                // Mono shares generic reference-type method bodies; ignore every singleton instance
                // except RadialMenuMain.
                if (!(__instance is RadialMenuMain menu)) return;

                if (!WingCommandManager.NativeRadialActive) { ReportInactive("Awake"); return; }

                try
                {
                    WingRadialMenu.EnsureRootInjected(menu, openingRoot: true);
                }
                catch (Exception e)
                {
                    Plugin.Logger.LogError("Failed to seed the wing menu entry at Awake: " + e);
                }
            }
        }

        /// <summary>Reinsert the root slice before native wheel rebuilds, including aircraft
        /// changes.</summary>
        [HarmonyPatch("SetupMain")]
        [HarmonyPrefix]
        private static void SetupMain_Prefix(RadialMenuMain __instance)
        {
            if (!WingCommandManager.NativeRadialActive) { ReportInactive("SetupMain"); return; }

            try
            {
                WingRadialMenu.EnsureRootInjected(__instance);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Failed to inject wing menu entry: " + e);
            }
        }

        /// <summary>Check every OpenMenu because SetupMain may be skipped when the scene prepopulated its
        /// aircraft; rebuild only after new injection.</summary>
        [HarmonyPatch(nameof(RadialMenuMain.OpenMenu))]
        [HarmonyPostfix]
        private static void OpenMenu_Postfix(RadialMenuMain __instance)
        {
            if (!WingCommandManager.NativeRadialActive) { ReportInactive("OpenMenu"); return; }

            try
            {
                if (WingRadialMenu.EnsureRootInjected(__instance, openingRoot: true))
                    GameAccess.SetupMain(__instance);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Failed to inject wing menu entry while opening: " + e);
            }
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        private static void OnDestroy_Postfix()
        {
            WingRadialMenu.Reset();
        }
    }
}
