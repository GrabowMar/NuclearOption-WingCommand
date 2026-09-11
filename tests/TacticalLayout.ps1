# Exercise production reflow and page-switch methods without loading Unity.
$ErrorActionPreference = 'Stop'
$source = Get-Content "$PSScriptRoot/../Ui/WmcScreen.TacticalNavigation.cs" -Raw
$methods = foreach ($name in @('ToggleRosterExpanded', 'ReflowTactical', 'FitTacticalViewport', 'SetOrderPage', 'SetGeometryPage')) {
    $match = [regex]::Match($source, "(?ms)^        private static void $name\(.*?^        }")
    if (!$match.Success) { throw "Missing layout method: $name" }
    $match.Value
}
$boundary = @'
using System;
class Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x; this.y=y; } }
class Rect { public float x,y,width,height; public Rect(float x,float y,float w,float h) { this.x=x; this.y=y; width=w; height=h; } }
class GameObject { public bool Active; public void SetActive(bool b) { Active=b; } }
class RectTransform { public Vector2 anchoredPosition=new Vector2(0,0), sizeDelta=new Vector2(0,0); public GameObject gameObject=new GameObject(); }
class ScrollRect { public float verticalNormalizedPosition; public void StopMovement() {} }
class WingButton { public bool Latched; public void SetLatched(bool b) { Latched=b; } }
class WingCommandManager { public static WingCommandManager Instance=new WingCommandManager(); public int Cancellations; public void CancelMapOrder(bool notify) { Cancellations++; } }
static class Mathf { public static float Max(float a,float b) => Math.Max(a,b); }
public static class TacticalLayoutChecks {
    const int RosterRowsPerPage=3, ExpandedRosterRows=6;
    const float RowPitch=32, RowHeight=30, PanelWidth=446, Pad=14, StatusStripHeight=56, Space2=8, FlightGroupsHeight=96;
    static bool rosterExpanded;
    static int rosterPage, orderPage, geometryPage;
    static float nextRefresh, panelHeight=960, tacticalTop=-174, tacticalCommandsTop=-226, tacticalCollapsedHeight=700;
    static RectTransform flightGroupsRoot=new RectTransform();
    static RectTransform rosterArea=new RectTransform(), tacticalCommands=new RectTransform(), tacticalContent=new RectTransform(),
        tacticalViewport=new RectTransform(), tacticalScrollTrack=new RectTransform();
    static ScrollRect tacticalScroll=new ScrollRect();
    static RectTransform[] orderPages={new RectTransform(),new RectTransform(),new RectTransform()};
    static WingButton[] orderTabs={new WingButton(),new WingButton(),new WingButton()};
    static RectTransform[] geometryPages={new RectTransform(),new RectTransform()};
    static WingButton[] geometryTabs={new WingButton(),new WingButton()};
    static void CloseFlightGroupEditor() {}
    static object Wing() => null;
    static void RefreshTactical(object wing) {}
    static void Place(RectTransform target, Rect rect) { target.anchoredPosition=new Vector2(rect.x,rect.y); target.sizeDelta=new Vector2(rect.width,rect.height); }
'@
$checks = @'
    static void Check(bool pass,string message) { if (!pass) throw new Exception(message); }
    public static void Run() {
        FitTacticalViewport(); ReflowTactical();
        float viewportHeight=tacticalViewport.sizeDelta.y;
        Check(rosterArea.sizeDelta.y==96 && tacticalCommands.anchoredPosition.y==tacticalCommandsTop, "Default three rows");
        Check(!flightGroupsRoot.gameObject.Active, "Collapsed flight has no group controls");
        Check(tacticalTop-viewportHeight==-(panelHeight-Pad-StatusStripHeight-Space2), "Viewport ends above pinned footer");
        rosterPage=2; ToggleRosterExpanded();
        Check(rosterExpanded && rosterPage==1, "Expansion preserves first visible aircraft");
        Check(rosterArea.sizeDelta.y==192 && tacticalCommands.anchoredPosition.y==tacticalCommandsTop-96-FlightGroupsHeight, "Six rows push commands down without overlap");
        Check(tacticalContent.sizeDelta.y==796+FlightGroupsHeight && tacticalViewport.sizeDelta.y==viewportHeight, "Only scroll content grows");
        Check(flightGroupsRoot.gameObject.Active && flightGroupsRoot.anchoredPosition.y==tacticalCommandsTop-96, "Groups follow expanded aircraft rows");
        SetOrderPage(2); SetGeometryPage(1);
        for (int i=0;i<3;i++) Check(orderPages[i].gameObject.Active==(i==2) && orderTabs[i].Latched==(i==2), "Exactly one order page");
        Check(geometryPages[1].gameObject.Active && !geometryPages[0].gameObject.Active, "Manoeuvres page independent of order page");
        Check(WingCommandManager.Instance.Cancellations==1 && rosterPage==1, "Order pagination disarms map tool without changing roster page");
        ToggleRosterExpanded();
        Check(!rosterExpanded && rosterPage==2 && tacticalContent.sizeDelta.y==700, "Collapse restores compact layout");
        Check(!flightGroupsRoot.gameObject.Active, "Collapsing hides group controls again");
    }
}
'@
Add-Type -TypeDefinition ($boundary + ($methods -join "`n") + $checks) -IgnoreWarnings -WarningAction SilentlyContinue
[TacticalLayoutChecks]::Run()
Write-Output 'Tactical layout and pagination checks passed.'
