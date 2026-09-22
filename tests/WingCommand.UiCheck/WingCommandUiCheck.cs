#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using NOAvionics.Ui;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Renders the shipped panel builders with synthetic readouts, without a game session.</summary>
public static class WingCommandUiCheck
{
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Mod = typeof(AvScreen).Assembly;
    private static readonly Type Screen = Mod.GetType("WingCommand.WmcScreen", true);
    private static readonly List<string> Errors = new List<string>();
    private static readonly List<string> EditorWarnings = new List<string>();
    private static int assertions, captures;

    public static void Run()
    {
        try
        {
            if (Shader.Find("TextMeshPro/Distance Field") == null)
            {
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
                AssetDatabase.importPackageCompleted += _ => EditorApplication.delayCall += Run;
                AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath,
                    "Package Resources/TMP Essential Resources.unitypackage"), false);
                return;
            }
            Initialize();
            Application.logMessageReceived += TrackError;
            foreach (float height in new[] { 420f, 596f, 896f }) Render(height);
            Check(Errors.Count == 0, "Unity emitted errors: " + string.Join("\n", Errors));
            File.WriteAllText("result.txt", "PASS: " + captures + " production-DLL captures; " + assertions +
                " layout assertions. " + EditorWarnings.Count + " known edit-mode portrait disposal warnings (see editor-warnings.txt). Synthetic readouts and fallback Consolas font. No live gameplay, flight keyboard input, or multiplayer claim.");
            File.WriteAllLines("editor-warnings.txt", EditorWarnings);
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void Initialize()
    {
        MethodInfo setPaths = typeof(BepInEx.Paths).GetMethod("SetExecutablePath", All);
        ParameterInfo[] parameters = setPaths.GetParameters();
        var args = new object[parameters.Length];
        args[0] = Path.GetFullPath("WingCommandPreview.exe");
        for (int i = 1; i < args.Length; i++) args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
        setPaths.Invoke(null, args);
        Type plugin = Mod.GetType("WingCommand.Plugin", true);
        plugin.GetProperty("Logger", All | BindingFlags.DeclaredOnly).SetValue(null, new BepInEx.Logging.ManualLogSource("WingCommandUiCheck"));
        object config = Activator.CreateInstance(Mod.GetType("WingCommand.WingConfig", true), new ConfigFile(Path.GetFullPath("fixture.cfg"), false));
        plugin.GetProperty("Settings", All).SetValue(null, config);
        AvStyleHost.Configure(Directory.GetCurrentDirectory(), Debug.Log, Debug.LogWarning);
        AvFont.Font = TMP_FontAsset.CreateFontAsset(new Font("C:/Windows/Fonts/consola.ttf"));
        new GameObject("Events", typeof(EventSystem));
    }

    private static void Render(float height)
    {
        Call("Reset");
        var canvasObject = new GameObject("WingCommandPreview", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform root = (RectTransform)canvas.transform;
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.localPosition = Vector3.zero;
        root.localScale = Vector3.one;
        root.sizeDelta = new Vector2(480f, height);
        var contentObject = new GameObject("Content", typeof(RectTransform), typeof(Image));
        RectTransform content = (RectTransform)contentObject.transform;
        content.SetParent(root, false);
        AvKit.Stretch(content);
        Call("BuildContent", content, height);
        Seed();
        for (int page = 0; page < 4; page++)
        {
            SelectPage(page);
            if (page == 0)
            {
                string[] names = { "orders", "formation", "route" };
                for (int deck = 0; deck < names.Length; deck++)
                {
                    Call("SetTacticalDeck", deck);
                    CapturePage(root, height, "tactical-" + names[deck]);
                }
                Call("ToggleRosterExpanded");
                CapturePage(root, height, "tactical-expanded");
                Call("ToggleRosterExpanded");
            }
            else CapturePage(root, height, new[] { "tactical", "supply", "loadout", "wing" }[page]);
        }
        ((RectTransform)Get("squadronViewRoot")).gameObject.SetActive(false);
        ((RectTransform)Get("customStudioRoot")).gameObject.SetActive(true);
        Call("ResetPageScroll", Enum.ToObject(Screen.GetNestedType("Page", All), 3));
        SeedStudio();
        CapturePage(root, height, "studio");
        CheckPortrait();
        if (height == 420f)
        {
            CheckPopup(root, height, 1, "shopTemplatePopup");
            CheckPopup(root, height, 2, "loadoutPopup");
        }
        CaptureEmptyPages(root, height);
        Object.DestroyImmediate(canvasObject);
    }

    private static void Seed()
    {
        var bar = (AvStyled.DataBar)Get("dataBar");
        bar.State.text = "WING COMMAND · OFFLINE LAYOUT CHECK";
        bar.SetChip(0, "4 AIRCRAFT", "info"); bar.SetChip(1, "FIXTURE", "inert"); bar.SetChip(2, "NO ORDERS", "inert");
        ((AvStyled.Metric)Get("fundsMetric")).Set("125.4m", "4 / 8 AIRCRAFT", .5f, AvTheme.RailReady);
        ((AvStyled.Metric)Get("fuelMetric")).Set("78", "BINGO AT 20%", .78f, AvTheme.RailReady);
        foreach (TMP_Text status in (IEnumerable)Get("statusLabels"))
            status.text = "Offline fixture · production layout · no game state or orders.";
        Text("summaryLabel", "COMMAND: PAIR 1-2 · 2 AIRCRAFT"); Text("rosterPageLabel", "1 / 1");
        Text("bentoScopeLabel", "PAIR 1-2 · 2 SELECTED"); Text("bentoTargetTitle", "2 CONTACTS TRACKED");
        Text("bentoTargetDetail", "FIGHTER · 14.2 km"); Text("bentoTargetKinematics", "CLOSING · 340 m/s");
        Text("bentoThreatLabel", "NO MISSILE WARNING"); Text("bentoStoresTitle", "COMBAT STORES");
        Text("bentoStoresLine1", "A-A  8 MISSILES"); Text("bentoStoresLine2", "A-G  4 STORES");
        Text("bentoStoresLine3", "GUN  640 ROUNDS"); Text("bentoTelemAlt", "ALT  3,400 m");
        Text("bentoTelemSpd", "SPD  240 m/s"); Text("bentoTelemSlot", "SLOT  1-2");
        Text("bentoTelemFuel", "FUEL  78%"); Text("bentoTelemHull", "HULL  100%");
        Text("doctrineTitleLabel", "ECHELON RIGHT"); Text("doctrineProfileLabel", "120 m SPACING\n2 SELECTED AIRCRAFT");
        Text("doctrineRulesLabel", "ESCORT"); Text("doctrineWeaponsLabel", "WEAPONS: AUTO");
        Text("formationSpacingLabel", "120 m"); Text("routeLabel", "ALT 3,000 m · SPEED 240 m/s");
        Text("nodePageLabel", "1-4 OF 4 NODES");
        int index = 0;
        foreach (TMP_Text label in (IEnumerable)Get("nodeLabels")) label.text = (++index) + "  TRANSIT · 3,000 m · 240 m/s";
        Type rowType = Screen.GetNestedType("RosterRow", All);
        var roster = (IList)Get("rosterRows");
        for (int i = 0; i < 4; i++)
        {
            object row = Activator.CreateInstance(rowType, All, null, new object[] { Get("rosterArea"), i }, null);
            roster.Add(row);
            Text(row, "slot", (i + 1).ToString("00")); Text(row, "plane", i % 2 == 0 ? "FS-12" : "FS-20");
            Text(row, "name", new[] { "DAYMAN", "VIXEN", "CINDER", "MICA" }[i]);
            Text(row, "order", i == 0 ? "FORMING · LEAD" : "FORMING");
            ((GameObject)Get(row, "go")).SetActive(true);
        }
        Text("supplyFundsLabel", "125,400,000 CR"); Text("supplySquadronLabel", "4 / 8 AIRCRAFT");
        Text("supplyPilotCountLabel", "1 / 4"); Text("supplyPilotNameLabel", "DAYMAN · M. FONTAINE");
        Text("supplyPilotRankLabel", "VETERAN · 1,250 XP"); Text("supplyPilotStatusLabel", "AVAILABLE FOR ASSIGNMENT");
        Text("supplyDispatchAirframeLabel", "FS-12 REVENANT"); Text("supplyDispatchStateLabel", "SELECT A DEPARTURE FIELD");
        Text("reserveLabel", "2 / 3"); Text("reserveHintLabel", "Choose an owned airframe before dispatch.");
        Text("offerDetailLabel", "MULTIROLE FIGHTER · 24,000,000 CR");
        Text("offerLoadoutLabel", "AIR SUPERIORITY · 6 STORES"); Text("assignmentCostLabel", "Select friendly AI on the map to assign it.");
        SeedTiles("shopTiles", "priceStock", "24.0m CR · 4x");
        SeedTiles("airframeTiles", "name", "Multirole fighter");
        ((TMP_Text)Get("shopEmptyLabel")).gameObject.SetActive(false);
        ((TMP_Text)Get("airframeEmptyLabel")).gameObject.SetActive(false);
        ((AvButton)Get("shopTemplateButton")).SetText("AIR SUPERIORITY");
        ((AvButton)Get("templateSelectButton")).SetText("AIR SUPERIORITY");
        ((AvButton)Get("fullFuelButton")).SetText("FULL FUEL");
        ((AvButton)Get("exceedLimitButton")).SetText("CAPACITY LIMIT");
        Text("templateLabel", "AIR SUPERIORITY"); Text("liveryLabel", "BOSCALI STANDARD");
        Text("templateSummaryLabel", "6 STORES · 1,840 kg");
        Text("loadoutStatusLabel", "Saved template. Select a hardpoint to change its store.");
        ((RectTransform)Get("pylonEmptyCard")).gameObject.SetActive(false);
        index = 0;
        foreach (object row in (IEnumerable)Get("pylonRows"))
        {
            ((GameObject)Get(row, "go")).SetActive(true);
            Text(row, "name", "HARDPOINT " + (++index)); Text(row, "store", index == 1 ? "IR MISSILE · 2 LINKED" : "RADAR-GUIDED MISSILE");
        }
        Text("pylonPageLabel", "1-5 OF 5 HARDPOINTS");
        SeedWing();
    }

    private static void SeedTiles(string field, string secondary, string detail)
    {
        int i = 0;
        foreach (object row in (IEnumerable)Get(field))
        {
            ((GameObject)Get(row, "go")).SetActive(true);
            Text(row, "code", new[] { "FS-12", "FS-20", "KR-67", "CI-22", "SAH-46", "EW-25" }[i++ % 6]);
            Text(row, secondary, detail);
        }
    }

    private static void SeedWing()
    {
        Type pilotType = Mod.GetType("WingCommand.WingPilot", true);
        object pilot = Activator.CreateInstance(pilotType);
        pilotType.GetField("Name", All).SetValue(pilot, "M. Fontaine");
        pilotType.GetField("Callsign", All).SetValue(pilot, "DAYMAN");
        pilotType.GetField("Xp", All).SetValue(pilot, 1250);
        Call("RenderPilotVisual", pilot);
        Call("SetWingDetail", "DAYMAN · M. FONTAINE", "VETERAN   XP 1,250 / 2,000", "8 KILLS / 14 SORTIES",
            "RADIO PROFILE · PROFESSIONAL", .6f,
            "Flew medical supply routes before joining the reserves. Precise on the radio and calm under pressure. Every sortie is a chance to bring the whole flight home.",
            "FS-12 REVENANT", "SLOT 1 · FLIGHT LEAD");
        Call("SyncPilotRows", Get("pilotRows"), Get("pilotRosterArea"));
        int i = 0;
        foreach (object row in (IEnumerable)Get("pilotRows"))
        {
            object entry = Activator.CreateInstance(pilotType);
            pilotType.GetField("Name", All).SetValue(entry, "Pilot " + (i + 1));
            pilotType.GetField("Callsign", All).SetValue(entry, new[] { "DAYMAN", "VIXEN", "CINDER", "MICA" }[i]);
            pilotType.GetField("Xp", All).SetValue(entry, 500 * i);
            row.GetType().GetMethod("Bind", All).Invoke(row, new object[] { entry, i == 0, (Action)(() => { }) });
            i++;
        }
        i = 0;
        Type perkType = Mod.GetType("WingCommand.PilotPerk", true);
        foreach (object card in (IEnumerable)Get("pilotSkillCards"))
            card.GetType().GetMethod("Bind", All).Invoke(card, new object[] { Enum.ToObject(perkType, i++) });
    }

    private static void SeedStudio()
    {
        Text("bodyValueLabel", "MALE"); Text("faceValueLabel", "2 / 6"); Text("hairValueLabel", "3");
        Text("uniformValueLabel", "BOSCALI"); Text("backdropValueLabel", "2 / 4");
        Text("personaValueLabel", "PROFESSIONAL"); Text("rankValueLabel", "VETERAN (1,250 XP)");
        Text("studioStatusLabel", "NOT IN SQUADRON · Save & Recruit adds this pilot");
        ((TMP_InputField)Get("studioCallsignField")).SetTextWithoutNotify("NIGHTFALL");
        ((TMP_InputField)Get("studioNameField")).SetTextWithoutNotify("Alexandra Fontaine");
        ((TMP_InputField)Get("studioBioField")).SetTextWithoutNotify("Flew medical supplies through contested airspace. Precise on the radio and calm under pressure.");
        Type recordType = Mod.GetType("WingCommand.CustomPilotRecord", true);
        int i = 0;
        foreach (object row in (IEnumerable)Get("studioRows"))
        {
            object record = Activator.CreateInstance(recordType);
            recordType.GetProperty("Callsign", All).SetValue(record, i == 0 ? "NIGHTFALL" : "PILOT " + (i + 1));
            recordType.GetProperty("Name", All).SetValue(record, "Alexandra Fontaine");
            recordType.GetProperty("Xp", All).SetValue(record, 1250);
            row.GetType().GetMethod("Bind", All).Invoke(row, new object[] { record, i == 0, (Action)(() => { }) });
            i++;
        }
        foreach (string field in new[] { "studioCallsignField", "studioNameField", "studioBioField" })
        {
            var input = (TMP_InputField)Get(field);
            Check(((RectTransform)input.transform).rect.height >= 30f, field + " is too short.");
            Check(input.textComponent.fontSize >= 10f, field + " text is too small.");
        }
    }

    private static void CheckPortrait()
    {
        Image portrait = (Image)Get("pilotPortrait");
        Sprite original = portrait.sprite;
        Call("UpdatePortraitAspectFill", portrait, null, 92f, 138f);
        Check(!portrait.enabled, "Missing portrait must not render a white rectangle.");
        Call("UpdatePortraitAspectFill", portrait, original, 92f, 138f);
        Check(original == null || portrait.enabled, "Valid portrait must re-enable its image.");
    }

    private static void CheckPopup(RectTransform root, float height, int page, string field)
    {
        SelectPage(page);
        var scrolls = (ScrollRect[])Get("pageScrolls");
        ScrollRect scroll = scrolls != null && page < scrolls.Length ? scrolls[page] : null;
        if (scroll != null) scroll.verticalNormalizedPosition = 0f;
        Layout(root);
        var popup = (AvKit.Popup)Get(field);
        var entries = new List<AvKit.PopupEntry>();
        for (int i = 0; i < 10; i++) entries.Add(new AvKit.PopupEntry("FIXTURE STORE " + i, i + " / 10", i == 0));
        int picked = -1;
        float y = page == 1 ? Convert.ToSingle(Get("shopTemplateRowY")) : Convert.ToSingle(Get("pylonAreaY")) - 208f;
        popup.Show(new Rect(14f, y - 30f, 444f, 0f), entries, index => picked = index);
        Layout(root);
        RectTransform list = (RectTransform)Get(popup, "listRect");
        Rect menu = Bounds(root, list);
        Rect viewport = scroll != null ? Bounds(root, scroll.viewport) : Bounds(root, root);
        Check(menu.yMin >= viewport.yMin - .5f && menu.yMax <= viewport.yMax + .5f,
            $"{field} must fit the compact body when opened at its bottom: menu={menu} viewport={viewport} y={y}");
        if (scroll != null)
            Check(!list.IsChildOf(scroll.viewport), field + " must escape the scrolling mask.");
        var rows = (IList)Get(popup, "rows");
        object more = null;
        int visibleEntries = 0;
        foreach (object row in rows)
        {
            if (!((GameObject)Get(row, "go")).activeSelf) continue;
            if (((TMP_Text)Get(row, "label")).text == "MORE...") more = row;
            else visibleEntries++;
        }
        Check(more != null, field + " must expose pagination in a compact body.");
        Capture(root, height, field + "-420-bottom-open.png");
        ((AvButton)Get(more, "hit")).OnPointerClick(new PointerEventData(EventSystem.current));
        ((AvButton)Get(rows[0], "hit")).OnPointerClick(new PointerEventData(EventSystem.current));
        Check(picked == visibleEntries, field + " paged callback must preserve the original entry index.");
        popup.Close();
    }

    private static void CaptureEmptyPages(RectTransform root, float height)
    {
        foreach (string field in new[] { "rosterRows", "shopTiles", "airframeTiles", "pilotRows", "pylonRows" })
            foreach (object row in (IEnumerable)Get(field)) ((GameObject)Get(row, "go")).SetActive(false);
        foreach (string field in new[] { "rosterEmptyLabel", "shopEmptyLabel", "airframeEmptyLabel", "pilotEmptyLabel", "pylonEmptyLabel" })
            if (Get(field) is TMP_Text label) label.gameObject.SetActive(true);
        ((RectTransform)Get("pylonEmptyCard")).gameObject.SetActive(true);
        Text("summaryLabel", "COMMAND: NO AIRCRAFT");
        Text("supplyPilotNameLabel", "NO PILOT SELECTED");
        Text("supplyPilotStatusLabel", "Recruit a pilot on WING before assignment.");
        Text("loadoutStatusLabel", "Select an airframe before creating a template.");
        Text("templateSummaryLabel", "NO TEMPLATE SELECTED");
        ((RectTransform)Get("customStudioRoot")).gameObject.SetActive(false);
        ((RectTransform)Get("squadronViewRoot")).gameObject.SetActive(true);
        Call("ResetPageScroll", Enum.ToObject(Screen.GetNestedType("Page", All), 3));
        Call("SetWingDetail", "NO PILOT SELECTED", "", "", "", 0f,
            "Recruit a pilot using RECRUIT RANDOM or CUSTOM PILOTS above, or requisition an aircraft on SUPPLY.",
            "NO AIRFRAME", "Select a pilot above to inspect active flight assignment.");
        Call("UpdatePortraitAspectFill", Get("pilotPortrait"), null, 92f, 138f);
        foreach (object card in (IEnumerable)Get("pilotSkillCards")) card.GetType().GetMethod("Hide", All).Invoke(card, null);
        for (int page = 0; page < 4; page++)
        {
            SelectPage(page);
            CapturePage(root, height, new[] { "tactical-empty", "supply-empty", "loadout-empty", "wing-empty" }[page]);
        }
    }

    private static void SelectPage(int page)
    {
        Type pageType = Screen.GetNestedType("Page", All);
        Call("SetPage", Enum.ToObject(pageType, page));
    }

    private static void CapturePage(RectTransform root, float height, string name)
    {
        Layout(root);
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!text.gameObject.activeInHierarchy || string.IsNullOrWhiteSpace(text.text)) continue;
            text.ForceMeshUpdate();
            Check(text.fontSize >= 10f, name + " text smaller than 10: " + text.text);
        }
        var scrolls = new List<ScrollRect>();
        foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true))
        {
            if (!scroll.gameObject.activeInHierarchy || scroll.content == null) continue;
            scrolls.Add(scroll);
            Rect viewport = Bounds(root, scroll.viewport);
            Check(viewport.xMin >= root.rect.xMin - 1f && viewport.xMax <= root.rect.xMax + 1f, name + " viewport escapes panel width.");
            Check(viewport.yMin >= root.rect.yMin + 77f, name + " viewport overlaps pinned footer.");
            Check(scroll.content.rect.height >= scroll.viewport.rect.height - .5f, $"{name} content ({scroll.content.rect.height}) is shorter than its viewport ({scroll.viewport.rect.height}) at height={height}");
            if (scroll.verticalScrollbar != null)
            {
                Check(scroll.verticalScrollbar.navigation.mode == Navigation.Mode.None, name + " scrollbar must not capture flight navigation.");
                Check(Mathf.Abs(scroll.verticalScrollbar.size - Mathf.Clamp01(scroll.viewport.rect.height / scroll.content.rect.height)) < .02f,
                    $"{name} scrollbar thumb must indicate the visible content fraction: thumb={scroll.verticalScrollbar.size:F3} bar={scroll.verticalScrollbar.gameObject.name} viewport={scroll.viewport.rect.height:F1} vpBoundsView={((Bounds)typeof(ScrollRect).GetField("m_ViewBounds", All).GetValue(scroll)).size.y:F1} vpBoundsPrev={((Bounds)typeof(ScrollRect).GetField("m_PrevViewBounds", All).GetValue(scroll)).size.y:F1} contentRect={scroll.content.rect.height:F1} contentBounds={((Bounds)typeof(ScrollRect).GetField("m_ContentBounds", All).GetValue(scroll)).size.y:F0} deepest={Deepest(root, scroll.content)}");
            }
        }
        float[] positions = { 1f, .5f, 0f };
        string[] suffix = { "top", "middle", "bottom" };
        for (int i = 0; i < positions.Length; i++)
        {
            foreach (ScrollRect scroll in scrolls) scroll.verticalNormalizedPosition = positions[i];
            Layout(root);
            if (i == 2)
            {
                foreach (ScrollRect scroll in scrolls)
                {
                    Rect viewport = Bounds(root, scroll.viewport);
                    if (scroll.content.rect.height > scroll.viewport.rect.height + 1f)
                        Check(Mathf.Abs(Bounds(root, scroll.content).yMin - viewport.yMin) < 1f, name + " cannot reach scroll content bottom.");
                    foreach (Selectable control in scroll.content.GetComponentsInChildren<Selectable>(true))
                    {
                        if (!control.gameObject.activeInHierarchy || control is Scrollbar) continue;
                        Check(Bounds(root, (RectTransform)control.transform).yMin >= viewport.yMin - 1f,
                            name + " last control is clipped at bottom: " + control.name);
                    }
                    foreach (AvButton control in scroll.content.GetComponentsInChildren<AvButton>(true))
                    {
                        if (!control.gameObject.activeInHierarchy) continue;
                        Rect bounds = Bounds(root, (RectTransform)control.transform);
                        Check(bounds.yMin >= viewport.yMin - 1f, name + " last action is clipped at bottom: " + control.name);
                        Check(bounds.xMin >= viewport.xMin - 1f && bounds.xMax <= viewport.xMax + 1f,
                            name + " action escapes scroll gutter: " + control.name);
                    }
                }
            }
            Capture(root, height, name + "-" + (int)height + "-" + suffix[i] + ".png");
        }
    }

    private static Rect Bounds(RectTransform root, RectTransform element)
    {
        var corners = new Vector3[4]; element.GetWorldCorners(corners);
        Vector3 min = root.InverseTransformPoint(corners[0]), max = root.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>Lowest descendant of a scroll content, for thumb-mismatch diagnostics.</summary>
    private static string Deepest(RectTransform root, RectTransform content)
    {
        string name = content.name;
        float lowest = float.MaxValue;
        foreach (RectTransform child in content.GetComponentsInChildren<RectTransform>(true))
        {
            Rect bounds = Bounds(root, child);
            if (bounds.yMin < lowest)
            {
                lowest = bounds.yMin;
                name = child.name + "@" + bounds.yMin.ToString("F0") +
                       (child.gameObject.activeInHierarchy ? "" : "(off)");
            }
        }
        return name;
    }

    private static void Layout(RectTransform root)
    {
        Canvas.ForceUpdateCanvases();
        foreach (ScrollRect scroll in root.GetComponentsInChildren<ScrollRect>(true))
            if (scroll.gameObject.activeInHierarchy)
            {
                scroll.Rebuild(CanvasUpdate.PostLayout);
                typeof(ScrollRect).GetMethod("LateUpdate", All).Invoke(scroll, null);
            }
        Canvas.ForceUpdateCanvases();
    }

    private static void Capture(RectTransform root, float height, string file)
    {
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        var cameraObject = new GameObject("Capture", typeof(Camera));
        Camera camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = height * .5f;
        camera.transform.position = new Vector3(0, 0, -10);
        camera.backgroundColor = AvTheme.Ground; camera.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture(960, (int)height * 2, 24);
        camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        File.WriteAllBytes(Path.GetFullPath(file), image.EncodeToPNG());
        RenderTexture.active = null;
        Object.DestroyImmediate(image); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(target);
        captures++;
    }

    private static object Get(string field) => Screen.GetField(field, All).GetValue(null);
    private static object Get(object target, string field) => target.GetType().GetField(field, All).GetValue(target);
    private static object Call(string method, params object[] args) => Screen.GetMethod(method, All).Invoke(null, args);
    private static void Text(string field, string value) { if (Get(field) is TMP_Text text) text.text = value; }
    private static void Text(object target, string field, string value) { if (Get(target, field) is TMP_Text text) text.text = value; }
    private static void TrackError(string text, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception) return;
        // The production compositor disposes its temporary texture correctly in Play Mode.
        // Edit Mode rejects delayed Destroy; record this exact known harness limitation.
        if (text.StartsWith("Destroy may not be called from edit mode!", StringComparison.Ordinal) &&
            (trace.Contains("WingCommand.PilotPortrait.LoadLayers") || trace.Contains("WingCommand.PilotPortrait:LoadLayers"))) EditorWarnings.Add(text + "\n" + trace);
        else Errors.Add(text);
    }
    private static void Check(bool pass, string message) { assertions++; if (!pass) throw new Exception(message); }
}
#endif
