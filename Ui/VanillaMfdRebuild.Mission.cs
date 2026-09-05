using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.SavedMission;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    internal static partial class VanillaMfdRebuild
    {
        // ----------------------------------------------------------- mission panel

        private sealed class MissionPresenter : Presenter
        {
            private readonly List<Objective> objectives = new List<Objective>();
            private RectTransform[] pages;
            private TMP_Text missionName;
            private TMP_Text missionDescription;
            private TMP_Text missionClock;
            private TMP_Text escalation;
            private MfdPagingGrid objectiveGrid;

            public MissionPresenter(MFDScreen screen) : base(screen, VanillaMfdPanelId.Mis) { }

            protected override int TabCount => 2;

            protected override void BuildContent()
            {
                Shell.ConfigureTabs(new[] { "MISSION", "OBJECTIVES" }, SelectPage);
                pages = new[] { Shell.CreatePage("Mission"), Shell.CreatePage("Objectives") };
                BuildMissionPage(pages[0]);
                BuildObjectivesPage(pages[1]);
                SelectPage(0);
            }

            protected override void RefreshContent()
            {
                Mission mission = MissionManager.CurrentMission;
                MissionManager manager = NetworkSceneSingleton<MissionManager>.i;
                RefreshMissionCopy(mission, manager);
                RefreshObjectives();

                Shell.DataBar.State.text = "MISSION OVERVIEW";
                Shell.DataBar.SetChip(0, objectives.Count + " ACTIVE", objectives.Count > 0);
                Shell.DataBar.SetChip(1, EscalationLabel(manager), manager != null);
                Shell.DataBar.SetChip(2, MissionClock(manager), manager != null);
            }

            protected override string AmbientStatus() =>
                objectives.Count == 0 ? "MISSION STATUS — NO ACTIVE OBJECTIVES" :
                "MISSION STATUS — " + objectives.Count + " ACTIVE OBJECTIVES";

            private void BuildMissionPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "MISSION BRIEF", "LIVE OPERATIONS FEED");
                AvKit.TacticalCard(page,
                    new Rect(AvTokens.Space3, y, Shell.Body.width - AvTokens.Space3, 196f), AvTheme.RailInfo);
                missionName = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 14f, Shell.Body.width - AvTokens.Space5, 30f),
                    "LOADING MISSION", "metric-value");
                missionDescription = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 54f, Shell.Body.width - AvTokens.Space5, 74f),
                    "", "row-sub");
                missionClock = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 144f, Shell.Body.width - AvTokens.Space5, 18f),
                    "TIME —", "row-main");
                escalation = AvStyled.Label(page,
                    new Rect(AvTokens.Space4, y - 168f, Shell.Body.width - AvTokens.Space5, 18f),
                    "ESCALATION —", "row-main");
            }

            private void BuildObjectivesPage(RectTransform page)
            {
                DrawSpine(page);
                float y = Heading(page, -AvTokens.Space1, Shell.Body.width,
                                  "ACTIVE OBJECTIVES", "LIVE MISSION GRAPH");
                objectiveGrid = new MfdPagingGrid(page, y, Shell.Body.width, 1, 9);
            }

            private void SelectPage(int selected)
            {
                for (int i = 0; i < pages.Length; i++) pages[i].gameObject.SetActive(i == selected);
                Shell.SetSelectedTab(selected);
                RequestRefresh();
            }

            private void RefreshMissionCopy(Mission mission, MissionManager manager)
            {
                if (mission == null)
                {
                    missionName.text = "NO MISSION LOADED";
                    missionDescription.text = "Waiting for mission controller data.";
                }
                else
                {
                    missionName.text = string.IsNullOrEmpty(mission.Name) ? "UNTITLED MISSION" : mission.Name;
                    missionDescription.text = mission.missionSettings == null ? "" :
                        AvTheme.Truncate(mission.missionSettings.description ?? "", 180);
                }

                missionClock.text = "MISSION TIME  " + MissionClock(manager);
                escalation.text = "ESCALATION    " + EscalationLabel(manager);
            }

            private void RefreshObjectives()
            {
                objectives.Clear();
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && map.HQ != null &&
                    MissionPosition.TryGetActiveObjectives(map.HQ, out List<Objective> active) && active != null)
                {
                    objectives.AddRange(active);
                }

                objectiveGrid.SetData(objectives.Count,
                    i => ObjectiveLabel(objectives[i]),
                    i => objectives[i] != null && objectives[i].CompletePercent >= 0.999f,
                    _ => { });
            }

            private static string ObjectiveLabel(Objective objective)
            {
                if (objective == null) return "OBJECTIVE LINK LOST";
                string prefix = objective.CompletePercent >= 0.999f ? "DONE  " : "LIVE  ";
                return prefix + AvTheme.Truncate(objective.ToUIString(oneLine: true)
                    .Replace("\n", " ").ToUpperInvariant(), 34);
            }

            private static string EscalationLabel(MissionManager manager)
            {
                if (manager == null) return "LINK LOST";
                if (manager.currentEscalation >= manager.strategicThreshold) return "STRATEGIC";
                if (manager.currentEscalation >= manager.tacticalThreshold) return "TACTICAL";
                return "CONVENTIONAL";
            }

            private static string MissionClock(MissionManager manager)
            {
                if (manager == null) return "—";
                int seconds = Mathf.Max(0, Mathf.FloorToInt(manager.MissionTime));
                return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
            }
        }

        private sealed class UnavailablePresenter : Presenter
        {
            public UnavailablePresenter(MFDScreen screen, VanillaMfdPanelId id) : base(screen, id) { }

            protected override void BuildContent()
            {
                RectTransform page = Shell.CreatePage("Unavailable");
                DrawSpine(page);
                AvStyled.SpineTick(page, 3f, -AvTokens.Space3);
                AvStyled.Label(page, new Rect(AvTokens.Space3, -AvTokens.Space3,
                                               Shell.Body.width - AvTokens.Space3, 18f),
                               "NATIVE ADAPTER UNAVAILABLE", "section-title");
                AvStyled.Label(page, new Rect(AvTokens.Space3, -AvTokens.Space6,
                                               Shell.Body.width - AvTokens.Space3, 56f),
                               "This game build changed the controller attached to this MFD. " +
                               "The source panel remains intact and will be restored when the map closes.",
                               "row-sub");
            }

            protected override void RefreshContent()
            {
                Shell.DataBar.State.text = "COMPATIBILITY HOLD";
                Shell.DataBar.SetChip(0, "NATIVE", false);
                Shell.DataBar.SetChip(1, "ADAPTER", false);
                Shell.DataBar.SetChip(2, "CHECK LOG", false);
            }

            protected override string AmbientStatus() => "UPDATE COMPATIBILITY REQUIRED";
        }
    }
}
