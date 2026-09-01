using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>
    /// Cached reflection accessors for the private members the radial-menu integration
    /// needs.
    ///
    /// The usual approach here is an assembly publicizer, but this machine's application
    /// control policy blocks the publicizer's MSBuild task from loading, so the members
    /// are reached through Harmony's <c>AccessTools</c> instead. Everything is resolved
    /// once at startup and reported through <see cref="Available"/>, so a future game
    /// update that renames a field degrades to "native radial unavailable" rather than
    /// throwing every frame.
    /// </summary>
    internal static class GameAccess
    {
        // RadialMenuMain
        private static AccessTools.FieldRef<RadialMenuMain, RadialMenuAction[]> actionsMainRef;
        private static AccessTools.FieldRef<RadialMenuMain, Aircraft> menuAircraftRef;
        private static MethodInfo setupMainMethod;

        // VirtualMFD
        private static AccessTools.FieldRef<VirtualMFD, List<Button>> leftButtonsRef;
        private static AccessTools.FieldRef<VirtualMFD, List<Button>> rightButtonsRef;
        private static AccessTools.FieldRef<VirtualMFD, List<MFDScreen>> leftScreensRef;
        private static AccessTools.FieldRef<VirtualMFD, List<MFDScreen>> rightScreensRef;

        // RadialMenuAction
        private static AccessTools.FieldRef<RadialMenuAction, RadialMenuAction.ActionType> actionTypeRef;
        private static AccessTools.FieldRef<RadialMenuAction, Sprite> iconSpriteRef;
        private static AccessTools.FieldRef<RadialMenuAction, Sprite> backgroundSpriteRef;
        private static AccessTools.FieldRef<RadialMenuAction, Color> bgInactiveRef;
        private static AccessTools.FieldRef<RadialMenuAction, Color> bgActiveRef;
        private static AccessTools.FieldRef<RadialMenuAction, Image> iconImageRef;

        /// <summary>True when every member resolved. False disables native radial integration.</summary>
        public static bool Available { get; private set; }

        public static string UnavailableReason { get; private set; }

        /// <summary>True when the VirtualMFD internals resolved, enabling the WMC screen.</summary>
        public static bool MfdAvailable { get; private set; }

        // Where a landing aircraft is actually going. Both stock landing states keep their
        // destination private, and it is the only honest source for it: Return To Base
        // hands off to the game's own state, which picks its own airbase, so anything the
        // mod computed itself would be a guess that disagrees with the aircraft whenever
        // the nearest base is not the one it chose.
        private static AccessTools.FieldRef<AIPilotLandingState, Airbase> landingAirbaseRef;
        private static AccessTools.FieldRef<AIHeloLandingState, Airbase.VerticalLandingPoint>
            heloLandingPointRef;

        /// <summary>True when a landing aircraft's destination can be read.</summary>
        public static bool LandingDestinationAvailable { get; private set; }

        public static void Initialise()
        {
            try
            {
                actionsMainRef  = Field<RadialMenuMain, RadialMenuAction[]>("actionsMain");
                menuAircraftRef = Field<RadialMenuMain, Aircraft>("aircraft");
                setupMainMethod = Require(AccessTools.Method(typeof(RadialMenuMain), "SetupMain"),
                                          "RadialMenuMain.SetupMain()");

                actionTypeRef       = Field<RadialMenuAction, RadialMenuAction.ActionType>("actionType");
                iconSpriteRef       = Field<RadialMenuAction, Sprite>("iconSprite");
                backgroundSpriteRef = Field<RadialMenuAction, Sprite>("backgroundSprite");
                bgInactiveRef       = Field<RadialMenuAction, Color>("backgroundColorInactive");
                bgActiveRef         = Field<RadialMenuAction, Color>("backgroundColorActive");
                iconImageRef        = Field<RadialMenuAction, Image>("iconImage");

                Available = true;

                // The MFD panel is a separate, optional feature: if these do not resolve
                // the radial still works, so failure is tracked on its own flag.
                try
                {
                    leftButtonsRef  = Field<VirtualMFD, List<Button>>("leftButtons");
                    rightButtonsRef = Field<VirtualMFD, List<Button>>("rightButtons");
                    leftScreensRef  = Field<VirtualMFD, List<MFDScreen>>("leftScreens");
                    rightScreensRef = Field<VirtualMFD, List<MFDScreen>>("rightScreens");
                    MfdAvailable = true;
                }
                catch (Exception mfd)
                {
                    MfdAvailable = false;
                    Plugin.Logger.LogWarning(
                        "MFD panel integration unavailable (" + mfd.Message +
                        "). The map overlay panel will be used instead.");
                }

                // Cosmetic on its own: without it the map simply draws no line for a
                // wingman that is on its way home.
                try
                {
                    landingAirbaseRef = Field<AIPilotLandingState, Airbase>("airbase");
                    heloLandingPointRef =
                        Field<AIHeloLandingState, Airbase.VerticalLandingPoint>("landingPoint");
                    LandingDestinationAvailable = true;
                }
                catch (Exception landing)
                {
                    LandingDestinationAvailable = false;
                    Plugin.Logger.LogWarning(
                        "Landing destination unreadable (" + landing.Message +
                        "). RTB will not be drawn on the map.");
                }
            }
            catch (Exception e)
            {
                Available = false;
                UnavailableReason = e.Message;
                Plugin.Logger.LogWarning(
                    "Native radial menu integration unavailable (" + e.Message +
                    "). Falling back to the standalone wheel; bind Keys/FallbackRadialMenu to use it.");
            }
        }

        private static AccessTools.FieldRef<TClass, TField> Field<TClass, TField>(string name)
        {
            FieldInfo info = Require(AccessTools.Field(typeof(TClass), name),
                                     typeof(TClass).Name + "." + name);
            return AccessTools.FieldRefAccess<TClass, TField>(info);
        }

        private static T Require<T>(T member, string description) where T : class
        {
            if (member == null) throw new MissingMemberException("could not resolve " + description);
            return member;
        }

        // ------------------------------------------------------------ RadialMenuMain

        public static RadialMenuAction[] GetActionsMain(RadialMenuMain menu) => actionsMainRef(menu);

        public static void SetActionsMain(RadialMenuMain menu, RadialMenuAction[] value) =>
            actionsMainRef(menu) = value;

        public static Aircraft GetMenuAircraft(RadialMenuMain menu) => menuAircraftRef(menu);

        public static void SetupMain(RadialMenuMain menu) => setupMainMethod.Invoke(menu, null);

        // --------------------------------------------------------------- VirtualMFD

        public static List<Button> GetLeftButtons(VirtualMFD mfd) => leftButtonsRef(mfd);
        public static List<Button> GetRightButtons(VirtualMFD mfd) => rightButtonsRef(mfd);
        public static List<MFDScreen> GetLeftScreens(VirtualMFD mfd) => leftScreensRef(mfd);
        public static List<MFDScreen> GetRightScreens(VirtualMFD mfd) => rightScreensRef(mfd);

        // ------------------------------------------------------------------- landing

        /// <summary>
        /// Where an aircraft that is currently landing is going, if it has picked somewhere.
        ///
        /// Both stock landing states settle on their destination inside their own update,
        /// so this returns false for the first moments after the order is given as well as
        /// on any build where the fields did not resolve. Callers draw nothing rather than
        /// falling back to a guessed airbase — a line to the wrong base is worse than no
        /// line, because the map is the thing the player would use to check.
        /// </summary>
        public static bool TryGetLandingDestination(Pilot pilot, out GlobalPosition destination)
        {
            destination = default;
            if (!LandingDestinationAvailable || pilot == null) return false;

            try
            {
                if (pilot.currentState == pilot.AILandingState && pilot.AILandingState != null)
                {
                    Airbase airbase = landingAirbaseRef(pilot.AILandingState);
                    if (airbase == null) return false;
                    destination = airbase.transform.GlobalPosition();
                    return true;
                }

                if (pilot.currentState == pilot.AIHeloLandingState && pilot.AIHeloLandingState != null)
                {
                    Airbase.VerticalLandingPoint point = heloLandingPointRef(pilot.AIHeloLandingState);
                    if (point == null || point.point == null) return false;
                    destination = point.point.GlobalPosition();
                    return true;
                }
            }
            catch (Exception e)
            {
                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.Logger.LogWarning("Landing destination read failed: " + e.Message);
            }

            return false;
        }

        // ---------------------------------------------------------- RadialMenuAction

        public static void SetActionType(RadialMenuAction action, RadialMenuAction.ActionType type) =>
            actionTypeRef(action) = type;

        public static Image GetIconImage(RadialMenuAction action) => iconImageRef(action);

        public static void SetIconSprite(RadialMenuAction action, Sprite sprite) =>
            iconSpriteRef(action) = sprite;

        /// <summary>
        /// Copy sprites and colours from a stock action. Runtime-created ScriptableObjects
        /// have null sprites and fully transparent colours, which would draw nothing.
        /// </summary>
        public static void CopyAppearance(RadialMenuAction target, RadialMenuAction template)
        {
            if (target == null || template == null) return;

            iconSpriteRef(target)       = iconSpriteRef(template);
            backgroundSpriteRef(target) = backgroundSpriteRef(template);
            bgInactiveRef(target)       = bgInactiveRef(template);
            bgActiveRef(target)         = bgActiveRef(template);
        }
    }
}
