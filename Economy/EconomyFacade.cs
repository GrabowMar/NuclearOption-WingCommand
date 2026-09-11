using System.Collections.Generic;
using NuclearOption.SavedMission;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Single entry point other modules use to reach Economy. Nested classes mirror the internal
    /// subsystem they forward to one-for-one (no renaming, no behavior change) so Economy's internals can
    /// be restructured without touching callers in Core/Flight/Personnel/Comms/Ui. Nested types owned by
    /// the internal classes (WingShop.Offer, WingShop.PurchaseQuote, WingShop.SquadronState,
    /// WingLoadoutCatalog.StoreOption) stay directly referenced by name, same as WingPilot in
    /// PersonnelFacade — the facade wraps static operations, not caller-held data types.</summary>
    internal static class EconomyFacade
    {
        internal static class Shop
        {
            public static void Reset() => WingShop.Reset();
            public static void Tick() => WingShop.Tick();
            public static int PendingWingSlots => WingShop.PendingWingSlots;
            public static bool IsPurchased(Aircraft aircraft) => WingShop.IsPurchased(aircraft);
            public static float PaidFor(Aircraft aircraft) => WingShop.PaidFor(aircraft);
            public static float PaidFor(PersistentID id) => WingShop.PaidFor(id);
            public static bool TakePurchased(PersistentID id) => WingShop.TakePurchased(id);
            public static float Allocation => WingShop.Allocation;
            public static bool IsFlyableAircraft(AircraftDefinition definition) => WingShop.IsFlyableAircraft(definition);
            public static bool MatchesLeader(AircraftDefinition definition) => WingShop.MatchesLeader(definition);
            public static IReadOnlyList<WingShop.Offer> LoadoutCatalogue() => WingShop.LoadoutCatalogue();
            public static IReadOnlyList<WingShop.Offer> Catalogue() => WingShop.Catalogue();
            public static bool MeetsExceedLimitRank => WingShop.MeetsExceedLimitRank;
            public static int ExceedLimitRank => WingShop.ExceedLimitRank;
            public static bool ExceedLimit { get => WingShop.ExceedLimit; set => WingShop.ExceedLimit = value; }
            public static float ExceedLimitMultiplier => WingShop.ExceedLimitMultiplier;
            public static void CycleSpawnFuel() => WingShop.CycleSpawnFuel();
            public static float SpawnFuelLevel => WingShop.SpawnFuelLevel;
            public static bool Buy(AircraftDefinition definition, out string reason, out float paid) =>
                WingShop.Buy(definition, out reason, out paid);
            public static WingShop.PurchaseQuote Quote(AircraftDefinition definition) => WingShop.Quote(definition);
            public static float CurrentPriceOf(AircraftDefinition definition) => WingShop.CurrentPriceOf(definition);
            public static WingShop.SquadronState Squadron() => WingShop.Squadron();
        }

        internal static class LoadoutCatalog
        {
            public static bool Available => WingLoadoutCatalog.Available;
            public static string Label(WingLoadoutChoice choice) => WingLoadoutCatalog.Label(choice);
            public static int PylonCount(AircraftDefinition definition) => WingLoadoutCatalog.PylonCount(definition);
            public static string PylonName(AircraftDefinition definition, int index) =>
                WingLoadoutCatalog.PylonName(definition, index);
            public static bool MirrorsPrevious(AircraftDefinition definition, int index) =>
                WingLoadoutCatalog.MirrorsPrevious(definition, index);
            public static void OptionsFor(AircraftDefinition definition, int index,
                                          List<WingLoadoutCatalog.StoreOption> into) =>
                WingLoadoutCatalog.OptionsFor(definition, index, into);
            public static WingLoadoutCatalog.StoreOption StoreOn(AircraftDefinition definition, int index, string key) =>
                WingLoadoutCatalog.StoreOn(definition, index, key);
            public static bool IsPylonBlocked(AircraftDefinition definition, int index, Loadout inProgress) =>
                WingLoadoutCatalog.IsPylonBlocked(definition, index, inProgress);
            public static Loadout FillScratch(AircraftDefinition definition, IReadOnlyList<string> keys) =>
                WingLoadoutCatalog.FillScratch(definition, keys);
            public static Loadout Build(AircraftDefinition definition, WingLoadoutChoice choice) =>
                WingLoadoutCatalog.Build(definition, choice);
        }

        internal static class LoadoutTemplates
        {
            public const int MaxNameLength = WingLoadoutTemplates.MaxNameLength;
            public const int MaxPerAirframe = WingLoadoutTemplates.MaxPerAirframe;
            public static LoadoutTemplateRecord ById(string id) => WingLoadoutTemplates.ById(id);
            public static IReadOnlyList<LoadoutTemplateRecord> For(AircraftDefinition definition) =>
                WingLoadoutTemplates.For(definition);
            public static LoadoutTemplateRecord Create(AircraftDefinition definition, string name,
                                                       System.Collections.Generic.IEnumerable<string> mountKeys) =>
                WingLoadoutTemplates.Create(definition, name, mountKeys);
            public static string NextDefaultName(AircraftDefinition definition) =>
                WingLoadoutTemplates.NextDefaultName(definition);
            public static LoadoutTemplateRecord Duplicate(LoadoutTemplateRecord source) =>
                WingLoadoutTemplates.Duplicate(source);
            public static void Delete(LoadoutTemplateRecord record) => WingLoadoutTemplates.Delete(record);
            public static void Rename(LoadoutTemplateRecord record, string name) =>
                WingLoadoutTemplates.Rename(record, name);
            public static void SetMount(LoadoutTemplateRecord record, int pylon, string key) =>
                WingLoadoutTemplates.SetMount(record, pylon, key);
            public static List<WingLoadoutTemplates.LiveryOption> GetLiveries(AircraftDefinition definition, Faction faction = null) =>
                WingLoadoutTemplates.GetLiveries(definition, faction);
            public static int GetLiveryIndex(AircraftDefinition definition) => WingLoadoutTemplates.GetLiveryIndex(definition);
            public static void SetLiveryIndex(AircraftDefinition definition, int index) =>
                WingLoadoutTemplates.SetLiveryIndex(definition, index);
            public static int CountFor(AircraftDefinition definition) => WingLoadoutTemplates.CountFor(definition);
            public static bool Exists(string id) => WingLoadoutTemplates.Exists(id);
        }

        internal static class LoadoutBook
        {
            public static void Reset() => WingLoadoutBook.Reset();
            public static WingLoadoutChoice AboardOf(Aircraft aircraft) => WingLoadoutBook.AboardOf(aircraft);
            public static bool IsKnown(Aircraft aircraft) => WingLoadoutBook.IsKnown(aircraft);
            public static void Forget(Aircraft aircraft) => WingLoadoutBook.Forget(aircraft);
            public static void Forget(PersistentID aircraftId) => WingLoadoutBook.Forget(aircraftId);
            public static WingLoadoutChoice PlannedFor(AircraftDefinition definition) => WingLoadoutBook.PlannedFor(definition);
            public static void Plan(AircraftDefinition definition, WingLoadoutChoice choice) =>
                WingLoadoutBook.Plan(definition, choice);
        }

        internal static class SupplyReserve
        {
            public const int Capacity = WingSupplyReserve.Capacity;
            public static void Reset() => WingSupplyReserve.Reset();
            public static void Tick() => WingSupplyReserve.Tick();
            public static bool IsHost => WingSupplyReserve.IsHost;
            public static bool HasFaction => WingSupplyReserve.HasFaction;
            public static int Count => WingSupplyReserve.Count;
            public static int CountOf(AircraftDefinition definition) => WingSupplyReserve.CountOf(definition);
            public static int OwnedOf(AircraftDefinition definition) => WingSupplyReserve.OwnedOf(definition);
            public static int FactionStockOf(AircraftDefinition definition) => WingSupplyReserve.FactionStockOf(definition);
            public static bool Hold(AircraftDefinition definition, out string reason) =>
                WingSupplyReserve.Hold(definition, out reason);
            public static bool Release(AircraftDefinition definition, out bool wasOwned, out string reason) =>
                WingSupplyReserve.Release(definition, out wasOwned, out reason);
            public static bool PeekLoadout(AircraftDefinition definition, out WingLoadoutChoice loadout) =>
                WingSupplyReserve.PeekLoadout(definition, out loadout);
        }

        internal static class LaunchFields
        {
            public static HangarLaunchMode Mode
            {
                get => WingLaunchFields.Mode;
                set => WingLaunchFields.Mode = value;
            }
            public static IReadOnlyList<Airbase> Listing => WingLaunchFields.Listing;
            public static bool IsAllowed(Airbase airbase) => WingLaunchFields.IsAllowed(airbase);
            public static void SetAllowed(Airbase airbase, bool allow) => WingLaunchFields.SetAllowed(airbase, allow);
            public static bool CanProduce(Airbase airbase, AircraftDefinition definition) =>
                WingLaunchFields.CanProduce(airbase, definition);
            public static bool CanAnyAllowedLaunch(FactionHQ hq, AircraftDefinition definition) =>
                WingLaunchFields.CanAnyAllowedLaunch(hq, definition);
            public static void RefreshListing(FactionHQ hq, Vector3 from) => WingLaunchFields.RefreshListing(hq, from);
            public static string DisplayName(Airbase airbase) => WingLaunchFields.DisplayName(airbase);
        }

        internal static class Airfield
        {
            public static void Tick() => WingAirfield.Tick();
            public static void Reset() => WingAirfield.Reset();
            public static bool HasLandingRunway(Aircraft aircraft) => WingAirfield.HasLandingRunway(aircraft);
            public static Airbase FieldUnder(Aircraft aircraft) => WingAirfield.FieldUnder(aircraft);
            public static void DrainLandingList(Aircraft aircraft) => WingAirfield.DrainLandingList(aircraft);
            public static void DrainTakeoffQueue(Aircraft aircraft) => WingAirfield.DrainTakeoffQueue(aircraft);
        }

        internal static class DepartureLane
        {
            public static bool Reserve(Airbase airbase, Transform anchor, object owner) =>
                HangarDepartureLane.Reserve(airbase, anchor, owner);
            public static void Release(object owner) => HangarDepartureLane.Release(owner);
            public static bool IsJammed(Airbase airbase) => HangarDepartureLane.IsJammed(airbase);
        }

        internal static class ShopDelivery
        {
            public static void Reset() => WingShopDelivery.Reset();
            public static void Tick() => WingShopDelivery.Tick();
            public static int PendingCount => WingShopDelivery.PendingCount;
            public static WingShopDelivery.PendingDelivery GetPending(int index) => WingShopDelivery.GetPending(index);
            public static bool CancelPending(WingShopDelivery.PendingDelivery order) => WingShopDelivery.CancelPending(order);
        }

        internal static class HangarStock
        {
            public static string AirframeLaunchText(AircraftDefinition definition, bool allowedOnly) =>
                WingHangarStock.AirframeLaunchText(definition, allowedOnly);
            public static string FieldStockText(Airbase airbase) => WingHangarStock.FieldStockText(airbase);
        }
    }
}
