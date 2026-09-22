using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    internal static partial class WmcScreen
    {
        private const float FlightGroupsHeight = 44f;
        private static RectTransform flightGroupsRoot, groupBar, groupEditor;
        private static readonly WingButton[] groupButtons = new WingButton[FlightGroups<WingMember>.Count];
        private static WingButton groupCreateButton, groupEditButton, groupDeleteButton, groupSaveButton;
        private static TMP_InputField groupNameField;
        private static int editingGroup = -1;
        private static int groupDeleteSlot = -1;
        private static float groupDeleteUntil;

        private static void ConfirmDeleteFlightGroup()
        {
            if (editingGroup < 0) return;
            if (groupDeleteSlot != editingGroup || Time.unscaledTime > groupDeleteUntil)
            {
                groupDeleteSlot = editingGroup;
                groupDeleteUntil = Time.unscaledTime + 3f;
                groupDeleteButton?.SetText("DELETE?");
                return;
            }

            WingCommandManager.Instance?.Selection.Groups.Clear(editingGroup);
            editingGroup = -1;
            groupDeleteSlot = -1;
            groupDeleteButton?.SetText("DELETE");
            nextRefresh = 0f;
        }

        private static void BuildFlightGroups(RectTransform parent)
        {
            flightGroupsRoot = PageRoot(parent, "OptionalFlightGroups");
            groupBar = PageRoot(flightGroupsRoot, "Groups");
            groupEditor = PageRoot(flightGroupsRoot, "GroupEditor");

            const int slots = 3;
            const float rowY = -7f;
            const float recallW = 80f;
            const float actionW = 60f;
            for (int i = 0; i < groupButtons.Length; i++)
            {
                int index = i;
                groupButtons[i] = WingUi.Button(groupBar, "G" + (i + 1),
                    new Rect(Pad + i * (recallW + TacticalGap), rowY, recallW, TacticalButtonHeight),
                    FontMicro, UiButtonStyle.Toggle, () =>
                    {
                        editingGroup = index;
                        WingCommandManager.Instance?.RecallFlightGroup(index);
                        nextRefresh = 0f;
                    })
                    .WithTooltip("Recall this group. With Flight expanded: Ctrl+1/2/3 recalls existing groups; Ctrl+Shift+1/2/3 replaces their selection.");
            }

            float actionsX = Pad + slots * (recallW + TacticalGap);
            groupCreateButton = WingUi.Button(groupBar, "CREATE",
                new Rect(actionsX, rowY, actionW, TacticalButtonHeight), FontMicro, UiButtonStyle.Default,
                CreateFlightGroup)
                .WithTooltip("Create a named group from the selected aircraft. Groups are optional and last for this mission.");
            groupEditButton = WingUi.Button(groupBar, "EDIT",
                new Rect(actionsX + actionW + TacticalGap, rowY, actionW, TacticalButtonHeight), FontMicro,
                UiButtonStyle.Default, EditFlightGroup)
                .WithTooltip("Edit the last recalled group. SAVE updates its name and members to your current selection.");
            groupDeleteButton = WingUi.Button(groupBar, "DELETE",
                new Rect(actionsX + (actionW + TacticalGap) * 2f, rowY, actionW, TacticalButtonHeight), FontMicro,
                UiButtonStyle.Default, ConfirmDeleteFlightGroup)
                .WithTooltip("Delete the saved group without releasing any aircraft. Press twice.");

            float fieldW = recallW * 3f + TacticalGap * 2f;
            if (WingKeyboardGuard.Available)
                groupNameField = WingUi.InputField(groupEditor,
                    new Rect(Pad, rowY, fieldW, TacticalButtonHeight),
                    12, _ => nextRefresh = 0f, "Group name, up to 12 characters", "GROUP NAME");
            groupSaveButton = WingUi.Button(groupEditor, "SAVE",
                new Rect(actionsX, rowY, actionW, TacticalButtonHeight), FontMicro, UiButtonStyle.Primary, () =>
                {
                    var manager = WingCommandManager.Instance;
                    if (manager == null || editingGroup < 0 || string.IsNullOrWhiteSpace(groupNameField?.text)) return;
                    manager.SaveFlightGroup(editingGroup, groupNameField.text);
                    CloseFlightGroupEditor();
                })
                .WithTooltip("Save the current command selection under this group name.");
            WingUi.Button(groupEditor, "BACK",
                new Rect(actionsX + actionW + TacticalGap, rowY, actionW, TacticalButtonHeight), FontMicro,
                UiButtonStyle.Quiet, CloseFlightGroupEditor)
                .WithTooltip("Close the group editor without saving.");

            groupEditor.gameObject.SetActive(false);
            flightGroupsRoot.gameObject.SetActive(false);
        }

        private static void CreateFlightGroup()
        {
            var groups = WingCommandManager.Instance?.Selection.Groups;
            if (groups == null || groupNameField == null) return;
            for (int i = 0; i < FlightGroups<WingMember>.Count; i++)
            {
                if (groups.Exists(i)) continue;
                editingGroup = i;
                EditFlightGroup();
                return;
            }
        }

        private static void EditFlightGroup()
        {
            if (editingGroup < 0 || groupNameField == null) return;
            groupNameField.SetTextWithoutNotify(WingCommandManager.Instance?.Selection.Groups.Name(editingGroup) ?? "");
            groupBar.gameObject.SetActive(false);
            groupEditor.gameObject.SetActive(true);
            nextRefresh = 0f;
        }

        private static void CloseFlightGroupEditor()
        {
            groupNameField?.DeactivateInputField();
            groupEditor?.gameObject.SetActive(false);
            groupBar?.gameObject.SetActive(true);
            nextRefresh = 0f;
        }

        private static void RefreshFlightGroups(WingRegistry wing, List<WingMember> scope)
        {
            if (!rosterExpanded) return;
            var selection = WingCommandManager.Instance.Selection;
            int created = 0;
            for (int i = 0; i < groupButtons.Length; i++)
            {
                bool exists = selection.Groups.Exists(i);
                groupButtons[i]?.gameObject.SetActive(exists);
                if (!exists) continue;
                created++;
                var members = selection.GroupMembers(i, wing);
                bool matches = members.Count > 0 && members.Count == scope.Count;
                foreach (WingMember member in members) matches &= selection.Contains(member);
                groupButtons[i]?.SetText(selection.Groups.Name(i) + " " + members.Count);
                groupButtons[i]?.SetLatched(matches);
            }
            bool editable = editingGroup >= 0 && selection.Groups.Exists(editingGroup);
            groupEditButton?.gameObject.SetActive(editable);
            groupDeleteButton?.gameObject.SetActive(editable);
            groupCreateButton?.SetEnabled(created < groupButtons.Length && scope.Count > 0 && groupNameField != null);
            groupEditButton?.SetEnabled(groupNameField != null);
            groupSaveButton?.SetEnabled(scope.Count > 0 && !string.IsNullOrWhiteSpace(groupNameField?.text));
        }

        private static void ResetFlightGroups()
        {
            flightGroupsRoot = groupBar = groupEditor = null;
            groupCreateButton = groupEditButton = groupDeleteButton = groupSaveButton = null;
            groupNameField = null;
            editingGroup = -1;
            Array.Clear(groupButtons, 0, groupButtons.Length);
        }
    }
}
