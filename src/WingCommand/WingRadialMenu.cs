using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WingCommand
{
    /// <summary>
    /// Nested wing menu built on the game's own radial wheel.
    ///
    /// A single "Wing Command" slice is appended to the stock main wheel. Selecting it
    /// swaps the wheel contents for the commander submenu; selecting "Formation" from
    /// there swaps again for the shape picker. Each leaf action restores the stock wheel
    /// once it has run, and a timeout restores it if the player wanders off.
    ///
    /// This is the same technique BOTE uses: replace <c>RadialMenuMain.actionsMain</c> and
    /// call <c>SetupMain()</c> to rebuild. Using the native wheel also means selection runs
    /// through Rewired look-axis input, which is what actually works while the cursor is
    /// captured for mouse-look.
    /// </summary>
    internal static class WingRadialMenu
    {
        private const string RootLabel = "Wing Command";
        private const float RestoreAfterSeconds = 6f;

        private static WingMenuAction rootEntry;
        private static WingMenuAction[] commanderMenu;
        private static WingMenuAction[] taskingMenu;
        private static WingMenuAction[] formationMenu;
        private static WingMenuAction[] maneuverMenu;
        private static WingMenuAction[] roeMenu;

        /// <summary>The stock wheel contents, captured the first time we swap away.</summary>
        private static RadialMenuAction[] stockActions;

        /// <summary>The main wheel as first observed, used to tell it from foreign submenus.</summary>
        private static RadialMenuAction[] baselineWheel;

        private static bool inSubmenu;
        private static float lastInUseTime;

        private static WingCommandManager Mgr => WingCommandManager.Instance;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Called every frame by the manager. Handles the restore timeout.</summary>
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

        /// <summary>
        /// Ensure the "Wing Command" slice is present in the stock wheel. Called from a
        /// prefix on <c>SetupMain</c> so it survives every rebuild the game does.
        /// </summary>
        internal static void EnsureRootInjected(RadialMenuMain menu)
        {
            if (menu == null || inSubmenu) return;

            RadialMenuAction[] current = GameAccess.GetActionsMain(menu);
            if (current == null) return;

            BuildMenus(menu);

            // BOTE uses the same swap-and-rebuild technique for its own submenus, so
            // SetupMain also fires for wheels that are not the main one. The main wheel
            // always retains the entries seen on first sight (mods append to it rather
            // than replace it), whereas a foreign submenu shares none of them. Without
            // this check, "Wing Command" would appear inside other mods' submenus.
            if (baselineWheel == null)
                baselineWheel = current;
            else if (!SharesAnyEntry(current, baselineWheel))
                return;

            if (Array.IndexOf(current, rootEntry) >= 0) return;

            var grown = new RadialMenuAction[current.Length + 1];
            current.CopyTo(grown, 0);
            grown[grown.Length - 1] = rootEntry;

            GameAccess.SetActionsMain(menu, grown);
            baselineWheel = grown;
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

        // -------------------------------------------------------------------- menus

        private static void BuildMenus(RadialMenuMain menu)
        {
            if (rootEntry != null) return;

            // Borrow appearance from whatever the stock wheel already has.
            RadialMenuAction[] templates = GameAccess.GetActionsMain(menu);
            Func<int, RadialMenuAction> template = i =>
                (templates != null && templates.Length > 0) ? templates[i % templates.Length] : null;

            rootEntry = WingMenuAction.Create(RootLabel, _ => ShowCommanderMenu());

            // Six combat shortcuts. Deliberate management (recruitment, release and the
            // full shape picker) belongs on WMC, where cost and scope can be inspected.
            var commander = new List<WingMenuAction>
            {
                Leaf("Form Up", WingAction.Rejoin, "rejoin"),
                Leaf("Attack My Target", WingAction.AttackMyTarget, "attack"),
                Leaf("Engage", WingAction.Engage, "engage"),
                Leaf("Disengage", WingAction.FallBack, "fallback"),
                Icon(WingMenuAction.Create("Rules Of Engagement", _ => ShowRoeMenu()), "posture"),
                Icon(WingMenuAction.Create("Tasking", _ => ShowTaskingMenu()), "tasking"),
            };

            var tasking = new List<WingMenuAction>
            {
                Leaf("Splash 'Em", WingAction.FireForEffect, "attack"),
                Leaf("Jam Target", WingAction.JamMyTarget, "jam", () => WingBrain.Jamming),
                Leaf("Hold Position", WingAction.OrbitHere, "orbit"),
                Leaf("Deliver Cargo", WingAction.DeliverCargo, "cargo"),
                Leaf("Land Here", WingAction.LandHere, "land"),
                Leaf("Return To Base", WingAction.ReturnToBase, "rtb"),
                Icon(WingMenuAction.Create("Manoeuvres", _ => ShowManeuverMenu(),
                                           _ => WingBrain.Manoeuvres), "maneuver"),
                Icon(WingMenuAction.Create("Formation", _ => ShowFormationMenu()), "formation"),
                Back(ShowCommanderMenu),
            };

            var maneuvers = new List<WingMenuAction>();
            foreach (ManeuverKind maneuver in ManeuverCatalog.All)
                maneuvers.Add(ManeuverLeaf(maneuver));
            maneuvers.Add(Back(ShowTaskingMenu));

            var roes = new List<WingMenuAction>
            {
                Roe("Defend", WingRoe.Hold),
                Roe("Escort", WingRoe.Escort),
                Roe("Free", WingRoe.Free),
                Back(ShowCommanderMenu),
            };

            var formations = new List<WingMenuAction>();
            foreach (FormationShape shape in FormationShapes.Core)
            {
                FormationShape captured = shape;
                WingMenuAction entry = WingMenuAction.Create(FormationShapes.Pretty(captured), _ =>
                {
                    Plugin.Config2.Shape.Value = captured;
                    Mgr?.Toast("Formation: " + FormationShapes.Pretty(captured));
                    RestoreStockWheel();
                });
                formations.Add(Icon(entry, "shape_" + captured));
            }
            formations.Add(Back(ShowTaskingMenu));

            commanderMenu = commander.ToArray();
            taskingMenu = tasking.ToArray();
            formationMenu = formations.ToArray();
            maneuverMenu = maneuvers.ToArray();
            roeMenu = roes.ToArray();

            // Take the wedge background and colours from a stock entry so the slices match
            // the game's styling, then overwrite the icon with our own drawn glyph.
            ApplyAppearance(rootEntry, template(0), "root");
            ApplyAll(commanderMenu, template);
            ApplyAll(taskingMenu, template);
            ApplyAll(formationMenu, template);
            ApplyAll(maneuverMenu, template);
            ApplyAll(roeMenu, template);
        }

        private static void ApplyAll(WingMenuAction[] entries, Func<int, RadialMenuAction> template)
        {
            for (int i = 0; i < entries.Length; i++)
                ApplyAppearance(entries[i], template(i), null);
        }

        /// <summary>Tag an entry with the glyph it should draw.</summary>
        private static WingMenuAction Icon(WingMenuAction action, string iconKey)
        {
            action.IconKey = iconKey;
            return action;
        }

        /// <summary>A "Back" slice that swaps the wheel to another submenu.</summary>
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
                // Keep the borrowed stock icon rather than losing the slice entirely.
                Plugin.Logger.LogWarning("Could not build icon '" + key + "': " + e.Message);
            }
        }

        /// <summary>
        /// A leaf action: run the order, then drop back to the stock wheel. An optional
        /// gate greys the slice out on the native wheel when the order is unavailable
        /// (e.g. Jam in Performance mode).
        /// </summary>
        private static WingMenuAction Leaf(string label, WingAction action, string iconKey,
                                           Func<bool> available = null)
        {
            WingMenuAction entry = WingMenuAction.Create(
                label,
                _ => { Mgr?.Execute(action); RestoreStockWheel(); },
                available == null ? (Func<Aircraft, bool>)null : _ => available());
            return Icon(entry, iconKey);
        }

        /// <summary>A manoeuvre leaf: fly it wing-wide, then drop back to the stock wheel.</summary>
        private static WingMenuAction ManeuverLeaf(ManeuverKind kind)
        {
            WingMenuAction entry = WingMenuAction.Create(
                ManeuverCatalog.Label(kind),
                _ => { Mgr?.ExecuteManeuver(kind, wholeWing: true); RestoreStockWheel(); },
                _ => WingBrain.Manoeuvres);
            return Icon(entry, "maneuver");
        }

        /// <summary>Select a concrete ROE instead of making the player cycle blindly.</summary>
        private static WingMenuAction Roe(string label, WingRoe roe)
        {
            WingMenuAction entry = WingMenuAction.Create(label, _ =>
            {
                if (Mgr != null)
                {
                    Mgr.Wing.Roe = roe;
                    Mgr.Toast("ROE: " + label.ToUpperInvariant());
                }
                RestoreStockWheel();
            });
            return Icon(entry, "posture");
        }


        // ------------------------------------------------------------------ swapping

        private static void ShowCommanderMenu() => Swap(commanderMenu, submenu: true);

        private static void ShowTaskingMenu() => Swap(taskingMenu, submenu: true);

        private static void ShowFormationMenu() => Swap(formationMenu, submenu: true);

        private static void ShowManeuverMenu() => Swap(maneuverMenu, submenu: true);

        private static void ShowRoeMenu() => Swap(roeMenu, submenu: true);

        internal static void RestoreStockWheel()
        {
            if (!inSubmenu || stockActions == null) return;
            Swap(stockActions, submenu: false);
        }

        private static void Swap(RadialMenuAction[] actions, bool submenu)
        {
            RadialMenuMain menu = SceneSingleton<RadialMenuMain>.i;
            if (menu == null || actions == null) return;

            // SetupMain evaluates AllowedOnAircraft against the cached aircraft; with no
            // aircraft the stock entries dereference null.
            if (GameAccess.GetMenuAircraft(menu) == null) return;

            if (stockActions == null && !submenu) return;
            if (stockActions == null) stockActions = GameAccess.GetActionsMain(menu);

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
                try { GameAccess.SetupMain(menu); } catch { /* nothing further we can do */ }
            }
        }

        /// <summary>Drop cached state when leaving a mission.</summary>
        internal static void Reset()
        {
            stockActions = null;
            baselineWheel = null;
            inSubmenu = false;
        }
    }

    [HarmonyPatch(typeof(RadialMenuMain))]
    internal static class WingRadialMenuPatches
    {
        /// <summary>
        /// Re-add the "Wing Command" slice before every wheel rebuild. The game rebuilds
        /// whenever the player's aircraft changes, which would otherwise drop it.
        /// </summary>
        [HarmonyPatch("SetupMain")]
        [HarmonyPrefix]
        private static void SetupMain_Prefix(RadialMenuMain __instance)
        {
            if (!Plugin.Config2.UseNativeRadial.Value || !GameAccess.Available) return;

            try
            {
                WingRadialMenu.EnsureRootInjected(__instance);
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("Failed to inject wing menu entry: " + e);
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
