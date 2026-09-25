using System.Collections.Generic;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WingCommand
{
    /// <summary>LOADOUT (spec WMC rebuild §LOADOUT), the 0.9 v2 page with its critique fixed: a build card that says it is a saved
    /// preset (SUPPLY's FIT flies it), airframe tiles, a template bar (NEW · COPY · gap · DELETE, two-press), a HARDPOINTS table where a
    /// station is one row everywhere and only CLEAR empties one, a store popup beside its row, and a LIVERY pinned per airframe that
    /// the spawn now wears. Templates and liveries live in this machine's config, so a client edits its own.</summary>
    internal sealed partial class WmcLoadout : IWmcPage
    {
        private readonly Dictionary<string, AvButton> ids;
        private RectTransform page, content;
        private Rect body;
        private float width;
        private WmcScroll scroll;
        private bool client;

        // What is being edited.
        private AircraftDefinition airframe;
        private StationLayout layout;
        private LoadoutTemplateRecord current;
        private readonly Dictionary<string, string> editing = new Dictionary<string, string>();
        private readonly List<string> keys = new List<string>();
        private FactionHQ hq;
        private MissionFacts mission;
        private FitSummary summary;
        private StoreFacts[] setFacts = new StoreFacts[0];
        private readonly List<AircraftDefinition> airframes = new List<AircraftDefinition>();

        private int pageKey = int.MinValue, metricKey = int.MinValue, metricGeneration = -1;
        private string hint, alert;

        public WmcLoadout(Dictionary<string, AvButton> controls) => ids = controls;

        public string Hint => hint;

        public string Alert => alert;

        public void Build(RectTransform pageRoot, Rect shellBody)
        {
            page = pageRoot;
            body = WmcUi.Page(page, shellBody, shellBody.height);
            width = body.width;
            float view = BezelLayout.LoadoutView(shellBody.height);
            scroll = WmcScroll.Build(page, new Rect(body.x, body.y, width + 8f, view), "LoadoutScroll");
            content = scroll.Content;
            rowsPerPage = BezelLayout.HardpointRows(shellBody.height);
            BuildCard(content);
            BuildTiles(content, -(BezelLayout.CardH + BezelLayout.BlockGap));
            BuildTemplateBar(content, -(BezelLayout.CardH + BezelLayout.BlockGap + BezelLayout.SectionHead + BezelLayout.HeadGap
                + BezelLayout.LoadoutTiles + BezelLayout.BlockGap));
            BuildHardpoints(content, -BezelLayout.HardpointsTop);
            BuildLivery(body.y - view);
            popup = new AvKit.Popup(page, shellBody.width);
            scroll.SetContentHeight(BezelLayout.LoadoutContent(rowsPerPage, false));
        }

        /// <summary>LOADOUT came into view: the airframe list and the faction's liveries are read again.</summary>
        private void OnShown()
        {
            FillAirframes();
            if (airframe == null || !airframes.Contains(airframe))
                airframe = WingRequisition.Selected != null && airframes.Contains(WingRequisition.Selected) ? WingRequisition.Selected
                    : airframes.Count > 0 ? airframes[0] : null;
            liveryFor = null;
            pageKey = int.MinValue;
        }

        /// <summary>Airframes with readable hardpoints that are neither VTOL nor placeholders; offered or stored ones first.</summary>
        private void FillAirframes()
        {
            airframes.Clear();
            Encyclopedia enc = Encyclopedia.i;
            if (enc == null || enc.aircraft == null) return;
            int offered = 0;
            foreach (AircraftDefinition d in enc.aircraft)
            {
                if (d == null) continue;
                ShopAirframe a = WingRequisition.For(d, hq);
                if (a.Vtol || a.Placeholder || WingLoadoutCatalog.Layout(d) == null) continue;
                if (WingRequisition.Catalogue.Contains(d) || a.Held > 0) airframes.Insert(offered++, d);
                else airframes.Add(d);
            }
        }

        public void Metrics(WmcContext c, WmcMetricRow m)
        {
            client = c.Client;
            bool shown = m.Generation != metricGeneration;
            // Escalation and rank move mid-mission: the rules are read every refresh (a struct, nothing allocated).
            ReadFaction();
            if (shown) OnShown();
            Resolve();
            int key;
            unchecked
            {
                key = (current != null ? current.Id.GetHashCode() : 0) + WingLoadoutTemplates.Revision * 31 + (airframe != null ? airframe.GetHashCode() : 0)
                    + mission.Rank * 7 + (mission.TacticalOpen ? 11 : 0) + (mission.StrategicOpen ? 13 : 0);
            }
            if (key == metricKey && !shown) return;
            metricKey = key;
            metricGeneration = m.Generation;
            bool has = current != null;
            bool bad = summary.Blocked > 0 || summary.Refused > 0 || summary.Fitted == 0;
            m.Set(0, has ? LoadoutWords.Stations(summary) : WmcText.Unknown, LoadoutWords.StationsCaption(summary, has, summary.Refused),
                has && summary.Stations > 0 ? (float)summary.Fitted / summary.Stations : 0f, has && !bad ? AvTheme.Friendly : AvTheme.Warning);
            m.Set(1, has ? LoadoutWords.Mass(summary.Mass) : WmcText.Unknown, has ? LoadoutWords.MassCaption : "", 0f, AvTheme.Friendly);
            m.Set(2, has ? LoadoutWords.Role(summary) : WmcText.Unknown, has ? LoadoutWords.RoleCaption(summary) : "",
                has && summary.Fitted > 0 ? 1f : 0f, AvTheme.Friendly);
        }

        /// <summary>The player's faction and rank: the liveries and the mission rules the table reads.</summary>
        private void ReadFaction()
        {
            GameManager.GetLocalPlayer(out Player player);
            hq = player != null && player.HQ != null ? player.HQ
                : WingService.Instance != null && WingService.Instance.Leader != null ? WingService.Instance.Leader.NetworkHQ : null;
            mission = WingLoadoutCatalog.Mission(player != null ? player.PlayerRank : 0);
        }

        /// <summary>Which template is edited: the one last edited on this airframe, else SUPPLY's fit for it, else its first; then its
        /// keys, each set's store and the summary — only when the template, its revision or the airframe moved.</summary>
        private void Resolve()
        {
            layout = airframe != null ? WingLoadoutCatalog.Layout(airframe) : null;
            LoadoutTemplateRecord t = null;
            if (airframe != null)
            {
                string key = airframe.jsonKey;
                if (key != null && editing.TryGetValue(key, out string id)) t = Mine(WingLoadoutTemplates.ById(id));
                if (t == null) t = Mine(WingLoadoutTemplates.ById(WingRequisition.FitOf(airframe)));
                if (t == null)
                {
                    IReadOnlyList<LoadoutTemplateRecord> list = WingLoadoutTemplates.For(airframe);
                    t = list.Count > 0 ? list[0] : null;
                }
            }
            int rev = WingLoadoutTemplates.Revision;
            if (ReferenceEquals(t, current) && rev == resolvedRevision && ReferenceEquals(airframe, resolvedAirframe) && mission.Rank == resolvedRank
                && mission.TacticalOpen == resolvedTactical && mission.StrategicOpen == resolvedStrategic) return;
            current = t;
            resolvedRevision = rev;
            resolvedAirframe = airframe;
            resolvedRank = mission.Rank;
            resolvedTactical = mission.TacticalOpen;
            resolvedStrategic = mission.StrategicOpen;
            keys.Clear();
            if (current != null) keys.AddRange(current.MountKeys);
            if (layout != null) layout.Normalize(keys);
            int sets = layout != null ? layout.Sets : 0;
            if (setFacts.Length != sets) setFacts = new StoreFacts[sets];
            if (setOptions.Length != sets) setOptions = new WingLoadoutCatalog.StoreOption[sets];
            for (int s = 0; s < sets; s++)
            {
                WingLoadoutCatalog.StoreOption o = WingLoadoutCatalog.StoreOn(airframe, s, keys[s]);
                setOptions[s] = o;
                StoreFacts f = o.Facts;
                if (f.Known) f.Refused = !StoreRules.Flies(StoreRules.Check(WingLoadoutCatalog.FactsOf(o, layout.PylonsAt(s), hq), mission));
                setFacts[s] = f;
            }
            summary = layout != null ? LoadoutSummary.Of(layout, setFacts) : default;
            // What the launch's own check will empty (a store another fitted store blocks), once per change.
            if (fittedSets.Length != sets)
            {
                fittedSets = new bool[sets];
                clearedSets = new bool[sets];
            }
            for (int s = 0; s < sets; s++) fittedSets[s] = setFacts[s].Known && !setFacts[s].Refused;
            if (layout != null) layout.WillClear(fittedSets, clearedSets);
            pageKey = int.MinValue;
        }

        private LoadoutTemplateRecord Mine(LoadoutTemplateRecord t) => t != null && airframe != null && t.AirframeKey == airframe.jsonKey ? t : null;

        private WingLoadoutCatalog.StoreOption[] setOptions = new WingLoadoutCatalog.StoreOption[0];
        private bool[] fittedSets = new bool[0], clearedSets = new bool[0];

        /// <summary>The rank a nuclear store needs here (strategic or tactical).</summary>
        private int RankFor(in WingLoadoutCatalog.StoreOption o) => (int)(o.Strategic ? mission.StrategicMinRank : mission.TacticalMinRank);
        private int resolvedRevision = -1, resolvedRank = -1;
        private bool resolvedTactical, resolvedStrategic;
        private AircraftDefinition resolvedAirframe;

        public void Refresh(WmcContext c)
        {
            client = c.Client;
            Resolve();
            bool asking = current != null && deleteGate.IsArmed(current.Id, Time.unscaledTime);
            int key;
            unchecked
            {
                key = (current != null ? current.Id.GetHashCode() : 1) + WingLoadoutTemplates.Revision * 31 + (airframe != null ? airframe.GetHashCode() : 0)
                    + tilePage * 7 + hpPage * 131 + (client ? 3 : 0) + (asking ? 5 : 0) + (nameField.Dirty ? 17 : 0) + airframes.Count * 1009
                    + (WingRequisition.FitOf(airframe) != null ? WingRequisition.FitOf(airframe).GetHashCode() : 0) + mission.Rank * 13;
            }
            nameField.EditingId = current?.Id;
            if (key == pageKey) return;
            pageKey = key;
            RefreshCard(asking);
            RefreshTiles();
            RefreshTemplateBar(asking);
            RefreshHardpoints();
            RefreshLivery();
            string code = airframe != null ? SupplyWords.Code(airframe.code, airframe.unitName) : null;
            hint = LoadoutWords.Hint(client, airframe != null, current != null, code);
            alert = asking ? LoadoutWords.DeleteAsk(current.Name) : current != null ? LoadoutWords.EmptyHereAlert(summary.Refused + summary.Blocked) : null;
        }

        // ---------------------------------------------------------------- the build card

        private Image cardRail, cardIcon, chipRail;
        private TMP_Text titleText, chipText, chainText;
        private WmcNameField nameField;

        private void BuildCard(RectTransform r)
        {
            AvStyled.Box(r, new Rect(0f, 0f, width, BezelLayout.CardH), "card");
            cardRail = AvStyled.Rail(r, new Rect(0f, 0f, 3f, BezelLayout.CardH), "inert");
            cardIcon = AvKit.Panel(r, new Rect(10f, -10f, 40f, 28f), Color.white);
            cardIcon.preserveAspect = true;
            cardIcon.raycastTarget = false;
            cardIcon.enabled = false;
            titleText = WmcKit.Text(r, new Rect(58f, -6f, 304f, 18f), "row-name");
            var chip = new Rect(368f, -6f, 84f, 16f);
            AvStyled.Box(r, chip, "chip");
            chipRail = AvStyled.Rail(r, new Rect(chip.x, chip.y, 3f, chip.height), "inert");
            chipText = WmcKit.Text(r, new Rect(chip.x + 7f, chip.y, chip.width - 9f, chip.height), "row-sub");
            chainText = WmcKit.Text(r, new Rect(58f, -26f, width - 64f, 14f), "row-sub");
            AvStyled.Label(r, new Rect(10f, -50f, 40f, 26f), "NAME", "metric-key");
            nameField = WmcNameField.Build(r, new Rect(54f, -50f, 244f, 26f), TemplateNames.MaxChars, CommitName,
                "The template's name as SUPPLY's FIT lists it: Enter saves it (16 characters at most).");
            WmcKit.Text(r, new Rect(306f, -50f, width - 312f, 26f), "row-sub").text = "PRESET · SUPPLY FIT";
        }

        private void RefreshCard(bool asking)
        {
            string code = airframe != null ? SupplyWords.Code(airframe.code, airframe.unitName) : WmcText.Unknown;
            bool supplyFit = current != null && WingRequisition.FitOf(airframe) == current.Id;
            WmcKit.Set(titleText, airframe != null ? LoadoutWords.Title(code, current?.Name) : "NO AIRFRAME");
            string state;
            WmcKit.Set(chipText, LoadoutWords.Chip(current != null, nameField.Dirty, supplyFit, out state));
            WmcUi.SetRail(chipRail, state);
            WmcUi.SetRail(cardRail, asking ? "warn" : state);
            WmcKit.Set(chainText, airframe != null ? LoadoutWords.Chain(code, current?.Name, layout != null ? layout.Stations : 0)
                : "Pick an airframe below");
            cardIcon.sprite = airframe != null ? IconFactory.Aircraft(airframe) : null;
            cardIcon.enabled = cardIcon.sprite != null;
            nameField.SetText(current?.Name ?? "");
            nameField.SetInteractable(current != null);
        }

        private void CommitName(string id, string typed)
        {
            LoadoutTemplateRecord t = WingLoadoutTemplates.ById(id);
            if (t != null && WingLoadoutTemplates.Rename(t, typed)) WingToast.Show("Template renamed " + t.Name);
            WmcPanel.Instance?.Refresh();
        }

        // ---------------------------------------------------------------- the pinned LIVERY row

        private TMP_Text liveryKey, liveryLabel;
        private AvButton[] liveryStepper;
        private readonly List<WingLoadoutTemplates.LiveryOption> liveries = new List<WingLoadoutTemplates.LiveryOption>();
        private readonly List<string> liveryTokens = new List<string>();
        private AircraftDefinition liveryFor;
        private Faction liveryFaction;

        private void BuildLivery(float top)
        {
            float y = top - (BezelLayout.LiveryPin - BezelLayout.LiveryRow);
            liveryKey = WmcKit.Text(page, new Rect(body.x, y, 112f, BezelLayout.LiveryRow), "metric-key");
            liveryStepper = AvKit.Stepper(page, body.x + 116f, y, width - 116f, out liveryLabel, () => StepLivery(-1), () => StepLivery(1),
                "The livery this airframe's wingmen wear (STANDARD: the faction's own).");
            ids["lo.livery.prev"] = liveryStepper[0];
            ids["lo.livery.next"] = liveryStepper[1];
        }

        private void RefreshLivery()
        {
            Faction faction = hq != null ? hq.faction : null;
            if (!ReferenceEquals(liveryFor, airframe) || !ReferenceEquals(liveryFaction, faction))
            {
                liveryFor = airframe;
                liveryFaction = faction;
                WingLoadoutTemplates.Liveries(airframe, faction, liveries);
                liveryTokens.Clear();
                foreach (WingLoadoutTemplates.LiveryOption o in liveries) liveryTokens.Add(o.Token);
            }
            string code = airframe != null ? SupplyWords.Code(airframe.code, airframe.unitName) : WmcText.Unknown;
            WmcKit.Set(liveryKey, LoadoutWords.LiveryKey(code));
            int i = LiveryChoice.IndexOf(liveryTokens, WingLoadoutTemplates.LiveryTokenOf(airframe));
            WmcKit.Set(liveryLabel, liveries.Count > 0 ? liveries[i].Label : LoadoutWords.Standard);
            bool can = airframe != null && liveries.Count > 1;
            foreach (AvButton b in liveryStepper)
            {
                b.SetEnabled(can);
                b.WithTooltip(can ? "The livery this airframe's wingmen wear (STANDARD: the faction's own)."
                    : airframe == null ? "Pick an airframe first." : "This faction offers no other livery for it.");
            }
        }

        private void StepLivery(int dir)
        {
            if (airframe == null || liveries.Count <= 1) return;
            int i = LiveryChoice.IndexOf(liveryTokens, WingLoadoutTemplates.LiveryTokenOf(airframe));
            WingLoadoutTemplates.SetLivery(airframe, liveryTokens[LiveryChoice.Step(i, liveries.Count, dir)]);
            WmcPanel.Instance?.Refresh();
        }
    }
}
