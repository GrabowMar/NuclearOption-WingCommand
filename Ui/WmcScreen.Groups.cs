using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace WingCommand
{
    internal static partial class WmcScreen
    {
        private const float FlightGroupsHeight = Space5 + (TacticalButtonHeight + Gap) * 2f;
        private static RectTransform flightGroupsRoot, groupBar, groupEditor;
        private static readonly WingButton[] groupButtons = new WingButton[FlightGroups<WingMember>.Count];
        private static WingButton groupCreateButton, groupEditButton, groupDeleteButton, groupSaveButton;
        private static TMP_InputField groupNameField;
        private static TMP_Text groupsHint;
        private static int editingGroup = -1;

        private static void BuildFlightGroups(RectTransform parent)
        {
            flightGroupsRoot = PageRoot(parent, "OptionalFlightGroups");
            float y = Heading(flightGroupsRoot, 0f, "GROUPS - OPTIONAL / SELECTED AIRCRAFT");
            groupBar = PageRoot(flightGroupsRoot, "Groups");
            groupEditor = PageRoot(flightGroupsRoot, "GroupEditor");
            groupCreateButton = TacticalButton(groupBar, "CREATE GROUP", Pad, y, TacticalCellWidth, CreateFlightGroup)
                .WithTooltip("Create a named group from the selected aircraft. Groups are optional and last for this mission.");
            for (int i = 0; i < groupButtons.Length; i++)
            {
                int index = i;
                groupButtons[i] = TacticalButton(groupBar, "", TacticalColumn(i + 1), y, TacticalCellWidth, () => {
                    editingGroup = index;
                    WingCommandManager.Instance?.RecallFlightGroup(index);
                    nextRefresh = 0f;
                }, UiButtonStyle.Toggle)
                    .WithTooltip("Recall this group. With Flight expanded: Ctrl+1/2/3 recalls existing groups; Ctrl+Shift+1/2/3 replaces their selection.");
            }
            float secondRow = y - TacticalButtonHeight - Gap;
            groupEditButton = TacticalButton(groupBar, "EDIT GROUP", Pad, secondRow, TacticalCellWidth, EditFlightGroup)
                .WithTooltip("Edit the last recalled group. SAVE updates its name and members to your current selection.");
            groupDeleteButton = TacticalButton(groupBar, "DELETE GROUP", TacticalColumn(1), secondRow,
                TacticalCellWidth, () => {
                    if (editingGroup < 0) return;
                    WingCommandManager.Instance?.Selection.Groups.Clear(editingGroup);
                    editingGroup = -1;
                    nextRefresh = 0f;
                }).WithTooltip("Delete the saved group without releasing any aircraft.");
            groupsHint = Label(groupBar, "Select aircraft, then CREATE GROUP.",
                new Rect(Pad, secondRow, PanelWidth - Pad * 2f, LineHeight),
                Dim(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);

            if (WingKeyboardGuard.Available)
                groupNameField = WingUi.InputField(groupEditor,
                    new Rect(Pad, y, TacticalCellWidth * 2f + Gap, TacticalButtonHeight),
                    12, _ => nextRefresh = 0f, "Group name, up to 12 characters", "GROUP NAME");
            groupSaveButton = TacticalButton(groupEditor, "SAVE", TacticalColumn(2), y, TacticalCellWidth, () => {
                var manager = WingCommandManager.Instance;
                if (manager == null || editingGroup < 0 || string.IsNullOrWhiteSpace(groupNameField?.text)) return;
                manager.SaveFlightGroup(editingGroup, groupNameField.text);
                CloseFlightGroupEditor();
            });
            TacticalButton(groupEditor, "BACK", TacticalColumn(3), y, TacticalCellWidth, CloseFlightGroupEditor);
            Label(groupEditor, "SAVE uses the current command selection.",
                new Rect(Pad, secondRow, PanelWidth - Pad * 2f, LineHeight),
                Dim(), FontSmall, FontStyles.Normal, TextAlignmentOptions.Left);
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
            groupsHint?.gameObject.SetActive(!editable);
            if (groupsHint != null)
                groupsHint.text = created == 0 ? "Select aircraft, then CREATE GROUP." : "Recall a group to edit or delete it.";
            groupCreateButton?.SetEnabled(created < groupButtons.Length && scope.Count > 0 && groupNameField != null);
            groupEditButton?.SetEnabled(groupNameField != null);
            groupSaveButton?.SetEnabled(scope.Count > 0 && !string.IsNullOrWhiteSpace(groupNameField?.text));
        }

        private static void ResetFlightGroups()
        {
            flightGroupsRoot = groupBar = groupEditor = null;
            groupCreateButton = groupEditButton = groupDeleteButton = groupSaveButton = null;
            groupNameField = null;
            groupsHint = null;
            editingGroup = -1;
            Array.Clear(groupButtons, 0, groupButtons.Length);
        }
    }
}
