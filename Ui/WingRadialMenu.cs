using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Harmony calls prefixes and postfixes by reflection.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>Adds a Wing Command slice to the game's radial wheel.
    /// <list type="bullet">
    /// <item>Pages are shown by swapping <c>actionsMain</c> and rebuilding the native wheel.</item>
    /// <item>The stock wheel comes back after a leaf action, or 6 s after the wheel closes.</item>
    /// <item>Pages: Call Wingmen, Form Up, Formation, Spacing, Autopilot, Combat, Recover, Orders (with Escort and
    /// Dismiss).</item>
    /// </list></summary>
    internal static class WingRadialMenu
    {
        private const string RootLabel = "Wing Command";
        private const float RestoreAfterSeconds = 6f;

        private static WingMenuAction rootEntry;
        private static WingMenuAction[] mainMenu, callMenu, formationMenu, spacingMenu, autopilotMenu, combatMenu, recoverMenu, ordersMenu;
        private static RadialMenuAction[] stockActions;
        private static RadialMenuAction[] baselineWheel;
        private static bool inSubmenu;
        private static float lastInUseTime;

        /// <summary>Restore the stock wheel after the timeout; called every frame by <see cref="WingHotkeys"/>.</summary>
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
            if (inSubmenu && Time.unscaledTime - lastInUseTime > RestoreAfterSeconds) RestoreStockWheel();
        }

        /// <summary>Keep the root slice in the wheel across native SetupMain rebuilds.</summary>
        internal static bool EnsureRootInjected(RadialMenuMain menu, bool openingRoot = false)
        {
            if (menu == null || inSubmenu) { Trace(openingRoot, "menu null or in submenu"); return false; }
            RadialMenuAction[] current = GameAccess.GetActionsMain(menu);
            if (current == null) { Trace(openingRoot, "actionsMain is null"); return false; }

            // Capture the baseline only at OpenMenu's root boundary; SetupMain also rebuilds other mods' submenus.
            if (openingRoot && baselineWheel == null)
                baselineWheel = current;
            else if (baselineWheel == null || !SharesAnyEntry(current, baselineWheel))
            {
                Trace(openingRoot, "not the root wheel");
                return false;
            }

            BuildMenus(menu);
            if (Array.IndexOf(current, rootEntry) >= 0) return false;

            var grown = new RadialMenuAction[current.Length + 1];
            current.CopyTo(grown, 0);
            grown[grown.Length - 1] = rootEntry;
            GameAccess.SetActionsMain(menu, grown);
            baselineWheel = grown;
            Trace(openingRoot, "injected, wheel now " + grown.Length + " entries");
            return true;
        }

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

        private static void BuildMenus(RadialMenuMain menu)
        {
            if (rootEntry != null && mainMenu != null) return;

            // Existing native actions are the appearance templates.
            RadialMenuAction[] templates = GameAccess.GetActionsMain(menu);
            Func<int, RadialMenuAction> template = i =>
                templates != null && templates.Length > 0 ? templates[i % templates.Length] : null;

            if (rootEntry == null) rootEntry = WingMenuAction.Create(RootLabel, _ => Swap(mainMenu, submenu: true));

            mainMenu = new[]
            {
                Icon(WingMenuAction.Create("Call Wingmen", _ => Swap(callMenu, submenu: true)), "airframe"),
                Leaf("Form Up", WingCommands.FormUp, "rejoin"),
                Icon(WingMenuAction.Create("Formation", _ => ShowFormation()), "formation"),
                Icon(WingMenuAction.Create("Spacing", _ => ShowSpacing()), "posture"),
                Icon(WingMenuAction.Create("Autopilot", _ => ShowAutopilot()), "move"),
                Icon(WingMenuAction.Create("Combat", _ => Swap(combatMenu, submenu: true)), "selection"),
                Icon(WingMenuAction.Create("Recover", _ => Swap(recoverMenu, submenu: true)), "rtb"),
                Icon(WingMenuAction.Create("Orders", _ => Swap(ordersMenu, submenu: true)), "move"),
            };
            ordersMenu = new[]
            {
                Leaf("Orbit Here", WingCommands.OrbitHere, "move"),
                Leaf("Hold Here", WingCommands.HoldHere, "move"),
                Leaf("Move Ahead", WingCommands.MoveAhead, "move"),
                Leaf("Patrol Here", WingCommands.PatrolHere, "move"),
                Leaf("Land Here", WingCommands.LandHere, "rtb"),
                Leaf("Take Off", WingCommands.TakeOff, "rejoin"),
                Leaf("Escort Target", WingCommands.EscortTarget, "selection"),
                Leaf("Escort Me", WingCommands.EscortMe, "rejoin"),
                Leaf("Dismiss", WingCommands.Dismiss, "rtb"),
                Back(),
            };
            recoverMenu = new[]
            {
                Leaf("RTB", WingCommands.Rtb, "rtb"),
                Leaf("Refit", WingCommands.Refit, "rtb"),
                Back(),
            };
            combatMenu = new[]
            {
                Leaf("Engage", WingCommands.Engage, "selection"),
                Leaf("Attack Target", WingCommands.AttackTarget, "selection"),
                Leaf("Disengage", WingCommands.Disengage, "rejoin"),
                Leaf("Doctrine", WingCommands.NextDoctrine, "selection"),
                Back(),
            };
            callMenu = new[]
            {
                Leaf("Call 1", () => WingCommands.Call(1), "selection"),
                Leaf("Call 2", () => WingCommands.Call(2), "selection"),
                Leaf("Call 3", () => WingCommands.Call(3), "selection"),
                Leaf("Next Field", WingCommands.NextField, "airframe"),
                Leaf("Recruit", WingCommands.Recruit, "selection"),
                Back(),
            };
            formationMenu = new[]
            {
                Leaf("Next Shape", WingCommands.NextShape, "formation"),
                Leaf("Next Family", WingCommands.NextFamily, "formation"),
                Back(),
            };
            spacingMenu = new[]
            {
                Leaf("Close", () => WingCommands.SetSpacing(SpacingPreset.Close), "formation"),
                Leaf("Standard", () => WingCommands.SetSpacing(SpacingPreset.Standard), "formation"),
                Leaf("Open", () => WingCommands.SetSpacing(SpacingPreset.Open), "formation"),
                Leaf("Spread", () => WingCommands.SetSpacing(SpacingPreset.Spread), "formation"),
                Back(),
            };
            autopilotMenu = new[]
            {
                Leaf("Level", () => WingCommands.Autopilot(ApCommand.Level), "move"),
                Leaf("Heading", () => WingCommands.Autopilot(ApCommand.Heading), "move"),
                Leaf("Altitude", () => WingCommands.Autopilot(ApCommand.Altitude), "move"),
                Leaf("Vertical Speed", () => WingCommands.Autopilot(ApCommand.VerticalSpeed), "move"),
                Leaf("Speed", () => WingCommands.Autopilot(ApCommand.Speed), "move"),
                Leaf("Off", () => WingCommands.Autopilot(ApCommand.Off), "back"),
                Back(),
            };

            ApplyAppearance(rootEntry, template(0), "root");
            ApplyAll(mainMenu, template);
            ApplyAll(callMenu, template);
            ApplyAll(formationMenu, template);
            ApplyAll(spacingMenu, template);
            ApplyAll(autopilotMenu, template);
            ApplyAll(combatMenu, template);
            ApplyAll(recoverMenu, template);
            ApplyAll(ordersMenu, template);
        }

        private static void ShowFormation()
        {
            FormationSelection sel = WingService.Instance?.Selection;
            if (sel != null) formationMenu[0].DisplayName = "Next Shape (" + sel.Current.Name + ")";
            Swap(formationMenu, submenu: true);
        }

        private static void ShowSpacing()
        {
            FormationSelection sel = WingService.Instance?.Selection;
            string[] names = { "Close", "Standard", "Open", "Spread" };
            for (int i = 0; i < names.Length; i++)
                spacingMenu[i].DisplayName = sel != null && (int)sel.Spacing == i ? "▶ " + names[i] : names[i];
            Swap(spacingMenu, submenu: true);
        }

        private static void ShowAutopilot()
        {
            HoldSpec h = PlayerAutopilot.Instance != null ? PlayerAutopilot.Instance.Session.Spec : default;
            autopilotMenu[0].DisplayName = Mark("Level", h.Lateral == LateralHold.Level);
            autopilotMenu[1].DisplayName = Mark("Heading", h.Lateral == LateralHold.Heading);
            autopilotMenu[2].DisplayName = Mark("Altitude", h.Vertical == VerticalHold.Altitude);
            autopilotMenu[3].DisplayName = Mark("Vertical Speed", h.Vertical == VerticalHold.VerticalSpeed);
            autopilotMenu[4].DisplayName = Mark("Speed", h.Speed);
            Swap(autopilotMenu, submenu: true);
        }

        private static string Mark(string label, bool active) => active ? "▶ " + label : label;

        private static void ApplyAll(WingMenuAction[] entries, Func<int, RadialMenuAction> template)
        {
            for (int i = 0; i < entries.Length; i++) ApplyAppearance(entries[i], template(i), null);
        }

        private static WingMenuAction Icon(WingMenuAction action, string iconKey)
        {
            action.IconKey = iconKey;
            return action;
        }

        private static WingMenuAction Back() => Icon(WingMenuAction.Create("Back", _ => Swap(mainMenu, submenu: true)), "back");

        /// <summary>A command that runs and then restores the stock wheel.</summary>
        private static WingMenuAction Leaf(string label, Action action, string iconKey) =>
            Icon(WingMenuAction.Create(label, _ =>
            {
                action();
                RestoreStockWheel();
            }), iconKey);

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
                Plugin.Logger.LogWarning("Could not build icon '" + key + "': " + e.Message);
            }
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
            // Stock AllowedOnAircraft dereferences the cached aircraft, so SetupMain needs it.
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
                Plugin.Logger.LogError("Radial page rebuild failed, restoring the stock wheel: " + e);
                GameAccess.SetActionsMain(menu, stockActions);
                inSubmenu = false;
                try { GameAccess.SetupMain(menu); } catch { /* leave the wheel as it is */ }
            }
        }

        /// <summary>Clear mission radial state.</summary>
        internal static void Reset()
        {
            stockActions = null;
            baselineWheel = null;
            inSubmenu = false;
            Destroy(ref mainMenu);
            Destroy(ref callMenu);
            Destroy(ref formationMenu);
            Destroy(ref spacingMenu);
            Destroy(ref autopilotMenu);
            if (rootEntry != null) UnityEngine.Object.Destroy(rootEntry);
            rootEntry = null;
        }

        private static void Destroy(ref WingMenuAction[] actions)
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

        private static void ReportInactive(string where)
        {
            if (reportedInactive) return;
            reportedInactive = true;
            Plugin.Logger.LogWarning("[Radial] " + where + ": the game's wheel is left alone because the reflection it " +
                "needs did not resolve" + (GameAccess.UnavailableReason == null ? "" : " (" + GameAccess.UnavailableReason + ")") +
                ". Use the Keys/* hotkeys instead.");
        }

        /// <summary>Seed actionsMain in the inherited SceneSingleton Awake; RadialMenuMain does not declare it.</summary>
        [HarmonyPatch]
        internal static class AwakePatch
        {
            private static MethodBase TargetMethod() => AccessTools.Method(typeof(SceneSingleton<RadialMenuMain>), "Awake");

            [HarmonyPostfix]
            private static void Postfix(SceneSingleton<RadialMenuMain> __instance)
            {
                // Mono shares generic reference-type method bodies; ignore every other singleton.
                if (!(__instance is RadialMenuMain menu)) return;
                if (!GameAccess.Available) { ReportInactive("Awake"); return; }
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

        [HarmonyPatch("SetupMain")]
        [HarmonyPrefix]
        private static void SetupMain_Prefix(RadialMenuMain __instance)
        {
            if (!GameAccess.Available) { ReportInactive("SetupMain"); return; }
            try
            {
                WingRadialMenu.EnsureRootInjected(__instance);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Failed to inject the wing menu entry: " + e);
            }
        }

        [HarmonyPatch(nameof(RadialMenuMain.OpenMenu))]
        [HarmonyPostfix]
        private static void OpenMenu_Postfix(RadialMenuMain __instance)
        {
            if (!GameAccess.Available) { ReportInactive("OpenMenu"); return; }
            try
            {
                if (WingRadialMenu.EnsureRootInjected(__instance, openingRoot: true)) GameAccess.SetupMain(__instance);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Failed to inject the wing menu entry while opening: " + e);
            }
        }

        [HarmonyPatch("OnDestroy")]
        [HarmonyPostfix]
        private static void OnDestroy_Postfix() => WingRadialMenu.Reset();
    }
}
