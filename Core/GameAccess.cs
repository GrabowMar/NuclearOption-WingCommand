using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>Caches private radial and MFD access through Harmony AccessTools. Local application policy
    /// blocks publicizer tasks. Resolve at startup and disable unavailable integrations instead of
    /// throwing each frame.</summary>
    internal static partial class GameAccess
    {
        // Native radial fields.
        private static AccessTools.FieldRef<RadialMenuMain, RadialMenuAction[]> actionsMainRef;
        private static AccessTools.FieldRef<RadialMenuMain, Aircraft> menuAircraftRef;
        private static MethodInfo setupMainMethod;

        // Native MFD fields.
        private static AccessTools.FieldRef<VirtualMFD, List<Button>> leftButtonsRef;
        private static AccessTools.FieldRef<VirtualMFD, List<Button>> rightButtonsRef;
        private static AccessTools.FieldRef<VirtualMFD, List<MFDScreen>> leftScreensRef;
        private static AccessTools.FieldRef<VirtualMFD, List<MFDScreen>> rightScreensRef;

        // Native action fields.
        private static AccessTools.FieldRef<RadialMenuAction, RadialMenuAction.ActionType> actionTypeRef;
        private static AccessTools.FieldRef<RadialMenuAction, Sprite> iconSpriteRef;
        private static AccessTools.FieldRef<RadialMenuAction, Sprite> backgroundSpriteRef;
        private static AccessTools.FieldRef<RadialMenuAction, Color> bgInactiveRef;
        private static AccessTools.FieldRef<RadialMenuAction, Color> bgActiveRef;
        private static AccessTools.FieldRef<RadialMenuAction, Image> iconImageRef;

        /// <summary>Whether all required native radial members resolved.</summary>
        public static bool Available { get; private set; }

        public static string UnavailableReason { get; private set; }

        /// <summary>Whether native MFD internals resolved for the WMC screen.</summary>
        public static bool MfdAvailable { get; private set; }

        // Read the native landing state's chosen airbase; a guessed nearest base may differ from the
        // actual destination.
        private static AccessTools.FieldRef<AIPilotLandingState, Airbase> landingAirbaseRef;
        private static AccessTools.FieldRef<AIHeloLandingState, Airbase.VerticalLandingPoint>
            heloLandingPointRef;

        /// <summary>Whether native landing destinations are readable.</summary>
        public static bool LandingDestinationAvailable { get; private set; }

        // Read the private spawned prefab immediately after TrySpawnAircraft, before registry
        // discovery.
        private static AccessTools.FieldRef<Hangar, GameObject> hangarSpawnedObjectRef;

        /// <summary>Whether the hangar's spawned object is readable.</summary>
        public static bool HangarSpawnAvailable { get; private set; }

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

                // Track optional MFD resolution separately so failure leaves the radial available.
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
                        "). WMC will be unavailable.");
                }

                // If landing reflection fails, omit the RTB map line.
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

                try
                {
                    hangarSpawnedObjectRef = Field<Hangar, GameObject>("spawnedObject");
                    HangarSpawnAvailable = true;
                }
                catch (Exception hangar)
                {
                    HangarSpawnAvailable = false;
                    Plugin.Logger.LogWarning(
                        "Hangar spawn unreadable (" + hangar.Message +
                        "). Hangar deliveries will wait for the unit registry.");
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

        // Radial accessors.

        public static RadialMenuAction[] GetActionsMain(RadialMenuMain menu) => actionsMainRef(menu);

        public static void SetActionsMain(RadialMenuMain menu, RadialMenuAction[] value) =>
            actionsMainRef(menu) = value;

        public static Aircraft GetMenuAircraft(RadialMenuMain menu) => menuAircraftRef(menu);

        public static void SetupMain(RadialMenuMain menu) => setupMainMethod.Invoke(menu, null);

        // MFD accessors.

        public static List<Button> GetLeftButtons(VirtualMFD mfd) => leftButtonsRef(mfd);
        public static List<Button> GetRightButtons(VirtualMFD mfd) => rightButtonsRef(mfd);
        public static List<MFDScreen> GetLeftScreens(VirtualMFD mfd) => leftScreensRef(mfd);
        public static List<MFDScreen> GetRightScreens(VirtualMFD mfd) => rightScreensRef(mfd);

        // Landing accessors.

        /// <summary>Read the native landing destination. Return false until the state chooses one or if
        /// reflection is unavailable; callers should omit the line rather than guess a base.</summary>
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

        /// <summary>Read the assigned landing runway's endpoints and nominal approach direction.</summary>
        public static bool TryGetLandingRunway(Pilot pilot, out GlobalPosition start, out GlobalPosition end, out Vector3 approachDir)
        {
            start = default;
            end = default;
            approachDir = default;
            if (!LandingDestinationAvailable || pilot == null) return false;

            try
            {
                if (pilot.currentState == pilot.AILandingState && pilot.AILandingState != null)
                {
                    Airbase airbase = landingAirbaseRef(pilot.AILandingState);
                    if (airbase != null && airbase.runways != null && airbase.runways.Length > 0)
                    {
                        Airbase.Runway rw = airbase.GetLandingRunway() ?? airbase.runways[0];
                        if (rw != null && rw.Start != null && rw.End != null)
                        {
                            start = rw.Start.GlobalPosition();
                            end = rw.End.GlobalPosition();
                            Vector3 dir = (end - start).normalized;
                            if (dir.sqrMagnitude < 0.01f) dir = rw.Start.forward;
                            approachDir = dir;
                            return true;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (Plugin.Settings.VerboseLogging.Value)
                    Plugin.Logger.LogWarning("Landing runway read failed: " + e.Message);
            }

            return false;
        }

        public static GameObject GetHangarSpawnedObject(Hangar hangar)
        {
            if (!HangarSpawnAvailable || hangar == null) return null;
            try { return hangarSpawnedObjectRef(hangar); }
            catch { return null; }
        }

        // Radial action accessors.

        public static void SetActionType(RadialMenuAction action, RadialMenuAction.ActionType type) =>
            actionTypeRef(action) = type;

        public static Image GetIconImage(RadialMenuAction action) => iconImageRef(action);

        public static void SetIconSprite(RadialMenuAction action, Sprite sprite) =>
            iconSpriteRef(action) = sprite;

        /// <summary>Copy native sprites and colours; newly created actions otherwise have null sprites and
        /// transparent colours.</summary>
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
