using System.Collections.Generic;
using UnityEngine;

namespace WingCommand
{
    /// <summary>Single entry point other modules use to reach Personnel. Nested classes mirror the
    /// internal subsystem they forward to one-for-one (no renaming, no behavior change) so Personnel's
    /// internals can be restructured without touching callers in Core/Flight/Combat/Economy/Comms/Ui.</summary>
    internal static class PersonnelFacade
    {
        internal static class Roster
        {
            public static void Reset() => WingPilotRoster.Reset();
            public static float SkillBonus(Aircraft aircraft) => WingPilotRoster.SkillBonus(aircraft);
            public static WingPilot Of(Aircraft aircraft) => WingPilotRoster.Of(aircraft);
            public static WingPilot Of(WingMember member) => WingPilotRoster.Of(member);
            public static void NoteSortie(Aircraft aircraft) => WingPilotRoster.NoteSortie(aircraft);
            public static void NoteSurvivedEngagement(Aircraft aircraft) =>
                WingPilotRoster.NoteSurvivedEngagement(aircraft);
            public static void Retire(PersistentID id, bool survived) => WingPilotRoster.Retire(id, survived);
            public static void Retire(WingMember member, bool survived) => WingPilotRoster.Retire(member, survived);
            public static WingPilot Assign(Aircraft aircraft, WingPilot preferred = null) =>
                WingPilotRoster.Assign(aircraft, preferred);
            public static float ReactionScale(Aircraft aircraft) => WingPilotRoster.ReactionScale(aircraft);
            public static float EnvelopeScale(Aircraft aircraft) => WingPilotRoster.EnvelopeScale(aircraft);
            public static void ReleaseReservation(WingPilot pilot, bool restoreSelection = false) =>
                WingPilotRoster.ReleaseReservation(pilot, restoreSelection);
            public static WingPilot ReserveForRequisition() => WingPilotRoster.ReserveForRequisition();
            public static string RankName(WingRank rank) => WingPilotRoster.RankName(rank);
            public static List<WingPilot> SelectablePilots() => WingPilotRoster.SelectablePilots();
            public static WingPilot Selected => WingPilotRoster.Selected;
            public static void Select(WingPilot pilot) => WingPilotRoster.Select(pilot);
            public static bool IsFlying(WingPilot pilot) => WingPilotRoster.IsFlying(pilot);
            public static bool IsReserved(WingPilot pilot) => WingPilotRoster.IsReserved(pilot);
            public static bool Contains(WingPilot pilot) => WingPilotRoster.Contains(pilot);
            public static List<WingPilot> DisplayRoster() => WingPilotRoster.DisplayRoster();
            public static WingRank TopRank => WingPilotRoster.TopRank;
            public static int XpForRank(WingRank rank) => WingPilotRoster.XpForRank(rank);
            public static WingPilot RecruitManual() => WingPilotRoster.RecruitManual();
            public static bool ContainsCallsign(string callsign) => WingPilotRoster.ContainsCallsign(callsign);
            public static WingRank RankFor(int xp) => WingPilotRoster.RankFor(xp);
            public static WingPilot FindByCallsign(string callsign) => WingPilotRoster.FindByCallsign(callsign);
            public static WingPilot ImportCustom(CustomPilotRecord record) => WingPilotRoster.ImportCustom(record);
            public static bool RemoveFromSquadron(WingPilot pilot) => WingPilotRoster.RemoveFromSquadron(pilot);
            public static bool HasPerk(Aircraft aircraft, PilotPerk perk) => WingPilotRoster.HasPerk(aircraft, perk);
            public static bool HasPerk(WingPilot pilot, PilotPerk perk) => WingPilotRoster.HasPerk(pilot, perk);
        }




        internal static class SearchAndRescue
        {
            public const float LocalRecoveryCost = WingSearchAndRescue.LocalRecoveryCost;
            public static void Tick() => WingSearchAndRescue.Tick();
            public static bool OrganizeLocalRecovery(WingPilot pilot) =>
                WingSearchAndRescue.OrganizeLocalRecovery(pilot);
            public static float LocalRecoveryRemaining(WingPilot pilot) =>
                WingSearchAndRescue.LocalRecoveryRemaining(pilot);
            public static WingPilot PilotOf(PilotDismounted native) => WingSearchAndRescue.PilotOf(native);
            public static void CollectDowned(List<Unit> into) => WingSearchAndRescue.CollectDowned(into);
            public static string Status(WingPilot pilot) => WingSearchAndRescue.Status(pilot);
        }

        internal static class CustomPilots
        {
            public static void EnsurePilotsDirectory() => WingCustomPilots.EnsurePilotsDirectory();
            public static bool TryGetEventLine(string tag, string eventName, string detail, out string phrase) =>
                WingCustomPilots.TryGetEventLine(tag, eventName, detail, out phrase);
            public static void OpenFolder() => WingCustomPilots.OpenFolder();
            public static List<CustomPilotRecord> LoadAllCustomPilots(out int chattersCount) =>
                WingCustomPilots.LoadAllCustomPilots(out chattersCount);
            public static int ImportAll(out int chattersCount, out string message) =>
                WingCustomPilots.ImportAll(out chattersCount, out message);
            public static bool SaveCustomPilots(IEnumerable<CustomPilotRecord> pilots, string fileName = "custom_pilots.json") =>
                WingCustomPilots.SaveCustomPilots(pilots, fileName);
            public static bool SaveOrUpdatePilot(CustomPilotRecord pilot, string fileName = "custom_pilots.json") =>
                WingCustomPilots.SaveOrUpdatePilot(pilot, fileName);
            public static bool DeleteCustomPilot(string callsign) => WingCustomPilots.DeleteCustomPilot(callsign);
        }

        internal static class Portraits
        {
            public static void Reset() => PilotPortrait.Reset();
            public static Sprite Sprite => PilotPortrait.Sprite;
            public static Sprite For(WingPilot pilot) => PilotPortrait.For(pilot);
            public static Sprite ForCustom(int face, int hair, int uniform, int backdrop) =>
                PilotPortrait.ForSelection(PilotPortraitGenerator.FromLegacySelection(face, hair, uniform, backdrop));
            public static Sprite ForSelection(PortraitSelection selection) => PilotPortrait.ForSelection(selection);
        }

        internal static class KillCredit
        {
            public static void Reset() => WingKillCredit.Reset();
            public static void Tick() => WingKillCredit.Tick();
            public static void NoteShot(Aircraft shooter, Unit target) => WingKillCredit.NoteShot(shooter, target);
        }


    }
}
