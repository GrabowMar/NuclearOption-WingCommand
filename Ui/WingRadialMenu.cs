using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// Harmony invokes patch Prefix/Postfix methods by reflection.
// IDE0051 cannot see a reflective call, so it is disabled for this file only.
#pragma warning disable IDE0051

namespace WingCommand
{
    /// <summary>
    /// Nested wing menu built on the game's own radial wheel.
    ///
    /// A single "Wing Command" slice is appended to the stock main wheel. Selecting it
    /// opens a small category page, keeping every command page readable instead of putting
    /// an unrelated ring of actions on one wheel. Each leaf action restores the stock
    /// wheel once it has run, and a timeout restores it if the player wanders off.
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
        private static WingMenuAction[] secondaryMenu;
        private static WingMenuAction[] formationMenu;
        private static WingMenuAction[] combatManeuverMenu;
        private static WingMenuAction[] roeMenu;

        /// <summary>The current root wheel contents, captured when its mod entry is selected.</summary>
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
        internal static bool EnsureRootInjected(RadialMenuMain menu, bool openingRoot = false)
        {
            if (menu == null || inSubmenu) { Trace(openingRoot, "menu null or in submenu"); return false; }

            RadialMenuAction[] current = GameAccess.GetActionsMain(menu);
            if (current == null) { Trace(openingRoot, "actionsMain is null"); return false; }

            // BOTE uses the same swap-and-rebuild technique for its own submenus, so
            // SetupMain also fires for wheels that are not the main one. OpenMenu is the
            // reliable root boundary; SetupMain alone is not, and using its first call as
            // the baseline could accidentally capture another mod's submenu.
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

        /// <summary>
        /// Diagnostic breadcrumb for the injection path. Rate-limited to one line per
        /// distinct message: the callers run on every wheel open and every rebuild, and an
        /// unthrottled log here buries everything else in BepInEx's output.
        /// </summary>
        private static readonly HashSet<string> traced = new HashSet<string>();

        private static void Trace(bool openingRoot, string what)
        {
            string line = "[Radial] " + (openingRoot ? "root" : "rebuild") + ": " + what;
            if (traced.Add(line)) Plugin.Logger.LogInfo(line);
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

        private static int builtRevision = -1;

        private static void BuildMenus(RadialMenuMain menu)
        {
            // The wheel is built once and kept, but a host profile can rename the orders on
            // it - the leaf labels below are baked into the WingMenuAction instances, so
            // without this the wheel would keep offering "Form Up" from a ship's bridge.
            if (rootEntry != null && builtRevision == WingHost.Revision) return;
            builtRevision = WingHost.Revision;

            // Borrow appearance from whatever the stock wheel already has.
            RadialMenuAction[] templates = GameAccess.GetActionsMain(menu);
            Func<int, RadialMenuAction> template = i =>
                (templates != null && templates.Length > 0) ? templates[i % templates.Length] : null;

            // Identity is load-bearing: EnsureRootInjected finds our slice in the stock
            // wheel by reference, so a rebuild must reuse the same root action rather than
            // make a new one, which would be injected alongside the old.
            if (rootEntry == null)
                rootEntry = WingMenuAction.Create(RootLabel, _ => ShowCommanderMenu());

            // Direct tactical whole-wing orders on first open, with a 6th slice leading
            // to secondary formations and posture configurations.
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
                Leaf(WingOrderCatalog.Label(WingOrder.ReturnToBase), WingAction.ReturnToBase, "rtb",
                     () => WingOrderCatalog.IsOfferable(WingOrder.ReturnToBase)),
                Icon(WingMenuAction.Create("More Orders", _ => ShowSecondaryMenu()), "tasking"),
            };

            var secondary = new List<WingMenuAction>
            {
                Icon(WingMenuAction.Create("Rules Of Engagement", _ => ShowRoeMenu()), "posture"),
                Icon(WingMenuAction.Create("Formation", _ => ShowFormationMenu()), "formation"),
                Leaf(WingOrderCatalog.Label(WingOrder.OrbitHere), WingAction.OrbitHere, "orbit",
                     () => WingOrderCatalog.IsOfferable(WingOrder.OrbitHere)),
                Leaf(WingOrderCatalog.Label(WingOrder.JamTarget), WingAction.JamMyTarget, "jam",
                     () => WingBrain.Jamming && WingOrderCatalog.IsOfferable(WingOrder.JamTarget)),
                Icon(WingMenuAction.Create("Manoeuvres", _ => ShowCombatManeuverMenu(),
                                           _ => WingBrain.Manoeuvres), "maneuver"),
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
                    Plugin.Logger.LogInfo($"[FormationChange] {WingFormation.Shape} -> {captured}");
                    WingFormation.Shape = captured;
                    Mgr?.Toast("Formation: " + FormationShapes.Pretty(captured));
                    RestoreStockWheel();
                });
                formations.Add(Icon(entry, "shape_" + captured));
            }
            formations.Add(Back(ShowSecondaryMenu));

            commanderMenu = commander.ToArray();
            secondaryMenu = secondary.ToArray();
            formationMenu = formations.ToArray();
            combatManeuverMenu = combatManeuvers.ToArray();
            roeMenu = roes.ToArray();

            // Take the wedge background and colours from a stock entry so the slices match
            // the game's styling, then overwrite the icon with our own drawn glyph.
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
                    Mgr.Toast("ROE: " + RoeRules.Label(roe));
                }
                RestoreStockWheel();
            });
            return Icon(entry, "posture");
        }


        // ------------------------------------------------------------------ swapping

        private static void ShowCommanderMenu() => Swap(commanderMenu, submenu: true);

        private static void ShowSecondaryMenu() => Swap(secondaryMenu, submenu: true);

        private static void ShowFormationMenu() => Swap(formationMenu, submenu: true);

        private static void ShowCombatManeuverMenu() => Swap(combatManeuverMenu, submenu: true);

        private static void ShowRoeMenu() => Swap(roeMenu, submenu: true);

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

            // SetupMain evaluates AllowedOnAircraft against the cached aircraft; with no
            // aircraft the stock entries dereference null.
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
        private static bool reportedInactive;

        /// <summary>
        /// Say once why the native wheel is being left alone. Silence here was the whole
        /// problem: the patches attached and then declined to do anything, which looks
        /// exactly like the patches never running.
        /// </summary>
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

        /// <summary>
        /// Put the slice into <c>actionsMain</c> the moment the wheel object exists, which
        /// is how BOTE does it and is the earliest point that can work.
        ///
        /// <c>RadialMenuMain</c> does not declare Awake — it inherits the one on
        /// <c>SceneSingleton&lt;RadialMenuMain&gt;</c> — so the target is resolved by hand
        /// rather than by attribute.
        ///
        /// Deliberately no <c>SetupMain()</c> call here: at Awake the menu's cached aircraft
        /// is still null, and the stock entries dereference it in AllowedOnAircraft. The
        /// array is simply seeded, and the game's own first OpenMenu builds the wheel from
        /// it. That also makes this the robust path — nothing has to observe an event, the
        /// entry is just *there* before anything reads the array.
        /// </summary>
        [HarmonyPatch]
        internal static class AwakePatch
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(SceneSingleton<RadialMenuMain>), "Awake");

            [HarmonyPostfix]
            private static void Postfix(SceneSingleton<RadialMenuMain> __instance)
            {
                // Mono shares one compiled body across every reference-type instantiation of
                // a generic, so patching the closed SceneSingleton<RadialMenuMain>.Awake also
                // runs for every other SceneSingleton<T> in the game. Claim only our own.
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

        /// <summary>
        /// Re-add the "Wing Command" slice before every wheel rebuild. The game rebuilds
        /// whenever the player's aircraft changes, which would otherwise drop it.
        /// </summary>
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

        /// <summary>
        /// The game only calls SetupMain from OpenMenu when the local aircraft reference
        /// changed. In scenes that pre-populate the reference, patching SetupMain alone
        /// never gets an opportunity to append the mod entry. Checking after every open
        /// supplies that missing lifecycle edge and rebuilds only when injection occurred.
        /// </summary>
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
